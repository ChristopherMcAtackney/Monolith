"""mncabin: the cabin kit. A cabin (or the pilot's hands) is a set of named objects built from plain numpy meshes, exported as one FBX plus a
layout JSON that the game reads without Blender (art_src/PIPELINE.md, "The cabin"). Builds in Blender's axes (x right, y forward, z up); the
layout is written in Unity's (x right, y up, z forward), through ``to_unity`` (the one place that mapping lives).

    asset = CabinAsset("cabin", materials, out_dir, res_dir, material_set)
    shell = asset.mesh("CABIN_SHELL")                         # geometry is drawn in cabin space
    lever = asset.mesh("BREAKER_LEVER", pivot=Frame(...))    # a moving part: stored relative to its pivot, which is the object's transform
    asset.empty("ANCHOR_seat_eye", Frame(...))                # a point the game binds to
    asset.export(layout)

Geometry helpers (all take a MeshBuilder first): ``loft`` (hard or smooth surfaces through rings of points), ``tube`` (swept round section),
``bevel_box`` (a box chamfered on every edge), ``cyl`` (a tapered cylinder between two points), ``torus``, ``plate`` (a flat quad on a trim-sheet
rectangle), ``noise2`` (smooth value noise for organic displacement).
"""
import json
import math
import os

import numpy as np

from . import mnmesh
from .mnmesh import Frame, MeshBuilder, norm, cross

try:
    import bpy
    import mathutils
except ImportError:      # unit tests outside Blender
    bpy = None
    mathutils = None


def to_unity(v):
    v = np.asarray(v, np.float64)
    return [round(float(v[0]), 4), round(float(v[2]), 4), round(float(v[1]), 4)]


def f_unity(f):
    """A frame as Unity sees it: position, forward (Blender y), up (Blender z)."""
    return {"pos": to_unity(f.o), "forward": to_unity(f.f), "up": to_unity(f.u)}


# ------------------------------------------------------------------------------------------------ geometry helpers

def poly3(poly_xz, y, cx=0.0, cz=0.0):
    """An (x, z) polygon at height ``y`` along the cabin's length, as 3-D points (N, 3)."""
    p = np.asarray(poly_xz, np.float64)
    return np.stack([p[:, 0] + cx, np.full(len(p), y, np.float64), p[:, 1] + cz], axis=1)


def _cum(points, closed):
    d = np.linalg.norm(np.roll(points, -1, axis=0) - points, axis=1) if closed else np.linalg.norm(np.diff(points, axis=0), axis=1)
    return np.r_[0.0, np.cumsum(d)]


