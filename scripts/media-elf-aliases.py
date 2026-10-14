"""Materialize SONAME/linker aliases when Windows cannot create ELF symlinks."""
from pathlib import Path
import shutil
import sys
from media_elf import dynamic_strings

directory = Path(sys.argv[1])
groups = {}
for path in directory.glob("*.so*"):
    if path.is_file() and path.read_bytes()[:4] == b"\x7fELF":
        groups.setdefault(path.name.split(".so")[0], []).append(path)
for stem, candidates in groups.items():
    source = max(candidates, key=lambda p: len(p.name))
    names = [stem + ".so"] + dynamic_strings(source.read_bytes(), 14)
    for name in names:
        if Path(name).name != name:
            raise RuntimeError("Invalid ELF SONAME")
        destination = directory / name
        if destination != source:
            shutil.copy2(source, destination)
