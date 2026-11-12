CupheadVR
VR mod for Cuphead (Steam).
Native stereo, played with a gamepad, as a pop-up storybook stage floating in front of you.

You need
- The game on Steam.
- A PC VR headset with an OpenXR runtime: SteamVR set as your OpenXR runtime in SteamVR settings, or Virtual Desktop's VDXR. The installer warns you if none is set.
- A gamepad.

Install
1. Unzip this folder anywhere.
2. Double click "Install CupheadVR.bat" and type 1.

Play
1. Start SteamVR or connect Virtual Desktop.
2. Start Cuphead from Steam. VR starts by itself. The game may freeze for a few seconds while it connects.
3. A stage floats in front of you. Play as usual.

Settings
Hold both sticks down for half a second to open the menu, or press Insert (or F1) on a keyboard. The game pauses while it is open.
LB and RB (or LT and RT) change tab. Up and down choose a setting. Left and right change it. A selects. B closes.
On a keyboard: arrow keys move and change, Enter selects, Esc or Backspace closes, Page Up and Page Down (or Q and E) change tab.
A PlayStation 5 DualSense works too, over USB or Bluetooth, with the same buttons in the PlayStation places (Cross selects, Circle closes).
Each setting has a short explanation at the bottom of the menu.

Stage tab
- Recenter: puts the stage straight ahead of where you face. SteamVR's own recenter does the same.
- Stage distance and Stage size: where the stage stands and how big it is.
- Surround: what you see around the stage. Black is normal. Green is pure green for Virtual Desktop's chroma-key passthrough, so your room shows around the stage. Transparent asks the headset software for see-through, where it offers that.
- Reset the stage: back to 2.15 m wide at 1.25 m.

Depth tab
- Layer depth: how far apart the background layers spread. Higher never goes past the horizon.
- Character pop: lifts Cuphead, enemies, bullets and effects off the ground, like cut-outs.
- Pop-out: brings the whole scene toward you.
- Lean parallax: how much leaning your head slides the layers against each other, like a real diorama. 0% (the default) keeps Cuphead lined up with his ground wherever your head is.
- Edge match: trims each eye's outer edge so both eyes see the same picture at the stage edges. Leave it on.
- Sky distance: how far back painted skies sit.

Picture tab
- Picture: how the stage reaches the headset. Auto uses Steady on Virtual Desktop and Panel on everything else. Steady holds still when you turn your head on Virtual Desktop. Panel can look a little sharper.
- Film grain and Colour fringe: the game's old film look. 0% is clean, 100% is how the game looks flat.
- Sharpen: crisper line art. Too high makes edges glow.
- Resolution: 100% matches your headset. Applies when the menu closes.
- Smooth motion: moving things are drawn smoothly at your headset's refresh rate. The game itself keeps its normal speed.

Options tab
- Click sounds.
- DualSense: reads a PS5 DualSense directly and gives it to the game as an Xbox pad. It steps aside when an Xbox pad is connected. Turn it off if Steam Input or DS4Windows already handles your DualSense.
- Reset everything, Close.

Uninstall
Double click "Install CupheadVR.bat" and type 2. No game files are changed by this mod.

If something goes wrong
- The game stays flat: a "VR DID NOT START" message at the top of the screen says why. Press Insert to see it again in the mod menu. Check your OpenXR runtime is set (SteamVR or VDXR). Then look in BepInEx\LogOutput.log in the game folder. Lines starting "OpenXR layer" list the OpenXR add-ons the game sees.
- Windows warns about the bat file: this mod is not signed, so Windows may ask you to confirm. Read the bat file first if you like. It is plain text.
- Smart App Control: if it is on, Windows can block unsigned mod DLLs and the game runs flat. Windows does not make this easy to turn back on, so decide for yourself. There is no workaround in the mod.
- When SteamVR starts, its dashboard may appear in front of the stage. Close it as usual.
- Far backgrounds strain your eyes: lower Layer depth, Sky distance or Pop-out.

Tested on
Pimax Dream Air through SteamVR, and Meta Quest 3 through Virtual Desktop's VDXR, both with an RTX 5090. Others should work through OpenXR but have not been tried. AMD and Intel graphics have not been tried.

Known limits
- Steam version only.
- No motion controls. A gamepad is required.
- No DLSS or other upscaler. The game is 2D and does not need one.
- The overworld map is flat on the stage; its art has no depth layers.
- Local co-op has not been tested.

Notes
- The game keeps its own 60 updates a second, so its speed is exactly the same as flat. Smooth motion draws the moving things in between.
- Settings are saved in BepInEx\config\com.scubasteve2365.cupheadvr.cfg in the game folder.

Version 1.05. Credits and licences are in THIRD-PARTY-NOTICES.txt. CupheadVR is MIT licensed, by scubasteve2365.
This is a fan mod, not made or approved by Studio MDHR.
