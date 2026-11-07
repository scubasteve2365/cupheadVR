# CupheadVR

VR mod for **Cuphead** (Steam): the game on a pop-up storybook stage floating in front of you, in native stereo,
played seated with a gamepad.

- **Native stereo** through OpenXR: both eyes are drawn from the same game tick, with no alternate-eye rendering and no warping.
- **Real depth from the game's own parallax layers.** Backgrounds sit far behind, Cuphead and the bosses stand out like
  cut-outs, and the foreground leans toward you.
- **Smooth motion at your headset's refresh rate**, at exactly the normal game speed.
- **In-headset menu** styled like the game's cards. Hold both sticks for half a second to open it.
- **One bat installer** that finds the game through Steam, and uninstalls the same way. No game files are changed.

## You need

- Cuphead on Steam
- A PC VR headset with OpenXR: SteamVR set as the OpenXR runtime, or Virtual Desktop's VDXR
- A gamepad

## Install

1. Download `CupheadVR-1.0.zip` from Releases and unzip it anywhere.
2. Double click `Install CupheadVR.bat` and type 1.
3. Start SteamVR or connect Virtual Desktop, then start Cuphead from Steam. VR starts by itself.

The full guide, settings and troubleshooting are in [mod/install/README.txt](mod/install/README.txt).

## How it works

Cuphead draws flat sprites with orthographic cameras, and moves each background and foreground layer at its own
parallax speed. A layer with speed s behaves like a layer at depth d/(1-s) behind a plane at distance d, so for each
eye the mod moves every layer by the eye's offset times s and renders the game's whole camera stack, then puts the
layers back. Things the game moves in its 60 Hz steps are drawn between steps, so motion is smooth at any refresh
rate without changing the game's speed. Details: [RE_NOTES.md](RE_NOTES.md).

## Credits

By scubasteve2365. MIT licence. Uses BepInEx (LGPL-2.1) and the Khronos OpenXR loader (Apache-2.0). The OpenXR side
comes from [OriVR](https://github.com/scubasteve2365/OriVR). See
[THIRD-PARTY-NOTICES.txt](mod/install/THIRD-PARTY-NOTICES.txt).

This is a fan mod, not made or approved by Studio MDHR. It contains no game files.
