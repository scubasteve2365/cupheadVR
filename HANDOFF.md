# CupheadVR handoff

Updated 2026-11-08 (RigB).

## Where things stand

1.01 is the public release (GitHub v1.01, latest). Native stereo on a room-fixed stage, depth from the game's parallax
speeds, smooth motion at headset rate, edge match, Cuphead-card menu. Details and evidence are in RE_NOTES.md.
Steve tested 1.0 on Pimax Dream Air (SteamVR) and Quest 3 (Virtual Desktop). 1.01 on RigB only, plus release by Steve's go-ahead.

| File | sha256 starts |
|---|---|
| build/CupheadVR.dll | f3b7f8d0806f508f |
| build/CupheadVRNative.dll | 4de01299ffea4d1f |
| build/openxr_loader.dll (Khronos 1.1.65 x64) | b0d0eed4e2b6aeb3 |

The repo is public: test work stays in local commits, and nothing is pushed without Steve's OK.

## Planned: Picture setting, Auto / Steady / Panel (standard for every mod, Steve 2026-11-08)

Not started. Today every flat thing is a quad: the stage (one quad per eye, CopyEye in mod/native/CupheadVRNative.cpp) and
the menu (CopyMenu). The HUD is inside the stage image. The black backdrop is already a projection layer.
Auto: projection on Virtual Desktop, quad elsewhere. VDXR reports its runtime as "VirtualDesktopXR 1.1.0" (no space, Steve's
Quest 3 log, 2026-11-08), so the match must be on "virtualdesktop" or "vdxr", not "Virtual Desktop".
Work: full-view swapchain per eye in place of the 16x16 backdrop, a native pass that draws each eye's stage image and the
menu at their room poses with that frame's eye pose and FOV, smoothed edges, the menu row, and the runtime and layer log lines.

## Open questions

1. Strict zero-difference against the back buffer (0.2% of pixels off by 1).
2. Game speed is by construction (1/60 fixed step kept); 72, 90 and 120 Hz not run on RigB (null driver is 144 only).
3. Local co-op not tested.
