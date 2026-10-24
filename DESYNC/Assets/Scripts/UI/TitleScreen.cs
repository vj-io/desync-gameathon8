using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Title scene overlay + attract mode (orbiting camera, backdrop flips RENDER/SOURCE every ~3 s) + mode menu:
    /// CAMPAIGN BREACH / QUICK BREACH / CLASSIC (6 ROOMS) / DAILY (greyed, soon). Mouse (hover + click), arrows / WASD /
    /// pad d-pad to move, SPACE / Enter / pad A / Start or a click on the backdrop to start the highlighted mode.
    public sealed class TitleScreen : UIPanel
    {
        GlitchLabel _logo;
        TMP_Text _tag, _intro, _meta, _sensVal, _caption, _goals;
        Slider _sens;
        UIButton[] _items; int _sel;
        static readonly RunMode[] Modes = { RunMode.Campaign, RunMode.Quick, RunMode.Classic, RunMode.Daily };
        static readonly string[] Labels = { "CAMPAIGN BREACH", "QUICK BREACH", "CLASSIC (6 ROOMS)", "DAILY · SOON" };
        static readonly string[] Blurbs =
        {
            "BRANCHING MAP · EXPLOITS · BOSSES",
            "ACT 1 + THE AUDITOR · ~6 MIN",
            "THE ORIGINAL SIX ROOMS · ~8 MIN",
            "ONE SHARED SEED A DAY · SOON",
        };
        /// Highlighted on every Show. CLASSIC keeps "click / SPACE / pad = jack in to Room 01" as the one-press path.
        internal const int DefaultItem = 2, DailyItem = 3;
        internal int Selected => _sel;
        Tween _pulse, _clickTw;
        Camera _cam; float _radius, _height, _angle;
        float _flipT, _introT, _introChars, _introDelay;
        int _introShown;
        bool _started, _shownSource;
        const float OrbitDegPerSec = 7f, FlipEvery = 3f, IntroCps = 55f;

        protected override void Create()
        {
            // click-anywhere catcher (sits below the slider, so dragging the slider never starts the game)
            var catcher = UIKit.Img(Root, "ClickCatcher", new Color(0, 0, 0, 0));
            UIKit.Stretch(catcher.rectTransform); catcher.raycastTarget = true;
            catcher.gameObject.AddComponent<TitleClick>().Owner = this;

            var shade = UIKit.Img(Root, "Shade", new Color(0f, 0f, 0.02f, 0.9f), UIKit.Vignette);
            UIKit.Stretch(shade.rectTransform);
            var card = UIKit.Img(Root, "Card", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.72f));
            UIKit.Place(card.rectTransform, Anc.C, new Vector2(0, -10), new Vector2(1520, 780));
            var fr = UIKit.Img(card.transform, "Frame", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.55f), UIKit.Frame, Image.Type.Sliced);
            UIKit.Stretch(fr.rectTransform);

            _caption = UIKit.Text(card.transform, "Caption", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Center, "HALCYON CORP // TERMINAL 0x01 // GAMEATHON 8.0 // BREAK THE SIMULATION");
            _caption.characterSpacing = 6f; UIKit.Place(_caption.rectTransform, Anc.T, new Vector2(0, -22), new Vector2(1400, 30));

            _logo = GlitchLabel.Create(card.transform, "Logo", Anc.C, new Vector2(0, 225), new Vector2(1400, 260), UIKit.Title, 210, UIKit.Paper, TextAlignmentOptions.Center, 5f, true, 18f);
            _logo.IdleEvery = 2.8f;
            _goals = UIKit.Text(card.transform, "Goals", UIKit.Hud, 30, UIKit.Dim, TextAlignmentOptions.Center); _goals.characterSpacing = 8f;
            UIKit.Place(_goals.rectTransform, Anc.C, new Vector2(0, 116), new Vector2(1400, 38));

            _tag = UIKit.Text(card.transform, "Tagline", UIKit.Hud, 46, UIKit.Cyan, TextAlignmentOptions.Center, "SPACE = SEE THE CODE");
            _tag.characterSpacing = 8f; UIKit.Place(_tag.rectTransform, Anc.C, new Vector2(0, 62), new Vector2(1400, 60));

            _intro = UIKit.Text(card.transform, "Intro", UIKit.Term, 42, UIKit.Paper, TextAlignmentOptions.Top);
            _intro.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_intro.rectTransform, Anc.C, new Vector2(0, -30), new Vector2(1240, 130));

            _items = new UIButton[Modes.Length];
            for (int i = 0; i < Modes.Length; i++)
            {
                int k = i;
                var col = i == DailyItem ? new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f) : i == 0 ? UIKit.Magenta : UIKit.Cyan;
                var b = UIButton.Create(card.transform, "Mode" + i, Labels[i], Anc.C, new Vector2(i % 2 == 0 ? -250 : 250, i < 2 ? -132 : -224), new Vector2(470, 80), 30, col, () => Pick(k));
                b.Label.characterSpacing = 3f;
                if (i == DailyItem) b.Hot = col;   // greyed: no hover highlight
                else b.Hovered = () => Select(k);
                _items[i] = b;
            }

            _meta = UIKit.Text(card.transform, "Meta", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Center);
            _meta.characterSpacing = 4f; UIKit.Place(_meta.rectTransform, Anc.C, new Vector2(0, -292), new Vector2(1400, 36));

            var sl = UIKit.Text(card.transform, "SensLabel", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Right, "SENSITIVITY");
            UIKit.Place(sl.rectTransform, Anc.C, new Vector2(-390, -342), new Vector2(260, 36));
            _sens = UIControls.NewSlider(card.transform, "SensSlider", Anc.C, new Vector2(0, -342), 420, 0.1f, 3f, Settings.Sensitivity, OnSens);
            _sensVal = UIKit.Text(card.transform, "SensValue", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left);
            UIKit.Place(_sensVal.rectTransform, Anc.C, new Vector2(300, -342), new Vector2(140, 36));
            _sensVal.SetText("{0:0.00}", Settings.Sensitivity);
        }

        sealed class TitleClick : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
        {
            public TitleScreen Owner;
            public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
            {
                if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left) Owner.Start0();
            }
        }

        void OnSens(float v)
        {
            Settings.Sensitivity = v;
            _sensVal.SetText("{0:0.00}", Settings.Sensitivity);
            GameEvents.SettingsChanged?.Invoke();
        }

        void Select(int i)
        {
            if (i == DailyItem || i < 0 || i >= _items.Length) return;
            if (_items[_sel] && _sel != i) _items[_sel].transform.localScale = Vector3.one;
            _sel = i;
            for (int k = 0; k < _items.Length; k++) _items[k].SetSelected(k == i);
            _meta.SetText(Blurbs[i] + "  ·  ↑↓←→ CHOOSE  ·  CLICK, SPACE OR PAD A TO JACK IN");
            UIKit.Kill(ref _pulse);
            _pulse = Tween.Scale(_items[i].transform, 1f, 1.04f, 0.55f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
        }

        void Pick(int i)
        {
            if (i == DailyItem) { _logo.Glitch(1.4f, 0.3f); UIKit.Pop(ref _clickTw, _items[i].transform, 0.9f, 0.2f); return; }   // not yet
            Select(i);
            Start0();
        }

        /// Starts the highlighted mode (backdrop click, SPACE / Enter / pad, or a mode button).
        void Start0()
        {
            if (_started) return;
            _started = true;
            UIKit.Kill(ref _pulse);
            _items[_sel].Label.SetText("JACKING IN...");
            _logo.Glitch(2.2f, 0.4f);
            UIKit.Pop(ref _clickTw, _items[_sel].transform, 0.85f, 0.25f);
            RunView.Start(Modes[_sel]);
        }

        public override void Show()
        {
            base.Show();
            _started = false; _cam = null;
            for (int i = 0; i < _items.Length; i++) { _items[i].Label.SetText(Labels[i]); _items[i].transform.localScale = Vector3.one; }
            var cfg = GameConfig.I;
            _intro.SetText(cfg.introLines != null && cfg.introLines.Length > 0 ? cfg.introLines[0] : "");
            _intro.maxVisibleCharacters = 0; _introChars = 0; _introShown = 0; _introDelay = 0.9f;
            _logo.Reveal("DESYNC", 0.9f);
            string goals;
            try { goals = GoalsLine(); }
            catch (System.Exception e) { goals = "BEST --:-- · MEDALS 0/" + SceneFlow.RoomCount * 4; Debug.LogWarning("[DESYNC] title goals: save unavailable (" + e.GetType().Name + ")"); }   // the title must never fail on a save problem
            _goals.SetText(goals);
            _flipT = FlipEvery;
            LayerState.Set(false);
            _shownSource = false; Tint();
            _sel = DefaultItem; Select(DefaultItem);
            UIKit.FreeCursor();
        }

        /// "BEST m:ss · MEDALS n/24": total of the best room times; medals count every tier earned (BRONZE 1 .. SYNC 4 per room).
        internal static string GoalsLine()
        {
            var save = Desync.Net.SaveService.Data;
            int medals = 0;
            foreach (var v in save.medals.Values) medals += v == "SYNC" ? 4 : v == "G" ? 3 : v == "S" ? 2 : v == "B" ? 1 : 0;
            int t = (int)Desync.Net.SaveService.TotalBestTime();
            string best = t > 0 ? (t / 60) + ":" + (t % 60).ToString("00") : "--:--";
            return "BEST " + best + " · MEDALS " + medals + "/" + SceneFlow.RoomCount * 4;
        }

        public override void Hide()
        {
            UIKit.Kill(ref _pulse); UIKit.Kill(ref _clickTw);
            base.Hide();
        }

        void Tint()
        {
            var c = _shownSource ? UIKit.Green : UIKit.Cyan;
            _tag.color = c;
            _caption.color = _shownSource ? new Color(UIKit.Green.r, UIKit.Green.g, UIKit.Green.b, 0.7f) : UIKit.Dim;
        }

        public override void Tick(float dt)
        {
            if (_started) return;
            UIKit.FreeCursor();
            if (UIKit.Confirm.WasPressedThisFrame()) { Start0(); return; }   // Space / Enter / pad South / pad Start (click path stays)
            int step = UIKit.NavStep;
            if (step != 0) Select(((_sel + step) % DailyItem + DailyItem) % DailyItem);   // DAILY is not selectable yet
            Orbit(dt);
            _flipT -= dt;
            if (_flipT <= 0f)
            {
                _flipT = FlipEvery;
                LayerState.Set(!LayerState.InSource);   // state only: no Flipped event (no SFX / pulse before the click)
                _logo.Glitch(1.2f, 0.3f);
            }
            if (LayerState.InSource != _shownSource) { _shownSource = LayerState.InSource; Tint(); }

            if (_introDelay > 0f) { _introDelay -= dt; return; }
            _introChars += dt * IntroCps;
            int want = (int)_introChars;
            if (want != _introShown) { _introShown = want; _intro.maxVisibleCharacters = want; }
        }

        void Orbit(float dt)
        {
            if (!_cam)
            {
                _cam = Camera.main;
                if (!_cam) return;
                var p = _cam.transform.position;
                _radius = new Vector2(p.x, p.z).magnitude; if (_radius < 2f) _radius = 9f;
                _height = p.y > 0.5f ? p.y : 3.5f;
                _angle = Mathf.Atan2(p.z, p.x);
            }
            _angle += OrbitDegPerSec * Mathf.Deg2Rad * dt;
            var pos = new Vector3(Mathf.Cos(_angle) * _radius, _height, Mathf.Sin(_angle) * _radius);
            var target = new Vector3(0f, Mathf.Min(1.6f, _height * 0.5f), 0f);
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos, Vector3.up));
        }
    }
}
