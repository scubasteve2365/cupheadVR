# CupheadVR handoff

Updated 2026-11-08 (RigB).

## Where things stand

1.04 is the public release (GitHub v1.04, latest, published 2026-11-11 on Steve's go-ahead; Insert key, DualSense experimental and not verified with a real pad, speed log).
1.02 adds the Picture setting (Auto, Steady, Panel; RE_NOTES has the results). Steve tested 1.0 on Pimax Dream Air (SteamVR) and
Quest 3 (Virtual Desktop). 1.01 and 1.02 tested on RigB only.

| File | sha256 starts |
|---|---|
| build/CupheadVR.dll | cf669e61f91dd82d |
| build/CupheadVRNative.dll | 1b2711e371dcf305 |
| build/openxr_loader.dll (Khronos 1.1.65 x64) | b0d0eed4e2b6aeb3 |
| build/package/CupheadVR-1.02.zip | 7227d36e43a6cc52 |

The repo is public: test work stays in local commits, and nothing is pushed without Steve's OK.

## For Steve to test in the headset

On Quest 3 through Virtual Desktop: the Picture row should read Auto (Steady); turn your head and check the stage and menu hold still.
On the Pimax: Auto (Panel). Compare Steady and Panel for sharpness and steadiness.

## Open questions

1. Strict zero-difference against the back buffer (0.2% of pixels off by 1).
2. Game speed is by construction (1/60 fixed step kept); 72, 90 and 120 Hz not run on RigB (null driver is 144 only).
3. Local co-op not tested.
4. The game pauses when its window loses focus (RE_NOTES, 1.02). Unchanged; ask Steve whether to keep it running.

## Standards: STANDARDS.md "Build 1 baseline"

Reviewed 2026-11-09 against 1.02. Steve, 2026-11-09: this game is compliant enough as is, no action. The gaps listed in the thread
(auto recenter timing, focus, menu placement rows, resolution now and next, DualSense, ultrawide check and others) are not planned.

## LESSONS_LEARNED.md check (2026-11-12, against 1.04)

| Entry | Answer for Cuphead VR | What a fix would take |
|---|---|---|
| Game's HDR setting on: black splash and menus | Fine, by design. Cuphead has no HDR option (SettingsData fields, static), and Unity 2017.4 here outputs 8-bit. The mod never copies the game's screen: every eye image, the menu and the HUD (inside the stage) are rendered by the mod into its own 8-bit ARGB32 textures (log `eye texture ... format 27`), so a 10-bit or 16-bit back buffer would not reach it. Not logged: the game has no HDR setting to log, and Windows HDR is not logged | Log Windows HDR for the game's display and the back buffer format at start: small, one build |
| 32-bit game runs flat with -51 | Not applicable: Cuphead is 64-bit | None |
| 32-bit game fails with -32 (64-bit-only add-on) | Not applicable as written. The 64-bit mirror is handled: each implicit layer is checked at start and any non-64-bit one is switched off for this process, every layer logged (`OpenXR layer ...`) | None |
| An add-on of the right bitness refuses to load (-32) | Problem: no retry. On -32 the mod logs the failure and the game stays flat | On -32, retry once with every implicit layer that has an off switch switched off, log which: small, native only |
| VR fails and the player sees nothing | Problem: on any OpenXR start failure the game runs flat with only log lines | Show "VR DID NOT START" with the error and a plain step, on the flat screen and in the mod menu: small to medium |
| Head moved away from the recenter point (added from this mod) | Fixed in 1.01 | Already fixed |
