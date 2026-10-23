using PrimeTween;
using UnityEngine;

namespace Desync.Enemies
{
    /// Room 6 boss. A giant wireframe face hangs above the core; its gaze (VisionCone, bothLayers) sweeps a full circle
    /// every GazePeriod while bobbing in pitch, so it detects in RENDER and SOURCE -> TRACE. Puppet strings show only in
    /// SOURCE. On FinaleStarted: Cinematic, the player's view is tweened up to the face, the face splits into the board's
    /// faces which fly to every Billboard (ShowFace), then GameCompleted (~6.2 s total).
    public class Architect : MonoBehaviour, IResettable
    {
        public const float GazePeriod = 14f, PitchSwing = 14f, PitchPeriod = 6.5f;

        public Transform headPivot;     // yaw follows the gaze
        public Transform gazeTilt;      // pitch (base value set by the factory)
        public float basePitch = 55f;
        public VisionCone gaze;
        public GameObject face;         // the big head visual
        public GameObject strings;
        public Transform[] shards;      // small board faces, inactive until the finale
        public float shardScale = 1.5f;

        bool _finale;
        Sequence _seq;

        void OnEnable()
        {
            GameEvents.FinaleStarted += Finale;
            LayerState.Changed += OnLayer;
            OnLayer(LayerState.InSource);
        }

        void OnDisable()
        {
            GameEvents.FinaleStarted -= Finale;
            LayerState.Changed -= OnLayer;
            if (_seq.isAlive) _seq.Stop();
            if (_finale) GameState.Cinematic = false;
        }

        void Update()
        {
            if (_finale) return;
            float t = RoomClock.Time;
            headPivot.localRotation = Quaternion.Euler(0f, 360f * t / GazePeriod, 0f);
            if (gazeTilt) gazeTilt.localRotation = Quaternion.Euler(basePitch + PitchSwing * Mathf.Sin(2f * Mathf.PI * t / PitchPeriod), 0f, 0f);
        }

        void OnLayer(bool source)
        {
            if (strings && !_finale) strings.SetActive(source);
        }

        void Finale()
        {
            if (_finale) return;
            _finale = true;
            GameState.Cinematic = true;
            if (gaze) gaze.Suppressed = true;
            if (strings) strings.SetActive(true);

            _seq = Sequence.Create();
            Transform root = PlayerRef.Root, head = PlayerRef.Head;
            if (root && head)
            {
                Vector3 to = headPivot.position - head.position;
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                _seq.Group(Tween.Rotation(root, Quaternion.Euler(0f, yaw, 0f), 1.5f, Ease.InOutSine))
                    .Group(Tween.LocalRotation(head, Quaternion.Euler(pitch, 0f, 0f), 1.5f, Ease.InOutSine))
                    .Group(Tween.Rotation(headPivot, Quaternion.Euler(0f, yaw + 180f, 0f), 1.5f, Ease.InOutSine));
            }
            _seq.ChainDelay(1f)                                                                      // stare: strings visible
                .Chain(Tween.Scale(face.transform, 1.08f, 0.12f, Ease.OutQuad, 6, CycleMode.Yoyo))   // glitch shiver
                .ChainCallback(this, a => a.Split())
                .ChainDelay(3f)
                .ChainCallback(() => GameEvents.GameCompleted?.Invoke());
        }

        void Split()
        {
            var boards = FindObjectsByType<Billboard>(FindObjectsInactive.Exclude);
            face.SetActive(false);
            for (int i = 0; i < shards.Length; i++)
            {
                var s = shards[i];
                s.gameObject.SetActive(true);
                s.position = face.transform.position;
                float a = 2f * Mathf.PI * i / shards.Length;
                Vector3 to = boards.Length > 0 ? boards[i % boards.Length].transform.position
                                               : headPivot.position + new Vector3(Mathf.Cos(a) * 9f, -3f, Mathf.Sin(a) * 9f);
                Tween.Position(s, to, 1.6f, Ease.InCubic);
                Tween.Scale(s, shardScale, shardScale * 0.3f, 1.6f, Ease.InCubic);
            }
            Tween.Delay(this, 1.6f, a =>
            {
                for (int i = 0; i < a.shards.Length; i++) a.shards[i].gameObject.SetActive(false);
                var bs = FindObjectsByType<Billboard>(FindObjectsInactive.Exclude);
                for (int i = 0; i < bs.Length; i++) bs[i].ShowFace(i);
            });
            GameEvents.ShakeRequest?.Invoke(0.6f);
        }

        public void ResetState()
        {
            if (_seq.isAlive) _seq.Stop();
            if (_finale) GameState.Cinematic = false;
            _finale = false;
            if (gaze) gaze.Suppressed = false;
            if (face) face.SetActive(true);
            foreach (var s in shards) if (s) { s.gameObject.SetActive(false); s.localScale = Vector3.one * shardScale; }
            OnLayer(LayerState.InSource);
        }
    }
}
