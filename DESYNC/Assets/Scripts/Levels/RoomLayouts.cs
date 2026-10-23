using System.Collections.Generic;

namespace Desync.Levels
{
    /// ASCII legend shared by the room grids, the validator and LevelBuilder.
    /// Map orientation: row 0 = north (+z), column 0 = west (-x). Yaw 0 = north, 90 = east. One cell = RoomDef.Cell metres.
    public static class Legend
    {
        public const char Wall = '#', Floor = '.', Void = ' ', Spawn = 'S', Exit = 'X', Terminal = 'T', Vent = 'V',
            Glitch = 'G', HijackPad = 'H', Desk = 'D', Rack = 'R', Plant = 'P', Cabinet = 'K', Poster = '!',
            Billboard = 'B', Architect = 'A', Upper = '2', RampEast = '>', RampWest = '<';
        // digits 1-9 = cameras (RoomDef.cams); letters a-h = drone waypoints, patrolled in letter order.

        public static bool IsVoid(char c) => c == Void;
        public static bool IsCamera(char c) => c >= '1' && c <= '9';
        public static bool IsWaypoint(char c) => c >= 'a' && c <= 'h';
        /// Cells whose content is solid in RENDER (WallRender collider): walls and furniture. Terminals are solid kiosks.
        public static bool Blocks(char c) => c == Wall || c == Desk || c == Rack || c == Plant || c == Cabinet || c == Terminal;
        public static int RampDir(char c) => c == RampEast ? 1 : c == RampWest ? -1 : 0;
    }

    /// Wall/ceiling camera placed on the cell marked with `id` (a digit).
    public sealed class CamDef
    {
        public readonly char id; public readonly float yaw, phase, pitch, height;
        public CamDef(char id, float yaw, float phase = 0f, float pitch = 30f, float height = 3.1f)
        { this.id = id; this.yaw = yaw; this.phase = phase; this.pitch = pitch; this.height = height; }
    }

    public sealed class RoomDef
    {
        public const float Cell = 2f;
        public int index = -1;               // 0..5, -1 for Title/Ending backdrops
        public string name;
        public string[] grid;
        public string[] levels;              // optional, same size: '2' upper floor, '>'/'<' ramp rising east/west
        public float spawnYaw, spawnNudge;   // nudge = metres forward from the spawn cell centre
        public float wallHeight = 3.5f, upperHeight = 3f;
        public int storyStart;               // storyIndex of the first terminal (reading order)
        public float traceCountdown = -1f;
        public CamDef[] cams = new CamDef[0];
        public float dronePhase;
        public string decal; public float decalAhead = 2f;   // floor text in front of the spawn (Room 1)
        public string[] posters;             // poster slogans (cycled); null = Posters.Default

        public int Rows => grid.Length;
        public int Cols => grid[0].Length;
        public char At(int c, int r) => r < 0 || r >= Rows || c < 0 || c >= Cols ? Legend.Void : grid[r][c];
        public char LevelAt(int c, int r) => levels == null || r < 0 || r >= Rows || c < 0 || c >= Cols ? Legend.Floor : levels[r][c];

        public int Count(char ch) { int n = 0; foreach (var row in grid) foreach (var x in row) if (x == ch) n++; return n; }

        public bool Find(char ch, out int c, out int r)
        {
            for (r = 0; r < Rows; r++) for (c = 0; c < Cols; c++) if (grid[r][c] == ch) return true;
            c = r = -1; return false;
        }

        /// Drone waypoint cells in patrol order (a, b, c ...).
        public List<(int c, int r)> Waypoints()
        {
            var list = new List<(int c, int r)>();
            for (char w = 'a'; w <= 'h'; w++) if (Find(w, out int c, out int r)) list.Add((c, r));
            return list;
        }

