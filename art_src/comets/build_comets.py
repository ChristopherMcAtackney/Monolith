r"""The Oort ice field's comet nuclei: five models the game scatters by the hundred (tools/blender_lib/mncomet.py has the machinery, this file has the five bodies).

    tools\blender.ps1 art_src\oort_art\build_comet_textures.py          (once, or when the surface textures change)
    tools\blender.ps1 art_src\comets\build_comets.py                      (writes the FBXs and the manifest; then tools\unity.ps1 Monolith.EditorTools.Setup.Project)
    tools\blender.ps1 art_src\comets\build_comets.py -- --preview a,b     (Cycles looks, one hard Sun and a black sky, into $MN_PREVIEW_DIR; --level 5 for a quick one; --no-export)

The two large ones are the distinctive ones, made to be seen from a couple of kilometres: ``comet_a`` (a bilobed nucleus like 67P: a big lobe and a head with a smooth dusty neck between them,
cliffs, pits and boulder fields) and ``comet_b`` (long, terraced and faulted like Tempel 1 and Borrelly, a smooth plain, a great scarp, a pair of craters). The three small ones are plainer:
``comet_c`` (a lumpy potato with dish craters and a pond of dust), ``comet_d`` (round and pitted, flat-floored pits like Wild 2's) and ``comet_e`` (a split fragment: planar faces, one fresh
layered cliff). All are built at their own native size in metres (the large ones ~1.2 km, the small ones ~250 m) and centred on their volume centroid; the game scales them to a mission rock's
radius with each model's ``fit`` (the bound, in mission radii) and stretches them per rock.

Axes while building: Blender's (x right, y forward, z up); the FBX is exported as the ships' are (Unity's bake-axis conversion turns it into x right, y up, z forward).
"""
import json
import math
import os
import sys
import time

import numpy as np

from blender_lib import mncomet, mnrock
from blender_lib.mncomet import Shape, IceSites
from blender_lib.mnmesh import norm

ROOT = os.environ.get("MN_ROOT", os.getcwd())
ART = os.path.join(ROOT, "Game", "Assets", "Monolith", "Art", "Comets")
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources", "CometArt")
MATERIAL_SET = "Assets/Monolith/Art/EarthHull/comets.materials.json"
MATERIAL = "mn_comet_surface"                   # one layered material: crust, dust and ice, blended by the vertex colour (R dust, G ice)
T0 = time.time()
LOD_SCREEN = [0.2, 0.065, 0.02, 0.0025]        # relative screen height each level starts at (the last is also where the body is culled)
HERO_LOD = dict(min_boulder=(0.0, 3.0, 6.0, 12.0, 1e9), lod_screen=[0.3, 0.14, 0.05, 0.016, 0.002])      # the two large ones carry five levels: 330k triangles for the close view
TILE, DETAIL = 150.0, 4.0                   # metres per texture tile (UV0) and per detail tile (UV1), at the model's own size


def log(msg):
    print(f"[MN] comets {time.time() - T0:5.1f}s  {msg}", flush=True)


def at(shape, d):
    """The base surface point in direction d."""
    d = norm(np.asarray(d, float))[None]
    return (d * shape.radial(d)[:, None])[0]


# ================================================================================================ A: the bilobe
def terrain_a(T, shape, level):
    P = T.P
    neck = np.exp(-(((P[:, 0] - 95.0) / 85.0) ** 2))                          # the waist between the lobes
    T.lumps(55, 560, 3, seed=1)
    T.lumps(26, 230, 3, seed=2)
    T.zones(300, -0.26, -0.02, bias=-1.0 * neck)                                # most of it rough, the neck smooth
    T.terraces(34, 5, 290, 26.0, seed=210)
    T.scarp((0.35, -0.2, 0.9), 150.0, 70.0, 26.0, 60.0, seed=620, mask=1.0 - neck)
    T.blocks(26, 90, seed=310)
    T.plains(14, 240)
    for d, R, depth, kind in A_FEATURES:
        c = at(shape, d)
        (T.pit if kind == "pit" else T.crater)(c, R, depth)
    T.trenches(6.0, 330, 0.02, seed=510, mask=T.rough)
    hollow = T.fill_hollows(radius=26, strength=0.8, lo=1.2, hi=5.0)
    T.rubble(20.0, (110, 55, 27, 13.5), floor=0.35, mask=1.0 - 0.7 * hollow)
    T.ripples(0.7, 10)


