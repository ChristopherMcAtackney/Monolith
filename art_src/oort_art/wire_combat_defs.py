"""Wire the Oort models into their combat definitions (plain Python, no Blender):

    python art_src/oort_art/wire_combat_defs.py [outpost|destroyer|all]

For each class it keeps the def's stand-in parts (what a headless lab without a model uses), adds ``partKinds`` (what each kind of the model's parts is worth: the shares of
the hull the stand-ins had), and replaces ``geometry`` with hit boxes that follow the model (Unity axes: x right, y up, z forward; a model's build axes are x right, y forward,
z up, so a build point (x, y, z) is the Unity point (x, z, y)). The boxes are the layout constants of the build scripts (art_src/oort_art/build_earth_outpost.py and
build_earth_destroyer.py): a hit box is axis-aligned, so a lobe of rock is three crossed boxes inside it and the station's blocks are boxes of their own.
Then it reports, for every part the model has, the outward normal the capital AI will give it (the nearest box face: the turret's arc is measured off it).
"""
import json
import math
import os
import sys

ROOT = os.environ.get("MN_ROOT", os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))
DEFS = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "Combat", "ships")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "ShipArt")


def u(c, h):
    """A box from build-axes centre and half extents to a Unity-axes box."""
    return {"centre": [round(c[0], 1), round(c[2], 1), round(c[1], 1)], "half": [round(h[0], 1), round(h[2], 1), round(h[1], 1)]}


def outward_normal(boxes, p):
    best, best_d = (0, 1, 0), 1e18
    for b in boxes:
        q = [p[i] - b["centre"][i] for i in range(3)]
        h = [max(b["half"][i], 0.01) for i in range(3)]
        out = [max(0.0, abs(q[i]) - h[i]) for i in range(3)]
        d = math.sqrt(sum(o * o for o in out))
        if d <= 0.0:
            d = -min(h[i] - abs(q[i]) for i in range(3))
        if d >= best_d:
            continue
        best_d = d
        r = [abs(q[i]) / h[i] for i in range(3)]
        if r[0] >= r[1] and r[0] >= r[2]:
            best = (math.copysign(1, q[0]), 0, 0)
        elif r[1] >= r[2]:
            best = (0, math.copysign(1, q[1]), 0)
        else:
            best = (0, 0, math.copysign(1, q[2]))
    return best


ROCK_LO, ROCK_HI = (-562.0, -411.0, -311.0), (560.0, 346.0, 292.0)       # the nucleus's bounds in build axes (the build script's log line "rock level 6 ... bounds")


def outpost():
    """One broad box over the nucleus (0.95 of its bounds), the station's blocks on it. Why one box and not the lobes' inscribed ones: a capital's beam sweeps a line along the face of
    the hit shape's bounding box at 0.9 of its half-extent (Weapons/Beams.cs PlanSweep), and a shot only hurts what its line meets, so the shape's boxes must fill that face: with
    the lobes' inscribed boxes the Patience's beams missed most of the time and the outpost took three times as long to fall (806 s against 608 s in the Oort auto-play)."""
    ZS, SY, YW = 282.0, -20.0, 315.0
    lo, hi = ROCK_LO, ROCK_HI
    c = tuple((lo[i] + hi[i]) / 2 for i in range(3))
    h = tuple(0.95 * (hi[i] - lo[i]) / 2 for i in range(3))
    boxes = [u(c, h)]
    boxes.append(u((-125.0, SY, ZS + 10.0), (225.0, 50.0, 24.0)))                  # the spine (its decks and the guns on them stand above the box)
    boxes.append(u((-100.0, SY + 42.0, ZS + 150.0), (100.0, 16.0, 100.0)))         # the Eye's ring
    boxes.append(u((-15.0, SY + 42.0, ZS + 110.0), (45.0, 13.0, 60.0)))             # its pylons, one each side
    boxes.append(u((-185.0, SY + 42.0, ZS + 110.0), (45.0, 13.0, 60.0)))
    boxes.append(u((82.0, SY, ZS + 128.0), (14.0, 14.0, 108.0)))                   # the comms tower
    geom = {
        "about": "The nucleus as one broad box over its bounds (a beam sweeps the face of the shape's bounding box, so the boxes must fill it) and the station's blocks on top: the spine, the "
                 "Eye's ring and pylons, the comms tower. Unity axes, the hangar face +z. From art_src/oort_art/build_earth_outpost.py via art_src/oort_art/wire_combat_defs.py.",
        "boxes": boxes,
        "boundRadius": 660,
    }
    kinds = {
        "comms": {"hpPercent": 3.0}, "sensors": {"hpPercent": 2.0}, "hangar": {"hpPercent": 4.0},
        "flak": {"hpPercent": 0.8, "weapon": "flak_gun"}, "aaf": {"hpPercent": 0.8, "weapon": "turret_laser"},
    }
    src = ("Parts come from the model (Resources/ShipArt/earth_outpost.ship.json): the comms array on its tower (kind comms: knock it out or the warning gets out), the Eye and the boom's dish "
           "(sensors), the hangar in the cliff (kind hangar: while it stands the outpost scrambles interceptors), six flak batteries on the station and six anti-fighter guns dug into the ice, "
           "each at the share of the hull the stand-in parts above had (the GTI Arcadia installation's, on FS2's scale). The vents (the comet's jets) are decoration: they break for the show "
           "and are not combat parts. The stand-ins are for a headless lab without a model.")
    return "earth_outpost", geom, kinds, src


