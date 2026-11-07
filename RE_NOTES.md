# CupheadVR RE notes

Started on RigB, 2026-11-07. "Live" means seen in the running game this session; "static" means read from the decompile only.
Decompiled code: C:\VRLab\re\CupheadVR\decomp (ILSpy 9.1, not in git). Evidence: C:\VRLab\evidence\CupheadVR\2026-11-07.

## Game build and install facts

| Fact | Value | How it was found |
|---|---|---|
| Install folder | `E:\SteamLibrary\steamapps\common\Cuphead` | appmanifest_268910.acf |
| Steam build | buildid 20241530 | appmanifest_268910.acf |
| Unity version | 2017.4.9f1 (6d84dfc57ccf) | output_log.txt line 1 (live), UnityPlayer.dll file version 2017.4.9.7177439 |
| Architecture | 64-bit (BepInEx: "Bits64", pointer size 8) | live, plugin log |
| Scripting | Mono, CLR 2.0.50727 (.NET 3.5 profile) | live, BepInEx log |
| Graphics | Direct3D 11.0, feature level 11.1, render thread on (`threaded=1`) | live, `%USERPROFILE%\AppData\LocalLow\Studio MDHR\Cuphead\output_log.txt` |
| Frame pacing | `targetFrameRate` 60, vSync 1, fixed step 1/60 s, max step 1/3 s | live, PROBE line |
| Window on RigB | 1280x720 | live, PROBE line |
| Saves | `%APPDATA%\Cuphead\cuphead_player_data_v1_slot_0..2.sav` (JSON, Steam cloud) | static `PlayerData.cs`, `OnlineInterfaceSteam.cs`; files listed |
| Input | Rewired (XInput 1.4) | live, log |
| Hashes | Cuphead.exe `c5fffd22...7ee02d`, UnityPlayer.dll `598135e3...399e11`, Assembly-CSharp.dll `d0eafb33...cbed` | sha256sum |

Steve's three save slots were copied to `C:\VRLab\backup\CupheadVR\saves_2026-11-07` before the first launch. The lab
plugin blocks every save write (`Lab.BlockSaves`, Harmony prefix on `OnlineInterfaceSteam.SaveCloudData` that reports
success and writes nothing). Seen live: "SAVE blocked" when entering slot 2. The save files' dates were unchanged after the session.

## BepInEx

BepInEx 5.4.23.6 x64 (GitHub release, zip sha256 `6c57a714...0a0755`, kept in `C:\VRLab\bin\BepInEx_5.4.23.6_x64`)
copied into the game folder. **Works live with the default config** (entry point Application, unlike Ori which needed Camera):
title screen reached, plugin loads, Harmony patches apply. The `isBatchMode` HarmonyX warning is harmless (same as Ori).

## Cameras (live, 2026-11-07)

All cameras are **orthographic**, at z 0, no rotation, near -10000 far 10000. Rendered in depth order into the back buffer:

| Camera | Depth | Ortho size | Mask | Clear | Role |
|---|---|---|---|---|---|
| ClearCamera | -100 | 5 | nothing | Color | clears the screen |
| LevelCamera (`CupheadLevelCamera`) or MapCamera (`CupheadMapCamera`) | -50 | levels 360 (Veggies) to 444 (2-1, zoomed); map 3.6 | all | Color | the game world. `CupheadLevelCamera.Current`, `CupheadMapCamera.Current` |
| UICamera(Clone) (`CupheadUICamera`) | 10 | 360 | UI | Nothing | pause menu, cards |
| CameraGlow | 71.3 | 5 | UI | Depth | renders into a texture (GlowBossTitle), map only |
| SceneLoaderCamera | 90 | perspective | Loader | Nothing | iris and fade transitions |
| RenderCamera(Clone) (`CupheadRendererCamera`) | 100 | 3.6 | nothing | Nothing | full-screen post: `ChromaticAberrationFilmGrain`, `BlurGamma` |

Levels use 1 world unit = 1 pixel at 720p (ortho 360). The map uses 100 pixels per unit (ortho 3.6).
`AbstractCupheadCamera.UpdateRect` letterboxes to 16:9 through `camera.rect` and changes `CanvasScaler` sizes.
The HUD (HP card) is a ScreenSpaceCamera canvas on the LevelCamera (`Level_HUD/Canvas`, plane distance 1, sorting layer UI).

