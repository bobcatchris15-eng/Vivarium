"""Generates high-definition PBR textures for the Brazilian Prehensile-Tailed Porcupine (quilled_arboreal)."""
import math
from pathlib import Path
from PIL import Image

TAU = math.tau

def srgb_encode(v):
    v = max(0.0, min(1.0, v))
    return int((12.92 * v if v <= 0.0031308 else 1.055 * (v ** (1.0 / 2.4)) - 0.055) * 255.0)

def generate_textures(texdir: Path):
    texdir.mkdir(parents=True, exist_ok=True)
    size = 512

    # 1. quill.png (Main dorsal & tail quills: true Brazilian porcupine bi/tri-banded pattern)
    img_quill = Image.new('RGBA', (size, size))
    pixels_quill = []
    for y in range(size):
        t = y / (size - 1)  # 0 at root/base, 1 at sharp tip
        for x in range(size):
            u = x / (size - 1)  # circumference
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            
            # Natural organic wobble on band borders
            band = t + 0.016 * math.sin(u * TAU * 2.0) + 0.008 * math.sin(u * 17.0) + 0.003 * (noise - 0.5)
            # Longitudinal keratin striation
            striation = 0.94 + 0.06 * math.sin(u * TAU * 16.0) + 0.02 * (noise - 0.5)

            # Banding schedule based on reference photos:
            # 0.00 - 0.12: Dark root (embedded in skin/underfur)
            # 0.12 - 0.44: Bright pale cream / ivory shaft
            # 0.44 - 0.74: Bold black melanin band
            # 0.74 - 0.84: Pale ivory needle section
            # 0.84 - 1.00: Bold dark needle point (16% of quill length)
            if band < 0.10:
                c = (0.08, 0.06, 0.05)
            elif band < 0.14:
                f = (band - 0.10) / 0.04
                c = (0.08 + f * (0.94 - 0.08), 0.06 + f * (0.91 - 0.06), 0.05 + f * (0.80 - 0.05))
            elif band < 0.42:
                c = (0.94 * striation, 0.91 * striation, 0.80 * striation)
            elif band < 0.46:
                f = (band - 0.42) / 0.04
                c = (0.94 - f * (0.94 - 0.035), 0.91 - f * (0.91 - 0.03), 0.80 - f * (0.80 - 0.025))
            elif band < 0.72:
                c = (0.035, 0.03, 0.025)
            elif band < 0.76:
                f = (band - 0.72) / 0.04
                c = (0.035 + f * (0.94 - 0.035), 0.03 + f * (0.91 - 0.03), 0.025 + f * (0.80 - 0.025))
            elif band < 0.84:
                c = (0.94 * striation, 0.91 * striation, 0.80 * striation)
            elif band < 0.88:
                f = (band - 0.84) / 0.04
                c = (0.94 - f * (0.94 - 0.035), 0.91 - f * (0.91 - 0.03), 0.80 - f * (0.80 - 0.025))
            else:
                c = (0.035, 0.03, 0.025)

            shade = 0.88 + 0.12 * math.sin(u * math.pi)
            rgb = [min(1.0, max(0.0, v * shade)) for v in c]
            pixels_quill.append((srgb_encode(rgb[0]), srgb_encode(rgb[1]), srgb_encode(rgb[2]), 255))
    img_quill.putdata(pixels_quill)
    img_quill.save(texdir / 'quill.png')

    # 2. quill_crown.png (Crown & Forehead Spines: high-contrast ivory with dark black tip)
    img_crown = Image.new('RGBA', (size, size))
    pixels_crown = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            band = t + 0.015 * math.sin(u * TAU * 2.0) + 0.006 * math.sin(u * 15.0)
            striation = 0.95 + 0.05 * math.sin(u * TAU * 16.0)

            # Crown spines have dark base, brilliant ivory body, and bold black tip (26% length)
            if band < 0.10:
                c = (0.08, 0.06, 0.05)
            elif band < 0.14:
                f = (band - 0.10) / 0.04
                c = (0.08 + f * (0.96 - 0.08), 0.06 + f * (0.93 - 0.06), 0.05 + f * (0.83 - 0.05))
            elif band < 0.70:
                c = (0.96 * striation, 0.93 * striation, 0.83 * striation)
            elif band < 0.76:
                f = (band - 0.70) / 0.06
                c = (0.96 - f * (0.96 - 0.03), 0.93 - f * (0.93 - 0.025), 0.83 - f * (0.83 - 0.02))
            else:
                c = (0.03, 0.025, 0.02)

            shade = 0.88 + 0.12 * math.sin(u * math.pi)
            rgb = [min(1.0, max(0.0, v * shade)) for v in c]
            pixels_crown.append((srgb_encode(rgb[0]), srgb_encode(rgb[1]), srgb_encode(rgb[2]), 255))
    img_crown.putdata(pixels_crown)
    img_crown.save(texdir / 'quill_crown.png')

    # 3. quill_flank.png (Flank quills)
    img_flank = Image.new('RGBA', (size, size))
    pixels_flank = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            band = t + 0.018 * math.sin(u * TAU * 2.0)
            striation = 0.94 + 0.06 * math.sin(u * TAU * 14.0)

            if band < 0.10:
                c = (0.08, 0.06, 0.05)
            elif band < 0.15:
                f = (band - 0.10) / 0.05
                c = (0.08 + f * (0.92 - 0.08), 0.06 + f * (0.89 - 0.06), 0.05 + f * (0.78 - 0.05))
            elif band < 0.40:
                c = (0.92 * striation, 0.89 * striation, 0.78 * striation)
            elif band < 0.45:
                f = (band - 0.40) / 0.05
                c = (0.92 - f * (0.92 - 0.035), 0.89 - f * (0.89 - 0.03), 0.78 - f * (0.78 - 0.025))
            elif band < 0.72:
                c = (0.035, 0.03, 0.025)
            elif band < 0.76:
                f = (band - 0.72) / 0.04
                c = (0.035 + f * (0.92 - 0.035), 0.03 + f * (0.89 - 0.03), 0.025 + f * (0.78 - 0.025))
            elif band < 0.84:
                c = (0.92 * striation, 0.89 * striation, 0.78 * striation)
            elif band < 0.88:
                f = (band - 0.84) / 0.04
                c = (0.92 - f * (0.92 - 0.035), 0.89 - f * (0.89 - 0.03), 0.78 - f * (0.78 - 0.025))
            else:
                c = (0.035, 0.03, 0.025)

            shade = 0.88 + 0.12 * math.sin(u * math.pi)
            rgb = [min(1.0, max(0.0, v * shade)) for v in c]
            pixels_flank.append((srgb_encode(rgb[0]), srgb_encode(rgb[1]), srgb_encode(rgb[2]), 255))
    img_flank.putdata(pixels_flank)
    img_flank.save(texdir / 'quill_flank.png')

    # 4. underfur.png (Deep charcoal espresso undercoat)
    img_fur = Image.new('RGBA', (size, size))
    pixels_fur = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            strand = math.sin(u * 220 + t * 40 + 0.8 * math.sin(t * 18 + u * 12))
            tone = 0.90 + 0.04 * strand + 0.05 * math.sin(u * 12 + t * 8) + 0.04 * (noise - 0.5)
            # Base color: deep charcoal/dark brown
            c = (0.058 * tone, 0.044 * tone, 0.036 * tone)
            pixels_fur.append((srgb_encode(c[0]), srgb_encode(c[1]), srgb_encode(c[2]), 255))
    img_fur.putdata(pixels_fur)
    img_fur.save(texdir / 'underfur.png')

    # 5. nose_skin.png (Fleshy rich dusty rose / warm mauve bulbous rhinarium)
    img_nose = Image.new('RGBA', (size, size))
    pixels_nose = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            pore = 0.93 + 0.07 * math.sin(u * 160 + t * 80) * math.sin(u * 85 - t * 140) + 0.03 * (noise - 0.5)
            mottling = 0.96 + 0.04 * math.sin(u * 14 + t * 21) + 0.03 * math.sin(u * 38 - t * 47)
            # Brazilian porcupine nose: rich fleshy dusty rose / warm mauve
            r = 0.58 * mottling * pore
            g = 0.38 * mottling * pore
            b = 0.40 * mottling * pore
            if t < 0.20:
                shade = 0.82 + 0.18 * (t / 0.20)
                r *= shade; g *= shade; b *= shade
            pixels_nose.append((srgb_encode(r), srgb_encode(g), srgb_encode(b), 255))
    img_nose.putdata(pixels_nose)
    img_nose.save(texdir / 'nose_skin.png')

    # 6. nose_normal.png (Soft pebble-grain leathery skin pore normal map)
    img_nose_norm = Image.new('RGBA', (size, size))
    pixels_nose_norm = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            a = 0.070 * math.sin(u * 150 + t * 75) + 0.040 * math.sin(u * 280 - t * 180) + 0.025 * (noise - 0.5)
            b = 0.070 * math.cos(u * 95 - t * 135) + 0.040 * math.cos(u * 220 + t * 250) + 0.025 * (noise - 0.5)
            nz = math.sqrt(max(0.001, 1.0 - (a * 2.0)**2 - (b * 2.0)**2))
            rgb = (int((0.5 + a) * 255), int((0.5 + b) * 255), int((0.5 + nz * 0.5) * 255), 255)
            pixels_nose_norm.append(rgb)
    img_nose_norm.putdata(pixels_nose_norm)
    img_nose_norm.save(texdir / 'nose_normal.png')

    # 7. surface-normal.png (Fine longitudinal striations and fur micro-grain)
    img_surf_norm = Image.new('RGBA', (size, size))
    pixels_surf_norm = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            a = 0.04 * math.sin(u * TAU * 16.0) + 0.015 * (noise - 0.5)
            b = 0.015 * math.sin(u * 80 - t * 45) + 0.01 * (noise - 0.5)
            nz = math.sqrt(max(0.001, 1.0 - (a * 2.0)**2 - (b * 2.0)**2))
            rgb = (int((0.5 + a) * 255), int((0.5 + b) * 255), int((0.5 + nz * 0.5) * 255), 255)
            pixels_surf_norm.append(rgb)
    img_surf_norm.putdata(pixels_surf_norm)
    img_surf_norm.save(texdir / 'surface-normal.png')

    # 8. paw_skin.png (Dark charcoal / slate-grey leathery climbing skin for paws and tactile tail)
    img_paw = Image.new('RGBA', (size, size))
    pixels_paw = []
    for y in range(size):
        t = y / (size - 1)
        for x in range(size):
            u = x / (size - 1)
            noise = (math.sin(x * 12.9898 + y * 78.233) * 43758.5453) % 1.0
            grain = 0.94 + 0.06 * math.sin(u * 120 + t * 65) + 0.04 * (noise - 0.5)
            c = (0.12 * grain, 0.11 * grain, 0.10 * grain)
            pixels_paw.append((srgb_encode(c[0]), srgb_encode(c[1]), srgb_encode(c[2]), 255))
    img_paw.putdata(pixels_paw)
    img_paw.save(texdir / 'paw_skin.png')

    print(f"All 8 porcupine textures successfully authored in {texdir}!")

if __name__ == '__main__':
    root = Path(__file__).resolve().parents[2]
    generate_textures(root / 'game/assets/guardians/quilled_arboreal/textures')
