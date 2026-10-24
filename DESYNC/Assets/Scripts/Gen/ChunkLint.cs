using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Desync.Gen.Tests")]

namespace Desync.Gen
{
    /// Authoring checks for one chunk (docs/CHUNKS.md lists the rules). Empty list = fine.
    internal static class ChunkLint
    {
        const string Legal = ".#XTCPIGASE";
        const int N = RoomGen.N;

        internal static List<string> Check(Chunk c)
        {
            var e = new List<string>();
            if (string.IsNullOrEmpty(c.id)) e.Add("missing id");
            if (c.act < 0 || c.act > 3) e.Add("act out of range");
            if (c.tiles == null || c.tiles.Length != N) { e.Add("needs 16 rows"); return e; }
            for (int r = 0; r < N; r++)
            {
                if (c.tiles[r].Length != N) { e.Add($"row {r} has {c.tiles[r].Length} chars"); return e; }
                foreach (char ch in c.tiles[r]) if (Legal.IndexOf(ch) < 0) e.Add($"row {r}: illegal '{ch}'");
            }
            if (e.Count > 0) return e;

            // borders: 'X' except the socket opening (cols/rows 6..9): '.' door, '#' source_only, 'X' wall
            var kinds = new[] { c.n, c.e, c.s, c.w };
            int openings = 0, first = -1;
            for (int d = 0; d < 4; d++)
            {
                char open = kinds[d] == SocketKind.Door ? '.' : kinds[d] == SocketKind.SourceOnly ? '#' : 'X';
                if (kinds[d] != SocketKind.Wall) openings++;
                for (int i = 0; i < N; i++)
                {
                    int r = d == 0 ? 0 : d == 2 ? N - 1 : i, col = d == 3 ? 0 : d == 1 ? N - 1 : i;
                    bool mid = i >= RoomGen.DoorLo && i <= RoomGen.DoorHi;
                    char want = mid ? open : 'X';
                    if (c.tiles[r][col] != want) { e.Add($"border {"NESW"[d]} tile {i}: '{c.tiles[r][col]}' should be '{want}'"); break; }
                    if (mid && kinds[d] != SocketKind.Wall && first < 0) first = r * N + col;
                }
            }
            if (openings == 0) e.Add("no door or source_only socket");
            if (e.Count > 0) return e;

            // every tile walkable in SOURCE connects to the sockets (no sealed pockets)
            var mark = new bool[N * N];
            var stack = new Stack<int>();
            stack.Push(first); mark[first] = true;
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                for (int d = 0; d < 4; d++)
                {
                    int r = t / N + (d == 0 ? -1 : d == 2 ? 1 : 0), col = t % N + (d == 1 ? 1 : d == 3 ? -1 : 0);
                    if (r < 0 || r >= N || col < 0 || col >= N) continue;
                    int u = r * N + col;
                    if (!mark[u] && RoomCheck.WalksSource(c.tiles[r][col])) { mark[u] = true; stack.Push(u); }
                }
            }
            var ps = new List<int>();
            int markers = 0;
            for (int r = 0; r < N; r++)
                for (int col = 0; col < N; col++)
                {
                    char ch = c.tiles[r][col];
                    if (RoomCheck.WalksSource(ch) && !mark[r * N + col]) { e.Add($"tile {r},{col} sealed off"); return e; }
                    if (r == 0 || r == N - 1 || col == 0 || col == N - 1) continue;
                    if (!RoomCheck.BlocksRender(ch) && !InOpen3x3(c, r, col)) e.Add($"tile {r},{col} in a passage under 3 tiles (1.5 m)");
                    if (ch == 'C' && !NextToWall(c, r, col)) e.Add($"camera {r},{col} not mounted on a wall");
                    if (ch == 'P') ps.Add(r * N + col);
                    if ("TCPIG".IndexOf(ch) >= 0) markers++;
                }
            if (ps.Count == 1) e.Add("a patrol needs >= 2 P markers");
            if (markers > 0 && c.budget <= 0) e.Add("markers but no budget");
            return e;
        }

        static bool InOpen3x3(Chunk c, int r, int col)
        {
            for (int r0 = r - 2; r0 <= r; r0++)
                for (int c0 = col - 2; c0 <= col; c0++)
                {
                    bool ok = r0 >= 0 && c0 >= 0 && r0 + 2 < N && c0 + 2 < N;
                    for (int dr = 0; ok && dr < 3; dr++)
                        for (int dc = 0; ok && dc < 3; dc++) ok = !RoomCheck.BlocksRender(c.tiles[r0 + dr][c0 + dc]);
                    if (ok) return true;
                }
            return false;
        }

        static bool NextToWall(Chunk c, int r, int col)
        {
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++) if (RoomCheck.BlocksRender(c.tiles[r + dr][col + dc])) return true;
            return false;
        }
    }
}