Film grain and scratches (`ChromaticAberrationFilmGrain`) advance on a 40 Hz timer in a coroutine (`FRAME_TIME` 0.025 s,
`animate_cr`), not per render, so a second render in the same frame gets the same grain (static read; matches the
re-render result below).

## Layers and depth (live dumps, 2026-11-07)

Dump CSVs: `evidence\CupheadVR\2026-11-07\dumps\*.csv` (one row per renderer: sorting layer, order, position, parallax).

- **Every renderer sits at z 0 in levels.** Draw order is sorting layer then sorting order only. Sorting layers:
  Background -1, Default 0, Enemies 1, Player 2, Map 3, Projectiles 4, Effects 5, Foreground 6, ForegroundEffects 7, UI 8, Loader 9, Top 10, AchievementToast 11.
- **Depth is encoded in the parallax speed.** Run-and-gun levels use `PlatformingLevelParallax` (side Background or
  Foreground, layer 0 to 19, speed from `ParallaxPropertiesData` per theme, `transform.x = base + camera.x * speed`).
  Bosses use `ParallaxLayer.percentage` (modes Comparative, MinMax, Centered). The overworld uses `MapParallaxLayer`
  but world 3 had none.

| Scene | Renderers active | Distinct parallax depths | Range |
|---|---|---|---|
| Forest Follies (`scene_level_platforming_1_1F`) | 385 | 25 (9 foreground, 16 background) | -0.65 to 1.0 |
| Funfair Fever (`scene_level_platforming_2_1F`) | 564 | 16 (9 foreground, 7 background) | -0.45 to 0.65 |
| The Root Pack (`scene_level_veggies`) | 42 | 9 plus 21 MinMax-mode renderers at 0 | -0.3 to 0.45 |
| Overworld 3 (`scene_map_world_3`) | 176 | none; z varies -13.7 to 2.5 on a few objects | n/a |

What this means: a layer with speed s moves like a layer at depth d/(1-s) behind a plane at distance d (s=1 is infinitely
far, s<0 is in front). So the game's own numbers give a physically consistent depth per layer, no hand-made table needed
for run-and-gun levels and Comparative boss layers. Open: MinMax-mode boss layers (Veggies ground and hills) carry
percentage 0 and move within a range, so they sit on the play plane for now; the overworld needs a different mapping
(it is drawn top-down at an angle, so depth by screen height or a tilted plane are candidates).

## Zero-difference control (live, 2026-11-07)

Command `zerodiff`: at end of frame, read the back buffer, then render every enabled camera in depth order with its own
settings into a 1280x720 ARGB32 render texture, twice.

| Scene | Re-render vs re-render | Re-render vs game's back buffer |
|---|---|---|
| Overworld 3 | 0 of 921,600 | 19 differ, max difference 1 of 255 |
| Forest Follies | 0 | 645, max 1 |
| Root Pack | 0 | 1,611, max 1 |
| Funfair Fever | 0 | 2,010, max 1 |

The off-screen path is exactly repeatable (what the per-eye path needs). Against the back buffer, 0.002% to 0.2% of pixels
differ by one step, scattered over the frame with no pattern (signed mean +0.03). Not proven why; likely a rounding
difference between writing to the swapchain and to an ARGB32 texture in the final blur/gamma pass. **Not yet a strict 0.**
Next step to close it: compare against the game's own frame captured into a render texture (target swap on RenderCamera)
instead of the back buffer.

## Stereo test shot (live, 2026-11-07)

Command `stereo <sep> <tag>`: at end of frame, per eye shift each parallax transform by `±e * s` (e = sep x half the view
width), render the camera stack off-screen, restore. Camera and HUD unchanged, so the play plane and HUD have zero parallax.
Script `tools\stereo_check.py` (32 px block matching). Images: `evidence\CupheadVR\2026-11-07\stereo`.

| Shot | Vertical offset (patches at dy 0) | Horizontal disparity, right minus left, px at 1280 |
|---|---|---|
| Funfair Fever, sep 0.034 (natural for a 1.85 m stage) | 409 of 456 | foreground -2 to -13, play plane 0, background +11 to +17 |
| Root Pack, sep 0.034 | 408 of 418 | foreground -13, play plane 0, background +4 to +20 |
| Overworld 3 | all 0 | all 0 (no parallax layers) |

