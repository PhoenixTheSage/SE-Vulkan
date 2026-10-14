"""Small ELF64 dependency reader usable by Windows cross packaging."""
import struct

def dynamic_strings(data, wanted):
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
            if tag == wanted:
                start = strings[4] + value
                result.append(data[start:data.index(b"\0", start)].decode())
    return result

def needed(data):
    return dynamic_strings(data, 1)

def relocate_rpath(data):
    """Shorten existing build rpaths in place; preserve all ELF offsets."""
    output = bytearray(data)
    offset = struct.unpack_from("<Q", data, 40)[0]
    stride, count = struct.unpack_from("<HH", data, 58)
    sections = [struct.unpack_from("<IIQQQQIIQQ", data, offset + i*stride) for i in range(count)]
    for section in sections:
        if section[1] != 6:
            continue
        strings = sections[section[6]]
        for pos in range(section[4], section[4]+section[5], section[9]):
            tag, value = struct.unpack_from("<qQ", data, pos)
            if tag in (15, 29):
                start = strings[4] + value
                end = data.index(b"\0", start)
                if end-start < len(b"$ORIGIN"):
                    raise RuntimeError("ELF rpath is too short for in-place private relocation")
                output[start:end] = b"$ORIGIN" + b"\0" * (end-start-len(b"$ORIGIN"))
    return bytes(output)
