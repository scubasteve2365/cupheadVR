# CupheadVR handoff

Updated 2026-11-07 (RigB, first session).

## Where things stand

Feasibility check done on RigB, no OpenXR yet. Details and evidence in RE_NOTES.md.

- Cuphead: Unity 2017.4.9f1, 64-bit Mono, D3D11, Steam build 20241530. BepInEx 5.4.23.6 x64 works with default settings.
- All cameras are orthographic and every sprite sits at z 0. Depth comes from the game's own parallax speeds: 16 to 25
  depth layers in run-and-gun levels, about 10 in the Root Pack boss, none on the overworld.
- Re-rendering the camera stack off-screen is exactly repeatable (0 pixels differ). Against the back buffer, up to 0.2% of
  pixels differ by 1 of 255: not yet a strict zero-difference.
- Both-eye test shots from the parallax speeds give correct depth signs and no vertical offset (script checked).
- No DLSS or upscaler work (Steve, 2026-11-07: the game has none).

Last build: build 1 (probes only), `build\CupheadVR.dll` sha256 `e6b716d3a6175a28...` (deployed on RigB). Not handed to RigA:
nothing to see in a headset yet.

## Lab commands (vrlab_command.txt, Lab.Commands on)

`probe`, `dump <tag>`, `autodump 0|1`, `zerodiff <tag>`, `stereo <sep> <tag>`, `slot <n>`, `level <Levels name>`,
`scene <Scenes name>`, `timescale <x>`. Get to the overworld: `py -3.12 C:\VRLab\games\CupheadVR\to_game.py 2`.

## Open questions

1. Overworld depth: no parallax layers. Tilted plane or depth by screen height?
2. Boss MinMax-mode layers (Root Pack ground and hills) carry 0: give them a fixed background depth or leave on the play plane?
3. Strict zero-difference against the game's own frame (capture into a render texture rather than the back buffer).
4. Game speed at 72/90/120 Hz: the game runs fixed 1/60 steps with targetFrameRate 60; not tested yet.

## Next

Port the OriVR skeleton (native OpenXR DLL, per-eye quad on a fixed stage, menu on hold both sticks 0.5 s), with the
per-eye layer shift from the stereo probe as the stereo method. Then Steve's first headset look via `lab.py CupheadVR handoff`.

## For Steve to test in the headset

Nothing yet.
