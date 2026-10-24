# DESYNC architecture

Unity 6000.4.6f1 with URP 17.4 and the new Input System only. WebGL2 is the primary target. The Unity project lives in `DESYNC/`. Namespaces: `Desync` (Core), `Desync.Rules`, `Desync.Content`, `Desync.Gen`, `Desync.Levels`, `Desync.Player`, `Desync.Enemies`, `Desync.Art`, `Desync.Audio`, `Desync.Flow`, `Desync.UI`.

## Assemblies

Runtime assemblies. Each is defined by an `.asmdef` in its folder under `DESYNC/Assets/`.

| Assembly | Folder | References | Engine-free |
| --- | --- | --- | --- |
| Desync.Rules | `Scripts/Rules` | none (uses Newtonsoft.Json) | yes |
| Desync.Content | `Scripts/Content` | Desync.Rules | yes |
| Desync.Gen | `Scripts/Gen` | Desync.Rules | yes (asmdef only so far) |
| Desync.Core | `Scripts/Core` | Desync.Rules, Input System | no |
| Desync.Levels | `Scripts/Levels` | Desync.Core | no |
| Desync.Art | `Scripts/Art` | Core, URP, PrimeTween | no |
| Desync.Audio | `Scripts/Audio` | Core, PrimeTween | no |
| Desync.Player | `Scripts/Player` | Core, Input System, PrimeTween, URP | no |
| Desync.Enemies | `Scripts/Enemies` | Core, Art, Input System, NavMesh package, TextMeshPro, PrimeTween | no |
| Desync.Flow | `Scripts/Flow` | Core, Player, Input System, PrimeTween, URP, UI | no |
| Desync.UI | `Scripts/UI` | Core, TextMeshPro, Input System, PrimeTween, uGUI | no |

Editor-only assemblies: `Desync.Build.Editor` (`Editor/Build`), `Desync.Setup.Editor` (`Editor/Setup`), `Desync.Levels.Editor` (`Editor/Levels`), plus `Desync.Art.Editor`, `Desync.Audio.Editor`, `Desync.Enemies.Editor` and `Desync.UI.Editor`.

Test assemblies: `Desync.Tests.EditMode` (`Assets/Tests/EditMode`, covers Rules, Gen and Content), `Desync.Levels.Tests`, `Desync.Audio.Tests` and `Desync.Audio.PlayTests`, `Desync.Enemies.EditorTests` and `Desync.Enemies.PlayTests`, `Desync.Flow.Tests` and `Desync.Flow.PlayTests`, `Desync.UI.Tests`.

Dependency rules:

- Rules has no engine references. Content and Gen depend only on Rules, so the balance simulator and the score server can reuse them.
- Core is the only home of shared state and events. Feature assemblies depend on Core, and on Art or Player where needed.
- Flow sits on top and drives the rooms.
- UI never references Flow. It raises request events (`StartRequested`, `RestartRequested`, `ContinueRequested`, and so on) that Flow handles.

## Shared runtime state (Core)

- **GameEvents** (`Core/GameEvents.cs`): static `Action` fields. Gameplay raises them with `GameEvents.X?.Invoke(...)`. Audio, art, UI and juice listen. Subscribe in `OnEnable`, unsubscribe in `OnDisable`.
- **GameState** (`Core/GameState.cs`): polled statics read every frame by the HUD, post-FX and audio. Each field names its one writer in a comment.
- **GameConfig.I** (`Core/GameConfig.cs`): all tunables. `Resources/GameConfig.asset` overrides the code defaults. Values are in `docs/BALANCE.md`.
- **RoomClock.Time**: deterministic room time. All schedules use it, so 30, 60 and 144 fps runs match.
- **LayerState**: `InSource` and `Set(bool)`. `Set` drives the global `_SOURCE` keyword and the Player to WallRender collision. `Changed` fires on every change, resets included. Never disable wall colliders to flip.
- **GameLayers**: physics layers and the collision matrix. Layer numbers are fixed in `CONTRACT.md`.
- **AssetLib.Get<T>(key)**: keyed shared assets from `Resources/AssetLib.asset`. Consumers accept null.
- **Settings**: sensitivity, shake, reduce FX and flashes, head bob, resolution scale, volume. Stored in PlayerPrefs.

## Rules API (pure C#, `Desync.Rules`)

Signatures are frozen. Bodies are still stubs (see `docs/CONTEXT.md`).

