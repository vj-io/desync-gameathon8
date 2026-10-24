using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Death recap (GameEvents.DeathRecap): cause card at the bottom, a ring on the killer when it is on screen or an
    /// edge arrow pointing at it when it is not. Flow freezes time and raises Rebooting when it ends (any key skips).
    /// The 60 % desaturation is the post-FX pass (PostFXController listens to the same event).
    /// In a BREACH run the Backup is already spent when the recap fires: the card says BACKUP USED (n left) or RUN OVER
    /// (RunManager.I.RunLost: this recap ends the run and leads to the run results).
    public sealed class RecapScreen : UIPanel
    {
        TMP_Text _cause, _time, _near, _any, _tag, _arrow, _run;
        RectTransform _card, _marker, _arrowRt, _tagRt; Image _ring; Tween _pop;
        bool _hasKiller; Vector3 _killerPos; float _t;
        const float EdgeInset = 90f;

        protected override void Create()
        {
            Group.blocksRaycasts = false;
            var danger = UIKit.Danger;

            // ---- killer marker (under the card)
            _marker = UIKit.Place(UIKit.Obj(Root, "Killer"), Anc.C, Vector2.zero, new Vector2(10, 10));
            _ring = UIKit.Img(_marker, "Ring", danger, UIKit.Ring); UIKit.Place(_ring.rectTransform, Anc.C, Vector2.zero, new Vector2(150, 150));
            _arrow = UIKit.Text(_marker, "Arrow", UIKit.Hud, 96, danger, TextAlignmentOptions.Center, "▲");
            _arrowRt = UIKit.Place(_arrow.rectTransform, Anc.C, Vector2.zero, new Vector2(110, 110));
            _tag = UIKit.Text(_marker, "Tag", UIKit.Hud, 30, danger, TextAlignmentOptions.Center); _tag.fontStyle = FontStyles.Bold; _tag.characterSpacing = 4f;
            _tagRt = UIKit.Place(_tag.rectTransform, Anc.C, new Vector2(0, -100), new Vector2(300, 40));
            _marker.gameObject.SetActive(false);

            // ---- cause card (bottom centre keeps the middle of the screen, where the killer usually is, clear)
            var card = UIKit.Box(Root, "Card", danger, 0.9f);
            _card = UIKit.Place(card.rectTransform, Anc.B, new Vector2(0, 110), new Vector2(1040, 300));
            var head = UIKit.Text(card.transform, "Head", UIKit.Title, 32, danger, TextAlignmentOptions.Center, "CONNECTION TERMINATED");
            head.fontStyle = FontStyles.Bold; head.characterSpacing = 10f;
            UIKit.Place(head.rectTransform, Anc.T, new Vector2(0, -16), new Vector2(980, 44));

            var cl = UIKit.Text(card.transform, "CauseLabel", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Left, "CAUSE"); cl.characterSpacing = 6f;
            UIKit.Place(cl.rectTransform, Anc.TL, new Vector2(48, -84), new Vector2(220, 40));
            _cause = UIKit.Text(card.transform, "Cause", UIKit.Title, 58, UIKit.Paper, TextAlignmentOptions.Left); _cause.fontStyle = FontStyles.Bold;
            UIKit.Place(_cause.rectTransform, Anc.TL, new Vector2(270, -64), new Vector2(740, 74));

            var tl = UIKit.Text(card.transform, "TimeLabel", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Left, "ROOM TIME"); tl.characterSpacing = 6f;
            UIKit.Place(tl.rectTransform, Anc.TL, new Vector2(48, -150), new Vector2(220, 40));
            _time = UIKit.Text(card.transform, "Time", UIKit.Hud, 40, UIKit.Paper, TextAlignmentOptions.Left); _time.fontStyle = FontStyles.Bold;
            UIKit.Place(_time.rectTransform, Anc.TL, new Vector2(270, -146), new Vector2(740, 48));

            _run = UIKit.Text(card.transform, "RunStatus", UIKit.Hud, 34, UIKit.Green, TextAlignmentOptions.Right); _run.fontStyle = FontStyles.Bold; _run.characterSpacing = 4f;
            UIKit.Place(_run.rectTransform, Anc.TR, new Vector2(-48, -146), new Vector2(440, 48));
            _run.gameObject.SetActive(false);

            _near = UIKit.Text(card.transform, "NearMiss", UIKit.Hud, 32, UIKit.Green, TextAlignmentOptions.Center); _near.characterSpacing = 4f;
            UIKit.Place(_near.rectTransform, Anc.T, new Vector2(0, -206), new Vector2(980, 40));

            _any = UIKit.Text(card.transform, "AnyKey", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, "ANY KEY ► REBOOT"); _any.characterSpacing = 8f;
            UIKit.Place(_any.rectTransform, Anc.B, new Vector2(0, 12), new Vector2(980, 32));
        }

        /// CAUSE line: ICE-<id> | MELT 100% | TRACE LOCKDOWN | GLITCH ZONE | DRONE | ARCHITECT. No allocation.
        internal static string CauseText(DeathCause cause, string killerId)
        {
            switch (cause)
            {
                case DeathCause.Ice: return string.IsNullOrEmpty(killerId) ? "ICE" : killerId;
                case DeathCause.Melt: return "MELT 100%";
                case DeathCause.Alarm:
                case DeathCause.TraceTimer: return "TRACE LOCKDOWN";
                case DeathCause.Drone: return "DRONE";
                case DeathCause.Architect: return "ARCHITECT";
                case DeathCause.Glitch: return "GLITCH ZONE";
                default: return "UNKNOWN";
            }
        }

        public void Show(DeathInfo d)
        {
            base.Show();
            _t = 0f;
            _cause.SetText(CauseText(d.cause, d.killerId));
            _time.SetText(UIKit.ClockShort(d.roomTime));
            bool near = !string.IsNullOrEmpty(d.nearMiss);
            _near.gameObject.SetActive(near);
            if (near) _near.SetText(UIKit.Safe(d.nearMiss.ToUpperInvariant(), UIKit.Hud));
            _hasKiller = !string.IsNullOrEmpty(d.killerId);
            _killerPos = d.killerPos;
            _marker.gameObject.SetActive(_hasKiller);
            if (_hasKiller) _tag.SetText(d.killerId);
            bool run = RunView.Active && RunManager.I, lost = run && RunManager.I.RunLost;
            _run.gameObject.SetActive(run);
            if (run) { _run.SetText(lost ? "RUN OVER" : "BACKUP USED · " + RunState.Backups + " LEFT"); _run.color = lost ? UIKit.Danger : UIKit.Green; }
            _any.SetText(lost ? "ANY KEY ► RUN RESULTS" : "ANY KEY ► REBOOT");
            UIKit.Pop(ref _pop, _card, 0.8f, 0.22f);
            Tick(0f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        public override void Tick(float dt)
        {
            _t += dt;
            _any.alpha = !UIKit.Flashes || ((int)(_t * 3f) & 1) == 0 ? 1f : 0.35f;
            if (_hasKiller) PlaceMarker();
        }

        void PlaceMarker()
        {
            var cam = PlayerRef.Cam ? PlayerRef.Cam : Camera.main;
            if (!cam) { _marker.gameObject.SetActive(false); return; }
            Vector3 vp = cam.WorldToViewportPoint(_killerPos);
            Vector2 size = Root.rect.size;
            var p = new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
            bool behind = vp.z <= 0f;
            if (behind) p = -p;   // the projection mirrors behind the camera
            float hx = size.x * 0.5f - EdgeInset, hy = size.y * 0.5f - EdgeInset;
            bool onScreen = !behind && Mathf.Abs(p.x) <= hx && Mathf.Abs(p.y) <= hy;
            if (!_marker.gameObject.activeSelf) _marker.gameObject.SetActive(true);
            _ring.enabled = onScreen; _arrow.enabled = !onScreen;
            if (onScreen)
            {
                _marker.anchoredPosition = p;
                _ring.rectTransform.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(_t * 10f));
                _tagRt.anchoredPosition = new Vector2(0f, -100f);
            }
            else
            {
                if (p.sqrMagnitude < 1f) p = new Vector2(0f, -1f);
                float k = Mathf.Min(hx / Mathf.Max(Mathf.Abs(p.x), 1e-4f), hy / Mathf.Max(Mathf.Abs(p.y), 1e-4f));
                p *= k;
                _marker.anchoredPosition = p;
                _arrowRt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg - 90f);   // the glyph points up
                _tagRt.anchoredPosition = -p.normalized * 90f;
            }
        }

        /// Test hooks.
        internal bool MarkerOnScreen => _marker.gameObject.activeSelf && _ring.enabled;
        internal bool MarkerArrow => _marker.gameObject.activeSelf && _arrow.enabled;
        internal Vector2 MarkerPos => _marker.anchoredPosition;
    }
}
