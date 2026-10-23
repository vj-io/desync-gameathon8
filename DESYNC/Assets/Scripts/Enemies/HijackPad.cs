using UnityEngine;

namespace Desync.Enemies
{
    /// Hijack console linked to one drone: the same hack minigame (3 arrows); success hijacks the drone for hijackDuration
    /// (raises Hijacked via HijackedDrone, not AccessGranted). Re-hackable once the drone is back on patrol.
    public class HijackPad : HackStation
    {
        public HijackedDrone drone;

        bool _wasBusy;

        protected override int Length => 3;
        protected override bool Available => drone && !drone.Active && !(drone.patrol && drone.patrol.Rebooting);
        protected override string StatusOverride => !drone ? "<color=#FF2633>NO LINK</color>" : null;
        protected override string DoneText => "LINK ACTIVE";
        protected override string DoneSub => "DRONE HIJACKED";

        protected override void OnHacked()
        {
            if (!drone) return;
            drone.Begin();
            GameEvents.PopText?.Invoke("DRONE HIJACKED", GameConfig.I.cyan);
        }

        protected override void Update()
        {
            bool busy = drone && drone.Active;
            if (done && _wasBusy && !busy) { done = false; Redraw(true); }   // hijack over: pad is live again
            _wasBusy = busy;
            base.Update();
        }

        public override void ResetState()
        {
            _wasBusy = false;
            base.ResetState();
        }
    }
}
