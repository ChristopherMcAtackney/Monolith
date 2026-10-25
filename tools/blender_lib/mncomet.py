"""mncomet: comet nuclei for the Oort ice field (art_src/comets/build_comets.py). Where mnrock makes one big nucleus for a station to stand on, this makes a family of free bodies that the
game scatters by the hundred: a shape (a smooth union of superellipsoid lobes, optionally cut by planes: a bilobed nucleus, an elongated one, a rounded one, a split fragment), then a TERRAIN
laid on it the way real nuclei are made (67P, Tempel 1, Hartley 2, Wild 2, Borrelly):

  * smooth dust plains and dust-filled hollows (flat, a little paler, almost no relief),
  * rough consolidated terrain: stepped terraces with cliffs, blocks, fractures, ridges,
  * craters with flat dusty floors and sharp rims, and deep steep-walled pits (sinkholes) with terraced walls,
  * boulders: strewn thinly everywhere, thick at the foot of the cliffs and in the hollows,
  * patches of exposed ice and frost: in the shadowed floors of pits and hollows and on fresh scarps (a few percent of the surface).

Everything is numpy (no bpy outside the last section), deterministic for a seed, and levelled like mnrock: ``level`` is the icosphere subdivision (6 is 82k triangles, 5 is 20k, 4 is 5k, 3 is
1.3k), the noise a level cannot carry is left out of it, and the lobes, craters and pits agree between levels so the LODs share one silhouette.

Faces carry one of three materials: 0 crust (the dark, dusty, consolidated surface), 1 dust (the smooth, paler fill) and 2 ice (frost and exposed ice). UVs: each face is mapped on the plane its
smoothed normal faces most (UV0 metres / ``tile``, UV1 metres / ``detail``); vertices are shared between faces of the same plane, so the mesh stays welded except along the projection seams.
"""
import math

import numpy as np

from .mnmesh import norm
from .mnrock import fbm3, ridged3, smoothstep, icosphere, vertex_normals, smin

try:
    import bpy
    import mathutils
except ImportError:      # outside Blender (unit tests)
    bpy = None
    mathutils = None

CRUST, DUST, ICE = 0, 1, 2


def smax(a, b, k):
    return -smin(-a, -b, k)


def rot_xyz(deg):
    ax, ay, az = (math.radians(d) for d in deg)
    cx, sx, cy, sy, cz, sz = math.cos(ax), math.sin(ax), math.cos(ay), math.sin(ay), math.cos(az), math.sin(az)
    rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return rz @ ry @ rx


# ------------------------------------------------------------------------------------------------ the shape


class Shape:
    """lobes: dicts c (centre), a (semi-axes), p (superellipsoid power, 2 is an ellipsoid, higher is boxier), rot (degrees about x, y, z). cuts: [(normal, offset)], the body is the
    part where normal . p < offset (planes that slice the body, smoothed by ``cut_blend`` metres). ``blend`` is the smooth-union radius between lobes (the neck's fillet)."""

    def __init__(self, lobes, blend=80.0, cuts=(), cut_blend=8.0):
        self.lobes = [(np.array(L["c"], float), np.array(L["a"], float), float(L.get("p", 2.3)), rot_xyz(L.get("rot", (0, 0, 0)))) for L in lobes]
        self.blend = float(blend)
        self.cuts = [(norm(np.array(n, float)), float(o)) for n, o in cuts]
        self.cut_blend = float(cut_blend)
        self.tmax = 1.2 * max(np.linalg.norm(c) + float(a.max()) for c, a, _, _ in self.lobes)

    def field(self, p):
        f = None
        for c, a, pw, R in self.lobes:
            q = np.abs(((p - c) @ R) / a)
            n = np.power((q ** pw).sum(axis=-1), 1.0 / pw)
            d = (n - 1.0) * float(a.min())
            f = d if f is None else smin(f, d, self.blend)
        for n, o in self.cuts:
            f = smax(f, p @ n - o, self.cut_blend)
        return f

    def radial(self, dirs, steps=48, bisect=14):
        """Distance along each unit direction from the origin to the outermost surface."""
        out = np.zeros(len(dirs))
        for s in range(0, len(dirs), 16000):
            dd = dirs[s:s + 16000]
            ts = np.linspace(self.tmax, 0.0, steps)
            inside = np.zeros((len(dd), steps), bool)
            for i, t in enumerate(ts):
                inside[:, i] = self.field(dd * t) < 0.0
            first = np.argmax(inside, axis=1)
            lo = ts[first]
            hi = ts[np.maximum(first - 1, 0)]
            for _ in range(bisect):
                mid = 0.5 * (lo + hi)
                ins = self.field(dd * mid[:, None]) < 0.0
                lo = np.where(ins, mid, lo)
                hi = np.where(ins, hi, mid)
            out[s:s + len(dd)] = 0.5 * (lo + hi)
        return out

    def normal(self, p, eps=1.5):
        g = np.stack([self.field(p + np.array(e) * eps) - self.field(p - np.array(e) * eps) for e in ((1, 0, 0), (0, 1, 0), (0, 0, 1))], axis=1)
        return norm(g)


