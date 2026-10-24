# DESYNC balance

Status (2026-10-24): the numbers below are the ones in `GameConfig.asset` and the Rules contract. Scoring and medal bodies are not implemented, so the scoring and medal figures are spec values that the code does not use yet. Dev times (`tDev`) are not measured.

Working rule from `BREACH_SPEC.md`: change at most three levers per pass, and record every pass in the log at the end.

## Current levers (in `Resources/GameConfig.asset`)

Melt:

| Lever | Value | Field |
| --- | --- | --- |
| Rise in SOURCE | 12 %/s | `meltRiseSource` |
| Drain in RENDER | 4 %/s | `meltDrainRender` |
| Per flip | 5 % | `meltFlipSpike` |
| Warning | 70 % | `meltWarn` |
| Hold at 100 % before death | 0.6 s | `meltDeathDrip` |

Trace and surveillance:

| Lever | Value | Field |
| --- | --- | --- |
| Dwell before TRACE starts | 0.5 s | `traceDwell` |
| TRACE duration | 6 s | `traceDuration` |
| TRACEs before alarm | 3 | `tracesToAlarm` |
| Camera cone | 60°, 8 m | `cameraConeAngle`, `cameraConeRange` |
| Camera sweep | ±45°, 3 s one way | `cameraSweepAngle`, `cameraSweepPeriod` |
| Drone speed | 2.5 m/s | `droneSpeed` |
| Drone cone | 45°, 6 m | `droneConeAngle`, `droneConeRange` |
| UNSTABLE countdown | 90 s | `traceCountdown` in `RoomLayouts.cs` |

ICE:

| Lever | Value | Field |
| --- | --- | --- |
| Hunter speed | 4.5 m/s | `iceSpeed` |
| Spawn interval, counted in SOURCE time | 4 s (2 s during TRACE) | `iceSpawnInterval`, `iceSpawnIntervalTraced` |
| Max alive | 4 | `iceMaxAlive` |
| Wind-up | 0.3 s | `iceWindup` |
| Despawn after returning to RENDER | 1 s | `iceDespawn` |

Terminals, hijack and glitch:

| Lever | Value | Field |
| --- | --- | --- |
| Interact range | 2.5 m | `terminalRange` |
| Wrong-key lockout | 0.4 s | `terminalWrongLockout` |
| Sequence length, rooms 1 to 6 | 3, 3, 4, 4, 5, 5 | `promptLengthByRoom` |
| Hijack duration | 10 s | `hijackDuration` |
| Glitch zone countdown | 3 s | `glitchZoneTime` |
| Restart wipe | 0.2 s | `restartWipe` |

Par shown in the HUD, grade card and level select. These are read today. They give way to `parScore` once scoring lands.

| Room | parTime (s) | parFlips |
| --- | --- | --- |
| BOOT | 25 | 2 |
| EYES | 50 | 4 |
| ICE | 70 | 6 |
| HIJACK | 90 | 8 |
| UNSTABLE | 100 | 10 |
| ROOT | 120 | 12 |

## Scoring (Rules contract; not implemented yet)

| Event | Points |
| --- | --- |
| Clean flip | 50 |
| Graze (cap 3 per ICE per room) | 150 |
| Redline (exit SOURCE with melt at 90 % or more) | 250 |
| Hack, perfect / normal | 300 / 120 |
| No-trace room | 500 |
| Room clear | 1000 |
| Puppet string | 400 |

- Multiplier: `1 + 0.25 × chain`, capped at 4.0 (reached at chain 12).
- Chain window: 3.0 s.
- Room score: each event's points × the multiplier at that moment, plus the time bonus.
- Time bonus: `max(0, gold − t) × 40`.
- Grade by `score / parScore`: S at 1.0 or more, A at 0.75, B at 0.5, otherwise C.
- Spec, not yet in code: melt carries over between rooms at 50 %.

## Medals (Rules contract; not implemented yet)

`tDev` is the dev best time for the room.

| Medal | Time |
| --- | --- |
| SYNC | at most 1.00 × tDev |
| GOLD | at most 1.25 × tDev |
| SILVER | at most 1.6 × tDev |
| BRONZE | any clear |

`GameState.RoomTDev` is 0 when unknown. With `tDev` at 0, every clear grades as BRONZE.

