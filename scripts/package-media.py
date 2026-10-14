"""Bundle the actual loader dependency closure and record immutable build receipts."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
from media_elf import needed, relocate_rpath

p = argparse.ArgumentParser()
p.add_argument("prefix", type=Path)
p.add_argument("output", type=Path)
p.add_argument("--platform", required=True, choices=["win-x64", "linux-x64"])
p.add_argument("--build", required=True, type=Path)
a = p.parse_args()
prefix, output, build = a.prefix.resolve(), a.output.resolve(), a.build.resolve()
output.mkdir(parents=True, exist_ok=True)
legal = output / "Legal"
legal.mkdir(exist_ok=True)
sources = {
    "mpv": {"version": "0.41.0", "sha256": "ee21092a5ee427353392360929dc64645c54479aefdb5babc5cfbb5fad626209", "url": "https://codeload.github.com/mpv-player/mpv/tar.gz/refs/tags/v0.41.0"},
    "ffmpeg": {"version": "8.0.1", "sha256": "05ee0b03119b45c0bdb4df654b96802e909e0a752f72e4fe3794f487229e5a41", "url": "https://ffmpeg.org/releases/ffmpeg-8.0.1.tar.xz"},
    "libplacebo": {"version": "7.349.0", "sha256": "627e32439a0b3d2b90368ead7e919f470ee7446c87cc0f7841bbe319b23aa8b1", "url": "https://codeload.github.com/haasn/libplacebo/tar.gz/refs/tags/v7.349.0"},
}
if a.platform == "linux-x64":
    sources["libass"] = {"version": "0.17.3", "sha256": "eae425da50f0015c21f7b3a9c7262a910f0218af469e22e2931462fed3c50959", "url": "https://github.com/libass/libass/releases/download/0.17.3/libass-0.17.3.tar.xz"}
    sources["fast_float"] = {"version": "6.1.6", "sha256": "4458aae4b0eb55717968edda42987cabf5f7fc737aee8fede87a70035dba9ab0", "url": "https://codeload.github.com/fastfloat/fast_float/tar.gz/refs/tags/v6.1.6"}
for folder, patterns in (("mpv-0.41.0", ["Copyright", "LICENSE.LGPL"]), ("ffmpeg-8.0.1", ["COPYING.LGPLv2.1", "LICENSE.md"]), ("libplacebo-7.349.0", ["LICENSE"])):
    for name in patterns:
        shutil.copy2(build / folder / name, legal / (folder + "-" + name.replace("/", "-")))
if a.platform == "linux-x64":
    shutil.copy2(build / "libass-0.17.3" / "COPYING", legal / "libass-0.17.3-COPYING")
    shutil.copy2(build / "fast_float-6.1.6" / "LICENSE-MIT", legal / "fast_float-6.1.6-MIT")
    shutil.copy2(Path(__file__).resolve().parent.parent / "Native/third_party/mpv/ffmpeg-sysctl-header.patch", legal / "ffmpeg-sysctl-header.patch")

def run(*args):
    return subprocess.check_output(args, text=True, errors="replace")

receipts = []
if a.platform == "win-x64":
    # objdump resolves PE imports, not PATH guesses or all DLLs in a toolchain.
    mingw = Path(shutil.which("gcc")).resolve().parent
    directories = [prefix / "bin", prefix / "lib", mingw]
    pending = [next(path for directory in directories for path in directory.glob("*mpv*.dll"))]
    seen = set()
    windows_system = Path(os.environ["WINDIR"]) / "System32"
    while pending:
        library = pending.pop()
        if library.name.lower() in seen:
            continue
        seen.add(library.name.lower())
        shutil.copy2(library, output / library.name)
        for name in re.findall(r"DLL Name:\s*(\S+)", run("objdump", "-p", str(library))):
            dependency = next((directory / name for directory in directories if (directory / name).exists()), None)
            if dependency:
                pending.append(dependency)
            elif not (windows_system / name).exists() and not name.lower().startswith(("api-ms-", "ext-ms-")):
                raise RuntimeError(f"Unresolved PE dependency {name} from {library}")
        if library.is_relative_to(mingw):
            unix_path = run("cygpath", "-u", str(library)).strip()
            package = run("pacman", "-Qoq", unix_path).strip()
            if package not in {row["package"] for row in receipts}:
                receipt = run("pacman", "-Qi", package)
                receipts.append({"package": package, "receipt": receipt})
                for package_file in run("pacman", "-Qlq", package).splitlines():
                    if "/share/licenses/" in package_file and not package_file.endswith("/"):
                        license_path = Path(run("cygpath", "-w", package_file).strip())
                        if license_path.is_file():
                            destination = legal / package / license_path.name
                            destination.parent.mkdir(exist_ok=True)
                            shutil.copy2(license_path, destination)
    # Preserve the exact auxiliary packages needed to reproduce this build.
    cache = Path(run("cygpath", "-w", "/var/cache/pacman/pkg").strip())
    receipts.append({"package": "build-environment", "installed": run("pacman", "-Q"),
                     "archives": [{"name": f.name, "sha256": hashlib.sha256(f.read_bytes()).hexdigest()} for f in sorted(cache.glob("*.pkg.tar.zst"))]})
elif os.name == "nt":
    # Cross-produced libraries deliberately use DT_RPATH=$ORIGIN, so the
    # private directory also resolves the transitive RPM dependency closure.
    root = Path(__file__).resolve().parent.parent
    sysroot = root / "Native/build-media/linux-sysroot"
    receipts = json.loads((root / "scripts/media-linux-rpms.json").read_text())
    directories = [prefix / "lib", sysroot / "usr/lib64", sysroot / "usr/lib64/pulseaudio", sysroot / "lib64"]
    pending = [prefix / "lib/libmpv.so.2"]
    seen = set()
    system = re.compile(r"^(ld-linux|lib(c|m|dl|pthread|rt|resolv|util)\.so)")
    while pending:
        library = pending.pop()
        if library.name in seen:
            continue
        seen.add(library.name)
        data = library.read_bytes()
        if data[:6] != b"\x7fELF\x02\x01":
            raise RuntimeError(f"Invalid ELF/symlink placeholder: {library}")
        (output / library.name).write_bytes(relocate_rpath(data))
        for name in needed(data):
            if not system.match(name):
                dependency = next((directory / name for directory in directories if (directory / name).is_file()), None)
                if dependency is None:
                    raise RuntimeError(f"Unresolved ELF dependency {name} from {library}")
                pending.append(dependency)
        versions = [tuple(map(int, v.split(b"."))) for v in re.findall(rb"GLIBC_(\d+\.\d+)", data)]
        if versions and max(versions) > (2, 34):
            raise RuntimeError(f"{library.name} exceeds glibc 2.34: {max(versions)}")
    for name in ("licenses", "doc"):
        directory = sysroot / "usr/share" / name
        if directory.exists():
            shutil.copytree(directory, legal / "AlmaLinux9" / name, dirs_exist_ok=True)
else:
    os.environ["LD_LIBRARY_PATH"] = str(prefix / "lib")
    pending = [prefix / "lib" / "libmpv.so.2"]
    seen = set()
    # These are the platform ABI, not part of the private media runtime.
    system = re.compile(r"^(ld-linux|lib(c|m|dl|pthread|rt|resolv|util)\.so)")
    while pending:
        library = pending.pop()
        if library.name in seen:
            continue
        seen.add(library.name)
        shutil.copy2(library, output / library.name, follow_symlinks=True)
        dependencies = run("ldd", str(library))
        if "not found" in dependencies:
            raise RuntimeError(dependencies)
        for name, path in re.findall(r"\s*(\S+) => (/\S+) ", dependencies):
            if not system.match(name):
                pending.append(Path(path))
        if not library.is_relative_to(prefix):
            receipts.append({"package": run("rpm", "-qf", str(library)).strip(), "source": run("rpm", "-qf", "--qf", "%{SOURCERPM}", str(library)).strip()})
    for library in output.glob("*.so*"):
        subprocess.check_call(["patchelf", "--set-rpath", "$ORIGIN", str(library)])
        versions = [tuple(map(int, v.split("."))) for v in re.findall(r"GLIBC_(\d+\.\d+)", run("readelf", "--version-info", str(library)))]
        if versions and max(versions) > (2, 34):
            raise RuntimeError(f"{library.name} exceeds glibc 2.34: {max(versions)}")
    for folder in (Path("/usr/share/licenses"), Path("/usr/share/doc")):
        for receipt in receipts:
            package = run("rpm", "-q", "--qf", "%{NAME}", receipt["package"]).strip()
            if (folder / package).exists():
                shutil.copytree(folder / package, legal / package, dirs_exist_ok=True)

for name in ("ffmpeg", "mpv", "placebo"):
    logs = list(build.glob(name + "*-build/meson-logs/meson-log.txt"))
    for log in logs:
        shutil.copy2(log, legal / (name + "-build.log"))
shutil.copy2(build / "ffmpeg-build" / "config.h", legal / "ffmpeg-config.h")
shutil.copy2(build / "mpv-build" / "config.h", legal / "mpv-config.h")
manifest = {"platform": a.platform, "cross_compiled": a.platform == "linux-x64" and os.name == "nt", "sources": sources, "configuration": "LGPL; software video; WASAPI or PulseAudio/ALSA; file inputs",
            "dependencies": receipts, "files": [{"path": str(f.relative_to(output)).replace("\\", "/"), "sha256": hashlib.sha256(f.read_bytes()).hexdigest(), "bytes": f.stat().st_size} for f in sorted(output.rglob("*")) if f.is_file() and f.name != "manifest.json"]}
(output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(f"Packaged {a.platform}: {len(seen)} runtime libraries -> {output}")
