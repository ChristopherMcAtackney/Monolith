// Monolith camera look: what the lens and the film do to the picture.
//
//   Pass 0  Glare   (before post-processing, additive, HDR) the Sun's veil, wings, anamorphic streaks, ghosts and the dust they light.
//                   Added over everything, ships included, so a hull crossing the Sun is veiled; dims when the Sun itself is hidden.
//   Pass 1  Grade   (after post-processing, display values) a graded black floor, a mild vignette and fine film grain.
//   Pass 2/3 Meter  (before post-processing) how bright the frame is: a 32 x 18 grid, then one smoothed number the sky reads next frame to decide how many stars a camera would catch.
//
// Sizes are fractions of the screen height so 1080p and 4K look the same. Nothing here reads a texture except the dust map.
Shader "Hidden/Monolith/Lens"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
    #pragma editor_sync_compilation

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

    TEXTURE2D(_LensDust);
    SAMPLER(sampler_LensDust);
    TEXTURE2D_X(_LookSource);
    TEXTURE2D(_Mottle);
    SAMPLER(sampler_Mottle);

    float4 _SunPx;          // xy: Sun position in pixels (SV_Position convention), z: 1 when the Sun is in front of the camera
    float4 _SunTint;        // rgb: Sun colour, luminance 1
    float4 _GlareScale;     // x: master scale (intensity times nits per display unit), y: visibility floor when the Sun is hidden, z: fringe (channel size spread), w: ghost intensity
    float4 _Veil;           // x: veil energy, y: veil size, z: wing energy, w: wing size
    float4 _Wing;           // x: tight halo energy, y: tight halo size, z: haze energy, w: haze size
    float4 _VeilTint;       // rgb: colour of the wide veil
    float4 _StreakThin;     // x: peak, y: thickness, z: reach, w: unevenness 0..1 (thin white-cyan streak)
    float4 _StreakBroad;    // x: peak, y: thickness, z: reach, w: band spacing (broad coloured streak and its fainter siblings)
    float4 _StreakTint;     // rgb: colour of the broad streaks
    float4 _Dust;           // x: dust lit by the Sun, y: bokeh dust, z: dust map scale
    float4 _Seed;           // x: noise seed
    float4 _Crystal;        // x: crystalline core strength (0 = off), y: core bloom radius, z: spike length (fractions of the screen height)

    float4 _Grade;          // x: grain amount, y: grain size px, z: grain chroma, w: frame
    float4 _Floor;          // rgb: black floor colour (display), w: vignette amount
    float4 _GradeMisc;      // x: master scale for the grade, y: mottle amount, z: mottle size px

    // ----------------------------------------------------------------------------------------------- helpers

    float Moffat(float r2, float a)            // energy-normalised, falls as r^-4 beyond the knee
    {
        float u = 1.0 + r2 / (a * a);
        return rcp(PI * a * a) / (u * u);
    }

    float Hash11(float x) { return GenerateHashedRandomFloat(asuint(x)); }

    // Smooth 1D value noise in 0..1.
    float Noise1(float x)
    {
        float i = floor(x), f = frac(x);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(Hash11(i + 17.0), Hash11(i + 18.0), f);
    }

    float Noise2(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float a = GenerateHashedRandomFloat(uint2(asuint(i.x), asuint(i.y)));
        float b = GenerateHashedRandomFloat(uint2(asuint(i.x + 1.0), asuint(i.y)));
        float c = GenerateHashedRandomFloat(uint2(asuint(i.x), asuint(i.y + 1.0)));
        float d = GenerateHashedRandomFloat(uint2(asuint(i.x + 1.0), asuint(i.y + 1.0)));
        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    // How much of the Sun is in view: depth taps in a small disc around it (the sky is depth 0 with reversed Z).
    float SunVisibility()
    {
        float2 size = _ScreenSize.xy;
        if (any(_SunPx.xy < 0.0) || any(_SunPx.xy >= size)) return 1.0;     // off-screen: nothing can be measured, assume clear
        float R = max(size.y * 0.005, 2.0);
        float vis = 0.0;
        static const float2 kTap[13] = {
            float2(0, 0), float2(0.5, 0), float2(-0.5, 0), float2(0, 0.5), float2(0, -0.5),
            float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
            float2(0.7, 0.7), float2(-0.7, 0.7), float2(0.7, -0.7), float2(-0.7, -0.7) };
        [unroll] for (int i = 0; i < 13; i++)
        {
            int2 p = int2(clamp(_SunPx.xy + kTap[i] * R, 0.0, size - 1.0));
            vis += (LoadCameraDepth(p) < 1e-6) ? 1.0 : 0.0;
        }
        return vis / 13.0;
    }

    // ----------------------------------------------------------------------------------------------- glare

    // A soft ghost: a glow with no edge to it. Round, or a rounded six-sided one (a hexagon blurred and halfway to a circle), a little
    // lopsided and lumpy so it does not look drawn. d is in screen heights from its centre.
    float GhostShape(float2 d, float radius, float hex, float seed)
    {
        float2 q = d / radius;
        float r = length(q);
        if (hex > 0.5 && hex < 1.5)
        {
            float2 a = abs(q);
            float h = max(dot(a, float2(0.8660254, 0.5)), a.y);
            r = lerp(r, h, 0.5);
        }
        r *= 1.0 + 0.18 * (Noise2(q * 1.3 + seed) - 0.5);                  // lopsided
        float body = exp(-r * r * 1.35) * 0.85 + 0.15 * smoothstep(2.2, 0.4, r);
        if (hex > 1.5) body = exp(-pow((r - 0.85) / 0.45, 2.0)) * 0.9;     // a wide soft ring, brightest a little inside its nominal radius, with no edge
        float lumpy = 0.78 + 0.44 * Noise2(q * 2.1 + seed * 3.1 + 7.0);
        return body * lumpy;
    }

    float3 Glare(float2 pos, float vis, out float lightField)
    {
        float H = _ScreenSize.y;
        float2 s = _SunPx.xy;
        float2 d = pos - s;
        float r2 = dot(d, d);
        float seen = lerp(_GlareScale.y, 1.0, vis);                  // glare left when the Sun is hidden: the scatter around the hull's edge
        float fringe = _GlareScale.z;

        // Veil, wings and halo, each a power-law blob; red a little tighter and blue a little wider than green gives the fringe.
        // The veil and wings are scatter and stay (dimmer) when the Sun is hidden; the tight halo is the Sun itself and goes with it.
        float3 sizeK = float3(1.0 - fringe, 1.0, 1.0 + fringe);
        float3 veil = 0.0, halo = 0.0;
        [unroll] for (int ch = 0; ch < 3; ch++)
        {
            float k = sizeK[ch];
            veil[ch] = _Veil.x * Moffat(r2, max(_Veil.y * H * k, 1.0)) + _Veil.z * Moffat(r2, max(_Veil.w * H * k, 1.0))
                     + _Wing.z * Moffat(r2, max(_Wing.w * H * k, 1.0));       // the haze: very wide, very faint, reaches the corners
            halo[ch] = _Wing.x * Moffat(r2, max(_Wing.y * H * k, 1.0));
        }
        float3 wide = (veil * seen + halo * vis) * _VeilTint.rgb;

        // Anamorphic streaks: a thin pale one, and a broad tinted one with fainter siblings above and below. They are fat near the Sun and
        // thin out and fade with distance, break up into patches, wander off the line and are longer to one side.
        float ax = abs(d.x), ay = abs(d.y);
        float u = _StreakThin.w;
        float sideK = d.x > 0.0 ? 1.0 : 0.55 + 0.35 * Noise1(_Seed.x * 0.7);
        float wob = (Noise1(d.x / (0.31 * H) + _Seed.x * 0.3 + 9.0) - 0.5) * 0.004 * H * u;
        float thin = 0.0, broad = 0.0;
        if (ay < 0.22 * H)       // streaks are thin: nothing to compute farther from their line than this
        {
            float tr = ax / max(_StreakThin.z * H * sideK, 1.0);                       // distance in reach units
            float breakup = lerp(1.0, smoothstep(0.12, 0.7, Noise1(d.x / (0.09 * H) + _Seed.x * 1.7 + 5.0)) * 0.8 + 0.2, u)
                          * lerp(1.0, 0.45 + 0.55 * Noise1(d.x / (0.27 * H) + _Seed.x), u);
            float thick = _StreakThin.y * H * lerp(2.6, 0.5, saturate(tr / 2.2)) * lerp(1.0, 0.6 + 0.8 * Noise1(d.x / (0.13 * H) + 3.0), u);
            float taper = exp(-pow(tr, 0.9) * 1.5);
            thin = _StreakThin.x * exp(-abs(d.y - wob) / max(thick, 0.3)) * taper * breakup * vis;
            [unroll] for (int b = 0; b < 3; b++)
            {
                float off = (b == 0) ? 0.0 : ((b == 1) ? 1.0 : -1.6) * _StreakBroad.w * H;
                float amp = (b == 0) ? 1.0 : ((b == 1) ? 0.50 : 0.32);
                float reach = _StreakBroad.z * H * ((b == 0) ? 1.0 : 0.7);
                float tb = ax / max(reach, 1.0);
                float th = _StreakBroad.y * H * ((b == 0) ? 1.0 : 1.6) * lerp(1.7, 0.7, saturate(tb / 2.0));
                float yy = abs(d.y - off - wob * 1.5);
                broad += amp * exp(-pow(yy / max(th, 0.5), 1.4)) * exp(-pow(tb, 0.9) * 1.3)
                         * lerp(1.0 - 0.8 * u, 1.0, Noise1(d.x / (0.15 * H) + _Seed.x * 2.3 + 11.0 * b));
            }
            broad *= _StreakBroad.x * lerp(0.12, 1.0, vis);
        }
        // The broad streaks drift in hue along their length (teal toward violet), as coated glass does.
        float hue = Noise1(d.x / (0.33 * H) + _Seed.x * 0.5 + 21.0);
        float3 broadTint = lerp(_StreakTint.rgb, _StreakTint.zxy, 0.4 * hue);
        float3 streak = _SunTint.rgb * float3(0.75, 0.95, 1.0) * thin + broadTint * broad;

        // The crystalline core: a soft six-sided bloom on the Sun and a few uneven spikes at 30 degree steps (three lines through it), each
        // a different length and strength, pale cyan to violet. Nothing here has a hard edge.
        float2 dn = d / H;
        float rN = length(dn);
        float crystal = 0.0;
        if (_Crystal.x > 0.0 && rN < 0.3)
        {
            float2 qa = abs(dn) / max(_Crystal.y, 1e-4);
            float hexd = max(dot(qa, float2(0.8660254, 0.5)), qa.y);
            crystal += _Crystal.x * exp(-pow(hexd, 2.0) * 1.6) * (0.75 + 0.5 * Noise2(dn * 40.0 + _Seed.x));
            [unroll] for (int k = 0; k < 3; k++)
            {
                float ang = 0.5235988 * (1.0 + 2.0 * k);        // 30, 90 and 150 degree lines, each nudged a little
                float2 dir = float2(cos(ang + 0.07 * k), sin(ang + 0.07 * k));
                float along2 = dot(dn, dir), perp = abs(dn.x * dir.y - dn.y * dir.x);
                float len = _Crystal.z * (0.55 + 0.9 * Hash11(_Seed.x + k * 3.1));
                float spike = exp(-perp / (0.0016 + 0.0009 * k)) * exp(-pow(abs(along2) / len, 0.9) * 2.2);
                crystal += _Crystal.x * 0.55 * spike * (0.5 + Hash11(_Seed.x * 1.3 + k * 7.0 + (along2 > 0.0 ? 1.0 : 0.0)));
            }
            crystal *= vis;
        }
        float3 crystalTint = lerp(float3(0.7, 0.9, 1.0), float3(0.85, 0.75, 1.0), saturate(rN * 12.0));

        float3 sunPart = wide + streak + crystalTint * crystal;

        // Ghosts: the Sun reflected between lens surfaces lands on the line through the picture centre, mirrored.
        float2 ctr = 0.5 * _ScreenSize.xy;
        float2 toSun = s - ctr;
        float edgeK = 0.4 + 0.6 * saturate(length(toSun) / (0.55 * H));       // ghosts grow as the Sun moves away from the middle
        float3 ghosts = 0.0;
        if (_GlareScale.w > 0.0)
        {
            static const float kT[5] = { 0.62, 1.45, -0.5, 0.18, 2.1 };
            static const float kR[5] = { 0.055, 0.09, 0.035, 0.022, 0.14 };
            static const float3 kC[5] = { float3(0.30, 0.60, 1.0), float3(1.0, 0.55, 0.38), float3(0.75, 0.55, 1.0), float3(0.45, 1.0, 0.75), float3(0.35, 0.50, 1.0) };
            static const float kA[5] = { 0.42, 0.26, 0.34, 0.34, 0.12 };
            [unroll] for (int g = 0; g < 5; g++)
            {
                float2 gp = ctr - toSun * kT[g];
                float2 gd = (pos - gp) / H;
                if (dot(gd, gd) < 7.0 * kR[g] * kR[g])      // a soft glow is nothing three radii out
                    ghosts += kC[g] * kA[g] * GhostShape(gd, kR[g], g == 1 ? 1.0 : (g == 4 ? 2.0 : 0.0), _Seed.x + g * 5.3);
            }
            ghosts *= _GlareScale.w * edgeK * vis;
        }

        // The light that dust catches: the Sun's own glare here (luminance), so dust lights up near the Sun and nowhere else.
        float L = dot(wide + streak * 1.5, float3(0.2126, 0.7152, 0.0722));
        lightField = min(L, 6.0);

        return sunPart + _SunTint.rgb * ghosts;
    }

    struct GlareVaryings
    {
        float4 positionCS : SV_POSITION;
        nointerpolation float vis : TEXCOORD0;      // how much of the Sun is in view, measured once per vertex instead of once per pixel
        UNITY_VERTEX_OUTPUT_STEREO
    };

    GlareVaryings VertGlare(Attributes input)
    {
        GlareVaryings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
        output.vis = (_SunPx.z < 0.5) ? 0.0 : SunVisibility();
        return output;
    }

    float4 FragGlare(GlareVaryings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        if (_SunPx.z < 0.5) return 0.0;
        float2 pos = input.positionCS.xy;
        float L;
        float3 g = Glare(pos, input.vis, L);

        // Dust and smudges on the lens or canopy, lit by that glare.
        float H = _ScreenSize.y;
        float2 uv = float2(pos.x / (2.0 * H), pos.y / H) * _Dust.z;
        float4 dust = (L > 0.003) ? SAMPLE_TEXTURE2D_LOD(_LensDust, sampler_LensDust, uv, 0) : 0.0;
        float Ld = pow(max(L, 0.0), 1.3) * smoothstep(0.003, 0.02, L);       // dirt is lit where the glare is strong, and hardly at all where it is faint
        float dustGain = Ld * (dust.r * 2.4 + dust.g * 0.9) * _Dust.x + Ld * dust.b * _Dust.y;
        g += _SunTint.rgb * dustGain;

        return float4(g * (_GlareScale.x * GetCurrentExposureMultiplier()), 0.0);
    }

    // ----------------------------------------------------------------------------------------------- grade

    // Unit-variance noise made of two scales: single-pixel static and clumps about _Grade.y pixels across, so that some of it survives being
    // scaled down and compressed (the single-pixel part does not).
    float GrainSample(float2 pix, float frame, float channel)
    {
        float2 off = float2(frame * 37.0 + channel * 101.0, frame * 91.0 + channel * 53.0);
        float fine = (GenerateHashedRandomFloat(uint3(uint(pix.x), uint(pix.y), uint(frame * 7.0 + channel * 3.0 + 11.0))) - 0.5) / 0.2887;
        float clump = (Noise2(pix / max(_Grade.y, 0.6) + off) - 0.5) / 0.17;
        return 0.55 * fine + 0.83 * clump;
    }

    float4 FragGrade(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        uint2 pix = uint2(input.positionCS.xy);
        float3 c = LOAD_TEXTURE2D_X(_LookSource, pix).rgb;
        float k = _GradeMisc.x;

        float H = _ScreenSize.y;
        float2 q = (input.positionCS.xy - 0.5 * _ScreenSize.xy) / H;
        float rr = length(q);

        // Vignette: corners lose some light.
        c *= 1.0 - _Floor.w * k * smoothstep(0.45, 1.15, rr);

        // Floor and grain are done in display-encoded values (what the eye sees as steps), not in linear light, so that a few 8-bit
        // levels of floor and grain stay a few levels instead of exploding near black.
        float3 e = LinearToSRGB(saturate(c));

        // Black floor: what the darkest areas rise to, a graded colour that drifts across the frame (a little bluer in one place, a little greener or
        // warmer in another) instead of one flat navy: a slow, fixed pattern, mostly in brightness with only a hint of hue, a level or two at most.
        float2 sp = input.positionCS.xy;
        float4 mt = SAMPLE_TEXTURE2D_LOD(_Mottle, sampler_Mottle, sp / _ScreenSize.xy, 0);     // 0..1: r common + detail, g b a hue drift (made once, on the CPU)
        float common = (mt.r - 0.5) * 3.2;
        float3 hue = (mt.gba - 0.5) * 3.2;
        float3 drift = common.xxx * 0.8 + hue * 0.3;
        float3 floorC = _Floor.rgb * k * (1.0 + 0.30 * drift);
        e = e + floorC * (1.0 - e);
        e += _GradeMisc.y * k * drift * (1.0 - e);

        // Film grain, strongest in the shadows and midtones.
        float lum = dot(e, float3(0.2126, 0.7152, 0.0722));
        float resp = lerp(1.0, 0.45, smoothstep(0.1, 0.8, lum));
        float amp = _Grade.x * k * resp;
        float mono = GrainSample(input.positionCS.xy, _Grade.w, 0.0);
        float3 gr = float3(GrainSample(input.positionCS.xy, _Grade.w, 1.0), GrainSample(input.positionCS.xy, _Grade.w, 2.0), GrainSample(input.positionCS.xy, _Grade.w, 3.0));
        e += amp * lerp(mono.xxx, gr, _Grade.z);

        c = SRGBToLinear(saturate(e));
        return float4(max(c, 0.0), 1.0);
    }

    // ----------------------------------------------------------------------------------------------- meter

    TEXTURE2D_X(_MeterSrc);
    TEXTURE2D(_MeterSmall);
    TEXTURE2D(_MeterPrev);
    float4 _MeterOut;       // xy: size of the target
    float4 _MeterTime;      // x: blend toward a brighter reading (the iris closes fast), y: toward a darker one (it opens slowly), z: 1 on the first frame or after a cut

    // 32 x 18 grid of the square root of luminance (so a bright patch counts by how much of the frame it covers, not by how hot it is).
    float4 FragMeterA(Varyings input) : SV_Target
    {
        float2 cell = floor(input.positionCS.xy);
        float2 cellPx = _ScreenSize.xy / _MeterOut.xy;
        float acc = 0.0;
        [unroll] for (int y = 0; y < 6; y++)
        {
            [unroll] for (int x = 0; x < 6; x++)
            {
                float2 p = (cell + (float2(x, y) + 0.5) / 6.0) * cellPx;
                float3 col = LOAD_TEXTURE2D_X(_MeterSrc, uint2(p)).rgb;
                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                acc += sqrt(clamp(lum, 0.0, 8.0));
            }
        }
        return float4(acc / 36.0, 0.0, 0.0, 1.0);
    }

    // The grid to one number, smoothed with a fast attack and slow release.
    float4 FragMeterB(Varyings input) : SV_Target
    {
        float acc = 0.0;
        for (uint y = 0; y < 18; y++)
            for (uint x = 0; x < 32; x++)
                acc += LOAD_TEXTURE2D(_MeterSmall, uint2(x, y)).x;
        float cur = acc / 576.0;
        float prev = LOAD_TEXTURE2D(_MeterPrev, uint2(0, 0)).x;
        float v = (_MeterTime.z > 0.5) ? cur : lerp(prev, cur, cur > prev ? _MeterTime.x : _MeterTime.y);
        return float4(v, 0.0, 0.0, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass    // 0: glare, added to the HDR picture
        {
            Name "Glare"
            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off
            HLSLPROGRAM
                #pragma vertex VertGlare
                #pragma fragment FragGlare
            ENDHLSL
        }

        Pass    // 1: grade, replaces the picture
        {
            Name "Grade"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragGrade
            ENDHLSL
        }

        Pass    // 2: meter, camera colour to a 32 x 18 grid
        {
            Name "MeterGrid"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragMeterA
            ENDHLSL
        }

        Pass    // 3: meter, grid to one smoothed number
        {
            Name "MeterKey"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragMeterB
            ENDHLSL
        }
    }
    Fallback Off
}
