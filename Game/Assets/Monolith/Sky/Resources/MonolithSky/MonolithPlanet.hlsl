// The backdrop planet (Jupiter) for the Monolith sky: an analytic sphere seen from one fixed point, drawn in the sky pass.
//
// It is in the sky pass, not a mesh, so it cannot break: it has no depth to lose precision in, no far plane to clip against and no size
// limit; it is behind every ship, beam and explosion because the sky pass only draws where nothing nearer has been drawn; and the stars
// and the Milky Way behind the disc are hidden by its alpha (the star pass asks PlanetAlpha for each star). At 200,000 km a flight of a few
// kilometres moves the planet by less than a thousandth of a pixel, so the viewer is a fixed point (that is what sells the scale).
//
// Everything is in planet radii. The viewer is at O = -D * J (J: unit vector from the viewer to the centre, D: distance in radii).
// A ray along d meets the sphere where its impact parameter b = D * |J x d| is below 1.
//
// Shading, per pixel on the disc:
//   - the albedo map (Cassini, a cylindrical map) sampled at the point's longitude and latitude after the motion warp (spin, zonal winds that
//     differ band by band, the Great Red Spot turning), with analytic texture gradients so the limb filters like a sphere should;
//   - sunlight: a Lambert and Lommel-Seeliger mix (limb darkening), through a soft twilight (a softplus of the cosine to the Sun, so light
//     fades over a few tens of degrees instead of stopping at the geometric terminator), reddened where it is long;
//   - a thin blue haze above the clouds: a column that grows as 1 / mu, lit with a wider twilight, forward scattering and Rayleigh;
//   - the same haze seen edge-on outside the disc: a thin bright rim whose thickness is the haze's scale height.
// The night side is black. The result is premultiplied (rgb is light added, a is how much of the sky behind is hidden).

#ifndef MONOLITH_PLANET_INCLUDED
#define MONOLITH_PLANET_INCLUDED

TEXTURE2D(_PlanetMap);
SAMPLER(sampler_PlanetMap);

float4 _PlanetDir;       // xyz: unit vector from the viewer to the centre; w: the viewer's distance from the centre in radii (0: no planet)
float4 _PlanetPole;      // xyz: north pole; w: cosine of the angular radius of the disc plus its haze (farther than this from the centre: not the planet)
float4 _PlanetE1;        // xyz: equatorial unit vector at longitude 0 (the central meridian at time zero)
float4 _PlanetE2;        // xyz: equatorial unit vector 90 degrees east of E1
float4 _PlanetSun;       // xyz: unit vector toward the Sun; w: radiance of a reflectance-1 Lambert surface facing the Sun, in nits
float4 _PlanetMotion;    // x: spin (rad/s), y: wind scale, z: GRS spin (rad/s), w: seconds on the planet's clock
float4 _PlanetMap0;      // x: map longitude of the central meridian at time zero (rad), y: GRS longitude in the map (rad), z: GRS latitude (rad), w: unused
float4 _PlanetGrs;       // x: GRS half-width as a latitude-equivalent angle (rad), y: half-height (rad), z, w: the oval ends and the collar ends, in oval radii
float4 _PlanetLook;      // x: Lommel-Seeliger share (limb darkening: 0 is Lambert), y: twilight width (cosine units), z: haze optical depth at the limb, w: haze scale height in radii
float4 _PlanetLook2;     // x: haze brightness, y: reddening of the twilight, z: albedo scale, w: detail strength
float4 _PlanetPx;        // x: angle of one pixel (rad), y: map texel angle at the equator (rad), z: pixels per unit of impact parameter at the limb, w: planetshine gain (the bake)
float4 _PlanetDetail;    // x: noise frequency per radian of longitude at the equator, y: contrast, z: stretch along the bands, w: unused
float4 _WindProfile[17]; // 68 samples of the zonal wind in m/s, from -90 to +90 degrees of latitude, relative to the Great Red Spot's own frame

