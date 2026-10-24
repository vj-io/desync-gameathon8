using UnityEngine;

namespace Desync.Enemies
{
    /// Splitter ICE (spec enemy 9, SOURCE), on an ICE marker. Its hunter (SplitterHunter) comes out at the post the first
    /// time the player is in SOURCE, then hunts like a pillar hunter: chases on sight, loses track 2 s after, walks back to
    /// the post and waits. A GRAZE on it (GameEvents.Grazed with its id) splits it: it vanishes and two IceMini take its
    /// place, MiniSpeed m/s straight at the player, for MiniLife s (EnemyEvents.SplitterSplit). Back at the post after a
    /// room reset. Frozen and harmless in RENDER like any ICE (the minis keep their clock running there).
    public class SplitterIce : MonoBehaviour, IResettable
    {
        public const float MiniLife = 8f, MiniSpeed = 6f, MiniSpread = 0.6f;

        public SplitterHunter hunter;
        public IceMini[] minis;

        bool _out, _split;
        float _minisUntil = -1f;

        public bool Split => _minisUntil >= 0f;

        void OnEnable() => GameEvents.Grazed += OnGrazed;
        void OnDisable() => GameEvents.Grazed -= OnGrazed;

        /// Raised from inside the hunter's own Update: the split itself waits for this component's next Update.
        void OnGrazed(string id)
        {
            if (hunter && hunter.gameObject.activeSelf && id == hunter.Id) _split = true;
        }

        void Update()
        {
            if (!_out && hunter && LayerState.InSource && PlayerRef.Exists) { _out = true; hunter.Spawn(transform.position); }
            if (_split) Break();
            if (_minisUntil >= 0f && RoomClock.Time >= _minisUntil) { _minisUntil = -1f; Despawn(minis); }
        }

        void Break()
        {
            _split = false;
            var t = hunter.transform;
            Vector3 p = t.position, side = t.right * MiniSpread;
            hunter.Despawn();
            for (int i = 0; i < minis.Length; i++) if (minis[i]) minis[i].Spawn(p + ((i & 1) == 0 ? side : -side));
            _minisUntil = RoomClock.Time + MiniLife;
            EnemyEvents.SplitterSplit?.Invoke(p);
        }

        static void Despawn(IceHunter[] hs)
        {
            foreach (var h in hs) if (h) h.Despawn();
        }

        public void ResetState()
        {
            _out = _split = false;
            _minisUntil = -1f;
            if (hunter) hunter.Despawn();
            Despawn(minis);
        }
    }
}