A_FEATURES = [((0.92, 0.30, 0.45), 40, 36, "pit"), ((0.78, -0.40, 0.40), 29, 28, "pit"), ((0.55, 0.70, 0.30), 52, 16, "crater"),
              ((-0.80, 0.45, 0.35), 105, 26, "crater"), ((-0.55, -0.75, 0.30), 82, 22, "crater"), ((-0.25, 0.20, 0.95), 64, 18, "crater"),
              ((-0.95, -0.15, -0.20), 70, 20, "crater"), ((0.15, -0.85, -0.50), 58, 15, "crater"), ((-0.30, 0.9, -0.2), 46, 12, "crater")]


def spec_a():
    shape = Shape([dict(c=(-250, 0, -10), a=(370, 310, 295), p=2.5, rot=(0, 0, 10)), dict(c=(330, 20, 35), a=(235, 205, 200), p=2.4, rot=(0, 12, -8))], blend=110.0)
    ice = IceSites([(at(shape, d), r) for d, r in [((0.78, -0.40, 0.40), 12), ((-0.80, 0.45, 0.35), 24), ((-0.2, 0.2, 0.95), 16), ((0.55, 0.70, 0.30), 14), ((-0.55, -0.75, 0.30), 14), ((0.2, -0.9, -0.3), 10)]], seed=3)
    return dict(id="comet_a", name="Bilobe", shape=shape, seed=17, terrain=terrain_a, ice=ice, fit=1.5, hero=True, **HERO_LOD,
                boulders=dict(count=2600, rmin=1.2, rmax=22.0, clusters=44, cluster_frac=0.55, cliff=3.5, hollow=1.2, floor=0.8, base=0.15, lv2_r=4.0))


# ================================================================================================ B: the long, terraced one
def smoothstep_(a, b, x):
    return mnrock.smoothstep(a, b, x)


def terrain_b(T, shape, level):
    P = T.P
    below = smoothstep_(-80.0, 60.0, P[:, 1] * 0.8 + P[:, 2] * 0.3)             # one flank is a smooth plain, the other a rough faulted plateau
    T.lumps(50, 520, 3, seed=1)
    T.lumps(22, 190, 3, seed=2)
    T.zones(300, -0.2, 0.0, bias=1.0 - 1.5 * below)
    T.terraces(26, 6, 330, 24.0, seed=210)
    T.scarp((0.15, 0.93, 0.34), 30.0, 80.0, 24.0, 55.0, seed=620)
    T.scarp((-0.2, -0.3, 0.93), 120.0, 36.0, 20.0, 50.0, seed=621, mask=T.rough)
    T.blocks(24, 80, seed=310)
    T.plains(10, 260)
    for d, R, depth, kind in B_FEATURES:
        c = at(shape, d)
        (T.pit if kind == "pit" else T.crater)(c, R, depth)
    T.trenches(5.0, 300, 0.02, seed=510, mask=T.rough)
    hollow = T.fill_hollows(radius=24, strength=0.8, lo=1.2, hi=5.0)
    T.rubble(18.0, (100, 50, 25, 12.5), floor=0.35, mask=1.0 - 0.7 * hollow)
    T.ripples(0.6, 10)


B_FEATURES = [((-0.9, 0.2, 0.4), 120, 24, "crater"), ((0.30, -0.8, 0.4), 90, 20, "crater"), ((-0.3, 0.1, 0.95), 56, 18, "crater"), ((0.9, 0.3, 0.3), 70, 22, "crater"),
              ((0.55, 0.15, 0.82), 30, 30, "pit"), ((0.7, 0.4, 0.6), 24, 24, "pit"), ((-0.5, -0.6, 0.6), 46, 12, "crater"), ((0.1, -0.5, -0.85), 60, 14, "crater")]


def spec_b():
    shape = Shape([dict(c=(-140, 0, 0), a=(440, 262, 238), p=2.4, rot=(0, 0, 14)), dict(c=(345, 28, 12), a=(330, 192, 172), p=2.3, rot=(0, 8, -10))], blend=95.0)
    ice = IceSites([(at(shape, d), r) for d, r in [((0.7, 0.4, 0.6), 12), ((-0.9, 0.2, 0.4), 26), ((0.15, 0.9, 0.3), 18), ((-0.3, 0.1, 0.95), 13), ((0.3, -0.8, 0.4), 16)]], seed=5)
    return dict(id="comet_b", name="Terraced", shape=shape, seed=29, terrain=terrain_b, ice=ice, fit=1.5, hero=True, **HERO_LOD,
                boulders=dict(count=2600, rmin=1.2, rmax=20.0, clusters=40, cluster_frac=0.55, cliff=3.5, hollow=1.0, floor=0.8, base=0.15, lv2_r=4.0))


