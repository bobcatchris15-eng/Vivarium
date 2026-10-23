"""High-fidelity 2048x2048 PBR texture generation for Hoatzin (Opisthocomus hoazin / crested_canopy).

Fixes the double-sRGB gamma washout and normal-map gamma skew in Blender by:
- Converting sRGB color arrays to linear before writing into Blender's sRGB float buffer (or saving directly via PIL).
- Setting 'Non-Color' colorspace on normal map images in Blender so neutral (0.5, 0.5, 1.0) is never gamma-warped.

Generates 2048x2048 ornithological textures:
- orbital-skin.png / orbital-normal.png: Vivid cobalt-cerulean to electric turquoise corrugated facial skin with raised dermal papillae and dark palpebral rim.
- feather-2.png: Sagittal punk-crest lanceolate plume — blazing golden-rufous/amber vane with dark rachis and deep espresso-black distal tip.
- streaked-contour.png: Nape, mantle & scapular contour feather — lustrous dark bronzy-olive sepia with bold creamy-ivory median shaft streak.
- feather-1.png / breast-underplumage.png: Warm ochraceous-buff throat & foreneck bib with fine herringbone barbules and basal self-shadowing.
- underplumage.png / underplumage-normal.png: Deep, fiery saturated rufous-chestnut plumage for lower crop, breast, belly, flanks, and crural thighs.
- feather-0.png / surface-normal.png: 2-Column Wing & Mantle Atlas:
    * Column 0 (u in [0.0, 0.5]): Iridescent dark bronzy-olive wing coverts & tertials with crisp creamy-buff terminal wing-bar crescent.
    * Column 1 (u in [0.5, 1.0]): Glowing deep rufous-chestnut primary & secondary remiges with dark bronzy-sepia distal tips.
- feather-3.png: Graduated tail rectrix — deep bronzy-sepia with broad, crisp creamy-buff terminal band across the distal 20%.
- horn-bill.png: UV-zoned rhamphotheca & podotheca — upper half (v < 0.5) dark olive-slate bill keratin with tomium/culmen wear; lower half (v >= 0.5) scutellate/reticulate avian tarsometatarsus & digital scales.
- red-iris.png: 1024x1024 carbuncle ruby-crimson iris with radial stroma fibers, collarette ring, dark limbal rim, and jet-black pupil.
"""
import math
from pathlib import Path
import numpy as np

TAU = math.tau

try:
    import bpy
    HAS_BPY = True
except ImportError:
    HAS_BPY = False

try:
    from PIL import Image
    HAS_PIL = True
except ImportError:
    HAS_PIL = False


def srgb_to_linear(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * (c ** (1.0 / 2.4)) - 0.055)


def save_image(arr, path, is_color=True):
    """Save float32 [0, 1] array to PNG without double-sRGB gamma washout or normal skew."""
    arr = np.clip(arr, 0.0, 1.0).astype(np.float32)
    h, w, c = arr.shape
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    if HAS_PIL:
        u8 = (arr * 255.0 + 0.5).astype(np.uint8)
        mode = 'RGBA' if c == 4 else 'RGB'
        img = Image.fromarray(u8, mode=mode)
        img.save(str(path), format='PNG')
        return
    if c == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), dtype=np.float32)], axis=-1)
    if HAS_BPY:
        name = Path(path).stem
        im = bpy.data.images.new(name, width=w, height=h, alpha=True)
        arr_flipped = np.flipud(arr)
        if is_color:
            im.colorspace_settings.name = 'sRGB'
            arr_flipped = arr_flipped.copy()
            arr_flipped[:, :, :3] = srgb_to_linear(arr_flipped[:, :, :3])
        else:
            im.colorspace_settings.name = 'Non-Color'
        im.pixels.foreach_set(arr_flipped.ravel())
        im.filepath_raw = str(Path(path).resolve())
        im.file_format = 'PNG'
        im.save()
        bpy.data.images.remove(im)


def normal_from_height(height, strength=1.0):
    h = height.astype(np.float32)
    dy, dx = np.gradient(h)
    nx = -dx * strength * 32.0
    ny = -dy * strength * 32.0
    nz = np.ones_like(nx)
    norm = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx /= norm
    ny /= norm
    nz /= norm
    rgb = np.stack([nx * 0.5 + 0.5, ny * 0.5 + 0.5, nz * 0.5 + 0.5], axis=-1)
    return rgb


