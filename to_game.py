"""Get Cuphead from launch to the overworld of the lab slot, using the mod's log to know the state.

    py -3.12 C:\\VRLab\\games\\CupheadVR\\to_game.py [slot] [timeout_s]

Starts the game if needed. Presses A on the title, waits for the slot screen (the mod logs the scene), then enters the
slot through the mod's "slot" command (the game's own EnterGame steps). Saves are never written while Lab.BlockSaves is on.
Steve's saves were backed up to C:\\VRLab\\backup\\CupheadVR\\saves_2026-11-07 before the first run.
Exit code 0 means a scene_map_world scene loaded.
"""
import re
import subprocess
import sys
import time

LAB = r"C:\VRLab\tools\lab.py"
GAME_LOG = r"E:\SteamLibrary\steamapps\common\Cuphead\BepInEx\LogOutput.log"


def lab(*args):
    return subprocess.run(["py", "-3.12", "-W", "ignore", LAB, "CupheadVR", *args], capture_output=True, text=True)


def scenes():
    with open(GAME_LOG, encoding="utf-8", errors="replace") as f:
        return re.findall(r"SCENE loaded (\S+)", f.read())


def main():
    slot = sys.argv[1] if len(sys.argv) > 1 else "2"
    timeout = float(sys.argv[2]) if len(sys.argv) > 2 else 90.0
    if lab("running").stdout.strip() != "yes":
        print(lab("launch").stdout.strip())
        if lab("wait", "SCENE loaded scene_title", "60").returncode:
            sys.exit("the title scene never loaded")
        time.sleep(15)  # the title animation has to finish before A is taken
    start = time.time()
    while time.time() - start < timeout:
        s = scenes()
        if s and s[-1] == "scene_slot_select":
            break
        lab("pad", "a:150 wait:2500")
    else:
        sys.exit("no slot screen, last scene " + (scenes() or ["none"])[-1])
    time.sleep(2)
    lab("key", "enter")  # clears the "controller disconnected" box the virtual pad leaves behind
    lab("cmd", "slot " + slot)
    while time.time() - start < timeout:
        s = scenes()
        if s and s[-1].startswith("scene_map_world"):
            print(f"overworld {s[-1]} after {time.time() - start:.0f} s")
            return
        time.sleep(1)
    print(lab("shot", "to_game_failed").stdout.strip())
    sys.exit("not on the overworld, last scene " + (scenes() or ["none"])[-1])


if __name__ == "__main__":
    main()
