"""Read-only CPU capture summary; never equate scheduling stacks with CPU samples."""
import argparse,json,sqlite3,pathlib
parser=argparse.ArgumentParser();parser.add_argument("run",type=pathlib.Path);parser.add_argument("--pid",type=int,required=True);args=parser.parse_args()
with sqlite3.connect("file:"+str((args.run/"nsight.sqlite").resolve()).replace("\\","/")+"?mode=ro",uri=True) as db:
 db.row_factory=sqlite3.Row
 if db.execute("pragma quick_check").fetchone()[0]!="ok":raise RuntimeError("capture database failed integrity check")
 duration=db.execute("select duration from ANALYSIS_DETAILS").fetchone()[0]
 global_pid=db.execute("select globalPid from PROCESSES where pid=?",(args.pid,)).fetchone()[0]
 values=(global_pid,global_pid+16777215,duration)
 threads=[dict(r) for r in db.execute("""select e.globalTid,e.globalTid&16777215 Tid,
 (select s.value from ThreadNames t join StringIds s on s.id=t.nameId where t.globalTid=e.globalTid limit 1) Name,
 sum(e.cpuCycles) CpuSamples,sum(case when e.cpuCycles=0 then 1 else 0 end) SchedulingStacks
 from COMPOSITE_EVENTS e where e.globalTid between ? and ? and e.start between 0 and ?
 group by e.globalTid order by CpuSamples desc""",values)]
 render=next(t for t in threads if t["Name"]=="Render thread")
 counts=[dict(r) for r in db.execute("""select m.value Module,sum(e.cpuCycles) CpuSamples,
 sum(case when c.unresolved=1 then e.cpuCycles else 0 end) UnresolvedSamples
 from COMPOSITE_EVENTS e join SAMPLING_CALLCHAINS c on c.id=e.id and c.stackDepth=0
 left join StringIds m on m.id=c.module where e.globalTid=? and e.start between 0 and ? and e.cpuCycles=1
 group by c.module order by CpuSamples desc""",(render["globalTid"],duration))]
 for row in counts:row["RenderLeafPercent"]=row["CpuSamples"]*100/render["CpuSamples"]
 leaves=[dict(r) for r in db.execute("""select s.value Symbol,m.value Module,c.unresolved Unresolved,count(*) CpuSamples
 from COMPOSITE_EVENTS e join SAMPLING_CALLCHAINS c on c.id=e.id and c.stackDepth=0
 left join StringIds s on s.id=c.symbol left join StringIds m on m.id=c.module
 where e.globalTid=? and e.start between 0 and ? and e.cpuCycles=1
 group by c.symbol,c.module,c.unresolved order by CpuSamples desc limit 40""",(render["globalTid"],duration))]
 native=[dict(r) for r in db.execute("""select s.value Symbol,c.unresolved Unresolved,count(distinct e.id) CpuSamples
 from COMPOSITE_EVENTS e join SAMPLING_CALLCHAINS c on c.id=e.id
 join StringIds m on m.id=c.module left join StringIds s on s.id=c.symbol
 where e.globalTid=? and e.start between 0 and ? and e.cpuCycles=1 and m.value like '%se_vulkan_native%'
 group by c.symbol,c.unresolved order by CpuSamples desc limit 40""",(render["globalTid"],duration))]
 result=dict(scope="Instrumented CPU-only 10 second private Pertam sample; periodic cpuCycles=1 only. Module/IP attribution; no resolved-function claim or FPS proof.",source="https://docs.nvidia.com/nsight-systems/AnalysisGuide/index.html#cpucycles-attribution",durationSeconds=duration/1e9,pid=args.pid,integrity="ok",threads=threads,renderLeafModules=counts,diagnostics=[dict(r) for r in db.execute("select * from DIAGNOSTIC_EVENT")],launch=json.loads((args.run/"launch.json").read_text(encoding="utf-8-sig")),functionAttribution=False)
 result.update(renderLeafSymbols=leaves,nativeInclusiveSymbols=native,symbolScope="Unresolved=1 rows identify instruction addresses only; named resolved native rows overlap and cannot be summed. Includes sampled active CPU only, excluding scheduling stacks.")
output=args.run/"nsight-cpu-summary.json";output.write_text(json.dumps(result,indent=2),encoding="utf-8");print(output);print("Render periodic samples",render["CpuSamples"],"scheduling stacks",render["SchedulingStacks"]);print([(r["Module"],round(r["RenderLeafPercent"],2)) for r in counts[:6]])
