"""Check a left/right pair from the mod's stereo shot, and make viewing images.

    py -3.12 stereo_check.py <prefix>   (prefix of <prefix>_L.png and <prefix>_R.png)

Block matching on 32x32 patches with texture: for each, the shift (dx, dy) of the right image that best matches the
left. Stereo is right when dy is 0 everywhere and dx varies by layer (positive dx = behind the play plane).
Writes <prefix>_SBS.png (left | right, for parallel viewing), <prefix>_X.png (right | left, cross-eyed) and
<prefix>_anaglyph.png (red left, cyan right).
"""
import sys
from collections import Counter
import numpy as np
from PIL import Image

p = sys.argv[1]
L = np.asarray(Image.open(p + "_L.png").convert("RGB"))
R = np.asarray(Image.open(p + "_R.png").convert("RGB"))
# Work at about 1280 wide; disparities are reported in pixels of that size (scale printed).
scale = max(1, round(L.shape[1] / 1280))
L = L[::scale, ::scale]
R = R[::scale, ::scale]
gl, gr = L.mean(2), R.mean(2)
h, w = gl.shape
B, S, V = 32, 64, 4
dxs, dys = [], []
for y in range(V + B, h - B - V, B):
    for x in range(S, w - B - S, B):
        a = gl[y:y + B, x:x + B]
        if a.std() < 6:
            continue
        best = None
        for dy in range(-V, V + 1):
            for dx in range(-S, S + 1):
                d = np.abs(gr[y + dy:y + dy + B, x + dx:x + dx + B] - a).mean()
                if best is None or d < best[0]:
                    best = (d, dx, dy)
        if best[0] < 4:
            dxs.append(best[1]); dys.append(best[2])
print(f"{p} (1/{scale} size): identical pixels {np.mean((L == R).all(2)) * 100:.1f}%, matched patches {len(dxs)}")
print("  dy counts:", sorted(Counter(dys).items()))
print("  dx counts:", sorted(Counter(dxs).items()))
Image.fromarray(np.concatenate([L, R], 1)).save(p + "_SBS.png")
Image.fromarray(np.concatenate([R, L], 1)).save(p + "_X.png")
ana = np.stack([L.mean(2), R.mean(2), R.mean(2)], 2).astype(np.uint8)
Image.fromarray(ana).save(p + "_anaglyph.png")
