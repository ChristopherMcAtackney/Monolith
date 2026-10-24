"""mnmesh: geometry as plain numpy arrays, with explicit normals and two UV sets, plus hard-surface primitives.

Ship space is Blender's: x right (starboard), y forward (the bow), z up. Nothing here imports bpy, so it can be unit
tested; mnship turns a MeshBuilder into a Blender object.

Meshes are not welded: every flat face owns its vertices, so edges are hard by construction; smooth surfaces (lathes) carry
their own normals. UV0 is whatever the material wants (metric tile coordinates for tiling textures, an atlas rectangle for
trim pieces). UV1 is always metric (metres / detail size) so one tiling detail map works on everything.
"""
import math

import numpy as np

EPS = 1e-9


def norm(v):
    v = np.asarray(v, np.float64)
    return v / (np.linalg.norm(v, axis=-1, keepdims=True) + EPS)


def cross(a, b):
    return np.cross(a, b)


class Frame:
    """A local frame: x right, y forward, z up (all unit, orthogonal). ``p(x, y, z)`` maps local to ship space."""

    def __init__(self, origin=(0, 0, 0), fwd=(0, 1, 0), up=(0, 0, 1)):
        f = norm(fwd)
        u = np.asarray(up, np.float64)
        u = norm(u - f * np.dot(u, f))
        self.o = np.asarray(origin, np.float64)
        self.f = f
        self.u = u
        self.r = cross(f, u)

    def p(self, x=0.0, y=0.0, z=0.0):
        return self.o + self.r * x + self.f * y + self.u * z

    def d(self, x=0.0, y=0.0, z=0.0):
        return self.r * x + self.f * y + self.u * z

    def matrix(self):
        return np.stack([self.r, self.f, self.u], axis=1)    # columns: local x, y, z in ship space

    def moved(self, x=0.0, y=0.0, z=0.0):
        fr = Frame(self.p(x, y, z), self.f, self.u)
        return fr

    def rotated(self, deg, axis="z"):
        """A new frame rotated about its own axis (z: yaw, x: pitch, y: roll)."""
        a = math.radians(deg)
        c, s = math.cos(a), math.sin(a)
        if axis == "z":
            return Frame(self.o, self.f * c + self.r * s, self.u)
        if axis == "x":
            return Frame(self.o, self.f * c + self.u * s, self.u * c - self.f * s)
        if axis == "y":
            return Frame(self.o, self.f, self.u * c + self.r * s)
        raise ValueError(axis)


IDENTITY = Frame()


def chamfered_rect(hw, hh, c):
    """8-point chamfered rectangle (x, z), clockwise seen from +y with x right, z up. Edges, in order:
    tr, r, br, b, bl, l, tl, t (top-right chamfer, right flat, ... top flat)."""
    c = min(c, hw * 0.99, hh * 0.99)
    return np.array([
        (hw - c, hh), (hw, hh - c), (hw, -(hh - c)), (hw - c, -hh),
        (-(hw - c), -hh), (-hw, -(hh - c)), (-hw, hh - c), (-(hw - c), hh)], np.float64)


EDGE_NAMES = ["tr", "r", "br", "b", "bl", "l", "tl", "t"]


def regular_poly(n, r, phase=0.0, sx=1.0, sz=1.0):
    a = phase + np.arange(n) * (-2 * math.pi / n)
    return np.stack([np.sin(a) * r * sx, np.cos(a) * r * sz], axis=1)


def _inside(poly, x, z):
    """Point in polygon (ray casting) for (x, z) polygon points."""
    inside = False
    n = len(poly)
    for i in range(n):
        x0, z0 = poly[i]
        x1, z1 = poly[(i + 1) % n]
        if (z0 > z) != (z1 > z) and x < (x1 - x0) * (z - z0) / (z1 - z0 + 1e-12) + x0:
            inside = not inside
    return inside


