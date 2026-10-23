// Emissive quads: HALCYON posters (mat.poster, _Mode 0, _MainTex = poster) and terminal screen backing (mat.screen, _Mode 1).
// RENDER: poster art / dim scrolling code lines with a glowing border. SOURCE: corrupt code (green glyph grid with red
// glitch cells; the poster's artwork survives as glyph brightness). Uses quad UVs 0..1.
// Blend state is switched with the world materials by PostFXController (additive in SOURCE).
Shader "Desync/Panel"
{
    Properties
    {
        _MainTex ("Poster (RGB)", 2D) = "black" {}
        [NoScaleOffset] _GlyphTex ("Glyph atlas (R)", 2D) = "black" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Mode ("Mode (0 poster, 1 screen)", Float) = 0
        _Glow ("Glow", Float) = 1
        _CodeGrid ("Code grid (columns, rows)", Vector) = (20, 30, 0, 0)
        _SourceColor ("Source colour", Color) = (0, 1, 0.4, 1)
        _DangerColor ("Corrupt colour", Color) = (1, 0.15, 0.2, 1)
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _Cull ("__cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DesyncPanel"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma dynamic_branch _ _SOURCE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DesyncCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                float _Mode;
                float _Glow;
                float4 _CodeGrid;
                half4 _SourceColor;
                half4 _DangerColor;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 seedFog : TEXCOORD1;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                float3 pivot = UNITY_MATRIX_M._m03_m13_m23;
                o.seedFog.xy = frac(pivot.xz * 0.137) * 97.0;   // per-panel variety
                o.seedFog.z = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 uv = i.uv;
                float2 seed = i.seedFog.xy;
                float3 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb;
                float2 e = min(uv, 1.0 - uv);
                float border = 1.0 - smoothstep(0.0, 0.03, min(e.x, e.y));

            if (_SOURCE)   // dynamic branch: one program for both layers, nothing new to compile/link on a flip
            {
                float2 cellUV = uv * _CodeGrid.xy;
                cellUV.y += t * 2.0;
                float2 cell = floor(cellUV);
                float id = floor(Hash12(cell + seed + floor(t * (1.0 + 4.0 * Hash12(cell + 3.7)))) * 64.0);
                float glyph = SampleGlyph(cellUV, id, ddx(cellUV), ddy(cellUV));
                float lum = dot(art, float3(0.3, 0.59, 0.11));
                float bright = lerp(0.35, 0.2 + 1.6 * lum, step(_Mode, 0.5));
                float corrupt = step(0.94, Hash12(cell + seed + floor(t * 3.0)));
                float3 col = lerp(_SourceColor.rgb, _DangerColor.rgb, corrupt) * glyph * bright * _Glow
                           + _SourceColor.rgb * border * 0.6;
                col *= FogVisibility(i.seedFog.z);
                return half4(col, 1.0);
            }
            else
            {
                float scan = 0.88 + 0.12 * sin(uv.y * 360.0 - t * 4.0);
                float sweepPos = frac(t * 0.15 + seed.x) * 1.6 - 0.3;   // diagonal sheen crossing the poster
                float sweep = (1.0 - smoothstep(0.0, 0.06, abs((uv.x + uv.y) * 0.5 - sweepPos))) * 0.25;
                float3 poster = art * _BaseColor.rgb * _Glow * scan + sweep * _BaseColor.rgb;

                // Terminal backing: dim code lines printing upward, glowing frame.
                float2 cellUV = uv * _CodeGrid.xy * float2(1.0, 0.6);
                cellUV.y -= floor(t * 2.0 + seed.y);
                float2 cell = floor(cellUV);
                float lineLen = Hash12(float2(cell.y, seed.x)) * _CodeGrid.x;
                float lit = step(cell.x, lineLen) * step(0.25, Hash11(cell.y + seed.y));
                float id = floor(Hash12(cell + seed) * 64.0);
                float glyph = SampleGlyph(cellUV, id, ddx(cellUV), ddy(cellUV)) * lit;
                float flick = 0.94 + 0.06 * Hash11(floor(t * 30.0) + seed.x);
                float3 screen = _BaseColor.rgb * (0.08 + glyph * 0.22 + border * 0.9) * scan * flick * _Glow;

                float3 col = lerp(poster, screen, saturate(_Mode));
                return half4(MixFog(col, i.seedFog.z), 1.0);
            }
            }
            ENDHLSL
        }
    }
    FallBack Off
}
