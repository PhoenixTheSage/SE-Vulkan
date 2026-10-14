"""Read the warmed capture window, retaining diagnostics and marker coverage.

This summary is diagnostic evidence, not an uninstrumented FPS benchmark.
"""
import argparse
import bisect
from collections import defaultdict
import json
import pathlib
import sqlite3

parser = argparse.ArgumentParser()
parser.add_argument("run", type=pathlib.Path)
args = parser.parse_args()
with sqlite3.connect(args.run / "nsight.sqlite") as db:
    db.row_factory = sqlite3.Row
    tables = {r[0] for r in db.execute("select name from sqlite_master where type='table'")}
    required = {"StringIds", "ANALYSIS_DETAILS", "DIAGNOSTIC_EVENT", "VULKAN_API", "VULKAN_WORKLOAD"}
    if not required <= tables:
        raise RuntimeError(f"Missing trace tables: {sorted(required - tables)}")
    duration = db.execute("select duration from ANALYSIS_DETAILS").fetchone()[0]

    def summary(table, field):
        if table not in tables:
            return []
        query = f"""select s.value as Name,count(*) as Samples,
            avg(v.end-v.start)/1e6 as MeanMs,sum(v.end-v.start)/1e6 as TotalMs,
            min(v.end-v.start)/1e6 as MinMs,max(v.end-v.start)/1e6 as MaxMs
            from {table} v join StringIds s on s.id=v.{field}
            where v.start>=0 and v.end<=? and v.{field} is not null
            group by s.value order by TotalMs desc"""
        return [dict(r) for r in db.execute(query, (duration,))]

    api = summary("VULKAN_API", "nameId")
    cpu_markers = summary("VULKAN_DEBUG_API", "textId")
    gpu_markers = summary("VULKAN_WORKLOAD", "textId")
    diagnostics = [dict(r) for r in db.execute("select * from DIAGNOSTIC_EVENT")]
    cpu_counts = {r["Name"]: r["Samples"] for r in cpu_markers}
    gpu_counts = {r["Name"]: r["Samples"] for r in gpu_markers}
    coverage = [{"Name": name, "GpuSamples": gpu_counts.get(name, 0), "CpuSamples": cpu_counts.get(name, 0)}
                for name in sorted(cpu_counts.keys() | gpu_counts.keys())]
    settings = json.loads((args.run / "nsight-launch.json").read_text(encoding="utf-8-sig"))
    batch_mode = settings["GpuWorkload"] == "batch"
    batch_overlap = None
    if batch_mode:
        # Correlate the recorded batch with its host submit, then the next
        # present on that context/thread before another submit. Do not infer
        # full GPU occupancy from these intervals: events may be dropped and
        # a batch range can contain waits.
        paired = []
        unmatched = 0
        submit_lookup = defaultdict(list)
        thread_calls = defaultdict(list)
        for call in db.execute("""select a.*,s.value as apiName from VULKAN_API a
                join StringIds s on s.id=a.nameId
                where a.start>=0 and a.end<=?
                and s.value in ('vkQueuePresentKHR','vkQueueSubmit2')
                order by a.start""", (duration,)):
            thread_calls[(call["contextId"], call["globalTid"])].append(call)
            if call["apiName"] == "vkQueueSubmit2":
                submit_lookup[(call["contextId"], call["correlationId"])].append(call)
        thread_starts = {key: [call["start"] for call in calls]
                         for key, calls in thread_calls.items()}
        for workload in db.execute("""select * from VULKAN_WORKLOAD
                where start>=0 and end<=? and textId is null""", (duration,)):
            submits = submit_lookup[(workload["contextId"], workload["correlationId"])]
            if len(submits) != 1:
                unmatched += 1
                continue
            submit = submits[0]
            thread_key = (submit["contextId"], submit["globalTid"])
            position = bisect.bisect_left(thread_starts[thread_key], submit["end"])
            calls = thread_calls[thread_key]
            next_call = calls[position] if position < len(calls) else None
            if next_call is None or next_call["apiName"] != "vkQueuePresentKHR":
                unmatched += 1
                continue
            paired.append({"GpuRangeMs": (workload["end"]-workload["start"])/1e6,
                           "PresentCallMs": (next_call["end"]-next_call["start"])/1e6,
                           "PresentReturnBeforeGpuEnd": next_call["end"] < workload["end"]})
        batch_overlap = {
            "MatchedRecordedFrameBatches": len(paired),
            "UnpairedRecordedBatches": unmatched,
            "PresentReturnedBeforeGpuEnd": sum(r["PresentReturnBeforeGpuEnd"] for r in paired),
            "MeanGpuRangeMs": sum(r["GpuRangeMs"] for r in paired)/len(paired) if paired else None,
            "MeanPresentCallMs": sum(r["PresentCallMs"] for r in paired)/len(paired) if paired else None,
            "Scope": "Historical recorded batch/host-submit/next-present pairs only; event loss prevents full occupancy or later presentation-cause claims."}
    dropped = any("not all vulkan events" in r["text"].lower() for r in diagnostics)
    incomplete = dropped or not coverage or any(r["GpuSamples"] != r["CpuSamples"] for r in coverage)
    output = {"Instrumented": True, "CaptureWindowSeconds": duration / 1e9,
              "GpuWorkload": settings["GpuWorkload"], "StartupExcluded": True,
              "PassAttributionComplete": not batch_mode and not incomplete,
              "Diagnostics": diagnostics, "ApiSummary": api, "CpuMarkers": cpu_markers,
              "GpuMarkers": gpu_markers, "MarkerCoverage": coverage,
              "GpuWorkloadSummary": summary("VULKAN_WORKLOAD", "nameId"),
              "RecordedBatchPresentationOverlap": batch_overlap,
              "Scope": "Recorded events in the warmed capture window only. API sums may span threads; GPU ranges can overlap/include waits. Missing events prevent complete pass attribution."}
    destination = args.run / "nsight-summary.json"
    destination.write_text(json.dumps(output, indent=2), encoding="utf-8")
    print(destination)