- `Melt.Step` and `Melt.Flip`: melt integration and the layer change. A flip adds `FlipCost` before the layer changes. Dead at 100 or more.
- `Modifiers`: every tunable a run can change, from exploits and mutators. `Default` is the base game. `Clamped()` enforces the floors and caps.
- `Scoring`: point constants, `GhostMultiplier`, `Grade`, `ScoreFromEvents` (replays a `RunEvent` log, and is the function the server will reuse), `TimeBonus`.
- `Medals.For` and `Medals.Threshold`.
- `Mulberry32`, `Rng.Substream`, `Rng.DailySeed` (FNV-1a of the UTC date).
- `SaveV3` and `SaveMigrator` (`Migrate`, `Serialize`). Migrates v1 to v3. Refuses a newer version.
- Content (`Desync.Content`): `Catalog.FromJson`, `Loadout.Build` (folds exploit effects into Modifiers), `CanHold`, `RollOffer`, `ValidateHeat`, `RollRoomMutators`.

## Scoring data flow

Planned path:

1. Gameplay raises events from `GameEvents`, such as `Flipped`, `Grazed`, `HackFinished`, `ExitReached` and `PlayerKilled`.
2. ScoreKeeper keeps the run event log, the GHOST chain and the multiplier, and writes `GameState.Score`, `Chain` and `Multiplier`.
3. `Scoring.ScoreFromEvents` replays the log. It is pure, so the server gets the same answer. The client score is never trusted.
4. RoomReport (`GameEvents.RoomReportReady`) feeds the results tally.
5. SaveService writes `SaveV3` through `SaveMigrator.Serialize` under PlayerPrefs key `desync.save` (WebGL: IndexedDB, per D-005). The raw text goes to `desync.save.backup` when a save is corrupt.

Current state:

| Step | Status |
| --- | --- |
| Events defined in `GameEvents.cs` | defined |
| ScoreKeeper | not written yet; `GameState` comments name it |
| `Scoring.ScoreFromEvents` | contract; body throws |
| RoomReport | type exists; no producer yet |
| SaveService | not written yet; `SaveMigrator` body throws |

## Content loading

Files: `DESYNC/Assets/Resources/Content/`.

- `rooms.json`: per room, `tDev` (dev best time, seconds) and `parScore` (grade par). Planned reader: RoomManager, which sets `GameState.RoomTDev`. `0` means unknown. See `docs/BALANCE.md` for the formulas.
- `hints.json`: contextual hints by key. The game shows one after three deaths in the same room.

Proposed mapping from `DeathCause` to hint key. Not in code yet:

| DeathCause | Key |
| --- | --- |
| Ice | `ice` |
| Melt | `melt` |
| Alarm, TraceTimer | `trace` |
| Drone | `drone` |
| Architect | `architect` |
| (none yet) | `glitch` |

`DeathCause` has no glitch case, so the `glitch` key cannot be reached until a glitch death exists. Decide that when the death recap lands.

## Level layout and validation

- `Levels/RoomLayouts.cs`: ASCII grids for the six rooms. The `Legend` class maps characters: `T` terminal, `H` hijack pad, `G` glitch zone, `V` vent, `X` exit, `S` spawn, digits for cameras, `a` to `h` for drone waypoints.
- `Editor/Levels/LevelBuilder.cs`: builds the scenes. For each room it logs a line starting `[DESYNC] LEVELS R<n> <NAME>:` that holds the validator output.
- `Levels/GridValidator.cs`: pure C# beatability check. Dijkstra over (cell, level, layer, melt). Two runs: `fastest` (cameras ignored) and `ghost` (cells a camera or drone could watch count as SOURCE-only). The `fastest` time is the source for `tDev`.
- `Levels/LevelRoomInfo.cs`: a subclass of `RoomInfo`. Unity serialises a MonoBehaviour by its file name, so LevelBuilder places this subclass. Everything that uses `RoomInfo` still works.

## Flow (from `CONTRACT.md`)

- REBOOT: triggered by `PlayerKilled`, `Alarm` or R. RoomManager raises `Rebooting`, waits the restart wipe (unscaled), resets state, and raises `RoomReset`.
- Terminals: `AccessGranted(line)` counts toward `RoomInfo.terminalCount`. When all are done, `ExitOpened` fires (in Room 6, `FinaleStarted` instead).
- Exit: the portal raises `ExitReached`. RoomManager grades the room and raises `RoomCleared`. The grade card shows for about 2.5 s, then the next room loads.
- Finale (Room 6): the Architect plays the reveal, then raises `GameCompleted`. The UI shows the compromised screen, then the Ending scene loads.
- Juice: anyone raises `ShakeRequest`, `HitStopRequest` or `PopText`. Player's `JuiceUtil` carries them out, honouring Settings.

## Known gaps

- No Net assembly exists yet. The leaderboard backend and analytics arrive in M4 (D-007).
- Rules and Content bodies still throw. The acceptance tests fail until they are implemented.
- ScoreKeeper, SaveService and the Gen generator are not written.
- `hints.json` keys have no reader yet, and the glitch key has no death cause.
- `tools/test.sh` (D-007) is not present.
- Several `GameConfig` values differ from the spec. See the drift table in `docs/BALANCE.md`.
