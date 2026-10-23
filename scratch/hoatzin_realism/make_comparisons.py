"""Side-by-Side Comparison Composer for the Crested Canopy Hoatzin (Baseline vs. Redesign)."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

RENDER_DIR = Path(r"E:\Vivarium\scratch\hoatzin_realism\renders")
COMP_DIR = RENDER_DIR / "comparisons"
COMP_DIR.mkdir(parents=True, exist_ok=True)

VIEWS = [
    ("perspective", "1. Full Body 3/4 Profile — Crested Canopy Hoatzin (Opisthocomus hoazin)"),
    ("top", "2. Dorsal & Lateral Mantle — Iridescent Bronze-Olive Coverts, Rufous Primaries & Tail Fan"),
    ("head", "3. Cranial Portrait — Sagittal Punk Crest, Corrugated Cobalt Facial Skin, Ruby Eye & Raptorial Bill"),
    ("wing", "4. Folded Wing Architecture — Layered Coverts, Buff Terminal Bars & Glowing Chestnut Remiges"),
    ("tail_feet", "5. Perching Anatomy & Rectrices — Reticulate Tarsal Scutes, Curved Talons & Banded Tail Fan"),
]


def load_font(size):
    for f in ["C:/Windows/Fonts/segoeuib.ttf", "C:/Windows/Fonts/arialbd.ttf", "C:/Windows/Fonts/consolab.ttf"]:
        if Path(f).exists():
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def build_comparison(view_key, title):
    b_path = RENDER_DIR / f"baseline_{view_key}.png"
    r_path = RENDER_DIR / f"realistic_{view_key}.png"
    if not b_path.exists() or not r_path.exists():
        print(f"Skipping {view_key} (missing {b_path} or {r_path})")
        return

    b_img = Image.open(b_path).convert("RGB")
    r_img = Image.open(r_path).convert("RGB")

    w, h = b_img.size
    header_h = 84
    gap = 12
    canvas = Image.new("RGB", (w * 2 + gap, h + header_h), (18, 20, 24))
    draw = ImageDraw.Draw(canvas)

    font_title = load_font(24)
    font_sub = load_font(20)

    draw.text((24, 14), title, fill=(238, 242, 248), font=font_title)
    draw.text((24, 48), "BASELINE (Original Procedural Mesh & Double-Gamma Textures)", fill=(235, 115, 105), font=font_sub)
    draw.text((w + gap + 24, 48), "REDESIGN (Anatomical Plumage, 2048 PBR, Sculpted Bill/Orbital/Talons)", fill=(110, 230, 155), font=font_sub)

    canvas.paste(b_img, (0, header_h))
    canvas.paste(r_img, (w + gap, header_h))

    out_path = COMP_DIR / f"hoatzin_comparison_{view_key}.png"
    canvas.save(out_path, format="PNG")
    print(f"Saved {out_path}")


def main():
    for view_key, title in VIEWS:
        build_comparison(view_key, title)
    print("All Hoatzin comparison panels generated!")


if __name__ == "__main__":
    main()
