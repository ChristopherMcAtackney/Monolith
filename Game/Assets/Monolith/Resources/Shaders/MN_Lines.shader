// Screen-space-width lines and points drawn by Monolith's custom passes (View: stars and space dust; Hud: the HUD).
// Every pass takes its own clip matrix in _MN_Matrix, so nothing depends on HDRP's camera-relative globals:
//   pass 0 Stars: POSITION = unit direction, TEXCOORD0 = (corner offset px .xy, falloff uv .zw). Drawn ON the far plane,
//                 so anything already drawn hides them. Additive.
//   pass 1 Dust:  POSITION = this end (camera-relative metres), TEXCOORD0 = (other end .xyz, side -1/+1 .w),
//                 TEXCOORD1.x = -1 at the head, +1 at the tail. Depth-tested against the scene. Additive.
//   pass 2 Hud:   POSITION = pixels from the top-left, TEXCOORD0.x = -1..1 across the line (soft edge). Alpha-blended, on top.
Shader "Hidden/Monolith/Lines"
{
    SubShader
    {
        HLSLINCLUDE
        #pragma target 4.5
        float4x4 _MN_Matrix;
        float4 _MN_Screen;      // width, height, 1/width, 1/height (pixels)
        float _MN_HalfWidth;    // dust: half the streak width in pixels
        float _MN_Dim;          // hud: 0 (unset) draws as is; 1 fades the line graphics out (the cabin collapses the HUD into its own displays)

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
    }
}
