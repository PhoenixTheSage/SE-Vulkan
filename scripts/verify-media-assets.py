"""Verify packaged hashes, native architecture and Linux loader closure/baseline."""
import hashlib
import argparse
import json
from pathlib import Path
import re
import struct
from media_elf import dynamic_strings, needed

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--platform', choices=['win-x64', 'linux-x64'])
selection = parser.parse_args().platform
results = []
for platform in ([selection] if selection else ("win-x64", "linux-x64")):
    directory = root / "Assets/Media" / platform
    manifest = json.loads((directory / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["sources"]["mpv"]["version"] == "0.41.0"
    assert manifest["sources"]["ffmpeg"]["version"] == "8.0.1"
    mpv_config = (directory / 'Legal/mpv-config.h').read_text()
    ffmpeg_config = (directory / 'Legal/ffmpeg-config.h').read_text()
    for flag in ('HAVE_GPL', 'HAVE_LUA', 'HAVE_JAVASCRIPT', 'HAVE_CPLUGINS', 'HAVE_VULKAN'):
        assert re.search(r'^#define '+flag+r' 0$', mpv_config, re.M), f'Unexpected mpv feature: {flag}'
    for flag in ('CONFIG_GPL', 'CONFIG_NONFREE', 'CONFIG_VERSION3', 'CONFIG_D3D11VA', 'CONFIG_DXVA2', 'CONFIG_VULKAN'):
        assert re.search(r'^#define '+flag+r' 0$', ffmpeg_config, re.M), f'Unexpected FFmpeg feature: {flag}'
    for row in manifest["files"]:
        path = (directory / row["path"]).resolve()
        assert path.is_relative_to(directory.resolve()), "Manifest path escapes package"
        data = path.read_bytes()
        assert hashlib.sha256(data).hexdigest() == row["sha256"], f"Hash mismatch: {path}"
        if path.suffix == ".dll":
            pe = struct.unpack_from("<I", data, 60)[0]
            assert data[:2] == b"MZ" and data[pe:pe+4] == b"PE\0\0" and struct.unpack_from("<H", data, pe+4)[0] == 0x8664, f"Not PE x64: {path}"
        if data[:4] == b"\x7fELF":
            assert data[:6] == b"\x7fELF\x02\x01" and struct.unpack_from("<H", data, 18)[0] == 62, f"Not ELF x64: {path}"
            versions = [tuple(map(int, v.split(b"."))) for v in re.findall(rb"GLIBC_(\d+\.\d+)", data)]
            assert not versions or max(versions) <= (2, 34), f"glibc >2.34 in {path.name}"
            for dependency in needed(data):
                if not re.match(r"^(ld-linux|lib(c|m|dl|pthread|rt|resolv|util)\.so)", dependency):
                    assert (directory / dependency).is_file(), f"Unbundled {dependency} in {path.name}"
    if platform == "linux-x64":
        entry = (directory / "libmpv.so.2").read_bytes()
        rpaths = dynamic_strings(entry, 15) + dynamic_strings(entry, 29)
        assert any("$ORIGIN" in value for value in rpaths), "libmpv must resolve its private loader directory"
        # Transitive RPM libraries have system rpaths. The cross build uses
        # inherited DT_RPATH so they also resolve the bundled closure.
        if manifest.get("cross_compiled"):
            assert any("$ORIGIN" in value for value in dynamic_strings(entry, 15)), "Cross bundle requires inherited DT_RPATH"
            assert rpaths == ["$ORIGIN"], "Cross bundle contains build-host loader paths"
    results.append({"platform": platform, "files": len(manifest["files"]), "status": "verified", "runtime_acceptance": "separate"})
    print(f"PASS {platform}: hashes, x64, loader dependencies and platform baseline")
(root / "TestResults").mkdir(exist_ok=True)
(root / "TestResults/MediaAssets.json").write_text(json.dumps(results, indent=2) + "\n")