// The moons (up to two), small discs in front of or behind the planet.
TEXTURE2D(_MoonMap0);
TEXTURE2D(_MoonMap1);
float4 _MoonCount;       // x: how many moons
float4 _MoonPos[2];      // xyz: the centre relative to the viewer in planet radii, w: the moon's radius in planet radii
float4 _MoonCen[2];      // xyz: the centre relative to the planet's centre (for shadows), w: the radius
float4 _MoonF[2];        // xyz: the moon's sub-planet direction (toward the planet's centre, in the equator plane); the map's centre meridian
float4 _MoonG[2];        // xyz: 90 degrees east of it
float4 _MoonLook[2];     // x: albedo scale, y: how lit by the Sun (0 in the planet's shadow), z: map width in texels, w: unused

static const float kPlanetRadiusM = 7.1492e7;

float PlanetWrapPi(float a) { return a - TWO_PI * floor(a / TWO_PI + 0.5); }

float PlanetWind(float lat)
{
    float x = clamp((lat / PI + 0.5) * 67.0, 0.0, 66.999);
    int i = (int)x;
    float f = x - (float)i;
    int j = i + 1;
    return lerp(_WindProfile[i >> 2][i & 3], _WindProfile[j >> 2][j & 3], f);
}

// How fast the clouds at this latitude slide east, in radians of longitude per second, on the planet's clock.
float PlanetZonalRate(float lat)
{
    return PlanetWind(lat) * _PlanetMotion.y * (1.0 / kPlanetRadiusM) / max(cos(lat), 0.15);
}

float2 PlanetMapUV(float3 n)
{
    float lat = asin(clamp(dot(n, _PlanetPole.xyz), -1.0, 1.0));
    float lonView = atan2(dot(n, _PlanetE2.xyz), dot(n, _PlanetE1.xyz));
    float t = _PlanetMotion.w;
    float lonRigid = lonView + _PlanetMap0.x - _PlanetMotion.x * t;          // the planet turns: the map slides west past the viewer
    float cg = cos(_PlanetMap0.z);
    float2 gs = float2(_PlanetGrs.x / cg, _PlanetGrs.y);

    // The Great Red Spot's oval keeps its place; the bands around it slide past at their own speeds (the oval's own latitude is the frame).
    float dlat = lat - _PlanetMap0.z;
    float2 q = float2(PlanetWrapPi(lonRigid - _PlanetMap0.y), dlat) / gs;
    float w = 1.0 - smoothstep(_PlanetGrs.z, _PlanetGrs.w, length(q));
    float lon1 = lonRigid - (PlanetZonalRate(lat) - PlanetZonalRate(_PlanetMap0.z)) * (1.0 - w) * t;

    // The oval turns counter-clockwise (it is an anticyclone in the south): rotate the sampling point back about its centre, in oval units.
    float2 q1 = float2(PlanetWrapPi(lon1 - _PlanetMap0.y), dlat) / gs;
    float w1 = 1.0 - smoothstep(_PlanetGrs.z, _PlanetGrs.w, length(q1));
    float th = _PlanetMotion.z * t * w1;
    float s, c;
    sincos(th, s, c);
    float2 q2 = float2(c * q1.x + s * q1.y, -s * q1.x + c * q1.y);
    float2 m = float2(_PlanetMap0.y, _PlanetMap0.z) + q2 * gs;
    return float2(0.5 + m.x / TWO_PI, 0.5 + m.y / PI);
}

// The point on the sphere that a ray along d meets, or the point where it grazes the limb.
float3 PlanetNormal(float3 d)
{
    float rc = _PlanetDir.w * length(cross(_PlanetDir.xyz, d));
    float root = sqrt(max(1.0 - rc * rc, 0.0));
    float t = _PlanetDir.w * dot(_PlanetDir.xyz, d) - root;
    return normalize(-_PlanetDir.w * _PlanetDir.xyz + t * d);
}