# ------------------------------------------------------------------------------------------------ mesh helpers


def adjacency(F, n):
    e = np.concatenate([F[:, [0, 1]], F[:, [1, 2]], F[:, [2, 0]]])
    e = np.unique(np.sort(e, axis=1), axis=0)
    a, b = e[:, 0], e[:, 1]
    deg = np.bincount(a, minlength=n) + np.bincount(b, minlength=n)
    return a, b, np.maximum(deg, 1)


def lap_smooth(v, adj, iters):
    a, b, deg = adj
    n = len(v)
    for _ in range(int(iters)):
        s = np.bincount(a, weights=v[b], minlength=n) + np.bincount(b, weights=v[a], minlength=n)
        v = 0.5 * v + 0.5 * s / deg
    return v


def mean_edge(V, F):
    t = V[F]
    ar = 0.5 * np.linalg.norm(np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]), axis=1)
    return math.sqrt(4.0 / math.sqrt(3.0) * float(ar.mean()))


def volume_centroid(V, F):
    t = V[F]
    vol = np.einsum("ij,ij->i", t[:, 0], np.cross(t[:, 1], t[:, 2])) / 6.0
    cen = t.sum(axis=1) / 4.0
    return (cen * vol[:, None]).sum(axis=0) / vol.sum(), float(vol.sum())


# ------------------------------------------------------------------------------------------------ the terrain


