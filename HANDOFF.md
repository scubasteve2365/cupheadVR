# CupheadVR handoff

Updated 2026-11-07 (RigB).

## Where things stand

Build 3 (on RigA 2026-11-07, CupheadVR.dll sha256 dd87e3c1...f509f): build 2 plus smooth motion. Build 2: the game on a stage in the headset, native stereo (both eyes from one tick), depth from the game's own
parallax speeds, head tracked, black surround, in-headset menu. Details and evidence in RE_NOTES.md.

| File | sha256 |
|---|---|
| build\CupheadVR.dll | 7200e1b0f9a244ec79168fc90610206e425d1a036d53c45c358e96396b5c75ea |
| build\CupheadVRNative.dll | ffc2501c3da900bb847ff6ebb33709b5017f11b6d949d2ea3732acb6207b0cf3 |
| build\openxr_loader.dll (Khronos 1.1.65 x64) | b0d0eed4e2b6aeb3ce529d82daf3daa5db104857d65c872884b4661142366ce2 |

The RigA hand-off also carries BepInEx 5.4.23.6 x64 (winhttp.dll, doorstop files, BepInEx\core).

Menu: hold both sticks 0.5 s, or F1. Rows: Stage distance, Stage size, Depth, Pop-out, Sky distance, Sharpen,
Resolution, Recenter, Reset to defaults, Close. Defaults: 1.85 m wide at 1.0 m, depth 100%, sky 85%, sharpen 30%.

## Lab commands (vrlab_command.txt, Lab.Commands on in RigB's BepInEx\config)

`probe`, `dump <tag>`, `zerodiff <tag>`, `stereo <sep> <tag>`, `slot <n>`, `level <Levels name>`, `scene <Scenes name>`,
`vr on|off`, `xrshot`, `eyecheck`, `xrdump`, `stamp 0|1`, `depth`, `popout`, `sky`, `sharpen`, `stage <w> <d>`,
`recenter`, `menu` (toggle) and `menu up|down|left|right|a|b`, `match 0|1`, `track <s>`, `timescale`.
To the overworld: `py -3.12 C:\VRLab\games\CupheadVR\to_game.py 2`. Lab.BlockSaves is on in RigB's config.

## Open questions

1. Motion smoothness: build 3 draws moving things between the 60 Hz game steps (RE_NOTES, Build 3). Waiting for Steve.
   `match 1` (needs a way to drive the player in the lab: the virtual pad joins as Player 2).
2. Overworld: front facing, flat for now (Steve, 2026-11-07: not tilted, like Ori). No depth there yet.
3. Root Pack style skies: pushed back by default (Sky distance), waiting for Steve's view.
4. Strict zero-difference against the back buffer (0.2% of pixels off by 1).
5. HUD: on the stage (HP card), as agreed for diorama games.

## For Steve to test in the headset

See the thread message for build 2.
