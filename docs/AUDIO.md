# Audio mix

Everything the player hears goes through `AudioManager` (`DESYNC/Assets/Scripts/Audio`). WebGL has no AudioMixer and no audio filters (CONTRACT), so the whole mix is `AudioSource.volume` plus one listener gain. This page is the reference for the levels and for the three things the M5 mix pass added: four volume buses, ducking, and a limiter. The code is `AudioManager.Mix.cs` (formulas in `MixMath`), checked by `Desync.M5.Tests.MixTests`.

## Signal chain

```
clip (normalised to -1 dBFS, peak 0.89)
  x cue volume (table below)                      set where the cue is played
  x bus: effects or interface                     pooled cues: when they start (MixCue)
         music                                    music voices: every frame (LateUpdate)
  x music duck (music only)                       death card, stingers
  -> voices (16 at most: 7 music, 3 loops, pooled cues in the rest)
  x limiter gain                                  AudioListener.volume = master x limiter
```

Master is `Settings.Volume` (PlayerPrefs, default 80 %). Music, effects and interface are `A11y.MusicVolume / SfxVolume / UiVolume` (saved with the profile under `a11y_vol_*`, default 100 %, range 0-100 %). All four sliders are in the pause menu under SOUND + ACCESS and apply at once.

## Buses

| Bus | What rides on it |
| --- | --- |
| Music | the four base stems, tension layer, TRACE a / b layers (adaptive music, 7 voices) |
| Effects | everything in the world: flips, footsteps, camera ticks, ICE, hacks, alarms, death, hums, heartbeat, enemy and boss cues, officers |
| Interface | clicks, score pops, map / draft / market / cooldown sounds (through the pool), and the UI's own source (results tally ticks, story sounds: its `AudioSource.volume`) |

A cue is "interface" when its clip is one of `uiClick, uiPop, scorePop, mapOpen, mapChoose, decryptTick, draftPick, marketBuy, cooldown` (`AudioManager.IsUiCue`); add a clip there when a new menu sound is added.

## Cue levels (volume before the bus; 1.0 = clip at -1 dBFS)

| Group | Cue | Volume | Priority |
| --- | --- | --- | --- |
| Music | stems (RENDER / SOURCE crossfade, melt low-pass crossfade) | 0.55 total | loop |
| | tension / TRACE a / TRACE b layer (relative to the stems) | 0.80 / 0.70 / 0.65 | loop |
| Loops | heartbeat (x0.55 at melt 70 % up to x1.0 at 100 %) / ICE hum / Lantern hum | 0.8 / 0.50 / 0.40 | loop |
| Flip | SOURCE / RENDER / rejected | 1.0 / 0.9 / 0.7 | high |
| Threat | camera tick (3D) | 1.0 | low |
| | TRACE alarm (1st, 2nd) / alarm (3rd) | 0.6 / 0.8 | stinger |
| | ICE wind-up / shriek (3D) | 0.85 / 0.8 | mid / high |
| | perception: suspicious / hunting / return | 0.55 / 0.7 / 0.45 | low+ / mid |
| Death, flow | crunch / flatline (melt) | 0.8 / 0.55 | stinger |
| | reboot / room clear / portal open | 0.7 / 0.8 / 0.7 | stinger / high / high |
| | finale sting | 0.9 | stinger |
| Hack | start / tick / wrong / cancel / access granted | 0.6 / 0.6 / 0.55 / 0.6 / 0.8 | mid / high |
| Scoring | clean flip pop / other pops / graze whoosh / REDLINE whoosh | 0.22 / 0.35 / 0.9 / 0.85 | low+ / low+ / high / stinger |
| Bosses | AUDITOR phase 2 / 3 / uplink tick | 0.85 / 0.9 / 0.55 | stinger / stinger / high |
| | wall swap / string severed | 0.8 / 0.8 | high |
| Enemies (3D) | Sentinel charge / pulse, Splitter, Daemon, Watcher | 0.8 / 1.0, 0.9, 0.95, 0.85 | mid / high |
| | officer alert / step / wind-up / strike | 0.9 / 0.55 / 0.9 / 1.0 or 0.8 | high / low |
| Interface | click / pop | 1.0 / 0.6 | mid |
| | map open / choose / decrypt tick / draft pick / market / cooldown | 0.5 / 0.6 / 0.5 / 0.7 / 0.7 / 0.6 | mid / high |

"Stinger" means priority 9 or 10 (`PMax - 1` and up): these also duck the music (below).

## Ducking

The music gain is multiplied by a duck that aims at:

- **Death card** (`GameState.Phase == Recap`, the 1.2 s freeze): x0.35 (-9.1 dB).
- **A stinger cue** (priority 9-10), for 1.1 s after it starts: x0.55 (-5.2 dB).
- both at once: the lower of the two.

The duck falls at 12 units/s (about 50 ms) and recovers at 1.2 units/s (about 0.5 s), so a stinger is heard on top of the music and the music comes back without a jump. It stacks with the older music moves, which are unchanged: hack duck -4 dB (`GameConfig.hackDuckDb`), pause x0.35, finale / REDLINE sting x0.4.

## Limiter

No cue should clip, but the browser gives us no DSP, so the limiter works from the volumes of the voices that are playing:

```
estimate = sqrt( sum of voice volume^2 ) x 0.89          separate sounds add in power, and each clip peaks at 0.89
gain     = estimate <= 0.97 ? 1 : 0.97 / estimate        0.97 = -0.26 dBFS
```

`gain` moves toward its target with a 30 ms attack and a 0.5 s release, and multiplies the master (`AudioListener.volume = Settings.Volume x gain`). A single cue at full level is never touched; two full-level cues at once lose 2.3 dB. It is an estimate, not a true-peak limiter: it errs on the safe side and cannot see inter-sample peaks. Worked examples at the default buses:

| Moment | Estimated peak | Gain |
| --- | --- | --- |
| Music + a footstep | 0.86 | none |
| Melt 90 %, TRACE 2: stems + tension + TRACE a | 0.71 | none |
| Melt 100 %, TRACE 3: stems + all three layers | 0.78 | none |
| Death card: crunch + flatline + heartbeat, music ducked | 1.07 | -0.9 dB |
| Room clear + REDLINE whoosh + 3 score pops + music | 1.25 | -2.2 dB |
| Alarm + flip + hack tick + heartbeat + full music | 1.59 | -4.3 dB |
| 16 full-level voices (the cap) | 3.56 | -11.3 dB |

`AudioManager.I.LimiterGain` and `.MusicDuck` expose the live values for the editor and the tests.

## Checking it

- Formulas, bus scaling, duck and limiter on the listener: `tools/unity.sh test <tag> PlayMode Desync.M5.Tests.MixTests`.
- By ear on a build, with the default levels: play Room 1 to a melt of 100 % with an alarm running and the death card up. Nothing should crackle, the music should dip under the alarm and the death card and come back within about a second, and the pause-menu sliders should each move only their own bus (music: the loop; effects: footsteps and flips; interface: menu clicks and the results tally).
- A new cue: give it a clip normalised to -1 dBFS, a volume from the table's range, a priority (9 or 10 only if it should duck the music), and add it to `IsUiCue` if it belongs to a menu.