def loft(mb, rings, mat, flip=False, tile=1.0, detail=0.5, smooth=False, closed=True, uoff=0.0, voff=0.0, uscale=1.0, vscale=1.0):
    """Quads between consecutive rings of points. ``rings`` (S, N, 3): each ring a closed loop (or an open row with closed=False) with the same
    number of points. A ring runs counterclockwise about the direction of travel (the polygons in this kit run top, right, bottom, left seen
    with x right and z up when travelling +y), which makes the normals point OUT; ``flip`` makes them point in (the inside of a room).
    Hard: each quad is flat. Smooth: shared vertices and averaged normals. UV0 is metric (metres / ``tile``), UV1 metric (metres / ``detail``)."""
    R = np.asarray(rings, np.float64)
    S, N, _ = R.shape
    if S < 2:
        return
    ucum = _cum(R[0], closed)                                     # along a ring
    cent = R.mean(axis=1)
    vcum = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(cent, axis=0), axis=1))]
    M = N if closed else N - 1
    j0 = np.arange(M)
    j1 = (j0 + 1) % N
    sgn = -1.0 if flip else 1.0
    if not smooth:
        P = np.empty((S - 1, M, 4, 3))
        U0 = np.empty((S - 1, M, 4, 2))
        for s in range(S - 1):
            a, b = R[s], R[s + 1]
            P[s, :, 0] = a[j0]; P[s, :, 1] = a[j1]; P[s, :, 2] = b[j1]; P[s, :, 3] = b[j0]
            u0, u1 = ucum[j0], ucum[j0 + 1]
            v0, v1 = vcum[s], vcum[s + 1]
            U0[s, :, 0] = np.stack([u0, np.full(M, v0)], 1)
            U0[s, :, 1] = np.stack([u1, np.full(M, v0)], 1)
            U0[s, :, 2] = np.stack([u1, np.full(M, v1)], 1)
            U0[s, :, 3] = np.stack([u0, np.full(M, v1)], 1)
        P = P.reshape(-1, 4, 3)
        U0 = U0.reshape(-1, 4, 2)
        N_ = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1])) * sgn
        if flip:
            P = P[:, ::-1]
            U0 = U0[:, ::-1]
        uv0 = np.stack([U0[..., 0] * uscale / tile + uoff, U0[..., 1] * vscale / tile + voff], axis=-1)
        uv1 = np.stack([U0[..., 0] / detail, U0[..., 1] / detail], axis=-1)
        mb.quads(P, uv0, uv1, mat, N_)
        return
    # smooth: a vertex grid with normals from central differences
    cols = N + 1 if closed else N
    grid = np.empty((S, cols, 3))
    grid[:, :N] = R
    if closed:
        grid[:, N] = R[:, 0]
    du = np.gradient(grid, axis=1)
    if closed:
        du[:, 0] = R[:, 1] - R[:, -1]
        du[:, N] = du[:, 0]
    dv = np.gradient(grid, axis=0)
    nrm = norm(cross(du, dv)) * sgn
    ucol = np.r_[ucum[:N], ucum[-1]] if closed else ucum
    uu, vv = np.meshgrid(ucol, vcum)
    uv0 = np.stack([uu * uscale / tile + uoff, vv * vscale / tile + voff], axis=-1).reshape(-1, 2)
    uv1 = np.stack([uu / detail, vv / detail], axis=-1).reshape(-1, 2)
    idx = np.arange(S * cols).reshape(S, cols)
    a, b, c, d = idx[:-1, :-1], idx[:-1, 1:], idx[1:, 1:], idx[1:, :-1]
    quads = np.stack([a, b, c, d], axis=-1).reshape(-1, 4)
    if flip:
        quads = quads[:, ::-1]
    tris = np.concatenate([quads[:, [0, 1, 2]], quads[:, [0, 2, 3]]], axis=0)
    mb.add_raw(grid.reshape(-1, 3), nrm.reshape(-1, 3), uv0, uv1, tris, mat)


def fan_cap(mb, ring, normal, mat, tile=1.0, centre=None):
    """A flat polygon over a closed ring of points, as a fan round its centre. ``normal`` is the direction it faces."""
    ring = np.asarray(ring, np.float64)
    k = len(ring)
    c = ring.mean(axis=0) if centre is None else np.asarray(centre, np.float64)
    n = norm(normal)
    a = np.repeat(c[None], k, 0)
    b = ring
    cc = np.roll(ring, -1, axis=0)
    fn = norm(cross(b - a, cc - a))
    if np.mean(np.einsum("ij,j->i", fn, n)) < 0:
        b, cc = cc, b
    ex = norm(cross(n, np.array([0.0, 0.0, 1.0]) if abs(n[2]) < 0.9 else np.array([1.0, 0.0, 0.0])))
    ey = cross(n, ex)

    def uv(p):
        rel = p - c
        return np.stack([rel @ ex, rel @ ey], axis=-1)
    pos = np.stack([a, b, cc], axis=1).reshape(-1, 3)
    nrm = np.repeat(n[None], len(pos), 0)
    uv0 = np.stack([uv(a), uv(b), uv(cc)], axis=1).reshape(-1, 2) / tile
    tris = (np.arange(k)[:, None] * 3 + np.array([0, 1, 2])).reshape(-1, 3)
    mb.add_raw(pos, nrm, uv0, uv0 * (tile / 0.5), tris, mat)


