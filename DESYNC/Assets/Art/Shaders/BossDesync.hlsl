// ARCHITECT P3 "Desync": both layers on screen at once. Global _Desync (0/1, written by Desync.Enemies.ArchitectBoss) splits
// the frame at a torn vertical seam: left of it reads as RENDER (cyan -> magenta by height), right of it as SOURCE (green,
// row-striped so the halves differ without colour too), whatever layer the player is really in (layer = _Layer: the half
// that matches it keeps the real image). Included by PostFX.shader after DesyncCommon.hlsl; one call after the colour fetch.
// No texture reads, no flashes; fxK (REDUCE FX) calms the seam's jitter.
#ifndef DESYNC_BOSS_DESYNC_INCLUDED
#define DESYNC_BOSS_DESYNC_INCLUDED

float _Desync;

float3 BossDesync(float3 col, float2 screen, float t, float fxK, float layer)
{
    if (_Desync <= 0.0) return col;
    float row = floor(screen.y * 60.0);
    float seam = 0.5 + ((Hash11(row + floor(t * 12.0) * 3.7) - 0.5) * 0.03 + sin(t * 1.7 + screen.y * 6.0) * 0.01) * fxK;
    float right = step(seam, screen.x);              // 1 = the SOURCE half
    float other = abs(right - saturate(layer));      // 1 where this half shows the layer you are not in
    float luma = dot(col, float3(0.299, 0.587, 0.114));
    float stripe = 0.75 + 0.25 * step(0.5, frac(screen.y * _ScreenParams.y / 3.0));
    float3 src = float3(0.0, 1.0, 0.4) * (0.12 + 1.6 * luma) * stripe;
    float3 ren = lerp(float3(0.0, 0.94, 1.0), float3(1.0, 0.17, 0.84), screen.y) * (0.1 + 1.4 * luma);
    col = lerp(col, lerp(ren, src, right), other * 0.85 * _Desync);
    float edge = saturate(1.0 - abs(screen.x - seam) * 250.0);
    return col + edge * lerp(float3(1.0, 0.17, 0.84), float3(0.0, 1.0, 0.4), right) * 0.6 * _Desync;
}

#endif
