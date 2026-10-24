using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Text;
using UnityEngine.Networking;
#endif

namespace Desync.Net
{
    public struct BoardRow { public int rank, score; public string tag; }

    /// A leaderboard page as the Daily screen shows it.
    public sealed class BoardView
    {
        public string board;
        public bool online;                                   // false: no store connected (503) or unreachable -> local best only
        public int total;                                     // players on the board
        public readonly List<BoardRow> top = new List<BoardRow>();
        public int yourRank, yourScore;                       // 0 when this player is not on the board
    }

    /// Client of web/api/leaderboard.ts. In a web build: fetch through Plugins/WebGL/DesyncNet.jslib (same origin), polled
    /// once a frame; elsewhere UnityWebRequest. Both are asynchronous and answer on the main thread; nothing ever blocks.
    public static class Leaderboard
    {
        public const string Endpoint = "api/leaderboard";
        public const int TimeoutSeconds = 10;

        /// Prefix of Endpoint: the page's own origin in a web build, the production site elsewhere. null = offline (tests).
#if UNITY_WEBGL && !UNITY_EDITOR
        public static string BaseUrl = "";
#else
        public static string BaseUrl = "https://desync-nine.vercel.app/";
#endif

        /// Top 50 of `board` and this player's rank. Always answers; offline / errors give online = false.
        public static void Fetch(string board, string playerId, Action<BoardView> done)
        {
            string q = "?board=" + Uri.EscapeDataString(board ?? "") + "&player=" + Uri.EscapeDataString(playerId ?? "");
            Send("GET", q, null, (status, text) => done(ParseBoard(board, status, text)));
        }

        /// Posts a ranked run. Answers the HTTP status (0 = unreachable / offline) and the rank the server gave (0 = none).
        public static void Submit(RunSubmission run, Action<int, int> done) =>
            Send("POST", "", LeaderboardScore.ToJson(run), (status, text) => done(status, Field(text, "rank")));

        public static BoardView ParseBoard(string board, int status, string text)
        {
            var v = new BoardView { board = board };
            if (status != 200) return v;
            try
            {
                var o = JObject.Parse(text);
                v.total = (int?)o["total"] ?? 0;
                if (o["top"] is JArray top)
                    foreach (var r in top)
                        v.top.Add(new BoardRow { rank = (int?)r["rank"] ?? 0, score = (int?)r["score"] ?? 0, tag = (string)r["tag"] ?? "" });
                if (o["you"] is JObject you) { v.yourRank = (int?)you["rank"] ?? 0; v.yourScore = (int?)you["score"] ?? 0; }
                v.online = true;
            }
            catch (Exception e) { Debug.LogWarning("[DESYNC] leaderboard reply unreadable: " + e.Message); }
            return v;
        }

        static int Field(string json, string name)
        {
            try { return (int?)JObject.Parse(json)[name] ?? 0; }
            catch (Exception) { return 0; }
        }

        /// `done(status, body)` on the main thread; status 0 when offline, unreachable or timed out.
        static void Send(string method, string query, string body, Action<int, string> done)
        {
            if (BaseUrl == null) { done(0, ""); return; }
            string url = BaseUrl + Endpoint + query;
#if UNITY_WEBGL && !UNITY_EDITOR
            Poller.Add(DesyncNet_Send(method, url, body ?? "", TimeoutSeconds * 1000), done);
#else
            var req = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            req.SendWebRequest().completed += _ =>
            {
                int status = req.result == UnityWebRequest.Result.ConnectionError ? 0 : (int)req.responseCode;
                string text = req.downloadHandler.text ?? "";
                req.Dispose();
                done(status, text);
            };
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int DesyncNet_Send(string method, string url, string body, int timeoutMs);
        [DllImport("__Internal")] static extern int DesyncNet_Status(int id);
        [DllImport("__Internal")] static extern string DesyncNet_Take(int id);

        /// Polls the browser's pending requests once a frame (no allocation while nothing is in flight).
        sealed class Poller : MonoBehaviour
        {
            static Poller I;
            readonly List<int> ids = new List<int>();
            readonly List<Action<int, string>> callbacks = new List<Action<int, string>>();

            public static void Add(int id, Action<int, string> done)
            {
                if (!I)
                {
                    var go = new GameObject("DesyncLeaderboard");
                    DontDestroyOnLoad(go);
                    I = go.AddComponent<Poller>();
                }
                I.ids.Add(id);
                I.callbacks.Add(done);
            }

            void Update()
            {
                for (int i = ids.Count - 1; i >= 0; i--)
                {
                    int status = DesyncNet_Status(ids[i]);
                    if (status < 0) continue;
                    string text = DesyncNet_Take(ids[i]);
                    var done = callbacks[i];
                    ids.RemoveAt(i);
                    callbacks.RemoveAt(i);
                    done(status, text);
                }
            }
        }
#endif
    }
}
