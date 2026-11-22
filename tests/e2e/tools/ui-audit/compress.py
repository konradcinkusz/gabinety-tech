#!/usr/bin/env python3
"""Shrinks every PNG under docs/ui/audit below ~150 KB: palette quantisation (32 colours, no dithering: UI is flat colour), then, if still too big,
a width reduction. Usage: python3 -I compress.py <dir>   (needs Pillow)"""
import os, sys
from PIL import Image
LIMIT = 150 * 1024
root = sys.argv[1]
n = 0; worst = 0
for d, _, files in os.walk(root):
    for f in files:
        if not f.endswith('.png'): continue
        p = os.path.join(d, f); im = Image.open(p).convert('RGB')
        for colors, scale in ((64, 1.0), (32, 1.0), (32, 0.85), (24, 0.7)):
            t = im if scale == 1.0 else im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)
            q = t.quantize(colors=colors, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
            q.save(p, optimize=True)
            if os.path.getsize(p) <= LIMIT: break
        n += 1; worst = max(worst, os.path.getsize(p))
        if os.path.getsize(p) > LIMIT: print('still large:', p, os.path.getsize(p) // 1024, 'KB')
print(f'{n} images, largest {worst // 1024} KB')
