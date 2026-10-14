"""Prepare a checksum-locked AlmaLinux 9 sysroot for Windows cross builds.

Run with: uv run --with rpmfile --with zstandard python scripts/prepare-media-linux.py
Only x86-64 public development headers and the ELF dependency closure are used.
No RPM scripts execute and no host/system files are installed.
"""
import gzip
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import struct
import urllib.request
import xml.etree.ElementTree as ET
import rpmfile

root = Path(__file__).resolve().parent.parent
cache = root / "Native/build-media/linux-rpms"
sysroot = root / "Native/build-media/linux-sysroot"
cache.mkdir(parents=True, exist_ok=True)
sysroot.mkdir(parents=True, exist_ok=True)
lockfile = root / "scripts/media-linux-rpms.json"
lock = json.loads(lockfile.read_text()) if lockfile.exists() else {}
parser = argparse.ArgumentParser()
parser.add_argument("--refresh-lock", action="store_true", help="Resolve a new dependency lock from AlmaLinux repositories")
refresh = parser.parse_args().refresh_lock
if not lock and not refresh:
    raise RuntimeError("Dependency lock missing; use --refresh-lock to resolve it explicitly")
ns = {"r": "http://linux.duke.edu/metadata/repo", "p": "http://linux.duke.edu/metadata/common", "rpm": "http://linux.duke.edu/metadata/rpm"}
packages, providers = {}, {}
indexfile = cache / "index.json"
index = json.loads(indexfile.read_text()) if indexfile.exists() else {}

def fetch(url, path, checksum=None):
    if not path.exists():
        urllib.request.urlretrieve(url, path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if checksum and digest != checksum:
        raise RuntimeError(f"Checksum mismatch: {path.name}")
    return digest

if not refresh:
    # Normal rebuilds use only the committed URLs and hashes. Repository
    # updates must never silently replace a locked runtime dependency.
    packages = {item["name"]: item for item in lock.values()}
    providers = {name: item for item in lock.values() for name in item.get("provides", [])}
else:
    for repo in ("BaseOS", "AppStream", "CRB"):
        base = f"https://repo.almalinux.org/almalinux/9/{repo}/x86_64/os/"
        metadata = ET.fromstring(urllib.request.urlopen(base + "repodata/repomd.xml").read())
        entry = next(e for e in metadata.findall("r:data", ns) if e.attrib["type"] == "primary")
        href, checksum = entry.find("r:location", ns).attrib["href"], entry.find("r:checksum", ns).text
        archive = cache / (repo + "-" + Path(href).name)
        fetch(base + href, archive, checksum)
        with gzip.open(archive, "rb") as source:
            for event, element in ET.iterparse(source):
                if element.tag != "{" + ns["p"] + "}package":
                    continue
                if element.find("p:arch", ns).text != "x86_64":
                    element.clear(); continue
                name = element.find("p:name", ns).text
                location = element.find("p:location", ns).attrib["href"]
                fmt = element.find("p:format", ns)
                package = {"name": name, "url": base + location, "sha256": element.find("p:checksum", ns).text,
                           "source_rpm": fmt.find("rpm:sourcerpm", ns).text, "license": fmt.find("rpm:license", ns).text}
                packages[name] = package
                for provide in fmt.findall("rpm:provides/rpm:entry", ns):
                    match = re.match(r"([^()]+\.so(?:\.\d+)*)", provide.attrib["name"])
                    if match:
                        providers[match[1]] = package
                    if provide.attrib["name"].startswith("pkgconfig("):
                        providers[provide.attrib["name"]] = package
                element.clear()
    indexfile.write_text(json.dumps({"version": 2, "packages": packages, "providers": providers}, indent=2))
for item in lock.values():
    packages[item["name"]] = item
    for name in item.get("provides", []):
        providers[name] = item

symlinks = {}
installed = set()
def install(package):
    name = package["name"]
    if name in installed:
        return
    print("Extract", name, flush=True)
    archive = cache / Path(package["url"]).name
    fetch(package["url"], archive, package["sha256"])
    with rpmfile.open(str(archive)) as rpm:
        development = [name.decode() for name in rpm.headers.get("requirename", []) if name.startswith(b"pkgconfig(") or name.endswith(b"-devel")]
        for member in rpm.getmembers():
            relative = Path(member.name.removeprefix("./"))
            destination = (sysroot / relative).resolve()
            if not destination.is_relative_to(sysroot.resolve()):
                raise RuntimeError("RPM member escapes sysroot")
            if member.isdir:
                destination.mkdir(parents=True, exist_ok=True)
            else:
                data = rpm.extractfile(member).read()
                destination.parent.mkdir(parents=True, exist_ok=True)
                if member.issymlink:
                    target = data.decode()
                    symlinks[destination] = (sysroot / target.lstrip("/")) if target.startswith("/") else destination.parent / target
                else:
                    destination.write_bytes(data)
    installed.add(name)
    lock[name] = {**package, "provides": [lib for lib, p in providers.items() if p["name"] == name]}
    for required in development:
        if required.startswith("glibc"):
            continue
        dependency = providers.get(required) or packages.get(required)
        if dependency:
            install(dependency)

def needed(data):
    if data[:6] != b"\x7fELF\x02\x01":
        return []
    offset = struct.unpack_from("<Q", data, 40)[0]
    stride, count = struct.unpack_from("<HH", data, 58)
    sections = [struct.unpack_from("<IIQQQQIIQQ", data, offset + i*stride) for i in range(count)]
    result = []
    for section in sections:
        if section[1] != 6:
            continue
        strings = sections[section[6]]
        for pos in range(section[4], section[4]+section[5], section[9]):
            tag, value = struct.unpack_from("<qQ", data, pos)
            if tag == 1:
                start = strings[4] + value
                result.append(data[start:data.index(b"\0", start)].decode())
    return result

for name in ("alsa-lib-devel", "pulseaudio-libs-devel", "freetype-devel", "fribidi-devel", "harfbuzz-devel", "zlib-devel"):
    install(packages[name])
for name in ("alsa-lib", "pulseaudio-libs", "freetype", "fribidi", "harfbuzz", "zlib"):
    install(packages[name])
if "libgcc" in lock:
    install(packages["libgcc"])
system = re.compile(r"^(ld-linux|lib(c|m|dl|pthread|rt|resolv|util)\.so)")
while True:
    missing = set()
    for path in sysroot.rglob("*.so*"):
        if path.is_file():
            missing.update(name for name in needed(path.read_bytes()) if not system.match(name) and not any(p.name == name for p in sysroot.rglob(name)))
    pending = {providers[name]["name"] for name in missing if providers[name]["name"] not in installed}
    if not pending:
        break
    for name in sorted(pending):
        install(packages[name])
for _ in range(8):
    for destination, target in symlinks.items():
        if target.is_file() and destination != target:
            destination.write_bytes(target.read_bytes())
for path in sysroot.rglob("*.pc"):
    text = path.read_text()
    pcroot = os.environ.get("SEVK_MEDIA_SYSROOT_ALIAS", str(sysroot)).replace("\\", "/")
    # Keep private Linux headers/libraries out of native Windows pkg-config.
    text = re.sub(r"(?<![A-Za-z0-9/])/usr/", lambda _: pcroot + "/usr/", text)
    text = text.replace("prefix=/usr\n", "prefix=" + pcroot + "/usr\n")
    path.write_text(text)
if refresh:
    lockfile.write_text(json.dumps(lock, indent=2) + "\n")
print(f"Prepared {len(installed)} checksum-locked RPMs at {sysroot}")
