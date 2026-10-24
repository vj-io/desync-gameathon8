# Launch checklist

For a WebGL release to Vercel (`tools/build.sh WebRelease`, `tools/deploy.sh [--prod]`). Work top to bottom; each item says how to measure it and what the last measurement was. "Last measured" numbers come from the `Builds/WebRelease` built at `6383df0` (M3 + hitch fixes), on an Intel UHD 630 laptop that was busy running other jobs, so timings are on the pessimistic side. Re-measure on the release build.

## 0. Gate

| # | Item | Budget | Last measured | Status |
| --- | --- | --- | --- | --- |
| 1 | Download size | <= 25 MB (spec), <= 35 MB gzip (CONTRACT) | 27.75 MB | over the spec budget, see 2 |
| 2 | Time to ready (E2E-01) | <= 8 s | 5.0-6.8 s typical, one 8.1 s run under load | ok, watch it |
| 3 | Restart after death (E2E-03) | < 300 ms | 216, 218 ms | ok |
| 4 | Frame time p95 (E2E-05) | < 20 ms at 60 fps | 16.7 ms (vsync-bound, 60 fps) | ok |
| 5 | Draw calls per frame (E2E-05) | <= 150 | 23 in room 1 | ok for the room measured, see 1 |
| 6 | Act 1 clears on a fixed seed (E2E-04) | passes | not yet run: needs a build with the autoplay hook | to do on the release build |
| 7 | Browsers | Chrome, Edge, Firefox, Safari 17+ | Chromium, Firefox, WebKit pass E2E-01 / 02 | Edge and real Safari by hand |
| 8 | Saves survive the update | v1 / v3 saves load, newer is refused | covered by tests | repeat by hand, see 4 |
| 9 | Analytics on and DNT respected | events visible, none under DNT | tests green | switch on in Vercel, see 5 |
| 10 | Rollback rehearsed | `vercel rollback` works | not rehearsed | rehearse once, see 8 |

## 1. Performance budgets

Spec non-negotiable 9, CONTRACT section 10.6. `tools/e2e.sh` runs the browser budgets; the rest needs a Development build (`tools/build.sh WebDev`) and the Unity Profiler.

| Budget | How to measure |
| --- | --- |
| 60 fps on an integrated GPU at 1080p | `tools/e2e.sh -g E2E-05` (real GPU, full Chromium; it skips itself on software WebGL). It counts animation frames for 8 s after a warm-up in Room 1 and fails above a 20 ms p95. For 1080p set the viewport (`use.viewport` in `tests/e2e/playwright.config.js`) to 1920x1080 and use fullscreen in the page. Test the weakest machine you can get; the budget is about that machine, not this one |
| <= 150 draw calls per room | E2E-05 counts every WebGL draw call per frame from inside the page (p95 must be <= 150). It only looks at the room it plays: run a heavy one too (Room 6, an Act 3 BREACH room) with `?autoplay=1` plus a console snippet, or read the Frame Debugger on a WebDev build |
| Post-FX <= 2.5 ms | WebDev build, Profiler or Frame Debugger: the URP post-process and the DESYNC full-screen passes (`PostFXController`). A cross-check: compare the E2E-05 frame p95 with Reduce FX on and off |
| GC spikes < 5 ms, zero allocations per frame | Profiler "GC.Alloc" while playing a room, a death and a restart. The PlayMode suites check the HUD, results and story screens for allocations; the page bench shows any long frame as a high max (last run: max 16.8 ms in 8 s) |
| Ready in <= 8 s | E2E-01 (page load to `__DESYNC__.ready`). Repeat three times; one slow run on a loaded machine is not a regression, three are |
| Restart < 300 ms | E2E-03 reads the game's own `metrics.lastRestartMs`; it skips on software WebGL because the restart there is bound by a 400 ms frame |
| Room generation <= 15 ms median, <= 50 ms p99 | `GEN_06_P99Under50ms` in `Assets/Tests/EditMode/GenTests.cs` |
| 16 voices, <= 400 particles, <= 6 NavMesh agents | the audio PlayMode tests (voice cap), the Profiler for the rest |

Set the machine's power mode to "high performance" and close other jobs before a measurement; note the GPU string the test prints (`renderer: ...`).

## 2. Download size

Everything the player downloads on first load is `index.html`, `TemplateData/`, and four files in `Build/`, all Brotli-compressed by Unity (decompression fallback kept on) and served with `Content-Encoding: br` (`web/site/vercel.json`; `tools/deploy.sh` fails when a `.unityweb` file comes back without it), so the bytes on disk are the bytes on the wire:

```
ls -l Builds/WebRelease/Build | awk '{ s += $5; print $5, $9 } END { printf "%.2f MB\n", s / 1e6 }'
```

Last measured (gzip build): data 14.41 MB, wasm 13.20 MB, framework 0.09 MB, loader 0.05 MB = **27.75 MB**, 2.75 MB over the 25 MB spec budget (inside the older 35 MB CONTRACT figure). Decide before launch which one is the bar. Levers, biggest first: Brotli instead of gzip is now the build setting (typically 15-25 % smaller; re-measure the next WebRelease); still open: keep the Act 2 and Act 3 music out of the first download (they are loaded on demand already, but still sit in the data file); raise managed code stripping and trim the wasm (the Newtonsoft JSON dependency is `link.xml`-preserved).

## 3. Browsers

WebGL2 is required (the page says so when it is missing). Matrix from the README: Chrome (primary), Edge, Firefox, Safari 17+.