## Par and dev times (`Resources/Content/rooms.json`)

Par formula, documented per D-009:

`parScore = room clear 1000 + no-trace 500 + terminals × 300 hack × 1.5 average multiplier + 4 clean flips × 50 × 1.5`, rounded to 50.

That gives `parScore = 1800 + 450 × terminals`.

Dev time formula: `tDev = fastest × 1.3`, rounded to 0.5 s. The `fastest` figure is the `fastest` value from the validator line for the room.

| Room | Terminals | parScore | tDev |
| --- | --- | --- | --- |
| BOOT | 1 | 2250 | 0 (unmeasured) |
| EYES | 1 | 2250 | 0 (unmeasured) |
| ICE | 2 | 2700 | 0 (unmeasured) |
| HIJACK | 2 | 2700 | 0 (unmeasured) |
| UNSTABLE | 2 | 2700 | 0 (unmeasured) |
| ROOT | 3 | 3150 | 0 (unmeasured) |

Open points on par:

- Hijack pads are not counted. One pad in HIJACK would add 450, making its par 3150.
- ROOT has no exit; the finale stands in for it. The formula assumes the finale pays the room clear.

Why `tDev` is 0: the validator lines (`[DESYNC] LEVELS R<n>`) that the dev-time formula needs are not in this worktree. `logs/` holds only the M0 test and build logs from 2026-10-23, and none of them come from a LevelBuilder run. To fill the table, run LevelBuilder from the LEVELS workstream, read each room's `fastest` value from the log, compute `tDev = round(fastest × 1.3, 0.5 s)`, and write it into `rooms.json`.

## Spec drift to decide (open, nothing changed)

Values in the code against `BREACH_SPEC.md`:

| Item | Code | Spec |
| --- | --- | --- |
| Camera cone range | 8 m | 12 m (Fixed Camera) |
| Camera full sweep cycle | 6 s (3 s each way) | 8 s period |
| Drone cone range | 6 m | 10 m (Guard Drone) |
| Drone speed | 2.5 m/s | 3 m/s (Guard Drone patrol) |
| Glitch zone forced flip | 3 s | 1.0 s |
| Room par | 25 to 120 s (six rooms) | 45 to 75 s (full game) |

Matches the spec: melt rates, flip spike, warning, death at 100 %, trace dwell at 0.5 s, camera cone angle, ICE speed.

## Spec targets (F10, not measured yet)

Bot simulation, heat 0, cautious play: clear rates Act 1 85 to 97 %, Act 2 60 to 85 %, Act 3 40 to 70 %. Heat 10, Act 3: 15 to 40 %. Median room 40 to 80 s. Winning run 18 to 22 min. Melt peak p90 at most 96 %. No single death cause above 55 %. No exploit lifts the floor-matched win rate by more than 15 points.

These apply to the full game. The six-room build will not meet them.

## Pass log

| Date | Pass | Levers changed (max 3) | Before to after | Reason | Result |
| --- | --- | --- | --- | --- | --- |

## Unlock economy (M4: unlock tree, Deep Layer 11-30)

Fragments buy the unlock tree (`Resources/Content/unlocks.json`, `Flow/Unlocks.cs`, screen `UI/UnlockTree.cs`). The balance check is `UnlocksTests.Economy_*` (EditMode): it recomputes the numbers below from `RunRules` and the spec's clear rates and fails when the tree drifts away from the cadence.

Income per cleared room (`RunRules`): clear 10, +10 for Gold or better, +15 for grade S. A death banks 40 % of what the run earned (rounded up) plus 3 (`RunRules.Bank`); a win or a cash-out banks all of it. Deep Layer adds +10 % per level up to DL 20; DL 21-30 add nothing, so the estimate leaves the bonus out (it only makes the tree cheaper).

Estimate inputs (all in the test, one place):

| Input | Value | Why |
| --- | --- | --- |
| Rooms per act, boss included | 4.2 | 4 map nodes x 0.80 (Infiltration .60, Elite .15, Anomaly .05; Market and Cooldown pay nothing) + the boss |
| Fragments per cleared room | 12 | 10 + 10 x ~10 % Gold-or-better rooms + 15 x ~7 % grade S |
| Minutes per room | 1.5 | a 12-room campaign in about 19 min (spec: 18-22), menus and drafts included |
| Run time share | 90 % | menus, results, retries inside a death |
| A run that dies | mid-act, banks `Bank(earned, died)` | the run goes as deep as the open acts allow, then cashes out |