class Patch:
    """A bilinear surface patch with corners p00 (u0, v0), p10 (u1, v0), p11, p01; u runs along the section, v along +y.
    The outward normal is u x v."""

    def __init__(self, p00, p10, p11, p01, label=""):
        self.p00, self.p10, self.p11, self.p01 = (np.asarray(p, np.float64) for p in (p00, p10, p11, p01))
        self.label = label

    def pt(self, u, v):
        u = np.asarray(u, np.float64)[..., None]
        v = np.asarray(v, np.float64)[..., None]
        return (1 - u) * (1 - v) * self.p00 + u * (1 - v) * self.p10 + u * v * self.p11 + (1 - u) * v * self.p01

    @property
    def lu(self):
        return 0.5 * (np.linalg.norm(self.p10 - self.p00) + np.linalg.norm(self.p11 - self.p01))

    @property
    def lv(self):
        return 0.5 * (np.linalg.norm(self.p01 - self.p00) + np.linalg.norm(self.p11 - self.p10))

    def normal(self, u=0.5, v=0.5):
        eu = self.pt(min(u + 0.01, 1), v) - self.pt(max(u - 0.01, 0), v)
        ev = self.pt(u, min(v + 0.01, 1)) - self.pt(u, max(v - 0.01, 0))
        return norm(cross(eu, ev))

    def eu(self, u=0.5, v=0.5):
        return norm(self.pt(min(u + 0.01, 1), v) - self.pt(max(u - 0.01, 0), v))

    def ev(self, u=0.5, v=0.5):
        return norm(self.pt(u, min(v + 0.01, 1)) - self.pt(u, max(v - 0.01, 0)))

    def frame_at(self, u, v):
        """A Frame at the patch point: local x = along u, local y = along v, local z = the normal (up out of the surface)."""
        n = self.normal(u, v)
        ev = self.ev(u, v)
        return Frame(self.pt(u, v), ev, n)


