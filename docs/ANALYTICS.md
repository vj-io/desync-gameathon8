# Analytics and privacy

The WebGL build reports a handful of anonymous gameplay events to **Vercel Web Analytics** as custom events. It exists to answer "do people get past room 1, where do they die, do they find the settings", nothing else. No account, key or secret is involved: the page loads Vercel's own script, which only exists on a Vercel deployment with Web Analytics switched on.

## How it flows

```
game code            Analytics.Track(name, props)            Desync.Net/Analytics.cs
  -> JSON props      {"mode":"Quick","won":true,...,"pid":"1a2b3c4d5e6f"}
  -> Desync_Track    Assets/Plugins/WebGL/DesyncPage.jslib
  -> page            window.__desyncTrack(name, json)        WebGLTemplates/Desync/index.html
  -> Vercel          window.va('event', { name, data })      script: /_vercel/insights/script.js
```

Outside a WebGL build (editor, desktop) `Track` records nothing unless a provider key is set, and then only fills a bounded in-memory queue. There is no network code in the game itself.

## What is sent

Every event also carries `pid` (see Privacy). Props are flat, with strings cut at 100 characters, as Vercel requires.

| Event | When | Props |
| --- | --- | --- |
| `game_loaded` | the title is up | `build` (git short sha of the build) |
| `run_start` | a mode starts from the title | `mode` (the run mode's name: Classic, Quick, Campaign, Daily, ..., or Deep), `assist`, `level` (Deep Layer only) |
| `room_start` | a room starts | `room` (`r1`..`r6`, or a node id in a BREACH run), `seed` |
| `room_end` | a room is cleared | `room`, `time` (s), `score`, `grade`, `medal` |
| `restart` | a room is restarted | `room` |
| `death` | the player dies (not in god mode) | `cause` (Ice, Melt, Alarm, Drone, Architect, TraceTimer, Glitch, Guard), `room`, `assist` |
| `run_end` | a run ends | `mode`, `won`, `rooms` (cleared), `score`, `time` (s), `assist` |
| `story_seen` | the story reached its last card | `at` = `story` |
| `story_skipped` | the story was skipped | `at` = `prompt` (first-time question) or `story` (Esc / held Space) |
| `share_copied` | the share text was copied | none |
| `settings_changed` | an accessibility or audio option changed (reported once the player stops, about 1.5 s) | `setting`, `value` |

`settings_changed` names: `assist`, `hud_scale`, `subtitle_scale`, `high_contrast`, `tab_toggle`, `reduce_fx`, `reduce_flashes`, `shake`, `head_bob`, `colorblind`, `subtitles`, `vol_master`, `vol_music`, `vol_sfx`, `vol_ui`, `custom_binds`. Values are `0`/`1` or a number with one decimal. `custom_binds` is how many keys differ from the defaults: **which keys is never sent**.

## Privacy note

- **No personal data.** Nothing typed by the player, no names, no free text, no save contents, no IP address handling of our own, no cookies, no local-storage identifiers set by the analytics code. Vercel documents Web Analytics as cookie-free; see their documentation for what the service itself records (page, referrer, country, browser, device) and for retention.
- **`pid` is a hash.** It is the first 12 hex digits of SHA-256 of `"desync:"` plus the random profile id that lives in the save (`SaveV3.playerId`, a GUID made on first launch). The profile id itself never leaves the browser. `pid` lets one profile's events be followed across a session and across days without identifying anyone. It is pseudonymous, not anonymous: clearing site data makes a new profile and a new `pid`. If that linkage is not wanted, delete the `pid` lines in `Analytics.ToJson` and the test that expects them.
- **Do Not Track is honoured twice.** The page loads no analytics script at all when the browser sends DNT, and `Analytics.Track` records nothing (`Analytics.DoNotTrack`, read through `Desync_DoNotTrack`).
- **Localhost and file pages** do not request the Vercel script (no 404, no noise); events are still kept in `window.__desyncEvents` for tests.
- **Gameplay facts only.** The seed, the room id, the time and the death cause are the game's own numbers; they identify a room attempt, not a person.
- **What would change this note:** a leaderboard name, a login, or any text field. None of those go through `Analytics.Track`; a feature that wants one needs its own review.

## Switching it on

1. Vercel dashboard, the project, **Analytics**, enable **Web Analytics**. No code change: the page already contains the stub (`window.va`) and, on a real host, the script tag for `/_vercel/insights/script.js`.
2. Custom events are a plan feature: check the project's plan includes them. Without them the page views still work and the `va('event')` calls are simply not recorded; the game is unaffected either way.
3. Deploy (`tools/deploy.sh`), play a room, and look for the events in the Analytics tab (they appear after a short delay).

## Checking it

- Tests: `Desync.M5.Tests.AnalyticsTests` (payload, flatness, hashing, Do Not Track, the sink) and `Desync.M5.Tests.SettingsUITests` (what the run and settings relays send); `tests/e2e/template.spec.js` (the page side: forwarding, Do Not Track, script injection) and `E2E-A1` in `tests/e2e/e2e.spec.js` (a built game's `game_loaded` reaches the page).
- By hand: open the game in a browser, then in the console `window.__desyncEvents` lists what was handed to analytics. With `navigator.doNotTrack = "1"` (or the browser's setting) it stays empty.
- A new event: call `Analytics.Track("name", ("key", value), ...)` with numbers, booleans and short strings only, add a row to the table above, and add it to the privacy review if it carries anything the player chose.