class Terrain:
    """Accumulates the surface's displacement along its base normal (``h``, metres) and the fields that decide materials and boulders. All noise is a function of position (so every level
    of detail sees the same bodies); a feature smaller than ~3 edges is left out of a level."""

    def __init__(self, P, N, F, seed, edge):
        self.P, self.N, self.F, self.seed, self.edge = P, N, F, int(seed), float(edge)
        n = len(P)
        self.h = np.zeros(n)
        self.rough = np.zeros(n)
        self.cliff = np.zeros(n)
        self.floor = np.zeros(n)         # the flat bottoms of craters and pits (frost likes them)
        self.hollow = np.zeros(n)        # where dust has filled a depression (fill_hollows)
        self.adj = adjacency(F, n)

    # -- helpers --------------------------------------------------------------------------------------------------
    def octs(self, wl, octaves):
        """How many octaves of a noise of base wavelength wl this level can carry (the finest kept is at least 3 edges long)."""
        k = int(math.floor(math.log2(max(wl / (3.0 * self.edge), 1e-6)))) + 1
        return max(0, min(octaves, k))

    def warped(self, wl, amp, seed):
        w = amp * np.stack([fbm3(self.P / wl, self.seed + seed + k, 2) for k in range(3)], axis=1)
        return self.P + w

    # -- the big forms --------------------------------------------------------------------------------------------
    def lumps(self, amp, wl, octaves=3, seed=0):
        o = self.octs(wl, octaves)
        if o:
            self.h += amp * fbm3(self.warped(wl * 0.8, wl * 0.3, seed + 40) / wl, self.seed + seed, o)

    def zones(self, wl, lo, hi, seed=100, bias=None):
        """Where the surface is rough (1) and where dust has smoothed it (0). ``bias`` (n,) adds to the noise before the threshold (a smooth neck, a rough end)."""
        n = fbm3(self.warped(wl * 0.8, wl * 0.35, seed + 40) / wl, self.seed + seed, 3)
        if bias is not None:
            n = n + bias
        self.rough = smoothstep(lo, hi, n)

    def _grad(self, v, smooth=2):
        """|gradient| of a per-vertex field in units per metre (largest over the edges at a vertex, smoothed a little)."""
        a, b, _ = self.adj
        g = np.abs(v[a] - v[b]) / np.maximum(np.linalg.norm(self.P[a] - self.P[b], axis=1), 1e-6)
        s = np.zeros(len(v))
        np.maximum.at(s, a, g)
        np.maximum.at(s, b, g)
        return lap_smooth(s, self.adj, smooth) + 1e-6

    def riser(self, n, th, width):
        """A smooth step of the field ``n`` at the level ``th`` that takes ``width`` metres to rise, wherever the field's own gradient lies (a cliff of a set steepness)."""
        u = np.clip((n - th) / (width * self._grad(n)), 0.0, 1.0)
        return u * u * (3.0 - 2.0 * u)

    def terraces(self, step, count, wl, width, seed=200, mask=None, qlo=0.15, qhi=0.9, jitter=0.1):
        """Stepped plateaus: ``count`` cliffs of ``step`` metres, each following a contour of a slow noise and rising over ``width`` metres: the layered scarps of Tempel 1, the cliffs of
        67P's big lobe. The cliffs are placed at quantiles of the noise so every level of detail has the same number of them."""
        o = self.octs(wl, 3)
        if not o:
            return
        q = self.warped(wl * 0.8, wl * 0.3, seed + 40)
        n = fbm3(q / wl, self.seed + seed, o)
        if self.octs(wl * 0.15, 2):
            n = n + jitter * 0.4 * fbm3(q / (wl * 0.15), self.seed + seed + 5, 2)
        w = self.rough if mask is None else mask
        tot = np.zeros(len(n))
        ths = np.quantile(n, np.linspace(qlo, qhi, count))
        for th in ths:
            r = self.riser(n, th, width)
            tot += r
            self.cliff = np.maximum(self.cliff, 4.0 * r * (1.0 - r) * w)
        self.h += step * (tot - np.median(tot)) * w

    def blocks(self, amp, wl, seed=300, mask=None):
        o = self.octs(wl, 3)
        if not o:
            return
        q = self.warped(wl, wl * 0.35, seed + 40)
        r = ridged3(q / wl, self.seed + seed, o)
        b = fbm3(q / (wl * 0.6), self.seed + seed + 7, max(1, o - 1))
        w = self.rough if mask is None else mask
        self.h += amp * (0.8 * (r - 0.35) + 0.5 * b) * w

    def rubble(self, amp, wls, floor=0.3, seed=700, mask=None):
        """Mid-scale roughness all over the rock: sharp-crested ridged noise at a ladder of wavelengths, strongest where the terrain is rough, weaker (``floor``) on smooth ground, absent in dust."""
        w = floor + (1.0 - floor) * self.rough
        if mask is not None:
            w = w * mask
        for k, wl in enumerate(wls):
            if self.octs(wl, 1) == 0:
                continue
            q = self.warped(wl, wl * 0.5, seed + 7 * k)
            r = ridged3(q / wl, self.seed + seed + k, 2)
            self.h += amp * (wl / wls[0]) ** 0.85 * (r - 0.4) * w

    def plains(self, amp, wl, seed=400):
        o = self.octs(wl, 2)
        if o:
            self.h += amp * fbm3(self.warped(wl, wl * 0.3, seed + 40) / wl, self.seed + seed, o) * (1.0 - self.rough)

    def ripples(self, amp, wl, seed=450):
        """The fine corrugation of dust: only where this level can carry it."""
        o = self.octs(wl, 2)
        if o:
            self.h += amp * fbm3(self.P / wl, self.seed + seed, o) * (1.0 - self.rough * 0.7)

    def trenches(self, depth, wl, width=0.03, seed=500, mask=None):
        """Fractures: the zero set of a noise field is a network of curves, each cut into a narrow trench."""
        if self.octs(wl * width, 1) == 0 and width * wl < 1.5 * self.edge:
            width = 1.5 * self.edge / wl
        q = self.warped(wl, wl * 0.4, seed + 40)
        n = fbm3(q / wl, self.seed + seed, 2)
        band = np.exp(-((n / width) ** 2))
        w = np.ones_like(self.h) if mask is None else mask
        self.h -= depth * band * w * (0.4 + 0.6 * smoothstep(0.0, 1.0, fbm3(q / (wl * 0.5), self.seed + seed + 3, 2) * 0.5 + 0.5))

    def scarp(self, normal, offset, height, width=30.0, wobble=40.0, seed=600, mask=None):
        """A long cliff: the body's surface steps by ``height`` across a wavering plane (Hathor on 67P, the great scarps of Tempel 1)."""
        n = norm(np.asarray(normal, float))
        d = self.P @ n - offset + wobble * fbm3(self.P / 260.0, self.seed + seed, 2)
        s = smoothstep(-width, width, d)
        w = 1.0 if mask is None else mask
        self.h += height * (s - 0.5) * w
        self.cliff = np.maximum(self.cliff, 4.0 * s * (1.0 - s) * (w if mask is not None else 1.0))

    def strata(self, normal, spacing, count, height, width, offset=0.0, mask=None, wobble=3.0, seed=650):
        """Layering: ``count`` small cliffs of ``height`` metres a ``spacing`` apart along ``normal``, each a few metres wide: the stepped bedding of a fresh scarp (a layered wall)."""
        n = norm(np.asarray(normal, float))
        d = self.P @ n - offset + wobble * fbm3(self.P / 40.0, self.seed + seed, 2)
        w = 1.0 if mask is None else mask
        tot = np.zeros(len(d))
        for k in range(int(count)):
            r = smoothstep(k * spacing, k * spacing + width, d)
            tot += r
            self.cliff = np.maximum(self.cliff, 4.0 * r * (1.0 - r) * (w if mask is not None else 1.0))
        self.h += height * tot * w

    # -- craters and pits ----------------------------------------------------------------------------------------------
    def _x(self, centre, R, wobble=0.14, ellip=0.0):
        """Distance from ``centre`` in units of R, bent so no hole is a perfect circle: stretched along a direction of its own (``ellip``) and its rim wandering (``wobble``)."""
        d = self.P - centre
        if ellip > 0.0:
            rng = np.random.default_rng((int(abs(centre[0]) * 131 + abs(centre[1]) * 17 + abs(centre[2]) * 7) + self.seed) % 100003)
            u = norm(rng.standard_normal(3))
            t = d @ u
            d = d - u[None, :] * (t[:, None] * (1.0 - 1.0 / (1.0 + ellip)))
        x = np.linalg.norm(d, axis=1) / R
        if wobble and R > 3.0 * self.edge:
            x = x / (1.0 + wobble * fbm3(self.P / (R * 0.55), self.seed + 77, 2))
        return x

    def crater(self, centre, R, depth, floor=0.55, rim=0.22, apron=0.35, wobble=0.22, ellip=0.25):
        """A dish with a flat dusty floor (fraction ``floor`` of the radius), a steep wall and a raised, slightly rough rim, with an apron of ejecta falling off outside."""
        if R < 2.5 * self.edge:
            return
        x = self._x(np.asarray(centre, float), R, wobble, ellip)
        wall = smoothstep(floor, 1.0, x) ** 0.85
        bowl = np.where(x < 1.0, -depth * (1.0 - wall), 0.0)
        lip = rim * depth * np.exp(-(((x - 1.0) / 0.13) ** 2))
        ap = apron * rim * depth * np.exp(-np.maximum(x - 1.0, 0.0) / 0.8) * (x >= 1.0)
        self.h += bowl + lip + ap
        self.floor = np.maximum(self.floor, (1.0 - smoothstep(floor * 0.8, floor * 1.1, x)))
        self.cliff = np.maximum(self.cliff, smoothstep(floor, 0.85, x) * (1.0 - smoothstep(0.95, 1.1, x)) * min(1.0, depth / (0.25 * R)))

    def pit(self, centre, R, depth, floor=0.7, layers=4):
        """A sinkhole: a deep, steep-walled hole with a terraced wall (the pits of Seth, Wild 2's craters)."""
        if R < 2.5 * self.edge:
            return
        x = self._x(np.asarray(centre, float), R, 0.24, 0.3)
        u = 1.0 - smoothstep(floor, 1.0, x) ** 0.7                      # 1 on the floor, 0 at the rim
        k = float(layers)
        lay = (np.floor(u * k) + smoothstep(0.55, 1.0, u * k - np.floor(u * k))) / k
        lay = np.minimum(lay, 1.0)
        bowl = np.where(x < 1.0, -depth * lay, 0.0)
        lip = 0.12 * depth * np.exp(-(((x - 1.0) / 0.1) ** 2))
        self.h += bowl + lip
        self.floor = np.maximum(self.floor, 1.0 - smoothstep(floor * 0.85, floor * 1.05, x))
        self.cliff = np.maximum(self.cliff, smoothstep(floor, 0.9, x) * (1.0 - smoothstep(0.98, 1.1, x)))

    # -- dust --------------------------------------------------------------------------------------------------------------
    def fill_hollows(self, radius, strength=0.7, lo=1.5, hi=6.0):
        """Dust collects in every hollow: lift the floors of depressions toward the surface round them. Returns a 0..1 mask of where it did."""
        it = min(300, max(1, int(math.ceil(2.0 * (radius / self.edge) ** 2))))
        hs = lap_smooth(self.h, self.adj, it)
        d = hs - self.h
        hollow = smoothstep(lo, hi, d) * (1.0 - self.cliff * 0.7)
        self.h = self.h + strength * hollow * d
        self.hollow = np.maximum(self.hollow, hollow)
        return hollow

    def slope(self):
        a, b, _ = self.adj
        dh = np.abs(self.h[a] - self.h[b]) / np.maximum(np.linalg.norm(self.P[a] - self.P[b], axis=1), 1e-6)
        s = np.zeros(len(self.h))
        np.maximum.at(s, a, dh)
        np.maximum.at(s, b, dh)
        return s


