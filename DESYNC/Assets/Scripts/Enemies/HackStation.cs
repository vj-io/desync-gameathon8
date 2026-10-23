using TMPro;
using UnityEngine;

namespace Desync.Enemies
{
    /// The arrow-sequence hack minigame shared by Terminal and HijackPad (IInteractable, prompt "E  HACK").
    /// E -> GameState.InHack, HackStarted. Correct arrow -> HackTick(step); wrong -> HackFailed, back to step 0, input locked
    /// for terminalWrongLockout. Complete -> OnHacked(). Cancel (HackCancelled): E again, death, or the player > range away.
    /// SOURCE presentation: room >= 3 shows arrows mirrored left/right; room >= 4 shows glyph noise with brief true flashes.
    /// Expected input is always the true sequence (flip to RENDER to read it). The sequence is seeded -> identical restarts.
    public abstract class HackStation : MonoBehaviour, IInteractable, IResettable
    {
        public const float CancelSlack = 1f, NoiseStep = 0.08f, FlashPeriod = 1.1f, FlashLength = 0.16f;
        const string Arrows = "↑→↓←";              // up, right, down, left
        const string Noise = "#%&@$?!/\\|<>=+*0123456789";

        public int room;
        public int seed = 1;
        public TextMeshPro screen;
        public string title = "HALCYON//SEC";

        protected bool done, hacking;
        bool _clean;   // no wrong arrow in this attempt
        int[] _seq;
        int _step, _startFrame = -1, _drawKey = int.MinValue;
        float _lockUntil = -1f;
        readonly char[] _buf = new char[640];
        int _len;

        public virtual string Prompt => "E  HACK";
        public bool CanInteract => !done && !hacking && Available && !GameState.InHack && GameState.Alive && !GameState.Cinematic;
        protected virtual bool Available => true;
        protected abstract int Length { get; }
        protected abstract void OnHacked();

        public bool Hacking => hacking;
        public bool Done => done;
        public int Step => _step;
        public int Expected(int i) => Seq[i];

        /// Arrows to enter: the station's length plus the run's HackLengthDelta, never fewer than 3.
        public int SequenceLength => Mathf.Max(3, Length + GameState.Mods.HackLengthDelta);

        int[] Seq
        {
            get
            {
                int n = SequenceLength;
                if (_seq != null && _seq.Length == n) return _seq;
                _seq = new int[n];
                uint s = (uint)seed * 2654435761u + 0x9E3779B9u;
                for (int i = 0; i < _seq.Length; i++) _seq[i] = (int)(MeshBuilder.Rand(ref s) * 4f) & 3;
                return _seq;
            }
        }

        public void Interact()
        {
            if (!CanInteract) return;
            hacking = true;
            _clean = true;
            _step = 0;
            _startFrame = Time.frameCount;
            GameState.InHack = true;
            GameEvents.HackStarted?.Invoke();
        }

        protected virtual void Update()
        {
            if (hacking)
            {
                bool again = Time.frameCount != _startFrame && GameInput.Interact.WasPressedThisFrame();
                if (again || !GameState.Alive || OutOfRange()) Cancel();
                else if (!GameState.Paused && RoomClock.Time >= _lockUntil)
                {
                    int dir = GameInput.Up.WasPressedThisFrame() ? 0 : GameInput.Right.WasPressedThisFrame() ? 1
                            : GameInput.Down.WasPressedThisFrame() ? 2 : GameInput.Left.WasPressedThisFrame() ? 3 : -1;
                    if (dir >= 0) Press(dir);
                }
            }
            Redraw(false);
        }

        /// One arrow input (0 up, 1 right, 2 down, 3 left). Public for tests / debug.
        public void Press(int dir)
        {
            if (!hacking || RoomClock.Time < _lockUntil) return;
            var seq = Seq;
            if (dir == seq[_step])
            {
                GameEvents.HackTick?.Invoke(_step);
                if (++_step < seq.Length) return;
                hacking = false;
                done = true;
                GameState.InHack = false;
                GameEvents.HackFinished?.Invoke(_clean);   // scored before OnHacked can open the exit
                OnHacked();
                return;
            }
            _clean = false;
            _step = 0;
            _lockUntil = RoomClock.Time + GameConfig.I.terminalWrongLockout;
            GameEvents.HackFailed?.Invoke();
        }

