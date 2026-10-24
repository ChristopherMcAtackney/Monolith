"""Simulate a fireball (Mantaflow, Blender 5.2), render it with Cycles into a flipbook atlas for the juice system.

  powershell -NoProfile -ExecutionPolicy Bypass -File tools\\blender.ps1 art_src\\juice_fx\\build_flip.py -- --variant fire_a [--res 96] [--preview] [--keep]

What it writes (Game/Assets/Monolith/Resources/Juice/Flip/<variant>.png and .json):
  one RGBA8 atlas, a grid of square frames in row-major order, frame times denser early (a blast opens fast):
    R  the fire: the burning gas integrated along the view (a line integral of the simulation's 'flame' field), encoded sqrt so 8 bits hold it
    G  the smoke: the density integrated along the view
    B  a smoothed copy of the smoke's surface slope (nx) and A (ny): a screen-space normal so the engine can light the smoke with the Sun and the fill
       (B, A = 0.5 + slope; the engine rebuilds z)
  and the json: grid, frame count, the simulation time of each frame, how the channels scale, the nominal radius.
The shader maps R through a temperature ramp (white core, yellow, orange, red), so the colour follows the engine's exposure and the blast's age.
"""
import bpy, sys, os, json, math, time, tempfile, shutil
import numpy as np

# ------------------------------------------------------------------------------------------------ the library of looks

VARIANTS = {
    # name: simulation parameters.  radius = size of the source lumps (m, in a 5 m domain); vel = outward speed of the source; blobs = how many lumps;
    # turb = strength of the turbulence field; frames = simulated frames (24 per second) before the smoke is cut where it reaches the domain's edge
    'fire_a': dict(seed=11, radius=0.40, vel=5.0, fuel=2.6, smoke=0.6, vort=0.9, burn=0.9, noise=1.0, frames=60, tscale=0.36, lumps=0.34, flame_smoke=0.45, dissolve=0.0, blobs=11, turb=14.0, turb_size=0.45),
    'fire_b': dict(endsim=14, seed=23, radius=0.55, vel=3.4, fuel=2.2, smoke=1.0, vort=1.4, burn=0.65, noise=1.3, frames=64, tscale=0.5, lumps=0.50, flame_smoke=0.8, dissolve=0.0, blobs=15, turb=9.0, turb_size=0.65, spread=1.1, emit=10, grow=1.6),
    'fire_c': dict(seed=37, radius=0.32, vel=8.0, fuel=2.2, smoke=0.5, vort=0.7, burn=1.0, noise=0.8, frames=64, tscale=0.28, lumps=0.55, flame_smoke=0.35, dissolve=0.0, blobs=9, turb=18.0, turb_size=0.35, jet=True, spread=1.0),
    'fire_d': dict(seed=51, radius=0.62, vel=2.4, fuel=3.6, smoke=1.2, vort=1.8, burn=0.50, noise=1.6, frames=70, tscale=0.8, lumps=0.45, flame_smoke=1.0, dissolve=0.0, blobs=13, turb=7.0, turb_size=0.8, spread=1.0, emit=12, grow=1.4),
    'smoke_a': dict(seed=71, radius=0.50, vel=1.1, fuel=0.35, smoke=3.2, vort=1.5, burn=1.8, noise=1.4, frames=84, tscale=0.45, lumps=0.50, flame_smoke=3.0, dissolve=0.0, blobs=12, turb=6.0, turb_size=0.7, spread=1.0, emit=7, grow=1.4),
}

FRAME_PX = 512
GRID = 6                         # 6 x 6 frames
DOMAIN = 5.0                     # metres across


SRC = {}


def log(*a):
    print('[MN]', *a, flush=True)


def argv():
    a = sys.argv
    return a[a.index('--') + 1:] if '--' in a else []


def arg(name, default=None):
    a = argv()
    if name in a:
        i = a.index(name)
        return a[i + 1] if i + 1 < len(a) and not a[i + 1].startswith('--') else True
    return default


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.use_gravity = False
    sc.render.fps = 24
    return sc


