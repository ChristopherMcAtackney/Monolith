// Monolith juice: every glow, bolt, beam, spark, fireball, ring, flare and scorch is a quad drawn by one HDRP custom pass
// (Juice/Fx/FxPass.cs) in the HDR colour buffer before post-processing, so the sky builder's bloom does the glow.
// Colours are "display units at the reference exposure" like the sky's: 1.0 is the brightest an 8-bit picture shows before
// tonemapping; effects use 5 to 60 so the bloom has something to spread. Output = units * nitsPerUnit * exposure multiplier,
// so the effects follow the camera's exposure the way real light does and never fight it.
//
//   Pass 0 Add     additive quads (everything that glows)
//   Pass 1 Alpha   premultiplied alpha quads: scorch marks, and every fireball, which is a RAYMARCHED VOLUME (a sphere of turbulent
//                  3D noise, lit by its own heat, opaque where it is dense, clipped at the scene's depth) so a blast hides the ship in it
//   Pass 2 Shield  a shield bubble (ellipsoid mesh) with hex ripples at up to 8 hit points
//   Pass 3 Edge    a screen-edge flash that never touches the middle of the picture (hits on the player)
//   Pass 4 BoostLens  the boost's radial blur, colour fringe and rim glow, read from a copy of the picture; clear in the middle and round the target
//
// Vertex layout of passes 0 and 1 (one quad = 4 vertices, corner in -1..1):
//   POSITION   centre, world space metres
//   COLOR      rgb = colour in display units, a = kind specific (bolt: core strength)
//   TEXCOORD0  corner.x, corner.y, kind, seed
//   TEXCOORD1  oriented kinds: axis.xyz (unit), half length m.   billboards: half height m (0 = square), -, -, angle (radians)
//   TEXCOORD2  half width m (a fireball: its radius), age 0..1, p1, p2
//   TEXCOORD3  soft distance m (0 = off), near fade start m, near fade range m, palette (0 fire, 1 plasma, 0..1 mix, above 1 cool gas)
//   TEXCOORD4  streaks: x = blur 0..1 (0 = hard saturated, 1 = soft smear).  beams: total length m, this quad's start and end fractions, core fraction
// Kinds: 0 glow, 1 fireball (volume), 2 streak (bolt, spark, missile), 3 ring, 4 flare, 5 scorch, 7 beam, 8 flipbook (a simulated explosion:
// one frame pair of an atlas in a texture array; p1 = frame (whole part and blend), p2 = array slice; t4 = smoke gain, heat gain, flip sign, opacity fade).
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
        TEXTURE3D(_MN_Noise3D);     // 64^3 tiling noise: r perlin-worley, g worley, b fine worley, a perlin
        SAMPLER(sampler_MN_Noise3D);
        TEXTURE2D_ARRAY(_MN_Flip);  // the simulated explosions: R burning gas (sqrt), G smoke, B and A smoke surface slope (see Flipbooks.cs)
        SAMPLER(sampler_MN_Flip);
        float4 _MN_SunDir;          // xyz: world direction toward the Sun; w: 1 when there is a Sun
        float4 _MN_SunCol;          // rgb: radiance of a white diffuse surface facing the Sun, in display units
        float4 _MN_Fill;            // rgb: the sky's fill on a white diffuse surface, in display units

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

        float Fbm2(float3 p)
        {
            return (0.6 * Noise3(p) + 0.4 * Noise3(p * 2.1 + 3.7));
        }

        float4 Tex3(float3 p)
        {
            return SAMPLE_TEXTURE3D_LOD(_MN_Noise3D, sampler_MN_Noise3D, p, 0);
        }

        // ------------------------------------------------------------------ colour

        // A cooling fireball: deep red, orange, yellow, then white at the top. T 0..1.6
        float3 Incandescent(float T)
        {
            return float3(saturate(T * 2.6), saturate((T - 0.30) * 1.55), saturate((T - 0.78) * 1.9));
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
            float4 s : TEXCOORD4;       // streaks: blur.  beams: total length, start fraction, end fraction, core fraction
            float4 ctr : TEXCOORD5;     // fireballs: centre (camera-relative) and radius
            float3 ray : TEXCOORD6;     // camera-relative world position of the fragment's quad point
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
            float dim = 1.0;
            float2 local = corner;
            float4 ctr = float4(centre, halfW);

            if ((kind > 1.5 && kind < 2.5) || (kind > 6.5 && kind < 7.5))
            {
                // Oriented quads (bolts, sparks, trails, beams): a cylinder billboard about the axis.
                float3 ax = a.t1.xyz;
                float len = max(a.t1.w, 1e-3);
                float3 side = cross(ax, centre / dist);
                float sl = length(side);
                side = sl > 1e-4 ? side / sl : _MN_CamRight.xyz;
                // A minimum width on screen, so a bolt far away is still a line and a beam still has presence. Beams carry their own (t4.w is unused for them: core fraction is t4.w).
                float minPx = _MN_Params.z * (kind > 6.5 ? 5.0 : 1.0);
                float minHalf = minPx * dist / _MN_Params.y;
                float hw = max(halfW, minHalf);
                // Sparks and specks (t4.y = 1): a streak thinner or shorter than a pixel puts only the light it really would on that pixel, so far ones fade out
                // instead of all turning into the same bright dot (the field of identical orange dots that reached the frame corners).
                dim = a.t4.y > 0.5 ? pow(saturate(halfW / minHalf) * saturate(max(a.t1.w, 1e-3) / hw), 0.85) : 1.0;
                len = max(len, hw);
                wpos = centre + ax * (corner.x * len) + side * (corner.y * hw);
                local = float2(corner.x * len / hw, corner.y);
                aspect = len / hw;
            }
            else
            {
                float hh = a.t1.x > 0.0 ? a.t1.x : halfW;
                float grow = 1.0;
                if (kind > 0.5 && kind < 1.5)
                {
                    // A fireball is a sphere: its quad must cover the sphere's silhouette, which is wider than the radius when close.
                    grow = min(rsqrt(max(1.0 - (halfW * halfW) / (dist * dist), 0.06)), 3.6) * 1.03;
                    hh = halfW;
                }
                float s, c;
                sincos(a.t1.w, s, c);
                float2 rc = float2(c * corner.x - s * corner.y, s * corner.x + c * corner.y);
                float3 bx = _MN_CamRight.xyz, by = _MN_CamUp.xyz;
                if ((kind > 0.5 && kind < 1.5) || kind > 7.5)
                {
                    // Face the ball, not the screen: off to the side of the view a ball's silhouette is an ellipse on a screen-aligned plane, and a square quad
                    // would cut it (square edges on a ball near the camera). On a plane across the line of sight it is a circle that grow covers exactly.
                    float3 vd = centre / dist;
                    bx = normalize(cross(_MN_CamUp.xyz, vd));
                    by = cross(vd, bx);
                }
                wpos = centre + bx * (rc.x * halfW * grow) + by * (rc.y * hh * grow);
                aspect = halfW / max(hh, 1e-3);
            }

            o.pos = mul(_MN_Matrix, float4(wpos, 1.0));
            o.col = a.col * dim;
            o.uv = float4(local, kind, a.t0.w);
            o.p = float4(a.t2.y, a.t2.z, a.t2.w, a.t3.w);
            o.q = float4(a.t3.x, a.t3.y, a.t3.z, dot(wpos, _MN_CamFwd.xyz));
            o.r = float4(aspect, a.t1.w, 0, 0);
            o.s = a.t4;
            o.ctr = ctr;
            o.ray = wpos;
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

        float NearFade(FxOut i)
        {
            return i.q.z > 0.0 ? saturate((i.q.w - i.q.y) / i.q.z) : 1.0;
        }

        // ------------------------------------------------------------------ the fireball: a raymarched, turbulent, glowing volume

        // Returns premultiplied colour (display units) and the opacity. col.rgb is the overall brightness, p1 the heat at birth (about 1 is
        // white-hot), p2 how fast it erodes into scattered embers, age 0..1 over its life, palette 0 fire / 1 plasma / above 1 a cool gas.
        float4 ShadeFire(FxOut i)
        {
            float age = i.p.x;
            float heat = i.p.y;
            float erodeRate = i.p.z;
            float palette = i.p.w;
            float seed = i.uv.w;
            float3 centre = i.ctr.xyz;
            float Rs = i.ctr.w;
            float3 rd = normalize(i.ray);

            float b = dot(rd, centre);
            float disc = b * b - (dot(centre, centre) - Rs * Rs);
            if (disc <= 0.0) return 0.0;
            float sq = sqrt(disc);
            float t0 = max(b - sq, 0.05);
            float t1 = b + sq;
            float sceneZ = LinearEyeDepth(LoadCameraDepth(uint2(i.pos.xy)), _ZBufferParams);
            t1 = min(t1, sceneZ / max(dot(rd, _MN_CamFwd.xyz), 1e-3));          // the hull in front of the ball hides the part behind it
            if (t1 <= t0) return 0.0;

            const int STEPS = 16;
            float dt = (t1 - t0) / STEPS;
            float jit = frac(sin(dot(i.pos.xy + frac(_MN_Params.w * 3.7) * 29.0, float2(12.9898, 78.233))) * 43758.5453);   // white noise: grain, not the hatching of a regular pattern
            float3 off = float3(seed * 7.13, seed * 3.71, seed * 11.3);
            float erode = age * (0.50 + erodeRate);
            float life = saturate(1.0 - pow(age, 1.45) * 0.88);       // cools slowly at first: most of the ball stays yellow-white
            // A blast has a life in two parts. It opens dense, opaque and blown out white (the first fifth of its life: it hides what it hits), then breaks up into
            // thin, smeared, cooling fire that goes paler and murkier as it cools (the rest).
            float isFire = 1.0 - saturate(palette);
            float open = (1.0 - smoothstep(0.05, 0.26, age)) * (1.0 - saturate(palette - 1.0));
            float cool = smoothstep(0.30, 0.95, age) * isFire;
            float blurMix = lerp(0.12, 0.55, smoothstep(0.08, 0.60, age)) * (1.0 - saturate(palette - 1.0));
            float extinction = lerp(3.4, 4.6, open);
            float3 sum = 0.0;
            float A = 0.0;

            [loop] for (int k = 0; k < STEPS; k++)
            {
                float t = t0 + (k + jit) * dt;
                float3 l = (rd * t - centre) / Rs;
                float r2 = dot(l, l);
                if (r2 >= 1.0) continue;
                float r = sqrt(r2);

                // Turbulence: a warp from a slow low-frequency lookup, then three octaves of Perlin-Worley in one fetch. Big lobes first, fine detail on them.
                // Motion blur: a blast flings its fire outward, so the detail is smeared along the radius (the noise is read along the direction from the
                // centre, so it varies round the ball and hardly at all along a ray), mixed in more as the ball ages and the streaks lengthen.
                float3 q = l * 0.55 + off + float3(0.12, -0.35, 0.08) * age;
                float3 warp = Tex3(q * 0.5).rgb - 0.5;
                float4 n = Tex3(q + warp * 0.9);
                float3 rh = l / max(r, 1e-3);
                float4 nr = Tex3(rh * (0.42 + 0.10 * r) + off * 0.6 + warp * 0.35);
                n = lerp(n, nr, blurMix * (1.0 - 0.75 * smoothstep(0.40, 0.90, r)));        // the smear is inside the ball: its silhouette is not combed into spikes
                float fbm = n.r * 0.55 + n.g * 0.30 + n.b * 0.15;
                float lobe = Tex3(l * 0.26 + off * 0.5 + float3(0.05, 0.10, 0.0) * age).r - 0.5;     // a few big billows: the silhouette is made of lobes, never a circle
                float base = 1.0 - r / 0.80;                          // a dense body, and lobes of thinning fire out to the edge of the sphere
                float mask = base * 1.05 + (fbm - 0.45) * lerp(1.9, 2.9, open) + lobe * lerp(1.7, 2.4, open) - erode * 1.6 + open * 0.30;
                float dens = pow(saturate(mask / lerp(0.85, 0.70, open)), lerp(1.6, 1.1, open));   // open: a dense mass; after: a wide feather, the fire thins out into the dark
                dens *= 1.0 - smoothstep(lerp(0.62, 0.40, open), 0.98, r + 0.50 * (0.45 - fbm) - 0.85 * lobe);
                dens *= 1.0 - smoothstep(0.78, 1.0, r);                                          // and nothing is left at the sphere's own boundary (a lobe must never be cut flat by it)          // the rim is lumpy: a different radius in every direction
                if (dens <= 0.0) continue;

                float hotspot = saturate(base) * saturate(base);
                // Churning layers: the temperature varies a lot from one billow to the next (white in the core, yellow, orange and red layers round it, dark
                // where the gas is thick and cold), so the dense opening is orange-red fire, not one beige mass.
                float churn = (n.g - 0.45) * 1.55 + (n.a - 0.5) * 1.25;
                float T = heat * 0.45 * life * saturate(0.26 + 0.95 * hotspot + churn + 0.20 * open) * lerp(0.55, 1.30, n.b);
                T = max(T, 0.06 * life);
                float3 ramp = lerp(Incandescent(T), PlasmaRamp(T), saturate(palette));
                ramp = lerp(ramp, float3(0.78, 0.88, 1.0) * (0.25 + T), saturate(palette - 1.0));
                float vein = pow(saturate(1.0 - abs(2.0 * n.g - 1.0)), 4.0);
                float lum = (0.10 + T * T * 1.25) * (0.70 + 0.70 * vein);
                lum = lerp(lum, 1.0 * (0.7 + 0.5 * vein), saturate(palette - 1.0));      // cool gas is lit, not black: a pale puff, even in front of a fireball
                // Paler and murkier as it cools: the colour drains toward a warm grey and the light dims; the hot core of the opening stays white.
                float grey = dot(ramp, float3(0.30, 0.59, 0.11));
                ramp = lerp(ramp, float3(grey, grey * 0.94, grey * 0.88), 0.55 * cool * isFire);
                lum *= lerp(1.0, 0.62, cool) * lerp(1.0, 1.12, open);
                // Smoke: pockets of thick cold gas that block the fire behind them (dark red-brown against the bright layers).
                float smoke = smoothstep(0.50, 0.80, n.a) * (1.0 - saturate(T * 1.4)) * isFire;
                lum *= 1.0 - 0.90 * smoke;
                dens *= 1.0 + 0.8 * smoke;
                lum *= 1.0 - 0.72 * smoothstep(0.45, 1.0, r);                                                // limb: the rim is cooler and dimmer, so its thin edge never clips to a bright hard line
                float3 emit = ramp * lum;
                dens *= lerp(smoothstep(0.10, 0.55, T), 1.0, saturate(palette - 1.0));                       // gas that has cooled thins out instead of hanging as a smooth red cloud
                float a = 1.0 - exp(-dens * extinction * dt / (2.0 * Rs));
                sum += (1.0 - A) * emit * a;
                A += (1.0 - A) * a;
                if (A > 0.985) break;
            }
            // A ball bigger than its quad is cut by the quad: fade it before the cut so no straight edge ever shows.
            // Only when the ball is so near that its silhouette is wider than its (clamped) quad; otherwise the ball ends inside the quad by itself and fading
            // from 0.8 of the quad would carve a square into a large ball.
            float2 qc = abs(i.uv.xy);
            float dC = max(length(centre), 0.01);
            float fit = rsqrt(max(1.0 - (Rs * Rs) / (dC * dC), 0.06));
            float silFrac = fit / (min(fit, 3.6) * 1.03);
            float quadFade = 1.0 - smoothstep(silFrac < 0.99 ? 0.985 : 0.80, 1.0, max(qc.x, qc.y));
            return float4(sum * i.col.rgb * quadFade, A * quadFade);
        }


        // ------------------------------------------------------------------ the flipbook: a simulated explosion, lit

        float4 FlipTap(float2 uv, float slice, float frame, float grid)
        {
            float f = floor(frame + 0.001);
            float row = floor(f / grid);
            float col = f - row * grid;
            float2 t = clamp(uv, 0.003, 0.997);
            float2 a = float2(col + t.x, (grid - 1.0 - row) + t.y) / grid;
            return SAMPLE_TEXTURE2D_ARRAY_LOD(_MN_Flip, sampler_MN_Flip, a, slice, 0);
        }

        // Returns premultiplied colour (display units) and opacity. The sprite is a simulation seen from one side: the fire is emission, the smoke is a
        // density whose slope gives it a surface the Sun, the sky's fill and the fire's own glow can light.
        float4 ShadeFlip(FxOut i)
        {
            const float GRID = 6.0;
            const float LAST = 35.0;
            float age = i.p.x;
            float frame = i.p.y;
            float slice = i.p.z;
            float smokeGain = i.s.x;
            float heat = i.s.y;
            float flipSign = i.s.z < 0.0 ? -1.0 : 1.0;
            float fade = i.s.w;
            float seed = i.uv.w;
            float2 c = i.uv.xy;
            float2 uv = float2(flipSign < 0.0 ? 0.5 - 0.5 * c.x : 0.5 + 0.5 * c.x, 0.5 + 0.5 * c.y);

            float f0 = min(floor(frame), LAST);
            float f1 = min(f0 + 1.0, LAST);
            float t = saturate(frame - f0);
            float4 s0 = FlipTap(uv, slice, f0, GRID);
            float4 s1 = FlipTap(uv, slice, f1, GRID);
            float fire = lerp(s0.r * s0.r, s1.r * s1.r, t);
            float smoke = lerp(s0.g, s1.g, t);
            float2 slope = lerp(s0.ba, s1.ba, t) - 0.5;
            slope.x *= flipSign;

            // Fine detail on top of the simulation's: churn that moves outward with the blast, so a big sprite is not smooth.
            float2 dp = c * 2.4 + float2(seed * 7.3, seed * 3.1);
            float dn = Tex3(float3(dp, age * 0.6 + seed * 5.0)).r;
            float dn2 = Tex3(float3(dp * 2.7 + 11.0, age * 0.9 + seed * 2.0)).b;
            float detail = 0.70 + 0.62 * dn + 0.35 * (dn2 - 0.5);
            fire *= saturate(detail);
            smoke *= 0.80 + 0.45 * dn2;

            float edge = 1.0 - smoothstep(0.86, 1.0, max(abs(c.x), abs(c.y)));

            // The fire: a temperature from the burning gas, then the same ramp as the raymarched fireball (white core, yellow, orange, red).
            // As it ages it goes darker and greyer, the murk of the cooling phase.
            float cool = smoothstep(0.25, 0.95, age);
            float T = fire * heat;
            float3 ramp = Incandescent(T);
            float lum = (0.10 + T * T * 1.25) * lerp(1.0, 0.65, cool);
            float grey = dot(ramp, float3(0.30, 0.59, 0.11));
            ramp = lerp(ramp, float3(grey, grey * 0.94, grey * 0.88), 0.45 * cool);
            float3 emit = ramp * lum * i.col.rgb;

            // The smoke: lit by the Sun and the sky's fill from its own surface slope, and by the fire inside it (the smoke round a flame glows orange).
            float S = smoke * smokeGain;
            float aS = 1.0 - exp(-S * 3.2);
            float3 N = normalize(float3(-slope.x * 26.0, -slope.y * 26.0, 1.0));
            float sn, cs;
            sincos(i.r.y, sn, cs);                                                      // the quad's own rotation (passed in r.y)
            N.xy = float2(cs * N.x - sn * N.y, sn * N.x + cs * N.y);
            float3 L = float3(dot(_MN_SunDir.xyz, _MN_CamRight.xyz), dot(_MN_SunDir.xyz, _MN_CamUp.xyz), -dot(_MN_SunDir.xyz, _MN_CamFwd.xyz));
            float lam = saturate((dot(N, L) + 0.35) / 1.35) * _MN_SunDir.w;
            float3 albedo = float3(0.26, 0.245, 0.23);
            float glow = saturate(fire * heat * 1.3 + 0.25 * fire);
            float3 litSmoke = albedo * (_MN_SunCol.rgb * lam + _MN_Fill.rgb + float3(1.0, 0.34, 0.10) * (glow * 2.2));

            float aF = 1.0 - exp(-fire * lerp(7.0, 2.5, smoothstep(0.05, 0.5, age)));
            float3 rgb = litSmoke * aS + emit * (1.0 - 0.55 * aS);
            float a = 1.0 - (1.0 - aS) * (1.0 - aF);
            float fadeIn = smoothstep(0.0, 0.10, age);                                  // no pop when a sprite appears (a smoke that begins mid-burn, one placed late)
            return float4(rgb, a) * (edge * fade * fadeIn);
        }

        // ------------------------------------------------------------------ the other kinds

        // Returns rgb (display units) for the additive pass, and a coverage / darkening alpha for the scorch.
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
                return 0;                       // fireballs are drawn by the alpha pass
            }
            else if (kind < 2.5)
            {
                // Streak: a bolt, a spark, an exhaust segment. c.x runs along it in half widths (tail at -aspect, head at +aspect), c.y across.
                // p1 = taper (0 = a capsule, 1 = a long point at the tail, 2 = sharper), p2 = tail brightness floor, s.x = blur (0 hard and saturated, 1 a soft smear).
                float cap = i.r.x;
                float t = saturate(c.x / (2.0 * cap) + 0.5);                  // 0 at the tail, 1 at the head
                float widthAt = lerp(1.0, pow(max(t, 1e-3), 0.55 * p1 + 1e-3), saturate(p1));
                float yn = c.y / max(widthAt, 0.05);
                float ex = max(c.x - (cap - 1.0), 0.0);                       // a round head
                float d2 = ex * ex + yn * yn;
                if (d2 >= 1.0) return 0;
                float d = sqrt(d2);
                float blur = i.s.x;
                float coreHard = 1.0 - smoothstep(0.10, 0.34, d);
                float bodyHard = 1.0 - smoothstep(0.38, 0.88, d);
                float core = lerp(coreHard, exp(-d2 * 16.0), blur);
                float body = lerp(bodyHard, exp(-d2 * 4.2), blur);
                float halo = (1.0 - d2) * exp(-d2 * 1.4);
                float tail = lerp(p2, 1.0, pow(t, 1.4));
                float3 rgb = colour * (halo * 0.40 + body * 1.30) + lerp(colour, float3(1, 1, 1), 0.86) * (core * i.col.a);
                return float4(rgb * tail * smoothstep(0.0, 0.04, t + (1.0 - saturate(p1))), 0);
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
                // Flare: a lens-like star. The quad is wide (anamorphic horizontal streak) and the glow is tight. p1 = vertical spike strength (default 0.28).
                float asp = max(i.r.x, 1.0);
                float spike = p1 > 0.0 ? p1 : 0.28;
                float horiz = exp(-abs(c.y) * 26.0) * pow(saturate(1.0 - abs(c.x)), 2.4);
                float vert = exp(-abs(c.x) * 30.0 * asp) * pow(saturate(1.0 - abs(c.y)), 3.0) * spike;
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
                // Beam (kind 7): one fat ribbon drawn in segments. c.x along (in half widths), c.y across (-1..1 over the whole width).
                // p1 = animation speed, p2 = noise scale, s = (total length, start, end, core fraction). A white-hot hard core inside a saturated body with a
                // ragged flaring edge, inside a soft glow. col.rgb = the body colour, col.a = the core's strength.
                float cap = i.r.x;
                float along = lerp(i.s.y, i.s.z, c.x * (0.5 / cap) + 0.5);
                float xm = along * i.s.x;
                // Round ends: the ribbon narrows to a point over about half its half width at each end, so a beam that stops at a hull has a rounded head.
                float segLen = max((i.s.z - i.s.y) * i.s.x, 1e-3);
                float Wm = segLen / (2.0 * cap);                                  // the ribbon's half width in metres
                float dEnd = (1.0 - along) * i.s.x / Wm, dStart = along * i.s.x / Wm;
                float capR = 0.7;
                float ce = dEnd < capR ? sqrt(saturate(1.0 - (1.0 - dEnd / capR) * (1.0 - dEnd / capR))) : 1.0;
                float cs = dStart < capR ? sqrt(saturate(1.0 - (1.0 - dStart / capR) * (1.0 - dStart / capR))) : 1.0;
                float y = abs(c.y) / max(min(ce, cs), 0.04);
                float coreF = max(i.s.w, 0.05);
                float bodyF = coreF * 2.5;
                float n1 = Tex3(float3(xm * 0.0065 * p2 - time * p1 * 0.02, c.y * 0.30, seed)).r;
                float n2 = Tex3(float3(xm * 0.0075 * p2 - time * p1 * 0.045, c.y * 0.45 + 0.37, seed + 0.31)).a;
                float n3 = Tex3(float3(xm * 0.017 * p2 - time * p1 * 0.09, c.y * 0.7, seed + 0.62)).a;
                float rag = (n1 - 0.5) * 0.30 + (pow(saturate(n2 * 1.35), 3.0) - 0.22) * 0.9 + (n3 - 0.5) * 0.10;      // flaring, ragged edges
                float coreEdge = coreF * (1.0 + 0.20 * rag);
                float bodyEdge = bodyF * (1.0 + 0.55 * rag);
                float core = 1.0 - smoothstep(coreEdge * 0.78, coreEdge * 1.10, y);
                float body = 1.0 - smoothstep(bodyEdge * 0.80, bodyEdge * 1.16, y);
                float shimmer = 0.80 + 0.45 * n2 + 0.25 * n3;
                float flame = pow(saturate(1.0 - y), 2.2) * (0.45 + 1.1 * n1);                  // the soft glow and the long flares beyond the body
                float streaks = pow(saturate(n3 * 1.6 - 0.35), 2.0) * exp(-pow(max(y - bodyEdge, 0.0) / (bodyF * 0.9), 2.0)) * (1.0 - body);
                float ends = 1.0;
                float3 whiteHot = lerp(colour, float3(1, 1, 1), 0.88);
                float3 rgb = colour * (body * shimmer * 1.0 + flame * 0.42 + streaks * 1.6) + whiteHot * (core * (0.85 + 0.3 * n2) * i.col.a);
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
                if (i.uv.z > 0.5 && i.uv.z < 1.5)
                {
                    float4 f = ShadeFire(i);
                    float nf = NearFade(i);
                    return float4(ToExposure(f.rgb * nf), saturate(f.a) * nf);
                }
                if (i.uv.z > 7.5 && i.uv.z < 8.5)
                {
                    float4 f = ShadeFlip(i);
                    float nf = NearFade(i);
                    float df = DepthFade(i);
                    return float4(ToExposure(f.rgb * nf * df), saturate(f.a) * nf * df);
                }
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
                float3 nOs = a.pos / _ShieldRadii.xyz;       // an ellipsoid's normal at unit-sphere point u is u / radii
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

        // ------------------------------------------------------------------ pass 4: the boost lens (Juice/Fx/BoostFx.cs)

        // Reads a copy of the picture (made by FxPass just before) and writes it back with a radial zoom blur and a red/blue fringe that grow
        // toward the rim, plus a faint glow there. Nothing inside the clear radius changes (the pixel is discarded, so the picture stays as it is),
        // nor a disc round the player's target. Blend Off: it replaces the pixel. The HUD draws after post-processing, so it is never touched.
        Pass
        {
            Name "BoostLens"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex VertLens
            #pragma fragment FragLens

            TEXTURE2D_X(_MN_SceneCopy);
            float4 _MN_BoostA;          // x zoom-blur span at the rim (fraction of the distance from the centre), y red/blue split at the rim (same unit)
            float4 _MN_BoostZone;       // x clear radius, y full-strength radius (fractions of the half diagonal)
            float4 _MN_BoostSafe;       // x, y: the target on screen (0..1, y down), z: its radius in screen heights (0 = none)
            float4 _MN_BoostGlow;       // rgb: the rim's glow in display units

            struct LensOut { float4 pos : SV_POSITION; };

            LensOut VertLens(uint id : SV_VertexID)
            {
                LensOut o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                return o;
            }

            float4 SceneAt(float2 px)
            {
                float2 uv = px * _MN_Screen.zw * _RTHandleScale.xy;
                return SAMPLE_TEXTURE2D_X_LOD(_MN_SceneCopy, s_linear_clamp_sampler, uv, 0);
            }

            float4 FragLens(LensOut i) : SV_Target
            {
                float2 px = i.pos.xy;                                   // pixel centre, y down
                float2 uv = px * _MN_Screen.zw;
                float aspect = _MN_Screen.x * _MN_Screen.w;
                float2 p = (uv - 0.5) * 2.0;
                p.x *= aspect;
                float r = length(p) / length(float2(aspect, 1.0));
                float m = smoothstep(_MN_BoostZone.x, _MN_BoostZone.y, r);
                if (_MN_BoostSafe.z > 0.0)
                {
                    float2 q = uv - _MN_BoostSafe.xy;
                    q.x *= aspect;
                    m *= smoothstep(0.8, 1.7, length(q) / _MN_BoostSafe.z);
                }
                if (m <= 0.003) discard;                                // leave the picture as it is

                float2 cen = 0.5 * _MN_Screen.xy;
                float2 d = px - cen;
                float blur = _MN_BoostA.x * m;
                float split = _MN_BoostA.y * m;
                float3 acc = 0.0;
                float wsum = 0.0;
                float alpha = 1.0;
                [unroll] for (int j = 0; j < 7; j++)
                {
                    float t = j / 6.0 - 0.5;                            // -0.5 .. 0.5 along the smear
                    float z = 1.0 + t * blur;
                    float wgt = 1.0 - 0.8 * abs(t);
                    float4 g = SceneAt(cen + d * z);
                    float cr = SceneAt(cen + d * (z + split)).r;
                    float cb = SceneAt(cen + d * (z - split)).b;
                    acc += wgt * float3(cr, g.g, cb);
                    wsum += wgt;
                    if (j == 3) alpha = g.a;
                }
                float3 rgb = acc / wsum;
                rgb += ToExposure(_MN_BoostGlow.rgb * (m * m));
                return float4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
