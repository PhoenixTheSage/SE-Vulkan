"""Read-only BC7 atlas reference decode, independent of the GPU sampler."""
import struct
import sys
import io
from PIL import Image

source = sys.argv[1]
if len(sys.argv) > 3:
    level = int(sys.argv[3])
    data = open(source, "rb").read()
    header = bytearray(data[:148])
    if data[84:88] != b"DX10" or struct.unpack_from("<I", data, 128)[0] not in (98, 99):
        raise ValueError("Mip reference decode expects the installed BC7 DX10 atlas")
    width, height = struct.unpack_from("<I", data, 16)[0], struct.unpack_from("<I", data, 12)[0]
    offset = 148
    for _ in range(level):
        offset += ((width + 3) // 4) * ((height + 3) // 4) * 16
        width, height = max(1, width // 2), max(1, height // 2)
    size = ((width + 3) // 4) * ((height + 3) // 4) * 16
    struct.pack_into("<I", header, 12, height)
    struct.pack_into("<I", header, 16, width)
    struct.pack_into("<I", header, 20, size)
    struct.pack_into("<I", header, 28, 1)
    struct.pack_into("<I", header, 108, 0x1000)
    source = io.BytesIO(header + data[offset:offset + size])

with Image.open(source) as atlas:
    rgba = atlas.convert("RGBA")
    with open(sys.argv[2], "wb") as output:
        output.write(struct.pack("<II", *rgba.size))
        output.write(rgba.tobytes())
