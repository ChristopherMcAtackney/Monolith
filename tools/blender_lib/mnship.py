"""mnship: a ship as the game sees it. Collects LOD meshes, named subsystems (with destroyed variants and debris), FX hooks,
hull damage sites, running lights and the collision proxy that a model script builds with mnmesh/mnkit, then writes them into
Blender objects, exports one FBX and a JSON manifest Unity reads.

Hierarchy in the FBX (names are the contract with Game/Assets/Monolith/Editor/ShipArtSetup.cs and Monolith.ShipArt):

    <id>                          root empty
      <id>_LOD0, _LOD1, _LOD2     the hull at three detail levels (LOD0 and LOD1 leave the subsystems out: they are separate, below)
      SUB_<kind>_<name>           an empty at the subsystem's pivot, +y = where it points
        SUB_..__<part>            intact parts (e.g. __base, __head), origin at the pivot
        SUB_..__damaged           optional: the half-wrecked look
        SUB_..__destroyed         the wreck: a stump, blackened and torn, in place of the intact parts
        SUB_..__debris_NN         detachable chunks; each has its own origin, ejection velocity and spin in the manifest
      FX_<kind>_<NN>              empties: where sparks, vents and fires may be spawned (+y = out of the hull)
      DMG_site_<NN>               hull damage sites: an empty on the hull (+y = outward normal) with a hidden breach mesh below it
        DMG_site_<NN>__breach
      <id>_COL                    collision proxy (a few hundred triangles)

Ship space while building is Blender's (x right, y forward, z up). The manifest is written in Unity's ship space
(x right, y up, z forward): to_unity() is the one place that mapping lives.
"""
import json
import os

import numpy as np

from . import mnmesh

try:
    import bpy
    import mathutils
except ImportError:      # unit tests outside Blender
    bpy = None
    mathutils = None


def to_unity(v):
    v = np.asarray(v, np.float64)
    return [round(float(v[0]), 4), round(float(v[2]), 4), round(float(v[1]), 4)]


class SubsystemRecord:
    def __init__(self, name, kind, frame, radius, hp=1.0, tags=None):
        self.name, self.kind, self.frame, self.radius, self.hp = name, kind, frame, radius, hp
        self.tags = tags or []
        self.parts = {}          # variant -> {part name -> MeshBuilder (local to the frame)}; variants: intact, damaged, destroyed
        self.debris = []         # dicts: builder (local to its own origin), origin, velocity, spin (all in the subsystem's local axes)


class _SubCtx:
    """Handed to model code inside ``ctx.subsystem(...)``. Draw in SHIP space as usual; ``part()`` / ``variant()`` /
    ``debris_piece()`` say which object the geometry belongs to. At LOD2 everything intact goes into the hull; at LOD1 the
    subsystem is skipped (check ``sb.lod``); at LOD0 each part is its own object."""

    def __init__(self, lodctx, rec):
        self._lod, self.rec = lodctx, rec
        self.lod = lodctx.lod
        self._throwaway = mnmesh.MeshBuilder("skip")

    def part(self, name="base", variant="intact"):
        if self.lod == 2:
            return self._lod.hull if variant == "intact" else self._throwaway
        if self.lod == 1:
            return self._throwaway
        return self.rec.parts.setdefault(variant, {}).setdefault(name, mnmesh.MeshBuilder(f"{self.rec.name}__{name}"))

    def variant(self, variant, name=None):
        """The builder for the 'damaged' or 'destroyed' look (one mesh per variant unless a name is given)."""
        return self.part(name or variant, variant)

    def debris_piece(self, builder_local, origin, velocity, spin=(0.0, 0.0, 0.0)):
        """A detachable chunk: ``builder_local`` is its geometry relative to ``origin`` (all in the subsystem's local axes)."""
        if self.lod == 0:
            self.rec.debris.append({"builder": builder_local, "origin": np.asarray(origin, np.float64), "velocity": np.asarray(velocity, np.float64),
                                    "spin": np.asarray(spin, np.float64)})

    @property
    def frame(self):
        return self.rec.frame


