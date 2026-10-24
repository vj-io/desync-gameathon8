using System.Collections.Generic;
using System.Text;
using Desync.Content;
using Desync.Flow;
using Desync.Gen;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Desync.UI
{
    /// BREACH run map: the current act's 3 lanes (4 rows + boss) left to right. Between nodes it is interactive (lit nodes =
    /// choices: click, or ←/→ + SPACE / pad); in a room TAB shows it read-only. Hovering or focusing a node previews its room
    /// mutators. Raises RunEvents.ChooseNode. (A boss's CASH OUT / GO DEEPER is on the room results screen.)
    public sealed class RunMapScreen : UIPanel
    {
        const int MaxNodes = 16, MaxEdges = 48;
        const float ColX0 = -820f, ColDx = 250f, LaneDy = 210f, GraphY = 40f, NodeSize = 128f;

        UIButton[] _node; TMP_Text[] _cap; Image[] _ring; RunNode[] _slot;
        Image[] _edge;
        TMP_Text _head, _sub, _preview, _loadout, _stats, _keys;
        RectTransform _panel; Tween _pop;

        readonly List<string> _choices = new List<string>();
        readonly StringBuilder _sb = new StringBuilder(256);
        int _focus, _shownFrame = -1, _count;
        bool _acted;
        float _t;

        public bool Interactive { get; private set; }
        internal int FocusIndex => _focus;
        internal IReadOnlyList<string> ChoiceIds => _choices;
        internal string PreviewText => _preview.text;

        protected override void Create()
        {
            var dim = UIKit.Img(Root, "Dim", new Color(0f, 0f, 0.03f, 0.84f)); UIKit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var panel = UIKit.Box(Root, "Panel", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.6f), 0.9f);
            _panel = UIKit.Place(panel.rectTransform, Anc.C, Vector2.zero, new Vector2(1840, 1000));

            _head = UIKit.Text(panel.transform, "Head", UIKit.Title, 50, UIKit.Paper, TextAlignmentOptions.Left); _head.fontStyle = FontStyles.Bold; _head.characterSpacing = 6f;
            UIKit.Place(_head.rectTransform, Anc.TL, new Vector2(48, -30), new Vector2(1200, 64));
            _sub = UIKit.Text(panel.transform, "Sub", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left); _sub.characterSpacing = 6f;
            UIKit.Place(_sub.rectTransform, Anc.TL, new Vector2(52, -96), new Vector2(1200, 34));

            var graph = UIKit.Obj(panel.transform, "Graph"); UIKit.Place(graph, Anc.C, new Vector2(0, GraphY), new Vector2(10, 10));
            _edge = new Image[MaxEdges];
            for (int i = 0; i < MaxEdges; i++)
            {
                _edge[i] = UIKit.Img(graph, "Edge" + i, UIKit.Dim);
                _edge[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                _edge[i].gameObject.SetActive(false);
            }
            _node = new UIButton[MaxNodes]; _cap = new TMP_Text[MaxNodes]; _ring = new Image[MaxNodes]; _slot = new RunNode[MaxNodes];
            for (int i = 0; i < MaxNodes; i++)
            {
                int k = i;
                var b = UIButton.Create(graph, "Node" + i, "", Anc.C, Vector2.zero, new Vector2(NodeSize, NodeSize), 64, UIKit.Cyan, () => Click(k), UIKit.Hud);
                b.Hovered = () => Hover(k);
                _ring[i] = UIKit.Img(b.transform, "Ring", UIKit.Paper, UIKit.Frame, Image.Type.Sliced); UIKit.Stretch(_ring[i].rectTransform, -14, -14, -14, -14);
                _cap[i] = UIKit.Text(b.transform, "Cap", UIKit.Hud, 22, UIKit.Dim, TextAlignmentOptions.Center); _cap[i].characterSpacing = 4f;
                UIKit.Place(_cap[i].rectTransform, Anc.B, new Vector2(0, -40), new Vector2(240, 30));
                _node[i] = b;
                b.gameObject.SetActive(false);
            }

            // right column: loadout + run stats
            var lh = UIKit.Text(panel.transform, "LoadoutHead", UIKit.Hud, 26, UIKit.Cyan, TextAlignmentOptions.Left, "LOADOUT"); lh.characterSpacing = 6f;
            UIKit.Place(lh.rectTransform, Anc.TR, new Vector2(-48, -150), new Vector2(520, 34));
            _loadout = UIKit.Text(panel.transform, "Loadout", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.TopLeft); _loadout.lineSpacing = 12f;
            UIKit.Place(_loadout.rectTransform, Anc.TR, new Vector2(-48, -194), new Vector2(520, 300));
            _stats = UIKit.Text(panel.transform, "Stats", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.TopLeft); _stats.lineSpacing = 12f;
            UIKit.Place(_stats.rectTransform, Anc.TR, new Vector2(-48, -520), new Vector2(520, 220));

            // preview (bottom-left)
            var pv = UIKit.Box(panel.transform, "Preview", new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.5f), 0.7f);
            UIKit.Place(pv.rectTransform, Anc.BL, new Vector2(48, 64), new Vector2(1180, 150));
            _preview = UIKit.Text(pv.transform, "Text", UIKit.Hud, 28, UIKit.Paper, TextAlignmentOptions.TopLeft); _preview.textWrappingMode = TextWrappingModes.Normal;
            UIKit.Stretch(_preview.rectTransform, 24, 14, 24, 14);

            _keys = UIKit.Text(panel.transform, "Keys", UIKit.Hud, 24, UIKit.Dim, TextAlignmentOptions.Center); _keys.characterSpacing = 6f;
            UIKit.Place(_keys.rectTransform, Anc.B, new Vector2(0, 20), new Vector2(1700, 34));
        }

        public override void Show() { Show(true); }

        /// interactive = between nodes (choose); false = TAB overlay in a room (look only).
        public void Show(bool interactive)
        {
            base.Show();
            Interactive = interactive;
            _acted = false; _shownFrame = Time.frameCount; _t = 0f;
            Rebuild();
            if (interactive) UIKit.Pop(ref _pop, _panel, 0.94f, 0.2f);
        }

        public override void Hide() { UIKit.Kill(ref _pop); base.Hide(); }

        void Rebuild()
        {
            int act = Mathf.Max(1, RunState.Act);
            var nodes = RunView.ActNodes(act);
            var cur = RunView.Node(RunState.NodeId);
            RunView.Choices(_choices);
            if (!Interactive) _choices.Clear();

            _head.SetText("ACT " + act + " · " + RunView.ActName(act));
            _sub.SetText(Interactive ? "CHOOSE YOUR NEXT NODE" : "RUN MAP · RELEASE TAB");
            _keys.SetText(Interactive ? "CLICK A LIT NODE · ←/→ SELECT · SPACE ENTER · PAD D-PAD + A" : "");

            // nodes
            _count = 0;
            if (nodes != null)
                foreach (var n in nodes)
                {
                    if (_count >= MaxNodes) break;
                    int i = _count++;
                    _slot[i] = n;
                    var b = _node[i];
                    b.gameObject.SetActive(true);
                    ((RectTransform)b.transform).anchoredPosition = NodePos(n);
                    var c = RunView.NodeColor(n.type);
                    b.Label.SetText(RunView.NodeGlyph[(int)n.type]);
                    _cap[i].SetText(n.type == NodeType.Boss ? RunView.BossName(act) : RunView.NodeShort[(int)n.type]);
                    bool choice = _choices.Contains(n.id), here = n.id == RunState.NodeId, been = RunView.Path.Contains(n.id);
                    float a = choice || here ? 1f : been ? 0.55f : 0.3f;
                    b.Base = new Color(c.r, c.g, c.b, a); b.Hot = choice ? UIKit.Paper : b.Base;
                    b.SetSelected(been || here);
                    _cap[i].color = choice || here ? c : UIKit.Dim;
                    _ring[i].enabled = here;
                    _ring[i].color = UIKit.Paper;
                }
            for (int i = _count; i < MaxNodes; i++) { _slot[i] = null; _node[i].gameObject.SetActive(false); }

            // edges
            int e = 0;
            for (int i = 0; i < _count; i++)
            {
                var from = _slot[i];
                foreach (var nid in from.next)
                {
                    int j = SlotOf(nid);
                    if (j < 0 || e >= MaxEdges) continue;
                    var to = _slot[j];
                    bool lit = from.id == RunState.NodeId && _choices.Contains(to.id);
                    bool walked = RunView.Path.Contains(from.id) && RunView.Path.Contains(to.id);
                    Edge(_edge[e++], NodePos(from), NodePos(to), lit ? UIKit.Paper : walked ? new Color(UIKit.Cyan.r, UIKit.Cyan.g, UIKit.Cyan.b, 0.8f) : new Color(UIKit.Dim.r, UIKit.Dim.g, UIKit.Dim.b, 0.25f), lit ? 5f : 3f);
                }
            }
            for (int i = e; i < MaxEdges; i++) _edge[i].gameObject.SetActive(false);

            // loadout + stats
            _sb.Clear();
            var held = RunState.Held;
            for (int i = 0; i < Loadout.MaxHeld; i++)
            {
                if (i < held.Count)
                {
                    var x = held[i];
                    _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(RunView.ArchColor(x.archetype))).Append('>')
                       .Append(RunView.ArchGlyph[(int)x.archetype]).Append("</color> ").Append(x.name.ToUpperInvariant())
                       .Append(" <alpha=#88>").Append(RunView.RarityName[(int)x.rarity]).Append("<alpha=#FF>\n");
                }
                else _sb.Append("<alpha=#55>· EMPTY SLOT<alpha=#FF>\n");
            }
            _loadout.SetText(UIKit.Safe(_sb.ToString(), UIKit.Hud));
            _sb.Clear();
            _sb.Append("BACKUPS   ").Append(RunView.Backups(RunState.Backups)).Append('\n')
               .Append("CYCLES    ").Append(RunState.Cycles).Append('\n')
               .Append("REROLLS   ").Append(RunState.Rerolls).Append('\n')
               .Append("FRAGMENTS ").Append(RunState.Fragments).Append('\n')
               .Append("TIME      ").Append(UIKit.ClockShort(RunState.RunTime));
            _stats.SetText(_sb.ToString());

            SetFocus(0);
            if (_choices.Count == 0) Preview(cur);
        }

        Vector2 NodePos(RunNode n) => n.type == NodeType.Boss
            ? new Vector2(ColX0 + 4 * ColDx, 0f)
            : new Vector2(ColX0 + n.index * ColDx, (1 - n.lane) * LaneDy);

        static void Edge(Image im, Vector2 a, Vector2 b, Color c, float width)
        {
            var d = b - a;
            float len = d.magnitude, off = NodeSize * 0.5f + 6f;
            if (len <= off * 2f) { im.gameObject.SetActive(false); return; }
            var dir = d / len;
            var rt = im.rectTransform;
            rt.anchoredPosition = a + dir * off;
            rt.sizeDelta = new Vector2(len - off * 2f, width);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            im.color = c;
            im.gameObject.SetActive(true);
        }

        int SlotOf(string id) { for (int i = 0; i < _count; i++) if (_slot[i].id == id) return i; return -1; }

        // ---------------------------------------------------------------- focus + preview
        void SetFocus(int f)
        {
            if (_choices.Count == 0) return;
            _focus = (f % _choices.Count + _choices.Count) % _choices.Count;
            for (int i = 0; i < _count; i++)
            {
                bool on = _slot[i].id == _choices[_focus];
                _node[i].transform.localScale = Vector3.one * (on ? 1.12f : 1f);
                if (_slot[i].id != RunState.NodeId) _ring[i].enabled = on;
            }
            Preview(RunView.Node(_choices[_focus]));
        }

        void Hover(int slot)
        {
            var n = _slot[slot];
            if (n == null) return;
            int ci = _choices.IndexOf(n.id);
            if (ci >= 0) SetFocus(ci); else Preview(n);
        }

        void Preview(RunNode n)
        {
            _sb.Clear();
            if (n != null)
            {
                var c = RunView.NodeColor(n.type);
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append('>').Append(RunView.NodeGlyph[(int)n.type]).Append(' ')
                   .Append(n.type == NodeType.Boss ? RunView.BossName(n.act) : RunView.NodeName[(int)n.type]).Append("</color>  <alpha=#99>")
                   .Append(RunView.NodeDesc[(int)n.type]).Append("<alpha=#FF>\n");
                if (RunView.IsRoom(n.type))
                {
                    var muts = RunView.PreviewMutators(n);
                    if (muts.Count == 0) _sb.Append("ROOM MUTATORS: NONE");
                    else
                    {
                        _sb.Append("ROOM MUTATORS: ");
                        for (int i = 0; i < muts.Count; i++)
                            _sb.Append(i > 0 ? "  ·  " : "").Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UIKit.Magenta)).Append('>')
                               .Append(muts[i].name.ToUpperInvariant()).Append("</color> ").Append(muts[i].desc);
                    }
                }
            }
            _preview.SetText(UIKit.Safe(_sb.ToString(), UIKit.Hud));
        }

        // ---------------------------------------------------------------- input
        void Click(int slot)
        {
            if (!Interactive || _acted) return;
            var n = _slot[slot];
            if (n == null || !_choices.Contains(n.id)) return;
            Choose(n.id);
        }

        void Choose(string id)
        {
            _acted = true;
            RunEvents.ChooseNode?.Invoke(id);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            for (int i = 0; i < _count; i++)   // lit choices breathe
            {
                if (_slot[i] == null || !_choices.Contains(_slot[i].id)) continue;
                float k = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f + i);
                var f = _node[i].Frame.color; f.a = k; _node[i].Frame.color = f;
            }
            if (!Interactive || _acted || _t < 0.25f || Time.frameCount == _shownFrame) return;   // the key that opened the map must not also choose
            int step = UIKit.NavStep;
            if (step != 0) SetFocus(_focus + step);
            else if (UIKit.Confirm.WasPressedThisFrame() && _choices.Count > 0) Choose(_choices[_focus]);
        }
    }
}
