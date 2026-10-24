using UnityEngine;

namespace Desync.Enemies
{
    /// Sentinel ICE (spec enemy 7, SOURCE): stationary. Every Period s of RoomClock (+ phase) it pulses: a player in SOURCE
    /// within PulseRadius (horizontal, same floor) dies (DeathCause.Ice, killer "SENTINEL-n"). For the Telegraph s before
    /// each pulse a ring grows from the crystal out to the kill radius (EnemyEvents.SentinelCharge), and stays at the rim
    /// for Flash s after it (EnemyEvents.SentinelPulse). The body kills on touch in SOURCE like any ICE. RENDER: harmless;
    /// the body flickers like ICE, but the rim and the charge ring keep showing, so pulses can be timed from the safe layer.
    public class Sentinel : MonoBehaviour, IResettable
    {
        public const float Period = 2.5f, Telegraph = 0.6f, PulseRadius = 4f, BodyRadius = 0.45f, Flash = 0.15f;

        public float phase;
        public Transform charge;        // unit ring, scaled out to PulseRadius over the telegraph
        public Transform crystal;       // spins
        public Renderer[] bodyRenderers;

        int _cycle = int.MinValue;
        bool _charging, _shown = true;
        float _flashUntil = -1f;
        string _id;

        public bool Charging => _charging;
        public string Id => _id ??= EnemyKit.SiblingId<Sentinel>(this, "SENTINEL-");

        void Update()
        {
            float now = RoomClock.Time, t = now + phase;
            int cycle = Mathf.FloorToInt(t / Period);
            if (_cycle == int.MinValue || cycle < _cycle) _cycle = cycle;   // first frame, or the clock was reset
            if (cycle > _cycle) { _cycle = cycle; Pulse(now); }

            float left = (cycle + 1) * Period - t;
            bool charging = left <= Telegraph;
            if (charging && !_charging) EnemyEvents.SentinelCharge?.Invoke(transform.position);
            _charging = charging;
            float r = charging ? PulseRadius * (1f - left / Telegraph) : now < _flashUntil ? PulseRadius : 0f;
            if (charge)
            {
                bool on = r > 0.05f;
                if (charge.gameObject.activeSelf != on) charge.gameObject.SetActive(on);
                if (on) charge.localScale = new Vector3(r, 1f, r);
            }

            bool source = LayerState.InSource;
            Show(source || (Time.frameCount & 4) == 0);
            if (crystal) crystal.localRotation = Quaternion.Euler(0f, now * 90f, 0f);
            if (source && EnemyKit.TouchesPlayer(transform.position + Vector3.up * 0.8f, BodyRadius)) Kill();
        }

        void Pulse(float now)
        {
            _flashUntil = now + Flash;
            EnemyEvents.SentinelPulse?.Invoke(transform.position);
            if (!LayerState.InSource || !PlayerRef.Exists) return;
            Vector3 d = PlayerRef.Position - transform.position;
            if (Mathf.Abs(d.y) < 2.5f && d.x * d.x + d.z * d.z <= PulseRadius * PulseRadius) Kill();
        }

        void Kill()
        {
            if (!GameState.Alive || GameState.Cinematic) return;
            PlayerRef.LastKillerId = Id;
            PlayerRef.LastKillerPos = transform.position;
            PlayerRef.Kill(DeathCause.Ice);
        }

        void Show(bool on)
        {
            if (on == _shown) return;
            _shown = on;
            foreach (var r in bodyRenderers) if (r) r.enabled = on;
        }

        public void ResetState()
        {
            _cycle = int.MinValue;
            _charging = false;
            _flashUntil = -1f;
            if (charge) charge.gameObject.SetActive(false);
        }
    }
}