def make_domain(sc, p, res, cache_dir):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    dom = bpy.context.object
    dom.name = 'Domain'
    dom.scale = (DOMAIN, DOMAIN, DOMAIN)
    bpy.ops.object.modifier_add(type='FLUID')
    m = dom.modifiers['Fluid']
    m.fluid_type = 'DOMAIN'
    ds = m.domain_settings
    ds.domain_type = 'GAS'
    ds.resolution_max = res
    ds.use_noise = False
    ds.noise_scale = 2
    ds.noise_strength = p['noise']
    ds.noise_pos_scale = 1.2
    ds.noise_time_anim = 0.8
    ds.use_adaptive_timesteps = True
    ds.cfl_condition = 3.0
    ds.vorticity = p['vort'] * 2.5
    ds.alpha = 0.0                    # no buoyancy: this is space
    ds.beta = 0.0
    try:
        ds.gravity = (0.0, 0.0, 0.0)
    except Exception as e:
        log('gravity', e)
    ds.time_scale = p.get('tscale', 1.0)
    ds.burning_rate = p['burn']
    ds.flame_smoke = p['flame_smoke']
    ds.flame_vorticity = 0.6
    ds.flame_ignition = 1.5
    ds.flame_max_temp = 3.0
    ds.use_dissolve_smoke = p['dissolve'] > 0
    if p['dissolve'] > 0:
        ds.dissolve_speed = int(p['dissolve'])
    ds.effector_weights.all = 1.0
    ds.effector_weights.turbulence = 1.0
    ds.cache_type = 'ALL'
    ds.cache_data_format = os.environ.get('MN_FMT', 'OPENVDB')
    ds.cache_noise_format = os.environ.get('MN_FMT', 'OPENVDB')
    ds.cache_directory = cache_dir
    ds.cache_frame_start = 1
    ds.cache_frame_end = p['frames']
    sc.frame_start = 1
    sc.frame_end = p['frames']
    return dom


def make_source(p):
    import random
    rng = random.Random(p['seed'])
    # a cluster of lumps: several spheres of different sizes thrown out from a centre, each pushing outward along its own normals, so the burst
    # is many colliding jets (billows) instead of one smooth ball
    n = p.get('blobs', 11)
    parts = []
    for i in range(n):
        v = None
        while True:
            v = (rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))
            if v[0] ** 2 + v[1] ** 2 + v[2] ** 2 <= 1.0:
                break
        r = p['radius'] * rng.uniform(0.35, 0.8)
        if i == 0:
            v = (0, 0, 0); r = p['radius'] * 0.9
        spread = p['radius'] * p.get('spread', 0.9)
        loc = (v[0] * spread, v[1] * spread * (0.4 if p.get('jet') else 1.0), v[2] * spread * (2.2 if p.get('jet') else 1.0))
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=r, location=loc)
        parts.append(bpy.context.object)
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    src = bpy.context.object
    src.name = 'Source'
    bpy.ops.object.shade_smooth()
    SRC['obj'] = src
    tex = bpy.data.textures.new('lumps', 'CLOUDS')
    tex.noise_scale = 0.5
    tex.noise_depth = 2
    d = src.modifiers.new('Displace', 'DISPLACE')
    d.texture = tex
    d.strength = p['lumps'] * p['radius'] * 1.6
    d.mid_level = 0.45
    d.texture_coords = 'OBJECT'
    bpy.ops.object.modifier_add(type='FLUID')
    m = src.modifiers['Fluid']
    m.fluid_type = 'FLOW'
    fs = m.flow_settings
    fs.flow_type = 'BOTH'
    fs.flow_behavior = 'INFLOW'
    fs.flow_source = 'MESH'
    fs.use_initial_velocity = True
    fs.velocity_normal = p['vel']
    fs.velocity_random = p.get('vrand', 0.4)
    fs.fuel_amount = p['fuel']
    fs.density = p['smoke']
    fs.temperature = 2.2
    fs.surface_distance = 1.2
    fs.subframes = 3
    emit = p.get('emit', 8)
    s0 = src.scale.copy()
    g = p.get('grow', 1.8)
    src.keyframe_insert('scale', frame=1)
    src.scale = (s0[0] * g, s0[1] * g, s0[2] * g)
    src.keyframe_insert('scale', frame=emit)
    fs.use_inflow = True
    fs.keyframe_insert('use_inflow', frame=1)
    fs.keyframe_insert('use_inflow', frame=emit)
    fs.use_inflow = False
    fs.keyframe_insert('use_inflow', frame=emit + 1)
    # a turbulence field through the whole domain stirs it into billows
    bpy.ops.object.effector_add(type='TURBULENCE', location=(0, 0, 0))
    f = bpy.context.object
    f.field.strength = p.get('turb', 14.0)
    f.field.size = p.get('turb_size', 0.45)
    f.field.flow = 0.0
    f.field.noise = 0.0
    f.field.seed = p['seed']
    return src