class IceSites:
    """Patches of exposed ice and frost: centres with a radius, broken up by noise so no patch is round. Positions, not vertex numbers, so every level sees the same patches."""

    def __init__(self, sites, seed=9):
        self.sites = sites           # [(centre (3,), radius)]
        self.seed = seed

    def field(self, P, edge=0.0):
        out = np.zeros(len(P))
        for i, (c, r) in enumerate(self.sites):
            r = max(r, 2.6 * edge)                  # a patch the mesh can show: a few edges across
            d = np.linalg.norm(P - c, axis=1) / r
            m = np.where(d < 1.6, 1.0 - smoothstep(0.35, 1.0, d + 0.35 * fbm3(P / (r * 0.7) + i * 5.1, self.seed + i, 2)), 0.0)
            out = np.maximum(out, m)
        return out


# ------------------------------------------------------------------------------------------------ boulders


def boulder_pick(rng, terrain, count, rmin, rmax, clusters=0, cluster_frac=0.4, weights=None, tail=2.6):
    """Boulder records (centre on the surface, outward normal, radius, shape seed): drawn where ``weights`` (per vertex of ``terrain``) are high, a fraction of them gathered into clusters."""
    n = len(terrain.P)
    w = weights / weights.sum()
    idx = rng.choice(n, size=count, p=w)
    cen = terrain.P[idx] + terrain.N[idx] * terrain.h[idx, None]
    nor = terrain.N[idx]
    u = rng.uniform(0.02, 1.0, count)
    r = np.minimum(rmin * u ** (-1.0 / (tail - 1.0)), rmax)
    if clusters:
        k = int(count * cluster_frac)
        heads = rng.choice(count, size=clusters, replace=False)
        for j in range(k):
            h = heads[j % clusters]
            c = idx[h]
            # pull the boulder to a vertex near the cluster head (a random one among the head's neighbours at a few steps)
            cen[j] = cen[h] + (rng.standard_normal(3) * (3.0 * r[h] + 6.0 * rmin))
            # snap to the surface along the head's normal frame: project on the nearest vertex
            d = np.linalg.norm(terrain.P - cen[j], axis=1)
            v = int(np.argmin(d))
            cen[j] = terrain.P[v] + terrain.N[v] * terrain.h[v]
            nor[j] = terrain.N[v]
    seeds = rng.integers(1, 100000, count)
    return [dict(c=cen[i], n=nor[i], r=float(r[i]), seed=int(seeds[i])) for i in range(count)]