# ================================================================================================ C: a lumpy potato
def terrain_c(T, shape, level):
    T.lumps(16, 150, 3, seed=1)
    T.lumps(7, 55, 3, seed=2)
    T.zones(110, -0.05, 0.2, bias=0.15)
    T.terraces(5, 3, 120, 10.0, seed=210)
    T.blocks(4.5, 30, seed=310)
    T.plains(4, 100)
    for d, R, depth, kind in C_FEATURES:
        c = at(shape, d)
        (T.pit if kind == "pit" else T.crater)(c, R, depth)
    T.trenches(1.6, 130, 0.025, seed=510, mask=T.rough)
    hollow = T.fill_hollows(radius=10, strength=0.8, lo=0.4, hi=2.0)
    T.rubble(3.0, (38, 19, 9.5, 5), floor=0.35, mask=1.0 - 0.8 * hollow)
    T.ripples(0.25, 5)


C_FEATURES = [((0.8, 0.3, 0.5), 44, 10, "crater"), ((-0.7, 0.6, 0.2), 33, 8, "crater"), ((0.0, -0.9, 0.4), 28, 7, "crater"), ((-0.4, -0.3, -0.85), 24, 6, "crater"),
              ((0.4, 0.8, -0.4), 20, 5, "crater")]


def spec_c():
    shape = Shape([dict(c=(0, 0, 0), a=(150, 112, 96), p=2.2, rot=(8, 4, 20)), dict(c=(96, 58, 28), a=(66, 56, 52), p=2.2)], blend=36.0)
    ice = IceSites([(at(shape, d), r) for d, r in [((0.8, 0.3, 0.5), 11), ((0.0, -0.9, 0.4), 8)]], seed=7)
    return dict(id="comet_c", name="Potato", shape=shape, seed=41, terrain=terrain_c, ice=ice, fit=1.3, hero=False, min_boulder=(0.0, 1.6, 4.0, 1e9),
                boulders=dict(count=700, rmin=0.6, rmax=5.0, clusters=12, cluster_frac=0.4, cliff=3.0, hollow=1.0, floor=0.8, base=0.2, lv2_r=2.2))


# ================================================================================================ D: round and pitted
def terrain_d(T, shape, level):
    T.lumps(9, 140, 3, seed=1)
    T.lumps(4, 50, 3, seed=2)
    T.zones(100, -0.1, 0.12, bias=-0.1)
    T.terraces(4, 2, 100, 9.0, seed=210)
    T.blocks(3.0, 26, seed=310)
    T.plains(3, 80)
    for d, R, depth, kind in D_FEATURES:
        c = at(shape, d)
        (T.pit if kind == "pit" else T.crater)(c, R, depth)
    T.trenches(1.2, 120, 0.02, seed=510, mask=T.rough)
    hollow = T.fill_hollows(radius=9, strength=0.8, lo=0.35, hi=1.8)
    T.rubble(2.4, (34, 17, 8.5, 4.5), floor=0.3, mask=1.0 - 0.8 * hollow)
    T.ripples(0.2, 4.5)


D_FEATURES = [((0.9, 0.2, 0.4), 28, 24, "pit"), ((-0.6, 0.7, 0.3), 22, 18, "pit"), ((0.1, -0.9, 0.35), 25, 20, "pit"), ((-0.8, -0.3, -0.5), 18, 14, "pit"), ((0.5, 0.5, -0.7), 16, 12, "pit"),
              ((-0.2, 0.2, 0.95), 34, 8, "crater"), ((0.6, -0.7, -0.35), 27, 7, "crater"), ((-0.95, 0.1, 0.1), 14, 10, "pit")]


def spec_d():
    shape = Shape([dict(c=(0, 0, 0), a=(124, 112, 104), p=2.1, rot=(0, 10, 5))], blend=30.0)
    ice = IceSites([(at(shape, d), r) for d, r in [((0.9, 0.2, 0.4), 9), ((-0.8, -0.3, -0.5), 7), ((-0.2, 0.2, 0.95), 8)]], seed=9)
    return dict(id="comet_d", name="Pitted", shape=shape, seed=53, terrain=terrain_d, ice=ice, fit=1.2, hero=False, min_boulder=(0.0, 1.6, 4.0, 1e9),
                boulders=dict(count=650, rmin=0.5, rmax=4.0, clusters=10, cluster_frac=0.35, cliff=3.0, hollow=1.0, floor=1.0, base=0.2, lv2_r=2.0))


