"""mnrock: rock for the Oort outpost (art_src/oort_art/build_earth_outpost.py): a comet nucleus made of two merged lobes (a smooth union of superellipsoids), bumped by lumps,
ridges, strata and craters, then carved by the model script (a shelf for the station, a cliff and a pocket for the hangar, pits for the guns, vents for the jets). The mesh is
the truth: placement queries (``drop`` a vertical ray, ``ray`` any ray) are answered by casting against it, so the station stands on the rock that is really there.

Everything here is numpy (no bpy), deterministic for a seed, and levelled: ``level`` is the icosphere subdivision (6 is 82k triangles with 9 m edges on a 1 km rock, 5 the
distance mesh, 3 the proxy); the noise octaves that the mesh cannot carry are left out of the coarse levels, so the lobes and the big craters agree between them.

UVs: each face is mapped on the plane its (smoothed) normal faces most, so a region 100 m across shares one projection and the seams between regions are few and long, like
the contacts between strata. UV0 is metres / ``tile`` for the rock textures, UV1 metres / ``detail`` for the tiling detail map.
"""
import math

import numpy as np

from .mnmesh import MeshBuilder, norm, cross

# ------------------------------------------------------------------------------------------------ noise


def _hash3(ix, iy, iz, seed):
    m = np.uint64(0xFFFFFFFF)
    a = (ix.astype(np.int64).astype(np.uint64) & m) * np.uint64(374761393) & m
    b = (iy.astype(np.int64).astype(np.uint64) & m) * np.uint64(668265263) & m
    c = (iz.astype(np.int64).astype(np.uint64) & m) * np.uint64(2147483629) & m
    h = (a ^ b ^ c ^ np.uint64((int(seed) * 2654435761) & 0xFFFFFFFF)) & m
    h = ((h ^ (h >> np.uint64(13))) * np.uint64(1274126177)) & m
    h = h ^ (h >> np.uint64(16))
    return (h & np.uint64(0xFFFF)).astype(np.float32) / 65535.0


def noise3(p, seed=0):
    """Smooth value noise in 0..1 at points p (n, 3)."""
    p = np.asarray(p, np.float64)
    f = np.floor(p)
    i = f.astype(np.int64)
    t = (p - f).astype(np.float32)
    t = t * t * (3.0 - 2.0 * t)
    x0, y0, z0 = i[:, 0], i[:, 1], i[:, 2]
    x1, y1, z1 = x0 + 1, y0 + 1, z0 + 1

    def lerp(a, b, k):
        return a + (b - a) * k
    c00 = lerp(_hash3(x0, y0, z0, seed), _hash3(x1, y0, z0, seed), t[:, 0])
    c10 = lerp(_hash3(x0, y1, z0, seed), _hash3(x1, y1, z0, seed), t[:, 0])
    c01 = lerp(_hash3(x0, y0, z1, seed), _hash3(x1, y0, z1, seed), t[:, 0])
    c11 = lerp(_hash3(x0, y1, z1, seed), _hash3(x1, y1, z1, seed), t[:, 0])
    return lerp(lerp(c00, c10, t[:, 1]), lerp(c01, c11, t[:, 1]), t[:, 2])


def fbm3(p, seed=0, octaves=5, lac=2.03, gain=0.5):
    """Fractal noise, roughly -1..1."""
    p = np.asarray(p, np.float64)
    amp, tot, out = 1.0, 0.0, np.zeros(len(p), np.float32)
    for o in range(octaves):
        out += amp * (noise3(p * (lac ** o) + o * 17.3, seed + o * 31) * 2.0 - 1.0)
        tot += amp
        amp *= gain
    return out / tot


def ridged3(p, seed=0, octaves=3):
    """Sharp ridges, 0..1 (1 on the ridge line)."""
    p = np.asarray(p, np.float64)
    amp, tot, out = 1.0, 0.0, np.zeros(len(p), np.float32)
    for o in range(octaves):
        n = noise3(p * (2.0 ** o) + o * 9.1, seed + o * 13) * 2.0 - 1.0
        out += amp * (1.0 - np.abs(n))
        tot += amp
        amp *= 0.5
    return (out / tot) ** 2.0


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-9), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


# ------------------------------------------------------------------------------------------------ meshes