def boulder_mesh(b, level, squash=0.62, sink=0.32):
    """One boulder: a lumpy, rounded-box blob, flattened toward the surface and sunk into it. -> (V, F) with the faces below the surface left out."""
    V, F = icosphere(level)
    rng = np.random.default_rng(b["seed"])
    p = rng.uniform(2.0, 4.2)
    v = V / np.power((np.abs(V) ** p).sum(axis=1), 1.0 / p)[:, None]               # rounded box
    v = v * (1.0 + 0.22 * fbm3(V * 2.1 + b["seed"] % 97, b["seed"], 3))[:, None]
    n = norm(b["n"])
    up = np.array([0, 0, 1.0]) if abs(n[2]) < 0.9 else np.array([1.0, 0, 0])
    t1 = norm(np.cross(up, n))
    a = rng.uniform(0, math.tau)
    t2 = np.cross(n, t1)
    t1, t2 = math.cos(a) * t1 + math.sin(a) * t2, -math.sin(a) * t1 + math.cos(a) * t2
    t2 = np.cross(n, t1)
    r = b["r"]
    sc = np.array([r * rng.uniform(0.8, 1.35), r * rng.uniform(0.7, 1.2), r * squash * rng.uniform(0.8, 1.3)])
    L = v * sc
    L[:, 2] -= sc[2] * sink * 2.0
    W = b["c"] + L[:, 0:1] * t1 + L[:, 1:2] * t2 + L[:, 2:3] * n
    keep = L[F][:, :, 2].max(axis=1) > -sc[2] * 0.15
    return W, F[keep]


