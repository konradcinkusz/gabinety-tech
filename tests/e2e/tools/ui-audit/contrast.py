#!/usr/bin/env python3
"""WCAG contrast of the token pairs of src/Gabinety.Web/wwwroot/tokens.css (docs/ui/tokens.css is its copy), light and dark. Prints a markdown table; exits 1 when a pair is below its minimum.
Usage: contrast.py [tokens.css]"""
import re, sys
path = sys.argv[1] if len(sys.argv) > 1 else 'src/Gabinety.Web/wwwroot/tokens.css'
css = open(path).read()
dark_start = css.index('prefers-color-scheme: dark')
def block(txt):
    return dict(re.findall(r'--color-([a-z-]+):\s*(#[0-9a-fA-F]{6})', txt))
light = block(css[:dark_start]); dark = {**light, **block(css[dark_start:])}
def lum(h):
    c = [int(h[i:i+2], 16)/255 for i in (1, 3, 5)]
    c = [x/12.92 if x <= 0.03928 else ((x+0.055)/1.055)**2.4 for x in c]
    return 0.2126*c[0] + 0.7152*c[1] + 0.0722*c[2]
def ratio(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la+0.05)/(lb+0.05)
# (foreground, background, minimum, where it is used)
PAIRS = [
 ('text', 'bg', 4.5, 'body text on page'), ('text', 'surface-raised', 4.5, 'body text on card'), ('text', 'surface-sunken', 4.5, 'text on table head'),
 ('text-secondary', 'bg', 4.5, 'secondary text on page'), ('text-secondary', 'surface-raised', 4.5, 'secondary text on card'), ('text-secondary', 'surface-sunken', 4.5, 'secondary on sunken'),
 ('text-muted', 'bg', 4.5, 'hints, captions on page'), ('text-muted', 'surface-raised', 4.5, 'hints on card'), ('text-muted', 'surface-sunken', 4.5, 'muted on sunken'),
 ('accent', 'bg', 4.5, 'link on page'), ('accent', 'surface-raised', 4.5, 'link on card'), ('accent-hover', 'surface-raised', 4.5, 'link hover'),
 ('accent-contrast', 'accent', 4.5, 'primary button label'), ('accent-contrast', 'accent-hover', 4.5, 'primary button hover'),
 ('accent-soft-text', 'accent-soft', 4.5, 'selected chip, info callout'), ('accent', 'accent-soft', 4.5, 'link inside info callout'),
 ('success', 'success-soft', 4.5, 'success badge, callout'), ('success', 'surface-raised', 4.5, 'success text on card'),
 ('warning', 'warning-soft', 4.5, 'warning badge, callout'), ('warning', 'surface-raised', 4.5, 'warning text on card'),
 ('danger', 'danger-soft', 4.5, 'danger badge, callout'), ('danger', 'surface-raised', 4.5, 'error text under a field'),
 ('danger-contrast', 'danger', 4.5, 'danger button label'),
 ('border-strong', 'surface-raised', 3.0, 'control outline (UI component)'), ('border-strong', 'bg', 3.0, 'control outline on page'),
 ('focus', 'bg', 3.0, 'focus ring on page'), ('focus', 'surface-raised', 3.0, 'focus ring on card'), ('focus', 'accent-soft', 3.0, 'focus ring on a selected chip'),
]
bad = 0
print('| Foreground on background | Use | Min | Light | Dark |\n|---|---|---|---|---|')
for fg, bg, mn, use in PAIRS:
    out = []
    for theme in (light, dark):
        r = ratio(theme[fg], theme[bg]); ok = r >= mn; bad += not ok
        out.append(f"{r:.2f}{'' if ok else ' FAIL'}")
    print(f"| `{fg}` on `{bg}` | {use} | {mn} | {out[0]} | {out[1]} |")
print(f"\n{len(PAIRS)} pairs, {bad} below minimum", file=sys.stderr)
sys.exit(1 if bad else 0)
