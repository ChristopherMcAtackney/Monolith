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

PLATE, TRIM, LIGHTS, ENGINE, CHROME, PLATE_B, PLATE_C, PLUME, PLATE_D, PLATE_E, WEAR, PAINT = 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11          # material slots, in the order the model script lists them

# running-light palette (earth_lights_emissive.png): 8 swatches of 16 px
# the five paint tones, darkest to lightest: deep black, graphite (the default), mid grey, light grey, pale cool grey
TONE_MATS = (PLATE_B, PLATE, PLATE_C, PLATE_D, PLATE_E)
TONE_DOMINANT = (0.10, 0.34, 0.34, 0.16, 0.06)

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
        self.trims_all = trims
        self.paint_info = trims.get("paint", {})
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
                    margin=0.2, sub_prob=0.0, sub=(1.2, 3.2), sub_height=(0.04, 0.10), stagger=True, step=0.0, mb=None, min_size=0.4):
        """Armour plates over a planar patch: rows along the ship, columns across, every plate its own height and its own place in the
        plate tile. ``u_range``/``v_range`` limit the region in metres. Larger plates may carry smaller ones (``sub_prob``): the layered look.
        ``min_size`` is the smallest plate side in metres (0.4 for capital hulls; fighters use about 0.2)."""
        self.last_cells = np.zeros((0, 5))
        self.last_tones = np.zeros(0, int)
        if self.lod >= 1:
            return 0
        mb = mb or self.hull
        rng = self.rng("plate", key)
        Lu, Lv = patch.lu, patch.lv
        ua, ub = (0.0, Lu) if u_range is None else u_range
        va, vb = (0.0, Lv) if v_range is None else v_range
        ua, ub, va, vb = ua + margin, ub - margin, va + margin, vb - margin
        if ub - ua < 2 * min_size or vb - va < 2 * min_size:
            return 0
        cells = []
        v = va
        ri = 0
        while v < vb - 1.25 * min_size:
            rl = rng.uniform(*rows)
            if vb - (v + rl) < rows[0] * 0.6:
                rl = vb - v
            u = ua
            first = True
            while u < ub - min_size:
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
        ok = (a1 - a0 > min_size) & (b1 - b0 > min_size)
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
        # paint: every region (a hull flat about as long as the ship's segment) has a dominant tone, its plates follow it with some spread, and a few are accents
        batch = self.tones(key, m, rng)
        for bi, mat in enumerate(TONE_MATS):
            g = batch == bi
            if g.any():
                mb.slabs(P[g], n[g], h[g], ch[g], mat, TRIM, tile=12.0, tile_off=off[g], edge_rect=(self.rect("edge_wall"), self.rect("edge_chamfer")), edge_tile=8.0)
        # the cells the overlays use are the plate's top face, inside its chamfer
        self.last_cells = np.stack([a0 + ch, np.maximum(a1 - ch, a0 + ch + 0.01), b0 + ch, np.maximum(b1 - ch, b0 + ch + 0.01), h], axis=1)
        self.last_tones = batch
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
                sb_ = np.clip(batch[:len(S)] + rng.choice([-2, -1, 1, 2], size=len(S)), 0, 4)       # a plate on a plate is a different batch of paint
                soff = rng.uniform(0, 1, size=(len(S), 2))
                for bi, mat in enumerate(TONE_MATS):
                    g = sb_ == bi
                    if g.any():
                        mb.slabs(SP[g], np.tile(patch.normal(), (int(g.sum()), 1)), sh[g], np.minimum(0.06, sh[g] * 0.9), mat, TRIM, tile=12.0,
                                 tile_off=soff[g], edge_rect=(self.rect("edge_wall"), self.rect("edge_chamfer")), edge_tile=8.0)
                count += len(S)
        return count

    def tones(self, key, m, rng):
        """Paint tone index (0 deep .. 4 pale) for m plates of one region: the region key is the plate key without its trailing part index."""
        region = str(key).rsplit("-", 1)[0]
        rr = self.rng("tone", region)
        dom = int(rr.choice(5, p=TONE_DOMINANT))
        w = np.exp(-np.abs(np.arange(5) - dom) * 1.6) + 0.02                   # neighbours in tone: light, not paint, carries the picture
        t = rng.choice(5, size=m, p=w / w.sum())
        accent = rng.random(m) < 0.06
        t[accent] = rng.choice(5, size=int(accent.sum()))
        return t

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
    def ring(self, y0, y1, outer, inner=None, centre=(0.0, 0.0), mb=None, rect_name="chrome_brushed", bevel=0.6, plate=False):
        """A collar round the hull: outer = (hw, hh, c). A stepped rim: the band itself and a thinner raised lip at each face. Chrome by default;
        ``plate=True`` makes a rib in the armour paint instead (matte black, chrome only on the corner rails)."""
        mb = mb or self.hull
        rect = self.rect(rect_name)
        if plate:
            return self._plate_rib(y0, y1, outer, inner, centre, mb)
        hw, hh, c = outer
        cx, cz = centre
        prof = chamfered_rect(hw, hh, c)
        fr = Frame((cx, 0, cz), (0, 1, 0), (0, 0, 1))
        mb.extrude(prof, fr, y0 + bevel, y1 - bevel, PLATE_C, tile=8.0, caps=(False, False))      # the band is matte armour; only the lips are polished
        # lips (stepped bevel) at each end: slightly smaller prisms
        for ya, yb, s0, s1 in ((y0, y0 + bevel, (1 - bevel / hw * 1.4, 1 - bevel / hh * 1.4), (1.0, 1.0)), (y1 - bevel, y1, (1.0, 1.0), (1 - bevel / hw * 1.4, 1 - bevel / hh * 1.4))):
            mb.extrude(prof, fr, ya, yb, CHROME, s0=s0, s1=s1, tile=3.0, caps=(False, False))
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

    def _plate_rib(self, y0, y1, outer, inner, centre, mb):
        hw, hh, c = outer
        cx, cz = centre
        prof = chamfered_rect(hw, hh, c)
        fr = Frame((cx, 0, cz), (0, 1, 0), (0, 0, 1))
        mb.extrude(prof, fr, y0, y1, PLATE, caps=(False, False), tile=8.0)
        if inner is not None:
            ihw, ihh, ic = inner
            iprof = chamfered_rect(ihw, ihh, ic)
            k = len(prof)
            for y, facing in ((y0, -1.0), (y1, 1.0)):
                P = np.array([[(prof[i][0] + cx, y, prof[i][1] + cz), (prof[(i + 1) % k][0] + cx, y, prof[(i + 1) % k][1] + cz),
                               (iprof[(i + 1) % k][0] + cx, y, iprof[(i + 1) % k][1] + cz), (iprof[i][0] + cx, y, iprof[i][1] + cz)] for i in range(k)])
                if facing < 0:
                    P = P[:, ::-1]
                mb.quads_metric(P, PLATE, 8.0)

    def dome(self, frame, radius, mb=None, rect_name="chrome_brushed", segs=16):
        """A sensor blister: a shallow dome on a short collar; frame z = out of the surface."""
        mb = mb or self.hull
        ax = Frame(frame.o, frame.u, frame.f)
        prof = [(radius * 1.15, 0.0), (radius * 1.15, radius * 0.18)]
        for i in range(1, 7):
            a = i / 6 * math.pi / 2
            prof.append((radius * math.cos(a) * 0.98, radius * 0.18 + radius * 0.7 * math.sin(a)))
        mb.lathe(prof, ax, CHROME, segs=segs, tile=3.0)

    def pipe(self, p0, p1, radius, mat=None, rect_name=None, sides=8, mb=None, caps=True):
        """A prism between two points: chrome (metric brushed tile) by default, or any atlas piece by ``rect_name`` (burnt metal for wrecks)."""
        mb = mb or self.hull
        d = np.asarray(p1, float) - np.asarray(p0, float)
        ln = np.linalg.norm(d)
        up = np.array([0, 0, 1.0]) if abs(d[2] / ln) < 0.9 else np.array([1.0, 0, 0])
        fr = Frame(p0, d, up)
        prof = mnmesh.regular_poly(sides, radius)
        if mat == PLATE:
            mb.extrude(prof, fr, 0, ln, PLATE, tile=4.0, caps=(caps, caps))
        elif rect_name is None:
            mb.extrude(prof, fr, 0, ln, CHROME, tile=3.0, caps=(caps, caps))
        else:
            mb.extrude(prof, fr, 0, ln, TRIM, rect=self.rect(rect_name), caps=(caps, caps), cap_mat=TRIM, cap_rect=self.rect(rect_name))

    def slab_box(self, frame, size, centre=(0, 0, 0), chamfer=0.0, mat=PLATE, tile=12.0, mb=None, rect=None, caps=True, off=(0, 0)):
        (mb or self.hull).box(frame, size, mat, centre=centre, tile=tile, chamfer=chamfer, rect=rect, caps=caps, off=off)

    # ---------------------------------------------------------------------------------------------- larger structures
    def fin(self, poly_yz, x, thickness, mb=None, rim="chrome_brushed"):
        """A fin or spine: a polygon in the (y, z) plane (ship space) extruded along x. Big faces take the plate tile, the rim is chrome."""
        mb = mb or self.hull
        fr = Frame((x, 0, 0), (1, 0, 0), (0, 0, 1))
        prof = [(-y, z) for y, z in poly_yz]                 # local x runs toward -y
        mb.extrude(prof, fr, -thickness / 2, thickness / 2, CHROME, caps=(True, True), cap_mat=PLATE, tile=3.0)

    def lattice(self, outline, x, thickness=0.9, beam=0.5, braces=3, mb=None, mat=None):
        """A lattice panel in the (y, z) plane at x: ``outline`` = [bottom-left, top-left, top-right, bottom-right] as (y, z); square beams round
        the edge, a cross-brace at the middle and X-braces between ``braces`` bays. mat=PLATE for black beams (default chrome)."""
        mb = mb or self.hull
        (y0, z0), (y1, z1), (y2, z2), (y3, z3) = outline
        P = lambda y, z: np.array([x, y, z], float)
        edges = [((y0, z0), (y1, z1)), ((y1, z1), (y2, z2)), ((y2, z2), (y3, z3)), ((y3, z3), (y0, z0))]
        for a, b in edges:
            self.pipe(P(*a), P(*b), beam, mat=mat, sides=4, mb=mb)
        sides = 4
        mid = lambda a, b, t: (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        L0, L1 = (y0, z0), (y1, z1)             # left edge, bottom to top
        R0, R1 = (y3, z3), (y2, z2)             # right edge, bottom to top
        self.pipe(P(*mid(L0, L1, 0.5)), P(*mid(R0, R1, 0.5)), beam * 0.7, mat=mat, sides=sides, mb=mb)          # a cross-brace at mid height
        for k in range(braces + 1):
            t = k / braces
            bot, top = mid(L0, R0, t), mid(L1, R1, t)
            if 0 < k < braces:
                self.pipe(P(*bot), P(*top), beam * 0.7, mat=mat, sides=sides, mb=mb)
            if k < braces:
                bot2, top2 = mid(L0, R0, (k + 1) / braces), mid(L1, R1, (k + 1) / braces)
                a, b = (bot, top2) if k % 2 == 0 else (top, bot2)
                self.pipe(P(*a), P(*b), beam * 0.6, mat=mat, sides=sides, mb=mb)

    def fin_plates(self, rect_yz, x, thickness, key, **style):
        """Raised armour plates on both faces of a fin: rect_yz = (y0, y1, z0, z1) inside the fin's outline."""
        if self.lod >= 1:
            return
        y0, y1, z0, z1 = rect_yz
        for side in (1, -1):
            xs = x + side * thickness / 2
            if side > 0:
                patch = Patch((xs, y0, z0), (xs, y1, z0), (xs, y1, z1), (xs, y0, z1))
            else:
                patch = Patch((xs, y1, z0), (xs, y0, z0), (xs, y0, z1), (xs, y1, z1))
            self.plate_patch(patch, f"{key}{side}", rows=style.get("rows", (4, 8)), cols=style.get("cols", (3, 7)), height=style.get("height", (0.1, 0.25)),
                             margin=0.3, sub_prob=style.get("sub_prob", 0.0))

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
        """Hatches, vents, stencilled codes, warning bands, big access panels and fastener lines on the tops of the plates just laid (cells from
        plate_patch), so no big plate is bare. Orientation: the piece x runs along the ship."""
        if self.lod >= 1 or len(cells) == 0:
            return
        rng = self.rng("details", key)
        n = patch.normal()
        Lu, Lv = patch.lu, patch.lv
        run = patch.ev()
        for a0, a1, b0, b1, h in cells:
            w, l = a1 - a0, b1 - b0
            r = rng.random()
            name = None
            painted = False
            if w >= 4.6 and l >= 7.0 and r < 0.10:
                name = "panel_big"
            elif w >= 1.6 and l >= 7.0 and r < 0.17:
                name, painted = "chevron_%d" % int(rng.integers(2)), True
            elif w >= 1.5 and l >= 3.6 and r < 0.30:
                name, painted = "code_%d" % int(rng.integers(6)), True
            elif w >= 3.2 and l >= 6.5 and r < 0.38:
                name = "vent_big"
            elif w >= min_size[0] and l >= min_size[1] and r < 0.38 + hatch:
                name = str(rng.choice(["hatch_a", "hatch_a", "hatch_b", "vent"]))
            if name is not None:
                pw, ph = (self.paint_info if painted else self.trims)[name]["metres"]
                sc = float(min(1.0, (w - 0.8) / ph, (l - 0.8) / pw, 1.4))
                if sc >= 0.45:
                    cu = rng.uniform(a0 + ph * sc / 2 + 0.3, max(a0 + ph * sc / 2 + 0.31, a1 - ph * sc / 2 - 0.3))
                    cv = rng.uniform(b0 + pw * sc / 2 + 0.3, max(b0 + pw * sc / 2 + 0.31, b1 - pw * sc / 2 - 0.3))
                    c = patch.pt(cu / Lu, cv / Lv) + n * (h + 0.02)
                    if painted:
                        self.paint_quad(name, c, run, n, scale=sc, lift=0.0)
                    else:
                        self.decal(c, run, n, name, scale=sc, lift=0.0)
                    continue
            if l >= 5.0 and w >= 2.0 and rng.random() < fasteners:
                pu = a0 + 0.35 if rng.random() < 0.5 else a1 - 0.55
                p0 = patch.pt(pu / Lu, (b0 + 0.4) / Lv) + n * (h + 0.015)
                self.strip(p0, run, n, l - 0.8, "fasteners", lift=0.0, scale=0.7)

    def wear(self, patch, cells, key, density=0.8, mb=None):
        """Alpha overlays laid on plate tops (transparent WEAR material, 1 cm above the plate): grime streaks along the ship, scorch blooms, pale
        scuffs along an edge, repainted patches (lighter or darker), coolant stains. Mid-scale variation that does not repeat with the tile."""
        if self.lod >= 1 or len(cells) == 0 or "wear" not in self.trims_all:
            return 0
        mb = mb or self.hull
        wear = self.trims_all["wear"]
        rng = self.rng("wear", key)
        n = patch.normal()
        Lu, Lv = patch.lu, patch.lv
        ev = patch.ev()
        count = 0
        # what shows on which paint: dark plates are marked by pale dust, pale scuffs and repainted patches; light plates by dark grime, scorch and stains
        by_tone = {
            "dark": (["streak_light", "scuff", "patch_light", "stain", "scorch", "bleach"], [0.32, 0.28, 0.10, 0.05, 0.08, 0.13]),
            "mid": (["streak", "streak_light", "scuff", "scorch", "patch_light", "patch_dark", "stain", "bleach"], [0.20, 0.14, 0.14, 0.13, 0.06, 0.05, 0.12, 0.16]),
            "light": (["streak", "scorch", "stain", "patch_dark", "streak"], [0.38, 0.22, 0.18, 0.08, 0.14]),
        }
        tones = getattr(self, "last_tones", np.zeros(len(cells), int))
        for ci, (a0, a1, b0, b1, h) in enumerate(cells):
            w, l = a1 - a0, b1 - b0
            if rng.random() > density or w < 1.0 or l < 1.4:
                continue
            tone = int(tones[ci]) if ci < len(tones) else 1
            kinds, weights = by_tone["dark" if tone <= 1 else "mid" if tone == 2 else "light"]
            weights = np.asarray(weights) / np.sum(weights)
            for _ in range(1 + int(rng.random() < 0.35)):
                kind = kinds[int(rng.choice(len(kinds), p=weights))]
                names = [k for k, v in wear.items() if v["kind"] == kind]
                name = names[int(rng.integers(len(names)))]
                pw, ph = wear[name]["metres"]
                if kind in ("patch_light", "patch_dark") and (w < 2.2 or l < 2.8):
                    continue
                sc = float(min(rng.uniform(0.55, 1.25), (l - 0.2) / pw, (w - 0.1) / ph, 1.0 if kind in ("scorch", "stain") and min(w, l) < 4 else 9.0))
                if sc < 0.28:
                    continue
                sx_, sy_ = pw * sc, ph * sc
                if sy_ > w - 0.1:
                    sy_ = w - 0.1
                cu0 = rng.uniform(a0 + 0.05, max(a0 + 0.06, a1 - sy_ - 0.05))
                cv0 = rng.uniform(b0 + 0.05, max(b0 + 0.06, b1 - sx_ - 0.05))
                rot180 = rng.random() < 0.5
                if kind == "scuff":                              # hug a lateral edge with the scratches toward it
                    cu0 = a1 - 0.02 - sy_ if rot180 else a0 + 0.02
                if not rot180:        # x along the ship, y = n x x (toward lower u): the box spans u in [cu0, cu0 + sy]
                    x = ev * sx_
                    y = cross(n, ev) * sy_
                    p0 = patch.pt((cu0 + sy_) / Lu, cv0 / Lv) + n * (h + 0.012)
                else:                 # turned half a turn: x against the ship, y toward higher u
                    x = -ev * sx_
                    y = cross(n, -ev) * sy_
                    p0 = patch.pt(cu0 / Lu, (cv0 + sx_) / Lv) + n * (h + 0.012)
                mb.quad_rect(p0, p0 + x, p0 + x + y, p0 + y, wear[name]["uv"], WEAR)
                count += 1
        return count

    def paint_quad(self, name, centre, run, n, scale=1.0, lift=0.016, size=None, mb=None):
        """One aged paint piece (a stencilled code, a chevron band, the hull number) laid on a surface facing ``n``, x along ``run``; alpha blended, so the plate
        shows through the chips."""
        if self.lod >= 1 or name not in self.paint_info:
            return
        mb = mb or self.hull
        info = self.paint_info[name]
        pw, ph = (size if size is not None else (info["metres"][0] * scale, info["metres"][1] * scale))
        x = norm(run)
        n = norm(n)
        y = cross(n, x)
        c = np.asarray(centre, np.float64) + n * lift
        a = c - x * pw / 2 - y * ph / 2
        mb.quad_rect(a, a + x * pw, a + x * pw + y * ph, a + y * ph, info["uv"], PAINT)

    def edge_wear(self, patch, cells, tones, key, mb=None, chip_p=0.62, grime_p=0.5, min_w=0.5, min_l=0.8, min_width=0.08):
        """Paint ageing along plate edges, laid just inside each plate's top face (alpha blended): edge chips (flakes of top paint gone to primer with a scrape of bare
        metal inside, many at the edge and thinning inward, a worn line of metal along it) and edge grime (dirt gathered at the edge, with short drips). Light
        plates collect more grime, dark plates show their chips more."""
        if self.lod >= 1 or len(cells) == 0 or not self.paint_info:
            return 0
        mb = mb or self.hull
        P = self.paint_info
        chips = [k for k, v in P.items() if v["kind"] == "edge_chip"]
        grimes = [k for k, v in P.items() if v["kind"] == "edge_grime"]
        rng = self.rng("edge", key)
        n = patch.normal()
        ev, eu = patch.ev(), patch.eu()
        Lu, Lv = patch.lu, patch.lv
        count = 0
        for ci, (a0, a1, b0, b1, h) in enumerate(cells):
            w, l = a1 - a0, b1 - b0
            if w < min_w or l < min_l:
                continue
            tone = int(tones[ci]) if ci < len(tones) else 1
            for edge in range(4):
                length = l if edge < 2 else w
                for kind, prob, lift in (("grime", grime_p * (0.5 if tone <= 1 else 1.0), 0.010), ("chip", chip_p, 0.014)):
                    if rng.random() > prob:
                        continue
                    name = (grimes if kind == "grime" else chips)[int(rng.integers(len(grimes if kind == "grime" else chips)))]
                    pw, ph = P[name]["metres"]
                    width = min(ph, 0.4 * min(w, l))
                    if width < min_width:
                        continue
                    if edge == 0:    # the high-u lateral edge: runs along +ev, inward is toward lower u
                        origin, x = patch.pt(a1 / Lu, b0 / Lv), ev
                    elif edge == 1:  # the low-u lateral edge: runs along -ev
                        origin, x = patch.pt(a0 / Lu, b1 / Lv), -ev
                    elif edge == 2:  # the low-v end: runs along +eu
                        origin, x = patch.pt(a0 / Lu, b0 / Lv), eu
                    else:            # the high-v end: runs along -eu
                        origin, x = patch.pt(a1 / Lu, b1 / Lv), -eu
                    y = cross(n, x)
                    origin = origin + n * (h + lift)
                    pos = 0.0
                    while pos < length - 0.05:
                        seg = min(pw, length - pos)
                        u0 = float(rng.uniform(0.0, max(0.0, 1.0 - seg / pw)))
                        pa = origin + x * pos
                        mb.quad_rect(pa, pa + x * seg, pa + x * seg + y * width, pa + y * width, P[name]["uv"], PAINT, uspan=(u0, u0 + seg / pw), vspan=(0.0, width / ph))
                        count += 1
                        pos += seg
        return count

    def wear_quad(self, name, centre, run, n, scale=1.0, lift=0.012, size=None, mb=None):
        """One wear overlay at a chosen place (heat tint on the engine block, a stain below a vent). Same conventions as ``decal``."""
        if self.lod >= 1 or "wear" not in self.trims_all:
            return
        mb = mb or self.hull
        info = self.trims_all["wear"][name]
        pw, ph = info["metres"]
        pw, ph = (size if size is not None else (pw * scale, ph * scale))
        x = norm(run)
        n = norm(n)
        y = cross(n, x)
        c = np.asarray(centre, np.float64) + n * lift
        a = c - x * pw / 2 - y * ph / 2
        mb.quad_rect(a, a + x * pw, a + x * pw + y * ph, a + y * ph, info["uv"], WEAR)

    def duct(self, p0, p1, normal, width=0.9, height=0.45, mb=None):
        """A raised cable tray between two points on a surface facing ``normal``: an armoured box with a conduit strip on its top."""
        mb = mb or self.hull
        p0, p1 = np.asarray(p0, float), np.asarray(p1, float)
        d = p1 - p0
        ln = np.linalg.norm(d)
        n = norm(normal)
        fr = Frame((p0 + p1) / 2 + n * height / 2, d, n)
        mb.box(fr, (width, ln, height), PLATE, chamfer=0.08, tile=4.0)
        run = d / ln
        y = cross(n, run)
        self.strip((p0 + n * (height + 0.012)) - y * (width * 0.42), run, n, ln, "conduit", lift=0.0, scale=width * 0.84 / 0.8)

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

