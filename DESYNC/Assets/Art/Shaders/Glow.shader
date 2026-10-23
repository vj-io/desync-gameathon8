// Opaque emissive body (mat.drone, mat.hijacked, mat.ice): base colour x vertex colour, fresnel rim, glowing
// barycentric edges when the mesh went through MeshPrep, optional pulse/flicker. Ignores _SOURCE (same look in both layers).
Shader "Desync/Glow"
{
    Properties
    {
        _BaseColor ("Base colour", Color) = (0.15, 0.15, 0.22, 1)
        _VertexColorWeight ("Vertex colour weight", Range(0, 1)) = 1
        _RimColor ("Rim colour (a = intensity)", Color) = (1, 0.169, 0.839, 1)
        _RimPower ("Rim power", Float) = 2.5
        _EdgeColor ("Edge colour", Color) = (1, 0.169, 0.839, 1)
        _EdgeGlow ("Edge (intensity, core px, halo px, halo)", Vector) = (1.5, 1.1, 6, 0.4)
        _Anim ("Anim (pulse speed, pulse amount, flicker, 0)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DesyncGlow"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DesyncCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _VertexColorWeight;
                half4 _RimColor;
                float _RimPower;
                half4 _EdgeColor;
                float4 _EdgeGlow;
                float4 _Anim;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 bary : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 bary : TEXCOORD2;
                half4 color : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.bary = v.bary;
                o.color = v.color;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                o.color.rgb = SRGBToLinear(v.color.rgb);
                #endif
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = i.normalWS * rsqrt(max(dot(i.normalWS, i.normalWS), 1e-6));
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                float hl = dot(n, _MainLightPosition.xyz) * 0.5 + 0.5;

                float3 raw, clean;
                float valid;
                DecodeBary(i.bary, raw, clean, valid);
                float px = EdgePixels(clean);
                float edge = (EdgeCore(px, _EdgeGlow.y) + EdgeHalo(px, _EdgeGlow.z) * _EdgeGlow.w) * valid;

                float3 pivot = UNITY_MATRIX_M._m03_m13_m23;
                float t = _Time.y;
                float pulse = 1.0 + _Anim.y * sin(t * _Anim.x);
                float flicker = 1.0 - _Anim.z * step(0.8, Hash11(floor(t * 18.0) + pivot.x * 13.7 + pivot.z * 3.1));

                half3 vc = lerp(half3(1, 1, 1), i.color.rgb, _VertexColorWeight);
                float3 col = _BaseColor.rgb * vc * lerp(0.6, 1.0, hl)
                           + _RimColor.rgb * rim * _RimColor.a
                           + _EdgeColor.rgb * edge * _EdgeGlow.x;
                col *= pulse * flicker;
                return half4(MixFog(col, i.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
