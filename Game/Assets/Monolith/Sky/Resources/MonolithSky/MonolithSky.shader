// Monolith sky: the Milky Way map, the Sun and the real star catalogue, drawn as an HDRP custom sky.
//
//   Pass 0  Bake    the Milky Way plus the cold ambient fill, into HDRP's lighting cubemap. No stars, no Sun.
//   Pass 1  Render  full-screen background: Milky Way, black lift, the Sun's core, halo and streak.
//   Pass 2  Stars   one small quad per catalogue star, additive, on top of pass 1 (hidden behind the backdrop planet's disc).
//   Pass 3  Planet  the backdrop planet (MonolithPlanet.hlsl), premultiplied over pass 1 and under the stars; the bake draws it too.
//
// Stars are placed by the camera's own view-projection (the same matrix, jitter included, that the geometry uses) and each
// is a point-spread function sized in screen pixels: a Gaussian core integrated exactly over each pixel (so a star's total
// light does not change as it slides across the pixel grid, which is what makes point stars shimmer) plus a faint wide
// halo for bright ones. Nothing is sampled from a texture except the Milky Way, which is smooth.
Shader "Hidden/Monolith/Sky"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
    #pragma editor_sync_compilation

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Sky/SkyUtils.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

    TEXTURE2D(_MilkyWay);
    SAMPLER(sampler_MilkyWay);

    float4x4 _SkyToWorld;      // equatorial -> world
    float4x4 _WorldToSky;      // world -> equatorial
    float4x4 _SkyViewProj;     // the camera's view-projection (direction in, w = 0)

    float4 _MilkyWayParams;    // x gain, y contrast, z angle of one output pixel in radians, w angle of one map texel at the equator in radians
    float4 _BlackLift;         // rgb
    float4 _MilkyWayTint;      // rgb tint, w saturation
    float4 _AmbientFill;       // rgb nits (bake only)
    float4 _AmbientShape;      // x: how much stronger the fill is away from the Sun (0 = even all round), y: gain on the Milky Way in the fill
    float4 _SunDirWS;          // xyz: unit vector toward the Sun
    float  _NitsPerUnit;       // nits in one display unit at the reference exposure

    float4 _SunPx;             // xy: position in pixels (SV_Position convention), z: 1 when the Sun is in front of the camera
    float4 _SunColor;
    float4 _SunCore;           // x energy, y sigma in pixels

    StructuredBuffer<float4> _StarPosMag;
    StructuredBuffer<uint>   _StarColor;
    float4 _StarParams;        // x gain, y gamma, z reference magnitude, w limit magnitude
    float4 _StarShape;         // x core sigma px, y saturation, z halo fraction
    float4 _StarHalo;          // x halo size px (dim), y halo size px (bright), z streak strength, w streak reach px (brightest)
    float4 _StarExposure;      // x frame brightness at which the stars lose half their light, y how sharply, z the same for the Milky Way (exponent), w fraction of the faintest stars a camera misses
    float4 _StarVariety;       // x brightness jitter, y size jitter, z glint rate (share of the brightest stars with a vertical glint), w glint length px
    TEXTURE2D(_MnSceneKey);    // 1 x 1, written by the camera look's meter: mean of sqrt(luminance) of the last frame (black if there is no meter)

    #include "MonolithPlanet.hlsl"

    // ------------------------------------------------------------------------------------------------ helpers

    // Abramowitz and Stegun 7.1.27, |error| < 5e-4, smooth everywhere.
    float ErfApprox(float x)
    {
        float ax = abs(x);
        float t = 1.0 + ax * (0.278393 + ax * (0.230389 + ax * (0.000972 + ax * 0.078108)));
        float t2 = t * t;
        float r = 1.0 - 1.0 / (t2 * t2);
        return x < 0.0 ? -r : r;
    }

    // Fraction of a 1D Gaussian (invS = 1 / (sigma * sqrt(2))) that falls inside the unit-wide pixel centred d from the star.
    float PixelFraction(float d, float invS)
    {
        return 0.5 * (ErfApprox((d + 0.5) * invS) - ErfApprox((d - 0.5) * invS));
    }

    // HDRP's GetSkyViewDirWS points from the sky back toward the camera (its own gradient sky uses -y for "up"), so flip it.
    float3 ViewDirWS(float2 positionCS)
    {
        return -GetSkyViewDirWS(positionCS);
    }

    float3 MilkyWay(float3 dirWS)
    {
        float3 e = mul((float3x3)_WorldToSky, dirWS);
        float ra = atan2(e.y, e.x);
        float lat = asin(clamp(e.z, -1.0, 1.0));
        float2 uv = float2(0.5 - ra * (0.5 * INV_PI), 0.5 + lat * INV_PI);
        // Pick the mip by hand: the map is equirectangular, so a texel is narrower the nearer the pole, and automatic
        // derivatives would also jump at the wrap. Isotropic, so it blurs a little at the poles instead of aliasing.
        float widthScale = max(cos(lat), 0.002);
        float lod = max(0.0, log2(_MilkyWayParams.z / (_MilkyWayParams.w * widthScale)));
        float3 c = SAMPLE_TEXTURE2D_LOD(_MilkyWay, sampler_MilkyWay, uv, lod).rgb;
        c = pow(max(c, 0.0), _MilkyWayParams.y);
        c = lerp(dot(c, float3(0.2126, 0.7152, 0.0722)), c, _MilkyWayTint.w);
        return c * _MilkyWayParams.x * _MilkyWayTint.rgb;
    }

    // The Sun itself: a point. Its glare (veil, wings, streaks, ghosts, dust) is the camera look's job (Monolith.Look), added over the whole picture.
    float3 SunLayer(float2 pos)
    {
        if (_SunPx.z < 0.5) return 0.0;
        float2 d = pos - _SunPx.xy;
        float invS = rcp(max(_SunCore.y, 0.05) * 1.41421356);
        float core = PixelFraction(d.x, invS) * PixelFraction(d.y, invS);
        return _SunColor.rgb * (_SunCore.x * core);
    }

    // ------------------------------------------------------------------------------------------------ full-screen passes

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
        return output;
    }

    float4 FragBake(Varyings input) : SV_Target
    {
        float3 dirWS = ViewDirWS(input.positionCS.xy);
        // The lighting cubemap: cold fill that is strongest on the side facing away from the Sun, plus the Milky Way's own light, so a
        // hull's shadow side keeps a gradient (form) instead of one flat tone, and never goes black.
        float awayFromSun = dot(dirWS, -_SunDirWS.xyz);
        float3 fill = _AmbientFill.rgb * max(0.0, 1.0 + _AmbientShape.x * awayFromSun);
        float3 c = MilkyWay(dirWS) * (_NitsPerUnit * _AmbientShape.y) + fill;
        if (_PlanetDir.w > 0.5)
        {
            // The backdrop planet is the biggest, warmest light in the sky: its reflected sunlight is what lights the ships' planet side (diffuse from the ambient probe, shine from the reflections).
            float4 pl = PlanetShade(dirWS, dirWS, dirWS, 4.0);
            c = c * (1.0 - pl.a) + pl.rgb * _PlanetPx.w;
        }
        return float4(c, 1.0);
    }

    // How much of the faint light a camera exposed for this frame still catches: 1 in a dark quiet frame, falling toward 0 as the frame holds more light
    // (the Sun, lit hulls, lasers, explosions). The meter gives the last frame's brightness.
    float CameraCatch()
    {
        float key = LOAD_TEXTURE2D(_MnSceneKey, int2(0, 0)).x;
        float L = key * key;
        return rcp(1.0 + pow(L / max(_StarExposure.x, 1e-5), max(_StarExposure.y, 0.1)));
    }

    float4 FragRender(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float3 dirWS = ViewDirWS(input.positionCS.xy);
        float3 c = MilkyWay(dirWS) * pow(CameraCatch(), _StarExposure.z) + _BlackLift.rgb + SunLayer(input.positionCS.xy);
        return float4(c * (_NitsPerUnit * GetCurrentExposureMultiplier()), 1.0);
    }

    float4 FragPlanet(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 pos = input.positionCS.xy;
        float3 d = ViewDirWS(pos);
        if (_PlanetDir.w < 0.5) return 0.0;
        float4 p = PlanetAndMoons(d, ViewDirWS(pos + float2(1.0, 0.0)), ViewDirWS(pos + float2(0.0, 1.0)));
        p.rgb *= GetCurrentExposureMultiplier();
        return p;
    }

    // ------------------------------------------------------------------------------------------------ stars

    struct StarVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 offsetPx   : TEXCOORD0;                        // this pixel's centre minus the star's centre, in pixels
        nointerpolation float4 energy : TEXCOORD1;            // x core energy, y halo energy, z halo size px, w 1 / (sigma * sqrt 2)
        nointerpolation float2 streak : TEXCOORD3;            // x streak peak, y streak reach px
        nointerpolation float2 wing : TEXCOORD4;              // x wing energy, y wing size px
        nointerpolation float2 glint : TEXCOORD5;             // x vertical glint peak, y its reach px
        nointerpolation float3 colour : TEXCOORD2;            // luminance-1 colour
    };

    static const float2 kCorner[6] = { float2(-1, -1), float2(-1, 1), float2(1, -1), float2(1, -1), float2(-1, 1), float2(1, 1) };

    StarVaryings VertStar(uint vertexID : SV_VertexID)
    {
        StarVaryings o;
        o.positionCS = float4(2.0, 2.0, 2.0, 1.0);            // off-screen: the whole quad is clipped away unless the star is real
        o.offsetPx = 0.0;
        o.energy = 0.0;
        o.colour = 0.0;
        o.streak = 0.0;
        o.wing = 0.0;
        o.glint = 0.0;

        uint star = vertexID / 6u;
        uint corner = vertexID - star * 6u;

        float4 pm = _StarPosMag[star];
        float3 dirWS = mul((float3x3)_SkyToWorld, pm.xyz);
        float4 clip = mul(_SkyViewProj, float4(dirWS, 0.0));
        if (clip.w < 1e-3) return o;
        float behindPlanet = PlanetAlpha(dirWS);                // the backdrop planet's disc and haze hide the stars behind them
        if (behindPlanet > 0.999) return o;

        // Brightness: real flux ratios raised to gamma, so Sirius and a magnitude-8 star both stay on the screen.
        float mag = pm.w;
        float fade = saturate((_StarParams.w - mag) * 4.0);
        float flux = _StarParams.x * exp2(-0.4 * _StarParams.y * (mag - _StarParams.z) * 3.321928) * fade;

        // Every star gets its own dice: the camera misses some of the faint ones, and no two catch the same light, the same size or the same tint.
        float h1 = GenerateHashedRandomFloat(uint2(star, 1u)), h2 = GenerateHashedRandomFloat(uint2(star, 2u)), h3 = GenerateHashedRandomFloat(uint2(star, 3u));
        float h4 = GenerateHashedRandomFloat(uint2(star, 4u)), h5 = GenerateHashedRandomFloat(uint2(star, 5u)), h6 = GenerateHashedRandomFloat(uint2(star, 6u));
        float keep = 1.0 - _StarExposure.w * saturate((mag - 6.0) / 2.6);
        if (h3 > keep) return o;
        flux *= CameraCatch() * lerp(1.0 - _StarVariety.x, 1.0 + _StarVariety.x, h1) * (1.0 - behindPlanet);
        if (flux < 1e-6) return o;

        float sigma = _StarShape.x * lerp(1.0 - _StarVariety.y, 1.0 + 1.4 * _StarVariety.y, h2);
        float lf = log2(flux) * 0.30103;                      // log10
        float t = saturate((lf + 1.5) / 3.0);                 // 0 for dim stars, 1 for the brightest
        // A point-spread function: the sharp core, a halo that even middling stars have a little of, and wide wings on the bright ones.
        float haloFrac = _StarShape.z * (0.25 + 0.75 * t) * (0.4 + 0.6 * t);
        float a = lerp(_StarHalo.x, _StarHalo.y, t);
        float wingFrac = 0.22 * smoothstep(0.15, 0.85, t);
        float aw = lerp(_StarHalo.y * 1.8, _StarHalo.y * 5.5, t);
        float haloPeak = flux * haloFrac * rcp(PI * a * a);
        float rHalo = a * sqrt(max(sqrt(haloPeak / 0.0012) - 1.0, 0.0));
        float wingPeak = flux * wingFrac * rcp(PI * aw * aw);
        rHalo = max(rHalo, aw * sqrt(max(sqrt(wingPeak / 0.0012) - 1.0, 0.0)));
        float extent = clamp(max(3.2 * sigma, rHalo) + 1.0, 3.0, 140.0);
        // The brightest stars also get a faint horizontal streak, like the Sun does (only the top of the range: t above about 0.75).
        float streakK = _StarHalo.z * smoothstep(0.72, 1.0, t) * lerp(0.2, 1.7, h5);
        float streakReach = _StarHalo.w * streakK;
        float extentX = clamp(max(extent, streakReach * 6.0 + 2.0), 3.0, 240.0);
        // A share of the brightest stars also catch a short vertical glint (a cross), most do not.
        float glintK = smoothstep(0.62, 0.95, t) * step(1.0 - _StarVariety.z, h6) * lerp(0.5, 1.4, h4);
        float glintReach = _StarVariety.w * glintK;
        float extentY = clamp(max(extent, glintReach * 6.0 + 2.0), 3.0, 240.0);

        float2 ndc = clip.xy / clip.w;
        float2 marginNdc = float2(extentX, extentY) * 2.0 * _ScreenSize.zw;
        if (any(abs(ndc) > 1.0 + marginNdc)) return o;

        uint packed = _StarColor[star];
        float3 chroma = float3(packed & 255u, (packed >> 8) & 255u, (packed >> 16) & 255u) * (1.0 / 255.0);
        float lum = dot(chroma, float3(0.2126, 0.7152, 0.0722));
        float sat = _StarShape.y * lerp(0.35, 1.0, h4) * (1.0 - 0.5 * t);
        o.colour = lerp(1.0, chroma / max(lum, 1e-3), sat) * lerp(float3(1.0, 0.97, 0.90), float3(0.90, 0.97, 1.0), h5);

        o.energy = float4(flux * (1.0 - haloFrac - wingFrac), flux * haloFrac, a, rcp(sigma * 1.41421356));
        o.wing = float2(flux * wingFrac, aw);
        float2 offsetPx = kCorner[corner] * float2(extentX, extentY);
        o.streak = float2(flux * 0.02 * streakK, max(streakReach, 0.01));
        o.glint = float2(flux * 0.012 * glintK, max(glintReach, 0.01));
        o.offsetPx = offsetPx;
        float2 ndcOut = ndc + offsetPx * 2.0 * _ScreenSize.zw;
        o.positionCS = float4(ndcOut * clip.w, UNITY_RAW_FAR_CLIP_VALUE * clip.w, clip.w);
        return o;
    }

    float4 FragStar(StarVaryings i) : SV_Target
    {
        float2 d = i.offsetPx;
        float core = PixelFraction(d.x, i.energy.w) * PixelFraction(d.y, i.energy.w);
        float a2 = i.energy.z * i.energy.z;
        float u = dot(d, d) / a2;
        float halo = rcp(PI * a2) / ((1.0 + u) * (1.0 + u));
        float aw2 = i.wing.y * i.wing.y;
        float uw = dot(d, d) / max(aw2, 1e-4);
        float wing = i.wing.x > 0.0 ? rcp(PI * max(aw2, 1e-4)) / ((1.0 + uw) * (1.0 + uw)) : 0.0;
        float v = i.energy.x * core + i.energy.y * halo + i.wing.x * wing;
        if (i.streak.x > 0.0)
            v += i.streak.x * exp(-abs(d.y) / 0.8) * pow(rcp(1.0 + abs(d.x) / i.streak.y), 1.6);
        if (i.glint.x > 0.0)
            v += i.glint.x * exp(-abs(d.x) / 0.7) * pow(rcp(1.0 + abs(d.y) / i.glint.y), 1.8);
        return float4(i.colour * (v * _NitsPerUnit * GetCurrentExposureMultiplier()), 0.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass    // 0: lighting cubemap
        {
            Name "Bake"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBake
            ENDHLSL
        }

        Pass    // 1: background
        {
            Name "Render"
            ZWrite Off
            ZTest LEqual
            Blend Off
            Cull Off
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragRender
            ENDHLSL
        }

        Pass    // 2: stars
        {
            Name "Stars"
            ZWrite Off
            ZTest LEqual
            Blend One One
            Cull Off
            HLSLPROGRAM
                #pragma vertex VertStar
                #pragma fragment FragStar
            ENDHLSL
        }

        Pass    // 3: the backdrop planet
        {
            Name "Planet"
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            Cull Off
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragPlanet
            ENDHLSL
        }
    }
    Fallback Off
}