float PlanetSoft(float mu0, float width)
{
    // A softplus of the cosine to the Sun, normalised so it is 1 facing the Sun: sunlight that fades over a twilight instead of stopping at the terminator.
    float x = mu0 / width;
    float sp = x > 20.0 ? mu0 : width * log(1.0 + exp(x));
    float norm = width * log(1.0 + exp(1.0 / width));
    return sp / norm;
}

float PlanetPhase(float cosScatter)
{
    // Haze scatters mostly toward the Sun's far side (forward) and a little everywhere (Rayleigh).
    float g = 0.55;
    float hg = (1.0 - g * g) / (4.0 * PI * pow(max(1.0 + g * g - 2.0 * g * cosScatter, 1e-3), 1.5));
    float ray = 0.75 * (1.0 + cosScatter * cosScatter) / (4.0 * PI);
    return 4.0 * PI * (0.25 * hg + 0.75 * ray);
}

// Light from the haze edge-on, outside the disc (and, mixed in, at the limb itself).
float PlanetHazeColumn(float rc)
{
    float z = max(rc - 1.0, 0.0);
    return _PlanetLook.z * exp(-z / _PlanetLook.w);
}

// How much of the sky behind a direction the planet hides (the disc, edge anti-aliased, and its haze). Cheap: the star pass calls it for every star.
float PlanetAlpha(float3 d)
{
    if (_PlanetDir.w < 0.5) return 0.0;
    if (dot(d, _PlanetDir.xyz) < _PlanetPole.w) return 0.0;
    float rc = _PlanetDir.w * length(cross(_PlanetDir.xyz, d));
    float cov = saturate((1.0 - rc) * _PlanetPx.z + 0.5);
    float haze = 1.0 - exp(-PlanetHazeColumn(rc));
    return cov + (1.0 - cov) * haze;
}

// Detail finer than the map: value noise on the map's own (longitude, latitude) plane, stretched along the bands, with an integer hash (no sin: the coordinates are in the thousands).
float PlanetHash(int2 p)
{
    uint x = (uint)(p.x + 65536) * 1597334677u, y = (uint)(p.y + 65536) * 3812015801u;
    uint h = (x ^ y) * 2654435761u;
    h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
    return (h & 0xFFFFFFu) * (1.0 / 16777215.0);
}

float PlanetNoise(float2 p)
{
    int2 i = (int2)floor(p);
    float2 f = p - floor(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(PlanetHash(i), PlanetHash(i + int2(1, 0)), u.x), lerp(PlanetHash(i + int2(0, 1)), PlanetHash(i + int2(1, 1)), u.x), u.y);
}

// Three octaves, -0.5 .. 0.5, roughly.
float PlanetFbm(float2 p)
{
    return PlanetNoise(p) * 0.52 + PlanetNoise(p * 2.07 + 17.3) * 0.31 + PlanetNoise(p * 4.31 + 5.9) * 0.17 - 0.5;
}

// The moons' shadows on the planet: 1 in sunlight, 0 in a shadow. A point X on the planet looks along the Sun's direction; a moon in that direction (at its distance, the Sun's disc, 0.0018 radian
// across at 5.2 AU, makes the umbra narrower and the penumbra wider) takes the light.
float PlanetMoonShadow(float3 x)
{
    float lit = 1.0;
    int moons = (int)(_MoonCount.x + 0.5);
    for (int k = 0; k < moons; k++)
    {
        float3 toMoon = _MoonCen[k].xyz - x;
        float along = dot(toMoon, _PlanetSun.xyz);
        if (along <= 0.0) continue;
        float perp = length(toMoon - along * _PlanetSun.xyz);
        float r = _MoonCen[k].w, w = along * 0.0009 + 1e-4;
        lit = min(lit, smoothstep(r - w, r + w, perp));
    }
    return lit;
}

