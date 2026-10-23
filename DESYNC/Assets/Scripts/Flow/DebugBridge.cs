using System;
using System.Globalization;
using System.Text;
using Desync.Player;
using UnityEngine;

namespace Desync.Flow
{
    /// Testing hook: unityInstance.SendMessage('DesyncDebug','Cmd','room 3'). Kept in release builds.
    /// Commands: room N (1..6) | flip | god on|off | demo on|off | state | win | tp x y z
    public class DebugBridge : MonoBehaviour
    {
        static DebugBridge I;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("DesyncDebug");
            DontDestroyOnLoad(go);
            I = go.AddComponent<DebugBridge>();
        }

        public void Cmd(string line)
        {
            var a = (line ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return;
            try
            {
                switch (a[0].ToLowerInvariant())
                {
                    case "room": GameManager.I.StartRoom(int.Parse(a[1], Inv) - 1); break;
                    case "flip": if (PlayerRef.Exists) PlayerRef.Root.GetComponent<FlipController>().TryFlip(); break;
                    case "god": GameState.God = On(a); break;
                    case "demo": GameState.DemoMode = On(a); break;
                    case "state": Debug.Log(StateJson()); break;
                    case "win": if (RoomManager.I) RoomManager.I.OpenExit(); break;
                    case "tp":
                        if (PlayerRef.Exists)
                            PlayerRef.Root.GetComponent<FirstPersonController>().Teleport(new Vector3(F(a[1]), F(a[2]), F(a[3])));
                        break;
                    default: Debug.LogWarning("[DESYNC] DesyncDebug: unknown command '" + line + "'"); break;
                }
            }
            catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException)
            {
                Debug.LogWarning("[DESYNC] DesyncDebug: bad arguments in '" + line + "'");
            }
        }

        static bool On(string[] a) => a.Length < 2 || a[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        static float F(string s) => float.Parse(s, Inv);

        /// One JSON line with every GameState field plus layer, room clock and player position.
        public static string StateJson()
        {
            var sb = new StringBuilder(512);
            sb.Append('{');
            Num(sb, "room", GameState.RoomIndex);
            Num(sb, "melt", GameState.Melt);
            Bool(sb, "alive", GameState.Alive);
            Bool(sb, "source", LayerState.InSource);
            Bool(sb, "traceActive", GameState.TraceActive);
            Num(sb, "traceRemaining", GameState.TraceRemaining);
            Num(sb, "traceCount", GameState.TraceCount);
            Num(sb, "traceCountdown", GameState.TraceCountdown);
            Num(sb, "flips", GameState.Flips);
            Num(sb, "combo", GameState.Combo);
            Num(sb, "terminalsDone", GameState.TerminalsDone);
            Num(sb, "terminalsTotal", GameState.TerminalsTotal);
            Bool(sb, "exitOpen", GameState.ExitOpen);
            Bool(sb, "paused", GameState.Paused);
            Bool(sb, "inHack", GameState.InHack);
            Bool(sb, "cinematic", GameState.Cinematic);
            Bool(sb, "demo", GameState.DemoMode);
            Bool(sb, "god", GameState.God);
            Bool(sb, "hijack", GameState.HijackActive);
            Num(sb, "glitchCountdown", GameState.GlitchCountdown);
            Num(sb, "pulse", GameState.Pulse);
            Num(sb, "time", RoomClock.Time);
            var p = PlayerRef.Position;
            sb.Append("\"pos\":[").Append(p.x.ToString("0.###", Inv)).Append(',')
              .Append(p.y.ToString("0.###", Inv)).Append(',').Append(p.z.ToString("0.###", Inv)).Append("],");
            sb.Append("\"prompt\":");
            if (GameState.InteractPrompt == null) sb.Append("null");
            else sb.Append('"').Append(GameState.InteractPrompt.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            sb.Append(",\"results\":[");
            for (int i = 0; i < GameState.Results.Length; i++)
            {
                var r = GameState.Results[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"grade\":\"").Append(r.grade == '\0' ? "" : r.grade.ToString()).Append("\",\"time\":")
                  .Append(r.time.ToString("0.###", Inv)).Append(",\"traces\":").Append(r.traces)
                  .Append(",\"flips\":").Append(r.flips).Append(",\"bestCombo\":").Append(r.bestCombo).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static void Num(StringBuilder sb, string k, float v) => sb.Append('"').Append(k).Append("\":").Append(v.ToString("0.###", Inv)).Append(',');
        static void Num(StringBuilder sb, string k, int v) => sb.Append('"').Append(k).Append("\":").Append(v).Append(',');
        static void Bool(StringBuilder sb, string k, bool v) => sb.Append('"').Append(k).Append("\":").Append(v ? "true" : "false").Append(',');
    }
}
