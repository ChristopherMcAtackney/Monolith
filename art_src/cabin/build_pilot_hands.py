"""The pilot's hands and arms: human, but noticeably changed by generations on other worlds. Longer, more slender fingers (about a sixth longer than a baseline hand, a
narrower palm), ashen skin with a cold cast and darker banding, short dark horn nails, ridged tendons on the backs of the hands; in worn olive sleeves with a cuff strap.
Never bizarre: five fingers, the right proportions, a hand you could shake. Jointed for procedural posing: every finger segment is its own object pivoting at its joint.

    tools\\blender.ps1 art_src\\cabin\\build_pilot_hands.py        (writes pilot_hands.fbx and pilot_hands.layout.json; then Setup.Project)

Axes: x right, y forward (the fingers), z up (the back of the hand). The right hand has its thumb on -x. Nodes (the contract with Monolith.Cabin.CabinHands):

    {R,L}_wrist                 the hand's root: the palm mesh, pivot at the wrist
    {R,L}_f{0..4}_{a,b,c}       finger segments (0 is the thumb: a, b), nested a > b > c, each pivoting at its joint, flexion about local x
    {R,L}_upper, {R,L}_fore     the sleeve's two halves, pivot at the shoulder and the elbow, running along local +z (Unity), solved by two-bone IK
"""
import math
import os
import sys

import numpy as np

from blender_lib import mnmesh, mncabin, mnpreview
from blender_lib.mnmesh import Frame
from blender_lib.mncabin import CabinAsset, loft, tube, fan_cap, bevel_box, cyl, torus, to_unity

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "Cabin")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "Cabin")
MATERIAL_SET = "Assets/Monolith/Art/Cabin/cabin.materials.json"
MATS = ["mn_cab_skin", "mn_cab_fabric", "mn_cab_metal", "mn_cab_rubber", "mn_cab_nail", "mn_cab_lamp_amber"]
SKIN, CLOTH, METAL, RUBBER, NAIL, LAMP = range(6)

# finger segment lengths (m) after the 15 percent stretch, radii at the base and the tip
STRETCH = 1.15
FINGERS = [
    # name, base x (right hand), base y, segment lengths, base radius, yaw degrees (spread), pitch
    ("thumb", -0.037, 0.010, (0.036 * STRETCH, 0.030 * STRETCH), 0.0125, -38.0),
    ("index", -0.030, 0.092, (0.040 * STRETCH, 0.024 * STRETCH, 0.019 * STRETCH), 0.0098, -2.0),
    ("middle", -0.010, 0.096, (0.044 * STRETCH, 0.027 * STRETCH, 0.020 * STRETCH), 0.0100, 0.0),
    ("ring", 0.010, 0.091, (0.040 * STRETCH, 0.025 * STRETCH, 0.019 * STRETCH), 0.0092, 2.0),
    ("pinky", 0.028, 0.082, (0.031 * STRETCH, 0.019 * STRETCH, 0.016 * STRETCH), 0.0082, 5.0),
]


def F(origin, fwd=(0, 1, 0), up=(0, 0, 1)):
    return Frame(np.asarray(origin, np.float64), np.asarray(fwd, np.float64), np.asarray(up, np.float64))


def V(*a):
    return np.array(a, np.float64)


def ellipse_ring(frame, y, w, t, zc=0.0, n=20, p=2.6):
    """A superellipse section (width w, thickness t) at local y, counter-clockwise about +y: top first, moving to +x."""
    a = np.linspace(0, 2 * math.pi, n, endpoint=False)
    c, s = np.sin(a), np.cos(a)           # starts at the top (z up), runs toward +x
    x = np.sign(c) * np.abs(c) ** (2.0 / p) * w * 0.5
    z = np.sign(s) * np.abs(s) ** (2.0 / p) * t * 0.5 + zc
    return np.array([frame.p(xx, y, zz) for xx, zz in zip(x, z)])


