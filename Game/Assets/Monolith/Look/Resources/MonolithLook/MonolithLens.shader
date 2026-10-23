// Monolith camera look: what the lens and the film do to the picture.
//
//   Pass 0  Glare   (before post-processing, additive, HDR) the Sun's veil, wings, anamorphic streaks, ghosts and the dust they light.
//                   Added over everything, ships included, so a hull crossing the Sun is veiled; dims when the Sun itself is hidden.
//   Pass 1  Grade   (after post-processing, display values) a graded black floor, a mild vignette and fine film grain.
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

    float4 _Grade;          // x: grain amount, y: grain size px, z: grain chroma, w: frame
    float4 _Floor;          // rgb: black floor colour (display), w: vignette amount
    float4 _GradeMisc;      // x: master scale for the grade

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

    // A soft round or six-sided ghost: 1 at its rim, fading in toward the middle and out beyond.
    float GhostShape(float2 d, float radius, float hex)
    {
        float r = length(d);
        if (hex > 0.5)
        {
            float2 q = abs(d);
            r = max(dot(q, float2(0.8660254, 0.5)), q.y) * 1.0;      // hexagon distance
        }
        float edge = smoothstep(radius * 1.12, radius * 0.92, r);       // outer edge
        float body = 0.35 + 0.65 * smoothstep(radius * 0.2, radius * 0.95, r);    // brighter toward the rim
        return edge * body;
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

        // Anamorphic streaks: a thin pale one, and a broad tinted one with fainter siblings above and below. Uneven along their length.
        float ax = abs(d.x), ay = abs(d.y);
        float u = _StreakThin.w;
        float along = lerp(1.0 - u, 1.0, Noise1(d.x / (0.23 * H) + _Seed.x)) * lerp(1.0 - 0.5 * u, 1.0, Noise1(d.x / (0.07 * H) + _Seed.x * 1.7 + 5.0));
        float wob = (Noise1(d.x / (0.31 * H) + _Seed.x * 0.3 + 9.0) - 0.5) * 0.004 * H * u;    // the streak wanders a little off the line
        float yThin = abs(d.y - wob);
        float sideK = d.x > 0.0 ? 1.0 : 0.55 + 0.35 * Noise1(_Seed.x * 0.7);      // longer on one side than the other
        float thick = _StreakThin.y * H * lerp(1.0, 0.45 + 1.5 * Noise1(d.x / (0.13 * H) + 3.0), u);
        float thin = _StreakThin.x * exp(-yThin / max(thick, 0.3))
                     * rcp(1.0 + ax / max(_StreakThin.z * H * sideK, 1.0)) * exp(-ax / (7.0 * _StreakThin.z * H * sideK + 1.0)) * along * vis;
        float broad = 0.0;
        [unroll] for (int b = 0; b < 3; b++)
        {
            float off = (b == 0) ? 0.0 : ((b == 1) ? 1.0 : -1.6) * _StreakBroad.w * H;
            float amp = (b == 0) ? 1.0 : ((b == 1) ? 0.30 : 0.18);
            float reach = _StreakBroad.z * H * ((b == 0) ? 1.0 : 0.7);
            float th = _StreakBroad.y * H * ((b == 0) ? 1.0 : 1.6);
            float yy = abs(d.y - off - wob * 1.5);
            broad += amp * exp(-pow(yy / max(th, 0.5), 1.4)) * rcp(1.0 + ax / max(reach, 1.0)) * exp(-ax / (6.0 * reach + 1.0))
                     * lerp(1.0 - 0.7 * u, 1.0, Noise1(d.x / (0.19 * H) + _Seed.x * 2.3 + 11.0 * b));
        }
        broad *= _StreakBroad.x * lerp(0.12, 1.0, vis);
        // The broad streaks drift in hue along their length (teal toward violet), as coated glass does.
        float hue = Noise1(d.x / (0.33 * H) + _Seed.x * 0.5 + 21.0);
        float3 broadTint = lerp(_StreakTint.rgb, _StreakTint.zxy, 0.4 * hue);
        float3 streak = _SunTint.rgb * float3(0.75, 0.95, 1.0) * thin + broadTint * broad;

        float3 sunPart = wide + streak;

        // Ghosts: the Sun reflected between lens surfaces lands on the line through the picture centre, mirrored.
        float2 ctr = 0.5 * _ScreenSize.xy;
        float2 toSun = s - ctr;
        float edgeK = saturate(length(toSun) / (0.55 * H));            // ghosts grow as the Sun moves away from the middle
        float3 ghosts = 0.0;
        if (_GlareScale.w > 0.0)
        {
            static const float kT[4] = { 0.55, 1.35, 1.9, -0.45 };
            static const float kR[4] = { 0.050, 0.030, 0.085, 0.020 };
            static const float3 kC[4] = { float3(0.35, 0.65, 1.0), float3(1.0, 0.55, 0.35), float3(0.55, 1.0, 0.8), float3(0.9, 0.8, 1.0) };
            static const float kA[4] = { 0.5, 0.35, 0.22, 0.45 };
            [unroll] for (int g = 0; g < 4; g++)
            {
                float2 gp = ctr - toSun * kT[g];
                float2 gd = (pos - gp) / H;
                ghosts += kC[g] * kA[g] * GhostShape(gd, kR[g], g == 2 ? 1.0 : 0.0);
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
        float4 dust = SAMPLE_TEXTURE2D_LOD(_LensDust, sampler_LensDust, uv, 0);
        float Ld = pow(max(L, 0.0), 1.3) * smoothstep(0.003, 0.02, L);       // dirt is lit where the glare is strong, and hardly at all where it is faint
        float dustGain = Ld * (dust.r * 2.4 + dust.g * 0.9) * _Dust.x + Ld * dust.b * _Dust.y;
        g += _SunTint.rgb * dustGain;

        return float4(g * (_GlareScale.x * GetCurrentExposureMultiplier()), 0.0);
    }

    // ----------------------------------------------------------------------------------------------- grade

    float GrainSample(float2 pix, float frame, float channel)
    {
        // Two scales of hashed noise, so the grain has clumps a little bigger than a pixel instead of single-pixel static.
        float2 p1 = pix / max(_Grade.y, 0.5);
        float2 off = float2(frame * 37.0 + channel * 101.0, frame * 91.0 + channel * 53.0);
        float a = Noise2(p1 + off) + Noise2(p1 * 1.9 + off.yx + 13.0) * 0.6;
        float b = GenerateHashedRandomFloat(uint3(uint(pix.x), uint(pix.y), uint(frame * 7.0 + channel * 3.0 + 11.0)));
        float n = (a / 1.6) * 0.65 + b * 0.35;      // 0..1, centred near 0.5
        return (n - 0.5) * 3.4;                     // about unit variance
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

        // Black floor: what the darkest areas rise to, graded rather than flat (a little lighter toward the middle of the frame).
        float3 floorC = _Floor.rgb * k * (1.2 - 0.4 * smoothstep(0.0, 1.1, rr));
        e = e + floorC * (1.0 - e);

        // Film grain, strongest in the shadows and midtones.
        float lum = dot(e, float3(0.2126, 0.7152, 0.0722));
        float resp = lerp(1.0, 0.4, smoothstep(0.1, 0.8, lum));
        float amp = _Grade.x * k * resp;
        float mono = GrainSample(input.positionCS.xy, _Grade.w, 0.0);
        uint3 hp = uint3(pix.x, pix.y, uint(_Grade.w) * 3u + 5u);
        float3 gr = (float3(GenerateHashedRandomFloat(hp), GenerateHashedRandomFloat(hp + 7u), GenerateHashedRandomFloat(hp + 13u)) - 0.5) * 3.4;
        e += amp * lerp(mono.xxx, gr, _Grade.z);

        c = SRGBToLinear(saturate(e));
        return float4(max(c, 0.0), 1.0);
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
    }
    Fallback Off
}
