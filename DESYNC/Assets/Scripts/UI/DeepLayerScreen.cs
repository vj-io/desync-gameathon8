using System;
using System.Collections.Generic;
using System.Text;
using Desync.Content;
using Desync.Flow;
using Desync.Net;
using Desync.Rules;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Deep Layer select (title menu, once the first ARCHITECT kill opens it). Flow's model (DeepLayer.cs): a Deep Layer run is a CAMPAIGN
    /// under heat mutators, its level is the total heat of the picks, every level has a preset, and levels open one at a time up to
    /// RunUnlocks.MaxDeepLayer. The screen shows DL 1-30 as three rows of ten (locked ones dimmed; gold = a reward still to earn, green = earned),
    /// the 20 heat mutators as rank tiles (click / SPACE steps the rank, wrapping to off), the heat total against the level, the Fragments
    /// bonus, the level's cosmetic reward (DL 11-20 and DL 30; DL 21-29 are played for the DL 30 one) and whether the bosses run hot.
    /// START raises DeepRun.StartRequested(level, picks) once DeepRules.Check passes; PRESET reloads the level's default picks.
    /// One linear focus ring (levels, tiles, PRESET, BACK, START): arrows / WASD / d-pad move it, SPACE / Enter / pad A act, mouse hover
    /// moves it and a click acts.
    public sealed class DeepLayerScreen : UIPanel
    {
        const int MaxTiles = 20, Cols = 4, Rows = 5;
        const float TileW = 440f, TileH = 118f, TileGapX = 20f, TileGapY = 6f, InputGuard = 0.25f;
        const int LevelCols = 10;
        const float LevelW = 128f, LevelH = 36f, LevelPitchX = 138f, LevelPitchY = 40f, TilesTop = 252f;

        sealed class Tile
        {
            public Mutator M; public UIButton B; public TMP_Text Name, Heat, Desc, Pips; public Image Fill; public int Rank;
        }

        readonly Tile[] _tile = new Tile[MaxTiles];
        UIButton[] _lvl; UIButton _preset, _back, _start;
        Image[] _ring; UIButton[] _btn; Action[] _act;
        TMP_Text _head, _sub, _best, _heat, _status, _reward, _boss, _prize;
        RectTransform _panel; Tween _pop, _startTw;
        readonly List<MutatorPick> _picks = new List<MutatorPick>();
        readonly StringBuilder _sb = new StringBuilder(96);
        int _tiles, _level, _maxDL, _focus, _shownFrame = -1;
        float _t, _launchT; bool _acted;

        internal int Level => _level;
        internal int MaxLevel => _maxDL;
        internal int TileCount => _tiles;
        internal int Focus => _focus;
        internal int Heat { get; private set; }
        internal bool CanStart { get; private set; }
        internal string StatusText => _status.text;
        internal string RewardText => _reward.text;
        internal string PrizeText => _prize.text;
        internal int RankOf(string id) { for (int i = 0; i < _tiles; i++) if (_tile[i].M.id == id) return _tile[i].Rank; return -1; }
        internal IReadOnlyList<MutatorPick> CurrentPicks => _picks;

        int LevelIdx(int level) => level - 1;
        int TileIdx(int i) => DeepRules.MaxLevel + i;
        int PresetIdx => DeepRules.MaxLevel + _tiles;
        int BackIdx => PresetIdx + 1;
        int StartIdx => PresetIdx + 2;
        int Count => StartIdx + 1;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.9f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.8f), 0.95f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1840, 1040));

            _head = UIKit.Text(panel.transform, "Head", UIKit.Title, 54, UIKit.Paper, TextAlignmentOptions.Left, "DEEP LAYER"); _head.fontStyle = FontStyles.Bold; _head.characterSpacing = 8f;
            UIKit.Place(_head.rectTransform, Anc.TL, new Vector2(40, -20), new Vector2(900, 70));
            _sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 24, UIKit.Magenta, TextAlignmentOptions.Left, "THE LEVEL IS THE HEAT · PICK MUTATORS UNTIL THE TOTAL MATCHES"); _sub.characterSpacing = 4f;
            UIKit.Place(_sub.rectTransform, Anc.TL, new Vector2(44, -92), new Vector2(1500, 32));
            _best = UIKit.Text(panel.transform, "Best", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Right); _best.characterSpacing = 4f;
            UIKit.Place(_best.rectTransform, Anc.TR, new Vector2(-40, -44), new Vector2(700, 36));

            // keyed to the catalog: heat mutators only (the weekend run mutators have no ranks)
            var cat = RunView.Cat;
            foreach (var m in cat.mutators) if (!m.runMutator && m.ranks.Count > 0 && _tiles < MaxTiles) _tile[_tiles++] = new Tile { M = m };

            _btn = new UIButton[DeepRules.MaxLevel + _tiles + 3]; _ring = new Image[_btn.Length]; _act = new Action[_btn.Length];
            _lvl = new UIButton[DeepRules.MaxLevel];
            for (int i = 0; i < DeepRules.MaxLevel; i++)
            {
                int level = i + 1;
                var b = UIButton.Create(panel.transform, "Level" + level, "DL " + level, Anc.TL, new Vector2(40 + i % LevelCols * LevelPitchX, -(128 + i / LevelCols * LevelPitchY)),
                                        new Vector2(LevelW, LevelH), 22, UIKit.Cyan, () => PickLevel(level), UIKit.Hud);
                _lvl[i] = b; Reg(LevelIdx(level), b, () => PickLevel(level));
            }
            _prize = UIKit.Text(panel.transform, "Prize", UIKit.Hud, 22, UIKit.Paper, TextAlignmentOptions.TopLeft); _prize.characterSpacing = 2f; _prize.richText = false;
            _prize.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_prize.rectTransform, Anc.TR, new Vector2(-40, -126), new Vector2(380, 118));

            for (int i = 0; i < _tiles; i++)
            {
                int k = i; var t = _tile[i];
                int col = i % Cols, row = i / Cols;
                var b = UIButton.Create(panel.transform, "Tile" + i, "", Anc.TL, new Vector2(40 + col * (TileW + TileGapX), -(TilesTop + row * (TileH + TileGapY))), new Vector2(TileW, TileH), 20, UIKit.Dim, () => StepRank(k), UIKit.Hud);
                t.B = b;
                t.Fill = UIKit.Img(b.transform, "Active", new Color(UIKit.Magenta.r, UIKit.Magenta.g, UIKit.Magenta.b, 0.16f)); UIKit.Stretch(t.Fill.rectTransform); t.Fill.transform.SetSiblingIndex(1);
                t.Name = UIKit.Text(b.transform, "Name", UIKit.Hud, 24, UIKit.Paper, TextAlignmentOptions.Left); t.Name.fontStyle = FontStyles.Bold; t.Name.richText = false;
                UIKit.Place(t.Name.rectTransform, Anc.TL, new Vector2(14, -8), new Vector2(290, 30));
                t.Heat = UIKit.Text(b.transform, "Heat", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Right); t.Heat.characterSpacing = 2f; t.Heat.richText = false;
                UIKit.Place(t.Heat.rectTransform, Anc.TR, new Vector2(-14, -10), new Vector2(130, 28));
                t.Desc = UIKit.Text(b.transform, "Desc", UIKit.Term, 26, UIKit.Paper, TextAlignmentOptions.TopLeft); t.Desc.richText = false; t.Desc.textWrappingMode = TextWrappingModes.Normal;
                UIKit.Place(t.Desc.rectTransform, Anc.TL, new Vector2(14, -40), new Vector2(412, 52));
                t.Pips = UIKit.Text(b.transform, "Pips", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left); t.Pips.characterSpacing = 4f; t.Pips.richText = false;
                UIKit.Place(t.Pips.rectTransform, Anc.BL, new Vector2(14, 6), new Vector2(300, 26));
                string name = UIKit.Safe(t.M.name.ToUpperInvariant(), UIKit.Hud);
                t.Name.SetText(name);
                var sb = new StringBuilder(t.M.desc ?? "");
                if (t.M.conflicts.Count > 0)
                {
                    sb.Append(" No ");
                    for (int c = 0; c < t.M.conflicts.Count; c++) sb.Append(c > 0 ? ", " : "").Append(cat.Exploit(t.M.conflicts[c])?.name ?? t.M.conflicts[c]);
                    sb.Append('.');
                }
                t.Desc.SetText(UIKit.Safe(sb.ToString(), UIKit.Term));
                Reg(TileIdx(i), b, () => StepRank(k));
            }

            _heat = UIKit.Text(panel.transform, "HeatTotal", UIKit.Title, 40, UIKit.Green, TextAlignmentOptions.Left); _heat.fontStyle = FontStyles.Bold; _heat.characterSpacing = 4f;
            UIKit.Place(_heat.rectTransform, Anc.BL, new Vector2(40, 108), new Vector2(740, 52));
            _status = UIKit.Text(panel.transform, "Status", UIKit.Hud, 24, UIKit.Paper, TextAlignmentOptions.Left); _status.characterSpacing = 3f;
            UIKit.Place(_status.rectTransform, Anc.BL, new Vector2(44, 76), new Vector2(740, 30));
            _reward = UIKit.Text(panel.transform, "Reward", UIKit.Hud, 22, UIKit.MedalColor(3), TextAlignmentOptions.Left); _reward.characterSpacing = 2f;
            UIKit.Place(_reward.rectTransform, Anc.BL, new Vector2(44, 46), new Vector2(740, 28));
            _boss = UIKit.Text(panel.transform, "Boss", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Left); _boss.characterSpacing = 2f;
            UIKit.Place(_boss.rectTransform, Anc.BL, new Vector2(44, 16), new Vector2(740, 28));

            _preset = UIButton.Create(panel.transform, "Preset", "PRESET", Anc.BR, new Vector2(-760, 40), new Vector2(300, 84), 32, UIKit.Cyan, Preset, UIKit.Hud);
            _back = UIButton.Create(panel.transform, "Back", "BACK", Anc.BR, new Vector2(-440, 40), new Vector2(300, 84), 32, UIKit.Cyan, Close, UIKit.Hud);
            _start = UIButton.Create(panel.transform, "Start", "START", Anc.BR, new Vector2(-40, 40), new Vector2(380, 84), 40, UIKit.Magenta, Launch, UIKit.Hud);
            Reg(PresetIdx, _preset, Preset); Reg(BackIdx, _back, Close); Reg(StartIdx, _start, Launch);
        }

        void Reg(int idx, UIButton b, Action act)
        {
            _btn[idx] = b; _act[idx] = act;
            var r = UIKit.Img(b.transform, "Focus", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(r.rectTransform, -9, -9, -9, -9); r.enabled = false;
            _ring[idx] = r;
            int k = idx;
            b.Hovered = () => SetFocus(k);
        }

        public override void Show()
        {
            base.Show();
            _t = 0f; _acted = false; _launchT = 0f; _shownFrame = Time.frameCount;
            var save = SaveService.Data;
            _maxDL = RunUnlocks.MaxDeepLayer(save);
            _best.SetText(save.deepLayerMax > 0 ? "BEST DL " + save.deepLayerMax : "NO DEEP LAYER WON YET");
            _level = Mathf.Max(1, _maxDL);   // the newest open level: where progress continues
            LoadPreset();
            Repaint();
            SetFocus(StartIdx);
            UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
            UIKit.FreeCursor();
        }

        public override void Hide() { UIKit.Kill(ref _pop); UIKit.Kill(ref _startTw); base.Hide(); }

        // ---------------------------------------------------------------- state
        void LoadPreset()
        {
            for (int i = 0; i < _tiles; i++) _tile[i].Rank = 0;
            foreach (var p in DeepRules.Preset(_level))
                for (int i = 0; i < _tiles; i++) if (_tile[i].M.id == p.id) _tile[i].Rank = p.rank;
        }

        void CollectPicks()
        {
            _picks.Clear();
            for (int i = 0; i < _tiles; i++) if (_tile[i].Rank > 0) _picks.Add(new MutatorPick(_tile[i].M.id, _tile[i].Rank));
            _picks.Sort((a, b) => string.CompareOrdinal(a.id, b.id));   // DeepRules.Canonical order, as the run stores them
        }

        void Repaint()
        {
            CollectPicks();
            var cat = RunView.Cat;
            Heat = DeepRules.Heat(_picks, cat);
            var errors = DeepRules.Check(_level, _picks, cat, _maxDL);
            CanStart = errors.Count == 0;

            var save = SaveService.Data;
            for (int i = 0; i < DeepRules.MaxLevel; i++)
            {
                int level = i + 1; bool open = level <= _maxDL;
                var b = _lvl[i];
                b.Label.SetText(open ? "DL " + level : UIKit.Safe("▒ " + level, UIKit.Hud));
                var tint = DeepRewards.ForLevel(level) == null ? UIKit.Cyan : DeepRewards.Earned(save, level) ? UIKit.Green : UIKit.MedalColor(3);   // gold: a reward still to earn
                b.Base = open ? tint : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.4f); b.Hot = open ? UIKit.Magenta : b.Base;
                b.SetSelected(level == _level);
            }
            PaintPrize(save);
            for (int i = 0; i < _tiles; i++)
            {
                var t = _tile[i]; int max = t.M.ranks.Count; bool on = t.Rank > 0;
                t.B.Base = on ? UIKit.Magenta : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.55f); t.B.Hot = on ? UIKit.Cyan : UIKit.Paper; t.B.SetSelected(false);
                t.Fill.enabled = on;
                t.Heat.SetText(on ? "+" + t.M.ranks[t.Rank - 1] + " HEAT" : max > 1 ? t.M.ranks[0] + "-" + t.M.ranks[max - 1] + " HEAT" : t.M.ranks[0] + " HEAT");
                t.Heat.color = on ? UIKit.Magenta : UIKit.Dim;
                _sb.Clear();
                for (int r = 1; r <= max; r++) _sb.Append(r <= t.Rank ? '●' : '○');
                if (on && max > 1) _sb.Append("  RANK ").Append(t.Rank);
                t.Pips.SetText(_sb.ToString()); t.Pips.color = on ? UIKit.Magenta : UIKit.Dim;
            }

            _heat.SetText("HEAT " + Heat + " / " + _level);
            _heat.color = Heat == _level ? UIKit.Green : UIKit.Danger;
            _status.SetText(StatusLine(errors));
            _status.color = CanStart ? UIKit.Green : UIKit.Paper;
            int percent = Mathf.Min(_level, DeepRules.RewardCap) * DeepRules.RewardPercent;
            _reward.SetText("FRAGMENTS +" + percent + " %" + (_level > DeepRules.RewardCap ? " (CAP)" : "") + " · A 200 RUN BANKS " + (200 + DeepRules.Bonus(200, _level)));
            _boss.SetText(_level >= DeepRules.BossHeatLevel ? "BOSS HEAT ON · " + (_level < DeepRules.MaxLevel ? "A WIN OPENS DL " + (_level + 1) : "THE DEEPEST LEVEL")
                                                              : "BOSS HEAT FROM DL " + DeepRules.BossHeatLevel + (_level < DeepRules.MaxLevel ? " · A WIN OPENS DL " + (_level + 1) : ""));
            _start.Base = CanStart ? UIKit.Magenta : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.5f); _start.Hot = CanStart ? UIKit.Cyan : _start.Base; _start.SetSelected(false);
        }

        /// The selected level's reward: a cosmetic at DL 11-20 and DL 30 (earned by winning the level), Fragments only elsewhere.
        void PaintPrize(SaveV3 save)
        {
            var c = DeepRewards.ForLevel(_level);
            _sb.Clear();
            Color tint = UIKit.Dim;
            if (c != null)
            {
                bool earned = DeepRewards.Earned(save, _level);
                bool worn = earned && Cosmetics.Equipped(save, c.slot) == c;
                _sb.Append("DL ").Append(_level).Append(" REWARD\n").Append(c.name).Append(" · ").Append(c.slot == CosmeticSlot.Flip ? "FLIP EFFECT" : "TITLE").Append('\n')
                   .Append(earned ? (worn ? "EARNED · WORN" : "EARNED") : _level > _maxDL ? "LOCKED" : "WIN DL " + _level + " TO EARN");
                tint = earned ? UIKit.Green : UIKit.MedalColor(3);
            }
            else if (_level > DeepRules.RewardCap)
                _sb.Append("DL ").Append(_level).Append(" · NO REWARD\nFRAGMENT BONUS STOPS AT DL ").Append(DeepRules.RewardCap).Append("\nPLAYED FOR DL ").Append(DeepRewards.CosmeticLevel).Append(" · SYNC GOLD");
            else
                _sb.Append("DL ").Append(_level).Append(" · FRAGMENTS ONLY\nCOSMETICS START AT DL ").Append(DeepRewards.FirstLevel);
            _prize.SetText(UIKit.Safe(_sb.ToString(), UIKit.Hud)); _prize.color = tint;
        }

        string StatusLine(List<string> errors)
        {
            if (errors.Count == 0) return "READY · " + _picks.Count + " MUTATOR" + (_picks.Count == 1 ? "" : "S");
            string e = errors[0];
            if (e.StartsWith("heat:", StringComparison.Ordinal))
            {
                var parts = e.Substring(5).Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out int have) && int.TryParse(parts[1], out int want))
                    return have < want ? "ADD " + (want - have) + " MORE HEAT" : "REMOVE " + (have - want) + " HEAT";
            }
            if (e.StartsWith("locked:", StringComparison.Ordinal)) return "THIS LEVEL IS LOCKED";
            return e.ToUpperInvariant();
        }

        // ---------------------------------------------------------------- actions
        void PickLevel(int level)
        {
            if (_acted) return;
            if (level > _maxDL) { UIKit.Pop(ref _startTw, _lvl[LevelIdx(level)].transform, 0.9f, 0.2f); return; }   // locked: one level opens per win
            _level = level; LoadPreset(); Repaint();
        }

        void StepRank(int i)
        {
            if (_acted || i < 0 || i >= _tiles) return;
            var t = _tile[i];
            t.Rank = (t.Rank + 1) % (t.M.ranks.Count + 1);
            Repaint();
            UIKit.Pop(ref _startTw, t.B.transform, 0.96f, 0.15f);
        }

        void Preset() { if (_acted) return; LoadPreset(); Repaint(); }

        void Close() { if (_acted) return; Hide(); }

        void Launch()
        {
            if (_acted) return;
            Repaint();
            if (!CanStart) { UIKit.Pop(ref _startTw, _start.transform, 0.92f, 0.2f); return; }
            _acted = true; _launchT = 1.5f;
            _status.SetText("JACKING IN..."); _status.color = UIKit.Magenta;
            UIKit.Pop(ref _startTw, _start.transform, 0.9f, 0.25f);
            RunView.ResetRun();   // DeepRun.StartRequested does not raise RunEvents.StartRun
            DeepRun.StartRequested?.Invoke(_level, new List<MutatorPick>(_picks));
        }

        // ---------------------------------------------------------------- focus + input
        void SetFocus(int f)
        {
            _focus = (f % Count + Count) % Count;
            for (int i = 0; i < Count; i++) if (_ring[i]) _ring[i].enabled = i == _focus;
        }

        public override void Tick(float dt)
        {
            _t += dt;
            UIKit.FreeCursor();
            if (_acted)
            {
                _launchT -= dt;   // the run normally takes over within a frame; a refusal (Flow logs why) re-opens the screen
                if (_launchT <= 0f && IsOn) { _acted = false; Repaint(); _status.SetText("RUN REFUSED"); _status.color = UIKit.Danger; }
                return;
            }
            if (_t < InputGuard || Time.frameCount == _shownFrame) return;   // the key that opened the screen must not also act
            int step = UIKit.NavStep;
            if (step != 0) SetFocus(_focus + step);
            else if (UIKit.Confirm.WasPressedThisFrame()) _act[_focus]?.Invoke();
        }
    }
}
