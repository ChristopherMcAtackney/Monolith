// Screen-space-width lines and points drawn by Monolith's custom passes (View: stars and space dust; Hud: the HUD).
// Every pass takes its own clip matrix in _MN_Matrix, so nothing depends on HDRP's camera-relative globals:
//   pass 0 Stars: POSITION = unit direction, TEXCOORD0 = (corner offset px .xy, falloff uv .zw). Drawn ON the far plane,
//                 so anything already drawn hides them. Additive.
//   pass 1 Dust:  POSITION = this end (camera-relative metres), TEXCOORD0 = (other end .xyz, side -1/+1 .w),
//                 TEXCOORD1.x = -1 at the head, +1 at the tail. Depth-tested against the scene. Additive.
//   pass 2 Hud:   POSITION = pixels from the top-left, TEXCOORD0.x = -1..1 across the line (soft edge). Alpha-blended, on top.
//   pass 3 HudShape: the HUD's line graphics. POSITION = pixels from the top-left (xy), TEXCOORD0 = (u, halfWidth, v, length): u is the signed pixels across
//                 the shape from its centre line, v the pixels along it from its start. The fragment measures its distance outside that box in pixels,
//                 so every edge is anti-aliased to a pixel, the glow (_MN_Glow) and the dark keyline (_MN_Key) come from the same distance.
//                 Premultiplied alpha, on top.
//   pass 4 HudText: the HUD's type. TEXCOORD0.xy = the glyph's cell in the signed-distance atlas _MN_FontTex (128 = the edge, spread _MN_FontP.z texels either side),
//                 so the edge is a pixel wide at any size; the same glow and keyline. Premultiplied alpha, on top.
Shader "Hidden/Monolith/Lines"
{
    Properties
    {
        _MN_FontTex ("HUD font atlas (signed distance)", 2D) = "black" {}
    }
    SubShader
    {
        HLSLINCLUDE
        #pragma target 4.5
        float4x4 _MN_Matrix;
        float4 _MN_Screen;      // width, height, 1/width, 1/height (pixels)
        float _MN_HalfWidth;    // dust: half the streak width in pixels
        float _MN_Dim;          // hud: 0 (unset) draws as is; 1 fades the line graphics out (the cabin collapses the HUD into its own displays)
        float4 _MN_Glow;        // hud passes 3, 4: glow radius px, glow strength (alpha at the edge, already raised to the 2.2), padding px (where the glow is gone), 1.4427 / radius
        float4 _MN_Key;         // hud passes 3, 4: dark keyline width px, keyline alpha, text boldness px (0 = the font's own weight), -
        float4 _MN_FontP;       // hud pass 4: atlas width, atlas height (texels), spread (texels), -
        Texture2D _MN_FontTex;
        SamplerState sampler_MN_FontTex;

        // The alpha a HUD colour is authored with: for a lit colour it is how bright it should look (the target is sRGB and blends in linear light, where a linear 0.2 reads as half
        // as bright), so it is raised to the 2.2; a dark colour (a plate, a shadow) keeps its alpha as the share of the picture it hides.
        float HudAlpha(float4 c)
        {
            float lum = max(c.r, max(c.g, c.b));
            return lerp(c.a, pow(saturate(c.a), 2.2), smoothstep(0.02, 0.08, lum));
        }

        #if defined(SHADER_API_D3D11) || defined(SHADER_API_D3D12) || defined(SHADER_API_VULKAN) || defined(SHADER_API_METAL) || defined(SHADER_API_SWITCH) || defined(SHADER_API_PSSL) || defined(SHADER_API_XBOXONE) || defined(SHADER_API_GAMECORE)
            #define MN_REVERSED_Z 1
        #endif
        ENDHLSL

        Pass
        {
            Name "Stars"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float3 dir : POSITION; float4 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };

            V vert(A i)
            {
                V o;
                float4 c = mul(_MN_Matrix, float4(i.dir, 0.0));
                c.xy += i.uv.xy * 2.0 * _MN_Screen.zw * c.w;
                #ifdef MN_REVERSED_Z
                    c.z = 0.0;
                #else
                    c.z = c.w;
                #endif
                o.pos = c;
                o.uv = i.uv.zw;
                o.col = i.col;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float a = saturate(1.0 - dot(i.uv, i.uv));
                return float4(i.col.rgb * (a * a), 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Dust"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float3 pos : POSITION; float4 other : TEXCOORD0; float4 along : TEXCOORD1; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float side : TEXCOORD0; float4 col : COLOR; };

            V vert(A i)
            {
                V o;
                float4 a = mul(_MN_Matrix, float4(i.pos, 1.0));
                float4 b = mul(_MN_Matrix, float4(i.other.xyz, 1.0));
                float2 pa = a.xy / a.w * _MN_Screen.xy;
                float2 pb = b.xy / b.w * _MN_Screen.xy;
                float2 d = pb - pa;
                float len = length(d);
                // The same head-to-tail direction at both ends, so the quad never twists (a still speck is a small square).
                float2 dir = len > 1e-3 ? d / len * (i.along.x > 0.0 ? -1.0 : 1.0) : float2(1.0, 0.0);
                float2 n = float2(-dir.y, dir.x);
                float2 px = (n * i.other.w + dir * i.along.x) * _MN_HalfWidth;   // out sideways, and past this end by a half width
                a.xy += px * 2.0 * _MN_Screen.zw * a.w;
                o.pos = a;
                o.side = i.other.w;
                o.col = i.col;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float a = saturate(1.0 - i.side * i.side);
                return float4(i.col.rgb * i.col.a * a, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Hud"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float3 pos : POSITION; float4 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float edge : TEXCOORD0; float4 col : COLOR; };

            V vert(A i)
            {
                V o;
                o.pos = mul(_MN_Matrix, float4(i.pos.xy, 0.0, 1.0));
                o.edge = i.uv.x;
                o.col = i.col;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float a = 1.0 - smoothstep(0.55, 1.0, abs(i.edge));
                return float4(i.col.rgb, i.col.a * a * (1.0 - _MN_Dim));
            }
            ENDHLSL
        }
        Pass
        {
            Name "HudShape"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float2 pos : POSITION; float4 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float4 col : COLOR; };

            V vert(A i)
            {
                V o;
                o.pos = mul(_MN_Matrix, float4(i.pos, 0.0, 1.0));
                o.uv = i.uv;
                o.col = float4(i.col.rgb, HudAlpha(i.col));
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float dout;                                         // signed pixels outside the shape (<= 0 inside)
                if (i.uv.w > -0.5)
                {
                    // a box: uv = (across from the centre line, half width, along from the start, length)
                    float du = abs(i.uv.x) - i.uv.y;
                    float dv = max(-i.uv.z, i.uv.z - i.uv.w);
                    float2 q = float2(du, dv);
                    dout = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
                }
                else if (i.uv.w > -1.5)
                {
                    // a triangle: uv.xyz = distances inside each edge
                    dout = -min(min(i.uv.x, i.uv.y), i.uv.z);
                }
                else
                {
                    // a ring: uv.xy = offset from the centre, uv.z = radius, uv.w = -(2 + half width); a disc has radius 0
                    dout = abs(length(i.uv.xy) - i.uv.z) - (-i.uv.w - 2.0);
                }
                float a = i.col.a * (1.0 - _MN_Dim);
                float core = saturate(0.5 - dout) * a;
                float out_ = max(dout, 0.0);
                float glow = _MN_Glow.y * exp2(-out_ * _MN_Glow.w) * saturate((_MN_Glow.z - out_) * 0.4) * a;
                float key = saturate(0.5 - (dout - _MN_Key.x)) * _MN_Key.y * a;
                float a2 = key + glow * (1.0 - key);
                float3 c2 = i.col.rgb * glow * (1.0 - key);          // the keyline is black
                float3 c3 = i.col.rgb * core + c2 * (1.0 - core);
                return float4(c3, core + a2 * (1.0 - core));
            }
            ENDHLSL
        }

        Pass
        {
            Name "HudText"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct A { float2 pos : POSITION; float4 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };

            V vert(A i)
            {
                V o;
                o.pos = mul(_MN_Matrix, float4(i.pos, 0.0, 1.0));
                o.uv = i.uv.xy;
                o.col = float4(i.col.rgb, HudAlpha(i.col));
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float d = _MN_FontTex.Sample(sampler_MN_FontTex, i.uv).r;
                float2 dx = ddx(i.uv) * _MN_FontP.xy;
                float2 dy = ddy(i.uv) * _MN_FontP.xy;
                float texelsPerPx = max(0.0001, max(length(dx), length(dy)));
                float sd = (d - 0.5) * 2.0 * _MN_FontP.z / texelsPerPx;        // pixels: positive inside the letter
                float spreadPx = _MN_FontP.z / texelsPerPx;
                float a = i.col.a * (1.0 - _MN_Dim);
                float core = saturate(sd + 0.5 + _MN_Key.z) * a;
                float out_ = max(-sd, 0.0);
                float fade = saturate((spreadPx - out_) / max(spreadPx * 0.6, 0.01));    // nothing is stored past the spread
                float glow = _MN_Glow.y * exp2(-out_ * _MN_Glow.w) * fade * a;
                float key = saturate(sd + 0.5 + _MN_Key.x + _MN_Key.z) * _MN_Key.y * fade * a;
                float a2 = key + glow * (1.0 - key);
                float3 c2 = i.col.rgb * glow * (1.0 - key);
                float3 c3 = i.col.rgb * core + c2 * (1.0 - core);
                return float4(c3, core + a2 * (1.0 - core));
            }
            ENDHLSL
        }
    }
}