# ================================================================================================ E: a split fragment
E_CUTS = [((1.0, 0.18, 0.08), 100.0), ((-0.32, 0.92, 0.22), 98.0), ((0.18, -0.28, 1.0), 80.0), ((-0.9, -0.4, -0.2), 132.0)]
E_FEATURES = [((-0.5, 0.2, 0.8), 26, 7, "crater"), ((-0.7, -0.6, 0.1), 20, 5, "crater"), ((0.1, 0.4, -0.9), 22, 6, "crater")]


def terrain_e(T, shape, level):
    P = T.P
    cut = P @ norm(np.array(E_CUTS[0][0])) - E_CUTS[0][1]                       # distance (negative) behind the big fresh face
    fresh = mnrock.smoothstep(-10.0, -1.0, cut)
    T.lumps(16, 120, 3, seed=1)
    T.lumps(6, 42, 3, seed=2)
    T.zones(90, -0.12, 0.1, bias=0.3 + 1.2 * fresh)
    T.strata(E_CUTS[0][0], 5.5, 9, 1.6, 2.2, offset=E_CUTS[0][1] - 52.0, mask=fresh * 1.0, wobble=2.0)
    T.blocks(3.2, 28, seed=310)
    T.plains(2.5, 80)
    for d, R, depth, kind in E_FEATURES:
        c = at(shape, d)
        (T.pit if kind == "pit" else T.crater)(c, R, depth)
    T.trenches(1.4, 110, 0.02, seed=510, mask=T.rough)
    hollow = T.fill_hollows(radius=9, strength=0.8, lo=0.35, hi=1.8)
    T.rubble(2.6, (32, 16, 8, 4.2), floor=0.3, mask=1.0 - 0.8 * hollow)
    T.ripples(0.2, 4.5)


def spec_e():
    shape = Shape([dict(c=(10, 0, 0), a=(158, 120, 98), p=2.25, rot=(0, 6, 12))], blend=20.0, cuts=E_CUTS, cut_blend=16.0)
    ice = IceSites([(at(shape, (0.97, 0.2, 0.05)) + np.array([0.0, 0.0, 14.0]), 14), (at(shape, (0.97, 0.15, -0.15)), 11), (at(shape, (-0.5, 0.2, 0.8)), 6)], seed=11)
    return dict(id="comet_e", name="Fragment", shape=shape, seed=67, terrain=terrain_e, ice=ice, fit=1.25, hero=False, ice_anywhere=True, min_boulder=(0.0, 1.6, 4.0, 1e9),
                boulders=dict(count=650, rmin=0.6, rmax=4.5, clusters=10, cluster_frac=0.4, cliff=3.5, hollow=1.0, floor=0.8, base=0.2, lv2_r=2.2))


BODIES = {"a": spec_a, "b": spec_b, "c": spec_c, "d": spec_d, "e": spec_e}
LEVELS = {k: ((7, 6, 5, 4, 3) if k in ("a", "b") else (6, 5, 4, 3)) for k in BODIES}
MIN_BOULDER = (0.0, 3.5, 9.0, 1e9)             # the smallest boulder (metres) each level of detail keeps


# ================================================================================================ the build
def build_body(key, only_level=None):
    spec = BODIES[key]()
    levels = LEVELS[key] if only_level is None else (only_level,)
    out = []
    boulders = None
    for i, lv in enumerate(levels):
        t = time.time()
        r = mncomet.build_level(spec, lv, boulders if i else None, spec.get("min_boulder", MIN_BOULDER)[i])
        if i == 0:
            boulders = r["boulders"]
        out.append((lv, r))
        log(f"{spec['id']} level {lv}: base {r['base_faces']} faces, {r['n_boulders']} boulders, total {len(r['F'])} faces, edge {r['edge']:.2f} m, dust {r['dust_frac'] * 100:.0f}% ice {r['ice_frac'] * 100:.1f}%  ({time.time() - t:.1f}s)")
        log("   " + ", ".join(f"{k} {v:.2f}" for k, v in r["stats"].items()))
    return spec, out


