"""Check the combat data against FS2's own tables.

Every number in Resources/Combat/weapons.json and Resources/Combat/ships/*.json is { value, unit, fs2 } or { value, unit, source }.
An 'fs2' entry names the FS2 table field the number was copied from, as "<name>|<field>" in data/tables/weapons.tbl, or
"ships:<name>|<field>" in data/tables/ships.tbl. This script opens FS2's tables in place (read only, from Root_fs2.vp, like
tools/fs2ref does), looks each one up and compares. A 'source' entry is ours: it is listed, not checked.

    python -I tools/combat/check_weapons.py [--out report.txt] [--vp path/to/Root_fs2.vp]

Exit code 0 when every copied number matches, 1 otherwise.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "tools", "fs2ref"))
import ships as fs2vp  # noqa: E402  (reads the VP archive in place)

COMBAT = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "Combat")

_FIELD = re.compile(r"^\s*([$+@][^:]+):\s*(.*)$")
_NUM = re.compile(r"^-?\d+(\.\d+)?")


def parse_table(text: str) -> dict[str, list[dict[str, str]]]:
    """name -> list of blocks (a name can appear more than once, e.g. ship templates), each a field -> raw value."""
    out: dict[str, list[dict[str, str]]] = {}
    blocks = re.split(r"(?m)^\$Name:", text)[1:]
    for b in blocks:
        lines = b.split("\n")
        name = lines[0].split(";")[0].strip().lstrip("@").strip()
        fields: dict[str, str] = {}
        for line in lines[1:]:
            line = line.split(";;")[0]
            m = _FIELD.match(line)
            if m:
                fields.setdefault(m.group(1).strip().lower(), m.group(2).strip())
        out.setdefault(name, []).append(fields)
    return out


def lookup(table: dict[str, list[dict[str, str]]], name: str, field: str) -> float | None:
    for fields in table.get(name, []):
        raw = fields.get(field.lower())
        if raw is None:
            continue
        m = _NUM.match(raw.split()[0] if raw else "")
        if m:
            return float(m.group(0))
    return None


def walk(node, path=""):
    """Yield (path, entry) for every { value, unit, fs2/source } entry in a json tree."""
    if isinstance(node, dict):
        if "value" in node and ("fs2" in node or "source" in node):
            yield path, node
            return
        for k, v in node.items():
            yield from walk(v, f"{path}.{k}" if path else k)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from walk(v, f"{path}[{i}]")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--vp", default=None)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()
    vp = fs2vp.vp_path(args.vp)
    weapons_tbl = parse_table(fs2vp.read_vp_entry(vp, "data/tables/weapons.tbl").decode("latin-1"))
    ships_tbl = parse_table(fs2vp.read_vp_entry(vp, "data/tables/ships.tbl").decode("latin-1"))

    files = [os.path.join(COMBAT, "weapons.json")]
    sdir = os.path.join(COMBAT, "ships")
    files += [os.path.join(sdir, f) for f in sorted(os.listdir(sdir)) if f.endswith(".json")]

    lines: list[str] = []
    copied = ours = bad = 0
    for path in files:
        data = json.load(open(path, encoding="utf-8"))
        rel = os.path.relpath(path, COMBAT).replace("\\", "/")
        for where, entry in walk(data):
            value = entry["value"]
            if "fs2" in entry:
                spec = entry["fs2"]
                table = weapons_tbl
                if spec.startswith("ships:"):
                    table = ships_tbl
                    spec = spec[len("ships:"):]
                name, _, field = spec.partition("|")
                fs2 = lookup(table, name.strip(), field.strip())
                copied += 1
                if fs2 is None:
                    bad += 1
                    lines.append(f"FAIL  {rel} {where}: FS2 has no '{field}' for '{name}'")
                elif abs(fs2 - float(value)) > 1e-6:
                    bad += 1
                    lines.append(f"FAIL  {rel} {where}: ours {value} but FS2 {name} {field} is {fs2}")
                else:
                    lines.append(f"PASS  {rel} {where} = {value}  (FS2 {name} {field})")
            else:
                ours += 1
                lines.append(f"OURS  {rel} {where} = {value}  ({entry['source'][:110]})")

    header = [
        f"FS2 table check: {copied} copied numbers compared against {os.path.basename(vp)} (weapons.tbl, ships.tbl): {copied - bad} match, {bad} differ.",
        f"{ours} numbers are ours, each with a source note (listed below, not checked).",
        "",
    ]
    report = "\n".join(header + lines) + "\n"
    if args.out:
        os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
        open(args.out, "w", encoding="utf-8").write(report)
    print("\n".join(header) + "\n".join(l for l in lines if not l.startswith("PASS")))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