# ------------------------------------------------------------------------------------------------ one level of one body


def assemble(V, F, dust, ice, tile, detail, seam_smooth=7):
    """Weld by (vertex, projection plane): -> dict(pos, nrm, uv0, uv1, col, tris (k, 3)). ``col`` is the vertex colour the surface material blends its layers by: R the dust's weight, G the ice's."""
    N = vertex_normals(V, F)
    sn = vertex_normals(V, F, smooth=seam_smooth)
    fn = sn[F].sum(axis=1)
    ax = np.argmax(np.abs(fn), axis=1)
    sg = np.sign(fn[np.arange(len(fn)), ax])
    sg[sg == 0] = 1.0
    key = F.reshape(-1).astype(np.int64) * 6 + np.repeat(ax * 2 + (sg > 0), 3)
    uniq, inv = np.unique(key, return_inverse=True)
    vi = uniq // 6
    plane = uniq % 6
    axp = plane // 2
    sgp = np.where(plane % 2 == 1, 1.0, -1.0)
    P = V[vi]
    pairs = np.array([(1, 2), (0, 2), (0, 1)])
    shifts = np.array([(0.00, 0.00), (0.37, 0.61), (0.73, 0.29)])
    ca = pairs[axp, 0]
    cb = pairs[axp, 1]
    u = P[np.arange(len(P)), ca] * sgp
    v = P[np.arange(len(P)), cb]
    uv0 = np.stack([u / tile + shifts[axp, 0], v / tile + shifts[axp, 1]], axis=1)
    uv1 = np.stack([u / detail, v / detail], axis=1)
    col = np.stack([dust[vi], ice[vi], np.zeros(len(vi)), np.ones(len(vi))], axis=1)
    return dict(pos=P.astype(np.float32), nrm=N[vi].astype(np.float32), uv0=uv0.astype(np.float32), uv1=uv1.astype(np.float32), col=col.astype(np.float32), tris=inv.reshape(-1, 3))