def generate_orbital_skin(size=2048):
    """Anatomical bare cobalt-turquoise facial skin covering lores, supraorbital brow, and cheek."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    dx = x - 0.50
    dy = y - 0.50
    dist = np.sqrt(dx * dx * 1.0 + dy * dy * 1.28)
    angle = np.arctan2(dy, dx)

    # Concentric palpebral folds around orbit + radiating canthal wrinkles + cellular dermal papillae
    concentric = np.sin(dist * TAU * 18.0 + 0.35 * np.sin(angle * 7.0)) * np.exp(-dist * 3.2) * 0.5 + 0.5
    radial_wrinkles = np.sin(angle * 36.0 + dist * 32.0) * np.exp(-dist * 2.5) * 0.5 + 0.5
    papillae_coarse = (np.sin(x * TAU * 64.0 + 0.8 * np.sin(y * TAU * 19.0)) * 0.5 + 0.5) * (
        np.sin(y * TAU * 68.0 + 0.8 * np.cos(x * TAU * 21.0)) * 0.5 + 0.5
    )
    papillae_fine = np.sin(x * TAU * 155.0 + y * TAU * 140.0) * 0.5 + 0.5
    dermal = papillae_coarse * 0.65 + papillae_fine * 0.35

    # True sRGB colors matching wild Opisthocomus hoazin:
    # Electric cerulean/turquoise orbital highlight: #1cb8e6
    # Vivid cobalt-azure mid-cheek: #1078c4
    # Deep royal cobalt-indigo outer lore/temple: #0b3d78
    # Dark charcoal palpebral ocular rim: #0a1017
    c_bright = np.array([0.110, 0.722, 0.902], dtype=np.float32)
    c_mid = np.array([0.063, 0.471, 0.769], dtype=np.float32)
    c_deep = np.array([0.043, 0.239, 0.471], dtype=np.float32)
    c_ring = np.array([0.039, 0.063, 0.090], dtype=np.float32)

    inner_ring = np.clip(1.0 - np.abs(dist - 0.19) / 0.15, 0.0, 1.0) ** 1.5
    outer_falloff = np.clip((dist - 0.24) / 0.28, 0.0, 1.0) ** 1.2
    crease = np.clip((0.145 - dist) / 0.045, 0.0, 1.0)

    base_color = c_mid[None, None, :] * (1.0 - outer_falloff[:, :, None]) + c_deep[None, None, :] * outer_falloff[:, :, None]
    base_color = base_color * (1.0 - (inner_ring * 0.68)[:, :, None]) + c_bright[None, None, :] * (inner_ring * 0.68)[:, :, None]
    base_color = base_color * (1.0 - (crease * 0.85)[:, :, None]) + c_ring[None, None, :] * (crease * 0.85)[:, :, None]

    # Subtle dermal tuberculation and fold shading
    relief_mod = 0.88 + 0.14 * dermal - 0.06 * radial_wrinkles - 0.05 * concentric
    base_color *= relief_mod[:, :, None]

    # Stippled dark follicular papillae on posterior temple and lower malar border
    papilla_mask = np.clip((dist - 0.22) / 0.24, 0.0, 1.0)
    papilla_dots = ((np.sin(x * TAU * 44.0 + 0.5 * np.sin(y * TAU * 13.0)) * 0.5 + 0.5) ** 10) * (
        (np.sin(y * TAU * 46.0 + 0.5 * np.cos(x * TAU * 15.0)) * 0.5 + 0.5) ** 10
    )
    papilla_intensity = np.clip(papilla_dots * papilla_mask * 1.35, 0.0, 1.0)
    rgb = base_color * (1.0 - papilla_intensity[:, :, None]) + c_ring[None, None, :] * papilla_intensity[:, :, None]

    height = concentric * 0.0038 + radial_wrinkles * 0.0028 + dermal * 0.0032 + papilla_intensity * 0.0040 - crease * 0.0035
    normal = normal_from_height(height, strength=1.8)
    return rgb, normal


def generate_rufous_crest(size=2048):
    """Lanceolate sagittal crest plume with blazing golden-rufous vane, dark rachis, and espresso-black tip."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y  # t=1 at tip (row 0), t=0 at base (row size-1)
    u_c = np.abs(x - 0.50)

    rachis_w = 0.015 * (0.35 + 0.65 * (1.0 - t))
    rachis = np.exp(-((u_c / rachis_w) ** 2))

    barb_coord = t - u_c * 0.42 + 0.006 * np.sin(t * 48.0)
    barbs = np.sin(barb_coord * TAU * 88.0) * 0.5 + 0.5
    barb_fine = np.sin(barb_coord * TAU * 220.0) * 0.5 + 0.5
    barb_tex = 0.84 + 0.12 * barbs + 0.04 * barb_fine

    # Rich golden-rufous / amber-ochre vane with deep espresso-black distal chevron tip:
    c_base = np.array([0.680, 0.285, 0.045], dtype=np.float32)
    c_vane = np.array([0.875, 0.485, 0.085], dtype=np.float32)
    c_gold = np.array([0.975, 0.655, 0.165], dtype=np.float32)
    c_dark = np.array([0.038, 0.024, 0.016], dtype=np.float32)
    c_rachis = np.array([0.095, 0.048, 0.022], dtype=np.float32)

    # Chevron-shaped dark tip starting earlier along outer margins and sweeping across distal 34%
    chevron_t = t + u_c * 0.24 + 0.025 * (barbs - 0.5)
    tip_dark = np.clip((chevron_t - 0.60) / 0.15, 0.0, 1.0) ** 1.25
    basal_shade = np.clip((0.22 - t) / 0.22, 0.0, 1.0)

    mid_boost = np.exp(-(((t - 0.42) / 0.22) ** 2)) * (1.0 - np.clip(u_c / 0.38, 0.0, 1.0) * 0.5)
    vane_col = c_vane[None, None, :] * (1.0 - mid_boost[:, :, None]) + c_gold[None, None, :] * mid_boost[:, :, None]
    vane_col = vane_col * (1.0 - basal_shade[:, :, None] * 0.45) + c_base[None, None, :] * (basal_shade[:, :, None] * 0.45)
    vane_col *= barb_tex[:, :, None]

    rgb = vane_col * (1.0 - tip_dark[:, :, None]) + c_dark[None, None, :] * tip_dark[:, :, None]
    rachis_col = c_rachis[None, None, :] * (1.0 - tip_dark[:, :, None]) + c_dark[None, None, :] * tip_dark[:, :, None]
    rgb = rgb * (1.0 - rachis[:, :, None]) + rachis_col * rachis[:, :, None]

    h = rachis * 0.0095 + barbs * 0.0022 + barb_fine * 0.0008
    normal = normal_from_height(h, strength=1.6)
    return rgb, normal


