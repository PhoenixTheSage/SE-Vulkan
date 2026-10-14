"""Inspect packaged PE/ELF architecture, C ABI exports and immutable local hashes."""
import hashlib
import json
import re
import struct
from pathlib import Path

root = Path(__file__).resolve().parent.parent
required = set(re.findall(r"SEVK_CALL (sevk_\w+)\(", "\n".join((root / "Native/include" / name).read_text() for name in ("se_vulkan.h", "se_media.h"))))


def elf(data):
    assert data[:6] == b"\x7fELF\x02\x01", "Expected ELF64 little endian"
    assert struct.unpack_from("<H", data, 18)[0] == 62, "Expected x86-64 ELF"
    section_offset = struct.unpack_from("<Q", data, 40)[0]
    stride, count = struct.unpack_from("<HH", data, 58)
    sections = [struct.unpack_from("<IIQQQQIIQQ", data, section_offset + i * stride) for i in range(count)]
    exports = set()
    for section in sections:
        if section[1] != 11:
            continue
        strings = sections[section[6]]
        for offset in range(section[4], section[4] + section[5], section[9]):
            name, info, other, index, _, _ = struct.unpack_from("<IBBHQQ", data, offset)
            if info >> 4 and index and other & 3 == 0:
                start = strings[4] + name
                exports.add(data[start:data.index(b"\0", start)].decode())
    return exports


def pe(data):
    assert data[:2] == b"MZ", "Expected PE"
    offset = struct.unpack_from("<I", data, 60)[0]
    assert data[offset:offset + 4] == b"PE\0\0"
    machine, count = struct.unpack_from("<HH", data, offset + 4)
    assert machine == 0x8664, "Expected x64 PE"
    optional = offset + 24
    assert struct.unpack_from("<H", data, optional)[0] == 0x20B, "Expected PE32+"
    size = struct.unpack_from("<H", data, offset + 20)[0]
    sections = [struct.unpack_from("<IIII", data, optional + size + i * 40 + 8) for i in range(count)]

    def file_offset(rva):
        for virtual_size, virtual_address, raw_size, raw_address in sections:
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                return raw_address + rva - virtual_address
        raise AssertionError("Export RVA outside PE sections")

    export = file_offset(struct.unpack_from("<I", data, optional + 112)[0])
    names_count = struct.unpack_from("<I", data, export + 24)[0]
    names = file_offset(struct.unpack_from("<I", data, export + 32)[0])
    result = set()
    for i in range(names_count):
        start = file_offset(struct.unpack_from("<I", data, names + i * 4)[0])
        result.add(data[start:data.index(b"\0", start)].decode())
    return result


assets = []
for relative, kind in (("Assets/Native/win-x64/se_vulkan_native.dll", "PE"),
                       ("Assets/Native/linux-x64/libse_vulkan_native.so", "ELF"),
                       ("Assets/Tools/linux-x64/bin/dxc", "ELF-tool"),
                       ("Assets/Tools/linux-x64/bin/spirv-val", "ELF-tool")):
    data = (root / relative).read_bytes()
    exports = pe(data) if kind == "PE" else elf(data)
    if not kind.endswith("tool"):
        assert required <= exports, f"Missing ABI exports in {relative}: {required - exports}"
    glibc = sorted(set(value.decode() for value in re.findall(rb"GLIBC_[0-9.]+", data)))
    assets.append(dict(path=relative, sha256=hashlib.sha256(data).hexdigest().upper(), bytes=len(data),
                       format=kind, architecture="x86-64", abi_exports=sorted(required & exports), glibc=glibc))
report = dict(abi=1, required_exports=len(required), assets=assets,
              linux_execution="Pending: Windows host has no WSL/Linux installation.")
output = root / "TestResults/Native-FinalAssets.json"
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps(report, indent=2) + "\n")
print(f"PASS: x64 PE/ELF, {len(required)} native C ABI exports on both platforms, Linux tool ELF packaging. {output}")
