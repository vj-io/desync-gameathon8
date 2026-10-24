// Fullscreen post-FX for the URP Full Screen Pass renderer feature (3 texture reads: the RGB split).
// Globals written by PostFXController every frame:
//   _Pulse 0..1 (flip pulse), _Melt 0..1, _Trace 0..1 (pulsing), _Reboot 0..1 (REBOOT wipe), _Wipe 0..1 (layer-switch
//   band, <0 = off), _Drip 0..1 (melt death), _FlashScale (1, low with Settings.ReduceFlashes, 0 with ReduceFx), _Layer (LayerState),
//   _FxReduce 0/1 (REDUCE FX: chromatic + glitch slices x0.3), _Desat 0..0.6 (death recap), _DesatFocus (killer viewport xy, radius, on).
Shader "Desync/PostFX"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "DesyncPostFX"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "DesyncCommon.hlsl"
            #include "BossDesync.hlsl"   // ARCHITECT P3 split (bosses workstream)

            float _Pulse, _Melt, _Trace, _Reboot, _Wipe, _Drip, _FlashScale, _Layer, _FxReduce, _Desat;
            float4 _DesatFocus;

            float3 Fetch(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screen = input.texcoord;
                float2 uv = screen;
                float t = _Time.y;
                float pulse = saturate(_Pulse);
                float fxK = lerp(1.0, 0.3, saturate(_FxReduce));
                float melt = saturate(_Melt);

                // Melt warp: the image sags (uv.y -= melt * 0.05 * noise).
                uv.y -= melt * 0.05 * ValueNoise(uv.x * 9.0 + t * 0.7);

                // Melt death: columns drip down.
                float dripCol = Hash11(floor(uv.x * 48.0));
                uv.y += _Drip * _Drip * (0.15 + 0.85 * dripCol) * 0.9;
                float dripMask = step(uv.y, 1.0);

                // Glitch slices: hash(floor(uv.y*24) + floor(t*20)) > 1 - 0.6*pulse.
                float h = Hash11(floor(uv.y * 24.0) + floor(t * 20.0) * 7.13);
                float gate = step(1.0 - 0.6 * pulse, h);
                uv.x += gate * (Hash11(h * 91.7) - 0.5) * 0.14 * pulse * fxK;

                // Layer-switch wipe: a torn band sweeping top -> bottom.
                float wipeOn = step(0.001, _Wipe) * step(_Wipe, 1.0);   // <= 0 = off
                float band = wipeOn * saturate(1.0 - abs(screen.y - (1.0 - _Wipe)) / 0.07);
                uv.x += band * (Hash11(floor(screen.y * 90.0) + floor(t * 60.0)) - 0.5) * 0.08;

                // REBOOT tear.
                uv.x += _Reboot * (Hash11(floor(screen.y * 70.0) + floor(t * 50.0)) - 0.5) * 0.3;

                // Chromatic aberration: a resting fringe towards the edges plus the flip pulse, capped at 0.004 UV
                // ("High, not Extreme": 0.004 UV peak on a flip); the REBOOT tear adds on top. REDUCE FX x0.3.
                float2 c = screen - 0.5;
                float edgeK = length(c) * 1.6;   // 0 at the centre (crosshair stays crisp), ~1.2 in the corners
                float ca = min(0.002 * edgeK + 0.004 * pulse, 0.004) + 0.012 * _Reboot * (0.5 + edgeK);
                float2 off = float2(ca * fxK, 0.0);
                float3 col = float3(Fetch(uv + off).r, Fetch(uv).g, Fetch(uv - off).b);
                col = BossDesync(col, screen, t, fxK, _Layer);

                // Scanlines + slow rolling bright band.
                float sl = sin(screen.y * _ScreenParams.y * 1.5707963);
                col *= 1.0 - 0.08 * sl * sl;
                col *= 1.0 + 0.04 * saturate(1.0 - abs(frac(t * 0.12) - screen.y) * 12.0);

                // Vignette + MELT RING (fills in from the edges, red heartbeat near 100).
                float2 q = c * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float dist = length(q);
                float angle = atan2(q.y, q.x);
                col *= 1.0 - 0.35 * smoothstep(0.45, 1.1, dist);
                float ringStart = lerp(1.15, 0.42, melt) + (ValueNoise(angle * 5.0 + t * 1.3) - 0.5) * 0.08 * melt;
                float meltOn = smoothstep(0.02, 0.12, melt);
                float ring = smoothstep(ringStart, ringStart + 0.22, dist) * meltOn;
                float rim = saturate(1.0 - abs(dist - ringStart - 0.02) / 0.025) * meltOn;   // glowing inner boundary
                float danger = smoothstep(0.75, 1.0, melt);
                float beat = danger * (0.5 + 0.5 * sin(t * 9.0)) * _FlashScale;
                float3 ringCol = lerp(float3(1.0, 0.17, 0.84), float3(1.0, 0.08, 0.1), danger);
                col = lerp(col, col * 0.25 + ringCol * 0.1, ring * (0.8 + 0.15 * beat));
                col += ringCol * rim * (0.55 + 0.6 * beat);

                // TRACE: red edge tint.
                col = lerp(col, col * 0.4 + float3(0.9, 0.05, 0.08) * 0.45, saturate(_Trace) * smoothstep(0.35, 1.0, dist) * 0.7);

                // Flip flashes in the new layer's colour.
                float3 layerCol = lerp(float3(0.0, 0.94, 1.0), float3(0.0, 1.0, 0.4), _Layer);
                col += (band * 0.5 + gate * pulse * 0.12) * layerCol * _FlashScale;

                // Death recap: desaturate, except a soft spot around the killer.
                float2 fq = (screen - _DesatFocus.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float keep = _DesatFocus.w * (1.0 - smoothstep(_DesatFocus.z * 0.6, _DesatFocus.z, length(fq)));
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, luma.xxx, saturate(_Desat) * (1.0 - keep));

                // Melt death: darken and bleed red; above the dripping columns is black.
                col = lerp(col, col * float3(1.0, 0.25, 0.35), saturate(_Drip * 1.5));
                col *= (1.0 - 0.6 * _Drip * _Drip) * lerp(1.0, dripMask, step(0.001, _Drip));

                // REBOOT: CRT collapse to a bright line and back, with green static.
                float r = saturate(_Reboot);
                float halfH = 0.5 * (1.0 - r) * (1.0 - r) + 0.002;
                float inside = 1.0 - smoothstep(halfH, halfH + 0.01, abs(screen.y - 0.5));
                float noise = Hash12(floor(screen * _ScreenParams.xy * 0.5) + floor(t * 30.0));
                col = lerp(col, float3(0.1, 0.9, 0.45) * noise, smoothstep(0.3, 0.95, r) * 0.65);
                col = col * inside + inside * r * r * 0.5 * _FlashScale;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
