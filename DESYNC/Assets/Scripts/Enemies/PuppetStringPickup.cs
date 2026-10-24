using UnityEngine;

namespace Desync.Enemies
{
    /// Puppet string (story shard pickup), SOURCE-only: a green string hanging into a nook, shown only in SOURCE. Touching it
    /// in SOURCE collects it once: EnemyEvents.PuppetString(id) (Flow scores PUPPET_STRING; the story text lands in M4).
    /// Gone until the room resets.
    public class PuppetStringPickup : MonoBehaviour, IResettable
    {
        public const float PickRadius = 0.6f;

        public string id;
        public Transform visual;

        bool _taken;

        public bool Taken => _taken;

        void Update()
        {
            bool show = LayerState.InSource && !_taken;
            if (visual && visual.gameObject.activeSelf != show) visual.gameObject.SetActive(show);
            if (!show) return;
            float t = RoomClock.Time;
            if (visual) visual.localRotation = Quaternion.Euler(4f * Mathf.Sin(t * 1.7f), t * 40f, 4f * Mathf.Sin(t * 2.3f));
            if (!GameState.Alive || GameState.Cinematic || !EnemyKit.TouchesPlayer(transform.position + Vector3.up, PickRadius)) return;
            _taken = true;
            EnemyEvents.PuppetString?.Invoke(id);
        }

        public void ResetState() => _taken = false;
    }
}
