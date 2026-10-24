"""mnkit: the hard-surface part kit for scripted capital ships (art_src/PIPELINE.md). One ``Kit`` per LOD build.

Scales it builds at (a capital ship needs all of them to read as enormous):
    silhouette  10 to 100 m   lofted hull segments, collars, fins, booms, radiator stacks, prow lances        loft(), ring(), fin(), boom()
    structure   2 to 10 m     armour plates in rows with gaps, step plates, turrets, nozzles, sensor arrays    plate_patch(), flak_turret() ...
    detail      0.2 to 2 m    window rows, hatches, vents, conduits, fastener lines, light strips, labels     strip(), decal()
    surface     under 0.2 m   the plate tile, the trim sheet's scratches and chips, and the tiling detail map (in the textures)

LOD rule used throughout: LOD0 builds everything; LOD1 builds the carcass and strips and the big structures but no plate slabs
and no subsystems (they are separate objects); LOD2 builds only silhouette blocks (plus the subsystems as boxes).
"""
import math
import zlib

import numpy as np

from . import mnmesh
from .mnmesh import Frame, Patch, norm, cross, chamfered_rect, EDGE_NAMES

PLATE, TRIM, LIGHTS, ENGINE = 0, 1, 2, 3          # material slots, in the order the model script lists them

# running-light palette (earth_lights_emissive.png): 8 swatches of 16 px
LIGHT_WHITE, LIGHT_COLD, LIGHT_RED, LIGHT_GREEN, LIGHT_AMBER, LIGHT_BLUE, LIGHT_EMBER, LIGHT_VIOLET = range(8)
LIGHT_RGB = {LIGHT_WHITE: (1, 1, 1), LIGHT_COLD: (0.8, 0.9, 1), LIGHT_RED: (1, 0.07, 0.05), LIGHT_GREEN: (0.07, 1, 0.22), LIGHT_AMBER: (1, 0.62, 0.18),
             LIGHT_BLUE: (0.25, 0.45, 1), LIGHT_EMBER: (1, 0.36, 0.08), LIGHT_VIOLET: (0.6, 0.35, 1)}


def station(y, hw, hh, c, cx=0.0, cz=0.0):
    return {"y": y, "hw": hw, "hh": hh, "c": c, "cx": cx, "cz": cz}


def lerp_station(a, b, t):
    return {k: a[k] + (b[k] - a[k]) * t for k in a}


def complement(lo, hi, gaps, min_len=1.0):
    """Intervals of [lo, hi] not covered by ``gaps`` (list of (a, b))."""
    out, cur = [], lo
    for a, b in sorted(gaps):
        if a - cur >= min_len:
            out.append((cur, a))
        cur = max(cur, b)
    if hi - cur >= min_len:
        out.append((cur, hi))
    return out