        public void Cancel()
        {
            if (!hacking) return;
            hacking = false;
            _step = 0;
            GameState.InHack = false;
            GameEvents.HackCancelled?.Invoke();
        }

        bool OutOfRange()
        {
            if (!PlayerRef.Exists) return true;
            Vector3 d = PlayerRef.Position - transform.position;
            d.y = 0f;
            float r = GameConfig.I.terminalRange + CancelSlack;
            return d.sqrMagnitude > r * r;
        }

        /// 0 = true arrows, 1 = mirrored, 2 = scrambled.
        int Mode => !LayerState.InSource ? 0 : room >= 4 ? 2 : room >= 3 ? 1 : 0;

        // ---------- screen (char buffer -> TMP, no per-frame allocations) ----------

        protected void Redraw(bool force)
        {
            if (!screen) return;
            int mode = Mode;
            int tick = mode == 2 ? Mathf.FloorToInt(RoomClock.Time / NoiseStep) : 0;
            bool flash = mode == 2 && Mathf.Repeat(RoomClock.Time, FlashPeriod) < FlashLength;
            bool locked = RoomClock.Time < _lockUntil;
            int key = (((tick * 4 + mode) * 2 + (flash ? 1 : 0)) * 2 + (locked ? 1 : 0)) * 64 + _step * 4 + (hacking ? 1 : 0) + (done ? 2 : 0);
            key = key * 2 + (StatusOverride != null ? 1 : 0);
            if (!force && key == _drawKey) return;
            _drawKey = key;

            _len = 0;
            Put("<size=55%><color=#00F0FF>"); Put(title); Put("</color></size>\n");
            if (done)
            {
                Put("<color=#00FF66>"); Put(DoneText); Put("</color>\n");
                Put("<size=50%><color=#00FF66>"); Put(DoneSub); Put("</color></size>");
            }
            else
            {
                var seq = Seq;
                for (int i = 0; i < seq.Length; i++)
                {
                    if (i > 0) Put(' ');
                    bool past = i < _step && hacking;
                    Put(past ? "<color=#00FF66>" : i == _step && hacking ? "<color=#FFFFFF>" : "<color=#8A93B8>");
                    int d = seq[i];
                    if (mode == 1) d = d == 1 ? 3 : d == 3 ? 1 : d;
                    if (mode == 2 && !flash && !past) Put(Noise[(int)((uint)(tick * 31 + i * 17 + seed) * 2654435761u >> 7) % Noise.Length]);
                    else Put(Arrows[d]);
                    Put("</color>");
                }
                Put("\n<size=50%>");
                string status = StatusOverride ?? (locked ? "<color=#FF2633>ACCESS DENIED</color>"
                    : !hacking ? "<color=#8A93B8>[E] JACK IN</color>"
                    : mode == 2 ? "<color=#00FF66>CORRUPT // FLIP</color>"
                    : mode == 1 ? "<color=#00FF66>SIGNAL MIRRORED</color>"
                    : "<color=#FF2BD6>ENTER SEQUENCE</color>");
                Put(status);
                Put("</size>");
            }
            screen.SetCharArray(_buf, 0, _len);
        }

        protected virtual string DoneText => "ACCESS GRANTED";
        protected virtual string DoneSub => "NODE BREACHED";
        protected virtual string StatusOverride => null;

        void Put(string s) { for (int i = 0; i < s.Length && _len < _buf.Length; i++) _buf[_len++] = s[i]; }
        void Put(char c) { if (_len < _buf.Length) _buf[_len++] = c; }

        protected virtual void Start() => Redraw(true);

        void OnDisable() { if (hacking) GameState.InHack = false; }

        public virtual void ResetState()
        {
            if (hacking) GameState.InHack = false;
            hacking = done = false;
            _step = 0;
            _lockUntil = -1f;
            Redraw(true);
        }
    }
}
