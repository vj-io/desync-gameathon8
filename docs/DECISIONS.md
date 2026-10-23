# Decisions (append-only)

**D-001 Stack.** `DESYNC/ProjectSettings/ProjectVersion.txt` (6000.4.6f1) and `DESYNC/Assets/` exist → STACK=UNITY. Unity 6.4 URP, WebGL2 build hosted on Vercel. Tests: Unity Test Framework (NUnit EditMode + PlayMode) through `tools/unity.sh test`; browser smoke tests with Playwright against the WebGL build.

**D-002 Pure rules core.** The rules engine lives in `Assets/Scripts/Rules` (asmdef `Desync.Rules`, `noEngineReferences: true`, Newtonsoft.Json only). Generation will live in `Assets/Scripts/Gen` (asmdef `Desync.Gen`, also engine-free). Game code reads `GameState.Mods` (a `Rules.Modifiers`) for every tunable it covers instead of `GameConfig`.

**D-003 Source of numbers.** Core rules keep the shipped values (melt +12/s SOURCE, +5 per flip, −4/s RENDER, TRACE after 0.5 s, death at 100). Scoring: clean flip 50, graze 150 (cap 3 per ICE per room), redline 250 (exit SOURCE at melt ≥ 90), hack perfect 300 / hack 120, no-trace room 500, room clear 1000, puppet string 400; multiplier 1 + 0.25·chain capped at 4.0; chain window 3.0 s; grade by score/par (S ≥ 1.0, A ≥ 0.75, B ≥ 0.5). Medals by dev time T_dev: SYNC ≤ 1.00, GOLD ≤ 1.25, SILVER ≤ 1.6 × T_dev, BRONZE = clear.

**D-004 Randomness.** Mulberry32 with named substreams (`Rng.Substream(seed, name)`); daily seed = FNV-1a of the UTC date `yyyy-MM-dd`. No `System.Random` or `UnityEngine.Random` in Rules/Gen (enforced by test RNG-04).

**D-005 Save.** `SaveV3` JSON in PlayerPrefs (IndexedDB on WebGL) under key `desync.save`, migrated by `Rules.SaveMigrator` (v1 → v3); corrupt saves fall back to defaults and the raw text is kept under `desync.save.backup`.

**D-006 Branching.** Feature work happens in the worktree `../gamethon-breach` on branch `breach/integration`, merged to `main` per milestone after a full test run and a WebGL preview deploy.

**D-007 Out of scope for now.** Leaderboard backend and analytics provider need accounts and secrets; the client is built provider-agnostic and the backend lands in M4. Hosted CI for Unity needs a licence secret; until then `tools/test.sh` runs the same gates locally.

**D-008 Repository content.** No credits or disclosure screens inside the game build; licence attributions stay in `CREDITS.md`.
