using Desync.Net;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Desync.UI
{
    /// STORY: the ~80 s in-engine cinematic, played in the Title scene's office. No room is loaded for it: an additive Room_01 / Room_06
    /// would bring its player, RoomInfo and live enemies along. StoryScript drives everything: eased camera moves (PrimeTween), typewriter
    /// captions (Orbitron headlines, VT323 system lines), RENDER/SOURCE flips on the words (LayerState: world shader, glyph rain, music
    /// stems), ECHO's ID card, the RENDER/SOURCE panels, the goal list, the logo, then "PRESS SPACE TO WAKE UP" -> StartRequested(0)
    /// with the Room 1 guide armed. ESC (pad Start) or holding SPACE (pad A) skips to Room 1 at any time. Beats sit on the 120 bpm
    /// grid and the first one waits for the music's next beat, so cuts land on the music. REDUCE FLASHING / REDUCE FX drop the flash
    /// overlay and the SOURCE flicker. No allocation per frame: strings come from the script, typewriters only move
    /// maxVisibleCharacters, the camera tween uses a non-capturing callback.
    public sealed class StoryScreen : UIPanel
    {
        const float HoldToSkip = 0.8f, Letterbox = 110f, FlickerTime = 0.14f;

        /// Typewriter over one to three stacked labels (a GlitchLabel's main text and ghosts).
        sealed class Typer
        {
            readonly TMP_Text _a, _b, _c; float _n, _cps; int _shown, _len;
            public Typer(TMP_Text a, TMP_Text b = null, TMP_Text c = null) { _a = a; _b = b; _c = c; }
            public void Start(int len, float cps) { _len = len; _cps = cps; _n = 0f; _shown = -1; Tick(0f); }
            public void Tick(float dt)
            {
                if (_shown >= _len) return;
                _n += dt * _cps;
                int k = Mathf.Min(_len, (int)_n);
                if (k == _shown) return;
                _shown = k; _a.maxVisibleCharacters = k;
                if (_b) _b.maxVisibleCharacters = k;
                if (_c) _c.maxVisibleCharacters = k;
            }
        }

        sealed class Panel { public RectTransform Rt; public TMP_Text Title, Body; public Typer Ty; public Tween Tw; public float Until; public bool On; }

        AudioSource _sfx;
        StoryScript _s;
        Image _flash, _skipFill; RectTransform _barTop, _barBot, _card;
        GlitchLabel _head, _logo; TMP_Text _sys, _wake, _skip, _cardName, _cardInfo;
        Panel _render, _source; readonly TMP_Text[] _goals = new TMP_Text[4];
        Typer _headTy, _sysTy, _cardTy;
        Tween _headTw, _cardTw, _goalTw, _logoTw, _wakeTw, _flashTw, _camTw, _topTw, _botTw;
        float _t, _hold, _flicker, _headUntil, _sysUntil, _cardUntil, _goalsUntil;
        bool _headOn, _sysOn, _cardOn, _goalsOn, _done, _waking, _armed, _pulse;
        int _beat, _shot, _goalN;
        Camera _cam; Vector3 _camPos0; Quaternion _camRot0; Desync.Art.GlyphRain _rain;
        Vector3 _from, _to, _lookFrom, _lookTo, _look;

        internal float Clock => _t;
        internal bool Finished => _done;
        internal bool Waking => _waking;
        internal int BeatIndex => _beat;
        internal float FlashAlpha => _flash.color.a;

        /// UIManager's shared 2D AudioSource (the cues are the game's own SFX from AssetLib).
        public void Init(AudioSource sfx) { _sfx = sfx; }

        protected override void Create()
        {
            var block = UIKit.Img(Root, "Block", new Color(0f, 0f, 0f, 0f)); UIKit.Stretch(block.rectTransform);
            block.raycastTarget = true;   // the title's click-anywhere catcher underneath never sees a click
            var vig = UIKit.Img(Root, "Vignette", new Color(0f, 0f, 0.02f, 0.85f), UIKit.Vignette); UIKit.Stretch(vig.rectTransform);
            _flash = UIKit.Img(Root, "Flash", new Color(1f, 1f, 1f, 0f)); UIKit.Stretch(_flash.rectTransform);
            _barTop = Bar("BarTop", 1f); _barBot = Bar("BarBottom", 0f);

            _head = GlitchLabel.Create(Root, "Head", Anc.C, new Vector2(0, 170), new Vector2(1820, 300), UIKit.Title, 80, UIKit.Paper, TextAlignmentOptions.Center, 4f, true, 2f);
            _head.Main.textWrappingMode = _head.A.textWrappingMode = _head.B.textWrappingMode = TextWrappingModes.Normal;
            _headTy = new Typer(_head.Main, _head.A, _head.B);

            _sys = UIKit.Text(Root, "Sys", UIKit.Term, 56, UIKit.Green, TextAlignmentOptions.Center);
            _sys.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_sys.rectTransform, Anc.C, new Vector2(0, -300), new Vector2(1600, 120));
            _sysTy = new Typer(_sys);

            // ECHO's ID card
            var card = UIKit.Box(Root, "Card", UIKit.Cyan, 0.92f);
            _card = UIKit.Place(card.rectTransform, Anc.C, new Vector2(0, -120), new Vector2(1240, 250));   // under a two-line headline
            var hdr = UIKit.Text(_card, "Header", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Left, "HALCYON CORPORATION // PERSONNEL FILE");
            hdr.characterSpacing = 6f; UIKit.Place(hdr.rectTransform, Anc.TL, new Vector2(250, -18), new Vector2(960, 30));
            var photo = UIKit.Img(_card, "Photo", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.12f));
            UIKit.Place(photo.rectTransform, Anc.L, new Vector2(30, 0), new Vector2(190, 200));
            var pf = UIKit.Img(photo.transform, "Frame", UIKit.Cyan, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(pf.rectTransform);
            var ghost = new Color(UIKit.Paper.r, UIKit.Paper.g, UIKit.Paper.b, 0.55f);
            UIKit.Place(UIKit.Img(photo.transform, "Head", ghost, UIKit.Disc).rectTransform, Anc.C, new Vector2(0, 30), new Vector2(76, 76));
            UIKit.Place(UIKit.Img(photo.transform, "Shoulders", ghost).rectTransform, Anc.B, new Vector2(0, 14), new Vector2(130, 62));
            _cardName = UIKit.Text(_card, "Name", UIKit.Title, 96, UIKit.Paper, TextAlignmentOptions.Left);
            _cardName.fontStyle = FontStyles.Bold; UIKit.Place(_cardName.rectTransform, Anc.L, new Vector2(250, 22), new Vector2(960, 110));
            _cardInfo = UIKit.Text(_card, "Info", UIKit.Term, 40, UIKit.Cyan, TextAlignmentOptions.Left);
            UIKit.Place(_cardInfo.rectTransform, Anc.L, new Vector2(250, -64), new Vector2(970, 50));
            _cardTy = new Typer(_cardInfo);

            _render = MakePanel("Render", UIKit.Cyan, -470f);
            _source = MakePanel("Source", UIKit.Green, 470f);

            for (int i = 0; i < _goals.Length; i++)
            {
                _goals[i] = UIKit.Text(Root, "Goal" + i, UIKit.Title, 56, UIKit.Paper, TextAlignmentOptions.Center);
                _goals[i].fontStyle = FontStyles.Bold; _goals[i].characterSpacing = 4f;
                UIKit.Place(_goals[i].rectTransform, Anc.C, new Vector2(0, 160 - i * 92), new Vector2(1600, 84));
            }

            _logo = GlitchLabel.Create(Root, "Logo", Anc.C, new Vector2(0, 80), new Vector2(1400, 260), UIKit.Title, 210, UIKit.Paper, TextAlignmentOptions.Center, 5f, true, 18f);
            _wake = UIKit.Text(Root, "Wake", UIKit.Hud, 44, UIKit.Cyan, TextAlignmentOptions.Center);
            _wake.characterSpacing = 8f; UIKit.Place(_wake.rectTransform, Anc.C, new Vector2(0, -390), new Vector2(1400, 60));

            _skip = UIKit.Text(_barBot, "Skip", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Right);
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

        Panel MakePanel(string name, Color c, float x)
        {
            var p = new Panel { Rt = UIKit.Place(UIKit.Box(Root, name, c, 0.88f).rectTransform, Anc.C, new Vector2(x, -40), new Vector2(820, 250)) };
            p.Title = UIKit.Text(p.Rt, "Title", UIKit.Title, 50, c, TextAlignmentOptions.Center); p.Title.fontStyle = FontStyles.Bold;
            UIKit.Place(p.Title.rectTransform, Anc.T, new Vector2(0, -26), new Vector2(780, 64));
            p.Body = UIKit.Text(p.Rt, "Body", UIKit.Term, 48, UIKit.Paper, TextAlignmentOptions.Top);
            p.Body.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(p.Body.rectTransform, Anc.T, new Vector2(0, -104), new Vector2(740, 130));
            p.Ty = new Typer(p.Body);
            return p;
        }

        /// Starts the cinematic from the top (title menu STORY, or WATCH on the first-time prompt).
        public void Play()
        {
            Show();
            _s = StoryScript.I;
            _beat = _shot = _goalN = 0; _done = _waking = _armed = _pulse = false; _hold = _flicker = 0f;
            HideCaptions();
            _logo.Show(false);
            UIKit.Kill(ref _wakeTw); _wake.gameObject.SetActive(false);
            _skip.SetText(UIKit.Safe(_s.skipHint, UIKit.Hud)); _skip.gameObject.SetActive(true); _skipFill.fillAmount = 0f;
            UIKit.Kill(ref _flashTw); _flash.color = new Color(1f, 1f, 1f, 0f);
            UIKit.Kill(ref _topTw); UIKit.Kill(ref _botTw);
            _topTw = Tween.ScaleY(_barTop, 0f, 1f, 0.5f, Ease.OutCubic, useUnscaledTime: true);
            _botTw = Tween.ScaleY(_barBot, 0f, 1f, 0.5f, Ease.OutCubic, useUnscaledTime: true);
            UIKit.FreeCursor();
            LayerState.Set(false);
            _cam = Camera.main;
            if (_cam)
            {
                _cam.transform.GetPositionAndRotation(out _camPos0, out _camRot0);
                if (!_rain) _rain = Desync.Art.GlyphRain.Attach(_cam);   // the player camera has its own; the title camera needs one
            }
            _t = -BeatLead();
            if (_s.beats.Length == 0) Finish();
        }

        /// Left without waking up (a mode change): the title gets its layer, camera and post-FX back.
        public override void Hide()
        {
            if (IsOn && !_done)
            {
                Cleanup();
                if (_cam) _cam.transform.SetPositionAndRotation(_camPos0, _camRot0);
            }
            UIKit.Kill(ref _wakeTw); UIKit.Kill(ref _topTw); UIKit.Kill(ref _botTw);
            base.Hide();
        }

        /// Skip (ESC / held SPACE) or wake up: straight into Room 1, story guide armed. Black until the room is in.
        internal void Finish()
        {
            if (_done) return;
            _done = true; _waking = false;
            Cleanup();
            HideCaptions();
            _logo.Show(false); _wake.gameObject.SetActive(false); _skip.gameObject.SetActive(false);
            _flash.color = UIKit.Ink;
            StoryGuide.Armed = true;
            GameEvents.StartRequested?.Invoke(0);
        }

        void Cleanup()
        {
            UIKit.Kill(ref _camTw); UIKit.Kill(ref _flashTw); UIKit.Kill(ref _headTw); UIKit.Kill(ref _cardTw); UIKit.Kill(ref _goalTw);
            UIKit.Kill(ref _logoTw); UIKit.Kill(ref _render.Tw); UIKit.Kill(ref _source.Tw);
            _flicker = 0f;
            if (LayerState.InSource) LayerState.Set(false);
            if (_pulse) { GameState.Pulse = 0f; _pulse = false; }
            if (_sfx) _sfx.pitch = 1f;
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            UIKit.FreeCursor();
            if (GameInput.Pause.WasPressedThisFrame()) { Finish(); return; }
            if (_waking)
            {
                var m = Mouse.current;
                if (UIKit.Confirm.WasPressedThisFrame() || (m != null && m.leftButton.wasPressedThisFrame)) { Finish(); return; }
            }
            else
            {
                if (GameInput.Flip.WasPressedThisFrame()) _armed = true;   // a hold counts only when it began during the story
                _hold = _armed && GameInput.Flip.IsPressed() ? _hold + dt : 0f;
                if (_hold >= HoldToSkip) { Finish(); return; }
                float fill = _hold / HoldToSkip;
                if (_skipFill.fillAmount != fill) _skipFill.fillAmount = fill;
            }

            _t += dt;
            var beats = _s.beats;
            while (_beat < beats.Length && beats[_beat].t <= _t) Fire(beats[_beat++]);
            var shots = _s.shots;
            while (_shot < shots.Length && shots[_shot].t <= _t) Shot(shots[_shot++]);

            if (_headOn && _t >= _headUntil) { _headOn = false; _head.Show(false); }
            if (_sysOn && _t >= _sysUntil) { _sysOn = false; _sys.gameObject.SetActive(false); }
            if (_cardOn && _t >= _cardUntil) { _cardOn = false; _card.gameObject.SetActive(false); }
            Expire(_render); Expire(_source);
            if (_goalsOn && _t >= _goalsUntil) HideGoals();

            _headTy.Tick(dt); _sysTy.Tick(dt); _cardTy.Tick(dt); _render.Ty.Tick(dt); _source.Ty.Tick(dt);
            if (_flicker > 0f && (_flicker -= dt) <= 0f) LayerState.Set(false);
            if (_pulse)   // the post-FX flip pulse; FlipController owns it in rooms, nobody does on the title
            {
                GameState.Pulse = Mathf.Max(0f, GameState.Pulse - dt / Mathf.Max(0.01f, GameConfig.I.flipPulse));
                _pulse = GameState.Pulse > 0f;
            }
        }

        void Fire(StoryBeat b)
        {
            switch (b.kind)
            {
                case "head":
                    _head.SetText(UIKit.Safe(b.text, UIKit.Title)); _head.SetColor(Col(b.color)); _head.Show(true);
                    _headTy.Start(b.text.Length, StoryScript.HeadCps);
                    _head.Glitch(1.2f, 0.25f); UIKit.Pop(ref _headTw, _head.transform, 1.12f, 0.25f, Ease.OutCubic);
                    _headOn = true; _headUntil = b.t + b.hold;
                    break;
                case "sys":
                    _sys.SetText(UIKit.Safe(b.text, UIKit.Term)); _sys.color = Col(b.color); _sys.gameObject.SetActive(true);
                    _sysTy.Start(b.text.Length, StoryScript.SysCps);
                    _sysOn = true; _sysUntil = b.t + b.hold;
                    break;
                case "card":
                    _cardName.SetText(UIKit.Safe(b.text, UIKit.Title)); _cardInfo.SetText(UIKit.Safe(b.sub, UIKit.Term));
                    _card.gameObject.SetActive(true); _cardTy.Start(b.sub.Length, StoryScript.BodyCps);
                    UIKit.Pop(ref _cardTw, _card, 0.7f, 0.35f);
                    _cardOn = true; _cardUntil = b.t + b.hold;
                    break;
                case "panel":
                    var p = b.side == "source" ? _source : _render;
                    p.Title.SetText(UIKit.Safe(b.text, UIKit.Title)); p.Body.SetText(UIKit.Safe(b.sub, UIKit.Term));
                    p.Rt.gameObject.SetActive(true); p.Ty.Start(b.sub.Length, StoryScript.BodyCps);
                    UIKit.Pop(ref p.Tw, p.Rt, 0.8f, 0.3f);
                    p.On = true; p.Until = b.t + b.hold;
                    break;
                case "goal":
                    if (_goalN >= _goals.Length) break;
                    var g = _goals[_goalN++];
                    g.SetText(UIKit.Safe(b.text, UIKit.Title)); g.gameObject.SetActive(true);
                    UIKit.Pop(ref _goalTw, g.transform, 1.4f, 0.25f, Ease.OutCubic);
                    _goalsOn = true; _goalsUntil = Mathf.Max(_goalsUntil, b.t + b.hold);
                    break;
                case "layer": Layer(b.side == "source"); break;
                case "flicker":   // "And it looks back.": a split-second glimpse of SOURCE (a glitch only under REDUCE FLASHING)
                    _head.Glitch(2.5f, 0.4f);
                    if (UIKit.Flashes) { LayerState.Set(true); _flicker = FlickerTime; Flash(UIKit.Magenta); }
                    break;
                case "logo":
                    _logo.Show(true); _logo.Reveal(UIKit.Safe(b.text, UIKit.Title), 0.9f); _logo.Glitch(2.5f, 0.5f);
                    UIKit.Pop(ref _logoTw, _logo.transform, 1.3f, 0.4f, Ease.OutCubic);
                    break;
                case "clear": HideCaptions(); break;
                case "wake":
                    _waking = true; _skip.gameObject.SetActive(false);
                    _wake.SetText(UIKit.Safe(b.text, UIKit.Hud)); _wake.gameObject.SetActive(true);
                    UIKit.Kill(ref _wakeTw);
                    _wakeTw = Tween.Alpha(_wake, 1f, 0.35f, 0.7f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
                    break;
            }
            if (!string.IsNullOrEmpty(b.sfx)) Sfx(b.sfx, b.pitch, b.vol);
        }

        void Layer(bool source)
        {
            _flicker = 0f;
            LayerState.Set(source);
            GameState.Pulse = 1f; _pulse = true;
            Flash(source ? UIKit.Green : UIKit.Cyan);
            Sfx(source ? "sfx.flip.source" : "sfx.flip.render", 1f, 0f);
        }

        void Flash(Color c)
        {
            if (!UIKit.Flashes) return;
            UIKit.Kill(ref _flashTw);
            _flash.color = new Color(c.r, c.g, c.b, 0.3f);
            _flashTw = Tween.Alpha(_flash, 0f, 0.35f, Ease.OutQuad, useUnscaledTime: true);
        }

        void Sfx(string key, float pitch, float vol)
        {
            var clip = _sfx ? AssetLib.Get<AudioClip>(key) : null;
            if (!clip) return;
            _sfx.pitch = pitch > 0f ? pitch : 1f;
            _sfx.PlayOneShot(clip, vol > 0f ? vol : 0.9f);
        }

        void Shot(StoryShot s)
        {
            if (!_cam) return;
            _from = s.join ? _cam.transform.position : s.from; _lookFrom = s.join ? _look : s.look;
            _to = s.to; _lookTo = s.lookTo;
            UIKit.Kill(ref _camTw);
            Pose(0f);
            _camTw = Tween.Custom(this, 0f, 1f, Mathf.Max(0.05f, s.dur), (me, v) => me.Pose(v), Ease.InOutSine, useUnscaledTime: true);
        }

        void Pose(float v)
        {
            if (!_cam) return;
            var p = Vector3.LerpUnclamped(_from, _to, v);
            _look = Vector3.LerpUnclamped(_lookFrom, _lookTo, v);
            var d = _look - p;
            if (d.sqrMagnitude > 1e-6f) _cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(d, Vector3.up));
        }

        void Expire(Panel p) { if (p.On && _t >= p.Until) { p.On = false; p.Rt.gameObject.SetActive(false); } }

        void HideCaptions()
        {
            _headOn = _sysOn = _cardOn = false;
            _head.Show(false); _sys.gameObject.SetActive(false); _card.gameObject.SetActive(false);
            _render.On = _source.On = false; _render.Rt.gameObject.SetActive(false); _source.Rt.gameObject.SetActive(false);
            HideGoals();
        }

        void HideGoals()
        {
            _goalsOn = false; _goalsUntil = 0f; _goalN = 0;
            for (int i = 0; i < _goals.Length; i++) _goals[i].gameObject.SetActive(false);
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

        /// Seconds until the music's next beat (GameConfig.bpm), so the beats, authored on that grid, cut on the music. 0 when the
        /// RENDER stem is not playing (silent page, tests).
        // Known shortcut: finds AudioManager's stem by its object name; a public music-clock hook in Audio would replace this lookup.
        static float BeatLead()
        {
            var go = GameObject.Find("music.Render");
            var src = go ? go.GetComponent<AudioSource>() : null;
            if (!src || !src.isPlaying) return 0f;
            float beat = 60f / Mathf.Max(30f, GameConfig.I.bpm);
            return beat - Mathf.Repeat(src.time, beat);
        }
    }

    /// First-time prompt over the title: "FIRST TIME IN THE SIMULATION?" [WATCH THE STORY] (default) / [SKIP · JUST PLAY]. Either answer
    /// is remembered in the save (settings["storySeen"] = "1"), so it asks once per player; the title menu's STORY entry replays the
    /// story any time. SKIP leaves the player on the title menu, the normal (unchanged) way into the game. Mouse (hover + click),
    /// arrows / WASD / d-pad to choose, SPACE / Enter / pad A to confirm.
    public sealed class StoryAsk : UIPanel
    {
        public const string SeenKey = "storySeen";
        public System.Action OnWatch;
        GlitchLabel _title;
        readonly UIButton[] _b = new UIButton[2];
        int _sel; Tween _pulse;
        internal int Selected => _sel;

        protected override void Create()
        {
            var s = StoryScript.I.ask;
            var scrim = UIKit.Img(Root, "Scrim", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.8f));
            UIKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;   // the title underneath gets no clicks
            var box = UIKit.Box(Root, "Box", UIKit.Magenta, 1f);
            UIKit.Place(box.rectTransform, Anc.C, Vector2.zero, new Vector2(1480, 500));
            _title = GlitchLabel.Create(box.transform, "Title", Anc.T, new Vector2(0, -50), new Vector2(1440, 110), UIKit.Title, 60, UIKit.Paper, TextAlignmentOptions.Center, 3f, true, 4f);
            var sub = UIKit.Text(box.transform, "Sub", UIKit.Term, 44, UIKit.Dim, TextAlignmentOptions.Center, UIKit.Safe(s.sub, UIKit.Term));
            UIKit.Place(sub.rectTransform, Anc.T, new Vector2(0, -172), new Vector2(1440, 56));
            for (int i = 0; i < _b.Length; i++)
            {
                int k = i;
                _b[i] = UIButton.Create(box.transform, i == 0 ? "Watch" : "Skip", UIKit.Safe(i == 0 ? s.watch : s.skip, UIKit.Title), Anc.C,
                    new Vector2(i == 0 ? -285 : 285, -60), new Vector2(520, 100), 34, i == 0 ? UIKit.Magenta : UIKit.Cyan, () => Choose(k));
                _b[i].Label.characterSpacing = 3f;
                _b[i].Hovered = () => Select(k);
            }
            var foot = UIKit.Text(box.transform, "Foot", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center, UIKit.Safe(s.foot, UIKit.Hud));
            foot.characterSpacing = 4f; UIKit.Place(foot.rectTransform, Anc.B, new Vector2(0, 30), new Vector2(1440, 32));
        }

        public override void Show()
        {
            base.Show();
            _title.Reveal(UIKit.Safe(StoryScript.I.ask.title, UIKit.Title), 0.6f); _title.Glitch(1.6f, 0.4f);
            Select(0);
            UIKit.FreeCursor();
        }

        public override void Hide()
        {
            UIKit.Kill(ref _pulse);
            for (int i = 0; i < _b.Length; i++) if (_b[i]) _b[i].transform.localScale = Vector3.one;
            base.Hide();
        }

        public override void Tick(float dt)
        {
            if (UIKit.NavStep != 0) Select(1 - _sel);
            if (UIKit.Confirm.WasPressedThisFrame()) Choose(_sel);
        }

        void Select(int i)
        {
            _sel = i;
            for (int k = 0; k < _b.Length; k++) { _b[k].SetSelected(k == i); if (k != i) _b[k].transform.localScale = Vector3.one; }
            UIKit.Kill(ref _pulse);
            _pulse = Tween.Scale(_b[i].transform, 1f, 1.05f, 0.55f, Ease.InOutSine, cycles: -1, cycleMode: CycleMode.Yoyo, useUnscaledTime: true);
        }

        void Choose(int i)
        {
            if (!IsOn) return;
            Remember();
            Hide();
            if (i == 0) OnWatch?.Invoke();
        }

        /// True until the player has answered once (and only when there is a story to watch). A save problem never blocks the title.
        public static bool ShouldAsk()
        {
            if (StoryScript.I.beats.Length == 0) return false;
            try { return !(SaveService.Data.settings.TryGetValue(SeenKey, out var v) && v == "1"); }
            catch (System.Exception e) { Debug.LogWarning("[DESYNC] story prompt: save unavailable (" + e.GetType().Name + ")"); return false; }
        }

        static void Remember()
        {
            try { SaveService.Data.settings[SeenKey] = "1"; SaveService.Save(); SaveService.Flush(); }   // flushed past the 2 s throttle: a one-off answer
            catch (System.Exception e) { Debug.LogWarning("[DESYNC] story prompt: answer not saved (" + e.GetType().Name + ")"); }
        }
    }
}