class LodContext:
    def __init__(self, ship, lod):
        self.ship, self.lod = ship, lod
        self.hull = mnmesh.MeshBuilder(f"{ship.id}_LOD{lod}")
        self.subsystems = []

    class _Scope:
        def __init__(self, ctx, rec):
            self.ctx, self.rec = ctx, rec

        def __enter__(self):
            self.sub = _SubCtx(self.ctx, self.rec)
            return self.sub

        def __exit__(self, exc_type, exc, tb):
            if exc_type is None:
                self.ctx.subsystems.append(self.rec)
            return False

    def subsystem(self, name, kind, frame, radius, hp=1.0, tags=None):
        """Declare a subsystem (turret, engine, bridge ...). ``frame`` = the pivot (its y axis is where it points)."""
        rec = SubsystemRecord(f"SUB_{kind}_{name}", kind, frame, radius, hp, tags)
        return LodContext._Scope(self, rec)


class Ship:
    def __init__(self, ship_id, display_name, out_dir, materials, seed=1, manifest_dir=None, material_set=None):
        self.id = ship_id
        self.name = display_name
        self.out_dir = out_dir
        self.manifest_dir = manifest_dir or out_dir
        self.material_set = material_set
        self.materials = list(materials)
        self.seed = seed
        self.lods = {}
        self.collision = mnmesh.MeshBuilder(f"{ship_id}_COL")
        self.lights = []         # dicts: pos (Unity ship space), kind, colour, size, blink
        self.hooks = []          # dicts: kind, frame, size
        self.sites = []          # dicts: frame, radius, breach MeshBuilder (ship space)
        self.extra = {}

    def mat(self, name):
        return self.materials.index(name)

    def build_lod(self, lod, fn):
        ctx = LodContext(self, lod)
        fn(ctx)
        self.lods[lod] = ctx
        return ctx

    def add_light(self, pos, kind, colour, size=0.5, blink=None):
        self.lights.append({"pos": to_unity(pos), "kind": kind, "colour": colour, "size": size, "blink": blink})

    def add_hook(self, kind, frame, size=1.0):
        """An FX hook: an empty named FX_<kind>_<NN> with +y pointing out of the hull. Sparks, vents, smoke and fires spawn here."""
        self.hooks.append({"kind": kind, "frame": frame, "size": size})

    def add_site(self, frame, radius, breach):
        """A hull damage site: DMG_site_<NN> at ``frame`` (+y = the outward normal). ``breach`` (MeshBuilder, ship space) is the torn-open look
        shown when the site is badly hit."""
        self.sites.append({"frame": frame, "radius": radius, "breach": breach})

    def check_geometry(self):
        """Winding versus normals, degenerate triangles: logged, and an error if a primitive got its winding backwards."""
        problems = 0
        for lod, ctx in sorted(self.lods.items()):
            bad, deg = ctx.hull.check_winding()
            print(f"[MN] check LOD{lod}: {bad} wrongly wound, {deg} degenerate triangles")
            problems += bad
            for rec in ctx.subsystems:
                for variant, parts in rec.parts.items():
                    for pname, mb in parts.items():
                        b, d = mb.check_winding()
                        if b:
                            print(f"[MN] check {rec.name}/{variant}/{pname}: {b} wrongly wound")
                            problems += b
        if problems:
            raise RuntimeError(f"{problems} triangles are wound against their normals")

    # -- Blender ------------------------------------------------------------------------------------------------------
    def _blender_mesh(self, mb, name, mats):
        d = mb.finalize()
        pos, nrm, uv0, uv1 = d["pos"], d["nrm"], d["uv0"], d["uv1"]
        tris_all, mat_ids = [], []
        for m, t in sorted(d["tris"].items()):
            tris_all.append(t)
            mat_ids.append(np.full(len(t), m, np.int32))
        mesh = bpy.data.meshes.new(name)
        if not tris_all:
            return mesh
        tris = np.concatenate(tris_all)
        midx = np.concatenate(mat_ids)
        n = len(pos)
        mesh.vertices.add(n)
        mesh.vertices.foreach_set("co", pos.reshape(-1).astype(np.float32))
        m = len(tris)
        mesh.loops.add(m * 3)
        mesh.loops.foreach_set("vertex_index", tris.reshape(-1).astype(np.int32))
        mesh.polygons.add(m)
        mesh.polygons.foreach_set("loop_start", (np.arange(m, dtype=np.int32) * 3))
        mesh.polygons.foreach_set("material_index", midx)
        mesh.polygons.foreach_set("use_smooth", np.ones(m, dtype=bool))
        mesh.update(calc_edges=True)
        uvl = mesh.uv_layers.new(name="UVMap")
        uvl.data.foreach_set("uv", uv0[tris.reshape(-1)].reshape(-1).astype(np.float32))
        uvd = mesh.uv_layers.new(name="UVDetail")
        uvd.data.foreach_set("uv", uv1[tris.reshape(-1)].reshape(-1).astype(np.float32))
        mesh.normals_split_custom_set_from_vertices([tuple(x) for x in nrm])
        for mname in mats:
            mesh.materials.append(mname)
        return mesh

    def _bl_materials(self):
        return [bpy.data.materials.get(n) or bpy.data.materials.new(n) for n in self.materials]

    @staticmethod
    def _matrix(f):
        return mathutils.Matrix(((f.r[0], f.f[0], f.u[0], f.o[0]), (f.r[1], f.f[1], f.u[1], f.o[1]),
                                 (f.r[2], f.f[2], f.u[2], f.o[2]), (0, 0, 0, 1)))

    def export(self, fbx_name=None):
        """Create the Blender objects and write <out_dir>/<id>.fbx and <manifest_dir>/<id>.ship.json."""
        os.makedirs(self.out_dir, exist_ok=True)
        os.makedirs(self.manifest_dir, exist_ok=True)
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bl_mats = self._bl_materials()
        root = bpy.data.objects.new(self.id, None)
        bpy.context.scene.collection.objects.link(root)

        def add_obj(name, mb, parent, local_at=None):
            mesh = self._blender_mesh(mb, name, bl_mats)
            obj = bpy.data.objects.new(name, mesh)
            bpy.context.scene.collection.objects.link(obj)
            obj.parent = parent
            if local_at is not None:
                obj.location = tuple(local_at)
            return obj

        def add_empty(name, f, props=None):
            e = bpy.data.objects.new(name, None)
            bpy.context.scene.collection.objects.link(e)
            e.parent = root
            e.matrix_world = self._matrix(f)
            e.empty_display_type = "ARROWS"
            for k, v in (props or {}).items():
                e[k] = v
            return e

        stats = {"lods": {}, "subsystems": [], "hooks": [], "damageSites": [], "lights": self.lights}
        self.check_geometry()
        for lod, ctx in sorted(self.lods.items()):
            add_obj(f"{self.id}_LOD{lod}", ctx.hull, root)
            stats["lods"][str(lod)] = {"triangles": ctx.hull.tri_count, "vertices": ctx.hull.vert_count}
        lod0 = self.lods[0]
        sub_tris = {"intact": 0, "destroyed": 0, "debris": 0}
        for rec in lod0.subsystems:
            f = rec.frame
            empty = add_empty(rec.name, f, {"mn_kind": rec.kind, "mn_radius": float(rec.radius), "mn_hp": float(rec.hp)})
            info = {"name": rec.name, "kind": rec.kind, "radius": rec.radius, "hp": rec.hp, "tags": rec.tags,
                    "position": to_unity(f.o), "forward": to_unity(f.f), "up": to_unity(f.u), "parts": {}, "debris": []}
            for variant, parts in rec.parts.items():
                for pname, mb in parts.items():
                    objname = f"{rec.name}__{pname}"
                    add_obj(objname, mb.to_local(f), empty)
                    info["parts"].setdefault(variant, []).append(objname)
                    sub_tris["destroyed" if variant == "destroyed" else "intact"] += mb.tri_count
            for i, d in enumerate(rec.debris):
                objname = f"{rec.name}__debris_{i:02d}"
                add_obj(objname, d["builder"], empty, local_at=d["origin"])
                sub_tris["debris"] += d["builder"].tri_count
                info["debris"].append({"name": objname, "position": to_unity(d["origin"]), "velocity": to_unity(d["velocity"]), "spin": to_unity(d["spin"])})
            stats["subsystems"].append(info)
        stats["lods"]["0"]["subsystemTriangles"] = sub_tris["intact"]
        stats["subsystemTriangles"] = sub_tris
        for i, h in enumerate(self.hooks):
            name = f"FX_{h['kind']}_{i:02d}"
            add_empty(name, h["frame"], {"mn_kind": h["kind"], "mn_size": float(h["size"])})
            stats["hooks"].append({"name": name, "kind": h["kind"], "size": h["size"], "position": to_unity(h["frame"].o), "direction": to_unity(h["frame"].f)})
        for i, s in enumerate(self.sites):
            name = f"DMG_site_{i:02d}"
            e = add_empty(name, s["frame"], {"mn_radius": float(s["radius"])})
            add_obj(f"{name}__breach", s["breach"].to_local(s["frame"]), e)
            stats["damageSites"].append({"name": name, "radius": s["radius"], "position": to_unity(s["frame"].o), "normal": to_unity(s["frame"].f),
                                         "breachTriangles": s["breach"].tri_count})
        col = self.collision
        add_obj(f"{self.id}_COL", col, root)
        stats["collisionTriangles"] = col.tri_count
        clo, chi = col.bounds()
        hlo, hhi = lod0.hull.bounds()
        lo, hi = np.minimum(hlo, clo), np.maximum(hhi, chi)
        size = hi - lo
        stats["bounds"] = {"min": to_unity(lo), "max": to_unity(hi), "size": to_unity(size), "length": round(float(size[1]), 2)}
        stats["id"] = self.id
        stats["name"] = self.name
        stats["materials"] = self.materials
        if self.material_set:
            stats["materialSet"] = self.material_set
        stats.update(self.extra)

        bpy.ops.object.select_all(action="DESELECT")
        for o in bpy.data.objects:
            o.select_set(True)
        fbx = os.path.join(self.out_dir, (fbx_name or self.id) + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=fbx, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options=FBX_SCALE,
            axis_forward=FBX_AXIS_FORWARD, axis_up=FBX_AXIS_UP, bake_space_transform=FBX_BAKE,
            object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True, mesh_smooth_type="OFF", use_tspace=False,
            add_leaf_bones=False, path_mode="STRIP", embed_textures=False, use_custom_props=True)
        with open(os.path.join(self.manifest_dir, self.id + ".ship.json"), "w") as f:
            json.dump(stats, f, indent=1)
        print(f"[MN] exported {fbx} ({os.path.getsize(fbx) / 1e6:.1f} MB): " +
              ", ".join(f"LOD{k} {v['triangles']} tris" for k, v in stats["lods"].items()) +
              f"; subsystems {len(lod0.subsystems)} ({sub_tris['intact']} intact / {sub_tris['destroyed']} destroyed / {sub_tris['debris']} debris tris), "
              f"{len(self.hooks)} hooks, {len(self.sites)} damage sites, collision {col.tri_count}, {len(self.lights)} lights")
        return stats


# FBX export settings, verified in Unity 2022.3 by probing a marked test ship: Blender's own axes are written untouched
# (forward Y, up Z, no baking) and the importer's "Bake Axis Conversion" (ShipArtImport sets it) turns that into an identity
# root with the bow on Unity's +z, starboard on +x and up on +y. Other combinations leave a 180 degree root or scale 100.
FBX_AXIS_FORWARD = "Y"
FBX_AXIS_UP = "Z"
FBX_BAKE = False
FBX_SCALE = "FBX_SCALE_ALL"
