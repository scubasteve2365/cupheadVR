"""Game speed test in VR on RigB (reference v1.9, section 2a). py -3.12 speedtest.py [rates...]  (default 72 90 120 144)

For each rate: checks RigB is free for CupheadVR (stops the whole run if not), sets the fake headset's rate with
nullhz.py (which closes SteamVR first), launches the game fresh (SteamVR starts fresh with it), enters the lab slot,
waits for a focused session (state 5), then logs game speed over 10 s windows in four conditions:
game VSync setting on (its default, as saved) and off (memory only), each with Picture Panel and Steady.
Always puts the fake headset back to 144 Hz at the end. Results: GAMESPEED lines in the mod log, copied to evidence.
"""
import os
import re
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = ["py", "-3.12", "-W", "ignore", r"C:\VRLab\tools\lab.py", "CupheadVR"]
GAME = r"E:\SteamLibrary\steamapps\common\Cuphead"
LOG = os.path.join(GAME, r"BepInEx\LogOutput.log")
VRCMD = r"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\vrcmd.exe"
EVID = r"C:\VRLab\evidence\CupheadVR\2026-11-10\speed"


def lab(*a, timeout=200):
    return subprocess.run(LAB + list(a), capture_output=True, text=True, timeout=timeout).stdout.strip()


def free():
    return "free for CupheadVR" in lab("rigb")


def log_text():
    with open(LOG, encoding="utf-8", errors="replace") as f:
        return f.read()


def wait_for(pattern, seconds):
    end = time.time() + seconds
    while time.time() < end:
        if re.search(pattern, log_text()):
            return True
        time.sleep(1)
    return False


def run_rate(hz):
    if not free():
        print("RigB not free, stopping:", lab("rigb"))
        return False
    print(subprocess.run(["py", "-3.12", os.path.join(HERE, "nullhz.py"), str(hz)], capture_output=True, text=True).stdout.strip())
    cmd = os.path.join(GAME, "vrlab_command.txt")
    if os.path.exists(cmd):
        os.remove(cmd)
    r = subprocess.run(["py", "-3.12", os.path.join(HERE, "..", "to_game.py"), "2", "150"], capture_output=True, text=True, timeout=300)
    print(r.stdout.strip()[-300:])
    if r.returncode != 0 or "refused" in r.stdout.lower() or "busy" in r.stdout.lower():
        print("launch did not reach the overworld, stopping")
        return False
    for _ in range(6):
        subprocess.run([VRCMD, "--hidedashboard"], capture_output=True)
        time.sleep(5)
        states = re.findall(r"XRSTAT state (\d)", log_text())
        if states and states[-1] == "5":
            break
    states = re.findall(r"XRSTAT state (\d)", log_text())
    print("session state", states[-1] if states else "none")
    for vsync in ("1", "0"):
        for picture in ("panel", "steady"):
            lab("cmd", "gamevsync %s;picture %s;speedlog 10" % (vsync, picture))
            time.sleep(1)
            lab("cmd", "probe")
            lab("cmd", "speedlog 10")  # restart the window after the switch has settled
            time.sleep(23)
            lab("cmd", "speedlog 0")
            time.sleep(1)
    os.makedirs(EVID, exist_ok=True)
    shutil.copy(LOG, os.path.join(EVID, "speed_%dhz_LogOutput.log" % hz))
    print(lab("stop"))
    return True


def main():
    rates = [int(a) for a in sys.argv[1:]] or [72, 90, 120, 144]
    try:
        for hz in rates:
            print("==== %d Hz" % hz)
            if not run_rate(hz):
                break
    finally:
        if free():
            print(lab("release"))
            print(subprocess.run(["py", "-3.12", os.path.join(HERE, "nullhz.py"), "144"], capture_output=True, text=True).stdout.strip())
        else:
            print("RigB not free at the end: the fake headset was not reset here, check its rate")


if __name__ == "__main__":
    main()
