import os
from PIL import Image, ImageDraw, ImageFont

RENDERS_DIR = r"E:\Vivarium\scratch\monitor_realism\renders"
COMP_DIR = r"E:\Vivarium\scratch\monitor_realism\renders\comparisons"
os.makedirs(COMP_DIR, exist_ok=True)

views = [
    ("perspective", "Three-Quarter Perspective View"),
    ("top", "Dorsal Anatomical Top View"),
    ("head", "Cranial & Facial Anatomy"),
    ("claw", "Paw & Recurved Claw Articulation"),
    ("tail", "Caudal Swimming Keel & Muscle Base")
]

# Try to find a standard font
try:
    font_title = ImageFont.truetype("arial.ttf", 36)
    font_sub = ImageFont.truetype("arial.ttf", 26)
    font_label = ImageFont.truetype("arialbd.ttf", 28)
except:
    font_title = ImageFont.load_default()
    font_sub = font_title
    font_label = font_title

header_height = 100
footer_height = 40
border = 10

for view_id, view_title in views:
    base_path = os.path.join(RENDERS_DIR, f"baseline_{view_id}.png")
    real_path = os.path.join(RENDERS_DIR, f"realistic_{view_id}.png")
    if not (os.path.exists(base_path) and os.path.exists(real_path)):
        print(f"Skipping {view_id}, file not found")
        continue

    img_base = Image.open(base_path)
    img_real = Image.open(real_path)

    w, h = img_base.size
    total_w = w * 2 + border * 3
    total_h = h + header_height + footer_height

    comp = Image.new("RGB", (total_w, total_h), color=(22, 24, 28))
    draw = ImageDraw.Draw(comp)

    # Title header
    draw.text((border, 20), f"Vivarium Great Ground Monitor Guardian — {view_title}", fill=(240, 242, 245), font=font_title)
    draw.text((border, 65), "Side-by-side comparison under identical 65mm focal length and 3-point studio lighting", fill=(160, 168, 178), font=font_sub)

    # Paste images
    x_base = border
    y_img = header_height
    x_real = border * 2 + w

    comp.paste(img_base, (x_base, y_img))
    comp.paste(img_real, (x_real, y_img))

    # Overlaid Banner Labels on top-left of each view
    # Baseline Banner (Red/Amber tint)
    draw.rectangle([x_base + 15, y_img + 15, x_base + 380, y_img + 65], fill=(180, 40, 40))
    draw.text((x_base + 25, y_img + 22), "BASELINE (Existing Asset)", fill=(255, 255, 255), font=font_label)

    # Realistic Banner (Teal/Emerald tint)
    draw.rectangle([x_real + 15, y_img + 15, x_real + 410, y_img + 65], fill=(20, 140, 90))
    draw.text((x_real + 25, y_img + 22), "REDESIGN (Lifelike Realistic)", fill=(255, 255, 255), font=font_label)

    # Footer metrics
    draw.text((x_base + 10, total_h - 32), "Baseline: 192,924 Vertices | 270,596 Tris | 14,000 Floating Scute Prisms", fill=(160, 165, 175), font=font_sub)
    draw.text((x_real + 10, total_h - 32), "Realistic: 84,886 Vertices (-56%) | 148,462 Tris | Shield Imbricate PBR", fill=(100, 210, 150), font=font_sub)

    out_file = os.path.join(COMP_DIR, f"comparison_{view_id}.png")
    comp.save(out_file)
    print("Saved composite comparison:", out_file)

print("ALL COMPOSITES CREATED!")
