#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Merge the communication example into an existing appconfig.json safely.

Default is a DRY RUN: it only prints what would be added/skipped.
Use --apply to write (a timestamped .bak file is created next to the target).

Examples:
    python tools/merge_comm_config.py
    python tools/merge_comm_config.py --target bin/x64/Release/appconfig.json --apply
    python tools/merge_comm_config.py --overwrite --apply

Rules:
  - Only the "Comm" node is touched; Algorithm / Flows / System / Ui are preserved.
  - Endpoints / Points / Messages / Jobs are matched by "Name";
    FlowBindings are matched by "Flow".
  - Existing entries are kept unless --overwrite is given.
"""

import argparse
import datetime
import json
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_TARGET = os.path.join(ROOT, "bin", "x64", "Release", "appconfig.json")
DEFAULT_SAMPLE = os.path.join(ROOT, "docs", "案例", "通讯配置-双流程-TCP示例.json")


def load_json(path):
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def get_comm(doc):
    if not isinstance(doc, dict):
        raise ValueError("配置文件根节点不是 JSON 对象")
    if "Comm" in doc:
        return doc["Comm"]
    # allow passing a file that only contains {"Comm": {...}}
    if "Endpoints" in doc or "Points" in doc:
        return doc
    raise ValueError("找不到 Comm 节点")


def key_of(kind, item):
    if kind == "FlowBindings":
        return str(item.get("Flow", "")).strip().lower()
    return str(item.get("Name", "")).strip().lower()


def merge_list(target, source, kind, overwrite):
    if target is None:
        target = []
    if source is None:
        source = []

    added, replaced, skipped = [], [], []
    index = {key_of(kind, x): i for i, x in enumerate(target) if isinstance(x, dict)}

    for item in source:
        if not isinstance(item, dict):
            continue
        k = key_of(kind, item)
        if not k:
            continue

        if k in index:
            if overwrite:
                target[index[k]] = item
                replaced.append(item.get("Name") or item.get("Flow"))
            else:
                skipped.append(item.get("Name") or item.get("Flow"))
        else:
            index[k] = len(target)
            target.append(item)
            added.append(item.get("Name") or item.get("Flow"))

    return target, added, replaced, skipped


def remap_sample_flows(comm, mapping):
    """Rewrite sample aliases (F1/F2) in Jobs.Flows, StartFlow actions and FlowBindings."""
    def m(v):
        return mapping.get(str(v).strip().lower(), v)

    for job in comm.get("Jobs", []) or []:
        flows = job.get("Flows")
        if isinstance(flows, list):
            job["Flows"] = [m(x) for x in flows]
        for a in job.get("Actions", []) or []:
            if str(a.get("Kind", "")).lower() == "startflow":
                a["Target"] = m(a.get("Target", ""))

    for b in comm.get("FlowBindings", []) or []:
        b["Flow"] = m(b.get("Flow", ""))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", default=DEFAULT_TARGET, help="existing appconfig.json")
    ap.add_argument("--sample", default=DEFAULT_SAMPLE, help="example JSON")
    ap.add_argument("--apply", action="store_true", help="write changes (default: dry-run)")
    ap.add_argument("--overwrite", action="store_true", help="replace same-name entries")
    ap.add_argument("--flow-map", default="", help='map sample aliases, e.g. "F1=流程 1,F2=流程 2"')
    ap.add_argument("--list-flows", action="store_true", help="list flows in target and exit")
    args = ap.parse_args()

    target_path = os.path.abspath(args.target)
    sample_path = os.path.abspath(args.sample)

    if not os.path.exists(target_path):
        print("[ERROR] target not found: " + target_path)
        return 2
    if not os.path.exists(sample_path):
        print("[ERROR] sample not found: " + sample_path)
        return 2

    if args.list_flows:
        try:
            doc = load_json(target_path)
        except Exception as ex:
            print("[ERROR] " + str(ex))
            return 2
        flows = doc.get("Flows", {}).get("Flows", []) if isinstance(doc, dict) else []
        print("Flows in " + target_path)
        for f in flows:
            if isinstance(f, dict):
                print("  Id=%-34s Name=%s  Enabled=%s" % (f.get("Id", ""), f.get("Name", ""), f.get("Enabled", "")))
        return 0

    try:
        target_doc = load_json(target_path)
        sample_doc = load_json(sample_path)
        target_comm = get_comm(target_doc)
        sample_comm = get_comm(sample_doc)
    except Exception as ex:
        print("[ERROR] " + str(ex))
        return 2

    mapping = {}
    for item in (args.flow_map or "").split(","):
        if "=" not in item:
            continue
        a, b = item.split("=", 1)
        a, b = a.strip(), b.strip()
        if a and b:
            mapping[a.lower()] = b

    if mapping:
        remap_sample_flows(sample_comm, mapping)

    # 合并前提示：示例里的流程别名是否能在目标配置里找到
    target_flows = target_doc.get("Flows", {}).get("Flows", []) if isinstance(target_doc, dict) else []
    known = set()
    for f in target_flows:
        if isinstance(f, dict):
            if f.get("Id"): known.add(str(f["Id"]).lower())
            if f.get("Name"): known.add(str(f["Name"]).lower())

    unresolved = []
    for job in sample_comm.get("Jobs", []) or []:
        for f in job.get("Flows", []) or []:
            if str(f).lower() not in known:
                unresolved.append(str(f))
        for a in job.get("Actions", []) or []:
            if str(a.get("Kind", "")).lower() == "startflow":
                t = str(a.get("Target", ""))
                if t and t.lower() not in known:
                    unresolved.append(t)
    for b in sample_comm.get("FlowBindings", []) or []:
        f = str(b.get("Flow", ""))
        if f and f.lower() not in known:
            unresolved.append(f)

    if unresolved:
        print("[WARN] sample references flows not found in target: " + ", ".join(sorted(set(unresolved))))
        print("       use --list-flows and --flow-map \"F1=<Id或名称>,F2=<Id或名称>\"")
        print("")

    old_schema = int(target_comm.get("SchemaVersion", 0) or 0)
    new_schema = max(2, old_schema)
    target_comm["SchemaVersion"] = new_schema

    report = []
    total_added = 0
    for kind in ("Endpoints", "Points", "Messages", "Jobs", "FlowBindings"):
        merged, added, replaced, skipped = merge_list(
            target_comm.get(kind), sample_comm.get(kind), kind, args.overwrite)
        target_comm[kind] = merged
        total_added += len(added)
        report.append((kind, added, replaced, skipped))

    print("Target : " + target_path)
    print("Sample : " + sample_path)
    print("Mode   : " + ("APPLY" if args.apply else "DRY-RUN (add --apply to write)"))
    print("Schema : %d -> %d" % (old_schema, new_schema))
    print("")
    for kind, added, replaced, skipped in report:
        print("%-13s +%d ~%d =%d" % (kind, len(added), len(replaced), len(skipped)))
        if added:
            print("    add    : " + ", ".join(added))
        if replaced:
            print("    replace: " + ", ".join(replaced))
        if skipped:
            print("    keep   : " + ", ".join(skipped))

    if not args.apply:
        print("")
        print("Nothing written. Re-run with --apply after reviewing.")
        return 0

    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = target_path + ".bak-" + stamp
    shutil.copy2(target_path, backup)

    with open(target_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(target_doc, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print("")
    print("Backup : " + backup)
    print("Written: " + target_path)
    print("Added %d entries. Restart VisionPlatform to load them." % total_added)
    return 0


if __name__ == "__main__":
    sys.exit(main())