def icosphere(level):
    t = (1.0 + 5.0 ** 0.5) / 2.0
    V = np.array([(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0), (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t), (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)], np.float64)
    V /= np.linalg.norm(V, axis=1, keepdims=True)
    F = np.array([(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8),
                  (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)], np.int64)
    for _ in range(level):
        e = np.sort(np.stack([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]], axis=1).reshape(-1, 2), axis=1)
        uniq, inv = np.unique(e, axis=0, return_inverse=True)
        mid = V[uniq[:, 0]] + V[uniq[:, 1]]
        mid /= np.linalg.norm(mid, axis=1, keepdims=True)
        base = len(V)
        V = np.concatenate([V, mid])
        m = inv.reshape(-1, 3) + base
        a, b, c = F[:, 0], F[:, 1], F[:, 2]
        F = np.concatenate([np.stack([a, m[:, 0], m[:, 2]], 1), np.stack([b, m[:, 1], m[:, 0]], 1), np.stack([c, m[:, 2], m[:, 1]], 1), np.stack([m[:, 0], m[:, 1], m[:, 2]], 1)])
    return V, F


def vertex_normals(V, F, smooth=0):
    """Area-weighted vertex normals; ``smooth`` extra passes average each face's normal over its vertices' (spreading one ring per pass)."""
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
    vn = np.zeros_like(V)
    for k in range(3):
        np.add.at(vn, F[:, k], fn)
    vn = norm(vn)
    for _ in range(smooth):
        f2 = vn[F].sum(axis=1)
        vn = np.zeros_like(V)
        for k in range(3):
            np.add.at(vn, F[:, k], f2)
        vn = norm(vn)
    return vn


# ------------------------------------------------------------------------------------------------ the shape


def smin(a, b, k):
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1.0 - h) + a * h - k * h * (1.0 - h)


def superellipsoid_sd(p, c, a, power):
    """An approximate signed distance in metres to a superellipsoid centred at c with semi-axes a (negative inside)."""
    q = np.abs((p - c) / a)
    n = np.power((q ** power).sum(axis=-1), 1.0 / power)
    return (n - 1.0) * float(np.min(a))


class Nucleus:
    """spec keys: lobes [(centre, semi_axes, power)], blend (m), seed, amp (dict of noise amplitudes in m), craters [(unit dir, radius m, depth m)]."""

    def __init__(self, spec):
        self.spec = spec
        self.lobes = [(np.array(c, float), np.array(a, float), float(p)) for c, a, p in spec["lobes"]]
        self.blend = float(spec.get("blend", 90.0))
        self.seed = int(spec.get("seed", 1))
        self.amp = dict(big=30.0, mid=9.0, fine=2.4, ridge=12.0, strata=3.5)
        self.amp.update(spec.get("amp", {}))
        self.craters = spec.get("craters", [])
        self.strata_axis = norm(np.array(spec.get("strata_axis", (0.25, 0.1, 1.0)), float))

    def field(self, p):
        f = None
        for c, a, pw in self.lobes:
            d = superellipsoid_sd(p, c, a, pw)
            f = d if f is None else smin(f, d, self.blend)
        return f

    def radius(self, d, tmax=1600.0, steps=44, bisect=12):
        """Distance along each unit direction from the origin to the outermost surface of the base shape."""
        n = len(d)
        out = np.zeros(n)
        for s in range(0, n, 20000):
            dd = d[s:s + 20000]
            m = len(dd)
            ts = np.linspace(tmax, 0.0, steps)
            inside = np.zeros((m, steps), bool)
            for i, t in enumerate(ts):
                inside[:, i] = self.field(dd * t) < 0.0
            first = np.argmax(inside, axis=1)                 # the first (outermost) step inside
            lo = ts[first]                                    # inside
            hi = ts[np.maximum(first - 1, 0)]                 # outside (or the same step when the origin region was hit first)
            for _ in range(bisect):
                mid = 0.5 * (lo + hi)
                ins = self.field(dd * mid[:, None]) < 0.0
                lo = np.where(ins, mid, lo)
                hi = np.where(ins, hi, mid)
            out[s:s + m] = 0.5 * (lo + hi)
        return out

    def displacement(self, p, level):
        """Metres along the radial direction at base-surface points p: lumps, ridges, strata (fewer octaves on coarse levels), craters. The domain is warped first,
        so nothing lines up: no bark, no stripes."""
        s = self.seed
        a = self.amp
        w = 70.0 * np.stack([fbm3(p / 310.0, s + 100, 2), fbm3(p / 310.0, s + 101, 2), fbm3(p / 310.0, s + 102, 2)], axis=1)
        q = p + w
        oct_mid = 4 if level >= 5 else 2
        d = a["big"] * fbm3(q / 400.0, s, 3)
        d += a["mid"] * fbm3(q / 95.0, s + 5, oct_mid)
        if level >= 5:
            d += a["ridge"] * (ridged3(q / 190.0, s + 11, 3) - 0.35)
            zone = smoothstep(0.15, 0.6, fbm3(q / 260.0, s + 29, 2))                      # strata only where the nucleus has cliffs
            d += zone * a["strata"] * (np.mod(q @ self.strata_axis / 52.0 + 0.9 * fbm3(q / 110.0, s + 19, 2), 1.0) ** 2.0 - 0.33)
        if level >= 6:
            d += a["fine"] * fbm3(q / 21.0, s + 7, 3)
        return d

    def crater_dips(self, p, level):
        d = np.zeros(len(p))
        if level < 4:
            return d
        for c, rad, depth in self._crater_pts():
            x = np.linalg.norm(p - c, axis=1) / rad
            bowl = -depth * np.clip(1.0 - x * x, 0.0, 1.0) ** 1.4
            rim = 0.28 * depth * np.exp(-((x - 1.08) / 0.16) ** 2)
            d += bowl + rim
        return d

    def _crater_pts(self):
        if not hasattr(self, "_cp"):
            self._cp = []
            for cdir, rad, depth in self.craters:
                cd = norm(np.array(cdir, float))[None, :]
                self._cp.append((cd[0] * self.radius(cd)[0], rad, depth))
        return self._cp

    def mesh(self, level, edits=(), ice_fn=None):
        """-> RockMesh. ``edits`` are functions f(V) -> V applied to the displaced vertices; ``ice_fn(centroids, normals, V, F)`` returns a bool per face (the ice material)."""
        dirs, F = icosphere(level)
        r0 = self.radius(dirs)
        P0 = dirs * r0[:, None]
        disp = self.displacement(P0, level) + self.crater_dips(P0, level)
        V = P0 + dirs * disp[:, None]
        for e in edits:
            V = e(V)
        return RockMesh(V, F, ice_fn)


