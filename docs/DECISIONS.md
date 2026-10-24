# Decisions (append-only)

**D-001 Stack.** `DESYNC/ProjectSettings/ProjectVersion.txt` (6000.4.6f1) and `DESYNC/Assets/` exist → STACK=UNITY. Unity 6.4 URP, WebGL2 build hosted on Vercel. Tests: Unity Test Framework (NUnit EditMode + PlayMode) through `tools/unity.sh test`; browser smoke tests with Playwright against the WebGL build.

**D-002 Pure rules core.** The rules engine lives in `Assets/Scripts/Rules` (asmdef `Desync.Rules`, `noEngineReferences: true`, Newtonsoft.Json only). Generation will live in `Assets/Scripts/Gen` (asmdef `Desync.Gen`, also engine-free). Game code reads `GameState.Mods` (a `Rules.Modifiers`) for every tunable it covers instead of `GameConfig`.

**D-003 Source of numbers.** Core rules keep the shipped values (melt +12/s SOURCE, +5 per flip, −4/s RENDER, TRACE after 0.5 s, death at 100). Scoring: clean flip 50, graze 150 (cap 3 per ICE per room), redline 250 (exit SOURCE at melt ≥ 90), hack perfect 300 / hack 120, no-trace room 500, room clear 1000, puppet string 400; multiplier 1 + 0.25·chain capped at 4.0; chain window 3.0 s; grade by score/par (S ≥ 1.0, A ≥ 0.75, B ≥ 0.5). Medals by dev time T_dev: SYNC ≤ 1.00, GOLD ≤ 1.25, SILVER ≤ 1.6 × T_dev, BRONZE = clear.

**D-004 Randomness.** Mulberry32 with named substreams (`Rng.Substream(seed, name)`); daily seed = FNV-1a of the UTC date `yyyy-MM-dd`. No `System.Random` or `UnityEngine.Random` in Rules/Gen (enforced by test RNG-04).

**D-005 Save.** `SaveV3` JSON in PlayerPrefs (IndexedDB on WebGL) under key `desync.save`, migrated by `Rules.SaveMigrator` (v1 → v3); corrupt saves fall back to defaults and the raw text is kept under `desync.save.backup`.

**D-006 Branching.** Feature work happens in the worktree `../gamethon-breach` on branch `breach/integration`, merged to `main` per milestone after a full test run and a WebGL preview deploy.

**D-007 Out of scope for now.** Leaderboard backend and analytics provider need accounts and secrets; the client is built provider-agnostic and the backend lands in M4. Hosted CI for Unity needs a licence secret; until then `tools/test.sh` runs the same gates locally.

**D-008 Repository content.** No credits or disclosure screens inside the game build; licence attributions stay in `CREDITS.md`.

**D-009 Room par and dev times.** `parScore` per room = 1000 (clear) + 500 (no trace) + 300 × 1.5 per terminal + 4 × 50 × 1.5 (clean flips), rounded to 50, so 1800 + 450 × terminals: BOOT and EYES 2250, ICE, HIJACK and UNSTABLE 2700, ROOT 3150. Hijack pads are not counted. `tDev` stays 0 (unknown, per `GameState.RoomTDev`) until a LevelBuilder run logs the validator's `fastest` time; then `tDev` = fastest × 1.3 rounded to 0.5 s. Values live in `DESYNC/Assets/Resources/Content/rooms.json`.

**D-010 Dev times filled.** tDev from the shipped build's validator `fastest` times (R1 7.5, R2 17.2, R3 17.5, R4 29.9, R5 26.8, R6 20.5 s) × 1.3 rounded to 0.5 s: 10.0, 22.5, 23.0, 39.0, 35.0, 26.5.

**D-011 Enemy stats by room kind.** The six classic rooms keep their tuned stats (camera 60° × 8 m sweeping ±45° over 3 s, drone 2.5 m/s × 6 m, glitch force-flip after 3 s). Generated BREACH rooms use the spec stats (fixed camera 60° × 12 m; sweep camera 90° arc, 8 s period, 10 m; drone 3 m/s × 10 m; glitch force-flip after 1.0 s with a 0.5 s telegraph) through factory overloads.

**D-012 Deep Layer heat totals 40.** The spec's per-rank heat values sum to 46 while the spec and MUT-01 require 40; trimmed: Hair Trigger 2/4 → 1/2; Expensive Flip, Unstable Reality, Persistent Melt and Auditor Protocol 2 → 1. `ranks[r-1]` is the total heat at rank r. Spec "x → y" changes are stored as additive deltas so effects stack before clamping.

