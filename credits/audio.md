# Audio credits (workstream `audio`)

Everything under `DESYNC/Assets/Audio/` is either derived from the CC0 sources below or generated in-house (numpy synthesis, public domain / CC0 by the DESYNC team).
All source downloads were treated as data; only derived, processed clips are shipped.
Reproduce with `.work/audio/venv/bin/python .work/audio/tools/build_music.py` and `.../build_sfx.py` (sources in `.work/audio/dl/`).
The M3 additions (act 2 / act 3 stems, music layers, run-flow / scoring / enemy cues) come from `.work/audio3/tools/build_music3.py` and `build_sfx3.py` (run from `.work/audio3/tools`, same Python environment; sources in `.work/audio3/dl/`, copies of the packs below).

## Source packs

| Title | Author | Licence | URL | Used for |
| --- | --- | --- | --- | --- |
| Cyber Runner Music (`cyber_runner.ogg`, 120 BPM synthwave, 98 s) | Ansimuz (Luis Zuno) | CC0 (per OpenGameArt page; bundled `public-license.txt`: free for personal and commercial use, modification and redistribution) | https://opengameart.org/content/cyber-runner-music | all four music stems |
| Midnight Drive (`midnight_drive.ogg`, 106.9 BPM synthwave, 204 s) | congusbongus | CC0 (per OpenGameArt page) | https://opengameart.org/content/midnight-drive | act 2 stems |
| Space City (`space_city_bpm115_0.ogg`, 115 BPM synthwave, 117 s) | MintoDog | CC0 (per OpenGameArt page) | https://opengameart.org/content/space-city | act 3 stems |
| Sci-Fi Sounds 1.0 | Kenney (kenney.nl) | CC0 1.0 | https://kenney.nl/assets/sci-fi-sounds | flip, ICE, death, portal, finale layers |
| Interface Sounds 1.0 | Kenney (kenney.nl) | CC0 1.0 | https://kenney.nl/assets/interface-sounds | UI, glitch, error, hack layers |
| Digital Audio 1.0 | Kenney (kenney.nl) | CC0 1.0 | https://kenney.nl/assets/digital-audio | power-ups, zaps, phasers, hijack |
| Impact Sounds 1.0 | Kenney (kenney.nl) | CC0 1.0 | https://kenney.nl/assets/impact-sounds | footsteps, thuds, punches, bell, plates |