- Automated: `E2E_BROWSER=firefox tools/e2e.sh -g "E2E-0[12]"` and `E2E_BROWSER=webkit ...` (engines only; the timing budgets mean something on Chromium with a GPU). Last run: Firefox ready in 5.0 s, WebKit in 6.0 s, both start a room and request pointer lock.
- By hand, per browser: the title loads and the story prompt answers to clicks and keys; a click starts a room and locks the pointer; Esc pauses and a click resumes; audio starts after the first click (autoplay policy); the share text copies (`C` on results); fullscreen works; reload keeps the profile (settings, best times).
- Safari: pointer lock and the clipboard are the usual trouble; check both. Firefox: check the gamepad.

## 4. Save migration

The profile is `SaveV3` JSON in PlayerPrefs (IndexedDB on WebGL), `desync.save`. This release adds keys to `settings` only (`a11y_*`, `assist_on`, `ta_*` and the others each workstream owns), so there is no version bump: a build that does not know a key ignores it, which also makes a rollback safe.

- Covered by tests: `SAVE_01..05` in `Assets/Tests/EditMode/RulesTests.cs` (v1 to v3 keeps medals and fragments, corrupt saves give defaults, a newer version is refused and never downgraded, idempotent on v3, a full profile stays under 64 KB) and `Desync.Net.Tests.SaveServiceTests` (the service around them: the raw text of a corrupt save is kept under `desync.save.backup`, a newer save is left alone, writes are throttled).
- By hand, once, on the release URL: (1) with the previous production build, play a room and change a setting; (2) deploy the new build to a preview and open it in the same browser profile (same origin needed: use the production domain for the check, or copy the IndexedDB entry); (3) the console line `[DESYNC] seed=... build=... save=3` appears, bests, medals and settings are intact, and `desync.save.backup` was not created; (4) in a fresh profile the first-time story prompt appears once.
- Remapped keys, volumes and Assist live in the same save and follow it; "Reset defaults" in SOUND + ACCESS and KEY BINDINGS clears them.

## 5. Analytics

Details and the privacy note: `docs/ANALYTICS.md`.

- [ ] Web Analytics is enabled on the Vercel project (and the plan includes custom events).
- [ ] After a preview deploy: play a room, die once, change a setting; the events (`game_loaded`, `room_start`, `death`, `room_end`, `settings_changed`) show up in the dashboard.
- [ ] `E2E_STRICT=1 BASE_URL=<preview> tools/e2e.sh -g "E2E-A1"` passes (`game_loaded` reaches the page, flat props, hashed `pid`).
- [ ] With the browser's Do Not Track on, nothing is recorded and no analytics script is requested.
- [ ] Read the privacy note once more if any event or property was added since.

## 6. Accessibility pass

All in the pause menu; all saved with the profile.

- [ ] KEY BINDINGS: remap Flip, restart, pause and move; a key already in use swaps; the HUD control card and the pause list show the new keys; RESET DEFAULTS restores them; a reload keeps them. Flip stays one key.
- [ ] SOUND + ACCESS: HUD size 80-140 % keeps every corner block on screen; subtitle size; high-contrast HUD; TAB as a toggle for the run map; Assist mode (melt rises x0.75, TRACE window +0.2 s, ICE x0.85), with the ASSIST tag on results and no effect on unlocks or medals.
- [ ] Colorblind palette, Reduce FX and Reduce Flashing still work (pause menu).
- [ ] Anything that submits a score checks `Assist.Flagged` (leaderboard, Daily): with Assist used anywhere in the attempt it must refuse.

## 7. Audio

`docs/AUDIO.md` has the levels. By ear on the release build: Room 1 to melt 100 % with an alarm and a death; no crackle, the music dips under the alarm and the death card, the four sliders each move only their bus.

## 8. Rollback

Pages are immutable deployments, and the Unity build files have hashed names and a one-year cache, so a rollback is safe against caches; `index.html` is `no-cache`.

```
vercel ls desync --prod              # find the last good production deployment
vercel rollback <deployment-url>     # point production back at it (no rebuild)
vercel rollback status               # confirm
vercel promote <deployment-url>      # roll forward again later
```

Then run `BASE_URL=https://desync-nine.vercel.app tools/e2e.sh` against production. A rollback never touches players' saves: they live in the browser, and an older build ignores keys it does not know (and refuses, rather than downgrades, a save newer than it understands). Rehearse it once on a preview before launch day. The plan matters: Vercel limits which deployments a rollback can target on some plans, so check which you have.

## 9. Smoke steps (5 minutes, on the production URL)

1. Open the page in a clean profile. The title is up in a few seconds; the footer and tab title describe the current game.
2. Answer the story prompt (SKIP), click to start CLASSIC. Move, flip with Space, hack a terminal with E and the arrows.
3. Die on purpose (stay in SOURCE), watch the death card, press R: back in the room almost at once.
4. Pause with Esc: the controls list, SOUND + ACCESS and KEY BINDINGS open and close; change a volume; reload; it stuck.
5. Quit to the title, start QUICK: map, pick a node, clear a room, draft, results with the share text (`C` copies).
6. Open the console: no red errors; `window.__DESYNC__.state` is a string; `window.__desyncEvents` has events.
7. `tools/e2e.sh` against the URL (`BASE_URL=...`), then `E2E_STRICT=1` on the release build for E2E-04 (`?autoplay=1&seed=2026&timescale=4&act=1` clears Act 1).

## 10. Known gaps at the time of writing

- Download size is over the spec budget (section 2).
- E2E-04 is a flow check: its bot wins rooms through the debug path (god mode, open exit, reach it), so it proves a run can be finished on a fixed seed, not that it is fair. Difficulty is the bot-sim's job.
- HUD size scales the in-room HUD only. The menus are authored for 1080p and would not fit at 140 %.
- In-world prompt text that Enemies builds ("E  HACK") is stripped to its action word by the HUD and the key glyph is drawn from the live binding; story captions that name Space still say Space after a remap.
- Edge and real Safari are not covered by the automated runs.