def build_hand(A, side):
    """One hand (side 'R' or 'L'). The left is the right mirrored in x."""
    mx = 1.0 if side == "R" else -1.0
    wrist_pivot = F((0, 0, 0))
    # the palm: a loft from the wrist stump to the knuckle line, thicker at the heel, arched across the back
    palm = A.mesh(f"{side}_wrist", pivot=wrist_pivot)
    secs = [(-0.034, 0.050, 0.036, 0.0), (-0.006, 0.060, 0.034, 0.0015), (0.030, 0.072, 0.030, 0.002), (0.064, 0.082, 0.027, 0.0025), (0.090, 0.088, 0.024, 0.002), (0.104, 0.084, 0.019, 0.001)]
    rings = np.array([ellipse_ring(wrist_pivot, y, w, t, zc) for y, w, t, zc in secs])
    if mx < 0:
        rings = rings * np.array([-1.0, 1.0, 1.0])
    loft(palm, rings, SKIN, smooth=True, flip=(mx < 0), tile=0.2, detail=0.1)
    fan_cap(palm, rings[0], -V(0, 1, 0), SKIN, 0.2)
    fan_cap(palm, rings[-1], V(0, 1, 0), SKIN, 0.2)
    # the heel of the thumb and the little finger's pad
    # (a few small rings along y at the thumb's side give the mound; built as a loft along y)
    mound_rings = np.array([[V(mx * (-0.043) + math.cos(a) * w * 0.5, 0.006 + dy + 0.0, -0.007 + math.sin(a) * t * 0.5) for a in np.linspace(0, 2 * math.pi, 14, endpoint=False)] for dy, w, t in ((0.0, 0.010, 0.008), (0.020, 0.030, 0.024), (0.046, 0.024, 0.018), (0.066, 0.010, 0.008))])
    if mx > 0:
        mound_rings = mound_rings[:, ::-1]
    loft(palm, mound_rings, SKIN, smooth=True, flip=False, tile=0.2, detail=0.1)
    # tendons on the back of the hand: four thin ridges from the knuckles toward the wrist
    for (name, bx, by, lens, br, yaw), k in zip(FINGERS[1:], range(4)):
        path = np.array([V(mx * bx * 0.8, by - 0.005, 0.0170), V(mx * bx * 0.62, by * 0.55, 0.0160), V(mx * bx * 0.42, 0.018, 0.0170), V(mx * bx * 0.30, -0.020, 0.0178)])
        tube(palm, path, 0.0013, 6, SKIN, caps=True, smooth=True, tile=0.1)
    # the fingers
    for fi, (name, bx, by, lens, br, yaw) in enumerate(FINGERS):
        thumb = fi == 0
        parent = f"{side}_wrist"
        yaw_r = math.radians(yaw) * mx
        fwd = V(math.sin(yaw_r) * (1 if thumb else 1), math.cos(yaw_r), 0.0)
        if thumb:
            fwd = V(mx * -math.sin(math.radians(38.0)), math.cos(math.radians(38.0)), -0.12)
        pos = V(mx * bx, by, -0.002 if thumb else 0.0)
        up = V(0, 0, 1)
        n = len(lens)
        for si in range(n):
            ln = lens[si]
            fr = F(pos, fwd, up)
            nm = f"{side}_f{fi}_{'abc'[si]}"
            mb = A.mesh(nm, pivot=fr, parent=parent)
            r0 = br * (1.0 - 0.14 * si) * (1.1 if thumb else 1.0)
            r1 = br * (1.0 - 0.14 * (si + 1)) * (1.05 if thumb else 1.0)
            cyl(mb, fr.p(0, 0.0, 0), fr.p(0, ln, 0), r0, r1, 10, SKIN, caps=False, smooth=True, tile=0.1)
            # the joint: a small ball at the far end hides the seam as the segment bends
            _ball(mb, fr.p(0, ln, 0), r1 * 1.04)
            if si == 0:
                _ball(mb, fr.p(0, 0.0, 0), r0 * 1.02)
            if si == n - 1:
                # the tip, and a short dark nail on the back of it
                _ball(mb, fr.p(0, ln, 0), r1 * 1.0)
                nail = fr.p(0, ln * 0.58, r1 * 0.86)
                bevel_box(mb, F(nail, fr.f, fr.u), (r1 * 1.55, ln * 0.62, r1 * 0.45), NAIL, 0.0012, tile=0.05)
            parent = nm
            pos = pos + fwd * ln
        A.empty(f"{side}_f{fi}_tip", F(pos, fwd, up), parent=parent)