def generate_streaked_neck(size=2048):
    """Nape, dorsal neck, mantle & scapular contour feather: dark bronzy-olive sepia with bold cream median streak."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y
    u_c = np.abs(x - 0.50)

    # Bold lanceolate cream-buff central shaft stripe (13% width in mid-feather, tapering sharply to tip)
    stripe_w = 0.115 * (np.sin(np.clip(t, 0.03, 0.96) * math.pi) ** 0.72)
    barb_coord = t - u_c * 0.34 + 0.004 * np.sin(t * 42.0)
    barbs = np.sin(barb_coord * TAU * 86.0) * 0.5 + 0.5
    barb_fine = np.sin(barb_coord * TAU * 210.0) * 0.5 + 0.5

    # Feather the edge of the central stripe along individual barbs
    stripe_u = u_c + 0.012 * (barbs - 0.5)
    stripe_mask = np.exp(-((stripe_u / np.maximum(stripe_w, 0.002)) ** 5)) * np.clip(t / 0.12, 0.0, 1.0) * np.clip((0.97 - t) / 0.06, 0.0, 1.0)

    # Colors:
    # Deep bronzy-olive sepia vane: #17130d to iridescent olive-bronze highlight #2b2418
    # Crisp creamy-buff central streak: #f5ebd1
    # Pale bronze-buff outer fringe so overlapping feathers separate cleanly: #6e5a3e
    c_sepia = np.array([0.065, 0.055, 0.036], dtype=np.float32)
    c_bronze = np.array([0.145, 0.122, 0.075], dtype=np.float32)
    c_streak = np.array([0.965, 0.925, 0.815], dtype=np.float32)
    c_fringe = np.array([0.320, 0.260, 0.175], dtype=np.float32)
    c_shaft = np.array([0.780, 0.710, 0.580], dtype=np.float32)

    sheen = np.exp(-(((t - 0.62) / 0.25) ** 2)) * (0.6 + 0.4 * barbs)
    dark_vane = c_sepia[None, None, :] * (1.0 - sheen[:, :, None] * 0.65) + c_bronze[None, None, :] * (sheen[:, :, None] * 0.65)
    dark_vane *= (0.86 + 0.10 * barbs + 0.04 * barb_fine)[:, :, None]

    # Subtle pale bronze rim along distal outer margin (t > 0.78 or u_c > 0.38)
    rim_mask = np.clip((t - 0.84) / 0.14, 0.0, 1.0) * np.clip(1.0 - u_c * 1.8, 0.0, 1.0) * 0.45
    dark_vane = dark_vane * (1.0 - rim_mask[:, :, None]) + c_fringe[None, None, :] * rim_mask[:, :, None]

    shaft_line = np.exp(-((u_c / 0.008) ** 2))
    rgb = dark_vane * (1.0 - stripe_mask[:, :, None]) + c_streak[None, None, :] * stripe_mask[:, :, None]
    rgb = rgb * (1.0 - (shaft_line * stripe_mask * 0.5)[:, :, None]) + c_shaft[None, None, :] * (shaft_line * stripe_mask * 0.5)[:, :, None]

    h = stripe_mask * 0.0040 + shaft_line * 0.0055 + barbs * 0.0018 + barb_fine * 0.0007
    normal = normal_from_height(h, strength=1.5)
    return rgb, normal


def generate_buff_plumage(size=2048):
    """Warm ochraceous-buff plumage for chin, throat, and foreneck crop bib."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y
    u_c = np.abs(x - 0.50)

    # Rich honey/ochraceous buff with warm cinnamon base and creamy-buff shaft/tip:
    c_base = np.array([0.580, 0.310, 0.115], dtype=np.float32)
    c_buff = np.array([0.835, 0.565, 0.255], dtype=np.float32)
    c_tip = np.array([0.935, 0.745, 0.445], dtype=np.float32)
    c_shaft = np.array([0.965, 0.835, 0.580], dtype=np.float32)

    barb_coord = t - u_c * 0.30 + 0.005 * np.sin(t * 36.0)
    barbs = np.sin(barb_coord * TAU * 74.0) * 0.5 + 0.5
    barb_fine = np.sin(barb_coord * TAU * 185.0) * 0.5 + 0.5
    mottling = np.sin(x * TAU * 19.0 + y * TAU * 15.0) * 0.5 + 0.5

    basal_mix = np.clip((0.35 - t) / 0.35, 0.0, 1.0) ** 1.3
    tip_mix = np.clip((t - 0.55) / 0.45, 0.0, 1.0) ** 1.2
    shaft = np.exp(-((u_c / 0.018) ** 2)) * np.clip(t / 0.15, 0.0, 1.0)

    col = c_buff[None, None, :] * (1.0 - basal_mix[:, :, None] * 0.55) + c_base[None, None, :] * (basal_mix[:, :, None] * 0.55)
    col = col * (1.0 - tip_mix[:, :, None] * 0.55) + c_tip[None, None, :] * (tip_mix[:, :, None] * 0.55)
    col *= (0.86 + 0.10 * barbs + 0.04 * mottling)[:, :, None]
    rgb = col * (1.0 - (shaft * 0.65)[:, :, None]) + c_shaft[None, None, :] * (shaft * 0.65)[:, :, None]

    h = shaft * 0.0045 + barbs * 0.0020 + barb_fine * 0.0008
    normal = normal_from_height(h, strength=1.35)
    return rgb, normal


