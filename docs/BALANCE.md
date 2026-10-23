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
