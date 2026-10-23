// WorldMat: every room surface and prop (mat.world) and floors (mat.floor, grid on).
// RENDER: flat vertex colour + half-lambert + spot pools, glowing cyan->magenta outline edges (quad diagonals suppressed), navy fog.
// SOURCE (global keyword _SOURCE): green full wireframe + world-space glyph columns scrolling down, faint fill.
// SOURCE transparency is a blend-state change the keyword cannot make: PostFXController.SetWorldBlend switches
// _SrcBlend/_DstBlend/_ZWrite/_Cull/renderQueue on the shared materials (additive, no depth write, both faces) on LayerState.Changed.
Shader "Desync/World"
{
    Properties
    {
        [NoScaleOffset] _GlyphTex ("Glyph atlas (R)", 2D) = "black" {}
        _EdgeColorLow ("Edge colour (low)", Color) = (0, 0.941, 1, 1)
        _EdgeColorHigh ("Edge colour (high)", Color) = (1, 0.169, 0.839, 1)
        _EdgeGradient ("Edge gradient (y start, height, 0, 0)", Vector) = (0.2, 3, 0, 0)
        _EdgeWidth ("Edge core width (px)", Float) = 1.3
        _EdgeGlow ("Edge glow (core, halo px, halo, 0)", Vector) = (1.6, 7, 0.45, 0)
        _Shade ("Shade floor", Range(0, 1)) = 0.6
        _GridColor ("Grid colour", Color) = (0, 0.941, 1, 1)
        _Grid ("Grid (spacing m, major every, intensity, 0)", Vector) = (1, 4, 0, 0)
        _SourceColor ("Source colour", Color) = (0, 1, 0.4, 1)
        _SourceMix ("Source (fill, wire, glyphs, diagonals)", Vector) = (0.035, 0.85, 0.45, 0.3)
        _GlyphScale ("Glyph cell size (m)", Float) = 0.3
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
            Name "DesyncWorld"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _SOURCE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "DesyncCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColorLow;
                half4 _EdgeColorHigh;
                float4 _EdgeGradient;
                float _EdgeWidth;
                float4 _EdgeGlow;
                half _Shade;
                half4 _GridColor;
                float4 _Grid;
                half4 _SourceColor;
                float4 _SourceMix;
                float _GlyphScale;
                float _SrcBlend;
                float _DstBlend;
                float _ZWrite;
                float _Cull;
            CBUFFER_END

            float4 _FlipWave;   // global (PostFXController): xyz = flip origin, w = seconds since the flip

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
                o.color.rgb = SRGBToLinear(v.color.rgb);   // vertex colours are authored like UI colours (sRGB)
                #endif
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float FlipWave(float3 positionWS)
            {
                float d = distance(positionWS, _FlipWave.xyz);
                float r = _FlipWave.w * 22.0;
                return saturate(1.0 - abs(d - r) * 0.6) * saturate(1.0 - _FlipWave.w * 0.8) * step(0.0001, _FlipWave.w);   // w = 0: unset (edit mode)
            }

            float GridLines(float2 p, float spacing)
            {
                float2 g = p / spacing;
                float2 d = abs(frac(g - 0.5) - 0.5) / max(fwidth(g), 1e-5);
                return 1.0 - saturate(min(d.x, d.y) - 0.25);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 raw, clean;
                float valid;
                DecodeBary(i.bary, raw, clean, valid);
                float vis = FogVisibility(i.fogFactor);
                float wave = FlipWave(i.positionWS);
                float grid = (GridLines(i.positionWS.xz, _Grid.x) * 0.45 + GridLines(i.positionWS.xz, _Grid.x * _Grid.y)) * _Grid.z;

            #if defined(_SOURCE)
                float outline = EdgeCore(EdgePixels(clean), _EdgeWidth) * valid;
                float diag = EdgeCore(EdgePixels(raw), _EdgeWidth * 0.8) * valid;
                float wire = max(outline, diag * _SourceMix.w);
                float halo = EdgeHalo(EdgePixels(clean), _EdgeGlow.y) * valid;

                // Glyph columns on the dominant world plane (1 texture read).
                float3 an = abs(n);
                float2 p;
                float axis;
                if (an.y > an.x && an.y > an.z) { p = i.positionWS.xz; axis = 0.0; }
                else if (an.x > an.z) { p = i.positionWS.zy; axis = 1.0; }
                else { p = i.positionWS.xy; axis = 2.0; }
                float t = _Time.y;
                float2 cellUV = p / _GlyphScale;
                cellUV.y += axis > 0.5 ? t * 0.5 / _GlyphScale : 0.0;   // walls: glyphs scroll down at 0.5 m/s
                float2 cell = floor(cellUV);
                float colH = Hash12(float2(cell.x, axis * 31.0));
                float speed = 1.5 + 3.0 * colH;                         // bright heads run down each column
                float trail = 1.0 - frac(cell.y / 14.0 + t * speed / 14.0 + colH * 5.3);
                float bright = trail * trail * trail;
                float id = floor(Hash12(cell + floor(t * (0.5 + 2.0 * Hash12(cell + 7.1)))) * 64.0);
                float glyph = SampleGlyph(cellUV, id, ddx(cellUV), ddy(cellUV));
                float lum = dot(i.color.rgb, float3(0.3, 0.59, 0.11));

                // Additive in SOURCE: fog fades to black; lines carry further into the fog than fill and glyphs.
                float3 col = _SourceColor.rgb * (
                      (_SourceMix.x * (0.6 + 2.0 * lum) + glyph * (0.05 + bright) * _SourceMix.z) * vis
                    + (wire * _SourceMix.y + halo * 0.1 + grid * 0.3) * sqrt(vis)
                    + wave * (0.25 + wire + 0.5 * glyph));
                return half4(col, 1.0);
            #else
                // Flat-shaded vertex colour (main light direction only, so the look survives any light setup).
                float hl = dot(n, _MainLightPosition.xyz) * 0.5 + 0.5;
                half3 vc = i.color.rgb;
                float3 base = vc * lerp(_Shade, 1.0, hl);

                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    float3 lit = 0;
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS);
                        lit += l.color * (l.distanceAttenuation * saturate(dot(n, l.direction)));
                    LIGHT_LOOP_END
                    base += lit * (vc * 2.0 + 0.3);   // spot pools read on dark surfaces
                }
                #endif

                float px = EdgePixels(clean);
                float core = EdgeCore(px, _EdgeWidth) * valid;
                float halo = EdgeHalo(px, _EdgeGlow.y) * valid;
                float h = saturate((i.positionWS.y - _EdgeGradient.x) / max(_EdgeGradient.y, 1e-3));
                float3 edgeCol = lerp(_EdgeColorLow.rgb, _EdgeColorHigh.rgb, h);
                float3 glow = edgeCol * (core * _EdgeGlow.x + halo * _EdgeGlow.z) * (1.0 + 2.0 * wave)
                            + _GridColor.rgb * grid * (1.0 + 2.0 * wave);

                float3 col = MixFog(base, i.fogFactor) + glow * sqrt(vis);
                return half4(col, 1.0);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
