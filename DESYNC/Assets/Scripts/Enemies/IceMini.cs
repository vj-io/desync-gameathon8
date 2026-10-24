using UnityEngine;

namespace Desync.Enemies
{
    /// Half of a split Splitter ICE: straight at the player at SplitterIce.MiniSpeed (x Hardened ICE), kills on touch, can be
    /// grazed. Waits frozen in RENDER; its owner (SplitterIce) ends it after MiniLife.
    public class IceMini : IceHunter
    {
        protected override float Speed => SplitterIce.MiniSpeed * GameState.Mods.IceSpeedMult;
        protected override bool DespawnsInRender => false;
        protected override void Animate(float dt) { if (visual) visual.Rotate(0f, 720f * dt, 0f, Space.Self); }
    }
}