def bevel_box(mb, frame, size, mat, bevel=0.012, centre=(0, 0, 0), tile=1.0, detail=0.5, caps=True, cap_mat=None, flip=False):
    """A box (sx, sy, sz) in ``frame`` (local y is its length) chamfered on every edge by ``bevel``. Hard-edged: every flat face and chamfer is
    its own quad, so the edges catch light. ``caps`` False leaves both ends open (for a box that sits on a wall)."""
    sx, sy, sz = size
    b = min(bevel, sx * 0.45, sy * 0.45, sz * 0.45)
    hw, hh = sx * 0.5, sz * 0.5
    fr = Frame(frame.p(*centre), frame.f, frame.u)
    full = mnmesh.chamfered_rect(hw, hh, b)
    small = mnmesh.chamfered_rect(max(hw - b, 1e-4), max(hh - b, 1e-4), b * 0.5)
    ys = [-sy * 0.5, -sy * 0.5 + b, sy * 0.5 - b, sy * 0.5]
    polys = [small, full, full, small]
    rings = np.array([[fr.p(x, y, z) for x, z in p] for p, y in zip(polys, ys)])
    loft(mb, rings, mat, flip=flip, tile=tile, detail=detail)
    if caps:
        cm = mat if cap_mat is None else cap_mat
        fan_cap(mb, rings[0], -fr.f, cm, tile)
        fan_cap(mb, rings[3], fr.f, cm, tile)


def tube(mb, path, radius, segs, mat, caps=True, smooth=True, tile=0.5, detail=0.25, flip=False, twist=0.0, closed_path=False):
    """A round section swept along ``path`` (K, 3) with parallel-transport frames. ``radius`` is a number or one per point."""
    path = np.asarray(path, np.float64)
    K = len(path)
    rad = np.full(K, float(radius)) if np.isscalar(radius) else np.asarray(radius, np.float64)
    tang = np.zeros_like(path)
    tang[1:-1] = path[2:] - path[:-2]
    tang[0] = path[1] - path[0]
    tang[-1] = path[-1] - path[-2]
    tang = norm(tang)
    ref = np.array([0.0, 0.0, 1.0]) if abs(tang[0][2]) < 0.9 else np.array([1.0, 0.0, 0.0])
    n1 = norm(ref - tang[0] * np.dot(ref, tang[0]))
    ang = np.linspace(0, 2 * math.pi, segs, endpoint=False)
    rings = np.empty((K, segs, 3))
    for i in range(K):
        if i > 0:
            n1 = n1 - tang[i] * np.dot(n1, tang[i])
            n1 = norm(n1)
        n2 = cross(tang[i], n1)                      # n1 x n2 = the tangent, so the ring runs counter-clockwise about it
        c, s = np.cos(ang + twist * i), np.sin(ang + twist * i)
        rings[i] = path[i] + rad[i] * (np.outer(c, n1) + np.outer(s, n2))
    loft(mb, rings, mat, flip=flip, tile=tile, detail=detail, smooth=smooth)
    if caps:
        fan_cap(mb, rings[0], -tang[0], mat, tile)
        fan_cap(mb, rings[-1], tang[-1], mat, tile)


def cyl(mb, p0, p1, r0, r1=None, segs=12, mat=0, caps=True, smooth=True, tile=0.5, detail=0.25):
    """A (tapered) cylinder between two points."""
    r1 = r0 if r1 is None else r1
    tube(mb, np.array([p0, p1], np.float64), np.array([r0, r1]), segs, mat, caps=caps, smooth=smooth, tile=tile, detail=detail)


def torus(mb, frame, big_r, small_r, mat, segs=32, tube_segs=10, tile=0.5, detail=0.25, arc=360.0):
    """A ring about the frame's y axis (local x, z span the ring plane); ``arc`` less than 360 makes a bent bar with capped ends."""
    full = arc >= 359.9
    a = np.linspace(0, math.radians(arc), segs + 1)
    path = np.array([frame.p(big_r * math.sin(t), 0.0, big_r * math.cos(t)) for t in a])
    tube(mb, path, small_r, tube_segs, mat, caps=not full, smooth=True, tile=tile, detail=detail)


def plate(mb, origin, xdir, ydir, w, h, rect, mat, detail=0.5):
    """A flat quad on a trim-sheet rectangle (u0, v0, u1, v1; v up): ``origin`` is its bottom-left corner, ``xdir`` runs along its width, ``ydir`` up its
    height (both unit). Faces along xdir x ydir."""
    xd, yd = norm(xdir), norm(ydir)
    o = np.asarray(origin, np.float64)
    mb.quad_rect(o, o + xd * w, o + xd * w + yd * h, o + yd * h, rect, mat, detail=detail)


