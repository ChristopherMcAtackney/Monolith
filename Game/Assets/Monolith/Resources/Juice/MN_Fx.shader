// Monolith juice: every glow, bolt, beam, spark, fireball, ring, flare and scorch is a quad drawn by one HDRP custom pass
// (Juice/Fx/FxPass.cs) in the HDR colour buffer before post-processing, so the sky builder's bloom does the glow.
// Colours are "display units at the reference exposure" like the sky's: 1.0 is the brightest an 8-bit picture shows before
// tonemapping; effects use 5 to 60 so the bloom has something to spread. Output = units * nitsPerUnit * exposure multiplier,
// so the effects follow the camera's exposure the way real light does and never fight it.
//
//   Pass 0 Add     additive quads (everything that glows)
//   Pass 1 Alpha   premultiplied alpha quads (scorch marks: a dark blot with a hot rim)
//   Pass 2 Shield  a shield bubble (ellipsoid mesh) with hex ripples at up to 8 hit points
//   Pass 3 Edge    a screen-edge flash that never touches the middle of the picture (hits on the player)
//
// Vertex layout of passes 0 and 1 (one quad = 4 vertices, corner in -1..1):
//   POSITION   centre, world space metres
//   COLOR      rgb = colour in display units, a = kind specific (bolt: core strength)
//   TEXCOORD0  corner.x, corner.y, kind, seed
//   TEXCOORD1  oriented kinds: axis.xyz (unit), half length m.   billboards: half height m (0 = square), -, -, angle (radians)
//   TEXCOORD2  half width m, age 0..1, p1, p2
//   TEXCOORD3  soft distance m (0 = off), near fade start m, near fade range m, palette (0 fire, 1 plasma, 0..1 mix)
//   TEXCOORD4  beams: whole beam length m, this quad's start and end as fractions of it
// Kinds: 0 glow, 1 fire, 2 streak (bolt, spark, missile trail), 3 ring, 4 flare, 5 scorch, 7 beam.
Shader "Hidden/Monolith/Fx"
{
    SubShader
    {
        HLSLINCLUDE
        #pragma target 4.5
        #pragma only_renderers d3d11 vulkan metal
        #pragma editor_sync_compilation

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

        float4x4 _MN_Matrix;        // projection * view, camera-relative
        float4 _MN_CamRight;        // xyz
        float4 _MN_CamUp;
        float4 _MN_CamFwd;
        float4 _MN_Eye;             // the camera's world position (vertices are world space; positions are made eye-relative here)
        float4 _MN_Screen;          // width, height, 1/width, 1/height
        float4 _MN_Params;          // x nits per display unit, y focal length in pixels, z min half width of a streak in pixels, w time (s)

        // ------------------------------------------------------------------ noise

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float Noise3(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i);
            float n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0));
            float n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1));
            float n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1));
            float n111 = Hash31(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }

        float Fbm3(float3 p)
        {
            float a = 0.5, s = 0.0;
            [unroll] for (int i = 0; i < 4; i++)
            {
                s += a * Noise3(p);
                p = p * 2.03 + float3(17.1, 9.2, 5.7);
                a *= 0.5;
            }
            return s * 1.0667;     // 0..1
        }

        float Fbm2(float3 p)
        {
            return (0.6 * Noise3(p) + 0.4 * Noise3(p * 2.1 + 3.7));
        }

        // ------------------------------------------------------------------ colour

        // A cooling fireball: deep red, orange, yellow, then white at the top. T 0..1.3
        float3 Incandescent(float T)
        {
            return float3(saturate(T * 2.4), saturate((T - 0.38) * 1.6), saturate((T - 0.85) * 2.2));
        }

        // The Expanse's blue-white plasma: violet-blue when cool, white-cyan when hot.
        float3 PlasmaRamp(float T)
        {
            return float3(saturate((T - 0.42) * 1.7), saturate((T - 0.12) * 1.5), saturate(T * 2.0 + 0.15));
        }

        // ------------------------------------------------------------------ pass 0 and 1: quads

        struct FxIn
        {
            float3 pos : POSITION;
            float4 col : COLOR;
            float4 t0 : TEXCOORD0;
            float4 t1 : TEXCOORD1;
            float4 t2 : TEXCOORD2;
            float4 t3 : TEXCOORD3;
            float4 t4 : TEXCOORD4;
        };

        struct FxOut
        {
            float4 pos : SV_POSITION;
            float4 col : COLOR;
            float4 uv : TEXCOORD0;      // xy = corner (billboards) or (x in half widths, y), z = kind, w = seed
            float4 p : TEXCOORD1;       // age, p1, p2, palette
            float4 q : TEXCOORD2;       // soft distance, near fade start, near fade range, view depth
            float4 r : TEXCOORD3;       // x = half length / half width (oriented) or width / height (billboards)
            float4 s : TEXCOORD4;       // beams: total length, start fraction, end fraction
        };

        FxOut VertFx(FxIn a)
        {
            FxOut o;
            float kind = a.t0.z;
            float2 corner = a.t0.xy;
            float halfW = a.t2.x;
            float3 centre = a.pos - _MN_Eye.xyz;
            float3 wpos;
            float dist = max(length(centre), 0.01);
            float aspect = 1.0;
            float2 local = corner;

            if ((kind > 1.5 && kind < 2.5) || kind > 6.5)
            {
                // Oriented quads (bolts, sparks, trails, beams): a cylinder billboard about the axis.
                float3 ax = a.t1.xyz;
                float len = max(a.t1.w, 1e-3);
                float3 side = cross(ax, centre / dist);
                float sl = length(side);
                side = sl > 1e-4 ? side / sl : _MN_CamRight.xyz;
                float minHalf = _MN_Params.z * dist / _MN_Params.y;
                float hw = max(halfW, minHalf);
                len = max(len, hw);
                wpos = centre + ax * (corner.x * len) + side * (corner.y * hw);
                local = float2(corner.x * len / hw, corner.y);
                aspect = len / hw;
            }
            else
            {
                float hh = a.t1.x > 0.0 ? a.t1.x : halfW;
                float s, c;
                sincos(a.t1.w, s, c);
                float2 rc = float2(c * corner.x - s * corner.y, s * corner.x + c * corner.y);
                wpos = centre + _MN_CamRight.xyz * (rc.x * halfW) + _MN_CamUp.xyz * (rc.y * hh);
                aspect = halfW / max(hh, 1e-3);
            }

            o.pos = mul(_MN_Matrix, float4(wpos, 1.0));
            o.col = a.col;
            o.uv = float4(local, kind, a.t0.w);
            o.p = float4(a.t2.y, a.t2.z, a.t2.w, a.t3.w);
            o.q = float4(a.t3.x, a.t3.y, a.t3.z, dot(wpos, _MN_CamFwd.xyz));
            o.r = float4(aspect, 0, 0, 0);
            o.s = a.t4;
            return o;
        }

        // The depth and distance fade shared by every quad: soft edges where a quad meets geometry, nothing hugging the lens.
        float DepthFade(FxOut i)
        {
            float f = 1.0;
            if (i.q.x > 0.0)
            {
                float sceneZ = LinearEyeDepth(LoadCameraDepth(uint2(i.pos.xy)), _ZBufferParams);
                f *= saturate((sceneZ - i.q.w) / i.q.x);
            }
            if (i.q.z > 0.0) f *= saturate((i.q.w - i.q.y) / i.q.z);
            return f;
        }

        // Returns rgb (display units) and a coverage / darkening alpha for the alpha pass.
        float4 ShadeFx(FxOut i)
        {
            float kind = i.uv.z;
            float seed = i.uv.w;
            float age = i.p.x;
            float p1 = i.p.y;
            float p2 = i.p.z;
            float2 c = i.uv.xy;
            float3 colour = i.col.rgb;
            float time = _MN_Params.w;

            if (kind < 0.5)
            {
                // Glow: a hard little core on a wide soft halo. p1 = hardness (1 = tight core, 0 = pure halo).
                float r2 = dot(c, c);
                if (r2 >= 1.0) return 0;
                float edge = 1.0 - r2;
                float g = lerp(1.0 / (1.0 + r2 * 40.0), exp(-r2 * 7.0), p1) * edge * edge;
                return float4(colour * g, 0);
            }
            else if (kind < 1.5)
            {
                // Fire: a billowing ball. p1 = heat at birth (about 1 is white-hot), p2 = how fast it shrinks into embers.
                float r2 = dot(c, c);
                if (r2 >= 1.0) return 0;
                float rad = sqrt(r2);
                float3 q = float3(c * 1.55, age * 1.3) + float3(seed * 7.1, seed * 3.3, seed * 13.7);
                float n = Fbm3(q);
                float n2 = Noise3(q * 2.9 + 11.0);
                float edge = 1.0 - rad;
                float mask = edge * 1.45 + (n - 0.5) * 1.35 + (n2 - 0.5) * 0.5 - 0.16 - age * (0.30 + p2) * 0.85;
                float dens = smoothstep(0.0, 0.95, mask);
                float T = p1 * pow(saturate(1.0 - age * 0.78), 1.1) * (0.30 + 0.80 * dens + 0.55 * (n - 0.5) + 0.3 * (n2 - 0.5));
                T = max(T, 0.0);
                float3 ramp = lerp(Incandescent(T), PlasmaRamp(T), saturate(i.p.w));
                // Palette above 1: gas, a pale cool haze whatever its temperature.
                ramp = lerp(ramp, float3(0.80, 0.90, 1.0) * (0.3 + T), saturate(i.p.w - 1.0));
                float bright = 0.08 + T * T * 1.4;
                return float4(colour * ramp * (dens * bright), 0);
            }
            else if (kind < 2.5)
            {
                // Streak: a capsule with a white-hot core, a saturated body and a wide soft halo.
                // c.x runs along the streak in half widths (tail at -aspect, head at +aspect), c.y across.
                float cap = i.r.x;
                float ex = max(abs(c.x) - (cap - 1.0), 0.0);
                float d2 = ex * ex + c.y * c.y;
                if (d2 >= 1.0) return 0;
                float t = saturate(c.x / (2.0 * cap) + 0.5);          // 0 at the tail, 1 at the head
                float tail = lerp(p2, 1.0, pow(t, 1.0 + p1 * 3.0));      // p2 = tail brightness, p1 = how sharply it falls off
                float core = exp(-d2 * 16.0);
                float body = exp(-d2 * 4.2);
                float halo = (1.0 - d2) * exp(-d2 * 1.2);
                float3 rgb = colour * (halo * 0.55 + body * 1.15) + lerp(colour, float3(1, 1, 1), 0.82) * (core * i.col.a);
                return float4(rgb * tail, 0);
            }
            else if (kind < 3.5)
            {
                // Ring: a shock front. p1 = radius (0..1 of the quad), p2 = thickness.
                float rad = length(c);
                if (rad >= 1.0) return 0;
                float ang = atan2(c.y, c.x);
                float rag = Noise3(float3(cos(ang) * 2.0 + seed, sin(ang) * 2.0, seed * 3.0 + age * 3.0));
                float th = max(p2, 0.01) * (0.6 + 0.8 * rag);
                float ring = exp(-pow((rad - p1) / th, 2.0));
                float inner = exp(-pow(rad / max(p1, 0.05), 2.0)) * 0.06;
                float fade = saturate((1.0 - rad) * 6.0);
                return float4(colour * ((ring + inner) * fade), 0);
            }
            else if (kind < 4.5)
            {
                // Flare: a lens-like star. The quad is wide (anamorphic horizontal streak) and the glow is tight.
                float r2 = dot(c, c);
                float asp = max(i.r.x, 1.0);
                float horiz = exp(-abs(c.y) * 26.0) * pow(saturate(1.0 - abs(c.x)), 2.4);
                float vert = exp(-abs(c.x) * 30.0 * asp) * pow(saturate(1.0 - abs(c.y)), 3.0) * 0.28;
                float glow = exp(-(c.x * c.x * asp * asp + c.y * c.y) * 22.0);
                return float4(colour * (horiz + vert + glow * 1.2), 0);
            }
            else if (kind < 5.5)
            {
                // Scorch: a dark blot with a ragged edge and a hot rim that cools. p1 = how dark (0..1), p2 = heat (0..1).
                float r2 = dot(c, c);
                if (r2 >= 1.0) return 0;
                float rad = sqrt(r2);
                float n = Fbm2(float3(c * 3.1 + seed * 9.3, seed * 2.1));
                float shape = saturate((0.92 - rad - (n - 0.5) * 0.95) * 2.4);
                float dark = shape * shape * p1;
                float rim = saturate(1.0 - abs(rad - 0.45 - (n - 0.5) * 0.35) * 5.0) * shape;
                float3 hot = Incandescent(p2 * (0.55 + 0.6 * n)) * (p2 * p2 * 6.0) * colour;
                return float4(hot * (shape * 0.55 + rim) * p2, dark);
            }
            else
            {
                // Beam (kind 7): a long ribbon drawn in segments. c.x along (in half widths), c.y across. p1 = animation speed, p2 = noise scale.
                float cap = i.r.x;
                float y = abs(c.y);
                float along = lerp(i.s.y, i.s.z, c.x * (0.5 / cap) + 0.5);       // 0 at the muzzle, 1 at the far end, over the whole beam
                float xm = along * i.s.x;                                          // metres from the muzzle
                float s1 = Noise3(float3(xm * 0.021 * p2 - time * p1 * 0.05, c.y * 4.0, seed));
                float s2 = Noise3(float3(xm * 0.067 * p2 - time * p1 * 0.11, c.y * 9.0 + 3.0, seed + 5.0));
                float streak = 0.30 + 1.15 * s1 * s1 + 0.55 * s2;
                float core = exp(-y * y * 18.0) * (0.78 + 0.5 * s2);
                float body = exp(-y * y * 4.4) * streak;
                float sheath = pow(saturate(1.0 - y * y), 2.0) * (0.25 + 1.1 * s1);
                float ends = smoothstep(0.0, 0.012, along) * smoothstep(1.0, 0.985, along);
                float3 rgb = colour * (sheath * 0.5 + body * 1.3) + lerp(colour, float3(1, 1, 1), 0.85) * (core * i.col.a);
                return float4(rgb * ends, 0);
            }
        }

        float3 ToExposure(float3 unitsRgb)
        {
            return unitsRgb * (_MN_Params.x * GetCurrentExposureMultiplier());
        }

        ENDHLSL

        Pass
        {
            Name "Add"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex VertFx
            #pragma fragment FragAdd

            float4 FragAdd(FxOut i) : SV_Target
            {
                float4 s = ShadeFx(i);
                float fade = DepthFade(i);
                return float4(ToExposure(s.rgb * fade), 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Alpha"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VertFx
            #pragma fragment FragAlpha

            float4 FragAlpha(FxOut i) : SV_Target
            {
                float4 s = ShadeFx(i);
                float fade = DepthFade(i);
                return float4(ToExposure(s.rgb * fade), saturate(s.a) * fade);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ pass 2: shield bubble

        Pass
        {
            Name "Shield"
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend One One

            HLSLPROGRAM
            #pragma vertex VertShield
            #pragma fragment FragShield

            float4x4 _MN_Object;        // object -> camera-relative world (rotation, scale and translation)
            float4x4 _MN_ObjectRot;     // rotation only
            float4 _ShieldRadii;        // xyz ellipsoid radii in metres
            float4 _ShieldColor;        // rgb display units
            float4 _ShieldParams;       // x rim glow 0..1 (rises after a hit), y ripple speed (rad/s), z ripple width (rad), w hex scale
            float4 _ShieldHit[8];       // xyz unit direction (unit-sphere space), w age in seconds (< 0 = unused)

            struct ShieldIn { float3 pos : POSITION; };
            struct ShieldOut
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                float3 nrm : TEXCOORD1;
                float3 view : TEXCOORD2;
            };

            ShieldOut VertShield(ShieldIn a)
            {
                ShieldOut o;
                float3 w = mul(_MN_Object, float4(a.pos, 1.0)).xyz;
                o.pos = mul(_MN_Matrix, float4(w, 1.0));
                o.dir = a.pos;
                float3 nOs = a.pos / (_ShieldRadii.xyz * _ShieldRadii.xyz);
                o.nrm = normalize(mul((float3x3)_MN_ObjectRot, nOs));
                o.view = normalize(w);
                return o;
            }

            float HexEdge(float2 p)
            {
                // Distance to the nearest hexagon edge of a unit hex grid (0 on an edge, about 0.5 at a centre).
                const float2 s = float2(1.0, 1.7320508);
                float4 hC = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * s, p - (hC.zw + 0.5) * s);
                float2 a = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                float2 q = abs(a);
                return 0.5 - max(dot(q, s * 0.5), q.x);
            }

            float4 FragShield(ShieldOut i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 n = normalize(i.nrm);
                float fres = pow(saturate(1.0 + dot(n, i.view)), 3.0);
                // Hex cells in a triplanar projection of the sphere direction.
                float3 ad = abs(d);
                float2 hp = (ad.x > ad.y && ad.x > ad.z) ? d.yz : (ad.y > ad.z ? d.xz : d.xy);
                float hex = HexEdge(hp * _ShieldParams.w);
                float cell = saturate(1.0 - smoothstep(0.0, 0.07, hex));       // the lit lines of the hexagons
                float cellFill = 0.35 + 0.65 * Hash31(floor(float3(hp * _ShieldParams.w * 1.7, 1.0)));

                float rip = 0.0, flash = 0.0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 h = _ShieldHit[k];
                    if (h.w < 0.0) continue;
                    float ang = acos(clamp(dot(d, h.xyz), -1.0, 1.0));
                    float front = h.w * _ShieldParams.y;
                    float life = exp(-h.w * 2.6);
                    float ring = exp(-pow((ang - front) / _ShieldParams.z, 2.0));
                    rip += ring * life * (0.55 + 0.45 * cell * 2.0) + exp(-pow(ang / (0.12 + front * 0.5), 2.0)) * exp(-h.w * 9.0);
                    flash += exp(-pow(ang / 0.20, 2.0)) * exp(-h.w * 5.5);
                }
                float lines = cell * (0.25 + 1.4 * saturate(rip)) * cellFill;
                float energy = _ShieldParams.x * (fres * 0.9 + 0.06) + rip * 1.1 + flash * 1.6 + lines * (0.3 * _ShieldParams.x + rip);
                return float4(ToExposure(_ShieldColor.rgb * energy), 0.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------ pass 3: screen-edge flash

        Pass
        {
            Name "Edge"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex VertEdge
            #pragma fragment FragEdge

            float4 _MN_EdgeColor;       // rgb display units
            float4 _MN_EdgeParams;      // x inner radius (0..1 of the half diagonal, nothing inside it), y outer radius, z noise, w seed

            struct EdgeOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            EdgeOut VertEdge(uint id : SV_VertexID)
            {
                EdgeOut o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            float4 FragEdge(EdgeOut i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                p.x *= _MN_Screen.x * _MN_Screen.w;       // width / height: a circle on a wide screen
                float r = length(p) / length(float2(_MN_Screen.x * _MN_Screen.w, 1.0));
                float m = smoothstep(_MN_EdgeParams.x, _MN_EdgeParams.y, r);
                float n = Noise3(float3(i.uv * 7.0, _MN_Params.w * 9.0 + _MN_EdgeParams.w));
                m *= lerp(1.0, 0.45 + 0.9 * n, _MN_EdgeParams.z);
                return float4(ToExposure(_MN_EdgeColor.rgb * m), 0.0);
            }
            ENDHLSL
        }
    }
}
