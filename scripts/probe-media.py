"""Measure CPU media cadence without game/GPU workload; not live GUI acceptance."""
import argparse
import ctypes as c
import json
from pathlib import Path
import time

parser = argparse.ArgumentParser()
parser.add_argument("video", type=Path)
parser.add_argument("--seconds", type=float, default=5)
parser.add_argument("--warmup", type=float, default=1)
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent

class Frame(c.Structure):
    _fields_ = [(n, c.c_uint32) for n in ("size", "abi", "state", "width", "height", "flags")] + [(n, c.c_uint64) for n in ("sequence", "frames", "dropped", "conversion_us")] + [(n, c.c_int64) for n in ("position_us", "av_offset_us")] + [("pixels", c.c_void_p), ("error", c.c_char * 512)]

lib = c.CDLL(str(root / "Native/build/Release/se_vulkan_native.dll"))
lib.sevk_media_create.argtypes = [c.c_char_p, c.POINTER(c.c_void_p)]
lib.sevk_media_play.argtypes = [c.c_void_p, c.c_uint32, c.c_char_p, c.c_float]
lib.sevk_media_update.argtypes = [c.c_void_p, c.c_uint32, c.POINTER(Frame)]
lib.sevk_media_destroy.argtypes = [c.c_void_p]
media = c.c_void_p()
assert lib.sevk_media_create(str(root / "Assets/Media/win-x64").encode(), c.byref(media)) == 0
try:
    assert lib.sevk_media_play(media, 1, str(args.video.resolve()).encode(), 0) == 0
    first = None
    warm_started = None
    deadline = time.monotonic() + 15
    max_offset = 0
    while time.monotonic() < deadline:
        frame = Frame(size=c.sizeof(Frame), abi=1)
        assert lib.sevk_media_update(media, 1, c.byref(frame)) == 0
        if frame.state == 3:
            raise RuntimeError(frame.error.decode())
        if frame.frames and warm_started is None:
            warm_started = time.monotonic()
        if warm_started is not None and time.monotonic()-warm_started >= args.warmup and first is None:
            first = (time.monotonic(), frame.frames, frame.conversion_us, time.process_time(), frame.dropped)
            deadline = first[0] + args.seconds
        if first and time.monotonic()-first[0] > 1 and frame.flags & 2:
            max_offset = max(max_offset, abs(frame.av_offset_us))
        if frame.state == 2:
            break
        time.sleep(.001)
    assert first is not None, "No decoded frames"
    elapsed = time.monotonic() - first[0]
    count = frame.frames - first[1]
    result = {"video": str(args.video), "warmup_seconds": args.warmup, "seconds": elapsed, "frames": count, "fps": count / elapsed, "dropped": frame.dropped-first[4], "startup_dropped": first[4],
              "output_owner_cpu_us_per_frame": (frame.conversion_us-first[2])/max(1, count), "process_cpu_seconds": time.process_time()-first[3], "max_av_offset_us": max_offset, "audio_flags": frame.flags}
    print(json.dumps(result, indent=2))
finally:
    lib.sevk_media_destroy(media)
