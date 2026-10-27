"""Locate screenshot-like compact purple candidates; candidates require visual confirmation."""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import label, find_objects

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('run', type=Path)
args = parser.parse_args()
rows = []
tiles = []
for path in sorted((args.run / 'captures').rglob('*.png')):
    im = Image.open(path).convert('RGB')
    rgb = np.asarray(im).astype(np.float32)
    r, g, b = rgb.transpose(2, 0, 1)
    mask = (r > g * 1.25 + 12) & (b > g * 1.25 + 12) & (r > 18) & (b > 18) & (r < b * 1.7) & (b < r * 1.7)
    labels, count = label(mask)
    sizes = np.bincount(labels.ravel())
    for i, bounds in enumerate(find_objects(labels), 1):
        if bounds is None:
            continue
        y, x = bounds
        w, h = x.stop - x.start, y.stop - y.start
        fill = sizes[i] / (w * h)
        if not (3 <= w <= 28 and 3 <= h <= 28 and sizes[i] >= 8 and fill >= .45):
            continue
        row = dict(image=path.relative_to(args.run).as_posix(), xyxy=[x.start, y.start, x.stop, y.stop], pixels=int(sizes[i]), fill=float(fill))
        rows.append(row)
        if len(tiles) < 160:
            cx, cy = (x.start + x.stop)//2, (y.start + y.stop)//2
            tile = Image.new('RGB', (180, 135), '#182028')
            tile.paste(im.crop((cx-24, cy-24, cx+24, cy+24)).resize((96,96), Image.Resampling.NEAREST), (42,0))
            ImageDraw.Draw(tile).text((2,98), path.stem[-29:] + '\n' + str(row['xyxy']), fill='white')
            tiles.append(tile)
(args.run / 'purple-candidates.json').write_text(json.dumps(dict(scope='Color/shape candidates only; authored purple plants can match. Visual confirmation and temporal/context comparison required.', candidates=rows), indent=2), encoding='utf-8')
if tiles:
    sheet = Image.new('RGB', (180*8, 135*((len(tiles)+7)//8)), '#182028')
    for i, tile in enumerate(tiles):
        sheet.paste(tile, ((i%8)*180, (i//8)*135))
    sheet.save(args.run / 'purple-candidates.png')
print(json.dumps(dict(images=len(list((args.run/'captures').rglob('*.png'))), candidates=len(rows), first=rows[:12])))