def bake(dom, frames):
    bpy.context.view_layer.objects.active = dom
    for o in bpy.context.scene.objects:
        o.select_set(o == dom)
    t = time.time()
    bpy.ops.fluid.bake_data()
    log('baked data in %.1f s' % (time.time() - t))
    if dom.modifiers['Fluid'].domain_settings.use_noise:
        t = time.time()
        bpy.ops.fluid.bake_noise()
        log('baked noise in %.1f s' % (time.time() - t))


# ------------------------------------------------------------------------------------------------ the render

def setup_render(sc, dom, cam_dist=12.0):
    sc.render.engine = 'CYCLES'
    cy = sc.cycles
    cy.samples = int(os.environ.get('MN_SAMPLES', 64))
    cy.use_denoising = False
    cy.volume_step_rate = 0.5
    cy.volume_max_steps = 1024
    try:
        cp = bpy.context.preferences.addons['cycles'].preferences
        cp.compute_device_type = 'OPTIX'
        cp.get_devices()
        for d in cp.devices:
            d.use = d.type == 'OPTIX'
        cy.device = 'GPU'
    except Exception as e:
        log('gpu setup failed, CPU:', e)
    sc.render.resolution_x = FRAME_PX
    sc.render.resolution_y = FRAME_PX
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'None'
    sc.render.image_settings.file_format = 'OPEN_EXR'
    sc.render.image_settings.color_depth = '32'
    sc.render.image_settings.color_mode = 'RGB'
    w = bpy.data.worlds.new('W')
    w.use_nodes = False
    w.color = (0, 0, 0)
    sc.world = w
    cam_data = bpy.data.cameras.new('Cam')
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = DOMAIN * 0.96
    cam = bpy.data.objects.new('Cam', cam_data)
    sc.collection.objects.link(cam)
    cam.location = (0, -cam_dist, 0)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sc.camera = cam
    return cam


def volume_material(dom, channels):
    """An emission-only volume that writes the integrals of three grids into the pixel (no extinction: pure line integrals)."""
    mat = bpy.data.materials.new('FlipInt')
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    comb = nt.nodes.new('ShaderNodeCombineColor')
    for i, name in enumerate(channels):
        a = nt.nodes.new('ShaderNodeAttribute')
        a.attribute_name = name
        mul = nt.nodes.new('ShaderNodeMath')
        mul.operation = 'MULTIPLY'
        mul.inputs[1].default_value = 1.0
        nt.links.new(a.outputs['Fac'], mul.inputs[0])
        nt.links.new(mul.outputs[0], comb.inputs[i])
    nt.links.new(comb.outputs[0], em.inputs['Color'])
    em.inputs['Strength'].default_value = 1.0
    nt.links.new(em.outputs[0], out.inputs['Volume'])
    dom.data.materials.clear()
    dom.data.materials.append(mat)
    return mat


def render_frame(sc, frame, path, px_size=None):
    px_size = px_size or FRAME_PX
    sc.render.resolution_x = px_size
    sc.render.resolution_y = px_size
    sc.frame_set(frame)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(px_size, px_size, -1)
    bpy.data.images.remove(img)
    return px[::-1, :, :3].copy()      # Blender stores rows bottom-up


def find_end_frame(sc, nframes, work):
    """The last frame before the smoke reaches the edge of the domain (after that it is clipped by the box). Noise-proof: judged on a coarse pooled picture."""
    end = nframes
    n = 192
    for f in range(4, nframes + 1, 2):
        px = render_frame(sc, f, os.path.join(work, 'probe.exr'), n)
        d = np.maximum(px[..., 1] - 0.0495, 0.0)
        c = d.reshape(n // 4, 4, n // 4, 4).mean(axis=(1, 3))              # 48 x 48
        ring = np.concatenate([c[:2].ravel(), c[-2:].ravel(), c[:, :2].ravel(), c[:, -2:].ravel()])
        peak = max(float(c.max()), 1e-6)
        log('probe frame %d: smoke peak %.3f, edge ring max %.3f (%.0f%%)' % (f, peak, ring.max(), 100 * ring.max() / peak))
        if ring.max() > 0.25 * peak and ring.max() > 0.02:
            end = max(8, f - 2)
            break
    return end


def blur(a, r):
    if r <= 0:
        return a
    h = int(math.ceil(3 * r))
    k = np.exp(-0.5 * (np.arange(-h, h + 1) / r) ** 2)
    k /= k.sum()
    t = np.apply_along_axis(lambda v: np.convolve(v, k, mode='same'), 0, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, mode='same'), 1, t)