        /// Structural problems (bad sizes, unknown chars, missing camera defs); empty when fine.
        public List<string> Lint()
        {
            var e = new List<string>();
            const string known = "#. SXTVGHDRPK!BA";
            for (int r = 0; r < Rows; r++)
            {
                if (grid[r].Length != Cols) e.Add($"row {r} length {grid[r].Length} != {Cols}");
                if (levels != null && (levels.Length != Rows || levels[r].Length != Cols)) e.Add($"levels row {r} size mismatch");
                foreach (var ch in grid[r])
                    if (known.IndexOf(ch) < 0 && !Legend.IsCamera(ch) && !Legend.IsWaypoint(ch)) e.Add($"unknown char '{ch}' row {r}");
            }
            if (index >= 0 && Count(Legend.Spawn) != 1) e.Add("needs exactly one S");
            foreach (var cam in cams) if (Count(cam.id) != 1) e.Add($"camera '{cam.id}' must appear once");
            for (char d = '1'; d <= '9'; d++)
                if (Count(d) > 0 && System.Array.Find(cams, x => x.id == d) == null) e.Add($"camera '{d}' has no CamDef");
            if (Count(Legend.HijackPad) > 0 && Waypoints().Count < 2) e.Add("hijack pad without a drone");
            return e;
        }
    }

    public static class Posters
    {
        // The poster texture already carries the HALCYON emblem; these fill its empty lower panel.
        public static readonly string[] Default =
        {
            "PRODUCTIVITY\nIS PEACE", "YOUR DATA IS\nSAFE WITH US", "WE SEE\nYOU THRIVE",
            "COMPLIANCE\nIS COMFORT", "THE CITY\nIS FINE", "SMILE. YOU ARE\nRENDERED",
        };
    }