def generate_chestnut_plumage(size=2048):
    """Deep, fiery saturated rufous-chestnut plumage for lower crop, breast, belly, flanks, and thighs."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y
    u_c = np.abs(x - 0.50)

    # Authentic deep rufous-chestnut / mahogany-rust (never salmon pink!):
    c_base = np.array([0.285, 0.068, 0.018], dtype=np.float32)
    c_rust = np.array([0.545, 0.155, 0.035], dtype=np.float32)
    c_copper = np.array([0.715, 0.245, 0.065], dtype=np.float32)
    c_fringe = np.array([0.810, 0.340, 0.110], dtype=np.float32)
    c_shaft = np.array([0.360, 0.090, 0.022], dtype=np.float32)

    barb_coord = t - u_c * 0.28 + 0.005 * np.sin(t * 38.0)
    barbs = np.sin(barb_coord * TAU * 72.0) * 0.5 + 0.5
    barb_fine = np.sin(barb_coord * TAU * 180.0) * 0.5 + 0.5
    waves = np.sin(x * TAU * 18.0 + y * TAU * 22.0) * 0.5 + 0.5

    basal_mix = np.clip((0.32 - t) / 0.32, 0.0, 1.0) ** 1.3
    mid_glow = np.exp(-(((t - 0.65) / 0.25) ** 2))
    tip_fringe = np.clip((t - 0.85) / 0.15, 0.0, 1.0) * np.clip(1.0 - u_c * 1.7, 0.0, 1.0)
    shaft = np.exp(-((u_c / 0.014) ** 2))

    col = c_rust[None, None, :] * (1.0 - basal_mix[:, :, None] * 0.65) + c_base[None, None, :] * (basal_mix[:, :, None] * 0.65)
    col = col * (1.0 - mid_glow[:, :, None] * 0.48) + c_copper[None, None, :] * (mid_glow[:, :, None] * 0.48)
    col = col * (1.0 - tip_fringe[:, :, None] * 0.42) + c_fringe[None, None, :] * (tip_fringe[:, :, None] * 0.42)
    col *= (0.85 + 0.11 * barbs + 0.04 * waves)[:, :, None]
    rgb = col * (1.0 - (shaft * 0.70)[:, :, None]) + c_shaft[None, None, :] * (shaft * 0.70)[:, :, None]

    h = shaft * 0.0050 + barbs * 0.0022 + barb_fine * 0.0008
    normal = normal_from_height(h, strength=1.45)
    return rgb, normal


def generate_flight_chestnut(size=2048):
    """2-Column Wing & Mantle Atlas (feather-0.png & surface-normal.png):
    - Column 0 (x in [0.0, 0.5]): Dark iridescent bronzy-olive wing covert / tertial with crisp creamy-buff terminal wing-bar crescent.
    - Column 1 (x in [0.5, 1.0]): Glowing deep rufous-chestnut primary / secondary flight feather with dark bronzy-sepia distal tip.
    """
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y  # t=1 at tip (row 0), t=0 at base (row size-1)

    # Local u in [0, 1] within each half-column, centered at u_local = 0.5
    is_primary = (x >= 0.50).astype(np.float32)
    u_local = np.where(x < 0.50, x * 2.0, (x - 0.50) * 2.0)
    u_c = np.abs(u_local - 0.50)

    rachis = np.exp(-((u_c / 0.016) ** 2))
    barb_coord = t - u_c * 0.34 + 0.004 * np.sin(t * 44.0)
    barbs = np.sin(barb_coord * TAU * 84.0) * 0.5 + 0.5
    barb_fine = np.sin(barb_coord * TAU * 210.0) * 0.5 + 0.5
    barb_mod = 0.86 + 0.10 * barbs + 0.04 * barb_fine

    # --- COLUMN 0: Dark Iridescent Bronzy-Olive Wing Covert with Creamy-Buff Terminal Bar ---
    c_cov_base = np.array([0.042, 0.035, 0.024], dtype=np.float32)
    c_cov_olive = np.array([0.115, 0.098, 0.058], dtype=np.float32)
    c_cov_bronze = np.array([0.185, 0.148, 0.082], dtype=np.float32)
    c_cov_bar = np.array([0.955, 0.905, 0.785], dtype=np.float32)
    c_cov_subbar = np.array([0.780, 0.640, 0.430], dtype=np.float32)
    c_cov_shaft = np.array([0.220, 0.180, 0.125], dtype=np.float32)

    sheen = np.exp(-(((t - 0.56) / 0.26) ** 2)) * (1.0 - u_c * 0.8)
    cov_col = c_cov_base[None, None, :] * (1.0 - sheen[:, :, None]) + c_cov_olive[None, None, :] * sheen[:, :, None]
    bronze_glint = (barbs ** 2) * np.clip((t - 0.25) / 0.55, 0.0, 1.0) * 0.45
    cov_col = cov_col * (1.0 - bronze_glint[:, :, None]) + c_cov_bronze[None, None, :] * bronze_glint[:, :, None]
    cov_col *= barb_mod[:, :, None]

    # Terminal buff wing-bar crescent across distal tip (t > 0.83)
    arch_t = t + 0.045 * (u_c ** 1.6) + 0.008 * (barbs - 0.5)
    subbar_mask = np.clip((arch_t - 0.81) / 0.04, 0.0, 1.0)
    bar_mask = np.clip((arch_t - 0.845) / 0.035, 0.0, 1.0)
    cov_col = cov_col * (1.0 - subbar_mask[:, :, None] * 0.75) + c_cov_subbar[None, None, :] * (subbar_mask[:, :, None] * 0.75)
    cov_col = cov_col * (1.0 - bar_mask[:, :, None] * 0.95) + c_cov_bar[None, None, :] * (bar_mask[:, :, None] * 0.95)
    cov_col = cov_col * (1.0 - rachis[:, :, None] * 0.75) + c_cov_shaft[None, None, :] * (rachis[:, :, None] * 0.75)

    # --- COLUMN 1: Glowing Rufous-Chestnut Primary Remex with Dark Bronzy-Sepia Tip ---
    c_pri_base = np.array([0.420, 0.115, 0.025], dtype=np.float32)
    c_pri_rufous = np.array([0.655, 0.205, 0.048], dtype=np.float32)
    c_pri_glow = np.array([0.795, 0.285, 0.078], dtype=np.float32)
    c_pri_tip = np.array([0.075, 0.060, 0.042], dtype=np.float32)
    c_pri_shaft = np.array([0.160, 0.055, 0.020], dtype=np.float32)

    pri_glow = np.exp(-(((t - 0.48) / 0.28) ** 2)) * (1.0 - u_c * 0.6)
    pri_col = c_pri_rufous[None, None, :] * (1.0 - pri_glow[:, :, None]) + c_pri_glow[None, None, :] * pri_glow[:, :, None]
    basal_dark = np.clip((0.20 - t) / 0.20, 0.0, 1.0)
    pri_col = pri_col * (1.0 - basal_dark[:, :, None] * 0.45) + c_pri_base[None, None, :] * (basal_dark[:, :, None] * 0.45)
    pri_col *= barb_mod[:, :, None]

    # Dark bronzy-sepia distal 22% tip and narrow outer-web margin
    tip_chevron = t + u_c * 0.16 + 0.012 * (barbs - 0.5)
    dark_tip_mask = np.clip((tip_chevron - 0.75) / 0.09, 0.0, 1.0) ** 1.2
    outer_edge_dark = np.clip((0.16 - u_local) / 0.12, 0.0, 1.0) * np.clip((t - 0.45) / 0.30, 0.0, 1.0) * 0.75
    dark_mask = np.maximum(dark_tip_mask, outer_edge_dark)
    pri_col = pri_col * (1.0 - dark_mask[:, :, None]) + c_pri_tip[None, None, :] * dark_mask[:, :, None]
    pri_col = pri_col * (1.0 - rachis[:, :, None] * 0.85) + c_pri_shaft[None, None, :] * (rachis[:, :, None] * 0.85)

    rgb = cov_col * (1.0 - is_primary[:, :, None]) + pri_col * is_primary[:, :, None]
    h = rachis * 0.0085 + barbs * 0.0020 + barb_fine * 0.0008 + bar_mask * (1.0 - is_primary) * 0.0015
    normal = normal_from_height(h, strength=1.55)
    return rgb, normal


def generate_banded_tail(size=2048):
    """Long graduated rectrix: deep bronzy-sepia with broad, crisp creamy-buff terminal band across distal 20%."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y
    u_c = np.abs(x - 0.50)
    rachis = np.exp(-((u_c / 0.014) ** 2))

    barb_coord = t - u_c * 0.24 + 0.004 * np.sin(t * 40.0)
    barbs = np.sin(barb_coord * TAU * 78.0) * 0.5 + 0.5
    barb_fine = np.sin(barb_coord * TAU * 195.0) * 0.5 + 0.5

    c_dark = np.array([0.048, 0.038, 0.026], dtype=np.float32)
    c_bronze = np.array([0.112, 0.092, 0.062], dtype=np.float32)
    c_subband = np.array([0.740, 0.610, 0.410], dtype=np.float32)
    c_band = np.array([0.960, 0.915, 0.800], dtype=np.float32)
    c_shaft_dark = np.array([0.095, 0.065, 0.038], dtype=np.float32)
    c_shaft_light = np.array([0.840, 0.770, 0.630], dtype=np.float32)

    sheen = np.exp(-(((t - 0.48) / 0.28) ** 2)) * (0.5 + 0.5 * barbs)
    vane_dark = c_dark[None, None, :] * (1.0 - sheen[:, :, None] * 0.6) + c_bronze[None, None, :] * (sheen[:, :, None] * 0.6)

    band_t = t - 0.025 * (u_c ** 1.5) + 0.006 * (barbs - 0.5)
    sub_mask = np.clip((band_t - 0.775) / 0.025, 0.0, 1.0)
    band_mask = np.clip((band_t - 0.795) / 0.022, 0.0, 1.0)

    rgb = vane_dark * (1.0 - sub_mask[:, :, None] * 0.7) + c_subband[None, None, :] * (sub_mask[:, :, None] * 0.7)
    rgb = rgb * (1.0 - band_mask[:, :, None]) + c_band[None, None, :] * band_mask[:, :, None]
    rgb *= (0.88 + 0.08 * barbs + 0.04 * barb_fine)[:, :, None]

    shaft_col = c_shaft_dark[None, None, :] * (1.0 - band_mask[:, :, None]) + c_shaft_light[None, None, :] * band_mask[:, :, None]
    rgb = rgb * (1.0 - rachis[:, :, None] * 0.85) + shaft_col * (rachis[:, :, None] * 0.85)

    h = rachis * 0.0080 + barbs * 0.0020 + barb_fine * 0.0007 + band_mask * 0.0012
    normal = normal_from_height(h, strength=1.45)
    return rgb, normal