Act clear rates by hour of play (spec F10: Act 1 85-97 %, Act 2 60-85 %, Act 3 40-70 %; early hours play below the middle of those ranges while the player learns, and an act only counts once its boss opened it):

| Hours | Acts open | Clear rate per act | Fragments / hour |
| --- | --- | --- | --- |
| 0-1 | 1 | .80 | 409 |
| 1-2 | 1-2 | .85, .60 | 347 |
| 2-4 | 1-3 | .88, .65, .45 | 280 |
| 4-8 | 1-3 | .91, .72, .55 | 306 |

Banked by hour 1 / 2 / 4 / 6 / 7 / 8: 409 / 755 / 1,315 / 1,928 / 2,234 / 2,540 Fragments.

The tree (cost in Fragments; `by` = hour a median player can afford everything due up to that node):

| Node | Cost | Needs | By (h) | Spec cadence |
| --- | --- | --- | --- | --- |
| STARTER SET (first 8 exploits) | free | | 0.5 | 0-0.5 h: first 8 exploits |
| DAILY BREACH | free | | 0.5 | Daily visible from the start |
| BLACK MARKET | 80 | | 1 | 0.5-1 h: Black Market |
| GHOST START | 100 | | 1 | 0.5-1 h: GHOST and PHANTOM starters |
| PHANTOM START | 120 | GHOST START | 1 | |
| EXPLOITS II (+8) | 90 | STARTER SET | 2 | 1-2 h: +16 exploits |
| EXPLOITS III (+8) | 110 | EXPLOITS II | 2 | |
| DUOS (10 duo exploits) | 80 | EXPLOITS III | 2 | 1-2 h: duos possible |
| TIME-ATTACK (`unlock_timeattack`) | 70 | | 2 | 1-2 h: Time-Attack |
| REDLINE / OVERCLOCK / TRACER START | 130 / 150 / 170 | the one before | 4 | "then others" |
| EXPLOITS IV (+8) | 340 | EXPLOITS III | 6 | 4-8 h: remaining exploits |
| EXPLOITS V (+8) | 420 | EXPLOITS IV | 7 | |
| EXPLOITS VI (+8) | 480 | EXPLOITS V | 8 | |

Costs due by hour 1 / 2 / 4 / 6 / 7 / 8: 300 / 650 / 1,100 / 1,440 / 1,860 / 2,340, against 409 / 755 / 1,315 / 1,928 / 2,234 / 2,540 banked (73 / 86 / 84 / 75 / 83 / 92 %). The whole tree, 2,340 Fragments, is affordable at about 7.4 h. The tests require every tier to be affordable by its hour and the whole tree to take between 6.5 and 8 h.

Gating rules (anti-grind): every node is reachable at DL 0 (no boss, no Deep Layer, no cosmetic as a requirement); nothing is time-gated or login-gated; a death still banks at least 40 % of a win. Only runs with the player's own seed are gated: Campaign, Quick and Deep Layer runs. Daily (and any shared-seed mode added later) always drafts from all 58 exploits, sells at the Black Market and starts empty-handed, so a leaderboard compares like with like. Deep Layer runs always keep the Black Market because Poor Market is one of their heat mutators. Until BLACK MARKET is bought, Market nodes on a Campaign or Quick map play as Infiltration rooms (same id, links and seed).

Deep Layer 11-30 (`DeepRules`, `DeepRewards`): the preset ladder adds one heat per level through DL 30 (30 of the 40 heat; Boss Overclock is not a step because DL 6 and up already run the bosses hot). DL 11-23 raise melt, hack, TRACE and camera pressure with the single-heat mutators; from DL 24 a heavier mutator replaces lighter ones each level (Double Patrol, Tracker Swarm, Scrambled Prompts, Fewer Choices, No Backups, Gaze Everywhere). Fragments +10 % per level stops at DL 20 (+200 %). Rewards are cosmetic only: one per level from DL 11 to DL 20 (six titles, RIPPLE, SHARDS, AFTERGLOW and STREAK flip effects) and the SYNC GOLD flip trail at DL 30; DL 21-29 pay nothing and are played for DL 30.
