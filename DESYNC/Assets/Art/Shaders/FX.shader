// Additive glow for effect meshes, lines and trails: vision cones, ICE trail, portal, LKP marker, glitch zone,
// Architect face, puppet strings, floor decal. All knobs are material floats (one SRP-batcher-compatible shader).
//  _MainTex      mask (R channel), white = no mask
//  _SourceColor  colour used in SOURCE, a = how much (reads global _Layer)
//  _FadeRange    > 0: fades out with distance from the object pivot (cone apex), metres
//  _Band         (frequency, speed, amount, 0): rings travelling out from the pivot
//  _Anim         (pulse speed, pulse amount, flicker, glitch slices)
//  _Shape        (rim, wireframe mask, vertex colour weight, 0); wireframe needs a MeshPrep'd mesh, else no-op
//  _ZTest        depth test (4 = LEqual, the default; 8 = Always draws the mesh through walls: the Sysadmin Key cone variants)
//  _CbHue        colour-blind palette hue of _BaseColor: 0 keep, 1 threat (danger colour), 2 SOURCE (yellow); global _Colorblind switches it on
Shader "Desync/FX"
{
    Properties
    {
        _MainTex ("Mask (R)", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (0, 0.941, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _SourceColor ("Source colour (a = weight)", Color) = (0, 1, 0.4, 0)
        _FadeRange ("Pivot fade range (m)", Float) = 0
        _Band ("Bands (freq, speed, amount, 0)", Vector) = (0, 0, 0, 0)
        _Anim ("Anim (pulse speed, pulse amount, flicker, glitch)", Vector) = (0, 0, 0, 0)
        _Shape ("Shape (rim, wire, vertex colour, 0)", Vector) = (0, 0, 1, 0)
        _Scroll ("UV scroll (xy)", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _CbHue ("Colour-blind hue (0 keep, 1 threat, 2 source)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "DesyncFX"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DesyncCommon.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
                float _Intensity;
                half4 _SourceColor;
                float _FadeRange;
                float4 _Band;
                float4 _Anim;
                float4 _Shape;
                float4 _Scroll;
                float _ZTest;
                float _CbHue;
            CBUFFER_END

            float _Layer;   // global, LayerState: 0 RENDER, 1 SOURCE

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 bary : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 bary : TEXCOORD2;
                half4 color : TEXCOORD3;
                float3 uvFog : TEXCOORD4;
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
                o.uvFog.xy = TRANSFORM_TEX(v.uv, _MainTex) + _Scroll.xy * _Time.y;
                o.uvFog.z = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 pivot = UNITY_MATRIX_M._m03_m13_m23;
                float d = distance(i.positionWS, pivot);

                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uvFog.xy).r;
                float fade = 1.0;
                if (_FadeRange > 0.0) { fade = saturate(1.0 - d / _FadeRange); fade *= fade; }
                float band = 1.0 + _Band.z * sin(d * _Band.x - t * _Band.y);
                float pulse = 1.0 + _Anim.y * sin(t * _Anim.x);
                float flicker = 1.0 - _Anim.z * step(0.75, Hash11(floor(t * 20.0) + pivot.x * 7.1 + pivot.z * 3.3));
                float slice = Hash12(float2(floor(i.positionWS.y * 9.0 + i.uvFog.y * 5.0), floor(t * 12.0)));
                float glitch = lerp(1.0, step(0.35, slice) * (0.4 + 1.2 * slice), _Anim.w);

                float3 n = i.normalWS * rsqrt(max(dot(i.normalWS, i.normalWS), 1e-6));
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim = pow(1.0 - saturate(abs(dot(n, v))), 2.0);
                float shape = lerp(1.0, 0.25 + 1.75 * rim, _Shape.x);

                float3 raw, clean;
                float valid;
                DecodeBary(i.bary, raw, clean, valid);
                float px = EdgePixels(clean);
                float wire = lerp(1.0, EdgeCore(px, 1.2) + EdgeHalo(px, 6.0) * 0.5, valid);
                shape *= lerp(1.0, wire, _Shape.y);

                half4 vc = lerp(half4(1, 1, 1, 1), i.color, _Shape.z);
                float3 baseCol = lerp(_BaseColor.rgb, _CbHue > 1.5 ? _CbSource.rgb : _CbDanger.rgb, _Colorblind * step(0.5, _CbHue));
                float3 srcCol = lerp(_SourceColor.rgb, _CbSource.rgb, _Colorblind);
                float3 tint = lerp(baseCol, srcCol, _Layer * _SourceColor.a);
                float3 col = tint * vc.rgb * (vc.a * mask * fade * band * pulse * flicker * glitch * shape * _Intensity);
                col *= FogVisibility(i.uvFog.z);   // additive: fog fades to black
                return half4(max(col, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
