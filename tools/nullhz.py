"""Copied from SF6VR's tools/nullhz.py (same lab, 2026-11-10). Set RigB's fake headset (SteamVR null driver) rate for the game speed test. py -3.12 nullhz.py <hz>
Only while RigB is free for CupheadVR (reference section 11): checks lab.py rigb first and stops if it is not.
Closes SteamVR through lab.py steamvr_off (which also checks the claim), edits displayFrequency and
secondsFromVsyncToPhotons in steamvr.vrsettings in place, keeping the file's own formatting. Put it back with 144."""
import re
import subprocess
import sys
import time

LAB = ["py", "-3.12", r"C:\VRLab\tools\lab.py", "CupheadVR"]
SETTINGS = r"C:\Program Files (x86)\Steam\config\steamvr.vrsettings"
VR = ("vrserver.exe", "vrcompositor.exe", "vrmonitor.exe")

hz = int(sys.argv[1])
out = subprocess.run(LAB + ["rigb"], capture_output=True, text=True).stdout
if "free for CupheadVR" not in out:
    sys.exit("RigB is not free for CupheadVR, not touching SteamVR: " + out.strip())
print(subprocess.run(LAB + ["steamvr_off"], capture_output=True, text=True).stdout.strip())
tl = subprocess.run(["tasklist"], capture_output=True, text=True).stdout.lower()
if any(e in tl for e in VR):
    sys.exit("SteamVR still running, settings not changed")
time.sleep(2)
s = open(SETTINGS, encoding="utf-8").read()
blk = re.search(r'"driver_null"\s*:\s*\{[^}]*\}', s)
if not blk:
    sys.exit("no driver_null section")
b = blk.group(0)
b2 = re.sub(r'("displayFrequency"\s*:\s*)[0-9.]+', r"\g<1>%d" % hz, b)
b2 = re.sub(r'("secondsFromVsyncToPhotons"\s*:\s*)[0-9.eE+-]+', r"\g<1>%.17g" % (1.0 / hz), b2)
s = s.replace(b, b2)
open(SETTINGS, "w", encoding="utf-8", newline="").write(s)
print("null driver set to %d Hz" % hz)
