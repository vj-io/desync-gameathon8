# Audio credits (workstream `audio`)

Everything under `DESYNC/Assets/Audio/` is either derived from the CC0 sources below or generated in-house (numpy synthesis, public domain / CC0 by the DESYNC team).
No AI-generated audio. All source downloads were treated as data; only derived, processed clips are shipped.
Reproduce with `.work/audio/venv/bin/python .work/audio/tools/build_music.py` and `.../build_sfx.py` (sources in `.work/audio/dl/`).

## Source packs

| Title | Author | Licence | URL | Used for |
| --- | --- | --- | --- | --- |
| Cyber Runner Music (`cyber_runner.ogg`, 120 BPM synthwave, 98 s) | Ansimuz (Luis Zuno) | CC0 (per OpenGameArt page; bundled `public-license.txt`: free for personal and commercial use, modification and redistribution) | https://opengameart.org/content/cyber-runner-music | all four music stems |
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
