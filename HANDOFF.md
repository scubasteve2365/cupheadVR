# CupheadVR handoff

Updated 2026-11-08 (RigB).

## Where things stand

1.02 is the public release (GitHub v1.02, latest, published 2026-11-09 on Steve's go-ahead).
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

## Reference v1.4, section 2a

Reviewed 2026-11-09 against 1.02. Steve, 2026-11-09: this game is compliant enough as is, no action. The gaps listed in the thread
(auto recenter timing, focus, menu placement rows, resolution now and next, DualSense, ultrawide check and others) are not planned.
