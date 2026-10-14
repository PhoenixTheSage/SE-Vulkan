"""Package a validated, installed-content shader catalog; never copies game HLSL."""
import argparse
import gzip
import hashlib
import json
import shutil
from collections import Counter, defaultdict
from pathlib import Path
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def package(inventory_path, report_path, destination, summary_path):
    inventory = json.loads(inventory_path.read_text(encoding="utf-8"))
    report = json.loads(report_path.read_text(encoding="utf-8"))
    variants = {v["Id"]: v for v in inventory["Variants"]}
    results = {v["Id"]: v for v in report["Variants"]}
    if set(variants) != set(results) or report["Failed"] or any(v["Compilation"] != "Validated" for v in results.values()):
        raise ValueError("Packaging requires every captured variant to compile and validate.")
    if any(f["Role"] == "UnscheduledHlsl" for f in inventory["Files"]):
        raise ValueError("Unexplained shader entrypoint files remain in the inventory.")
    root = ET.Element("ShaderSuite", Schema="1", Api="1.4", AssemblySha256=inventory["GameAssemblySha256"],
                      Total=str(len(results)), Validated=str(len(results)), Integrated="0", Verified="0")
    files_node = ET.SubElement(root, "Sources")
    for source in inventory["Files"]:
        ET.SubElement(files_node, "Source", Path=source["Path"], Sha256=source["Sha256"], Role=source["Role"], Reason=source.get("AuditReason") or "")
    modules_node = ET.SubElement(root, "Modules")
    variant_node = ET.SubElement(root, "Variants")
    unique_modules = {}
    families = defaultdict(lambda: {"Variants": 0, "Validated": 0, "Integrated": 0, "VisuallyVerified": 0})
    source_coverage = defaultdict(lambda: {"Variants": 0, "Validated": 0, "Integrated": 0, "VisuallyVerified": 0})
    destination.mkdir(parents=True, exist_ok=True)
    module_dir = destination / "Modules"
    module_dir.mkdir(exist_ok=True)
    for variant_id, result in sorted(results.items()):
        variant = variants[variant_id]
        module_hash = result["SpirvSha256"]
        spirv, metadata = Path(result["SpirvPath"]), Path(result["MetadataPath"])
        if digest(spirv) != module_hash:
            raise ValueError(f"Compiled module hash mismatch: {variant_id}")
        if module_hash not in unique_modules:
            unique_modules[module_hash] = metadata
            # Metadata describes the binary's layout; variant source/defines and
            # its content-sensitive cache key remain separate registry records.
            metadata_hash = digest(metadata)
            shutil.copy2(spirv, module_dir / (module_hash + ".spv"))
            shutil.copy2(metadata, module_dir / (module_hash + ".xml"))
            ET.SubElement(modules_node, "Module", Sha256=module_hash, MetadataSha256=metadata_hash)
        node = ET.SubElement(variant_node, "Variant", Id=variant_id, Source=variant["Source"], Profile=variant["Profile"],
                             Entry=variant["EntryPoint"], Module=module_hash, CacheKey=result["CacheKey"],
                             Origins=";".join(variant["Origins"]), Description=variant["Description"] or "", VertexLayout=variant["VertexLayout"] or "")
        for key, value in sorted(variant["Defines"].items()):
            ET.SubElement(node, "Define", Name=key, Value=value)
        for item in (families[variant["Source"].split("/")[0]], source_coverage[variant["Source"]]):
            item["Variants"] += 1
            item["Validated"] += 1
    root.set("UniqueModules", str(len(unique_modules)))
    registry = ET.tostring(root, encoding="utf-8", xml_declaration=True)
    with (destination / "catalog.xml.gz").open("wb") as stream, gzip.GzipFile(filename="", mode="wb", fileobj=stream, mtime=0) as archive:
        archive.write(registry)
    coverage = ET.Element("ShaderSuiteCoverage", Schema="1", Api="1.4", Total=str(len(results)), Validated=str(len(results)),
                          Failed="0", Integrated="0", Verified="0", UniqueModules=str(len(unique_modules)),
                          LegacyExcluded=str(sum(f["Role"] == "LegacyUnreferenced" for f in inventory["Files"])),
                          CatalogSha256=digest(destination / "catalog.xml.gz"), AssemblySha256=inventory["GameAssemblySha256"])
    ET.ElementTree(coverage).write(destination / "coverage.xml", encoding="utf-8", xml_declaration=True)
    summary = {"SchemaVersion": 1, "GameAssemblySha256": inventory["GameAssemblySha256"], "Target": "Vulkan 1.4 / SPIR-V 1.6",
               "TotalVariants": len(results), "Validated": len(results), "Failed": 0, "IntegratedVanillaVariants": 0,
               "VisuallyVerifiedVanillaVariants": 0, "UniqueModules": len(unique_modules),
               "ToolchainSha256": sorted({r["ToolchainSha256"] for r in results.values()}),
               "Sources": inventory["Files"], "Families": dict(sorted(families.items())),
               "Entrypoints": dict(sorted(source_coverage.items())), "Origins": dict(Counter(o for v in variants.values() for o in v["Origins"])),
               "CatalogSha256": digest(destination / "catalog.xml.gz"), "FinishedUtc": report["FinishedUtc"]}
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"Packaged {len(results)} variants / {len(unique_modules)} unique SPIR-V modules; vanilla integration and visual acceptance remain pending.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("inventory", "report", "destination", "summary"):
        parser.add_argument(arg, type=Path)
    args = parser.parse_args()
    package(args.inventory, args.report, args.destination, args.summary)