class MeshBuilder:
    def __init__(self, name="mesh"):
        self.name = name
        self._chunks = []

    # -- raw ------------------------------------------------------------------------------------------------------
    def add_raw(self, pos, nrm, uv0, uv1, tris, mat):
        pos = np.asarray(pos, np.float32).reshape(-1, 3)
        if len(pos) == 0:
            return
        n = len(pos)
        self._chunks.append((pos, np.asarray(nrm, np.float32).reshape(n, 3), np.asarray(uv0, np.float32).reshape(n, 2),
                             np.asarray(uv1, np.float32).reshape(n, 2), np.asarray(tris, np.int64).reshape(-1, 3), int(mat)))

    @property
    def tri_count(self):
        return int(sum(len(c[4]) for c in self._chunks))

    @property
    def vert_count(self):
        return int(sum(len(c[0]) for c in self._chunks))

    def is_empty(self):
        return not self._chunks

    def merge(self, other):
        self._chunks.extend(other._chunks)

    def transformed_copy(self, frame, name=None):
        """A copy placed by ``frame`` (local coordinates become ship space)."""
        m = MeshBuilder(name or self.name)
        M = frame.matrix()
        for pos, nrm, uv0, uv1, tris, mat in self._chunks:
            m._chunks.append((((pos.astype(np.float64) @ M.T) + frame.o).astype(np.float32),
                              (nrm.astype(np.float64) @ M.T).astype(np.float32), uv0, uv1, tris, mat))
        return m

    def check_winding(self):
        """Counts triangles whose winding disagrees with their stored normals (a bug in a primitive) and degenerate ones. Returns (bad, degenerate)."""
        bad = deg = 0
        for pos, nrm, uv0, uv1, tris, mat in self._chunks:
            p = pos.astype(np.float64)
            a, b, c = p[tris[:, 0]], p[tris[:, 1]], p[tris[:, 2]]
            fn = np.cross(b - a, c - a)
            ln = np.linalg.norm(fn, axis=1)
            sn = nrm[tris[:, 0]].astype(np.float64) + nrm[tris[:, 1]] + nrm[tris[:, 2]]
            d = np.einsum("ij,ij->i", fn, sn)
            ok = ln > 1e-10
            deg += int((~ok).sum())
            bad += int(((d < 0) & ok).sum())
        return bad, deg

    def to_local(self, frame, name=None):
        """A copy whose coordinates are relative to ``frame`` (the inverse of ``transformed_copy``)."""
        m = MeshBuilder(name or self.name)
        M = frame.matrix()
        for pos, nrm, uv0, uv1, tris, mat in self._chunks:
            m._chunks.append((((pos.astype(np.float64) - frame.o) @ M).astype(np.float32), (nrm.astype(np.float64) @ M).astype(np.float32),
                              uv0, uv1, tris, mat))
        return m

    def finalize(self):
        """-> dict(pos, nrm, uv0, uv1 (n, k); tris_by_mat {mat: (m, 3)})."""
        if not self._chunks:
            z = np.zeros((0, 3), np.float32)
            return {"pos": z, "nrm": z, "uv0": np.zeros((0, 2), np.float32), "uv1": np.zeros((0, 2), np.float32), "tris": {}}
        off = 0
        pos, nrm, uv0, uv1 = [], [], [], []
        by = {}
        for p, n, a, b, t, mat in self._chunks:
            pos.append(p); nrm.append(n); uv0.append(a); uv1.append(b)
            by.setdefault(mat, []).append(t + off)
            off += len(p)
        return {"pos": np.concatenate(pos), "nrm": np.concatenate(nrm), "uv0": np.concatenate(uv0), "uv1": np.concatenate(uv1),
                "tris": {m: np.concatenate(v) for m, v in by.items()}}

    def bounds(self):
        if not self._chunks:
            return np.zeros(3), np.zeros(3)
        allp = np.concatenate([c[0] for c in self._chunks])
        return allp.min(0).astype(np.float64), allp.max(0).astype(np.float64)

    # -- quads ------------------------------------------------------------------------------------------------------
    def quads(self, P, UV0, UV1, mat, N=None):
        """Batched quads. P (n,4,3) counter-clockwise seen from outside; UV0/UV1 (n,4,2); N (n,3) optional, else flat from the diagonals."""
        P = np.asarray(P, np.float64)
        n = len(P)
        if n == 0:
            return
        if N is None:
            N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
        Nv = np.repeat(np.asarray(N)[:, None, :], 4, axis=1)
        base = np.arange(n)[:, None] * 4
        tris = np.concatenate([base + np.array([0, 1, 2]), base + np.array([0, 2, 3])], axis=0).reshape(-1, 3)
        # keep per-quad triangle order stable: (0,1,2),(0,2,3) per quad
        t = np.empty((n * 2, 3), np.int64)
        t[0::2] = base + np.array([0, 1, 2])
        t[1::2] = base + np.array([0, 2, 3])
        self.add_raw(P.reshape(-1, 3), Nv.reshape(-1, 3), np.asarray(UV0).reshape(-1, 2), np.asarray(UV1).reshape(-1, 2), t, mat)

    def quad_metric(self, p0, p1, p2, p3, mat, tile=12.0, off=(0.0, 0.0), detail=2.0, doff=(0.0, 0.0), flip_uv=False):
        """One planar quad with metric UVs: u along p0->p1, v up the face (v = distance along the in-plane perpendicular)."""
        P = np.array([[p0, p1, p2, p3]], np.float64)
        self.quads_metric(P, mat, tile, np.array([off]), detail, np.array([doff]), flip_uv)

    def quads_metric(self, P, mat, tile=12.0, off=None, detail=2.0, doff=None, flip_uv=False):
        P = np.asarray(P, np.float64)
        n = len(P)
        if n == 0:
            return
        N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
        eu = norm(P[:, 1] - P[:, 0] + P[:, 2] - P[:, 3])
        ev = norm(cross(N, eu))
        rel = P - P[:, 0:1]
        s = np.einsum("nkc,nc->nk", rel, eu)
        t = np.einsum("nkc,nc->nk", rel, ev)
        st = np.stack([s, t], axis=2)
        if flip_uv:
            st = st[:, :, ::-1]
        off = np.zeros((n, 2)) if off is None else np.asarray(off, np.float64).reshape(n, 2)
        doff = np.zeros((n, 2)) if doff is None else np.asarray(doff, np.float64).reshape(n, 2)
        self.quads(P, off[:, None, :] + st / tile, doff[:, None, :] + st / detail, mat, N)

    def quad_rect(self, p0, p1, p2, p3, rect, mat, detail=2.0, uspan=(0.0, 1.0), vspan=(0.0, 1.0), flip=False, doff=(0, 0)):
        """A quad mapped to an atlas rectangle (u0, v0, u1, v1; v up). p0 is the rectangle's bottom-left corner, p1 bottom-right.
        uspan/vspan pick part of the rectangle (for cut strips). UV1 is metric in the quad's plane."""
        u0, v0, u1, v1 = rect
        a, b = uspan
        c, d = vspan
        uu0, uu1 = u0 + (u1 - u0) * a, u0 + (u1 - u0) * b
        vv0, vv1 = v0 + (v1 - v0) * c, v0 + (v1 - v0) * d
        UV0 = np.array([[(uu0, vv0), (uu1, vv0), (uu1, vv1), (uu0, vv1)]], np.float64)
        if flip:
            UV0 = UV0[:, [1, 0, 3, 2]]
        P = np.array([[p0, p1, p2, p3]], np.float64)
        N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
        eu = norm(P[:, 1] - P[:, 0] + P[:, 2] - P[:, 3])
        ev = norm(cross(N, eu))
        rel = P - P[:, 0:1]
        st = np.stack([np.einsum("nkc,nc->nk", rel, eu), np.einsum("nkc,nc->nk", rel, ev)], axis=2)
        self.quads(P, UV0, np.asarray(doff, np.float64) + st / detail, mat, N)

    # -- primitives -----------------------------------------------------------------------------------------------------
    def extrude(self, profile, frame, y0, y1, mat, s0=(1.0, 1.0), s1=(1.0, 1.0), shift1=(0.0, 0.0), caps=(True, True),
                tile=12.0, cap_mat=None, detail=2.0, uv_off=(0.0, 0.0), rect=None, cap_rect=None):
        """A prism along the frame's y: ``profile`` ((x, z) points, either winding) at y0 scaled by s0, and at y1 scaled by s1 and
        shifted by shift1. Side quads get metric UVs (or an atlas rect: u round the section by edge fraction, v along y)."""
        prof = np.asarray(profile, np.float64)
        # Orient so that side quads face outward: seen from +y, clockwise (x right, z up) is the convention used here.
        area = 0.5 * np.sum(prof[:, 0] * np.roll(prof[:, 1], -1) - np.roll(prof[:, 0], -1) * prof[:, 1])
        if area > 0:      # counter-clockwise: reverse
            prof = prof[::-1]
        k = len(prof)
        r0 = np.array([frame.p(x * s0[0], y0, z * s0[1]) for x, z in prof])
        r1 = np.array([frame.p(x * s1[0] + shift1[0], y1, z * s1[1] + shift1[1]) for x, z in prof])
        i0 = np.arange(k)
        i1 = (i0 + 1) % k
        P = np.stack([r0[i0], r0[i1], r1[i1], r1[i0]], axis=1)
        if rect is not None:
            u_edges = np.cumsum(np.r_[0.0, np.linalg.norm(r0[i1] - r0[i0], axis=1)])
            total = u_edges[-1]
            ur0, vr0, ur1, vr1 = rect
            UV0 = np.zeros((k, 4, 2))
            for j in range(k):
                ua = ur0 + (ur1 - ur0) * (u_edges[j] / total)
                ub = ur0 + (ur1 - ur0) * (u_edges[j + 1] / total)
                UV0[j] = [(ua, vr0), (ub, vr0), (ub, vr1), (ua, vr1)]
            N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
            eu = norm(P[:, 1] - P[:, 0] + P[:, 2] - P[:, 3])
            ev = norm(cross(N, eu))
            rel = P - P[:, 0:1]
            st = np.stack([np.einsum("nkc,nc->nk", rel, eu), np.einsum("nkc,nc->nk", rel, ev)], axis=2)
            self.quads(P, UV0, st / detail, mat, N)
        else:
            self.quads_metric(P, mat, tile, np.tile(uv_off, (k, 1)), detail)
        cm = mat if cap_mat is None else cap_mat
        ext = max(float(np.abs(prof[:, 0]).max()) * max(s0[0], s1[0]), float(np.abs(prof[:, 1]).max()) * max(s0[1], s1[1]), 1e-6)
        for end, (ring, sgn) in enumerate(((r0, -1.0), (r1, 1.0))):
            if not caps[end]:
                continue
            c = ring.mean(axis=0)
            if _inside(prof, 0.0, 0.0):          # a polygon built round its own origin (ragged plates): fan from the origin, which sees every edge
                c = frame.p(s0[0] * 0.0 if end == 0 else shift1[0], y0 if end == 0 else y1, s0[1] * 0.0 if end == 0 else shift1[1])
            n = frame.f * sgn
            front = sgn > 0
            A = np.repeat(c[None], k, 0)
            B = ring[i0] if front else ring[i1]
            C = ring[i1] if front else ring[i0]

            def uvof(pts):
                rel = pts - frame.o
                xz = np.stack([rel @ frame.r, rel @ frame.u], axis=-1)
                if cap_rect is not None:
                    u0, v0, u1, v1 = cap_rect
                    return np.stack([u0 + (u1 - u0) * (xz[:, 0] / (2 * ext) + 0.5), v0 + (v1 - v0) * (xz[:, 1] / (2 * ext) + 0.5)], axis=-1)
                return xz / tile
            self.add_tris_fan(A, B, C, np.repeat(n[None], k, 0), uvof(A), uvof(B), uvof(C), cm, detail, frame)

    def add_tris_fan(self, A, B, C, N, UA, UB, UC, mat, detail, frame):
        n = len(A)
        pos = np.stack([A, B, C], axis=1).reshape(-1, 3)
        nrm = np.repeat(N[:, None, :], 3, axis=1).reshape(-1, 3)
        uv0 = np.stack([UA, UB, UC], axis=1).reshape(-1, 2)
        uv1 = uv0 * 1.0
        tris = (np.arange(n)[:, None] * 3 + np.array([0, 1, 2])).reshape(-1, 3)
        self.add_raw(pos, nrm, uv0, uv1, tris, mat)

    def box(self, frame, size, mat, centre=(0, 0, 0), tile=12.0, chamfer=0.0, rect=None, caps=True, off=(0, 0), detail=2.0):
        """Box (sx, sy, sz) in ``frame`` centred at ``centre`` (local); chamfer > 0 bevels the four long edges."""
        sx, sy, sz = size
        prof = chamfered_rect(sx * 0.5, sz * 0.5, chamfer) if chamfer > 0 else np.array([(sx / 2, sz / 2), (sx / 2, -sz / 2), (-sx / 2, -sz / 2), (-sx / 2, sz / 2)])
        fr = Frame(frame.p(*centre), frame.f, frame.u)
        self.extrude(prof, fr, -sy * 0.5, sy * 0.5, mat, tile=tile, rect=rect, caps=(caps, caps), uv_off=off, detail=detail)

    def lathe(self, profile, frame, mat, segs=24, tile=8.0, rect=None, crease=40.0, detail=2.0, closed_start=False, arc=(0.0, 360.0), flip=False, uv_off=(0, 0)):
        """Surface of revolution about the frame's y axis. ``profile``: (radius, y) points along the surface. Vertices at corners sharper
        than ``crease`` degrees are split so edges stay hard; elsewhere the normals blend. ``rect`` maps u round the axis (0..1 of the
        arc) and v along the profile into an atlas rect; otherwise metric tile UVs. Winding: normals point away from the axis for
        profile points listed in increasing y with the outside on the +radius side; ``flip`` reverses (for inner bores)."""
        prof = np.asarray(profile, np.float64)
        m = len(prof) - 1
        a0, a1 = math.radians(arc[0]), math.radians(arc[1])
        ang = np.linspace(a0, a1, segs + 1)
        # per profile segment normal in the (r, y) plane: rotate the tangent by -90 degrees (outside = +r)
        seg_n = np.zeros((m, 2))
        for i in range(m):
            dr, dy = prof[i + 1, 0] - prof[i, 0], prof[i + 1, 1] - prof[i, 1]
            l = math.hypot(dr, dy) + EPS
            seg_n[i] = (dy / l, -dr / l)
        if flip:
            seg_n = -seg_n
        seg_len = np.hypot(np.diff(prof[:, 0]), np.diff(prof[:, 1]))
        total = seg_len.sum() + EPS
        cum = np.r_[0.0, np.cumsum(seg_len)]
        # vertex normals per segment end
        def vnorm(i, end):
            n = seg_n[i].copy()
            j = i + 1 if end == 1 else i - 1
            if 0 <= j < m:
                cosang = float(np.dot(seg_n[i], seg_n[j]))
                if cosang > math.cos(math.radians(crease)):
                    n = seg_n[i] + seg_n[j]
            return n / (np.linalg.norm(n) + EPS)
        for i in range(m):
            (r0, y0), (r1, y1) = prof[i], prof[i + 1]
            n0, n1 = vnorm(i, 0), vnorm(i, 1)
            ca, sa = np.cos(ang), np.sin(ang)
            ring0 = np.stack([r0 * sa, np.full_like(ca, y0), r0 * ca], axis=1)
            ring1 = np.stack([r1 * sa, np.full_like(ca, y1), r1 * ca], axis=1)
            nr0 = np.stack([n0[0] * sa, np.full_like(ca, n0[1]), n0[0] * ca], axis=1)
            nr1 = np.stack([n1[0] * sa, np.full_like(ca, n1[1]), n1[0] * ca], axis=1)
            pos = np.concatenate([ring0, ring1])
            nrm = np.concatenate([nr0, nr1])
            world = (pos @ frame.matrix().T) + frame.o
            wn = nrm @ frame.matrix().T
            ua = (ang - a0) / max(a1 - a0, 1e-6)
            if rect is not None:
                ur0, vr0, ur1, vr1 = rect
                u = ur0 + (ur1 - ur0) * ua
                v0 = vr0 + (vr1 - vr0) * (cum[i] / total)
                v1 = vr0 + (vr1 - vr0) * (cum[i + 1] / total)
                uv0 = np.concatenate([np.stack([u, np.full_like(u, v0)], 1), np.stack([u, np.full_like(u, v1)], 1)])
            else:
                rmid = 0.5 * (r0 + r1)
                u = ua * (a1 - a0) * rmid / tile + uv_off[0]
                uv0 = np.concatenate([np.stack([u, np.full_like(u, cum[i] / tile + uv_off[1])], 1),
                                      np.stack([u, np.full_like(u, cum[i + 1] / tile + uv_off[1])], 1)])
            d = np.concatenate([np.stack([ua * (a1 - a0) * r0 / detail, np.full_like(ua, cum[i] / detail)], 1),
                                np.stack([ua * (a1 - a0) * r1 / detail, np.full_like(ua, cum[i + 1] / detail)], 1)])
            k = segs + 1
            idx = np.arange(segs)
            a, b, c, dd = idx, idx + 1, idx + 1 + k, idx + k
            # ring0 at lower y, the angle runs from the top (z) towards +x: (a, b, c) is counter-clockwise seen from outside the surface.
            tris = np.concatenate([np.stack([a, b, c], 1), np.stack([a, c, dd], 1)])
            if flip:
                tris = tris[:, ::-1]
            self.add_raw(world, wn, uv0, d, tris, mat)
        if closed_start:
            pass

    def disc(self, frame, radius, y, mat, segs=24, facing=1.0, tile=8.0, rect=None, inner=0.0, rect_center=None):
        """A flat disc (or annulus) at local y facing +y (facing 1) or -y; rect maps the disc into an atlas rectangle (u across, v up)."""
        ang = np.linspace(0, 2 * math.pi, segs + 1)
        sa, ca = np.sin(ang), np.cos(ang)
        n = frame.f * facing
        pts_o = np.array([frame.p(radius * s, y, radius * c) for s, c in zip(sa, ca)])
        if inner > 0:
            pts_i = np.array([frame.p(inner * s, y, inner * c) for s, c in zip(sa, ca)])
        else:
            pts_i = np.repeat(frame.p(0, y, 0)[None], segs + 1, 0)
        k = segs
        A, B, C, D = pts_i[:k], pts_i[1:k + 1], pts_o[1:k + 1], pts_o[:k]
        P = np.stack([A, B, C, D], axis=1)
        if facing > 0:
            P = P[:, ::-1]
        N = np.repeat(n[None], k, 0)
        def uv_of(pts):
            rel = pts - frame.o
            return np.stack([rel @ frame.r, rel @ frame.u], axis=-1)
        UV = uv_of(P.reshape(-1, 3)).reshape(k, 4, 2)
        if rect is not None:
            u0, v0, u1, v1 = rect
            UV0 = np.stack([u0 + (u1 - u0) * (UV[..., 0] / (2 * radius) + 0.5), v0 + (v1 - v0) * (UV[..., 1] / (2 * radius) + 0.5)], axis=-1)
        else:
            UV0 = UV / tile
        self.quads(P, UV0, UV / 2.0, mat, N)

    def tri_fan_cap(self, ring, centre, normal, mat, tile=8.0):
        k = len(ring)
        A = np.repeat(np.asarray(centre)[None], k, 0)
        B = np.asarray(ring)[np.arange(k)]
        C = np.asarray(ring)[(np.arange(k) + 1) % k]
        UV = np.zeros((k, 2))
        self.add_tris_fan(A, B, C, np.repeat(np.asarray(normal)[None], k, 0), UV, UV, UV, mat, 2.0, None)

    def slabs(self, B, n, h, ch, top_mat, edge_mat, tile=12.0, tile_off=None, edge_rect=None, detail=2.0, lod=0, edge_tile=6.0):
        """Raised armour plates, vectorised. B (m,4,3): base corners counter-clockwise seen from outside (p0, p1 along u, p2, p3);
        n (m,3) outward normals; h (m,) heights; ch (m,) chamfer sizes. Each plate is a wall up to h - ch, a 45-degree chamfer, and a
        top. ``edge_rect`` maps walls and chamfers into an atlas rect (u along the edge in metres / edge_tile, v across it);
        ``top_mat`` surfaces use metric tile UVs with a per-plate offset."""
        B = np.asarray(B, np.float64)
        m = len(B)
        if m == 0:
            return
        n = np.asarray(n, np.float64)
        h = np.asarray(h, np.float64)
        ch = np.minimum(np.asarray(ch, np.float64), h * 0.999)
        ctr = B.mean(axis=1)
        # in-plane inward direction at each corner, so the chamfer is ch wide on both edges
        e01 = norm(B[:, 1] - B[:, 0])
        e03 = norm(B[:, 3] - B[:, 0])
        sgn = np.array([(1, 1), (-1, 1), (-1, -1), (1, -1)], np.float64)    # per corner: +e01/-e01 and +e03/-e03 toward inside
        inset = e01[:, None, :] * sgn[None, :, 0:1] * ch[:, None, None] + e03[:, None, :] * sgn[None, :, 1:2] * ch[:, None, None]
        mid = B + n[:, None, :] * (h - ch)[:, None, None]
        top = B + n[:, None, :] * h[:, None, None] + inset
        if tile_off is None:
            tile_off = np.zeros((m, 2))
        # top face
        self.quads_metric(top, top_mat, tile, tile_off, detail, tile_off * 0.37)
        if lod >= 1 and False:
            return
        # walls (base -> mid) and chamfers (mid -> top), four sides each, with edge UVs.
        i0 = np.arange(4)
        i1 = (i0 + 1) % 4
        wall_rect, chamfer_rect = edge_rect if (isinstance(edge_rect, tuple) and len(edge_rect) == 2 and not np.isscalar(edge_rect[0])) else (edge_rect, edge_rect)
        for lower, upper, rect in ((B, mid, wall_rect), (mid, top, chamfer_rect)):
            if lower is B and (h - ch).max() <= 1e-4:
                continue
            P = np.stack([lower[:, i0], lower[:, i1], upper[:, i1], upper[:, i0]], axis=2)    # (m, 4 sides, 4 corners, 3)
            P = P.transpose(0, 1, 2, 3).reshape(-1, 4, 3)
            self._edge_quads(P, edge_mat, rect, edge_tile, detail)

    def _edge_quads(self, P, mat, rect, tile, detail):
        N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
        eu = norm(P[:, 1] - P[:, 0] + P[:, 2] - P[:, 3])
        ev = norm(cross(N, eu))
        rel = P - P[:, 0:1]
        st = np.stack([np.einsum("nkc,nc->nk", rel, eu), np.einsum("nkc,nc->nk", rel, ev)], axis=2)
        if rect is None:
            UV0 = st / tile
        else:
            u0, v0, u1, v1 = rect
            # u along the edge repeats inside the rect is impossible in an atlas: map the edge's length to a wrap by using the
            # rect's width as one repetition of metres == tile, and clamp longer edges (plates are short compared with a strip)
            su = np.clip(st[..., 0] / tile, 0.0, 1.0)
            vv = np.clip(st[..., 1] / np.maximum(st[..., 1].max(axis=1, keepdims=True), 1e-6), 0.0, 1.0)
            UV0 = np.stack([u0 + (u1 - u0) * su, v0 + (v1 - v0) * vv], axis=-1)
        self.quads(P, UV0, st / detail, mat, N)
