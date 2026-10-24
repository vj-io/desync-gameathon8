using Desync.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Red screen-edge pulse while a security officer is chasing the player (EnemyEvents.GuardChase). The danger colour on the
    /// shared vignette, under the HUD; a soft 1.4 Hz throb, never a full-screen flash, and steady with Reduce Flashing / Reduce FX.
    /// UIManager.BuildCanvas creates it (again after a palette rebuild); it fades out on death, pause and REBOOT.
    public sealed class HudGuards : MonoBehaviour
    {
        const float Rate = 1.4f, Low = 0.16f, High = 0.42f, Steady = 0.28f, Fade = 3f;

        Image _edge;
        bool _on;
        float _a;

        public static HudGuards Create(Transform canvas)
        {
            var img = UIKit.Img(canvas, "GuardChaseEdge", new Color(UIKit.Danger.r, UIKit.Danger.g, UIKit.Danger.b, 0f), UIKit.Vignette);
            UIKit.Stretch(img.rectTransform);
            img.raycastTarget = false;
            var h = img.gameObject.AddComponent<HudGuards>();
            h._edge = img;
            return h;
        }

        internal float Alpha => _a;

        void OnEnable()
        {
            _on = Guard.Chasing > 0;
            EnemyEvents.GuardChase += OnChase;
            GameEvents.Rebooting += Off;
            GameEvents.RoomReset += Off;
        }

        void OnDisable()
        {
            EnemyEvents.GuardChase -= OnChase;
            GameEvents.Rebooting -= Off;
            GameEvents.RoomReset -= Off;
        }

        void OnChase(bool on) => _on = on;
        void Off() => _on = false;

        void Update()
        {
            float target = 0f;
            if (_on && GameState.Alive && !GameState.Paused)
                target = UIKit.Flashes ? Mathf.Lerp(Low, High, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Rate * 2f * Mathf.PI)) : Steady;
            _a = Mathf.MoveTowards(_a, target, Time.unscaledDeltaTime * Fade);
            var c = _edge.color;
            if (Mathf.Approximately(c.a, _a)) return;
            c.a = _a;
            _edge.color = c;
        }
    }
}