def generate_slate_horn(size=2048):
    """UV-zoned rhamphotheca (upper half y < 0.5) and scutellate/reticulate avian podotheca (lower half y >= 0.5)."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    t = 1.0 - y

    # Colors:
    # Dark olive-slate horn: #1b2126, weathered pale olive-ivory tomium/culmen: #5c6564
    # Avian tarsal scutes: #1e242b, deep inter-scute groove: #0b0e12, scute ridge highlight: #37414c
    c_horn = np.array([0.106, 0.129, 0.149], dtype=np.float32)
    c_olive_horn = np.array([0.165, 0.188, 0.184], dtype=np.float32)
    c_wear = np.array([0.365, 0.400, 0.392], dtype=np.float32)

    striae = np.sin(x * TAU * 58.0 + 0.8 * np.sin(y * TAU * 9.0)) * 0.5 + 0.5
    grain = np.sin(x * TAU * 195.0 + y * TAU * 42.0) * 0.5 + 0.5

    # Upper half (y < 0.5, t > 0.5): Raptorial/folivorous bill rhamphotheca
    t_bill = np.clip((t - 0.50) * 2.0, 0.0, 1.0)
    edge_wear = np.maximum(np.clip((0.12 - x) / 0.12, 0.0, 1.0), np.clip((x - 0.88) / 0.12, 0.0, 1.0))
    culmen_wear = np.exp(-(((x - 0.50) / 0.09) ** 2)) * 0.65
    tip_wear = np.clip((t_bill - 0.72) / 0.28, 0.0, 1.0)
    wear = np.clip(np.maximum(edge_wear * 0.75, np.maximum(culmen_wear, tip_wear * 0.85)), 0.0, 1.0)

    bill_rgb = c_horn[None, None, :] * (1.0 - t_bill[:, :, None] * 0.35) + c_olive_horn[None, None, :] * (t_bill[:, :, None] * 0.35)
    bill_rgb *= (0.88 + 0.12 * (striae * 0.65 + grain * 0.35))[:, :, None]
    bill_rgb = bill_rgb * (1.0 - wear[:, :, None] * 0.70) + c_wear[None, None, :] * (wear[:, :, None] * 0.70)

    # Lower half (y >= 0.5, t <= 0.5): Scutellate & hexagonal reticulate tarsometatarsus/toe scales
    t_leg = np.clip(t * 2.0, 0.0, 1.0)
    # Anterior scutellate transverse plates near x ~ 0.5, hexagonal reticulate scales on sides
    ant_mask = np.exp(-(((x - 0.50) / 0.22) ** 4))
    scute_rows = np.sin(t_leg * TAU * 28.0 + 0.25 * np.cos(x * TAU * 4.0))
    scute_plate = np.clip((scute_rows + 0.75) / 1.75, 0.0, 1.0) ** 0.55

    # Hexagonal reticulate tubercles on lateral/plantar surfaces
    hx = x * 42.0
    hy = t_leg * 56.0
    retic_a = np.cos(hx * TAU) * np.cos(hy * TAU * 0.577)
    retic_b = np.cos((hx + 0.5) * TAU) * np.cos((hy + 0.5) * TAU * 0.577)
    retic = np.clip(np.maximum(retic_a, retic_b) * 0.5 + 0.5, 0.0, 1.0) ** 0.65

    scale_h = ant_mask * scute_plate + (1.0 - ant_mask) * retic
    c_groove = np.array([0.043, 0.051, 0.063], dtype=np.float32)
    c_scute = np.array([0.135, 0.160, 0.188], dtype=np.float32)
    c_ridge = np.array([0.235, 0.275, 0.318], dtype=np.float32)

    leg_rgb = c_groove[None, None, :] * (1.0 - scale_h[:, :, None]) + c_scute[None, None, :] * scale_h[:, :, None]
    ridge_hi = np.clip((scale_h - 0.68) / 0.32, 0.0, 1.0)
    leg_rgb = leg_rgb * (1.0 - ridge_hi[:, :, None] * 0.55) + c_ridge[None, None, :] * (ridge_hi[:, :, None] * 0.55)
    leg_rgb *= (0.92 + 0.08 * grain)[:, :, None]

    is_leg = (y >= 0.50).astype(np.float32)
    rgb = bill_rgb * (1.0 - is_leg[:, :, None]) + leg_rgb * is_leg[:, :, None]
    h = (1.0 - is_leg) * (striae * 0.0025 + grain * 0.0010) + is_leg * (scale_h * 0.0085 + grain * 0.0010)
    normal = normal_from_height(h, strength=1.6)
    return rgb, normal


def generate_red_iris(size=1024):
    """Carbuncle ruby-crimson iris with radiating stroma cords, collarette, limbal ring, and jet-black pupil."""
    y, x = np.mgrid[0:size, 0:size] / (size - 1)
    dx = x - 0.50
    dy = y - 0.50
    r = np.sqrt(dx * dx + dy * dy) * 2.0
    theta = np.arctan2(dy, dx)

    c_ruby = np.array([0.740, 0.035, 0.042], dtype=np.float32)
    c_scarlet = np.array([0.940, 0.110, 0.075], dtype=np.float32)
    c_deep = np.array([0.420, 0.015, 0.018], dtype=np.float32)
    c_limbal = np.array([0.065, 0.008, 0.008], dtype=np.float32)
    c_pupil = np.array([0.008, 0.008, 0.010], dtype=np.float32)

    fibers = np.sin(theta * 84.0 + 1.4 * np.sin(r * 28.0)) * 0.5 + 0.5
    crypts = (np.sin(theta * 29.0 - r * 18.0) * 0.5 + 0.5) * (np.cos(theta * 43.0 + r * 24.0) * 0.5 + 0.5)
    collarette = np.exp(-(((r - 0.46) / 0.09) ** 2))
    limbal_mask = np.clip((r - 0.78) / 0.18, 0.0, 1.0) ** 1.2
    pupil_mask = np.clip((0.35 - r) / 0.022, 0.0, 1.0)

    iris_col = c_ruby[None, None, :] * (0.72 + 0.28 * fibers[:, :, None])
    iris_col = iris_col * (1.0 - crypts[:, :, None] * 0.35) + c_deep[None, None, :] * (crypts[:, :, None] * 0.35)
    iris_col = iris_col * (1.0 - collarette[:, :, None] * 0.55) + c_scarlet[None, None, :] * (collarette[:, :, None] * 0.55)
    iris_col = iris_col * (1.0 - limbal_mask[:, :, None]) + c_limbal[None, None, :] * limbal_mask[:, :, None]
    rgb = iris_col * (1.0 - pupil_mask[:, :, None]) + c_pupil[None, None, :] * pupil_mask[:, :, None]

    h = fibers * 0.0028 + collarette * 0.0020
    normal = normal_from_height(h, strength=1.1)
    return rgb, normal


def generate_all_textures(out_dir):
    out = Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)

    skin, skin_n = generate_orbital_skin(2048)
    save_image(skin, out / "orbital-skin.png", is_color=True)
    save_image(skin_n, out / "orbital-normal.png", is_color=False)

    crest, crest_n = generate_rufous_crest(2048)
    save_image(crest, out / "feather-2.png", is_color=True)

    streak, streak_n = generate_streaked_neck(2048)
    save_image(streak, out / "streaked-contour.png", is_color=True)

    buff, buff_n = generate_buff_plumage(2048)
    save_image(buff, out / "feather-1.png", is_color=True)
    save_image(buff, out / "breast-underplumage.png", is_color=True)

    chestnut, chestnut_n = generate_chestnut_plumage(2048)
    save_image(chestnut, out / "underplumage.png", is_color=True)
    save_image(chestnut_n, out / "underplumage-normal.png", is_color=False)

    flight, flight_n = generate_flight_chestnut(2048)
    save_image(flight, out / "feather-0.png", is_color=True)
    save_image(flight_n, out / "surface-normal.png", is_color=False)

    tail, tail_n = generate_banded_tail(2048)
    save_image(tail, out / "feather-3.png", is_color=True)

    horn, horn_n = generate_slate_horn(2048)
    save_image(horn, out / "horn-bill.png", is_color=True)
    save_image(horn_n, out / "horn-normal.png", is_color=False)

    iris, iris_n = generate_red_iris(1024)
    save_image(iris, out / "red-iris.png", is_color=True)

    print(f"Generated 2048x2048 true-sRGB Hoatzin PBR textures in {out}")


if __name__ == '__main__':
    generate_all_textures('game/assets/guardians/crested_canopy/textures')