**D-013 Exploit flags.** `Loadout.Build` returns `flags` for behaviour the game implements. The run manager writes them (plus run-mutator flags) into `GameState.Flags` at room start; Player/Flow/Enemies check them once per room (cached), never per frame by string.

**D-014 Deep Layer model.** The player picks mutators and ranks; the level equals the picks' total heat. Each level has a default preset (ladder: tight_deadline, hardened_ice, poor_market, long_sequences, unstable_reality, persistent_melt, wider_lens, hot_silicon, hardened_ice 2, tight_deadline 2). DL 1 opens with the first ARCHITECT kill; the highest level available is one above the best level won (cap 10 in M3). Rooms get `mut:<id>` per pick plus their flags; from DL 6 also `flag:boss_overclock`. Fragments +10 % per level (cap DL 20) before the 40 % death rule. Boss kill flags (`kill_auditor`, `kill_compiler`, `kill_architect`) live in `SaveV3.unlocks`; acts 2 and 3 open through the previous boss kill. Heat threats (Double Patrol, Tracker Swarm, Unstable Reality, Gaze Everywhere) draw from their own stream and are not budgeted.

**D-015 Human guards.** HALCYON security officers are RENDER-only people: patrol, chase (slower than the player), telegraphed strike; walls stop them; flipping to SOURCE escapes them and freezes them. Death cause `Guard`.

**D-016 Story mode.** A first-time prompt on the title offers a 60–90 s in-engine story (text, motion, the game's own scenes and sound; no voice-over), then Room 1 with story-guided prompts. Replayable from the title menu.

**D-017 Graze cap bonus.** `Modifiers.GrazeCapBonus` (appended, default 0) raises the per-ICE graze cap to `GrazeCapPerIce + GrazeCapBonus`. Only `redline_surge` sets it: +1 per REDLINE exit in the cleared room, for the next room played only.

**D-018 Daily, Weekly Ladder, leaderboard.** Daily = act 1 on `Rng.DailySeed` with a two-mutator pair (different tags, never GAZE) on every non-boss room; the first daily run of a UTC day is the scored attempt (spent at start), later runs are practice. Weekly Ladder = full campaign on the ISO-week seed, opens on `kill_auditor`, unlimited attempts, best kept. The server recomputes every submitted score from the run's event log (TypeScript port of the scoring rules, kept in lockstep with C# by a shared fixture); mismatches are rejected, one daily entry per player. Room time and modifiers are client-reported and only range-checked. Assist runs are never posted. Without a Redis store the board answers 503 and the game shows the local best.

**D-019 Unlock tree and Deep Layer 11-30.** Fragments buy unlock nodes (`Resources/Content/unlocks.json`): exploit packs on the spec's cadence (first 8 free), archetype starters, Black Market access, Time-Attack. Gating applies to Campaign, Quick and Deep Layer only; shared-seed modes see everything. DL 11-20 pay one cosmetic each (titles, flip effects), DL 30 the SYNC GOLD flip trail; cosmetics use palette hues only and switch off under Reduce FX / Reduce Flashing.

**D-020 Time-Attack.** 42 rooms: classic `r1`-`r6` plus 36 fixed-seed generated rooms `ta_a<act>_<nn>`; generated T_dev = validator best-path time x 1.3 (D-009). Best medal per room, best time, and a ghost of the fastest clear for the 10 most recently improved rooms (save stays under 64 KB).

**D-021 Story shards, endings, Endless Overclock.** 30 shards (`shards.json`, 10 per act) saved as `shard_a<act>_<nn>` in unlocks, each act read in order. Anomaly rooms always hold one, act 1 rooms always until the player has 3, otherwise 40 % per room on its seed, placed out of every sight cone; a boss kill pays the next one; a pickup scores PUPPET STRING. Endings play over the arena before the boss results: Exposed (sever), Merged (refuse), Desync (true; offered from the 4th ARCHITECT fight once 3 kills and 30 shards are earned, by touching a glyph behind the face in the final window). Endless Overclock (unlocked by `ending_desync`): no map/draft/market, act cycles every 3 rooms with heat +2, multiplier +0.1 per room, melt carries in full, death ends the run; best is rooms then score.
