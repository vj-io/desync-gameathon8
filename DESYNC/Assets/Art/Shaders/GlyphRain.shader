// Glyph rain particles (mat.glyphrain). Each billboard is a falling streak of glyphs: bright head at the bottom,
// fading tail above. Needs the vertex streams GlyphRain.cs sets: Position, Color, UV, SizeXY, StableRandomX.
Shader "Desync/GlyphRain"
{
    Properties
    {
        [NoScaleOffset] _GlyphTex ("Glyph atlas (R)", 2D) = "black" {}
        _Color ("Colour", Color) = (0, 1, 0.4, 1)
        _HeadColor ("Head colour", Color) = (0.75, 1, 0.85, 1)
        _Intensity ("Intensity", Float) = 1.3
        _NearFade ("Near fade (start m, length m)", Vector) = (0.8, 2, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "DesyncGlyphRain"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DesyncCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _HeadColor;
                float _Intensity;
                float4 _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uvSize : TEXCOORD0;   // uv.xy, particle size.xy
                float4 random : TEXCOORD1;   // x = stable random
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uvSize : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 seedFog : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.color = v.color;
                o.uvSize = v.uvSize;
                o.seedFog = float2(v.random.x * 997.0, ComputeFogFactor(p.positionCS.z));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 size = max(i.uvSize.zw, 1e-3);
                float rows = max(1.0, floor(size.y / size.x + 0.5));
                float2 cuv = float2(i.uvSize.x, i.uvSize.y * rows);
                float row = floor(cuv.y);                        // 0 = bottom = leading head
                float seed = i.seedFog.x;
                float id = floor(Hash12(float2(seed, row + floor(_Time.y * (3.0 + row * 0.7) + seed))) * 64.0);
                float glyph = SampleGlyph(cuv, id, ddx(cuv), ddy(cuv));
                float k = row / rows;
                float head = 1.0 - step(0.5, row);
                float bright = pow(1.0 - k, 1.6) * 0.9 + head * 0.6;
                float near = saturate((distance(i.positionWS, _WorldSpaceCameraPos) - _NearFade.x) / max(_NearFade.y, 1e-3));
                float3 col = lerp(_Color.rgb, _HeadColor.rgb, head) * glyph * bright * i.color.a * near * _Intensity;
                col *= FogVisibility(i.seedFog.y);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
