"""Soft-knee saturation limit for authored species pigment colours.

Rule (HSV, sRGB-authored values): saturation below KNEE is unchanged; above it the
excess is scaled by SLOPE. Hue and value are preserved. Applies to
game/content/flora/*.json visual.color/color2 and Srgb(r,g,b) literals in the plant
catalog and species generators. Bioluminescence colours are not touched.

Usage: python tone_down_saturation.py <repo_root> [--apply]
"""
import colorsys, glob, json, re, sys
from pathlib import Path

KNEE, SLOPE = 0.60, 0.5


def soften(rgb):
    h, s, v = colorsys.rgb_to_hsv(*rgb)
    if s <= KNEE:
        return None
    s2 = KNEE + (s - KNEE) * SLOPE
    return [round(x, 3) for x in colorsys.hsv_to_rgb(h, s2, v)]


def main():
    root, apply = Path(sys.argv[1]), '--apply' in sys.argv
    changed = 0
    for f in sorted(glob.glob(str(root / 'game/content/flora/*.json'))):
        path = Path(f)
        raw = path.read_bytes()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        data = json.loads(text)
        vis = data.get('visual', {})
        new_text = text
        for key in ('color', 'color2'):
            c = vis.get(key)
            if not c or len(c) < 3:
                continue
            s = soften(c[:3])
            if s is None:
                continue
            # Replace the array literal for this key in place, keeping file formatting.
            m = re.search(r'("%s"\s*:\s*\[)([^\]]*)(\])' % key, new_text)
            if not m:
                continue
            indent = re.search(r'\n(\s*)[-0-9.]', m.group(2))
            sep = ',\n' + indent.group(1) if indent else ', '
            lead = m.group(2)[:len(m.group(2)) - len(m.group(2).lstrip())]
            tail = m.group(2)[len(m.group(2).rstrip()):]
            body = lead + sep.join(repr(x) for x in s) + tail
            new_text = new_text[:m.start(2)] + body + new_text[m.end(2):]
            print(f'{data["id"]:22s} {key:7s} {c[:3]} -> {s}')
            changed += 1
        if apply and new_text != text:
            path.write_bytes((b'\xef\xbb\xbf' if bom else b'') + new_text.encode('utf-8'))
    pat = re.compile(r'Srgb\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*([0-9.]+)\s*\)')
    files = glob.glob(str(root / 'src/Vivarium.Sim/Repr/Plant/Catalog/*.cs')) + glob.glob(str(root / 'src/Vivarium.Sim/Repr/Plant/Species/*.cs'))
    for f in sorted(files):
        path = Path(f)
        text = path.read_text(encoding='utf-8')

        def sub(m):
            nonlocal changed
            rgb = [float(x) for x in m.groups()]
            s = soften(rgb)
            if s is None:
                return m.group(0)
            changed += 1
            print(f'{path.name:45s} {rgb} -> {s}')
            return 'Srgb(%s)' % ','.join(('%.3f' % x).rstrip('0').rstrip('.') if x else '0' for x in s)

        new = pat.sub(sub, text)
        if apply and new != text:
            path.write_text(new, encoding='utf-8')
    print(changed, 'colours', 'changed' if apply else 'would change')


if __name__ == '__main__':
    main()
