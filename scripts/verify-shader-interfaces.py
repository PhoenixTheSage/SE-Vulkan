"""Check reflected VS/PS linkage for captured material shader permutations."""
import argparse
from collections import defaultdict
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def reflected_value(element):
    """Compare serialized values, excluding XML indentation and sibling tails."""
    return (element.tag, tuple(sorted(element.attrib.items())),
            (element.text or "").strip(), tuple(reflected_value(child) for child in element))


def verify(inventory_path, report_path, output_path):
    inventory = {v["Id"]: v for v in json.loads(inventory_path.read_text(encoding="utf-8"))["Variants"]}
    report = json.loads(report_path.read_text(encoding="utf-8"))
    if report["Failed"] or report["Total"] != len(inventory):
        raise ValueError("Interface verification requires the complete validated catalog.")
    groups = defaultdict(dict)
    for result in report["Variants"]:
        variant = inventory[result["Id"]]
        if not variant["Source"].startswith("Geometry/Materials/"):
            continue
        group = (variant["Source"].rsplit("/", 1)[0], json.dumps(variant["Defines"], sort_keys=True))
        groups[group][variant["Profile"][:2]] = result
    failures = []
    pairs = 0
    shared_descriptors = 0
    for (source, _), stages in groups.items():
        if not {"vs", "ps"}.issubset(stages):
            failures.append({"Source": source, "Reason": "Missing captured material stage partner"})
            continue
        vertex = ET.parse(stages["vs"]["MetadataPath"]).getroot()
        pixel = ET.parse(stages["ps"]["MetadataPath"]).getroot()
        vertex_descriptors = {(d.findtext("Set"), d.findtext("Binding")): d for d in vertex.findall("./Descriptors/ShaderDescriptor")}
        for descriptor in pixel.findall("./Descriptors/ShaderDescriptor"):
            key = (descriptor.findtext("Set"), descriptor.findtext("Binding"))
            if key in vertex_descriptors:
                shared_descriptors += 1
                if reflected_value(descriptor) != reflected_value(vertex_descriptors[key]):
                    failures.append({"Source": source, "VertexId": stages["vs"]["Id"], "PixelId": stages["ps"]["Id"],
                                     "Reason": "Different resources/layouts reuse a descriptor across material stages", "Set": key[0], "Binding": key[1]})
        outputs = {int(v.findtext("Location")): v.findtext("Type") for v in vertex.findall("./Interface/ShaderInterfaceVariable") if v.findtext("Direction") == "Output"}
        for variable in pixel.findall("./Interface/ShaderInterfaceVariable"):
            if variable.findtext("Direction") != "Input":
                continue
            location = int(variable.findtext("Location"))
            if outputs.get(location) != variable.findtext("Type"):
                failures.append({"Source": source, "VertexId": stages["vs"]["Id"], "PixelId": stages["ps"]["Id"],
                                 "Location": location, "Input": variable.findtext("Name"), "Expected": variable.findtext("Type"), "Output": outputs.get(location)})
        pairs += 1
    result = {"PairsChecked": pairs, "SharedDescriptorsChecked": shared_descriptors, "Failures": failures,
              "Scope": "Material VS/PS reflected locations/types and full shared descriptor layouts; descriptor contents, raster state and visual acceptance remain separate."}
    output_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    if failures:
        raise ValueError(f"{len(failures)} material interface mismatches; see {output_path}")
    print(f"PASS: {pairs} material vertex/pixel pairs, {shared_descriptors} shared descriptors, zero interface/layout mismatches.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("inventory", "report", "output"):
        parser.add_argument(arg, type=Path)
    args = parser.parse_args()
    verify(args.inventory, args.report, args.output)