class RockMesh:
    def __init__(self, V, F, ice_fn=None):
        self.V = np.asarray(V, np.float64)
        self.F = np.asarray(F, np.int64)
        # outward winding: faces must look away from the centroid on average
        fn = np.cross(self.V[self.F[:, 1]] - self.V[self.F[:, 0]], self.V[self.F[:, 2]] - self.V[self.F[:, 0]])
        cen = self.V[self.F].mean(axis=1)
        if np.mean(np.einsum("ij,ij->i", fn, cen - self.V.mean(axis=0))) < 0:
            self.F = self.F[:, ::-1].copy()
        self.N = vertex_normals(self.V, self.F)
        self.ice = np.zeros(len(self.F), bool)
        if ice_fn is not None:
            self.ice = np.asarray(ice_fn(self.V[self.F].mean(axis=1), self.N[self.F].mean(axis=1), self.V, self.F), bool)
        # query structures
        tri = self.V[self.F]
        self._mn = tri.min(axis=1)
        self._mx = tri.max(axis=1)

    def bounds(self):
        return self.V.min(axis=0), self.V.max(axis=0)

    # -- queries ----------------------------------------------------------------------------------------------------
    def drop(self, x, y, z_from=None, up=True):
        """The highest (up) or lowest surface point on the vertical line through (x, y): -> (point, normal) or None."""
        m = (self._mn[:, 0] <= x) & (self._mx[:, 0] >= x) & (self._mn[:, 1] <= y) & (self._mx[:, 1] >= y)
        idx = np.nonzero(m)[0]
        best = None
        for i in idx:
            a, b, c = self.V[self.F[i]]
            # barycentric in the xy plane
            d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(d) < 1e-12:
                continue
            l1 = ((b[1] - c[1]) * (x - c[0]) + (c[0] - b[0]) * (y - c[1])) / d
            l2 = ((c[1] - a[1]) * (x - c[0]) + (a[0] - c[0]) * (y - c[1])) / d
            l3 = 1.0 - l1 - l2
            if l1 < -1e-9 or l2 < -1e-9 or l3 < -1e-9:
                continue
            z = l1 * a[2] + l2 * b[2] + l3 * c[2]
            if z_from is not None and ((up and z > z_from) or (not up and z < z_from)):
                continue
            if best is None or (up and z > best[0]) or (not up and z < best[0]):
                n = norm(l1 * self.N[self.F[i, 0]] + l2 * self.N[self.F[i, 1]] + l3 * self.N[self.F[i, 2]])
                best = (z, np.array([x, y, z]), n)
        return None if best is None else (best[1], best[2])

    def ray(self, o, d, tmax=1e9):
        """Nearest hit of the ray o + t d (t > 0): -> (point, normal) or None. Moller-Trumbore over every face."""
        o = np.asarray(o, float)
        d = norm(np.asarray(d, float))
        tri = self.V[self.F]
        v0, v1, v2 = tri[:, 0], tri[:, 1], tri[:, 2]
        e1, e2 = v1 - v0, v2 - v0
        h = np.cross(d, e2)
        a = np.einsum("ij,ij->i", e1, h)
        ok = np.abs(a) > 1e-12
        f = np.where(ok, 1.0 / np.where(ok, a, 1.0), 0.0)
        s = o - v0
        u = f * np.einsum("ij,ij->i", s, h)
        q = np.cross(s, e1)
        v = f * (q @ d)
        t = f * np.einsum("ij,ij->i", e2, q)
        hit = ok & (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 1e-6) & (t < tmax)
        if not hit.any():
            return None
        i = np.nonzero(hit)[0][np.argmin(t[hit])]
        w = 1.0 - u[i] - v[i]
        n = norm(w * self.N[self.F[i, 0]] + u[i] * self.N[self.F[i, 1]] + v[i] * self.N[self.F[i, 2]])
        return o + d * t[i], n

    # -- geometry out -----------------------------------------------------------------------------------------------
    def _uv_planes(self, smooth_passes=7):
        """The projection axis (0 x, 1 y, 2 z) and sign of each face, from normals smoothed over about +-60 m."""
        sn = vertex_normals(self.V, self.F, smooth=smooth_passes)
        fn = sn[self.F].sum(axis=1)
        ax = np.argmax(np.abs(fn), axis=1)
        sg = np.sign(fn[np.arange(len(fn)), ax])
        sg[sg == 0] = 1.0
        return ax, sg

    def emit(self, mb, rock_mat, ice_mat, tile=96.0, detail=4.0, offset=(0.0, 0.0, 0.0)):
        """Add the mesh to a MeshBuilder: unwelded faces with smooth normals; ``offset`` shifts the rock in ship space."""
        ax, sg = self._uv_planes()
        P = self.V[self.F] + np.asarray(offset)                    # (m, 3, 3)
        Nn = self.N[self.F]
        m = len(self.F)
        # axis -> the two ship-space coordinates that become (u, v); sign mirrors u so opposite faces differ
        pairs = {0: (1, 2), 1: (0, 2), 2: (0, 1)}
        shifts = {0: (0.00, 0.00), 1: (0.37, 0.61), 2: (0.73, 0.29)}
        uv0 = np.zeros((m, 3, 2))
        uv1 = np.zeros((m, 3, 2))
        for a in (0, 1, 2):
            sel = ax == a
            if not sel.any():
                continue
            ca, cb = pairs[a]
            u = P[sel][:, :, ca] * sg[sel][:, None]
            v = P[sel][:, :, cb]
            sx, sy = shifts[a]
            uv0[sel, :, 0] = u / tile + sx
            uv0[sel, :, 1] = v / tile + sy
            uv1[sel, :, 0] = u / detail
            uv1[sel, :, 1] = v / detail
        pos = P.reshape(-1, 3)
        nrm = Nn.reshape(-1, 3)
        tris = np.arange(m * 3).reshape(m, 3)
        # winding: faces are already outward; keep order
        for mat, sel in ((rock_mat, ~self.ice), (ice_mat, self.ice)):
            if not sel.any():
                continue
            ids = tris[sel]
            sub_pos = pos[ids.reshape(-1)]
            sub_nrm = nrm[ids.reshape(-1)]
            sub_uv0 = uv0[sel].reshape(-1, 2)
            sub_uv1 = uv1[sel].reshape(-1, 2)
            k = int(sel.sum())
            mb.add_raw(sub_pos, sub_nrm, sub_uv0, sub_uv1, np.arange(k * 3).reshape(k, 3), mat)


