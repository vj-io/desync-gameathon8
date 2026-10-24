using Desync.Flow;
using Desync.Net;
using Desync.Rules;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Time-Attack select (UIManager.OpenTimeAttack): the 42 rooms by row (CLASSIC r1-r6, ACT 1-3 x 12), each tile with its
    /// medal (icon + bar), best time and next target ("0.8 s GOLD"); the focused room's detail below (dev time, medal times,
    /// best, next target, ghost). Locked until the meta unlock "unlock_timeattack" (TimeAttackRooms.Unlocked; open in
    /// development builds). One linear focus ring (tiles, BACK, START): arrows / WASD / d-pad move it, SPACE / Enter / pad A
    /// act, Esc closes; hover focuses a tile, click plays it. Dev times of generated rooms are worked out one room per
    /// frame while the screen is open (generation + validation), and on demand for the focused room.
    public sealed class TimeAttackScreen : UIPanel
    {
        const float TileW = 126f, TileH = 104f, Gap = 8f, InputGuard = 0.25f;
        static readonly string[] RowNames = { "CLASSIC", "ACT 1", "ACT 2", "ACT 3" };
        static string _lastId;   // the room played last: focused when the screen opens again

        sealed class Tile { public TimeAttackRooms.Room R; public UIButton B; public TMP_Text Best, Next; public Image Bar, Icon; }

        Tile[] _tiles;
        UIButton _back, _start;
        Image[] _ring;
        TMP_Text _summary, _name, _times, _best, _next, _ghost, _status;
        RectTransform _panel; Tween _pop, _btnTw;
        int _focus, _sel, _warm, _shownFrame = -1;
        float _t;
        bool _unlocked, _acted;

        internal int TileCount => _tiles.Length;
        internal int Focus => _focus;
        internal string SelectedId => _tiles[_sel].R.id;
        internal bool Unlocked => _unlocked;
        internal string NameText => _name.text;
        internal string NextText => _next.text;
        internal string StatusText => _status.text;
        internal string TileNext(int i) => _tiles[i].Next.text;
        internal string TileBest(int i) => _tiles[i].Best.text;
        internal int BackIdx => _tiles.Length;
        internal int StartIdx => _tiles.Length + 1;
        int Count => _tiles.Length + 2;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.9f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.8f), 0.95f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1840, 1040));
            var p = panel.transform;

            var head = UIKit.Text(p, "Head", UIKit.Title, 54, UIKit.Paper, TextAlignmentOptions.Left, "TIME-ATTACK"); head.fontStyle = FontStyles.Bold; head.characterSpacing = 8f;
            UIKit.Place(head.rectTransform, Anc.TL, new Vector2(40, -20), new Vector2(900, 70));
            var sub = UIKit.Text(p, "Sub", UIKit.Hud, 24, UIKit.Cyan, TextAlignmentOptions.Left, "ONE ROOM · START TO EXIT · R RESTARTS · RACE YOUR GHOST"); sub.characterSpacing = 4f;
            UIKit.Place(sub.rectTransform, Anc.TL, new Vector2(44, -92), new Vector2(1300, 32));
            _summary = UIKit.Text(p, "Summary", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Right); _summary.characterSpacing = 4f;
            UIKit.Place(_summary.rectTransform, Anc.TR, new Vector2(-40, -44), new Vector2(800, 36));

            var rooms = TimeAttackRooms.All;
            _tiles = new Tile[rooms.Count];
            _ring = new Image[rooms.Count + 2];
            for (int row = 0; row < RowNames.Length; row++)
            {
                var label = UIKit.Text(p, "Row" + row, UIKit.Hud, 26, row == 0 ? UIKit.Magenta : UIKit.Cyan, TextAlignmentOptions.Left, RowNames[row]); label.characterSpacing = 4f;
                UIKit.Place(label.rectTransform, Anc.TL, new Vector2(40, -(150 + row * (TileH + 14f) + 34f)), new Vector2(150, 36));
            }
            for (int i = 0; i < rooms.Count; i++)
            {
                int k = i; var r = rooms[i];
                int row = r.act, col = r.number - 1;
                var pos = new Vector2(190 + col * (TileW + Gap), -(150 + row * (TileH + 14f)));
                var b = UIButton.Create(p, "Tile" + i, r.Classic ? "R" + r.number : r.number.ToString("00"), Anc.TL, pos, new Vector2(TileW, TileH), 30, row == 0 ? UIKit.Magenta : UIKit.Cyan, () => Play(k), UIKit.Hud);
                b.Label.rectTransform.anchorMin = new Vector2(0f, 0.6f); b.Label.rectTransform.offsetMin = b.Label.rectTransform.offsetMax = Vector2.zero;
                var t = new Tile { R = r, B = b };
                t.Best = UIKit.Text(b.transform, "Best", UIKit.Hud, 20, UIKit.Paper); UIKit.Place(t.Best.rectTransform, Anc.B, new Vector2(0, 34), new Vector2(TileW - 8, 24));
                t.Next = UIKit.Text(b.transform, "Next", UIKit.Hud, 16, UIKit.Dim); UIKit.Place(t.Next.rectTransform, Anc.B, new Vector2(0, 11), new Vector2(TileW - 6, 20));
                t.Bar = UIKit.Img(b.transform, "Bar", UIKit.Dim); UIKit.Place(t.Bar.rectTransform, Anc.B, new Vector2(0, 3), new Vector2(TileW - 12, 5));
                t.Icon = UIKit.Img(b.transform, "Medal", UIKit.Dim, UIKit.Disc); UIKit.Place(t.Icon.rectTransform, Anc.TR, new Vector2(-8, -8), new Vector2(16, 16));
                _tiles[i] = t;
                Ring(i, b);
            }

            float dy = -(150 + 4 * (TileH + 14f) + 16f);
            _name = UIKit.Text(p, "Name", UIKit.Title, 44, UIKit.Paper, TextAlignmentOptions.Left); _name.fontStyle = FontStyles.Bold; _name.characterSpacing = 4f;
            UIKit.Place(_name.rectTransform, Anc.TL, new Vector2(40, dy), new Vector2(1100, 56));
            _times = UIKit.Text(p, "Times", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Left); _times.characterSpacing = 2f;
            UIKit.Place(_times.rectTransform, Anc.TL, new Vector2(44, dy - 64), new Vector2(1300, 34));
            _best = UIKit.Text(p, "Best", UIKit.Hud, 32, UIKit.Paper, TextAlignmentOptions.Left); _best.characterSpacing = 3f;
            UIKit.Place(_best.rectTransform, Anc.TL, new Vector2(44, dy - 108), new Vector2(1100, 40));
            _next = UIKit.Text(p, "Next", UIKit.Hud, 32, UIKit.Green, TextAlignmentOptions.Left); _next.characterSpacing = 3f;
            UIKit.Place(_next.rectTransform, Anc.TL, new Vector2(44, dy - 152), new Vector2(1100, 40));
            _ghost = UIKit.Text(p, "Ghost", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Left); _ghost.characterSpacing = 3f;
            UIKit.Place(_ghost.rectTransform, Anc.TL, new Vector2(44, dy - 196), new Vector2(1100, 32));
            _status = UIKit.Text(p, "Status", UIKit.Hud, 24, UIKit.Danger, TextAlignmentOptions.Left); _status.characterSpacing = 3f;
            UIKit.Place(_status.rectTransform, Anc.BL, new Vector2(44, 40), new Vector2(1000, 32));

            _back = UIButton.Create(p, "Back", "BACK", Anc.BR, new Vector2(-440, 40), new Vector2(300, 84), 32, UIKit.Cyan, Close, UIKit.Hud);
            _start = UIButton.Create(p, "Start", "START", Anc.BR, new Vector2(-40, 40), new Vector2(380, 84), 40, UIKit.Magenta, () => Play(_sel), UIKit.Hud);
            Ring(BackIdx, _back);
            Ring(StartIdx, _start);
        }

        void Ring(int idx, UIButton b)
        {
            var r = UIKit.Img(b.transform, "Focus", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(r.rectTransform, -7, -7, -7, -7); r.enabled = false;
            _ring[idx] = r;
            int k = idx;
            b.Hovered = () => SetFocus(k);
        }

        public override void Show()
        {
            base.Show();
            _t = 0f; _acted = false; _warm = 0; _shownFrame = Time.frameCount;
            var save = SaveService.Data;
            _unlocked = TimeAttackRooms.Unlocked(save);
            int sync = 0, gold = 0, focus = 0;
            for (int i = 0; i < _tiles.Length; i++)
            {
                PaintTile(i, save);
                var m = TimeAttackRooms.MedalOf(save, _tiles[i].R);
                if (m == Medal.Sync) sync++;
                if (m >= Medal.Gold) gold++;
                if (_tiles[i].R.id == _lastId) focus = i;
            }
            _summary.SetText("SYNC " + sync + " / " + _tiles.Length + "   GOLD+ " + gold + " / " + _tiles.Length);
            _status.SetText(_unlocked ? "" : "LOCKED · TIME-ATTACK OPENS AS YOU KEEP BREACHING");
            _start.Base = _unlocked ? UIKit.Magenta : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f); _start.Hot = _unlocked ? UIKit.Cyan : _start.Base; _start.SetSelected(false);
            _sel = focus;
            SetFocus(focus);
            Detail();
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
            UIKit.FreeCursor();
        }

        public override void Hide() { UIKit.Kill(ref _pop); UIKit.Kill(ref _btnTw); base.Hide(); }

        // ---------------------------------------------------------------- painting (allocates: on Show / focus / warm-up only)
        void PaintTile(int i, SaveV3 save)
        {
            var t = _tiles[i];
            var m = TimeAttackRooms.MedalOf(save, t.R);
            float best = TimeAttackRooms.BestTime(save, t.R);
            var c = m == Medal.None ? new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.35f) : UIKit.MedalColor((int)m);
            t.Bar.color = c; t.Icon.color = c;
            t.Best.SetText(best > 0f ? UIKit.ClockShort(best) : "--");
            t.Next.SetText(Next(t.R, best, true));
            t.Next.color = NextColor(t.R, best);
            var col = !_unlocked ? new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f) : t.R.Classic ? UIKit.Magenta : UIKit.Cyan;
            t.B.Base = col; t.B.Hot = _unlocked ? UIKit.Paper : col; t.B.SetSelected(false);
        }

        /// "0.8 s GOLD" (tile) / "0.8 s to GOLD" (detail); "SYNC" when the dev time is beaten; "" before a clear; "..." while the
        /// dev time is still being worked out (tiles only).
        static string Next(TimeAttackRooms.Room r, float best, bool tile)
        {
            if (best <= 0f) return tile ? "" : "CLEAR IT FOR BRONZE";
            if (tile && !TimeAttackRooms.HasTDev(r)) return "...";
            float tDev = TimeAttackRooms.TDev(r);
            if (tDev <= 0f) return "";
            string s = RoomReports.NextTarget(best, tDev, 0, 0, 0, 0);   // no par: null once SYNC is held
            if (s == null) return tile ? "SYNC" : "SYNC · DEV TIME BEATEN";
            return tile ? s.Replace(" to ", " ") : s;
        }

        static Color NextColor(TimeAttackRooms.Room r, float best)
        {
            if (best <= 0f || !TimeAttackRooms.HasTDev(r)) return UIKit.Dim;
            var m = Medals.For(best, TimeAttackRooms.TDev(r));
            return m == Medal.Sync ? UIKit.MedalColor(4) : UIKit.MedalColor((int)m + 1);
        }

        void Detail()
        {
            var save = SaveService.Data;
            var r = _tiles[_sel].R;
            float tDev = TimeAttackRooms.TDev(r), best = TimeAttackRooms.BestTime(save, r);
            var m = TimeAttackRooms.MedalOf(save, r);
            _name.SetText(r.Classic ? r.Label : "ACT " + r.act + " · ROOM " + r.number.ToString("00"));
            _times.SetText(tDev > 0f
                ? "SYNC " + UIKit.ClockShort(Medals.Threshold(Medal.Sync, tDev)) + " (DEV TIME)   GOLD " + UIKit.ClockShort(Medals.Threshold(Medal.Gold, tDev))
                  + "   SILVER " + UIKit.ClockShort(Medals.Threshold(Medal.Silver, tDev)) + "   BRONZE ANY CLEAR"
                : "NO DEV TIME · BRONZE ANY CLEAR");
            _best.SetText(best > 0f ? "BEST " + UIKit.ClockShort(best) + "   " + (m == Medal.None ? "" : UIKit.MedalNames[(int)m]) : "NO CLEAR YET");
            _best.color = m == Medal.None ? UIKit.Paper : UIKit.MedalColor((int)m);
            _next.SetText("NEXT  " + Next(r, best, false));
            var g = TimeAttackRooms.LoadGhost(save, r.id);
            _ghost.SetText(string.IsNullOrEmpty(g) ? "NO GHOST YET · YOUR FASTEST CLEAR RACES YOU" : "GHOST READY · YOUR FASTEST CLEAR RACES YOU");
        }

        // ---------------------------------------------------------------- actions
        void Play(int i)
        {
            if (_acted || i < 0 || i >= _tiles.Length) return;
            if (!_unlocked) { UIKit.Pop(ref _btnTw, _tiles[i].B.transform, 0.9f, 0.2f); return; }
            _acted = true;
            _lastId = _tiles[i].R.id;
            Hide();
            if (!TimeAttack.Play(_lastId)) { Show(); _status.SetText("ROOM UNAVAILABLE"); }
        }

        void Close() { if (!_acted) Hide(); }

        // ---------------------------------------------------------------- focus + input
        void SetFocus(int f)
        {
            _focus = (f % Count + Count) % Count;
            for (int i = 0; i < Count; i++) if (_ring[i]) _ring[i].enabled = i == _focus;
            if (_focus < _tiles.Length && _focus != _sel) { _sel = _focus; Detail(); }
        }

        public override void Tick(float dt)
        {
            _t += dt;
            UIKit.FreeCursor();
            WarmOne();
            if (_acted || _t < InputGuard || Time.frameCount == _shownFrame) return;   // the key that opened the screen must not also act
            if (GameInput.Pause.WasPressedThisFrame()) { Close(); return; }
            int step = UIKit.NavStep;
            if (step != 0) SetFocus(_focus + step);
            else if (UIKit.Confirm.WasPressedThisFrame())
            {
                if (_focus == BackIdx) Close();
                else Play(_focus == StartIdx ? _sel : _focus);
            }
        }

        /// Works out one generated room's dev time per frame and repaints its tile.
        void WarmOne()
        {
            while (_warm < _tiles.Length && TimeAttackRooms.HasTDev(_tiles[_warm].R)) _warm++;
            if (_warm >= _tiles.Length) return;
            TimeAttackRooms.TDev(_tiles[_warm].R);
            PaintTile(_warm, SaveService.Data);
            if (_warm == _sel) Detail();
            _warm++;
        }
    }
}
