"""Builds the install folder and zip in ..\\..\\build\\package from a finished build. Run with Python 3.
Ported from OriVR's make_package.py.

    make_package.py <BepInEx x64 folder> <openxr loader licence> <BepInEx licence text>

The zip stays local. It is not to be published until Steve says ship.
"""
import os
import shutil
import sys
import zipfile

VERSION = "1.04"
HERE = os.path.dirname(os.path.abspath(__file__))
BUILD = os.path.normpath(os.path.join(HERE, "..", "..", "build"))
PKG = os.path.join(BUILD, "package", "CupheadVR")
bep, xr_licence, bep_licence = sys.argv[1], sys.argv[2], sys.argv[3]

if os.path.isdir(PKG):
    shutil.rmtree(PKG)
files = os.path.join(PKG, "files")
os.makedirs(files)
for name in ("winhttp.dll", "doorstop_config.ini", ".doorstop_version"):
    shutil.copy2(os.path.join(bep, name), files)
# BepInEx core only: BepInEx creates config, cache and the rest on first start.
shutil.copytree(os.path.join(bep, "BepInEx", "core"), os.path.join(files, "BepInEx", "core"),
                ignore=shutil.ignore_patterns("*.xml"))

plugin = os.path.join(files, "BepInEx", "plugins", "CupheadVR")
os.makedirs(plugin)
for name in ("CupheadVR.dll", "CupheadVRNative.dll", "openxr_loader.dll"):
    shutil.copy2(os.path.join(BUILD, name), plugin)

for name in ("Install CupheadVR.bat", "README.txt", "THIRD-PARTY-NOTICES.txt"):
    with open(os.path.join(HERE, name), "rb") as src:
        text = src.read().replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")  # Windows line endings, whatever git did
    with open(os.path.join(PKG, name), "wb") as dst:
        dst.write(text)
licences = os.path.join(PKG, "licences")
os.makedirs(licences)
shutil.copy2(xr_licence, os.path.join(licences, "OpenXR-loader-LICENSE.txt"))
shutil.copy2(bep_licence, os.path.join(licences, "BepInEx-LICENSE.txt"))
shutil.copy2(r"C:\VRLabin\minhook\LICENSE.txt", os.path.join(licences, "MinHook-LICENSE.txt"))
shutil.copy2(os.path.join(HERE, "..", "..", "LICENSE"), os.path.join(licences, "CupheadVR-LICENSE.txt"))

zip_path = os.path.join(BUILD, "package", "CupheadVR-" + VERSION + ".zip")
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    for root, _, names in os.walk(PKG):
        for n in names:
            full = os.path.join(root, n)
            z.write(full, os.path.join("CupheadVR", os.path.relpath(full, PKG)))
print(zip_path, os.path.getsize(zip_path) // 1024, "KB")
