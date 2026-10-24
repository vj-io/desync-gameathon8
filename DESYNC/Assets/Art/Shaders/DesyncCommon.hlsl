#ifndef DESYNC_COMMON_INCLUDED
#define DESYNC_COMMON_INCLUDED

// Shared helpers for the DESYNC shaders. WebGL2/GLES3 safe: float math only, no uint tricks.
// Include after URP Core.hlsl.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// Colour-blind palette: Desync.Art.Palette writes these globals (linear colours) when the setting changes. _Colorblind = 0 keeps
// every shader on its authored colours. Pattern cues (hatch on solid walls, stripes on threat bodies) read in both modes.
float _Colorblind;
float4 _CbRenderLow, _CbRenderHigh, _CbSource, _CbDanger;

// "Hash without Sine" by Dave Hoskins (MIT), https://www.shadertoy.com/view/4djSRW
float Hash11(float p)
{
    p = frac(p * 0.1031);
    p *= p + 33.33;
    p *= p + p;
    return frac(p);
}

float Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float ValueNoise(float x)
{
    float i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(Hash11(i), Hash11(i + 1.0), f);
}

// MeshPrep vertex layout: TEXCOORD3 = (bary.x, bary.y, bary.z, flags).
//   bary  = (1,0,0) / (0,1,0) / (0,0,1) at the triangle's corners.
//   flags = 8 + suppressed-edge bits (1: edge opposite x, 2: opposite y, 4: opposite z); 0 = mesh was never prepped.
// raw   = full wireframe (incl. quad diagonals); clean = suppressed edges pushed to 1 so they never draw.
void DecodeBary(float4 data, out float3 raw, out float3 clean, out float valid)
{
    float f = floor(data.w + 0.5);
    valid = step(7.5, f);
    f -= 8.0 * valid;
    float3 sup = float3(fmod(f, 2.0), fmod(floor(f * 0.5), 2.0), floor(f * 0.25));
    raw = data.xyz;
    clean = max(raw, sup);
}

// Screen-space distance (pixels) to the nearest edge of the triangle.
float EdgePixels(float3 b)
{
    float3 d = b / max(fwidth(b), 1e-5);
    return min(d.x, min(d.y, d.z));
}

// Anti-aliased line core of `widthPx` pixels.
float EdgeCore(float px, float widthPx)
{
    return 1.0 - smoothstep(widthPx - 0.5, widthPx + 0.5, px);
}

// Soft halo that fakes glow on the faces next to an edge.
float EdgeHalo(float px, float haloPx)
{
    float h = saturate(1.0 - px / max(haloPx, 1e-3));
    return h * h;
}

// 1 = no fog, 0 = fully fogged (works with every fog keyword, incl. fog off).
float FogVisibility(float fogFactor)
{
    return MixFogColor(float3(1.0, 1.0, 1.0), float3(0.0, 0.0, 0.0), fogFactor).x;
}

// Anti-aliased 50 % stripes along c (c counts stripe periods). Far away the stripes average out to a flat 50 %.
float Stripes(float c)
{
    float w = fwidth(c);
    return 1.0 - smoothstep(0.5 - w, 0.5 + w, abs(frac(c) - 0.5) * 2.0);
}

// Glyph atlas: 8x8 cells, id 0..63, single channel (R). cellUV is in glyph cells (1 unit = 1 glyph).
TEXTURE2D(_GlyphTex);
SAMPLER(sampler_GlyphTex);

float SampleGlyph(float2 cellUV, float id, float2 dx, float2 dy)
{
    float2 cell = float2(fmod(id, 8.0), floor(id * 0.125));
    float2 uv = (cell + frac(cellUV)) * 0.125;
    return SAMPLE_TEXTURE2D_GRAD(_GlyphTex, sampler_GlyphTex, uv, dx * 0.125, dy * 0.125).r;
}

#endif
