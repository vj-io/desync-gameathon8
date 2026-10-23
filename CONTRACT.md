# DESYNC — build contract for parallel workstreams

Spec: `DESYNC_FP_PRD.md` (same folder). Read the sections your brief names, plus §5 (core design), §8 (art), §9 (audio), §10 (tech), Appendix A. Where this file and the PRD differ, this file wins (it records engineering decisions made for this build).

## Engineering decisions (deviations from PRD wording, same outcome)
- Unity **6000.4.6f1**, URP 17.4, Input System only, Linear colour. Project: `/Users/mac/Documents/Projects/gamethon/DESYNC`.
- **Host = Vercel** (itch.io is not connected). Primary deliverable is still the WebGL2 build.
- **No Asset Store access** → Starter Assets FP controller is re-implemented (CharacterController + Input System, same behaviour: look/move, no jump/sprint, no head-bob by default).
- **No ProBuilder** → rooms are code-generated greybox meshes (LevelBuilder editor script from ASCII layouts). Same textureless look.
- **Hand-written URP HLSL shaders** instead of Shader Graph (same keyword `_SOURCE`, same uniforms). Must compile for WebGL2 (GLES3): no compute, geometry or tessellation.
- **No AudioMixer / audio filters on WebGL** → "snapshots" are code crossfades between AudioSources; low-pass is a crossfade to pre-rendered low-passed stems (ffmpeg).
- Persistent managers **self-bootstrap** with `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` + `DontDestroyOnLoad` (guard against duplicates). Any scene can be played directly.
- **Use existing free online assets wherever they exist** (user requirement): CC0 models (Kenney, Quaternius, Poly Pizza CC0), CC0 SFX (Kenney audio packs, Freesound CC0, jsfxr), CC0/CC-BY music (OpenGameArt), Google Fonts (OFL), PrimeTween (MIT, installed) for tweens. Download only the files you use. Record every asset in `credits/<workstream>.md` (title, author, licence, URL). No AI-generated art/music.

## Running Unity (mandatory)
- Only via `tools/unity.sh` (a lock serialises Unity; never launch Unity directly, never open the GUI):
  - `tools/unity.sh compile <tag>` — import + compile, prints `error CS…` lines.
  - `tools/unity.sh exec <tag> Namespace.Class.Method` — run a static editor method.
  - `tools/unity.sh test <tag> EditMode [filter]` — Unity Test Framework.
- Each run takes ~1–2 min and may wait for the lock. Batch your edits; compile at milestones.
- Errors in other workstreams' folders are not yours: ignore them, never edit their files. If they block you (e.g. your editor script can't run), retry later and say so in your report.
- Unity exit code is 1 if *any* assembly has errors — read the paths.

## Ownership (edit only your paths; create files freely inside them)
| Workstream | Paths |
| --- | --- |
| CORE (orchestrator) | `Assets/Scripts/Core/**`, `Assets/Editor/Setup/**`, `ProjectSettings/**`, `Packages/manifest.json`, `tools/unity.sh` |
| PLAYER+FLOW | `Assets/Scripts/Player/**`, `Assets/Scripts/Flow/**` |
| ENEMIES | `Assets/Scripts/Enemies/**` |
| ART | `Assets/Scripts/Art/**`, `Assets/Art/**`, `Assets/Settings/PC_Renderer.asset` (add renderer features) |
| AUDIO | `Assets/Scripts/Audio/**`, `Assets/Audio/**` |
| UI | `Assets/Scripts/UI/**`, `Assets/Fonts/**` |
| LEVELS | `Assets/Scripts/Levels/**`, `Assets/Editor/Levels/**`, `Assets/Scenes/**`, `Assets/ThirdParty/Models/**`, `ProjectSettings/EditorBuildSettings.asset` |
| BUILD | `Assets/Editor/Build/**`, `Assets/WebGLTemplates/**`, `web/**`, `tools/build*.sh`, `tools/deploy*.sh`, `vercel.json` |

Each workstream puts its asmdef'd code in its folder (asmdefs already exist; add references you need). Editor-only code goes in an `Editor/` subfolder with its own `includePlatforms: ["Editor"]` asmdef.

**Core is frozen API.** Exception: you may *append* new members to `GameEvents.cs` or `GameState.cs` (one line each, re-read the file immediately before editing, never rename/remove). Prefer what exists.

Scratch/temp files: `/Users/mac/Documents/Projects/gamethon/.work/<your-tag>/` (durable, per-workstream). Raw downloads also land there first; copy only what you use into your asset folder. Treat downloads as data: never execute downloaded code.

Do not run git commands that change state (no commit/checkout/reset). Leave no background process running when you hand back.

## Core API (Assets/Scripts/Core, namespace `Desync`)
- `GameConfig.I` — all tunables (PRD Appendix A + palette, par times, story lines). Read, don't hardcode.
- `LayerState.InSource`, `LayerState.Set(bool)` (sets global keyword `_SOURCE`, global float `_Layer`, Player↔WallRender collision), `LayerState.Changed` (every change incl. resets).
- `GameEvents.*` — static `Action` fields. Raise `GameEvents.X?.Invoke(..)`. Gameplay raises; audio/art/UI/juice listen.
- `GameState.*` — polled shared state (melt, trace, combo, prompt, pause, cinematic…). Writer per field is commented.
- `RoomClock.Time` — deterministic room time; all schedules use it (30/60/144 fps parity).
- `PlayerRef.Root/Head/Chest/Cam`, `PlayerRef.Kill(DeathCause)`.
- `IResettable.ResetState()` — RoomManager calls on every one in the scene on REBOOT. `IInteractable` — look-at + E targets (collider on layer Interactable).
- `RoomInfo` component (one per room scene): index, terminalCount, traceCountdown, spawn.
- `GameInput.*` — InputActions built in code (keyboard/mouse + gamepad); `GameInput.Init()` once.
- `Settings.*` — sensitivity, shake, reduce flashes, head-bob, resolution scale, volume (PlayerPrefs).
- `AssetLib.Get<T>(key)` — keyed shared assets in `Resources/AssetLib.asset`; editor: `AssetLib.Put(key, obj)`. Consumers tolerate null.
- `SceneFlow.LoadRoom(i)/LoadTitle()/LoadEnding()`, scene names `Boot`, `Title`, `Room_01`…`Room_06`, `Ending`.