// d: the pixel's direction; dx, dy: the neighbours' (for the texture's footprint); lod: >= 0 samples a fixed mip (the lighting bake).
float4 PlanetShade(float3 d, float3 dx, float3 dy, float lod)
{
    float rc = _PlanetDir.w * length(cross(_PlanetDir.xyz, d));
    float sd = (1.0 - rc) * _PlanetPx.z;              // pixels inside the limb
    float cov = saturate(sd + 0.5);
    float3 outRgb = 0.0;
    float outA = 0.0;
    float sunNits = _PlanetSun.w;

    // The haze seen edge-on: lit by the Sun at the point where the ray is closest to the centre.
    {
        float3 nT = PlanetNormal(d);
        float3 v = -d;
        float mu0T = dot(nT, _PlanetSun.xyz);
        float N = PlanetHazeColumn(rc);
        float amt = 1.0 - exp(-N);
        float3 col = float3(0.60, 0.76, 1.0) * (_PlanetLook2.x * 0.55 * PlanetSoft(mu0T, _PlanetLook.y * 1.4) * PlanetPhase(-dot(_PlanetSun.xyz, v)));
        outRgb = col * (amt * sunNits);
        outA = amt;
    }
    if (cov <= 0.0) return float4(outRgb, outA);

    // The disc.
    float3 n = PlanetNormal(d);
    float3 v = -d;
    float mu = saturate(dot(n, v));
    float mu0 = dot(n, _PlanetSun.xyz);
    float2 uv = PlanetMapUV(n);
    float3 albedo;
    if (lod >= 0.0)
    {
        albedo = SAMPLE_TEXTURE2D_LOD(_PlanetMap, sampler_PlanetMap, uv, lod).rgb;
    }
    else
    {
        float2 uvx = PlanetMapUV(PlanetNormal(dx)), uvy = PlanetMapUV(PlanetNormal(dy));
        float2 gx = uvx - uv, gy = uvy - uv;
        gx.x -= round(gx.x); gy.x -= round(gy.x);
        albedo = SAMPLE_TEXTURE2D_GRAD(_PlanetMap, sampler_PlanetMap, uv, gx, gy).rgb;

        // Where one map texel is bigger than a pixel the map has nothing more to say: add the fine structure it would have (stretched along the bands, strongest where the map
        // itself has structure at its finest scale, so smooth zones stay smooth and belt edges and turbulent areas get finer streaks).
        float2 texels = float2(_PlanetPx.y > 0.0 ? 2.0 * PI / _PlanetPx.y : 8192.0, 0.0);
        texels.y = 0.5 * texels.x;
        float footprint = sqrt(length(gx * texels) * length(gy * texels));
        float magnified = smoothstep(1.0, 0.3, footprint) * _PlanetLook2.w;
        if (magnified > 0.001)
        {
            float3 fine = SAMPLE_TEXTURE2D_LOD(_PlanetMap, sampler_PlanetMap, uv, 0.0).rgb;
            float3 coarse = SAMPLE_TEXTURE2D_LOD(_PlanetMap, sampler_PlanetMap, uv, 2.5).rgb;
            float energy = abs(dot(fine - coarse, float3(0.2126, 0.7152, 0.0722)));
            float lat = (uv.y - 0.5) * PI, lon = (uv.x - 0.5) * TWO_PI;
            float2 p = float2(lon * max(cos(lat), 0.2) / _PlanetDetail.z, lat) * _PlanetDetail.x;
            float amount = _PlanetDetail.y * magnified * saturate(0.35 + energy * 14.0);
            albedo *= saturate(1.0 + amount * 2.0 * PlanetFbm(p));
        }
    }
    albedo *= _PlanetLook2.z;

    float f0 = PlanetSoft(mu0, _PlanetLook.y);
    float ls = 2.0 * f0 / max(f0 + mu, 1e-4);
    float shape = lerp(f0, ls, _PlanetLook.x);
    float tw = saturate(mu0 / 0.45);
    float3 tint = lerp(exp(-_PlanetLook2.y * float3(0.0, 0.55, 1.35)), 1.0, tw);

    // The haze over the clouds: a column that grows toward the limb, lit with a wider twilight.
    float tauN = _PlanetLook.z * 0.04 / max(mu, 0.05);
    float hazeAmt = 1.0 - exp(-tauN);
    float cosScatter = -dot(_PlanetSun.xyz, v);
    float3 hazeCol = float3(0.60, 0.76, 1.0) * (_PlanetLook2.x * 0.55 * PlanetSoft(mu0, _PlanetLook.y * 1.4) * PlanetPhase(cosScatter));

    float3 surface = albedo * tint * shape * exp(-tauN * 0.5);
    float3 disc = (surface + hazeCol * hazeAmt) * (sunNits * PlanetMoonShadow(n));

    float3 rgb = lerp(outRgb, disc, cov);
    float a = lerp(outA, 1.0, cov);
    return float4(rgb, a);
}


