"""Compose a six-species iteration sheet from one native plant_catalog capture.

Usage: python compose_iteration_sheet.py <capture_dir> <out.jpg> <title> id1 id2 ... id6
Crops match compose_post_review.py so sheets are directly comparable.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

FONT = Path('C:/Windows/Fonts/segoeui.ttf')


def main():
    capture, out, title, ids = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], sys.argv[4:]
    folder = capture / 'plant_catalog'
    label = ImageFont.truetype(str(FONT), 27)
    header = ImageFont.truetype(str(FONT), 31)
    rows = (len(ids) + 1) // 2
    sheet = Image.new('RGB', (1800, 70 + rows * 600), '#202829')
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 14), title, font=header, fill='#f0eee4')
    for i, ident in enumerate(ids):
        x, y = (i % 2) * 900, 70 + (i // 2) * 600
        with Image.open(folder / f'{ident}_growth.png') as im:
            sheet.paste(im.crop((200, 80, 1400, 800)).resize((900, 540), Image.Resampling.LANCZOS), (x, y + 60))
        draw.text((x + 18, y + 12), ident, font=label, fill='#f0eee4')
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=92, subsampling=0)
    print(out)


if __name__ == '__main__':
    main()