def build_level(spec, level, boulders=None, min_r=0.0):
    """One level of detail of one body from its spec (art_src/comets/build_comets.py: ``shape``, ``seed``, ``terrain(T, shape, level)``, ``ice`` (IceSites or None), ``boulders`` (count, rmin, rmax,
    ...), the material thresholds). ``boulders`` (level 0's records) are reused at coarser levels, thinned to what the level can carry; None draws them. -> dict(V, F, mat, boulders, edge, ...)."""
    shape = spec["shape"]
    seed = spec["seed"]
    dirs, F = icosphere(level)
    r0 = shape.radial(dirs)
    P0 = dirs * r0[:, None]
    N0 = shape.normal(P0)
    edge = mean_edge(P0, F)
    T = Terrain(P0, N0, F, seed, edge)
    spec["terrain"](T, shape, level)
    V = P0 + N0 * T.h[:, None]
    dust_v = smoothstep(spec.get("dust_lo", 0.35), spec.get("dust_hi", 0.85), np.maximum(spec.get("dust_plains", 0.85) * (1.0 - T.rough), T.hollow))
    ice_v = np.zeros(len(V))
    if spec.get("ice") is not None:
        ice_v = spec["ice"].field(T.P, edge)             # distances on the base surface: a site sits at the foot of a crater, not at its displaced floor
    ice_v = smoothstep(spec.get("ice_lo", 0.2), spec.get("ice_hi", 0.7), ice_v)
    dust_v = dust_v * (1.0 - ice_v)
    allV, allF, allD, allI = [V], [F], [dust_v], [ice_v]
    off = len(V)
    sb = spec.get("boulders")
    if boulders is None and sb:
        rng = np.random.default_rng(seed * 31 + 5)
        slope = T.slope()
        w = sb.get("base", 0.2) + sb.get("cliff", 3.0) * np.maximum(T.cliff, smoothstep(0.3, 1.0, slope)) + sb.get("hollow", 1.0) * T.hollow + sb.get("floor", 0.6) * T.floor
        w = w * (0.35 + 0.65 * T.rough + sb.get("plains", 0.4) * (1.0 - T.rough))
        boulders = boulder_pick(rng, T, sb["count"], sb["rmin"], sb["rmax"], sb.get("clusters", 0), sb.get("cluster_frac", 0.4), w, sb.get("tail", 2.6))
    nb = 0
    if boulders:
        r_min = min_r
        for b in boulders:
            if b["r"] < r_min:
                continue
            lv = 2 if (b["r"] > (sb or {}).get("lv2_r", 5.0) and level >= 5) else 1
            W, Fb = boulder_mesh(b, lv)
            if len(Fb) == 0:
                continue
            allV.append(W)
            allF.append(Fb + off)
            icy = float(np.random.default_rng(b["seed"]).random() < spec.get("ice_boulders", 0.0))
            allD.append(np.zeros(len(W)))
            allI.append(np.full(len(W), icy))
            off += len(W)
            nb += 1
    return dict(V=np.concatenate(allV), F=np.concatenate(allF), dust=np.concatenate(allD), ice=np.concatenate(allI), edge=edge, T=T, boulders=boulders, n_boulders=nb, base_verts=len(V), base_faces=len(F),
                dust_frac=float((dust_v[F].mean(axis=1) > 0.5).mean()), ice_frac=float((ice_v[F].mean(axis=1) > 0.5).mean()),
                stats=dict(rough=float(T.rough.mean()), h_lo=float(np.percentile(T.h, 2)), h_hi=float(np.percentile(T.h, 98)), cliff=float((T.cliff > 0.3).mean()), hollow=float((T.hollow > 0.3).mean()), floor=float((T.floor > 0.3).mean())))


# ------------------------------------------------------------------------------------------------ Blender


