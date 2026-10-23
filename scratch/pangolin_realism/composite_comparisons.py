import os
from PIL import Image, ImageDraw, ImageFont

RENDERS_DIR = r"E:\Vivarium\scratch\pangolin_realism\renders"
COMP_DIR = r"E:\Vivarium\scratch\pangolin_realism\renders\comparisons"
os.makedirs(COMP_DIR, exist_ok=True)

views = [
    ("perspective", "Three-Quarter Carapace & Stance View"),
    ("top", "Dorsal Imbricated Armor Lattice View"),
    ("head", "Cranial Visor, Rhinarium & Hooded Eye Anatomy"),
    ("claw", "Fossorial Bracer Armor & Sickle Digging Talons"),
    ("tail", "Serrated Caudal Mace-Blade & Scale Shingling"),
]

try:
    font_title = ImageFont.truetype("arial.ttf", 36)
    font_sub = ImageFont.truetype("arial.ttf", 25)
    font_label = ImageFont.truetype("arialbd.ttf", 28)
except Exception:
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

    draw.text((border, 20), f"Vivarium Armored Forest Pangolin Guardian — {view_title}", fill=(240, 242, 245), font=font_title)
    draw.text((border, 65), "Side-by-side comparison under identical 65mm focal length and 3-point AgX studio lighting", fill=(160, 168, 178), font=font_sub)

    x_base = border
    y_img = header_height
    x_real = border * 2 + w

    comp.paste(img_base, (x_base, y_img))
    comp.paste(img_real, (x_real, y_img))

    # Baseline Banner
    draw.rectangle([x_base + 15, y_img + 15, x_base + 380, y_img + 65], fill=(180, 40, 40))
    draw.text((x_base + 25, y_img + 22), "BASELINE (Existing Asset)", fill=(255, 255, 255), font=font_label)

    # Redesign Banner
    draw.rectangle([x_real + 15, y_img + 15, x_real + 430, y_img + 65], fill=(20, 140, 90))
    draw.text((x_real + 25, y_img + 22), "REDESIGN (Armored Guardian)", fill=(255, 255, 255), font=font_label)

    # Footer metrics
    draw.text((x_base + 10, total_h - 32), "Baseline: 98,925 Verts | 195,156 Tris | Washed-Out Cardboard Rings & Inverted Claws", fill=(160, 165, 175), font=font_sub)
    draw.text((x_real + 10, total_h - 32), "Redesign: Phyllotactic Keeled Armor, Cranial Visor, Fossorial Sickle Talons & True PBR", fill=(100, 210, 150), font=font_sub)

    out_file = os.path.join(COMP_DIR, f"comparison_{view_id}.png")
    comp.save(out_file)
    print("Saved composite comparison:", out_file)

print("ALL PANGOLIN COMPOSITES CREATED!")