Kenney licence text (each pack's `License.txt`): "Creative Commons Zero, CC0 ... free to use in personal, educational and commercial projects. Support us by crediting Kenney or www.kenney.nl (this is not mandatory)". Credit given here.

## Music (`Assets/Audio/Music`, 48 kHz, 32.000 s = 16 bars @ 120 BPM = 1,536,000 samples each)

| File | What | Processing (ffmpeg / numpy) |
| --- | --- | --- |
| `music_render.ogg` | RENDER stem, stereo | Cyber Runner bars 12-28 (24.0 s-56.0 s) cut on a bar line; last 1 s equal-power cross-faded with the 1 s that preceded the cut so the loop end flows into the start |
| `music_source.ogg` | SOURCE stem, mono | same loop: highpass 70 Hz, `acrusher` 6 bit + 5x sample-rate reduction, lowpass 7 kHz, slow pitch wobble (`vibrato` 0.3125 Hz, 10 whole cycles per loop), run on a 3x tiled loop with the middle copy kept so effects wrap seamlessly, time-aligned to the RENDER stem |
| `music_render_lp.ogg` | RENDER low-passed (melt), mono | 4th-order low-pass 600 Hz (3x tiling) |
| `music_source_lp.ogg` | SOURCE low-passed (melt), mono | same low-pass on the SOURCE stem |

### Act 2 and act 3 stem sets (M3; same 32.000 s / 16 bars @ 120 BPM / 1,536,000 samples, so every set and layer shares one bar grid)

Each set is cut and derived exactly like act 1 (render = cross-faded loop, source = bit-crush + sample-rate reduction + wobble, `_lp` = 600 Hz low-pass), then the render loudness is matched to the act 1 render (RMS 0.1405). Both source tracks are brought to 120 BPM by varispeed (resampling, so no time-stretch artefacts; the key moves with it) and cut on a beat, 16 bars long, at the start where the chords before the cut best match the chords at the end of the loop (chroma similarity over four bars) and the energy is steady; the last second is cross-faded with the second before the cut.

| Files | Source | Loop | Varispeed |
| --- | --- | --- | --- |
| `music_render_a2.ogg`, `music_source_a2.ogg`, `music_render_lp_a2.ogg`, `music_source_lp_a2.ogg` | Midnight Drive | bar 48 of the 120 BPM grid (96.84 s) | x1.1225 (+1.97 semitones) |
| `music_render_a3.ogg`, `music_source_a3.ogg`, `music_render_lp_a3.ogg`, `music_source_lp_a3.ogg` | Space City | bar 25 (50.48 s) | x1.0435 (+0.74 semitones) |

### Music layers (M3; mono, 48 kHz, 32.000 s, in-house synthesis from seeded noise and oscillators, pitch-neutral so they sit over any act; CC0 by the DESYNC team)

| File | Content | Plays |
| --- | --- | --- |
| `music_tension.ogg` | thump on every beat, 16th-note ticks, a 4-bar band-passed noise riser into every fourth bar line with a low impact on the line | fades in with melt: clamp((melt - 50) / 40, 0, 1) |
| `music_trace_a.ogg` | scanner clicks on the 8ths, offbeat 1760 Hz pings with a dotted-8th echo, a radar sweep every 2 bars | TRACE level 1 and up |
| `music_trace_b.ogg` | klaxon alternating B5 / F#5 on the 8ths (square + saw, tremolo), 46 Hz sub throb ducked on every beat | TRACE level 2 and up |

## SFX (`Assets/Audio/SFX`, 44.1 kHz, OGG Vorbis ~q0.5-0.6). "Kenney:" = CC0 clip from the pack above, layered/re-pitched/trimmed; "synth" = generated in-house (sfxr-style oscillators, noise, FM; CC0).

| File | Components |
| --- | --- |
| `flip_source.ogg` | synth rising band-pass whoosh; Kenney Sci-Fi `explosionCrunch_000` (bit-crushed), `impactMetal_001`; Interface `glitch_002` |
| `flip_render.ogg` | synth falling whoosh; Kenney Sci-Fi `forceField_004`; Impact `impactPlate_light_002` |
| `flip_reject.ogg` | synth AM square buzz; Kenney Interface `error_006`, `glitch_004` |
| `camera_tick.ogg` | Kenney Interface `tick_004`; Impact `impactTin_medium_002`, `impactMetal_heavy_001` |
| `trace_alarm.ogg` | synth klaxon (square + saw pulses, echo) |
| `ice_windup.ogg` | synth saw + noise riser; Kenney Digital `phaserUp5` |
| `ice_shriek.ogg` | synth FM scream (distorted); Kenney Sci-Fi `explosionCrunch_003`, `laserLarge_002` |
| `ice_hum.ogg` (4.0 s seamless loop, mono) | Kenney Sci-Fi `spaceEngineLow_000` (tail cross-faded into head, band-passed 60 Hz-1.2 kHz, 7-bit `acrusher` grit, ffmpeg) + synth drone (55/110/330 Hz sines, 100 Hz square buzz, 0.5 Hz phase wobble, 8 Hz tremolo; every frequency a whole number of cycles per loop). Rebuild: `.work/audio/venv/bin/python .work/polish-al/build_ice_hum.py`. Played by `AudioManager` as one looping 3D voice on the nearest live ICE hunter |
| `hack_tick.ogg` | synth 8-bit square blip (E5), pitched 2^(step/12) at runtime |
| `hack_wrong.ogg` | synth falling square; Kenney Digital `lowRandom`; Interface `error_004` |
| `hack_start.ogg`, `hack_cancel.ogg` | Kenney Interface `maximize_004` + `toggle_001`, `minimize_004` + `toggle_002` |
| `access_granted.ogg` | synth Cmaj9 saw stab; Kenney Digital `powerUp12` |
| `room_clear.ogg` | synth rising arpeggio + Cmaj9 swell; Kenney Digital `powerUp3` |
| `heartbeat_loop.ogg` (0.9 s loop) | Kenney Impact `impactSoft_heavy_000`, `impactSoft_heavy_002` (low-passed) + synth sub sines |
| `flatline.ogg` | synth 880 Hz ECG tone; Kenney Impact `impactSoft_heavy_003` |
| `death_crunch.ogg` | Kenney Sci-Fi `explosionCrunch_001` (bit-crushed), `lowFrequency_explosion_001`; Impact `impactPunch_heavy_001` |
| `reboot_wipe.ogg` | synth noise sweep; Kenney Digital `highDown`; Interface `glitch_001`, `glitch_003`; Impact `impactSoft_medium_001` |
| `footstep_render_0..4.ogg` | Kenney Impact `footstep_carpet_000`, `_001`, `_003` (the pack's other two takes are byte-identical duplicates) + two re-pitched twins (office carpet) |
| `footstep_source_0..4.ogg` | the five RENDER footsteps through ffmpeg `acrusher` (4 bit, 7x reduction) + low-pass |
| `hijack_on.ogg`, `hijack_off.ogg` | Kenney Digital `zapThreeToneUp`, `zapThreeToneDown` |
| `glitch_beep.ogg` | synth 1 kHz square beep |
| `portal_open.ogg` | Kenney Sci-Fi `doorOpen_001`, `forceField_001`; Digital `powerUp5` |
| `ui_click.ogg`, `ui_pop.ogg` | Kenney Interface `click_001` + `tick_001`; `confirmation_001` |
| `finale_sting.ogg` | Kenney Sci-Fi `lowFrequency_explosion_000`; Impact `impactBell_heavy_001`; Interface `glitch_001..004`; synth Dm9 saw pad |

### M3 cues (44.1 kHz, OGG Vorbis; "Kenney:" = CC0 clip from the pack above, layered/re-pitched/trimmed; "synth" = generated in-house, CC0)

| File | Components |
| --- | --- |
| `score_pop.ogg` | synth triangle blip + octave shimmer; pitched at runtime one semitone per GHOST chain step |
| `redline_whoosh.ogg` | synth band-pass noise falling 5.2 kHz -> 260 Hz, sine drop 150 -> 34 Hz, synth noise rising 280 Hz -> 6.2 kHz; Kenney Digital `lowDown`, `highUp` |
| `graze_whoosh.ogg` (3D, mono) | synth band-pass noise sweep 2.6 kHz -> 600 Hz; Kenney Sci-Fi `laserSmall_003` (re-pitched) |
| `map_open.ogg` | synth band-pass noise sweep; Kenney Interface `open_003` |
| `map_choose.ogg` | synth square notes E5 / B5; Kenney Interface `select_003` |
| `decrypt_tick.ogg` | synth 1.76 kHz square blip; Kenney Interface `tick_001` (re-pitched); 16 of them play over 0.8 s, pitch rising one octave |
| `draft_pick.ogg` | synth square arpeggio C5 E5 G5 C6; Kenney Digital `powerUp2`; Interface `confirmation_003` |
| `market_buy.ogg` | synth sine notes E6 / B6; Kenney Impact `impactGlass_light_001`; Interface `pluck_001`, `confirmation_004` |
| `cooldown.ogg` | synth noise exhale and Fmaj7 triangle pad; Kenney Impact `impactBell_heavy_000` (pitched down) |
| `tracker_sting.ogg` (3D, mono, 1.0 s) | synth reversed noise swell, 880 -> 660 Hz sonar ping, FM growl climbing an octave; Kenney Sci-Fi `laserLarge_002` (re-pitched) |
| `auditor_p2.ogg`, `auditor_p3.ogg` | synth saw riser 220 -> 880 Hz with tremolo and sub drop, three rising klaxon blasts (880 / 1175 / 1568 Hz); Kenney Impact `impactMetal_heavy_002`; Sci-Fi `forceField_002`, `lowFrequency_explosion_000`; Interface `glitch_004`; Digital `powerUp7` |
| `uplink_tick.ogg` | synth 1.32 kHz square blip; Kenney Interface `tick_004`; pitched 1.0 -> 1.5 as the countdown runs out |
| `lantern_hum.ogg` (3D, mono, 4.0 s seamless loop) | synth sines at 220 / 330 / 880 Hz and a phase-modulated 440 Hz voice, 3 Hz tremolo (every frequency a whole number of cycles per loop) |
| `sentinel_charge.ogg` (3D, mono, 0.6 s) | synth saw 70 -> 760 Hz, band-pass noise riser; Kenney Sci-Fi `thrusterFire_002` |
| `sentinel_pulse.ogg` (3D, mono) | synth sine drop 110 -> 30 Hz; Kenney Sci-Fi `forceField_003`, `lowFrequency_explosion_000`; Impact `impactMetal_heavy_003` |
| `splitter_split.ogg` (3D, mono) | synth saw glides 400 -> 1000 Hz and 400 -> 160 Hz; Kenney Impact `impactGlass_light_002`; Interface `glitch_002` |
| `daemon_cross.ogg` (3D, mono) | synth bit-crushed noise sweep, FM growl; Kenney Sci-Fi `explosionCrunch_002` (reversed); Interface `glitch_001`, `glitch_003` |
| `watcher_beam.ogg` (3D, mono) | synth FM lock-on sweep 300 Hz -> 1.8 kHz, 2.09 kHz ping; Kenney Sci-Fi `computerNoise_001` |
| `alert_suspicious.ogg`, `alert_hunting.ogg`, `alert_return.ogg` (3D, mono) | synth triangle notes G5 / C6 (rising), FM rasp climbing a fourth over band-pass noise, sine notes E5 / A4 (falling); Kenney Interface `question_001`; Impact `impactMetal_light_002` |
| `wall_swap.ogg` | synth sine drop 90 -> 40 Hz, bit-crushed noise sweep; Kenney Impact `impactMetal_heavy_000`; Sci-Fi `doorClose_001`; Interface `glitch_001` |