def blender_mesh(name, data, mat_name):
    """A Blender mesh from ``assemble`` data: one material slot, smooth, with split normals, both UV sets and the vertex colour "Col" (R dust, G ice)."""
    pos, nrm, uv0, uv1, col, tris = data["pos"], data["nrm"], data["uv0"], data["uv1"], data["col"], data["tris"]
    mesh = bpy.data.meshes.new(name)
    n = len(pos)
    mesh.vertices.add(n)
    mesh.vertices.foreach_set("co", pos.reshape(-1).astype(np.float32))
    m = len(tris)
    mesh.loops.add(m * 3)
    mesh.loops.foreach_set("vertex_index", tris.reshape(-1).astype(np.int32))
    mesh.polygons.add(m)
    mesh.polygons.foreach_set("loop_start", (np.arange(m, dtype=np.int32) * 3))
    mesh.polygons.foreach_set("material_index", np.zeros(m, np.int32))
    mesh.polygons.foreach_set("use_smooth", np.ones(m, dtype=bool))
    mesh.update(calc_edges=True)
    uvl = mesh.uv_layers.new(name="UVMap")
    uvl.data.foreach_set("uv", uv0[tris.reshape(-1)].reshape(-1).astype(np.float32))
    uvd = mesh.uv_layers.new(name="UVDetail")
    uvd.data.foreach_set("uv", uv1[tris.reshape(-1)].reshape(-1).astype(np.float32))
    ca = mesh.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="POINT")
    ca.data.foreach_set("color", col.reshape(-1).astype(np.float32))
    mesh.normals_split_custom_set_from_vertices([tuple(x) for x in nrm])
    mesh.materials.append(bpy.data.materials.get(mat_name) or bpy.data.materials.new(mat_name))
    return mesh


def preview_material(name):
    """The three layers as Cycles knows them: crust, dust and ice mixed by the vertex colour, as the game's layered material mixes them."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    mix1 = nt.nodes.new("ShaderNodeMix")
    mix1.data_type = "RGBA"
    mix2 = nt.nodes.new("ShaderNodeMix")
    mix2.data_type = "RGBA"
    nt.links.new(attr.outputs["Color"], sep.inputs["Color"])
    mix1.inputs[6].default_value = PREVIEW_COLOURS["crust"]
    mix1.inputs[7].default_value = PREVIEW_COLOURS["dust"]
    mix2.inputs[7].default_value = PREVIEW_COLOURS["ice"]
    nt.links.new(sep.outputs["Red"], mix1.inputs[0])
    nt.links.new(sep.outputs["Green"], mix2.inputs[0])
    nt.links.new(mix1.outputs[2], mix2.inputs[6])
    nt.links.new(mix2.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.85
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return m


PREVIEW_COLOURS = {"crust": (0.20, 0.185, 0.17, 1), "dust": (0.30, 0.265, 0.23, 1), "ice": (0.62, 0.68, 0.76, 1)}


def render_preview(out_dir, name, obj, radius, views=((70, 20, 0), (110, -10, 40), (35, 40, 200), (160, 0, 120)), size=(800, 800), samples=40, sun_strength=1.8):
    """Cycles (CPU, a few seconds) with one hard Sun and a black sky, the light a comet really has. views: (phase degrees from the Sun, elevation, azimuth)."""
    import os
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    world = bpy.data.worlds.new("w") if scene.world is None else scene.world
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.002, 0.003, 0.005, 1)
    bg.inputs[1].default_value = 1.0
    for o in [o for o in bpy.data.objects if o.type in ("LIGHT", "CAMERA")]:
        bpy.data.objects.remove(o, do_unlink=True)
    sun_d = bpy.data.lights.new("sun", "SUN")
    sun_d.energy = sun_strength
    sun = bpy.data.objects.new("sun", sun_d)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (0.0, 0.0, 0.0)                                                         # a Sun lamp shines down its -z: the Sun sits at +z, the camera swings round it
    cam_d = bpy.data.cameras.new("cam")
    cam_d.lens = 85.0
    cam_d.clip_start, cam_d.clip_end = 1.0, 100000.0
    cam = bpy.data.objects.new("cam", cam_d)
    scene.collection.objects.link(cam)
    scene.camera = cam
    dist = radius * 3.6
    for i, (phase, elev, azim) in enumerate(views):
        # the Sun is at +z; the camera sits at ``phase`` degrees from it, round by ``azim``
        ph = math.radians(phase)
        az = math.radians(azim)
        el = math.radians(elev)
        c = mathutils.Vector((math.sin(ph) * math.cos(az), math.sin(ph) * math.sin(az), math.cos(ph)))
        c = (c + mathutils.Vector((0, 0, 0))).normalized()
        cam.location = c * dist
        cam.rotation_euler = (-c).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"{name}_{i}.png")
        bpy.ops.render.render(write_still=True)
        print(f"[MN] preview {scene.render.filepath}")