    /// The six rooms (PRD 5.5) plus the Title/Ending backdrops. One new idea per room; story indices R1 0, R2 1,
    /// R3 2-3, R4 4-5, R5 6-7, R6 8-10. Run GridValidator on every room (LevelBuilder logs the results).
    public static class RoomLayouts
    {
        public static readonly RoomDef[] Rooms =
        {
            // R1 BOOT: spawn faces a dead-end wall 4 m ahead; the floor says SPACE = SEE THE CODE. Phase through, hack, exit.
            new RoomDef
            {
                index = 0, name = "BOOT", storyStart = 0, spawnYaw = 0f, spawnNudge = 1f,
                decal = "SPACE = SEE THE CODE", decalAhead = 2.9f,
                grid = new[]
                {
                    "  #######  ",
                    "  #!.X.!#  ",
                    "  #.....#  ",
                    "  #P.T.P#  ",
                    "  #.....#  ",
                    "  ##...##  ",
                    "   #...#   ",
                    "   #####   ",
                    "   #.!.#   ",
                    "   #...#   ",
                    "   #.S.#   ",
                    "   #####   ",
                },
            },

            // R2 EYES: L-corridor watched by a corner camera, office pods under a second camera. Flip to blind them,
            // or phase straight through the office wall.
            new RoomDef
            {
                index = 1, name = "EYES", storyStart = 1, spawnYaw = 0f,
                cams = new[] { new CamDef('1', 180f), new CamDef('2', 180f, 1.5f) },
                grid = new[]
                {
                    "###################",
                    "#..1....!......X..#",
                    "#.................#",
                    "#..###########..###",
                    "#..# #!.......2..!#",
                    "#..# #.D.D..D.D...#",
                    "#..# #............#",
                    "#..# #.D.D..D.D.P.#",
                    "#..# #............#",
                    "#..# #P...!....T..#",
                    "#..# #..........K.#",
                    "#..# ##############",
                    "#.S#               ",
                    "####               ",
                },
            },

            // R3 ICE: open server hall. Terminals sit inside closed rack pens: phase in, ICE pours from the vent,
            // flip back to RENDER inside the pen to live, hack, phase out. The camera owns the centre aisle.
            new RoomDef
            {
                index = 2, name = "ICE", storyStart = 2, spawnYaw = 0f,
                cams = new[] { new CamDef('1', 0f, 0f, 35f) },
                grid = new[]
                {
                    "#####################",
                    "#P...!........!....X#",
                    "#...................#",
                    "#.RRRRRR.....RRRRRR.#",
                    "#.R....R.....R....R.#",
                    "#.R.T..R.....R..T.R.#",
                    "#.R....R.....R....R.#",
                    "#.RRRRRR..V..RRRRRR.#",
                    "#.........1.........#",
                    "#..RRRR.......RRRR..#",
                    "#...................#",
                    "#!.......S.........!#",
                    "#####################",
                },
            },

            // R4 HIJACK: two-storey atrium. Ground: drone loop, terminal, hijack pad, vent. Mezzanine (Floor slab, 3 m,
            // railings) holds the cameras, the second terminal and the exit; one ramp up. Both layers are lethal.
            new RoomDef
            {
                index = 3, name = "HIJACK", storyStart = 4, spawnYaw = 0f, wallHeight = 6.5f, upperHeight = 3f,
                cams = new[] { new CamDef('1', 180f, 0f, 45f), new CamDef('2', 225f, 1.5f, 40f) },
                dronePhase = 0f,
                grid = new[]
                {
                    "######################",
                    "#!T.........!....K..X#",
                    "#....................#",
                    "#.........1..........#",
                    "#....................#",
                    "#..a.......b.........#",
                    "#....................#",
                    "#!......V............#",
                    "#....................#",
                    "#.T................!.#",
                    "#..d.......c......2..#",
                    "#....H...............#",
                    "#.....S..............#",
                    "######################",
                },
                levels = new[]
                {
                    "######################",
                    "#22222222222222222222#",
                    "#22222222222222222222#",
                    "#22222222222222222222#",
                    "#................2222#",
                    "#................2222#",
                    "#................2222#",
                    "#................2222#",
                    "#................2222#",
                    "#................2222#",
                    "#................2222#",
                    "#...........>>>>>2222#",
                    "#...........>>>>>2222#",
                    "######################",
                },
            },

            // R5 UNSTABLE: cubicle maze with glitch floors that force a flip after 3 s, a drone in the central loop,
            // three cameras and a 90 s TRACE countdown. Scrambled prompts in SOURCE.
            // The hijack pad (H) sits at the foot of the last cubicle lane, just outside camera 2's reach: hack it, and the
            // drone orbits you and jams the exit-run cones for 10 s, so the dash to X needs no flip (or the long way round).
            new RoomDef
            {
                index = 4, name = "UNSTABLE", storyStart = 6, spawnYaw = 90f, traceCountdown = 90f,
                cams = new[] { new CamDef('1', 180f), new CamDef('2', 270f, 1f), new CamDef('3', 0f, 2f) },
                grid = new[]
                {
                    "######################",
                    "#S.....1..#.......T..#",
                    "#.........#..........#",
                    "#..##..##.#..##..##..#",
                    "#..##..##....##..##..#",
                    "#...G.....a......b...#",
                    "#.........G.......!..#",
                    "####..##..##..##..####",
                    "#..#..##..##..##..#..#",
                    "#..G......V.......G..#",
                    "#.........d......c...#",
                    "#..##..##..##..##..#.#",
                    "#..##..##..##..##.2#.#",
                    "#.T..3....V...H.....X#",
                    "#.P.........G......K.#",
                    "######################",
                },
            },

            // R6 ROOT: circular core under the Architect's face. Its gaze sweeps both layers; three shield terminals in
            // closed pods (phase in, flip back, hack); two vents; HALCYON billboards around the wall. No exit: finale.
            new RoomDef
            {
                index = 5, name = "ROOT", storyStart = 8, spawnYaw = 0f, wallHeight = 7f,
                grid = new[]
                {
                    "      #######      ",
                    "    ##...B...##    ",
                    "   ##..#####..##   ",
                    "  #....#.T.#....#  ",
                    " ##....#...#....## ",
                    " #.....#####.....# ",
                    "#B...............B#",
                    "#.................#",
                    "#.....#.....#.....#",
                    "#V.......A.......V#",
                    "#.................#",
                    "#.#####.....#####.#",
                    "#.#.T.#.....#.T.#.#",
                    " ##...#.....#...## ",
                    " ######.....###### ",
                    "  #!...........!#  ",
                    "   ##....S....##   ",
                    "    ##...B...##    ",
                    "      #######      ",
                },
            },
        };

        /// Title backdrop: open office island around the origin (no RoomInfo/player). The UI orbits Camera.main.
        public static readonly RoomDef Title = new RoomDef
        {
            name = "TITLE",
            grid = new[]
            {
                "  #########  ",
                "  #!.RRR.!#  ",
                " P.........P ",
                " ..D.D.D.D.. ",
                " ........... ",
                " ..D.D.D.D.. ",
                " P.........P ",
                " ........... ",
            },
        };

        /// Ending backdrop: ring of HALCYON billboards under the Architect's head.
        public static readonly RoomDef Ending = new RoomDef
        {
            name = "ENDING", wallHeight = 7f,
            grid = new[]
            {
                "   #######   ",
                "  ##..B..##  ",
                " ##.......## ",
                " #B.......B# ",
                " #....A....# ",
                " #.........# ",
                " ##.......## ",
                "  ##.....##  ",
                "   #.....#   ",
            },
        };
    }
}
