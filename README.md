# DESYNC

`SPACE = SEE THE CODE`

> You are standing in HALCYON Corp's neon office. Press SPACE and the room tears apart into falling green code: the walls stop existing, the cameras go blind — but hunter-ICE lives in the code, and every second there melts your brain. Flip in and out of reality to slip past surveillance, breach terminals, hijack the company's drones, and reach the Root to unmask the Architects before you flatline.

First-person stealth-action intrusion · 7–9 min · 6 rooms · mouse + keyboard (gamepad optional) · Gameathon 8.0 entry

## Play in browser

[https://desync-nine.vercel.app](https://desync-nine.vercel.app)

Chrome recommended. Click **CLICK TO JACK IN** on the title screen: the click locks the pointer and starts audio.

## Controls

| Keyboard / mouse | Gamepad | Action |
| --- | --- | --- |
| Mouse | Right stick | Look (sensitivity in pause menu) |
| WASD | Left stick | Move, 5 m/s (no sprint, no jump) |
| Space | South | Flip layer, RENDER ↔ SOURCE |
| E (terminal within 2.5 m) | West | Start hack |
| Arrow keys | D-pad | Enter the sequence during a hack |
| R | — | Restart room |
| Esc | — | Pause. The browser releases the pointer on Esc; click to resume |
| F2 (hidden) | — | Level select |
| F9 (hidden) | — | Trace-free demo mode |

## How it plays

Space flips the world between two layers. Each room is a stealth puzzle: read it, flip to slip past what is watching, dodge the threats, breach the terminals, reach the portal.

- **RENDER (the lie):** walls are solid. Cameras and drones are active.
- **SOURCE (the truth):** walls are passable. Cameras are blind. ICE hunters are active.
- **Flip rule:** a flip is rejected if you would end inside a wall.
- **Melt:** the brain-melt meter fills in SOURCE (+12 %/s, +5 % per flip) and drains in RENDER (−4 %/s). At 100 % you reboot.
- **TRACE:** staying in a camera cone for 0.5 s starts a 6 s TRACE: cones turn red and ICE spawns. Three TRACEs in one room restart it.
- **ICE:** hunters spawn from vents in SOURCE and chase you through the floor, where walls used to be. Touching one is a fail.
- **Terminals:** press E, then enter the arrow sequence. A wrong key resets it after a 0.4 s lockout. Each breach leaks a line of the Architects' memo.
- **Fail:** an ICE touch, melt at 100 % or a third TRACE: 0.2 s REBOOT, then an identical restart. No death screen.

> You process tickets at HALCYON Corp. Your city is a render. Your boss is a puppet.
>
> Every terminal you breach leaks a line of the Architects' memo: the city is a training set, and you are the data.
>
> Reach the Root, tear off their masks in SOURCE, and broadcast their real faces before your brain melts.

**Rooms**

1. **BOOT**: flip; walls vanish
2. **EYES**: cameras; phase past the cones
3. **ICE**: hunters; melt; flip back to live
4. **HIJACK**: both layers lethal; hijack drones; mirrored prompts
5. **UNSTABLE**: glitch zones; scrambled prompts; TRACE countdown (90 s)
6. **ROOT**: the Architect's gaze sweeps both layers; three shield terminals

## Run it

- **Web (primary):** open the link above. Test matrix: Chrome (primary), Edge, Firefox, Safari 17+.
- **macOS:** the build is unsigned. Right-click the app, choose Open, then confirm to get past Gatekeeper.
- **Windows:** Mono build. Run the game `.exe`.
- Native builds are not in git (`Builds/` is ignored). Build them with `tools/build.sh MacRelease` or `tools/build.sh WinRelease`.

## Tech

- Unity 6.4 (6000.4.6f1), Universal Render Pipeline 17.4, Input System, WebGL2 build.
- Rooms are code-generated greybox meshes; shaders are hand-written URP HLSL. No Asset Store, ProBuilder or Shader Graph.
- Hosted on Vercel. Build the web target with `tools/build.sh WebRelease`; deploy with `tools/deploy.sh --prod` (omit `--prod` for a preview).
- Spec: [DESYNC_FP_PRD.md](DESYNC_FP_PRD.md). Engineering decisions and the build contract: [CONTRACT.md](CONTRACT.md). Where they differ, CONTRACT.md wins. Built by parallel agent workstreams against that contract.

## Known issues

- Gamepad UI navigation is not implemented.
- Safari pointer-lock behaviour is untested.

## Team

BD (PRD owner).

## Credits

Third-party assets, licences and sources: [CREDITS.md](CREDITS.md).

`#Gameathon8 #TheMatrix #BreakTheSimulation`