def _ball(mb, c, r, n=8):
    """A small sphere (a lathe of a half circle) at c."""
    prof = [(r * math.sin(t), r * math.cos(t)) for t in np.linspace(math.pi, 0, 7)]
    mb.lathe(prof, F(c), SKIN, segs=n, tile=0.1, crease=100.0)


def build_arm(A, side):
    """The sleeve: an upper-arm tube and a forearm tube with a cuff, each running along local +y from its pivot (the game places the pivots by IK)."""
    mx = 1.0 if side == "R" else -1.0
    up = A.mesh(f"{side}_upper", pivot=F((0, 0, 0)))
    path = np.array([V(0, 0, 0), V(0, 0.10, 0), V(0, 0.20, 0), V(0, 0.30, 0)])
    tube(up, path, np.array([0.036, 0.038, 0.035, 0.031]), 14, CLOTH, caps=True, smooth=True, tile=0.3)
    fo = A.mesh(f"{side}_fore", pivot=F((0, 0, 0)))
    L = 0.27
    path = np.array([V(0, 0, 0), V(0, 0.07, 0), V(0, 0.16, 0), V(0, L - 0.045, 0), V(0, L - 0.02, 0), V(0, L, 0)])
    tube(fo, path, np.array([0.031, 0.032, 0.029, 0.0265, 0.0275, 0.0255]), 14, CLOTH, caps=False, smooth=True, tile=0.3)
    # the cuff strap: a band of worn webbing with a buckle and a tiny amber lamp on the outside of the wrist
    torus(fo, F((0, L - 0.032, 0)), 0.0278, 0.0036, RUBBER, segs=18, tube_segs=6)
    bevel_box(fo, F((0, L - 0.032, 0.031)), (0.014, 0.011, 0.004), METAL, 0.0012, tile=0.05)
    bevel_box(fo, F((mx * 0.010, L - 0.052, 0.0285)), (0.007, 0.007, 0.003), LAMP, 0.001, tile=0.05)


def main():
    preview = "--preview" in sys.argv
    A = CabinAsset("pilot_hands", MATS, ART, RES, MATERIAL_SET)
    for side in ("R", "L"):
        build_hand(A, side)
        build_arm(A, side)
    A.export({"format": "monolith-cabin/1", "about": "The pilot's hands (build_pilot_hands.py).", "displays": [], "levels": [], "obstacles": [], "interactables": [], "lights": [],
              "eye": {"pos": [0, 0, 0]}, "stand": {"pos": [0, 0, 0], "faceYawDeg": 0}, "eyeHeights": {"stand": 1, "crouch": 1}, "bounds": {"min": [0, 0, 0], "max": [0, 0, 0]}})
    if preview:
        out = os.environ.get("MN_PREVIEW_DIR", os.path.join(ROOT, "art_src", "cabin", "_preview_hands"))
        cols = {"mn_cab_skin": (0.55, 0.5, 0.5, 1), "mn_cab_fabric": (0.3, 0.3, 0.2, 1), "mn_cab_metal": (0.6, 0.6, 0.6, 1), "mn_cab_rubber": (0.05, 0.05, 0.05, 1), "mn_cab_nail": (0.1, 0.08, 0.08, 1), "mn_cab_lamp_amber": (1, 0.6, 0.1, 1)}
        import bpy
        for o in bpy.data.objects:                       # lay the pieces out for the picture: the game poses them at run time
            if o.parent is not None and o.parent.type != "EMPTY":
                continue
            if o.name.startswith("L_"):
                o.location.x += 0.24
            if o.name.endswith("_fore"):
                o.location.y -= 0.27
            if o.name.endswith("_upper"):
                o.location.y -= 0.57
        mnpreview.render_views(out, [("top", (0.12, 0.05, 0.55), (0.12, 0.0, 0.0), 50), ("side", (0.60, 0.05, 0.03), (0.0, 0.05, 0.0), 50), ("three", (0.35, -0.30, 0.30), (0.05, 0.02, 0.0), 50), ("close", (-0.10, 0.02, 0.22), (-0.03, 0.06, 0.0), 50)],
                               colours=cols, lens=50, hide=(), sun=(0.3, 0.2, -0.9), clip=(0.02, 50.0))


main()
