# Chunks and room generation

Generated rooms are stitched from hand-authored 8 m chunks. Chunk data lives in
`DESYNC/Assets/Resources/Content/chunks/*.json` (`act1.json`, `act2.json`, `act3.json`, plus `connectors.json` for
act-0 chunks usable in any act). Code: `DESYNC/Assets/Scripts/Gen` (`RoomGen` builds, `RoomCheck` validates,
`RunGen` builds the run map, `ChunkLint` checks authoring). Tests: `Assets/Tests/EditMode/GenTests.cs` (GEN-01..07)
and `Assets/Scripts/Gen/Tests/GenRulesTests.cs`.

## Scale

- A chunk is 16 x 16 tiles at 0.5 m = 8 m square. Row 0 is north, column 0 is west.
- Room grids (chunks): Act 1 3 x 3 (24 m), Act 2 4 x 3 (32 x 24 m), Act 3 4 x 4 (32 m). Width x height.
- Room space for entities: origin at the room's north-west corner, +x east, -z south, metres.
  Tile (row r, column c) has its centre at x = (c + 0.5) * 0.5, z = -(r + 0.5) * 0.5. Yaw 0 = north, 90 = east.

## Legend

| Char | Meaning | RENDER | SOURCE |
| --- | --- | --- | --- |
| `.` | floor | walk | walk |
| `#` | render wall (walls, furniture, glass, racks) | solid | passable |
| `X` | solid in both layers (structure, pillars, chunk borders) | solid | solid |
| `A` | anomaly wall: a render wall with the glyph flicker, leading to a SOURCE-only pocket | solid | passable |
| `T` | terminal spot (the kiosk stands on this tile; the player hacks it from here) | walk | walk |
| `C` | camera mount (floor tile against a wall) | walk | walk |
| `P` | patrol waypoint (a cell's P markers form one drone loop) | walk | walk |
| `I` | ICE spawn | walk | walk |
| `G` | glitch zone centre | walk | walk |
| `S` / `E` | optional spawn / exit spot (otherwise the clear floor tile nearest the chunk centre is used) | walk | walk |
| ` ` | void (only in rooms: cells dropped as unreachable) | - | - |

Builders: every tile that is not `#`, `X`, `A` or ` ` is floor. `#` and `A` tiles also need floor under them (the
player walks through them in SOURCE).

## Borders and sockets

Each chunk declares a socket per side: `door`, `source_only` or `wall`. Borders are structural and follow one
convention (the lint test enforces it, and the generator re-stamps them):

- The border row/column is `X`, except tiles 6..9 (the middle 2 m) which are `.` for a door, `#` for a
  source_only gate (a thin render wall: walk through it in SOURCE) and `X` for a wall.
- Two neighbouring chunks must show the same socket on the shared edge (door-door, gate-gate, wall-wall).
  Edges on the room perimeter, or facing a void cell, are stamped solid.
- Chunks may be placed in any of 8 orientations (quarter turns, optionally mirrored); sockets turn with the tiles.
  Camera and terminal facings are computed after placement, so orientation never matters to authors.

## Authoring rules (`ChunkLint`)

- 16 rows of 16 characters from the legend; ids unique; act 0..3.
- Every tile walkable in SOURCE connects to the sockets (no sealed pockets).
- Passages are at least 3 tiles (1.5 m) wide: every walkable interior tile sits in an open 3 x 3 block. Prefer 4+.
- `C` sits next to a wall; `P` markers come in groups of 2+ per chunk with clear straight legs between them
  (drones fly the loop in angular order around the markers' centre).
- `budget` is the most threat cost the generator may place in this chunk (it spreads threats over the room).
  Chunks with markers need a budget above 0.
- Tag `side` keeps a chunk off the solution path (use it for anomaly pockets and dead ends).
- Themes: offices/pods in Act 1, server rows and cooling in Act 2, abstract mind-space in Act 3. Thin `#` walls go
  where a flip is the clever route; `X` pillars sparingly. Each act has at least one `side` chunk with an `A` wall.

## Generating a room

`Generator.GenerateRoom(seed, act, node, heat, lib)` — all randomness from `Rng.Substream(seed, name)`:

1. **Path** (`path`): Spelunky walk. Start on a random column of row 0; each step goes left or right (p 0.4 each)
   or down (0.2); a blocked or revisiting sideways step drops down instead; going down from the last row ends the
   path there. Consecutive path cells need door sockets on the shared edge.
2. **Shortcut** (Act 2+): one `source_only` edge between two path cells that are adjacent but at least 3 steps apart
   on the path, so a flip skips part of the route. Paths without such a pair are redrawn.
3. **Chunks** (`chunks`): backtracking over the cells, path cells first, with forward checking (a placement is
   kept only if every open neighbour still has a chunk that fits). Candidates: the act's chunks plus connectors, in
   all 8 orientations. Preference: act chunks over connectors, unused chunks over repeats, and off-path cells that
   open onto a neighbour. One path cell (not the first) must take a chunk with a `T`. Off-path cells that no door or
   gate connects to the path become void.
4. **Mirror** (`mirror`): with p 0.5 the whole room is flipped east-west.
5. **Spawn and exit**: in the first and last path cells (`S` / `E` markers, else the clear floor tile nearest the
   chunk centre). Spawn faces the next path cell.
6. **Terminals**: `T` markers on path cells, distinct cells first; Act 1: 1 (2 from node 2), Act 2: 2,
   Act 3: 2 (3 from node 2). Ordered along the path; ids `TRM-1`...
7. **Threats** (`threats`): budget B = 10 + 6 x act + 2 x node + heat / 2. Marker slots are shuffled; each takes a
   random threat that fits its marker, the remaining budget, the chunk's budget and the room's enemy-type cap
   (3 / 4 / 5 types in Acts 1 / 2 / 3; glitch zones do not count as a type). Costs and first appearance
   (r1 = node 0):

   | Threat | Marker | Cost | From | Id |
   | --- | --- | --- | --- | --- |
   | Fixed camera (60 deg, 12 m) | C | 2 | A1 node 0 | CAM-n |
   | Sweep camera (90 deg arc, 10 m) | C | 3 | A1 node 1 | CAM-n |
   | Guard drone | P | 4 | A1 node 2 | DRN-n |
   | ICE hunter (vent) | I | 4 | A1 node 0 | ICE-n |
   | Tracker ICE | I | 5 | A1 node 3 | ICE-n |
   | Lantern drone | P | 4 | A2 node 0 | DRN-n |
   | Sentinel ICE | I | 4 | A2 node 1 | ICE-n |
   | Splitter ICE | I | 5 | A2 node 3 | ICE-n |
   | Glitch zone | G | 2 | A2 node 0 | GLT-n |
   | Daemon | I | 7 | A3 node 0 | DMN-n |
   | Watcher eye | C | 6 | A3 node 1 | EYE-n |

   The Auditor drone (cost 0) is spawned at TRACE 2 by the game, not placed. Placement rules: no camera or watcher
   whose cone covers the spawn or a terminal's 3 x 3 hacking spot; no ICE or glitch zone within 4.5 m of the spawn;
   no glitch zone within 2.5 m of a terminal; a patrol needs clear legs. Cameras and watchers face the compass
   direction (45 deg steps) with the longest clear view; terminals face the longest of the four cardinal views.
8. **Anomaly**: at most one `A` run survives (entity `ANM-1` at its middle tile); other `A` runs become `#`.
9. **Validate** (below). A rejected room is rebuilt from `Rng.Substream(seed, "attempt" + k)` up to 20 attempts in
   total, then `fallbackRoom` names an original room for the act (`Room_01`/`Room_02`, `Room_03`/`Room_04`,
   `Room_05`/`Room_06` by seed parity) and `tiles` is null.
10. **Par** = best route x 1.6, rounded to 5 s, at least 45 s; at most 75 s in Acts 1-2 (no upper clamp in Act 3).

Output (`RoomSpec`): `tiles` keeps the markers of placed entities (`S`, `E`, `T`, `C`, `P`, `I`, `G`, `A`); unused
markers become `.`. Drones and lanterns carry their loop in `path` (x, z pairs) and start on the first waypoint.
Glitch zones are a centre point; the builder chooses the radius.

## How the validator judges a room (`Generator.Validate`)

- **Reachability**: BFS over (tile, layer). A flip is possible on any RENDER floor tile, so this is connectivity
  over SOURCE-walkable tiles. Every terminal and the exit must be reached (`unreachable_terminal`, `unreachable_exit`).
- **Camera cones**: every fixed (60 deg, 12 m) and sweep camera (the full 90 deg arc, 10 m) marks the RENDER floor
  tiles it can see along an unblocked line (`#`, `X`, `A` block sight). Watchers, drones and lanterns are not part of
  the check. The spawn and terminals' 3 x 3 spots must be unseen (`spawn_in_cone`, `terminal_in_cone`).
- **Route**: A* over (next target, tile, layer, 5 % melt bucket) at 5 m/s, 8-way moves without corner cutting:
  spawn -> each terminal in order (hacked standing in RENDER for 2 s, melt draining) -> exit (either layer).
  Melt uses the Rules rates (SOURCE +12 %/s, RENDER -4 %/s, flip +5 % before the change) and may never reach 85 %
  (`melt_budget`). The route may never stand in RENDER inside a camera cone, so cones are crossed in SOURCE.
  Among equal times the cooler route wins. Time and melt are fixed-point integers, so every platform accepts the
  same rooms.
- **Reported**: `bestPathSeconds` (including hack time), `minMeltPeak` (peak melt on that route),
  `maxCameraExposureOnBestPath` (longest continuous RENDER time inside a cone on that route; must stay < 0.4 s,
  `camera_exposure`), `flipFreePathExists` (spawn, terminals and exit connected in RENDER outside every cone).
- **ICE**: `ice_near_entry` when any ICE-type spawn (vent, tracker, sentinel, splitter, daemon) is within 4 m.

## Run map (`Generator.GenerateRun`)

Per act: 4 rows of 2-3 nodes on 3 lanes, then a boss. Edges go only to the next row: each node links to the lanes
within one step and is topped up with the nearest lanes to at least two choices; every next-row node gets a parent.
Node types: Infiltration 60, Elite 15, Black Market 10, Cooldown 10, Anomaly 5 (weights); Act 1 row 0 is always
Infiltration; at most one Anomaly per act. Ids are `a{act}n{k}` in reading order (boss last); room seeds are
`Rng.Substream(seed, id).NextUInt()`. The boss has no `next`: the run moves to the next act after the cash-out choice.

## Adding a chunk

Copy a chunk in the act file, give it a new id, keep the border convention, set sockets, budget and tags, then run
`tools/unity.sh test gen EditMode "GenTests|GenRulesTests"` (the lint test names the offending tile).

## Chunk previews

### Act 1 — HALCYON offices

**a1_open_office** — N=door E=door S=door W=door; budget 8; tags: office

```
XXXXXX....XXXXXX
XC.........T...X
X..............X
X..............X
X...##....##...X
X...##....##...X
..P..........P..
................
................
..P..........P..
X...##....##...X
X...##....##...X
X..............X
X.I..........I.X
X.............CX
XXXXXX....XXXXXX
```

**a1_cubicle_pods** — N=door E=door S=door W=door; budget 8; tags: office, pods, flip

```
XXXXXX....XXXXXX
X#####....#####X
X#...#....#...#X
X#.T.#....#.I.#X
X#...#....#...#X
X#####C...#####X
................
..P..........P..
..P..........P..
................
X#####...C#####X
X#...#....#...#X
X#.I.#....#.T.#X
X#...#....#...#X
X#####....#####X
XXXXXX....XXXXXX
```

**a1_reception** — N=door E=wall S=door W=door; budget 7; tags: office, lobby

```
XXXXXX....XXXXXX
XC............CX
X..............X
X..............X
X..............X
X...########...X
....########...X
...............X
...............X
...............X
X...XX....XX...X
X...XX....XX...X
X..............X
X.T..........I.X
X.....P..P.....X
XXXXXX....XXXXXX
```

**a1_meeting_room** — N=door E=door S=wall W=wall; budget 7; tags: office, glass, flip

```
XXXXXX....XXXXXX
X..............X
X.P..........P.X
X..............X
X..............X
X.P..........P.X
X...............
X########.......
X.......#.......
X.......#.......
X...T...#..I...X
X.......#......X
X.......#......X
X.......#......X
X.......#.....CX
XXXXXXXXXXXXXXXX
```

**a1_office_ring** — N=wall E=door S=door W=wall; budget 7; tags: office, flip

```
XXXXXXXXXXXXXXXX
X..............X
X.P..........P.X
X.......I......X
X...########...X
X...#......#...X
X...#......#....
X...#..T...#....
X...#......#....
X...#......#....
X...########...X
X..............X
X.P..........P.X
X..............X
XC.............X
XXXXXX....XXXXXX
```

**a1_server_closet** — N=door E=source_only S=door W=wall; budget 7; tags: office, closet, flip

```
XXXXXX....XXXXXX
X........C#....X
X.........#....X
X.........#.I..X
X...XX....#....X
X...XX....#....X
X.........#....#
X.........#..T.#
X.........#....#
X.........#....#
X...XX....#....X
X...XX....#....X
X.........#....X
X.........#....X
XC........#....X
XXXXXX....XXXXXX
```

**a1_storage** — N=wall E=wall S=wall W=door; budget 4; tags: office, side, anomaly

```
XXXXXXXXXXXXXXXX
X........X.....X
X........A.....X
X........A.....X
X........A.....X
X........X.....X
.........XXXXXXX
...............X
...............X
.............I.X
X...##....##...X
X...##....##...X
X..............X
X..............X
XC.............X
XXXXXXXXXXXXXXXX
```

**a1_bullpen** — N=door E=door S=door W=door; budget 8; tags: office, partitions

```
XXXXXX....XXXXXX
XC.............X
X...........T..X
X..............X
X..............X
X#####....#####X
................
................
................
................
X#####....#####X
X..............X
X..I...........X
X..............X
X.............CX
XXXXXX....XXXXXX
```

**a1_print_hub** — N=wall E=door S=source_only W=door; budget 7; tags: office, print

```
XXXXXXXXXXXXXXXX
XC............CX
X..............X
X..............X
X...P......P...X
X..............X
......####......
......####......
......####......
......####......
X..............X
X...P......P...X
X.I..........T.X
X..............X
X..............X
XXXXXX####XXXXXX
```

**a1_atrium** — N=door E=door S=door W=source_only; budget 8; tags: office, atrium

```
XXXXXX....XXXXXX
XC............CX
X..............X
X..............X
X...XX....XX...X
X...XX....XX...X
#.P..........P..
#......XX.......
#......XX.......
#.P..........P..
X...XX....XX...X
X...XX....XX...X
X..............X
X.T..........I.X
X..............X
XXXXXX....XXXXXX
```

### Act 2 — server core

**a2_server_rows** — N=door E=door S=door W=door; budget 10; tags: servers

```
XXXXXX....XXXXXX
XC............CX
X..............X
X..............X
X...########...X
X...########...X
................
...T.......I....
................
................
X...########...X
X...########...X
X..............X
X.P..........P.X
X..............X
XXXXXX....XXXXXX
```

**a2_cold_aisle** — N=door E=source_only S=door W=source_only; budget 9; tags: servers, aisle, flip

```
XXXXXX....XXXXXX
X...##....##...X
X...##....##...X
X...##....##...X
X...##....##.I.X
X...##....##...X
#...##....##...#
#.T.##....##...#
#...##....##...#
#...##....##...#
X...##.G..##...X
X.I.##....##...X
X...##....##...X
X...##....##...X
X...##...C##...X
XXXXXX....XXXXXX
```

**a2_cooling_plant** — N=door E=door S=wall W=wall; budget 9; tags: cooling

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X...XXX........X
X...XXX....T...X
X...XXX.........
X.P..........P..
X...............
X........XXX....
X........XXX...X
X.I......XXX..GX
X..............X
X..............X
X.............CX
XXXXXXXXXXXXXXXX
```

**a2_rack_maze** — N=door E=door S=door W=door; budget 10; tags: servers, maze, flip

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X##########....X
X##########....X
................
.......T...G....
................
.....##########.
X....##########X
X..............X
X..P.......P...X
X............I.X
X..............X
XXXXXX....XXXXXX
```

**a2_coolant_tank** — N=door E=source_only S=wall W=door; budget 9; tags: cooling

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X..............X
X......##......X
......XXXX.....#
.....#XXXX#....#
.....#XXXX#....#
......XXXX.....#
X......##......X
X..............X
X..T.......I...X
X..............X
X.............CX
XXXXXXXXXXXXXXXX
```

**a2_server_hall** — N=wall E=door S=door W=door; budget 10; tags: servers

```
XXXXXXXXXXXXXXXX
X...##C...##...X
X...##....##...X
X...##....##.T.X
X...##....##...X
X..............X
..P..........P..
................
................
..P..........P..
X..............X
X...##....##...X
X.I.##....##...X
X...##.G..##...X
X...##....##...X
XXXXXX....XXXXXX
```

**a2_cable_vault** — N=wall E=wall S=door W=wall; budget 4; tags: cables, side, anomaly

```
XXXXXXXXXXXXXXXX
X.....X........X
X.....A........X
X.....A........X
X.....A....I...X
X.....X........X
XXXXXXX........X
X..............X
X..............X
X..............X
X...##....##...X
X...##....##...X
X..............X
XC.............X
X.............CX
XXXXXX....XXXXXX
```

**a2_ups_room** — N=door E=door S=source_only W=wall; budget 9; tags: power

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X...##....##...X
X...##....##...X
X...............
X.....G...T.....
X...............
X...............
X...##....##...X
X...##....##...X
X..............X
X.I..........I.X
X.............CX
XXXXXX####XXXXXX
```

**a2_server_cage** — N=door E=door S=door W=source_only; budget 10; tags: servers, cage, flip

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X...########...X
X...#......#...X
#...#......#....
#...#..T...#....
#...#......#....
#...#......#....
X...########...X
X..............X
X.P..........P.X
X...I......G...X
X.............CX
XXXXXX....XXXXXX
```

**a2_pump_room** — N=source_only E=door S=door W=source_only; budget 9; tags: cooling, pumps

```
XXXXXX####XXXXXX
XC.............X
X..............X
X..............X
X...XX....XX...X
X...XX....XX...X
#...............
#.....####...T..
#.....####......
#...............
X...XX....XX...X
X...XX....XX...X
X..............X
X.I.......G....X
X..............X
XXXXXX....XXXXXX
```

### Act 3 — the Architect's mind

**a3_ring** — N=door E=door S=door W=door; budget 12; tags: mind, ring, flip

```
XXXXXX....XXXXXX
XC............CX
X..............X
X..............X
X...########...X
X...#......#...X
....#......#....
................
.......T........
................
X...#......#...X
X...########...X
X..............X
X.I..........I.X
X..............X
XXXXXX....XXXXXX
```

**a3_switchback** — N=door E=wall S=door W=wall; budget 11; tags: mind, switchback, flip

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X..............X
X##########....X
X..............X
X.....G....T...X
X..............X
X..............X
X....##########X
X..............X
X.I............X
X..............X
X.............CX
XXXXXX....XXXXXX
```

**a3_lattice** — N=door E=door S=door W=door; budget 12; tags: mind, lattice

```
XXXXXX....XXXXXX
XC............CX
X..............X
X..............X
X...X......X...X
X..............X
................
......G.........
.........T......
................
X..............X
X...X......X...X
X..............X
X.I..........I.X
X..............X
XXXXXX....XXXXXX
```

**a3_mirror_halls** — N=wall E=door S=wall W=door; budget 11; tags: mind, mirror, flip

```
XXXXXXXXXXXXXXXX
XC............CX
X..............X
X..............X
X#####....#####X
X..............X
................
................
................
................
X..............X
X#####....#####X
X..............X
X.I.........T..X
X.............CX
XXXXXXXXXXXXXXXX
```

**a3_bridge** — N=door E=source_only S=door W=source_only; budget 11; tags: mind, bridge

```
XXXXXX....XXXXXX
X...XX....XX...X
X...XX....XX...X
X...XX....XX.I.X
X...XX....XX...X
X...XX....XX...X
#.T.##....##...#
#...##....##...#
#...##....##...#
#...##....##...#
X...XX....XX...X
X...XX....XX...X
X...XX....XX...X
X...XX....XX...X
X...XX...CXX...X
XXXXXX....XXXXXX
```

**a3_eye** — N=door E=door S=door W=wall; budget 12; tags: mind, eye, flip

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X.....####.....X
X....#....#....X
X...#......#....
X...#..T...#....
X...#......#....
X...#......#....
X....#....#....X
X.....####.....X
X..............X
X.I..........I.X
X.............CX
XXXXXX....XXXXXX
```

**a3_memory_cells** — N=door E=door S=door W=door; budget 12; tags: mind, cells, flip

```
XXXXXX....XXXXXX
X#####....#####X
X#...#....#...#X
X#.T.#....#.I.#X
X#...#....#...#X
X#####C...#####X
................
..P..........P..
..P..........P..
................
X#####...C#####X
X#...#....#...#X
X#.I.#....#.G.#X
X#...#....#...#X
X#####....#####X
XXXXXX....XXXXXX
```

**a3_glyph** — N=door E=door S=source_only W=wall; budget 11; tags: mind, glyph

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X...#.....#....X
X...#.....#....X
X...#.....#.....
X...#######.....
X...#.....#.T...
X...#.....#.....
X...#.....#....X
X..............X
X..............X
X.I.......G....X
X.............CX
XXXXXX####XXXXXX
```

**a3_thought_loop** — N=wall E=door S=wall W=wall; budget 5; tags: mind, side, anomaly

```
XXXXXXXXXXXXXXXX
X..............X
X..............X
X..............X
X...XAAAX......X
X...X...X......X
X...X...X.......
X...X...X.......
X...XXXXX.......
X...............
X..............X
X.I............X
X..............X
X..............X
XC............CX
XXXXXXXXXXXXXXXX
```

**a3_cathedral** — N=door E=source_only S=door W=door; budget 12; tags: mind, nave

```
XXXXXX....XXXXXX
XC............CX
X..............X
X..............X
X.....####.....X
X.....####.....X
...............#
.......T.......#
....X......X...#
...............#
X..............X
X.P..........P.X
X..............X
X.I..........G.X
X..............X
XXXXXX....XXXXXX
```

### Connectors (act 0, any act)

**c_cross** — N=door E=door S=door W=door; budget 4; tags: connector

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X...XX....XX...X
X...XX....XX...X
................
................
................
................
X...XX....XX...X
X...XX....XX...X
X..............X
X.I............X
X..............X
XXXXXX....XXXXXX
```

**c_tee** — N=door E=door S=door W=wall; budget 4; tags: connector

```
XXXXXX....XXXXXX
X..............X
X..............X
X..............X
X...##.........X
X...##.........X
X...##..........
X...##.....T....
X...##..........
X...##..........
X...##.........X
X...##.........X
X..............X
X..............X
XC............IX
XXXXXX....XXXXXX
```

**c_corner** — N=door E=door S=wall W=wall; budget 4; tags: connector

```
XXXXXX....XXXXXX
X..............X
X..............X
X..............X
X..............X
X..............X
X...............
X...............
X...............
X...######......
X...######.....X
X...######.....X
X..............X
X.I............X
XC.............X
XXXXXXXXXXXXXXXX
```

**c_straight** — N=door E=wall S=door W=wall; budget 4; tags: connector

```
XXXXXX....XXXXXX
X..............X
X..............X
X..............X
X...XX....XX...X
X...XX....XX...X
X..............X
X..T...........X
X..............X
X..............X
X...XX....XX...X
X...XX....XX...X
X..............X
X.............IX
XC.............X
XXXXXX....XXXXXX
```

**c_dead_end** — N=door E=wall S=wall W=wall; budget 3; tags: connector

```
XXXXXX....XXXXXX
X..............X
X..............X
X..............X
X..............X
X..............X
X...######.....X
X...#....#.....X
X...#.T..#.....X
X...#....#.....X
X...######.....X
X..............X
X..............X
X.I............X
X.............CX
XXXXXXXXXXXXXXXX
```

**c_src_corner** — N=door E=door S=source_only W=source_only; budget 4; tags: connector, source

```
XXXXXX....XXXXXX
X..............X
X..............X
X..............X
X..............X
X...##.........X
#...##..........
#...##..........
#...##..........
#...########....
X...########...X
X..............X
X..............X
X.........I....X
XC.............X
XXXXXX####XXXXXX
```

**c_src_straight** — N=door E=source_only S=door W=source_only; budget 4; tags: connector, source

```
XXXXXX....XXXXXX
X...#......#...X
X...#......#...X
X...#......#...X
X...#......#...X
X...#..T...#...X
#...#......#...#
#...#......#...#
#...#......#...#
#...#......#...#
X...#......#...X
X.I.#......#...X
X...#......#...X
X...#......#...X
X...#.....C#...X
XXXXXX....XXXXXX
```

**c_src_tee** — N=door E=door S=door W=source_only; budget 4; tags: connector, source

```
XXXXXX....XXXXXX
XC.............X
X..............X
X..............X
X..............X
X..............X
#######.........
#...............
#...............
#...............
X######........X
X..............X
X..............X
X............I.X
X..............X
XXXXXX....XXXXXX
```

**c_src_end** — N=door E=source_only S=wall W=wall; budget 3; tags: connector, source

```
XXXXXX....XXXXXX
X..............X
X..............X
X..............X
X..............X
X..............X
X.........#....#
X.........#....#
X.........#....#
X.........#....#
X..............X
X..............X
X..T...........X
X..............X
XC............IX
XXXXXXXXXXXXXXXX
```
