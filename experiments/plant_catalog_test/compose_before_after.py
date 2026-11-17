"""Compose before/after rows of native plant_catalog shots.

Usage: python compose_before_after.py <out.jpg> <title> <after_dir> <id>=<before_png> ...
Crops match compose_post_review.py.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FONT = Path('C:/Windows/Fonts/segoeui.ttf')


def main():
    out, title, after = Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3]) / 'plant_catalog'
    pairs = [a.split('=', 1) for a in sys.argv[4:]]
    label = ImageFont.truetype(str(FONT), 24)
    header = ImageFont.truetype(str(FONT), 31)
    rowh = 330
    sheet = Image.new('RGB', (1800, 70 + rowh * len(pairs)), '#202829')
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 14), title, font=header, fill='#f0eee4')
    for i, (ident, before) in enumerate(pairs):
        y = 70 + i * rowh
        for x, path, tag in ((0, Path(before), 'before'), (900, after / f'{ident}_growth.png', 'after')):
            with Image.open(path) as im:
                sheet.paste(im.crop((200, 80, 1400, 800)).resize((480, 288), Image.Resampling.LANCZOS), (x + 400, y + 36))
            draw.text((x + 18, y + 40), f'{ident}\n{tag}', font=label, fill='#f0eee4')
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=90, subsampling=0)
    print(out)


if __name__ == '__main__':
    main()
