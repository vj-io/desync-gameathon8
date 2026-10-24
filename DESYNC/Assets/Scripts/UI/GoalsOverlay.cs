using System;
using TMPro;
using UnityEngine;

namespace Desync.UI
{
    /// Resources/Content/rooms.json: per-room tDev + parScore. Read once; a missing or broken file gives zeros.
    static class RoomGoals
    {
        [Serializable] sealed class Room { public string id; public int index; public string name; public float tDev; public int parScore; }
        [Serializable] sealed class Doc { public Room[] rooms; }
        static Room[] _rooms;

        static Room Get(int index)
        {
            if (_rooms == null)
            {
                var ta = Resources.Load<TextAsset>("Content/rooms");
                try { _rooms = ta ? JsonUtility.FromJson<Doc>(ta.text)?.rooms : null; } catch (ArgumentException) { _rooms = null; }
                if (_rooms == null) _rooms = new Room[0];
            }
            foreach (var r in _rooms) if (r != null && r.index == index) return r;
            return null;
        }

        public static int ParScore(int index) { var r = Get(index); return r != null ? r.parScore : 0; }

        /// Flow's live value for the current room wins; the file is the fallback.
        public static float TDev(int index)
        {
            if (index == GameState.RoomIndex && GameState.RoomTDev > 0f) return GameState.RoomTDev;
            var r = Get(index); return r != null ? r.tDev : 0f;
        }
    }

    /// TAB (hold) in a room: medal time limits, par score, grade thresholds.
    public sealed class GoalsOverlay : UIPanel
    {
        TMP_Text _head, _medals, _score, _foot;
        int _room = -99; float _tDev = -1f;

        protected override void Create()
        {
            Group.blocksRaycasts = false;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.7f), 0.9f);
            UIKit.Place(panel.rectTransform, Anc.C, new Vector2(0, 40), new Vector2(1000, 520));
            _head = UIKit.Text(panel.transform, "Head", UIKit.Title, 44, UIKit.Paper, TextAlignmentOptions.Center); _head.fontStyle = FontStyles.Bold; _head.characterSpacing = 6f;
            UIKit.Place(_head.rectTransform, Anc.T, new Vector2(0, -28), new Vector2(940, 60));

            var mh = UIKit.Text(panel.transform, "MedalsHead", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left, "MEDALS · TIME LIMIT"); mh.characterSpacing = 6f;
            UIKit.Place(mh.rectTransform, Anc.TL, new Vector2(60, -116), new Vector2(420, 34));
            _medals = UIKit.Text(panel.transform, "MedalList", UIKit.Hud, 34, UIKit.Paper, TextAlignmentOptions.TopLeft);
            _medals.lineSpacing = 18f;
            UIKit.Place(_medals.rectTransform, Anc.TL, new Vector2(60, -160), new Vector2(420, 260));

            var sh = UIKit.Text(panel.transform, "ScoreHead", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left, "GRADE · SCORE"); sh.characterSpacing = 6f;
            UIKit.Place(sh.rectTransform, Anc.TL, new Vector2(540, -116), new Vector2(400, 34));
            _score = UIKit.Text(panel.transform, "ScoreList", UIKit.Hud, 34, UIKit.Paper, TextAlignmentOptions.TopLeft);
            _score.lineSpacing = 18f;
            UIKit.Place(_score.rectTransform, Anc.TL, new Vector2(540, -160), new Vector2(400, 260));

            _foot = UIKit.Text(panel.transform, "Foot", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center);
            _foot.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(_foot.rectTransform, Anc.B, new Vector2(0, 30), new Vector2(920, 90));
        }

        public override void Show()
        {
            base.Show();
            int ri = GameState.RoomIndex;
            float tDev = RoomGoals.TDev(ri);
            if (ri == _room && tDev == _tDev) return;
            _room = ri; _tDev = tDev;
            var cfg = GameConfig.I;
            string name = ri >= 0 && cfg.roomNames != null && ri < cfg.roomNames.Length ? cfg.roomNames[ri].ToUpperInvariant() : "";
            _head.SetText("R" + (ri + 1).ToString("00") + " · " + name + " · GOALS");

            var sb = new System.Text.StringBuilder();
            for (int m = 4; m >= 1; m--)
            {
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UIKit.MedalColor(m))).Append('>').Append(UIKit.MedalNames[m].PadRight(8)).Append("</color>");
                sb.Append(m == 1 ? "CLEAR" : tDev > 0f ? UIKit.ClockShort(UIKit.MedalLimit(m, tDev)) : "--").Append('\n');
            }
            _medals.SetText(sb.ToString());

            int par = RoomGoals.ParScore(ri);
            sb.Clear();
            sb.Append("PAR     ").Append(par > 0 ? par.ToString() : "--").Append('\n');
            char[] g = { 'S', 'A', 'B' }; float[] k = { 1f, 0.75f, 0.5f };   // D-003: score/par >= 1.0 S, 0.75 A, 0.5 B
            for (int i = 0; i < 3; i++)
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UIKit.GradeColor(g[i]))).Append('>').Append(g[i]).Append("</color>       ")
                  .Append(par > 0 ? Mathf.CeilToInt(par * k[i]) + "+" : "--").Append('\n');
            _score.SetText(sb.ToString());

            _foot.SetText("S NEEDS " + (par > 0 ? par.ToString() : "PAR") + " PTS · NO TRACE +" + Desync.Rules.Scoring.NoTraceRoomPts
                          + " · CLEAN FLIPS RAISE THE MULTIPLIER (MAX x" + GameState.Mods.ChainCap.ToString("0.0") + ")");
        }
    }
}