def boulder(mb, mat, centre, normal, radius, rng, tile=24.0, detail=4.0, level=2, squash=0.7, sink=0.35):
    """A loose rock sitting on a surface: a displaced icosphere, flattened toward the surface and sunk into it. Smooth normals, box-projected UVs."""
    V, F = icosphere(level)
    n = norm(np.asarray(normal, float))
    up = np.array([0, 0, 1.0]) if abs(n[2]) < 0.9 else np.array([1.0, 0, 0])
    t1 = norm(np.cross(up, n))
    t2 = np.cross(n, t1)
    sc = np.array([radius * rng.uniform(0.8, 1.3), radius * rng.uniform(0.7, 1.2), radius * squash * rng.uniform(0.8, 1.2)])
    seed = int(rng.integers(1, 10000))
    bump = fbm3(V * 2.3 + seed, seed, 3) * 0.28
    L = V * (1.0 + bump)[:, None] * sc
    L[:, 2] -= radius * sink                        # sunk
    W = centre + L[:, 0:1] * t1 + L[:, 1:2] * t2 + L[:, 2:3] * n
    # drop faces entirely below the surface plane (they would never show)
    keep = (L[F][:, :, 2].max(axis=1) > -radius * (sink + 0.55))
    F = F[keep]
    rm = RockMesh(W, F)
    rm.N = vertex_normals(W, F)
    rm.emit(mb, mat, mat, tile=tile, detail=detail)
    return rm