def box_ring(mb, frame, size, wall, mat, depth=None, bevel=0.01, tile=1.0):
    """A rectangular frame (a bezel): outer size (sx, sz) and wall thickness, ``depth`` along the frame's y. Four bevel boxes."""
    sx, sz = size[0], size[1]
    d = depth if depth is not None else 0.03
    bevel_box(mb, frame, (sx, d, wall), mat, bevel, centre=(0, 0, sz * 0.5 - wall * 0.5), tile=tile)
    bevel_box(mb, frame, (sx, d, wall), mat, bevel, centre=(0, 0, -sz * 0.5 + wall * 0.5), tile=tile)
    bevel_box(mb, frame, (wall, d, sz - 2 * wall), mat, bevel, centre=(sx * 0.5 - wall * 0.5, 0, 0), tile=tile)
    bevel_box(mb, frame, (wall, d, sz - 2 * wall), mat, bevel, centre=(-sx * 0.5 + wall * 0.5, 0, 0), tile=tile)


def quad_face(mb, frame, w, h, mat, uv_rect=None, centre=(0, 0, 0), detail=0.5, flip=False, tile=1.0):
    """A flat quad in the frame's x-z plane facing the frame's -y (toward a viewer looking along +y at it from the front), covering the full uv rect
    (default 0..1): displays use it, so the RenderTexture fills the quad. ``flip`` faces +y."""
    c = frame.p(*centre)
    r, u = frame.r, frame.u
    p0 = c - r * (w * 0.5) - u * (h * 0.5)
    p1 = c + r * (w * 0.5) - u * (h * 0.5)
    p2 = c + r * (w * 0.5) + u * (h * 0.5)
    p3 = c - r * (w * 0.5) + u * (h * 0.5)
    rect = uv_rect if uv_rect is not None else (0.0, 0.0, 1.0, 1.0)
    if flip:
        mb.quad_rect(p1, p0, p3, p2, (rect[2], rect[1], rect[0], rect[3]), mat, detail=detail)
    else:
        mb.quad_rect(p0, p1, p2, p3, rect, mat, detail=detail)


def noise2(x, y, seed=0, scale=1.0):
    """Smooth value noise in -1..1 at points (x, y) (arrays), by bilinear-smoothstep interpolation of a hashed lattice."""
    x = np.asarray(x, np.float64) * scale
    y = np.asarray(y, np.float64) * scale
    xi, yi = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    xf, yf = x - xi, y - yi
    sx, sy = xf * xf * (3 - 2 * xf), yf * yf * (3 - 2 * yf)

    def h(ix, iy):
        n = (ix * 374761393 + iy * 668265263 + seed * 1442695041) & 0xFFFFFFFF
        n = ((n ^ (n >> 13)) * 1274126177) & 0xFFFFFFFF
        return ((n ^ (n >> 16)) & 0xFFFF) / 32767.5 - 1.0
    a, b, c, d = h(xi, yi), h(xi + 1, yi), h(xi, yi + 1), h(xi + 1, yi + 1)
    return (a * (1 - sx) + b * sx) * (1 - sy) + (c * (1 - sx) + d * sx) * sy


# ------------------------------------------------------------------------------------------------ the asset

class _Node:
    def __init__(self, name, kind, builder=None, frame=None, parent=None, props=None):
        self.name, self.kind, self.mb, self.frame, self.parent, self.props = name, kind, builder, frame, parent, props or {}