Signs are right: foreground in front, background behind. The few dy of ±1 to ±4 are patches matched on film grain.
Same frame by construction (both eyes rendered in one end-of-frame).

## Build 2: OpenXR stage (live on RigB, 2026-11-07)

Native DLL ported from OriVRNative to 64-bit (Khronos loader 1.1.65 x64). New: a black 16x16 projection layer behind
the stage (no runtime home around it), and the reference-space-change event passed on as a recenter. C# rig: per eye,
every parallax transform moves by eye offset x speed (head tracked, so leaning gives motion parallax too), then the whole
camera stack renders into the eye texture. Skies without a parallax speed (MinMax ParallaxLayer at 0, Background
sprites with sorting order -500 or lower) get Sky distance (default 0.85).

| Check | Result | Evidence |
|---|---|---|
| OpenXR on SteamVR 2.18.4 null driver | session state 5, 144.0 fps at 3872x2178 per eye, 1 skipped frame at start | LogOutput XRSTAT lines |
| Eye check (both eyes at one spot, 4 frames) | 0 of 8,433,216 pixels differ | EYECHECK line |
| Stereo, Root Pack, tracked eyes | dy 0 on 287 of 311 patches; foreground -13, background +18, sky +37 px at 1291 wide (expected sky 0.85 x 44 = 37) | evidence\CupheadVR\2026-11-07\xr |
| Menu | opens, rows change values, game font CupheadMemphis-Medium, drawn in front of the stage | xr\scene_level_veggies_f6241_L_view.png |

Found live: the game sets `targetFrameRate` 60 and vsync again on every level load. The rig lifts both each frame while VR runs.

Game speed: the player moves in `FixedUpdate` by `CupheadTime.FixedDelta` (LevelPlayerMotor.cs, static), so with the
game's own 1/60 s step its speed does not depend on the frame rate. Motion updates 60 times a second while the headset
shows 72 to 144, which may look juddery: not verified. The `match 1` lab command sets one fixed step per headset frame
(Ori's approach), but measuring run speed and jump height failed this session: the lab's virtual pad joined as Player 2
and then disconnected, and keyboard arrows did not move the player. So build 2 ships with the game's 1/60 step.

## Build 3: smooth motion at headset rate (live on RigB, 2026-11-07)

Stepping the game at the headset rate (Ori's way) is not safe here. Static read of the decompile: 126 scripts have
FixedUpdate, and a crude scan finds 44 whose FixedUpdate body never names a delta. Some of those really count steps,
for example `CharmFloatWingsFX` (`position += vel` each step) and `FlyingBirdLevelNursePill` (`velocity.y -= 10` each
step). At 120 steps a second those would run twice as fast.

So the game keeps its own 1/60 s step (game speed unchanged by construction, `fixedDelta 0.01667` in PROBE), and the rig
interpolates for drawing: after each fixed step it records every renderer's transform and its parents. At each rendered
frame, anything still exactly where the last step left it is drawn between its last two positions,
`alpha = (Time.time - Time.fixedTime) / fixedDeltaTime` (one step behind, like Unity's rigidbody interpolation), then
put back. Things moved in Update or LateUpdate (camera, parallax, animation frames) are left alone. A move of more than
400 units in one step counts as a jump and is not interpolated.

| Root Pack, potato bullet, 144 Hz null driver | Frames with no motion | Step per frame |
|---|---|---|
| Smooth off | 34 of 59, 41 of 71 | mean 4.9, sd 5.8 |
| Smooth on | 0 of 59, 0 of 71 | mean 4.86 to 4.88, sd 0.30 to 0.41 |

4.86 per frame x 144 = 700 units/s, the same as 11.67 per step x 60 with smoothing off, so speed is unchanged.
Restore check: 0 errors over about 8,000 eye pairs (depth layers back where they were after each render). Eye check
0 of 8,433,216. 72, 90 and 120 Hz not run (the null driver runs 144 only).
Menu row added: Smooth motion On/Off (default On, config VR.SmoothMotion).
