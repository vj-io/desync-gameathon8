using System;
using System.Collections.Generic;
using Desync.Enemies;
using Desync.Flow;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Desync.UI
{
    /// The ARCHITECT's ending sequence, played over the arena between the boss's last moment and its room results: Exposed,
    /// Merged and the true ending Desync each have their own script (Content/shards.json "endings", Flow's EndingScript).
    /// Like STORY: typewriter captions (Orbitron headlines, VT323 system lines), RENDER/SOURCE flips on the words, the logo, a
    /// closing prompt; and motion: the camera turns to the face and pushes in, Desync splits the screen into both layers
    /// (_Desync). ESC or holding SPACE skips; SPACE / click on the prompt continues to the results. Camera, layer and split are
    /// put back when it ends. UIManager arms it on BossEvents.Ending and asks it first when the room's report arrives.
    /// No allocation per frame: strings come from the script, typewriters only move maxVisibleCharacters.
    public sealed class EndingSequence : UIPanel
    {
        const float HoldToSkip = 0.8f, Letterbox = 110f, HeadCps = 60f, SysCps = 45f;

        static readonly int DesyncId = Shader.PropertyToID("_Desync");

        GlitchLabel _head, _logo;
        TMP_Text _sys, _wake, _skip;
        const string SkipHint = "ESC SKIP  ·  HOLD SPACE";
        Image _flash, _skipFill;
        RectTransform _barTop, _barBot;
        Tween _flashTw, _headTw, _logoTw, _wakeTw;
        AudioSource _sfx;

        List<EndingBeat> _beats;
        Action _done;
        string _armed; int _armedFrame = -1;
        float _t, _hold, _headUntil, _sysUntil, _headN, _sysN;
        int _beat, _headLen, _sysLen;
        bool _headOn, _sysOn, _waking, _skipArmed, _split;

        // camera: the head pivot's pose before the sequence, and the running turn / push
        Transform _cam; Vector3 _pos0; Quaternion _rot0;
        Quaternion _turnFrom, _turnTo; float _turnT, _turnDur = -1f;
        Vector3 _pushFrom, _pushTo; float _pushT, _pushDur = -1f;

        internal string Ending { get; private set; }
        internal float Clock => _t;
        internal bool Waking => _waking;

        protected override void Create()
        {
            var block = UIKit.Img(Root, "Block", new Color(0f, 0f, 0f, 0f)); UIKit.Stretch(block.rectTransform);
            block.raycastTarget = true;   // the results and HUD underneath never see a click
            var vig = UIKit.Img(Root, "Vignette", new Color(0f, 0f, 0.02f, 0.8f), UIKit.Vignette); UIKit.Stretch(vig.rectTransform);
            _flash = UIKit.Img(Root, "Flash", new Color(1f, 1f, 1f, 0f)); UIKit.Stretch(_flash.rectTransform);
            _barTop = Bar("BarTop", 1f); _barBot = Bar("BarBottom", 0f);

            _head = GlitchLabel.Create(Root, "Head", Anc.C, new Vector2(0, 170), new Vector2(1820, 300), UIKit.Title, 76, UIKit.Paper, TextAlignmentOptions.Center, 4f, true, 2f);
            _head.Main.textWrappingMode = _head.A.textWrappingMode = _head.B.textWrappingMode = TextWrappingModes.Normal;
            _sys = UIKit.Text(Root, "Sys", UIKit.Term, 56, UIKit.Green, TextAlignmentOptions.Center);
            _sys.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_sys.rectTransform, Anc.C, new Vector2(0, -300), new Vector2(1600, 120));
            _logo = GlitchLabel.Create(Root, "Logo", Anc.C, new Vector2(0, 40), new Vector2(1600, 260), UIKit.Title, 190, UIKit.Paper, TextAlignmentOptions.Center, 5f, true, 18f);
            _wake = UIKit.Text(Root, "Wake", UIKit.Hud, 40, UIKit.Cyan, TextAlignmentOptions.Center);
            _wake.characterSpacing = 8f; UIKit.Place(_wake.rectTransform, Anc.C, new Vector2(0, -390), new Vector2(1400, 60));

            _skip = UIKit.Text(_barBot, "Skip", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Right, SkipHint);
            _skip.characterSpacing = 4f; UIKit.Place(_skip.rectTransform, Anc.R, new Vector2(-48, 8), new Vector2(800, 34));
            var track = UIKit.Img(_barBot, "SkipTrack", new Color(1f, 1f, 1f, 0.12f));
            UIKit.Place(track.rectTransform, Anc.R, new Vector2(-48, -20), new Vector2(300, 4));
            _skipFill = UIKit.Img(track.transform, "Fill", UIKit.Cyan); UIKit.Stretch(_skipFill.rectTransform);
            _skipFill.type = Image.Type.Filled; _skipFill.fillMethod = Image.FillMethod.Horizontal; _skipFill.fillAmount = 0f;
        }

        RectTransform Bar(string name, float y)
        {
            var rt = UIKit.Img(Root, name, UIKit.Ink).rectTransform;
            rt.anchorMin = new Vector2(0f, y); rt.anchorMax = new Vector2(1f, y); rt.pivot = new Vector2(0.5f, y);
            rt.sizeDelta = new Vector2(0f, Letterbox); rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// BossEvents.Ending: the room report raised in this same frame (the boss raises ExitReached right after) plays it first.
        public void Arm(string ending) { _armed = ending; _armedFrame = Time.frameCount; }

        /// Plays the armed ending, then `done`; false (nothing shown) when none is armed for this frame or it has no script.
        public bool PlayArmed(Action done)
        {
            bool armed = _armed != null && _armedFrame == Time.frameCount;
            string e = _armed;
            _armed = null;
            return armed && Play(e, done);
        }

        /// Plays `ending` ("Exposed", "Merged", "Desync", any case), then `done`; false when the script has no such ending.
        public bool Play(string ending, Action done)
        {
            var script = ShardBook.Content.Ending(ending);
            if (script == null || script.beats.Count == 0) return false;
            if (IsOn) Finish(false);
            _beats = script.beats;
            _beats.Sort((a, b) => a.t.CompareTo(b.t));
            Ending = script.id;
            _done = done;
            Show();
            _t = _hold = 0f; _beat = 0; _waking = _skipArmed = _split = false;
            _headOn = _sysOn = false; _head.Show(false); _sys.gameObject.SetActive(false); _logo.Show(false);
            UIKit.Kill(ref _wakeTw); _wake.gameObject.SetActive(false); _skip.gameObject.SetActive(true); _skipFill.fillAmount = 0f;
            _skip.SetText(Rebinds.FlipText(SkipHint));   // the hold is the flip key, as bound now
            UIKit.Kill(ref _flashTw); _flash.color = new Color(1f, 1f, 1f, 0f);
            Tween.ScaleY(_barTop, 0f, 1f, 0.5f, Ease.OutCubic, useUnscaledTime: true);
            Tween.ScaleY(_barBot, 0f, 1f, 0.5f, Ease.OutCubic, useUnscaledTime: true);
            _sfx = Root.GetComponentInParent<AudioSource>();
            _cam = PlayerRef.Head;
            if (_cam) _cam.GetLocalPositionAndRotation(out _pos0, out _rot0);
            _turnDur = _pushDur = -1f;
            UIKit.FreeCursor();
            return true;
        }

        /// Left by a mode change: everything put back, no `done`.
        public override void Hide()
        {
            if (IsOn) Restore();
            UIKit.Kill(ref _wakeTw); UIKit.Kill(ref _flashTw); UIKit.Kill(ref _headTw); UIKit.Kill(ref _logoTw);
            _done = null;
            base.Hide();
        }

        internal void Finish(bool callDone = true)
        {
            var done = _done;
            _done = null;
            Hide();
            if (callDone) done?.Invoke();
        }

        void Restore()
        {
            if (_split) { Shader.SetGlobalFloat(DesyncId, 0f); _split = false; }
            if (LayerState.InSource) LayerState.Set(false);
            if (_cam) _cam.SetLocalPositionAndRotation(_pos0, _rot0);
            _cam = null;
            if (_sfx) _sfx.pitch = 1f;
        }

        public override void Tick(float dt)
        {
            UIKit.FreeCursor();
            if (GameInput.Pause.WasPressedThisFrame()) { Finish(); return; }
            if (_waking)
            {
                var m = Mouse.current;
                if (UIKit.Confirm.WasPressedThisFrame() || (m != null && m.leftButton.wasPressedThisFrame)) { Finish(); return; }
            }
            else
            {
                if (GameInput.Flip.WasPressedThisFrame()) _skipArmed = true;   // a hold counts only when it began during the sequence
                _hold = _skipArmed && GameInput.Flip.IsPressed() ? _hold + dt : 0f;
                if (_hold >= HoldToSkip) { Finish(); return; }
                float fill = _hold / HoldToSkip;
                if (_skipFill.fillAmount != fill) _skipFill.fillAmount = fill;
            }

            _t += dt;
            while (_beat < _beats.Count && _beats[_beat].t <= _t) Fire(_beats[_beat++]);
            if (_headOn && _t >= _headUntil) { _headOn = false; _head.Show(false); }
            if (_sysOn && _t >= _sysUntil) { _sysOn = false; _sys.gameObject.SetActive(false); }
            Type(ref _headN, HeadCps, _headLen, dt, _head.Main, _head.A, _head.B);
            Type(ref _sysN, SysCps, _sysLen, dt, _sys, null, null);
            Move(dt);
        }

        static void Type(ref float n, float cps, int len, float dt, TMP_Text a, TMP_Text b, TMP_Text c)
        {
            if (n >= len) return;
            int was = (int)n;
            n = Mathf.Min(len, n + dt * cps);
            int k = (int)n;
            if (k == was) return;
            a.maxVisibleCharacters = k;
            if (b) b.maxVisibleCharacters = k;
            if (c) c.maxVisibleCharacters = k;
        }

        void Fire(EndingBeat b)
        {
            switch (b.kind)
            {
                case "head":
                    _head.SetText(UIKit.Safe(b.text, UIKit.Title)); _head.SetColor(Col(b.color)); _head.Show(true);
                    _headLen = b.text.Length; _headN = 0f; _head.Main.maxVisibleCharacters = _head.A.maxVisibleCharacters = _head.B.maxVisibleCharacters = 0;
                    _head.Glitch(1.2f, 0.25f); UIKit.Pop(ref _headTw, _head.transform, 1.12f, 0.25f, Ease.OutCubic);
                    _headOn = true; _headUntil = b.t + b.hold;
                    break;
                case "sys":
                    _sys.SetText(UIKit.Safe(b.text, UIKit.Term)); _sys.color = Col(b.color); _sys.gameObject.SetActive(true);
                    _sysLen = b.text.Length; _sysN = 0f; _sys.maxVisibleCharacters = 0;
                    _sysOn = true; _sysUntil = b.t + b.hold;
                    break;
                case "logo":
                    _headOn = false; _head.Show(false);
                    _logo.SetColor(Col(b.color)); _logo.Show(true); _logo.Reveal(UIKit.Safe(b.text, UIKit.Title), 0.9f); _logo.Glitch(2.5f, 0.5f);
                    UIKit.Pop(ref _logoTw, _logo.transform, 1.3f, 0.4f, Ease.OutCubic);
                    break;
                case "layer":
                    LayerState.Set(b.side == "source");
                    Flash(b.side == "source" ? UIKit.Green : UIKit.Cyan);
                    Sfx(b.side == "source" ? "sfx.flip.source" : "sfx.flip.render");
                    break;
                case "split":
                    _split = b.value > 0.5f;
                    Shader.SetGlobalFloat(DesyncId, _split ? 1f : 0f);
                    break;
                case "flash": Flash(Col(b.color)); break;
                case "face": Turn(b.dur); break;
                case "push": Push(b.value, b.dur); break;
                case "wake":
                    _waking = true; _skip.gameObject.SetActive(false);
                    _wake.SetText(UIKit.Safe(b.text, UIKit.Hud)); _wake.gameObject.SetActive(true);
                    UIKit.Kill(ref _wakeTw);
                    _wakeTw = Tween.Alpha(_wake, 1f, 0.35f, 0.7f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
                    break;
            }
            if (!string.IsNullOrEmpty(b.sfx)) Sfx(b.sfx);
        }

        /// Turn the camera to the ARCHITECT's eye over `dur` s (nothing when there is no boss or no camera).
        void Turn(float dur)
        {
            var boss = ArchitectBoss.Current;
            if (!_cam || !boss || !boss.eye) return;
            Vector3 d = boss.eye.position - _cam.position;
            if (d.sqrMagnitude < 1e-4f) return;
            _turnFrom = _cam.rotation; _turnTo = Quaternion.LookRotation(d, Vector3.up);
            _turnT = 0f; _turnDur = Mathf.Max(0.05f, dur);
        }

        /// Move the camera `metres` along its current view over `dur` s.
        void Push(float metres, float dur)
        {
            if (!_cam) return;
            _pushFrom = _cam.position;
            _pushTo = _pushFrom + (_turnDur > 0f ? _turnTo : _cam.rotation) * Vector3.forward * metres;
            _pushT = 0f; _pushDur = Mathf.Max(0.05f, dur);
        }

        void Move(float dt)
        {
            if (!_cam) return;
            if (_turnDur > 0f)
            {
                _turnT = Mathf.Min(_turnDur, _turnT + dt);
                _cam.rotation = Quaternion.Slerp(_turnFrom, _turnTo, Mathf.SmoothStep(0f, 1f, _turnT / _turnDur));
                if (_turnT >= _turnDur) _turnDur = -1f;
            }
            if (_pushDur > 0f)
            {
                _pushT = Mathf.Min(_pushDur, _pushT + dt);
                _cam.position = Vector3.Lerp(_pushFrom, _pushTo, Mathf.SmoothStep(0f, 1f, _pushT / _pushDur));
                if (_pushT >= _pushDur) _pushDur = -1f;
            }
        }

        void Flash(Color c)
        {
            if (!UIKit.Flashes) return;
            UIKit.Kill(ref _flashTw);
            _flash.color = new Color(c.r, c.g, c.b, 0.3f);
            _flashTw = Tween.Alpha(_flash, 0f, 0.35f, Ease.OutQuad, useUnscaledTime: true);
        }

        void Sfx(string key)
        {
            var clip = _sfx ? AssetLib.Get<AudioClip>(key) : null;
            if (clip) _sfx.PlayOneShot(clip, 0.9f);
        }

        static Color Col(string c)
        {
            switch (c)
            {
                case "cyan": return UIKit.Cyan;
                case "green": return UIKit.Green;
                case "magenta": return UIKit.Magenta;
                case "danger": return UIKit.Danger;
                case "dim": return UIKit.Dim;
                default: return UIKit.Paper;
            }
        }
    }
}
