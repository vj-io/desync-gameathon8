using System;
using System.Collections.Generic;
using Desync.Content;
using Desync.Flow;
using Desync.Gen;
using Desync.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// Read-side helpers for the BREACH run screens: labels, glyphs and colours (palette only: no new hues), node lookups,
    /// the catalog, and what the UI remembers about the run (path taken, best GHOST chain).
    public static class RunView
    {
        /// A BREACH run is live (CLASSIC keeps the classic HUD, goals overlay and flow).
        public static bool Active => RunState.Mode != RunMode.Classic && RunState.Phase != RunPhase.None;

        // ---------------------------------------------------------------- catalog (read once; a missing or broken file gives an empty one)
        static Catalog _cat;
        public static Catalog Cat
        {
            get
            {
                if (_cat != null) return _cat;
                var e = Resources.Load<TextAsset>("Content/exploits"); var m = Resources.Load<TextAsset>("Content/mutators");
                try { _cat = Catalog.FromJson(e ? e.text : "{}", m ? m.text : "{}"); }
                catch (Exception ex) { Debug.LogWarning("[DESYNC] run UI: catalog unreadable (" + ex.Message + ")"); _cat = new Catalog(); }
                return _cat;
            }
        }

        // ---------------------------------------------------------------- run memory (UI side)
        public static readonly List<string> Path = new List<string>();   // node ids entered this run, in order
        public static int BestChain;                                      // best GHOST chain seen this run (share text)

        public static void ResetRun() { Path.Clear(); BestChain = 0; }

        /// Title / run results: start a run. The run manager hands CLASSIC to the six-room flow (StartRequested(0)) inside this
        /// same click, so the pointer lock still happens within the user gesture.
        public static void Start(RunMode m) { RunEvents.StartRun?.Invoke(m); }

        /// The act whose boss ends the run: QUICK / DAILY = act 1 (as the run manager plays them), CAMPAIGN = RunRules.CampaignActs.
        public static bool FinalAct => RunState.Act >= (RunState.Mode == RunMode.Quick || RunState.Mode == RunMode.Daily ? 1 : RunRules.CampaignActs);

        /// "A1 · INFILTRATION", "A1 · AUDITOR". Allocates: call on change.
        public static string NodeLabel(RunNode n) =>
            "A" + n.act + " · " + (n.type == NodeType.Boss ? BossName(n.act) : NodeName[(int)n.type]);

        /// Records RunState.NodeId when it moves on (called on every phase change).
        public static void TrackNode()
        {
            var id = RunState.NodeId;
            if (id != null && (Path.Count == 0 || Path[Path.Count - 1] != id)) Path.Add(id);
        }

        // ---------------------------------------------------------------- graph
        public static RunNode Node(string id)
        {
            var g = RunState.Graph;
            if (g == null || id == null) return null;
            for (int a = 0; a < g.acts.Count; a++)
                for (int i = 0; i < g.acts[a].Count; i++)
                    if (g.acts[a][i].id == id) return g.acts[a][i];
            return null;
        }

        public static List<RunNode> ActNodes(int act)
        {
            var g = RunState.Graph;
            return g != null && act >= 1 && act <= g.acts.Count ? g.acts[act - 1] : null;
        }

        /// Nodes the player may enter next: the current node's `next`, or row 0 of the act before the first choice.
        public static void Choices(List<string> into)
        {
            into.Clear();
            var cur = Node(RunState.NodeId);
            if (cur != null && cur.act == RunState.Act) { into.AddRange(cur.next); return; }
            var nodes = ActNodes(RunState.Act);
            if (nodes != null) foreach (var n in nodes) if (n.index == 0) into.Add(n.id);
        }

        /// Room mutators a node will roll: the current node reads the run's list; others replay the run manager's own roll
        /// (RunRules.RoomMutators on the node's "mutators" substream), so the preview is exactly what the room gets.
        public static List<Mutator> PreviewMutators(RunNode n)
        {
            if (n == null || !IsRoom(n.type)) return new List<Mutator>();
            if (n.id == RunState.NodeId) return new List<Mutator>(RunState.RoomMutators);
            return RunRules.RoomMutators(Rng.Substream(n.roomSeed, "mutators"), Cat, n);
        }

        public static bool IsRoom(NodeType t) => t == NodeType.Infil || t == NodeType.Elite || t == NodeType.Anomaly || t == NodeType.Boss;

        // ---------------------------------------------------------------- labels (index = (int)enum)
        public static readonly string[] NodeGlyph = { "►", "◆", "$", "▼", "▒", "■" };
        public static readonly string[] NodeShort = { "INFIL", "ELITE", "MARKET", "COOL", "ANOMALY", "BOSS" };
        public static readonly string[] NodeName = { "INFILTRATION", "HARDENED", "BLACK MARKET", "COOLDOWN", "ANOMALY", "BOSS" };
        public static readonly string[] NodeDesc =
        {
            "A generated room. Clear it for an exploit draft.",
            "Two forced mutators, a guaranteed Rare+ exploit.",
            "Spend Cycles: exploits, removals, a Backup.",
            "Reset melt carry-over, +1 Backup or a reroll token.",
            "SOURCE-only secret room.",
            "Beat it to cash out or go deeper.",
        };
        public static readonly string[] ActNames = { "LOBBY / RENDER FARM", "SERVER CORE", "ARCHITECT'S MIND" };
        public static readonly string[] BossNames = { "AUDITOR", "COMPILER", "ARCHITECT" };
        public static readonly string[] ModeNames = { "CLASSIC", "QUICK BREACH", "CAMPAIGN BREACH", "DAILY BREACH", "ENDLESS OVERCLOCK" };   // by RunMode

        public static string ActName(int act) => act >= 1 && act <= ActNames.Length ? ActNames[act - 1] : "";
        public static string BossName(int act) => act >= 1 && act <= BossNames.Length ? BossNames[act - 1] : "BOSS";

        public static Color NodeColor(NodeType t)
        {
            switch (t)
            {
                case NodeType.Elite: return UIKit.Magenta;
                case NodeType.Market: return UIKit.MedalColor(3);
                case NodeType.Cooldown: return UIKit.Paper;
                case NodeType.Anomaly: return UIKit.Green;
                case NodeType.Boss: return UIKit.Danger;   // the boss is the threat
                default: return UIKit.Cyan;
            }
        }

        public static readonly string[] ArchGlyph = { "○", "▲", "◇", "»", "■", "◆" };
        public static readonly string[] ArchName = { "GHOST", "REDLINE", "PHANTOM", "OVERCLOCK", "TRACER", "DUO" };
        public static readonly string[] RarityName = { "COMMON", "RARE", "EPIC" };

        public static Color ArchColor(Archetype a)
        {
            switch (a)
            {
                case Archetype.Redline: return UIKit.Magenta;
                case Archetype.Phantom: return UIKit.Paper;
                case Archetype.Overclock: return UIKit.Green;
                case Archetype.Tracer: return UIKit.MedalColor(3);
                case Archetype.Duo: return UIKit.Magenta;
                default: return UIKit.Cyan;
            }
        }

        /// "CH" for Clean Handshake (HUD chip caption). Allocates: call when the loadout changes.
        public static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new System.Text.StringBuilder(3);
            bool start = true;
            foreach (char c in name)
            {
                if (start && char.IsLetterOrDigit(c) && sb.Length < 3) sb.Append(char.ToUpperInvariant(c));
                start = c == ' ' || c == '-';
            }
            return sb.ToString();
        }

        /// The first conditional clause of an exploit ("chain>=4"), or null when its effects always hold.
        public static string When(Exploit e)
        {
            if (e?.effects == null) return null;
            for (int i = 0; i < e.effects.Count; i++) if (!string.IsNullOrEmpty(e.effects[i].when)) return e.effects[i].when;
            return null;
        }

        /// Live condition context (chain, melt, trace level, node, layer) for the HUD glow. No allocation.
        public static ConditionContext Live(int node)
        {
            return new ConditionContext
            {
                chain = GameState.Chain, melt = GameState.Melt, trace = GameState.TraceCount, node = node,
                layer = LayerState.InSource ? Layer.Source : Layer.Render,
            };
        }

        /// "●○" Backups (max RunRules.MaxBackups). Allocates: call on change.
        public static string Backups(int n) { var sb = new System.Text.StringBuilder(4); for (int i = 0; i < RunRules.MaxBackups; i++) sb.Append(i < n ? '●' : '○'); return sb.ToString(); }
    }

    /// A card with a rarity border (exploit draft, market, cooldown). Built on UIButton (hover + pointer-down click);
    /// keyboard focus is an outer ring so the card text stays readable.
    public sealed class RunCard
    {
        public UIButton Button;
        public RectTransform Rt;
        public Image Border, Focus, Shade;
        public TMP_Text Glyph, Kicker, Name, Desc, Foot;
        public bool Epic;

        public static RunCard Create(Transform parent, string name, Vector2 pos, Vector2 size, Action click, Action hover)
        {
            var c = new RunCard();
            c.Button = UIButton.Create(parent, name, "", Anc.C, pos, size, 20, UIKit.Cyan, click, UIKit.Hud);
            c.Button.Hovered = hover;
            c.Rt = (RectTransform)c.Button.transform;
            c.Border = c.Button.Frame;
            c.Focus = UIKit.Img(c.Rt, "Focus", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(c.Focus.rectTransform, -12, -12, -12, -12);
            c.Focus.enabled = false;
            float w = size.x - 40f;
            c.Glyph = UIKit.Text(c.Rt, "Glyph", UIKit.Hud, 44, UIKit.Cyan, TextAlignmentOptions.Left); c.Glyph.richText = false;
            UIKit.Place(c.Glyph.rectTransform, Anc.TL, new Vector2(20, -14), new Vector2(60, 56));
            c.Kicker = UIKit.Text(c.Rt, "Kicker", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Right); c.Kicker.characterSpacing = 4f; c.Kicker.richText = false;
            UIKit.Place(c.Kicker.rectTransform, Anc.TR, new Vector2(-20, -26), new Vector2(w - 60, 32));
            c.Name = UIKit.Text(c.Rt, "Name", UIKit.Title, 34, UIKit.Paper, TextAlignmentOptions.Center); c.Name.fontStyle = FontStyles.Bold; c.Name.richText = false;
            c.Name.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Place(c.Name.rectTransform, Anc.T, new Vector2(0, -90), new Vector2(w, 100));
            c.Desc = UIKit.Text(c.Rt, "Desc", UIKit.Term, 34, UIKit.Paper, TextAlignmentOptions.Top); c.Desc.richText = false;
            c.Desc.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Stretch(c.Desc.rectTransform, 22, 74, 22, 206);
            c.Foot = UIKit.Text(c.Rt, "Foot", UIKit.Hud, 26, UIKit.Dim, TextAlignmentOptions.Center); c.Foot.characterSpacing = 4f;
            UIKit.Place(c.Foot.rectTransform, Anc.B, new Vector2(0, 18), new Vector2(w, 40));
            c.Shade = UIKit.Img(c.Rt, "Shade", new Color(UIKit.Ink.r, UIKit.Ink.g, UIKit.Ink.b, 0.6f)); UIKit.Stretch(c.Shade.rectTransform);
            c.Shade.enabled = false;
            return c;
        }

        /// Exploit card: archetype glyph + name, rarity word + border pattern, catalog effect text.
        public void Set(Exploit e, string foot)
        {
            int r = (int)e.rarity;
            Set(RunView.ArchGlyph[(int)e.archetype], RunView.ArchName[(int)e.archetype] + " · " + RunView.RarityName[r], e.name, e.desc, RunView.ArchColor(e.archetype), r, foot);
        }

        public void Set(string glyph, string kicker, string name, string desc, Color c, int rarity, string foot)
        {
            Glyph.SetText(glyph); Glyph.color = c;
            Kicker.SetText(kicker);
            Name.SetText(UIKit.Safe((name ?? "").ToUpperInvariant(), UIKit.Title));
            Desc.SetText(UIKit.Safe(desc ?? "", UIKit.Term));
            Foot.SetText(foot ?? "");
            Button.Base = c; Button.Hot = c == UIKit.Magenta ? UIKit.Cyan : UIKit.Magenta; Border.color = c;
            UIKit.SetBorder(Border, rarity, 2f);
            Epic = rarity >= 2;
            Shade.enabled = false;
        }

        public void SetFocus(bool on) { Focus.enabled = on; Rt.localScale = Vector3.one * (on ? 1.04f : 1f); }

        public void Tick() { if (Epic) UIKit.AnimateBorder(Border); }

        public void Show(bool on) { Rt.gameObject.SetActive(on); }
    }
}