class Kit:
    def __init__(self, ctx, trims, seed=1):
        self.ctx = ctx
        self.lod = ctx.lod
        self.hull = ctx.hull
        self.trims = trims["pieces"]
        self.seed = seed

    # ---------------------------------------------------------------------------------------------- basics
    def rng(self, *key):
        return np.random.default_rng(zlib.crc32(repr((self.seed,) + key).encode()))

    def rect(self, name):
        return self.trims[name]["uv"]

    def piece_m(self, name):
        return self.trims[name]["metres"]

    def _const_uv(self, rect, n):
        u = (rect[0] + rect[2]) * 0.5
        v = (rect[1] + rect[3]) * 0.5
        return np.full((n, 4, 2), (u, v))

    def light_rect(self, idx):
        return ((idx * 16 + 3) / 128.0, 0.1, (idx * 16 + 13) / 128.0, 0.9)

    # ---------------------------------------------------------------------------------------------- hull carcass
    def loft(self, stations, caps=(True, True), mb=None, carcass=True):
        """A faceted hull: chamfered rectangles at each station, joined by planar bands. Returns a list (per band) of dicts
        edge name -> Patch (edges: tr, r, br, b, bl, l, tl, t). The carcass is drawn dark (the underlayer) so panel gaps read as depth;
        at LOD1+ it is drawn with the plate tile and the plating (LOD0 only) is skipped."""
        mb = mb or self.hull
        rings = []
        for s in stations:
            prof = chamfered_rect(s["hw"], s["hh"], s["c"])
            rings.append(np.array([(x + s["cx"], s["y"], z + s["cz"]) for x, z in prof]))
        bands = []
        for i in range(len(stations) - 1):
            d = {}
            for e in range(8):
                a, b = e, (e + 1) % 8
                d[EDGE_NAMES[e]] = Patch(rings[i][a], rings[i][b], rings[i + 1][b], rings[i + 1][a], EDGE_NAMES[e])
            bands.append(d)
            if carcass:
                self._carcass(mb, list(d.values()))
        for end, on in enumerate(caps):
            if not on:
                continue
            ring = rings[0] if end == 0 else rings[-1]
            c = ring.mean(axis=0)
            n = np.array([0, -1.0, 0]) if end == 0 else np.array([0, 1.0, 0])
            k = len(ring)
            A = np.repeat(c[None], k, 0)
            B = ring[np.arange(k)] if end == 1 else ring[(np.arange(k) + 1) % k]
            C = ring[(np.arange(k) + 1) % k] if end == 1 else ring[np.arange(k)]
            uv = np.stack([np.stack([p[:, 0], p[:, 2]], axis=1) / 12.0 for p in (A, B, C)], axis=1)
            mb.add_raw(np.stack([A, B, C], axis=1).reshape(-1, 3), np.tile(n, (k * 3, 1)), uv.reshape(-1, 2), (uv * 6).reshape(-1, 2),
                       (np.arange(k)[:, None] * 3 + np.array([0, 1, 2])).reshape(-1, 3), PLATE if self.lod > 0 else PLATE)
        return bands

    def _carcass(self, mb, patches):
        P = np.array([[p.p00, p.p10, p.p11, p.p01] for p in patches])
        if self.lod >= 1:
            mb.quads_metric(P, PLATE, 12.0, np.zeros((len(P), 2)), 2.0)
        else:
            rect = self.rect("underlayer")
            n = len(P)
            N = norm(cross(P[:, 2] - P[:, 0], P[:, 3] - P[:, 1]))
            eu = norm(P[:, 1] - P[:, 0] + P[:, 2] - P[:, 3])
            ev = norm(cross(N, eu))
            rel = P - P[:, 0:1]
            st = np.stack([np.einsum("nkc,nc->nk", rel, eu), np.einsum("nkc,nc->nk", rel, ev)], axis=2)
            mb.quads(P, self._const_uv(rect, n), st / 2.0, TRIM, N)

    # ---------------------------------------------------------------------------------------------- plating
    def plate_patch(self, patch, key, u_range=None, v_range=None, rows=(5.0, 12.0), cols=(3.0, 8.0), gap=0.14, height=(0.12, 0.34), chamfer=0.12,
                    margin=0.2, sub_prob=0.0, sub=(1.2, 3.2), sub_height=(0.04, 0.10), stagger=True, step=0.0, mb=None):
        """Armour plates over a planar patch: rows along the ship, columns across, every plate its own height and its own place in the
        plate tile. ``u_range``/``v_range`` limit the region in metres. Larger plates may carry smaller ones (``sub_prob``): the layered look."""
        self.last_cells = np.zeros((0, 5))
        if self.lod >= 1:
            return 0
        mb = mb or self.hull
        rng = self.rng("plate", key)
        Lu, Lv = patch.lu, patch.lv
        ua, ub = (0.0, Lu) if u_range is None else u_range
        va, vb = (0.0, Lv) if v_range is None else v_range
        ua, ub, va, vb = ua + margin, ub - margin, va + margin, vb - margin
        if ub - ua < 0.8 or vb - va < 0.8:
            return 0
        cells = []
        v = va
        ri = 0
        while v < vb - 0.5:
            rl = rng.uniform(*rows)
            if vb - (v + rl) < rows[0] * 0.6:
                rl = vb - v
            u = ua
            first = True
            while u < ub - 0.4:
                cw = rng.uniform(*cols)
                if first and stagger:
                    cw *= rng.uniform(0.35, 1.0)
                first = False
                if ub - (u + cw) < cols[0] * 0.55:
                    cw = ub - u
                cells.append((u, u + cw, v, v + rl, ri))
                u += cw
            v += rl
            ri += 1
        if not cells:
            return 0
        C = np.array(cells)
        g = gap * 0.5
        a0, a1, b0, b1 = C[:, 0] + g, C[:, 1] - g, C[:, 2] + g, C[:, 3] - g
        ok = (a1 - a0 > 0.4) & (b1 - b0 > 0.4)
        a0, a1, b0, b1, ri_ = a0[ok], a1[ok], b0[ok], b1[ok], C[ok, 4]
        m = len(a0)
        if m == 0:
            return 0
        uu = np.stack([a0, a1, a1, a0], axis=1) / Lu
        vv = np.stack([b0, b0, b1, b1], axis=1) / Lv
        P = patch.pt(uu, vv)                                    # (m, 4, 3)
        n = np.tile(patch.normal(), (m, 1))
        h = rng.uniform(*height, size=m) + step * ri_
        ch = np.minimum(chamfer, h * 0.9)
        off = rng.uniform(0, 1, size=(m, 2))
        mb.slabs(P, n, h, ch, PLATE, TRIM, tile=12.0, tile_off=off, edge_rect=self.rect("edge_paint"), edge_tile=8.0)
        self.last_cells = np.stack([a0, a1, b0, b1, h], axis=1)
        count = m
        if sub_prob > 0:
            big = ((a1 - a0) > sub[1] * 1.6) & ((b1 - b0) > sub[1] * 1.6) & (rng.random(m) < sub_prob)
            subs = []
            for i in np.nonzero(big)[0]:
                sw = rng.uniform(*sub)
                sl = rng.uniform(*sub) * 1.4
                su = rng.uniform(a0[i] + 0.4, max(a0[i] + 0.4, a1[i] - sw - 0.4))
                sv = rng.uniform(b0[i] + 0.4, max(b0[i] + 0.4, b1[i] - sl - 0.4))
                subs.append((su, su + sw, sv, sv + min(sl, b1[i] - sv - 0.3), h[i]))
            if subs:
                S = np.array(subs)
                suu = np.stack([S[:, 0], S[:, 1], S[:, 1], S[:, 0]], axis=1) / Lu
                svv = np.stack([S[:, 2], S[:, 2], S[:, 3], S[:, 3]], axis=1) / Lv
                SP = patch.pt(suu, svv) + patch.normal() * S[:, 4][:, None, None]
                sh = rng.uniform(*sub_height, size=len(S))
                mb.slabs(SP, np.tile(patch.normal(), (len(S), 1)), sh, np.minimum(0.06, sh * 0.9), PLATE, TRIM, tile=12.0,
                         tile_off=rng.uniform(0, 1, size=(len(S), 2)), edge_rect=self.rect("edge_paint"), edge_tile=8.0)
                count += len(S)
        return count

    # ---------------------------------------------------------------------------------------------- trim strips and decals
    def strip(self, p0, run, n, length, name, lift=0.03, scale=1.0, mb=None, flip=False):
        """Lay a trim-sheet strip: bottom-left corner at ``p0``, running ``length`` metres along ``run`` (a unit vector in the surface), on a
        surface facing ``n``. The piece's up is n x run, so choose ``run`` = up x n for a flank (windows upright). Tiling strips repeat by
        cutting quads, each at most one piece-width long. Returns the number of quads."""
        mb = mb or self.hull
        info = self.trims[name]
        rect = info["uv"]
        pw, ph = info["metres"]
        pw, ph = pw * scale, ph * scale
        x = norm(run)
        n = norm(n)
        y = cross(n, x)
        p0 = np.asarray(p0, np.float64) + n * lift
        pos, count = 0.0, 0
        while pos < length - 1e-3:
            seg = min(pw, length - pos)
            span = seg / pw
            a = p0 + x * pos
            mb.quad_rect(a, a + x * seg, a + x * seg + y * ph, a + y * ph, rect, TRIM, uspan=(0.0, span), flip=flip)
            count += 1
            pos += seg
            if not info["tile_u"]:
                break
        return count

    def decal(self, centre, run, n, name, scale=1.0, lift=0.04, mb=None, size=None):
        """A single trim piece centred at ``centre`` on a surface facing ``n``; its x runs along ``run``, its up is n x run."""
        if self.lod >= 1:
            return
        mb = mb or self.hull
        info = self.trims[name]
        pw, ph = info["metres"]
        if size is not None:
            pw, ph = size
        else:
            pw, ph = pw * scale, ph * scale
        x = norm(run)
        n = norm(n)
        y = cross(n, x)
        c = np.asarray(centre, np.float64) + n * lift
        a = c - x * pw / 2 - y * ph / 2
        mb.quad_rect(a, a + x * pw, a + x * pw + y * ph, a + y * ph, info["uv"], TRIM)

    # ---------------------------------------------------------------------------------------------- lights
    def light(self, pos, normal, colour=LIGHT_COLD, size=0.5, kind="running", blink=None, record=True):
        """A small emissive lamp on a short post: a bezel box with a lit face. Recorded in the manifest."""
        n = norm(normal)
        up = np.array([0, 0, 1.0]) if abs(n[2]) < 0.9 else np.array([0, 1.0, 0])
        fr = Frame(pos, n, up)
        if self.lod <= 1:
            self.hull.box(fr, (size, size * 0.55, size), LIGHTS, centre=(0, size * 0.25, 0), rect=self.light_rect(colour), caps=True)
        if record and self.lod == 0:
            self.ctx.ship.add_light(pos, kind, LIGHT_RGB[colour], size, blink)

    # ---------------------------------------------------------------------------------------------- solid parts
    def ring(self, y0, y1, outer, inner=None, centre=(0.0, 0.0), mb=None, rect_name="chrome_brushed", bevel=0.6):
        """A chrome collar round the hull: outer = (hw, hh, c). A stepped rim: the band itself and a thinner raised lip at each face."""
        mb = mb or self.hull
        rect = self.rect(rect_name)
        hw, hh, c = outer
        cx, cz = centre
        prof = chamfered_rect(hw, hh, c)
        fr = Frame((cx, 0, cz), (0, 1, 0), (0, 0, 1))
        mb.extrude(prof, fr, y0 + bevel, y1 - bevel, TRIM, rect=rect, caps=(False, False))
        # lips (stepped bevel) at each end: slightly smaller prisms
        for ya, yb, s0, s1 in ((y0, y0 + bevel, (1 - bevel / hw * 1.4, 1 - bevel / hh * 1.4), (1.0, 1.0)), (y1 - bevel, y1, (1.0, 1.0), (1 - bevel / hw * 1.4, 1 - bevel / hh * 1.4))):
            mb.extrude(prof, fr, ya, yb, TRIM, s0=s0, s1=s1, rect=rect, caps=(False, False))
        # front and back faces: dark annulus between the lip and the hull
        if inner is not None:
            ihw, ihh, ic = inner
            iprof = chamfered_rect(ihw, ihh, ic)
            for y, facing in ((y0, -1.0), (y1, 1.0)):
                k = len(prof)
                P = np.array([[ (prof[i][0] * (1 - bevel / hw * 1.4) + cx, y, prof[i][1] * (1 - bevel / hh * 1.4) + cz),
                               (prof[(i + 1) % k][0] * (1 - bevel / hw * 1.4) + cx, y, prof[(i + 1) % k][1] * (1 - bevel / hh * 1.4) + cz),
                               (iprof[(i + 1) % k][0] + cx, y, iprof[(i + 1) % k][1] + cz), (iprof[i][0] + cx, y, iprof[i][1] + cz)] for i in range(k)])
                if facing < 0:
                    P = P[:, ::-1]
                mb.quads_metric(P, PLATE, 12.0)

    def pipe(self, p0, p1, radius, mat=TRIM, rect_name="chrome_brushed", sides=8, mb=None, caps=True):
        mb = mb or self.hull
        d = np.asarray(p1, float) - np.asarray(p0, float)
        ln = np.linalg.norm(d)
        up = np.array([0, 0, 1.0]) if abs(d[2] / ln) < 0.9 else np.array([1.0, 0, 0])
        fr = Frame(p0, d, up)
        prof = mnmesh.regular_poly(sides, radius)
        mb.extrude(prof, fr, 0, ln, mat, rect=self.rect(rect_name), caps=(caps, caps))

    def slab_box(self, frame, size, centre=(0, 0, 0), chamfer=0.0, mat=PLATE, tile=12.0, mb=None, rect=None, caps=True, off=(0, 0)):
        (mb or self.hull).box(frame, size, mat, centre=centre, tile=tile, chamfer=chamfer, rect=rect, caps=caps, off=off)

    # ---------------------------------------------------------------------------------------------- larger structures
    def fin(self, poly_yz, x, thickness, mb=None, rim="chrome_brushed"):
        """A fin or spine: a polygon in the (y, z) plane (ship space) extruded along x. Big faces take the plate tile, the rim is chrome."""
        mb = mb or self.hull
        fr = Frame((x, 0, 0), (1, 0, 0), (0, 0, 1))
        prof = [(-y, z) for y, z in poly_yz]                 # local x runs toward -y
        mb.extrude(prof, fr, -thickness / 2, thickness / 2, TRIM, rect=self.rect(rim), caps=(True, True), cap_mat=PLATE, tile=12.0)

    def truss(self, p0, p1, radius=1.5, bay=5.0, rod=0.18, mb=None):
        """A triangular truss between two points: three longerons, a ring of struts and a diagonal in every bay."""
        mb = mb or self.hull
        p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
        d = p1 - p0
        L = np.linalg.norm(d)
        ax = d / L
        a = _perp_vec(ax)
        b = cross(ax, a)
        n_bays = max(1, int(round(L / bay)))
        sides = 5 if self.lod == 0 else 3
        rings = []
        for i in range(n_bays + 1):
            c = p0 + d * (i / n_bays)
            rings.append([c + radius * (math.cos(t) * a + math.sin(t) * b) for t in (0, 2 * math.pi / 3, 4 * math.pi / 3)])
        for k in range(3):
            self.pipe(rings[0][k], rings[-1][k], rod * 1.3, mb=mb, sides=sides, caps=False)
        if self.lod == 0:
            for i, r in enumerate(rings):
                for k in range(3):
                    self.pipe(r[k], r[(k + 1) % 3], rod, mb=mb, sides=4, caps=False)
                if i < n_bays:
                    for k in range(3):
                        self.pipe(r[k], rings[i + 1][(k + 1) % 3], rod, mb=mb, sides=4, caps=False)

    def tile_piece(self, name, origin, xdir, ydir, w, h, mb=None, lift=0.0, scale=1.0):
        """Cover a rectangle (origin, xdir x ydir, w x h metres) with a non-tiling trim piece repeated, alternate tiles mirrored.
        The quads face along xdir x ydir."""
        mb = mb or self.hull
        info = self.trims[name]
        pw, ph = info["metres"]
        pw, ph = pw * scale, ph * scale
        x, y = norm(xdir), norm(ydir)
        n = cross(x, y)
        nx, ny = max(1, int(round(w / pw))), max(1, int(round(h / ph)))
        sw, sh = w / nx, h / ny
        o = np.asarray(origin, float) + n * lift
        for i in range(nx):
            for j in range(ny):
                a = o + x * (i * sw) + y * (j * sh)
                mb.quad_rect(a, a + x * sw, a + x * sw + y * sh, a + y * sh, info["uv"], TRIM, flip=bool((i + j) % 2))

    def scatter_details(self, patch, cells, key, hatch=0.30, fasteners=0.18, min_size=(2.6, 3.4)):
        """Hatches, vents and fastener lines on the tops of the plates just laid (cells from plate_patch), so no big plate is bare.
        Orientation: the piece x runs along the ship."""
        if self.lod >= 1 or len(cells) == 0:
            return
        rng = self.rng("details", key)
        n = patch.normal()
        Lu, Lv = patch.lu, patch.lv
        run = patch.ev()
        for a0, a1, b0, b1, h in cells:
            w, l = a1 - a0, b1 - b0
            r = rng.random()
            if w >= min_size[0] and l >= min_size[1] and r < hatch:
                name = rng.choice(["hatch_a", "hatch_a", "hatch_b", "vent"])
                pw, ph = self.trims[name]["metres"]
                sc = float(min(1.0, (w - 0.8) / ph, (l - 0.8) / pw, 1.4))
                if sc < 0.45:
                    continue
                cu = rng.uniform(a0 + ph * sc / 2 + 0.3, max(a0 + ph * sc / 2 + 0.31, a1 - ph * sc / 2 - 0.3))
                cv = rng.uniform(b0 + pw * sc / 2 + 0.3, max(b0 + pw * sc / 2 + 0.31, b1 - pw * sc / 2 - 0.3))
                c = patch.pt(cu / Lu, cv / Lv) + n * (h + 0.02)
                self.decal(c, run, n, name, scale=sc, lift=0.0)
            elif l >= 5.0 and w >= 2.0 and r < hatch + fasteners:
                pu = a0 + 0.35 if rng.random() < 0.5 else a1 - 0.55
                p0 = patch.pt(pu / Lu, (b0 + 0.4) / Lv) + n * (h + 0.015)
                self.strip(p0, run, n, l - 0.8, "fasteners", lift=0.0, scale=0.7)

    def frame_on(self, point, normal, forward=(0, 1, 0)):
        """A frame sitting on a surface: origin at the point, local z = the surface normal, local y = ``forward`` projected onto the surface."""
        n = norm(normal)
        f = np.asarray(forward, float)
        f = f - n * np.dot(f, n)
        if np.linalg.norm(f) < 1e-6:
            f = _perp_vec(n)
        return Frame(point, f, n)


def _perp_vec(n):
    n = norm(n)
    a = np.array([0, 0, 1.0]) if abs(n[2]) < 0.9 else np.array([1.0, 0, 0])
    return norm(cross(a, n))

