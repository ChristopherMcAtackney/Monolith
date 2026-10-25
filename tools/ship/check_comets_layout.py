r"""Proof that swapping the field's rocks for the comets moved nothing: the scenery lines two player runs logged with ``-mnLogScenery`` (index, place, radius and stretch of every rock) must be identical.

    python -I tools/ship/check_comets_layout.py <baseline player.log> <this build's player.log>

Run each build once with ``-mnLogScenery`` (a capture is enough: ``tools\capture.ps1 -Plan ... -PlayerArgs "-mnLogScenery"``). Exit 0 when the two layouts agree to the logged precision, 1 when they differ.
"""
import re
import sys

PAT = re.compile(r"SCENERY (\w+) (\d+) at (\S+) (\S+) (\S+) radius (\S+) stretch (\S+) (\S+) (\S+) seed (\d+)")


def rows(path):
    out = []
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = PAT.search(line)
            if m:
                out.append(m.groups()[:9])
    return out


def main():
    a, b = rows(sys.argv[1]), rows(sys.argv[2])
    if not a or not b:
        print("no SCENERY lines in", sys.argv[1] if not a else sys.argv[2])
        return 1
    same = a == b
    print(f"{len(a)} rocks in the first log, {len(b)} in the second: {'IDENTICAL' if same else 'DIFFERENT'}")
    if not same:
        for i, (x, y) in enumerate(zip(a, b)):
            if x != y:
                print("first difference at row", i, x, y)
                break
    return 0 if same else 1


sys.exit(main())