// One moon: a Lommel-Seeliger sphere with its map, lit by the Sun, dark in the planet's shadow. rgb premultiplied, a coverage; dist: how far along the ray it is (a large number if missed).
float4 PlanetMoonShade(int k, float3 d, out float dist)
{
    dist = 1e9;
    float3 rel = _MoonPos[k].xyz;
    float rm = _MoonPos[k].w;
    float range = length(rel);
    float impact = length(cross(rel, d));
    float pixel = _PlanetPx.x * range;                          // one pixel at the moon, in radii
    float edge = (rm - impact) / max(pixel, 1e-9);
    if (dot(rel, d) <= 0.0 || edge < -1.0) return 0.0;
    float cov = saturate(edge + 0.5);
    float root = sqrt(max(rm * rm - impact * impact, 0.0));
    float t = dot(rel, d) - root;
    dist = t;
    float3 n = normalize(t * d - rel);
    float mu = saturate(dot(n, -d));
    float mu0 = dot(n, _PlanetSun.xyz);
    float f0 = saturate(mu0 + 0.03) / 1.03;
    float shape = lerp(f0, 2.0 * f0 / max(f0 + mu, 1e-4), 0.6);
    float lon = atan2(dot(n, _MoonG[k].xyz), dot(n, _MoonF[k].xyz));
    float lat = asin(clamp(dot(n, _PlanetPole.xyz), -1.0, 1.0));
    float2 uv = float2(0.5 + lon / TWO_PI, 0.5 + lat / PI);
    float texelsPerPixel = (_PlanetPx.x * range / rm) * (_MoonLook[k].z / TWO_PI);
    float lod = max(log2(max(texelsPerPixel, 1e-3)), 0.0);
    float3 albedo = (k == 0 ? SAMPLE_TEXTURE2D_LOD(_MoonMap0, sampler_PlanetMap, uv, lod).rgb : SAMPLE_TEXTURE2D_LOD(_MoonMap1, sampler_PlanetMap, uv, lod).rgb) * _MoonLook[k].x;
    albedo = saturate(albedo);
    float3 rgb = albedo * shape * _MoonLook[k].y * _PlanetSun.w;
    return float4(rgb * cov, cov);
}

// The whole backdrop for one pixel: the planet, then the moons in front of or behind it.
float4 PlanetAndMoons(float3 d, float3 dx, float3 dy)
{
    float4 p = 0.0;
    float tPlanet = 1e9;
    if (_PlanetDir.w > 0.5 && dot(d, _PlanetDir.xyz) >= _PlanetPole.w)
    {
        p = PlanetShade(d, dx, dy, -1.0);
        float rc = _PlanetDir.w * length(cross(_PlanetDir.xyz, d));
        if (rc < 1.0) tPlanet = _PlanetDir.w * dot(_PlanetDir.xyz, d) - sqrt(1.0 - rc * rc);
    }
    int moons = (int)(_MoonCount.x + 0.5);
    for (int k = 0; k < moons; k++)
    {
        float dist;
        float4 m = PlanetMoonShade(k, d, dist);
        if (m.a <= 0.0) continue;
        p = dist < tPlanet ? m + p * (1.0 - m.a) : p + m * (1.0 - p.a);
    }
    return p;
}

#endif
