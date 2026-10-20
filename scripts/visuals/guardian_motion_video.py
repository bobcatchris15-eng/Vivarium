"""Encode sampled native guardian frames with their recorded frame-delta timing.

This is review evidence, not an FPS benchmark or an interpolated animation.
Usage: python guardian_motion_video.py <native-run> <archetype>
"""
import html
import json
import re
import subprocess
import sys
from pathlib import Path


def main():
    run = Path(sys.argv[1]).resolve()
    species = sys.argv[2]
    directory = run / species
    rows = json.loads((directory / 'populated-motion.json').read_text())
    by_frame = {r['frame']: r for r in rows}
    images = sorted(directory.glob('populated-*.png'), key=lambda p: int(re.search(r'-(\d+)\.png$', p.name)[1]))
    if len(images) < 2:
        raise ValueError('Not enough native frames')
    frames = [int(re.search(r'-(\d+)\.png$', p.name)[1]) for p in images]
    times = [by_frame[f]['presentationSeconds'] for f in frames]
    if any(b <= a for a, b in zip(times, times[1:])):
        raise ValueError('Recorded presentation time is not strictly monotonic')
    lines = ['ffconcat version 1.0']
    for i, path in enumerate(images):
        # ffconcat is a file format, not shell input; quote its path syntax.
        escaped = path.as_posix().replace("'", "'\\''")
        lines.append(f"file '{escaped}'")
        duration = times[i + 1] - times[i] if i + 1 < len(times) else .1
        lines.append(f'duration {duration:.9f}')
    lines.append(lines[-2])
    concat = run / 'native-motion.ffconcat'
    concat.write_text('\n'.join(lines), encoding='utf-8')
    video = run / 'guardian-motion.mp4'
    subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-f', 'concat',
                    '-safe', '0', '-i', str(concat), '-fps_mode', 'vfr', '-c:v', 'libx264',
                    '-preset', 'fast', '-crf', '20', '-pix_fmt', 'yuv420p',
                    '-movflags', '+faststart', str(video)], check=True)
    transitions = []
    previous = None
    for row in rows:
        if row['activity'] != previous:
            transitions.append({'time': row['presentationSeconds'] - times[0],
                                'frame': row['frame'], 'activity': row['activity'],
                                'energy': row['energy']})
            previous = row['activity']
    buttons = []
    seen = set()
    for item in transitions:
        if item['activity'] in seen:
            continue
        seen.add(item['activity'])
        start = max(0, item['time'])
        buttons.append(f'<button onclick="review.currentTime={start:.3f};review.play()">'
                       f'{html.escape(item["activity"])} at {start:.1f}s</button>')
    report = {'native_frames': len(rows), 'encoded_native_images': len(images),
              'presentation_seconds': times[-1] - times[0], 'transitions': transitions,
              'energy_initial': rows[0]['energy'], 'energy_final': rows[-1]['energy'],
              'limitation': 'Images sampled every three rendered frames; recorded native delta timing retained. '
                            'Focused fixture hides camera-obstructing flora. No performance or full ecosystem acceptance.'}
    (run / 'motion-summary.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    page = '<!doctype html><meta charset="utf-8"><title>Guardian native motion review</title>' \
           '<style>body{background:#101410;color:#e5eadd;font:16px system-ui;max-width:1000px;margin:24px auto}' \
           'video{width:100%;background:#000}button{padding:12px;margin:5px;background:#304632;color:white;border:0}' \
           'p{line-height:1.5}</style><h1>Guardian native motion review</h1>' \
           '<p>Focused feeding and activity review. Obstructing rendered flora is hidden for inspection; ' \
           'ecological organisms remain. This is sampled native motion, not an FPS benchmark.</p>' \
           '<video id="review" controls preload="metadata" src="guardian-motion.mp4"></video><div>' \
           + ''.join(buttons) + '</div><p>' + html.escape(json.dumps(report)) + '</p>'
    (run / 'motion-review.html').write_text(page, encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
