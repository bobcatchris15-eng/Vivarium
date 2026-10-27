"""Measure the visually confirmed fixed-camera purple fleck, not whole-scene material colors."""
import argparse
import json
from collections import defaultdict
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('run', type=Path)
args = parser.parse_args()
roi = (610, 260, 660, 310)
groups = defaultdict(list)
for path in sorted((args.run / 'captures').glob('log_coverage_far_reverse_*.png')):
    if '_dof_' not in path.stem and '_log_finite_' not in path.stem:
        continue
    mode = path.stem.removeprefix('log_coverage_far_reverse_').rsplit('_', 1)[0]
    im = Image.open(path).convert('RGB')
    a = np.asarray(im.crop(roi)).astype(float)
    r, g, b = a.transpose(2, 0, 1)
    mask = (r > g*1.25+12) & (b > g*1.25+12) & (r > 18) & (b > 18)
    groups[mode].append(dict(image=path.relative_to(args.run).as_posix(), purple_pixels=int(mask.sum())))
rows, tiles = [], []
for mode, frames in groups.items():
    moving = 'motion' in mode
    rows.append(dict(mode=mode, frames=frames, affected_frames=sum(x['purple_pixels']>0 for x in frames),
        maximum_purple_pixels=max(x['purple_pixels'] for x in frames), comparable_roi=not moving))
    chosen = max(frames, key=lambda x:x['purple_pixels'])
    im = Image.open(args.run / chosen['image']).convert('RGB')
    tile = Image.new('RGB', (250, 235), '#182028')
    crop = ImageEnhance.Brightness(im.crop((597, 245, 677, 325))).enhance(2)
    tile.paste(crop.resize((160,160), Image.Resampling.NEAREST), (45,55))
    ImageDraw.Draw(tile).text((5,5), mode+'\n'+str(sum(x['purple_pixels']>0 for x in frames))+'/'+str(len(frames))+' frames; '+str(chosen['purple_pixels'])+' purple pixels', fill='white')
    tiles.append(tile)
result = dict(scope='Confirmed purple block ROI in fixed far-reverse camera only. Counts are observed samples, not statistical guarantees. Moving-camera ROIs are not comparable. Composite crops use 2x display brightness and nearest-neighbor magnification for inspection; originals preserved.', roi=roi, rows=rows)
(args.run / 'fleck-control-summary.json').write_text(json.dumps(result,indent=2), encoding='utf-8')
if tiles:
    sheet = Image.new('RGB', (1000,235*((len(tiles)+3)//4)), '#182028')
    for i,tile in enumerate(tiles):
        sheet.paste(tile,((i%4)*250,(i//4)*235))
    sheet.save(args.run / 'fleck-control-crops.png')
print(json.dumps([dict(mode=x['mode'],affected=x['affected_frames'],total=len(x['frames']),max=x['maximum_purple_pixels']) for x in rows]))
