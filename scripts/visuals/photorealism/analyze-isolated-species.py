"""Rank temporal pixel outliers in isolated native fixtures; this is triage, not visual acceptance."""
import argparse
import json
from pathlib import Path
import numpy as np
from PIL import Image


def components(mask):
    from scipy.ndimage import label, find_objects
    labels, count = label(mask, structure=np.ones((3, 3)))
    sizes = np.bincount(labels.ravel())
    found = []
    for i, bounds in enumerate(find_objects(labels), 1):
        if bounds is None or sizes[i] > 64:
            continue
        y, x = bounds
        if x.stop-x.start <= 16 and y.stop-y.start <= 16:
            found.append(dict(pixels=int(sizes[i]), xyxy=[x.start,y.start,x.stop,y.stop]))
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    args = parser.parse_args()
    rows = []
    def analyze(specimen):
        info = json.loads(specimen.read_text())
        row = dict(species=info['species'], path=info['geometry']['path'], views={})
        for view in ('whole', 'macro'):
            results = {}
            for aa, count in [('baseline', 6), ('msaa_off', 3)]:
                images = np.stack([np.asarray(Image.open(specimen.parent / f'{view}_{aa}_{i:02}.png').convert('RGB')) for i in range(count)]).astype(np.int16)
                median = np.median(images, axis=0)
                delta = np.max(np.abs(images - median), axis=3)
                peaks = delta.max(axis=0)
                groups = components(peaks > 64)
                results[aa] = dict(frames=count, mean_absolute_display_delta=float(np.abs(images-median).mean()),
                    pixels_over_64=int((peaks > 64).sum()), small_temporal_components=len(groups),
                    candidates=sorted(groups, key=lambda g:g['pixels'], reverse=True)[:20])
            row['views'][view] = results
        row['triage_score'] = sum(v['baseline']['small_temporal_components'] for v in row['views'].values())
        return row
    from concurrent.futures import ThreadPoolExecutor
    with ThreadPoolExecutor(max_workers=4) as pool:
        rows = list(pool.map(analyze, sorted((args.run / 'captures').glob('*/specimen.json'))))
    rows.sort(key=lambda r:r['triage_score'], reverse=True)
    result = dict(scope='Temporal outlier triage only. Silhouette sampling, TAA convergence and legitimate shading variation can trigger candidates. Low score does not prove absence. Six baseline versus three control frames is not a statistical A/B.', species_count=len(rows), rows=rows)
    (args.run / 'temporal-analysis.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps([dict(species=r['species'], score=r['triage_score']) for r in rows[:16]]))


if __name__ == '__main__':
    main()