def destroyer():
    """The hull stations of build_earth_destroyer.py (y, hw, hh): each interval's inscribed box, then the mesa, the tower, the cannons' turret bodies and the chin hangar pod."""
    hull = [(-500, 52, 38), (-470, 58, 42), (-408, 60, 44), (-398, 66, 46), (-300, 92, 54), (-200, 100, 60), (-110, 92, 60), (-40, 78, 56), (30, 70, 54), (90, 66, 50), (200, 58, 44),
            (330, 46, 36), (342, 46, 34), (430, 32, 26), (498, 20, 16)]
    boxes = []
    for (y0, w0, h0), (y1, w1, h1) in zip(hull[:-1], hull[1:]):
        if y1 - y0 < 15:
            continue
        boxes.append(u(((0.0), (y0 + y1) / 2, 0.0), (min(w0, w1) - 1.5, (y1 - y0) / 2, min(h0, h1) - 1.5)))
    boxes.append(u((0.0, -180.0, 74.0), (50.0, 150.0, 16.0)))                      # the dorsal mesa
    boxes.append(u((0.0, -216.0, 104.0), (36.0, 46.0, 26.0)))                      # the bridge tower
    boxes.append(u((8.0, 285.0, 84.0), (36.0, 112.0, 40.0)))                       # the dorsal cannon: barbette, turret and barrel
    boxes.append(u((-22.0, -208.0, -90.0), (36.0, 78.0, 40.0)))                    # the ventral cannon's
    boxes.append(u((0.0, 152.0, -58.0), (32.0, 60.0, 14.0)))                       # the chin hangar pod
    geom = {
        "about": "The hull's stations as inscribed boxes (the wasp-waisted loft: engine block, the flared citadel, the waist, the lance, the prow), the dorsal mesa and bridge tower, the two cannons' "
                 "turret bodies and the chin hangar pod. Unity axes, nose +z. From art_src/oort_art/build_earth_destroyer.py via art_src/oort_art/wire_combat_defs.py.",
        "boxes": boxes,
        "boundRadius": 640,
    }
    kinds = {
        "turret": {"hpPercent": 1.5, "weapon": "beam_slash"}, "flak": {"hpPercent": 0.8, "weapon": "flak_gun"}, "aaf": {"hpPercent": 0.8, "weapon": "turret_laser"},
        "engine": {"hpPercent": 4.0}, "hangar": {"hpPercent": 4.0}, "sensors": {"hpPercent": 3.0}, "comms": {"hpPercent": 3.0}, "bridge": {"hpPercent": 2.0}, "reactor": {"hpPercent": 2.0},
    }
    src = ("Parts come from the model (Resources/ShipArt/earth_destroyer.ship.json): the two heavy beam cannons (kind turret, firing beam_slash: the dorsal one on the lance, the ventral one "
           "aft under the citadel; the part sits at the muzzle, where the charge glow gathers), six flak batteries on the shoulders, six anti-fighter guns on the decks and the belly, the three "
           "engines, the chin hangar, the bridge on its tower, the sensor panel on the tower and the dish on the prow, the comms mast and the reactor stacks, each at the share of the hull "
           "the stand-in parts above had (FS2's GTD Hecate on FS2's capital shares). The stand-ins are for a headless lab without a model.")
    return "earth_destroyer", geom, kinds, src


NAMES = {
    "earth_outpost": ("Earth comet outpost",
                      "Earth's early-warning outpost (design section 3, the strike), built into a comet nucleus. Its parts come from the model (see partKindsSource): the comms array (kind comms: knock it out "
                      "or the warning gets out), the hangar (kind hangar: while it stands, the outpost scrambles interceptors), the Eye and the boom's dish (sensors), flak on the station, anti-fighter guns "
                      "dug into the ice. Hull, power and weapon energy are FS2's GTI Arcadia installation's; part shares ours, on FS2's scale. Missions address parts by kind, so the stand-in parts below "
                      "are only for a lab without the model."),
    "earth_destroyer": ("Hecate-class destroyer",
                        "Earth's destroyer for the Oort strike (design section 3, beat 5). Its parts come from the model (see partKindsSource): two heavy beam cannons (kind turret, firing beam_slash), flak, "
                        "anti-fighter guns, three engines, a chin hangar, bridge, sensors, comms and reactor. Hull, power and weapon energy are FS2's GTD Hecate's; part shares follow FS2's capital ships "
                        "(turrets about 0.8% each, beams 1.5%, engines 4%). Missions address parts by kind (turret for the beams, engine, hangar), so the stand-in parts below are only for a lab without "
                        "the model."),
}


def wire(res):
    cid, geom, kinds, src = res
    path = os.path.join(DEFS, cid + ".json")
    d = json.load(open(path, encoding="utf-8"))
    d["name"], d["note"] = NAMES[cid]
    d["geometry"] = geom
    d["partKindsSource"] = src
    d["partKinds"] = kinds
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(d, indent=2, ensure_ascii=False) + "\n")
    man = os.path.join(RES, cid + ".ship.json")
    if os.path.exists(man):
        m = json.load(open(man, encoding="utf-8"))
        print(f"{cid}: {len(geom['boxes'])} boxes; parts:")
        for s in m["subsystems"]:
            if s["kind"] not in kinds:
                continue
            n = outward_normal(geom["boxes"], s["position"])
            print(f"  {s['name']:<24} {s['kind']:<8} at {tuple(round(v) for v in s['position'])} normal {n}")
    else:
        print(f"{cid}: wired (no manifest yet)")


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    for name, fn in (("outpost", outpost), ("destroyer", destroyer)):
        if what in ("all", name):
            r = fn()
            if r:
                wire(r)