class CabinAsset:
    def __init__(self, asset_id, materials, out_dir, res_dir, material_set):
        self.id = asset_id
        self.materials = list(materials)
        self.out_dir, self.res_dir, self.material_set = out_dir, res_dir, material_set
        self.nodes = {}
        self.order = []

    def mat(self, name):
        return self.materials.index(name)

    def mesh(self, name, pivot=None, parent=None, **props):
        """The builder of a named mesh object (made on first use). ``pivot`` makes the object's own transform that frame and stores its geometry
        relative to it (a hinge, a lever, a module that slides); geometry is always drawn in cabin space."""
        n = self.nodes.get(name)
        if n is None:
            n = _Node(name, "mesh", MeshBuilder(name), pivot, parent, props)
            self.nodes[name] = n
            self.order.append(name)
        return n.mb

    def empty(self, name, frame, parent=None, **props):
        n = _Node(name, "empty", None, frame, parent, props)
        self.nodes[name] = n
        self.order.append(name)
        return n

    def _blender_mesh(self, mb, name, bl_mats):
        d = mb.finalize()
        pos, nrm, uv0, uv1 = d["pos"], d["nrm"], d["uv0"], d["uv1"]
        mesh = bpy.data.meshes.new(name)
        if not d["tris"]:
            return mesh, 0
        used = sorted(d["tris"])
        tris_all, mat_ids = [], []
        for slot, m in enumerate(used):
            t = d["tris"][m]
            tris_all.append(t)
            mat_ids.append(np.full(len(t), slot, np.int32))
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
        for m_ in used:
            mesh.materials.append(bl_mats[m_])
        return mesh, int(len(tris))

    @staticmethod
    def _matrix(f):
        return mathutils.Matrix(((f.r[0], f.f[0], f.u[0], f.o[0]), (f.r[1], f.f[1], f.u[1], f.o[1]),
                                 (f.r[2], f.f[2], f.u[2], f.o[2]), (0, 0, 0, 1)))

    def check(self):
        bad_total = 0
        for name in self.order:
            n = self.nodes[name]
            if n.kind != "mesh":
                continue
            bad, deg = n.mb.check_winding()
            if bad:
                print(f"[MN] check {name}: {bad} wrongly wound triangles")
            bad_total += bad
        if bad_total:
            raise RuntimeError(f"{bad_total} triangles are wound against their normals")

    def export(self, layout=None, fbx_name=None):
        os.makedirs(self.out_dir, exist_ok=True)
        os.makedirs(self.res_dir, exist_ok=True)
        self.check()
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bl_mats = [bpy.data.materials.get(n) or bpy.data.materials.new(n) for n in self.materials]
        root = bpy.data.objects.new(self.id, None)
        bpy.context.scene.collection.objects.link(root)
        objs = {}
        stats = {"objects": {}, "triangles": 0}
        for name in self.order:
            n = self.nodes[name]
            if n.kind == "mesh":
                mb = n.mb.to_local(n.frame) if n.frame is not None else n.mb
                mesh, tri = self._blender_mesh(mb, name, bl_mats)
                obj = bpy.data.objects.new(name, mesh)
                stats["objects"][name] = tri
                stats["triangles"] += tri
            else:
                obj = bpy.data.objects.new(name, None)
                obj.empty_display_type = "ARROWS"
                for k, v in n.props.items():
                    obj[k] = v
            bpy.context.scene.collection.objects.link(obj)
            obj.parent = objs[n.parent] if n.parent else root
            if n.frame is not None:
                obj.matrix_world = self._matrix(n.frame)
            for k, v in (n.props or {}).items():
                if n.kind == "mesh":
                    obj[k] = v
            objs[name] = obj
        bpy.ops.object.select_all(action="DESELECT")
        for o in bpy.data.objects:
            o.select_set(True)
        fbx = os.path.join(self.out_dir, (fbx_name or self.id) + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=fbx, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="Y", axis_up="Z", bake_space_transform=False,
            object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True, mesh_smooth_type="OFF", use_tspace=False,
            add_leaf_bones=False, path_mode="STRIP", embed_textures=False, use_custom_props=True)
        out = dict(layout or {})
        out["id"] = self.id
        out["materials"] = self.materials
        out["materialSet"] = self.material_set
        out["stats"] = stats
        out["nodes"] = {name: ({"kind": n.kind, "parent": n.parent, **({"frame": f_unity(n.frame)} if n.frame is not None else {})}) for name, n in self.nodes.items()}
        with open(os.path.join(self.res_dir, self.id + ".layout.json"), "w") as f:
            json.dump(out, f, indent=1)
        print(f"[MN] exported {fbx} ({os.path.getsize(fbx) / 1e6:.1f} MB): {len(self.nodes)} nodes, {stats['triangles']} triangles")
        return out