def export_fbx(spec, built, cen, fbx_path):
    """One FBX per body: <id> (an empty) with <id>_LOD0 .. _LODn under it, one material slot, the vertex colour "Col" (R dust, G ice) and both UV sets. -> the manifest entry."""
    import bpy
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    root = bpy.data.objects.new(spec["id"], None)
    bpy.context.scene.collection.objects.link(root)
    tris, verts = [], []
    bound = 0.0
    for lv, r in built:
        d = mncomet.assemble(r["V"] - cen, r["F"], r["dust"], r["ice"], TILE, DETAIL)
        mesh = mncomet.blender_mesh(f"{spec['id']}_LOD{len(tris)}", d, MATERIAL)
        obj = bpy.data.objects.new(f"{spec['id']}_LOD{len(tris)}", mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.parent = root
        tris.append(int(len(d["tris"])))
        verts.append(int(len(d["pos"])))
        if len(tris) == 1:
            bound = float(np.linalg.norm(r["V"] - cen, axis=1).max())
            lo, hi = (r["V"] - cen).min(axis=0), (r["V"] - cen).max(axis=0)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.data.objects:
        o.select_set(True)
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="Y", axis_up="Z",
                             bake_space_transform=False, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True, mesh_smooth_type="OFF", use_tspace=False, add_leaf_bones=False,
                             path_mode="STRIP", embed_textures=False, colors_type="LINEAR")
    r0 = built[0][1]
    vol = mncomet.volume_centroid(r0["V"][:r0["base_verts"]], r0["F"][:r0["base_faces"]])[1]
    entry = dict(id=spec["id"], name=spec["name"], fbx=os.path.basename(fbx_path)[:-4], hero=bool(spec.get("hero")), fit=spec["fit"], bound=round(bound, 2),
                 volumeRadius=round((3.0 * vol / (4.0 * math.pi)) ** (1.0 / 3.0), 2), extent=[round(float(x), 1) for x in (hi - lo)],
                 triangles=tris, vertices=verts, lods=len(tris), lodScreen=spec.get("lod_screen", LOD_SCREEN)[:len(tris)], material=MATERIAL,
                 dustFraction=round(r0["dust_frac"], 3), iceFraction=round(r0["ice_frac"], 4), boulders=r0["n_boulders"], jets=spec.get("jets_manifest", []))
    log(f"exported {os.path.basename(fbx_path)} ({os.path.getsize(fbx_path) / 1e6:.1f} MB): triangles {tris}, bound {bound:.0f} m, extent {entry['extent']}, volume radius {entry['volumeRadius']:.0f} m")
    return entry


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    keys = list(BODIES)
    preview = None
    level = None
    export = True
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--preview":
            i += 1
            preview = args[i].split(",")
        elif a == "--only":
            i += 1
            keys = args[i].split(",")
        elif a == "--level":
            i += 1
            level = int(args[i])
        elif a == "--no-export":
            export = False
        i += 1
    import bpy
    entries = []
    for key in keys:
        spec, built = build_body(key, level)
        lv0, r0 = built[0]
        cen, vol = mncomet.volume_centroid(r0["V"][:r0["base_verts"]], r0["F"][:r0["base_faces"]])
        log(f"{spec['id']}: volume {vol / 1e6:.2f} Mm3, centroid {np.round(cen, 1)}")
        if preview and key in preview:
            for o in list(bpy.data.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            d = mncomet.assemble(r0["V"] - cen, r0["F"], r0["dust"], r0["ice"], TILE, DETAIL)
            mat = mncomet.preview_material(MATERIAL)
            mesh = mncomet.blender_mesh(spec["id"], d, MATERIAL)
            obj = bpy.data.objects.new(spec["id"], mesh)
            bpy.context.scene.collection.objects.link(obj)
            bound = float(np.linalg.norm(r0["V"] - cen, axis=1).max())
            out_dir = os.environ.get("MN_PREVIEW_DIR") or os.path.join(ROOT, "art_src", "comets", "_preview")
            mncomet.render_preview(out_dir, spec["id"], obj, bound)
        if export and level is None:
            entries.append(export_fbx(spec, built, cen, os.path.join(ART, spec["id"] + ".fbx")))
    if entries:
        os.makedirs(RES, exist_ok=True)
        path = os.path.join(RES, "comets.json")
        old = {}
        if os.path.exists(path):
            old = {e["id"]: e for e in json.load(open(path)).get("models", [])}
        for e in entries:
            old[e["id"]] = e
        doc = dict(schema="monolith-comets/1", materialSet=MATERIAL_SET, material=MATERIAL, models=[old[k] for k in sorted(old)])
        with open(path, "w") as f:
            json.dump(doc, f, indent=1)
        log(f"wrote {path} ({len(doc['models'])} models)")


main()