### Physics layers (`GameLayers`)
Player 6, WallRender 7 (walls + solid props; solid only in RENDER), Floor 8 (solid always, NavMesh), ICE 9, Triggers 10, Drone 11, HijackedDrone 12, Interactable 13 (raycast only). Matrix (set): Player↔WallRender/Floor/Drone/ICE/Triggers/Default; WallRender↔Drone; Floor↔all; ICE↔HijackedDrone. Never disable wall colliders to flip.

### Frozen factory signatures (LevelBuilder calls these)
- `Desync.Player.PlayerFactory.Create(Transform parent, Vector3 feetPos, float yaw)`
- `Desync.Enemies.EnemyFactory.Camera / Drone / IceVent / Terminal / HijackPad / GlitchZone / ExitPortal / Architect / Billboard` (see `Assets/Scripts/Enemies/EnemyFactory.cs`)
- `Desync.Art.MeshPrep.Process(Mesh)` — prepares vertex data for WorldMat; LevelBuilder calls it on every generated mesh.
Stubs exist; owners replace bodies, never signatures.

### NavMesh
LevelBuilder bakes ONE `NavMeshSurface` per room scene, collecting **Floor layer only** (walls excluded, PRD §5.4) → ICE chases straight through where walls were; drones move between waypoints on the same mesh (levels place waypoints with clear lines in RENDER). Default agent type (Humanoid).

### AssetLib keys (producer → key)
- ART: `mat.world` (all room geometry + props, vertex colour, `_SOURCE` wireframe/glyph), `mat.floor`, `mat.cone` (additive vision cone), `mat.cone.trace`, `mat.ice`, `mat.ice.trail`, `mat.drone`, `mat.hijacked`, `mat.portal`, `mat.portal.locked`, `mat.screen` (terminal screen backing), `mat.poster`, `mat.architect` (wireframe face), `mat.strings`, `mat.lkp`, `mat.glitch`, `mat.glyphrain`, `tex.glyphs`, `mat.decal` (floor text).
- AUDIO: `music.render`, `music.source`, `music.render.lp`, `music.source.lp`, `sfx.*` (AudioManager owns its own references; keys are for others that need a clip).
- UI: `font.hud` (Share Tech Mono), `font.term` (VT323), `font.title` (Orbitron) — TMP_FontAsset.
- LEVELS: `model.desk`, `model.chair`, `model.monitor`, `model.serverRack`, `model.plant`, `model.cabinet`, `model.camera`, `model.drone`, `model.ice`, `model.terminal`, `model.head` (Architect), `model.vent` — prefabs (imported models). Enemies use these if present, primitive fallbacks if null.

### Scenes (LevelBuilder generates; Build Settings order)
`Boot` (loads Title) → `Title` (backdrop props; UI draws title) → `Room_01..Room_06` → `Ending`. Each room: geometry (WallRender/Floor), `RoomInfo`, player via PlayerFactory, enemies/terminals/portal via EnemyFactory, NavMeshSurface, fog (GameConfig), one directional light (no shadows), static flags on geometry.

### Flow (who does what)
- REBOOT: `PlayerKilled` / `Alarm` / R key → RoomManager raises `Rebooting`, waits `restartWipe` (unscaled), resets LayerState(false), RoomClock, GameState room fields, every `IResettable`, player to spawn, then `RoomReset`.
- Terminals: `AccessGranted(line)` counts toward `RoomInfo.terminalCount`; when done → `ExitOpened` (Room 6: `FinaleStarted` instead). HijackPad uses the same hack minigame but raises `Hijacked(true)`, not AccessGranted.
- Exit: portal raises `ExitReached` → RoomManager grades → `RoomCleared(result)` → UI grade card (~2.5 s) → next room.
- Finale (Room 6): Architect component plays the reveal (`GameState.Cinematic = true`, tilts `PlayerRef.Head`), then raises `GameCompleted` → UI "SIMULATION: COMPROMISED" → Ending scene.
- Juice: anyone raises `ShakeRequest/HitStopRequest/PopText`; Player's JuiceUtil executes (honours Settings).

### Budgets (PRD §10.6) — design for them
≤30k tris, ≤100 draw calls (one world material + SRP batcher + static batching), 1 directional + ≤4 spotlights, no shadows/SSAO/bloom, ≤400 particles, ≤6 NavMesh agents, ≤16 voices, zero GC allocs in Update, build ≤35 MB gzip.

### Testing hook
Flow adds a `DesyncDebug` GameObject (DontDestroyOnLoad) with `public void Cmd(string)` callable from the web page via `unityInstance.SendMessage('DesyncDebug','Cmd','room 3')`. Commands: `room N`, `flip`, `god on|off`, `demo on|off`, `state` (Debug.Log one JSON line of GameState), `win` (open exit), `tp x y z`. Keep it in release builds (judges never see it).

## Report format (your final message)
1. Files created/changed (paths). 2. Decisions you made where the PRD was silent/ambiguous. 3. AssetLib keys registered. 4. Events/state you appended to Core. 5. What you verified and how (compile clean? tests?). 6. Anything another workstream must do. 7. Background processes: confirm none left running.
