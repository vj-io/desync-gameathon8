using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Title scene overlay + attract mode (orbiting camera, backdrop flips RENDER/SOURCE every ~3 s).
    public sealed class TitleScreen : UIPanel
    {
        GlitchLabel _logo;
        TMP_Text _tag, _intro, _jackIn, _meta, _sensVal, _caption;
        Image _jackFrame;
        Slider _sens;
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

            _tag = UIKit.Text(card.transform, "Tagline", UIKit.Hud, 46, UIKit.Cyan, TextAlignmentOptions.Center, "SPACE = SEE THE CODE");
            _tag.characterSpacing = 8f; UIKit.Place(_tag.rectTransform, Anc.C, new Vector2(0, 70), new Vector2(1400, 60));

            _intro = UIKit.Text(card.transform, "Intro", UIKit.Term, 42, UIKit.Paper, TextAlignmentOptions.Top);
            _intro.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_intro.rectTransform, Anc.C, new Vector2(0, -30), new Vector2(1240, 130));

            var jb = UIKit.Img(card.transform, "JackIn", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.14f));
            UIKit.Place(jb.rectTransform, Anc.C, new Vector2(0, -175), new Vector2(980, 120));
            _jackFrame = UIKit.Img(jb.transform, "Frame", UIKit.Magenta, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(_jackFrame.rectTransform);
            _jackIn = UIKit.Text(jb.transform, "Label", UIKit.Title, 76, UIKit.Magenta, TextAlignmentOptions.Center, "CLICK TO JACK IN");
            _jackIn.fontStyle = FontStyles.Bold; _jackIn.characterSpacing = 6f; UIKit.Stretch(_jackIn.rectTransform);
            _jackPanel = jb.rectTransform;

            _meta = UIKit.Text(card.transform, "Meta", UIKit.Hud, 30, UIKit.Dim, TextAlignmentOptions.Center, "~8 min · 6 rooms");
            _meta.characterSpacing = 4f; UIKit.Place(_meta.rectTransform, Anc.C, new Vector2(0, -272), new Vector2(900, 40));

            var sl = UIKit.Text(card.transform, "SensLabel", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Right, "SENSITIVITY");
            UIKit.Place(sl.rectTransform, Anc.C, new Vector2(-390, -330), new Vector2(260, 36));
            _sens = UIControls.NewSlider(card.transform, "SensSlider", Anc.C, new Vector2(0, -330), 420, 0.1f, 3f, Settings.Sensitivity, OnSens);
            _sensVal = UIKit.Text(card.transform, "SensValue", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left);
            UIKit.Place(_sensVal.rectTransform, Anc.C, new Vector2(300, -330), new Vector2(140, 36));
            _sensVal.SetText("{0:0.00}", Settings.Sensitivity);
        }

        RectTransform _jackPanel;

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

        void Start0()
        {
            if (_started) return;
            _started = true;
            _jackIn.SetText("JACKING IN...");
            _logo.Glitch(2.2f, 0.4f);
            UIKit.Pop(ref _clickTw, _jackPanel, 0.85f, 0.25f);
            GameEvents.StartRequested?.Invoke(0);
        }

        public override void Show()
        {
            base.Show();
            _started = false; _cam = null;
            _jackIn.SetText("CLICK TO JACK IN");
            var cfg = GameConfig.I;
            _intro.SetText(cfg.introLines != null && cfg.introLines.Length > 0 ? cfg.introLines[0] : "");
            _intro.maxVisibleCharacters = 0; _introChars = 0; _introShown = 0; _introDelay = 0.9f;
            _logo.Reveal("DESYNC", 0.9f);
            _flipT = FlipEvery;
            LayerState.Set(false);
            _shownSource = false; Tint();
            UIKit.Kill(ref _pulse);
            _jackPanel.localScale = Vector3.one;
            _pulse = Tween.Scale(_jackPanel, 1f, 1.045f, 0.55f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
            FreeCursor();
        }

        public override void Hide()
        {
            UIKit.Kill(ref _pulse); UIKit.Kill(ref _clickTw);
            base.Hide();
        }

        static void FreeCursor()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
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
            FreeCursor();
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