def ramp_preview(fire, smoke, gain):
    """A quick look at a frame: the temperature ramp over a grey smoke (what the shader will do, roughly)."""
    T = np.clip(fire * gain, 0, 3)
    r = np.clip(T * 2.6, 0, 1); g = np.clip((T - 0.3) * 1.55, 0, 1); b = np.clip((T - 0.78) * 1.9, 0, 1)
    em = np.stack([r, g, b], -1) * (0.1 + T * T * 1.25)[..., None] * 1.6
    a = 1 - np.exp(-smoke * 6.0)
    sm = np.array([0.18, 0.17, 0.16])[None, None, :] * a[..., None]
    img = em * np.exp(-smoke * 2.0)[..., None] + sm
    img = img / (1 + img)
    return np.clip(img, 0, 1) ** (1 / 2.2)


def main():
    name = arg('--variant', 'fire_a')
    p = dict(VARIANTS[name])
    res = int(arg('--res', 96))
    preview = bool(arg('--preview', False))
    keep = bool(arg('--keep', False))
    reuse = bool(arg('--reuse', False))
    nframes_sim = p['frames']
    if arg('--frames'):
        p['frames'] = nframes_sim = int(arg('--frames'))
    cache = os.path.join(tempfile.gettempdir(), 'mn_flip_cache', name)
    if os.path.isdir(cache) and not reuse:
        shutil.rmtree(cache, ignore_errors=True)
    os.makedirs(cache, exist_ok=True)

    sc = reset_scene()
    dom = make_domain(sc, p, res, cache)
    make_source(p)
    if not reuse:
        bake(dom, nframes_sim)
    cam = setup_render(sc, dom)
    volume_material(dom, ['flame', 'density', 'heat'])
    floor_probe = None

    SRC['obj'].hide_render = True
    work = os.path.join(cache, 'render')
    os.makedirs(work, exist_ok=True)
    use_frames = find_end_frame(sc, nframes_sim, work)
    if p.get('endsim'):
        use_frames = min(use_frames, p['endsim'])
    log('using sim frames 1 to %d of %d (the smoke reaches the edge of the domain after that)' % (use_frames, nframes_sim))
    # frame times: denser early (a blast opens fast)
    K = GRID * GRID
    u = np.linspace(0, 1, K)
    times = 1 + (use_frames - 1) * (u ** 1.7)
    frames_f = []
    t0 = time.time()
    for k in range(K):
        f = float(times[k])
        fi = int(round(f))
        px = render_frame(sc, fi, os.path.join(work, 'f%03d.exr' % k))
        frames_f.append(px)
        if k % 6 == 0:
            log('rendered %d/%d (%.0f s)' % (k + 1, K, time.time() - t0))
    F = np.stack(frames_f)               # K x H x W x 3 : flame, density, heat integrals
    # the renderer's volume integral has a constant floor (about 0.05 everywhere inside the domain): take it off, per frame and channel
    FLOOR = 0.0495
    F = np.maximum(F - FLOOR, 0.0)
    flame = F[..., 0]; dens = F[..., 1]; heat = F[..., 2]
    log('integrals: flame max %.3f p99 %.3f | density max %.3f p99 %.3f | heat max %.3f' %
        (flame.max(), np.percentile(flame, 99.9), dens.max(), np.percentile(dens, 99.9), heat.max()))

    # normalise: the brightest 99.95 percent of the fire maps to 1 (the shader's gain restores it); the smoke likewise
    fire_scale = float(np.percentile(flame, 99.95)) + 1e-6
    smoke_scale = float(np.percentile(dens, 99.95)) + 1e-6
    fire_n = np.clip(flame / fire_scale, 0, 1)
    smoke_n = np.clip(dens / smoke_scale, 0, 1)
    # the renderer's sampling noise: a light blur (about a pixel) on both, so a frame magnified on screen is smooth gas and not film grain
    for k in range(K):
        fire_n[k] = blur(fire_n[k], 1.1)
        smoke_n[k] = blur(smoke_n[k], 1.1)
    fire_enc = np.sqrt(fire_n)
    # a smoothed smoke for the slope: normals from its gradient
    nx = np.zeros_like(smoke_n); ny = np.zeros_like(smoke_n)
    for k in range(K):
        s = blur(smoke_n[k] + 0.35 * fire_n[k], 3)
        gy, gx = np.gradient(s)
        nx[k] = np.clip(0.5 + gx * 9.0, 0, 1)
        ny[k] = np.clip(0.5 - gy * 9.0, 0, 1)

    # nominal radius: of the density + fire mass, as a fraction of the half frame, at the frame where the fire is half gone
    mass = (fire_n + smoke_n)
    yy, xx = np.mgrid[0:FRAME_PX, 0:FRAME_PX]
    rr = np.hypot(xx - FRAME_PX / 2, yy - FRAME_PX / 2) / (FRAME_PX / 2)
    fire_sum = fire_n.reshape(K, -1).sum(1)
    kmid = int(np.argmax(fire_sum))
    w = mass[kmid].ravel(); order = np.argsort(rr.ravel()); cw = np.cumsum(w[order]) / max(w.sum(), 1e-6)
    r90 = float(rr.ravel()[order][np.searchsorted(cw, 0.90)])
    log('peak-fire frame %d, 90 percent radius %.2f of the half frame' % (kmid, r90))

    # when the burst really starts to grow (the first frames of the emission are a small still blob): the first frame whose fire covers a fifth of the largest area
    area = (fire_n > 0.12).reshape(K, -1).sum(1).astype(np.float32)
    kstart = int(np.argmax(area >= 0.35 * area.max()))
    start_seconds = float((times[kstart] - 1) / 24.0)
    if name.startswith('smoke'):
        fs = (fire_n ** 4).reshape(K, -1).sum(1)
        pk = int(np.argmax(fs))
        after = [k for k in range(pk, K) if fs[k] < 0.12 * fs[pk]]
        kstart = after[0] if after else pk
        start_seconds = float((times[kstart] - 1) / 24.0)
    log('growth starts at frame %d (%.2f s)' % (kstart, start_seconds))
    atlas = np.zeros((GRID * FRAME_PX, GRID * FRAME_PX, 4), np.float32)
    for k in range(K):
        gy_, gx_ = divmod(k, GRID)
        tile = np.stack([fire_enc[k], smoke_n[k], nx[k], ny[k]], -1)
        atlas[gy_ * FRAME_PX:(gy_ + 1) * FRAME_PX, gx_ * FRAME_PX:(gx_ + 1) * FRAME_PX] = tile
    out_dir = os.path.join(os.environ.get('MN_ROOT', '.'), 'Game', 'Assets', 'Monolith', 'Resources', 'Juice', 'Flip')
    os.makedirs(out_dir, exist_ok=True)
    img = bpy.data.images.new('atlas', GRID * FRAME_PX, GRID * FRAME_PX, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color'
    img.alpha_mode = 'CHANNEL_PACKED'
    img.pixels.foreach_set(atlas[::-1].reshape(-1))
    img.filepath_raw = os.path.join(out_dir, name + '.png')
    img.file_format = 'PNG'
    img.save()
    meta = dict(name=name, grid=GRID, frame=FRAME_PX, frames=K, times=[float(t) for t in (times - 1) / 24.0], seconds=float((use_frames - 1) / 24.0), startSeconds=start_seconds,
                fireScale=fire_scale, smokeScale=smoke_scale, radius90=r90, domainMetres=DOMAIN, simFrames=nframes_sim, res=res)
    json.dump(meta, open(os.path.join(out_dir, name + '.json'), 'w'), indent=1)
    log('wrote', img.filepath_raw, os.path.getsize(img.filepath_raw) // 1024, 'KB')

    if preview:
        pdir = os.path.join(os.environ.get('MN_ROOT', '.'), 'gauntlet', 'captures', 'juice-fx')
        os.makedirs(pdir, exist_ok=True)
        gain = 1.7
        sel = [0, 2, 4, 7, 10, 14, 18, 24, 30, 35]
        tiles = [ramp_preview(fire_n[k] ** 1.0, smoke_n[k], gain) for k in sel]
        cols = 5
        rows = (len(tiles) + cols - 1) // cols
        sheet = np.zeros((rows * FRAME_PX, cols * FRAME_PX, 4), np.float32); sheet[..., 3] = 1
        for i, t in enumerate(tiles):
            r_, c_ = divmod(i, cols)
            sheet[r_ * FRAME_PX:(r_ + 1) * FRAME_PX, c_ * FRAME_PX:(c_ + 1) * FRAME_PX, :3] = t
        im2 = bpy.data.images.new('prev', sheet.shape[1], sheet.shape[0], alpha=True)
        im2.pixels.foreach_set(sheet[::-1].reshape(-1))
        im2.filepath_raw = os.path.join(pdir, name + '_preview.png')
        im2.file_format = 'PNG'
        im2.save()
        log('preview', im2.filepath_raw)
    if not keep:
        shutil.rmtree(work, ignore_errors=True)


main()
