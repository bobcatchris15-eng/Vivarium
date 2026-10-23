"""Unified 3D-space anatomical scale & pigment baker for the Great Ground Guardian.

Evaluates regional varanid scale morphology (polygonal cranial shields, barred
labial plates, bead-and-keel dorsal osteoderms, transverse rectangular belly
scutes, annular caudal verticils, diamond limb scales, and striated 3D crest
osteoderms) directly in 3D object coordinates (X right, Y up, -Z forward).

Computes directional heightfield derivatives along each triangle's 3D UV tangent
and bitangent vectors so the baked 2048x2048 tangent-space normal map, albedo,
and roughness maps are 100% continuous across arbitrary UV island seams.
"""
import math
import bpy
import numpy as np


def _hash2(u, v):
    return np.mod(np.sin(u * 127.1 + v * 311.7) * 43758.5453123, 1.0)


def _hash3(x, y, z):
    return np.mod(np.sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453123, 1.0)


def _staggered_scale_cell(u, v, jitter=0.18, aspect=1.0):
    """Return (cell_height, keel_ridge, cell_seed, du, dv) for a staggered scale grid."""
    row = np.floor(v)
    uu = u + (row % 2.0) * 0.5
    col = np.floor(uu)
    seed = _hash2(col, row)
    seed2 = _hash2(col + 19.3, row - 41.7)
    du = (uu - col) - 0.5 + (seed - 0.5) * jitter
    dv = (v - row) - 0.5 + (seed2 - 0.5) * jitter
    # Hexagonal-oval distance metric
    r2 = (du * aspect) ** 2 + dv ** 2 + 0.18 * np.abs(du * dv)
    rim = np.clip(1.0 - r2 * 3.6, 0.0, 1.0)
    dome = rim ** 0.52
    # Longitudinal median keel running along v (anterior-posterior) within each scale
    keel_env = np.clip(1.0 - (dv * 1.65) ** 2, 0.0, 1.0)
    keel = dome * keel_env * np.exp(-((du * 5.2) ** 2))
    return dome, keel, seed, du, dv


def evaluate_scale_field(pts, nrms, scute_flag):
    """Evaluate 3D varanid scale heightfield H in [0, 1] and regional masks."""
    x = pts[:, 0]
    y = pts[:, 1]
    z = pts[:, 2]
    nx = nrms[:, 0]
    ny = nrms[:, 1]
    nz = nrms[:, 2]

    ax = np.abs(x)
    # Approximate mouth line y_mouth(z)
    t_mouth = np.clip((z + 1.255) / 0.395, 0.0, 1.0)
    y_mouth = 0.268 + 0.014 * t_mouth - 0.005 * np.sin(math.pi * t_mouth)

    # Regional blend weights (smooth transitions in 3D metres)
    w_head = np.clip((-0.80 - z) / 0.12, 0.0, 1.0)
    w_tail = np.clip((z - 0.44) / 0.14, 0.0, 1.0) * np.clip((0.20 - ax) / 0.06, 0.0, 1.0)
    w_limb = np.clip((ax - 0.165) / 0.045, 0.0, 1.0) * (1.0 - w_tail) * (1.0 - w_head)
    w_foot = np.clip((0.125 - y) / 0.040, 0.0, 1.0) * np.clip((ax - 0.18) / 0.06, 0.0, 1.0)
    w_limb = np.maximum(w_limb, w_foot)
    w_belly = np.clip((-ny - 0.38) / 0.35, 0.0, 1.0) * (1.0 - w_head * 0.85) * (1.0 - w_limb * 0.85)
    w_labial = w_head * np.exp(-(((y - y_mouth) / 0.015) ** 2)) * np.clip((ax - 0.014) / 0.022, 0.0, 1.0)
    w_gular = np.clip((-0.48 - z) / 0.18, 0.0, 1.0) * np.clip((y_mouth - 0.008 - y) / 0.018, 0.0, 1.0) * (1.0 - w_limb) * (1.0 - w_labial * 0.85)

    # 1. Dorsal & Flank bead-and-keel osteoderm scales (~9-12mm)
    # Use continuous cylindrical-like arc coordinate around body axis
    cy_est = np.where(z < -0.5, 0.29, 0.26)
    arc = np.arctan2(x, y - cy_est + 1e-4) * 0.19
    d_dome, d_keel, d_seed, _, _ = _staggered_scale_cell(arc * 95.0, z * 86.0, jitter=0.16, aspect=1.08)
    # Surrounding micro-rosette granules in the interstitial creases
    g_dome, _, _, _, _ = _staggered_scale_cell(arc * 270.0, z * 250.0, jitter=0.22, aspect=1.0)
    h_dorsal = 0.68 * d_dome + 0.34 * d_keel + 0.16 * (1.0 - d_dome) * g_dome
    # Add lateral flank fold and cervical neck fold macro-creases
    flank_fold = np.exp(-(((y - 0.235) / 0.018) ** 2)) * np.clip(1.0 - np.abs(z + 0.15) / 0.42, 0.0, 1.0) * np.clip((ax - 0.14) / 0.04, 0.0, 1.0)
    gular_fold = np.exp(-(((z + 0.81) / 0.024) ** 2)) * (1.0 - w_limb)
    antehumeral_fold = np.exp(-(((z + 0.57) / 0.026) ** 2)) * (1.0 - w_limb)
    macro_folds = -0.22 * flank_fold * np.sin(z * 110.0 + x * 45.0) - 0.28 * (gular_fold + antehumeral_fold)
    h_dorsal = np.clip(h_dorsal + macro_folds, 0.0, 1.0)

    # 2. Cranial polygonal head shields & circumorbital granules
    # Use wrap-around cranial coordinate s_head so both horizontal skull tables and vertical cheek/jaw sides have crisp scales
    s_head = np.sign(x) * (ax + np.abs(0.32 - y) * 0.88)
    d_eye = np.sqrt(((z + 1.006) / 0.045) ** 2 + ((y - 0.326) / 0.032) ** 2 + ((ax - 0.085) / 0.035) ** 2)
    eye_granule_w = np.clip(1.2 - d_eye, 0.0, 1.0)
    c_dome, c_keel, c_seed, _, _ = _staggered_scale_cell(s_head * 105.0, z * 95.0, jitter=0.20, aspect=0.95)
    e_dome, _, _, _, _ = _staggered_scale_cell(y * 230.0, z * 230.0, jitter=0.15, aspect=1.0)
    h_head = (1.0 - eye_granule_w) * (0.78 * (c_dome ** 0.75) + 0.18 * c_keel) + eye_granule_w * (0.72 * e_dome)
    # Rectangular supralabial & infralabial shields along upper and lower jaw margins
    lab_u = (z + 1.26) * 82.0
    lab_v = (y - y_mouth) / 0.0075
    lab_x_plate = 1.0 - np.clip((np.abs(np.mod(lab_u, 1.0) - 0.5) * 2.18) ** 3.8, 0.0, 1.0)
    # Distinct upper and lower labial rows separated by the oral commissure groove at y == y_mouth
    oral_groove = 1.0 - 0.75 * np.exp(-((lab_v * 2.2) ** 2))
    lab_y_env = np.clip(1.0 - (lab_v * 0.48) ** 2, 0.0, 1.0) * oral_groove
    lab_cell = (lab_x_plate * lab_y_env) ** 0.65
    h_head = (1.0 - w_labial) * h_head + w_labial * (0.86 * lab_cell + 0.08)

    # 3. Mandibular & Gular throat pleats + oval pebble scales (wraps around vertical jaw sides via s_jaw)
    s_jaw = np.sign(x) * (ax + np.maximum(0.0, y_mouth - y) * 1.15)
    gul_pleat = 0.5 + 0.5 * np.cos(s_jaw * 220.0 + np.sin(z * 18.0) * 1.2)
    gul_dome, gul_keel, _, _, _ = _staggered_scale_cell(s_jaw * 135.0, z * 112.0, jitter=0.14, aspect=1.20)
    h_gular = 0.64 * gul_dome + 0.22 * gul_keel + 0.14 * gul_pleat

    # 4. Ventral rectangular belly scutes (transverse rows)
    bel_u = x * 72.0
    bel_v = z * 105.0
    bel_x = 1.0 - np.clip((np.abs(np.mod(bel_u, 1.0) - 0.5) * 2.12) ** 3.5, 0.0, 1.0)
    bel_z_frac = np.mod(bel_v, 1.0)
    # Imbricated posterior overlap step on each belly plate
    bel_z = np.clip(bel_z_frac * 1.25, 0.0, 1.0) * np.clip((1.0 - bel_z_frac) * 5.5, 0.0, 1.0)
    h_belly = 0.80 * (bel_x * bel_z) ** 0.55 + 0.10

    # 5. Caudal tail annular verticils (whorls of keeled rectangular scutes)
    t_ring = z * 94.0
    t_around = np.arctan2(x, y - 0.18) * 14.0
    tr_frac = np.mod(t_ring, 1.0)
    tr_env = np.clip(tr_frac * 1.3, 0.0, 1.0) * np.clip((1.0 - tr_frac) * 5.0, 0.0, 1.0)
    ta_frac = np.mod(t_around + (np.floor(t_ring) % 2.0) * 0.35, 1.0) - 0.5
    ta_env = np.clip(1.0 - (ta_frac * 2.1) ** 2, 0.0, 1.0)
    t_keel = tr_env * np.exp(-((ta_frac * 5.5) ** 2))
    h_tail = 0.68 * (tr_env * ta_env) ** 0.55 + 0.32 * t_keel

    # 6. Limb & Digit imbricated diamond scales + transverse dorsal digital scutellae on toes
    l_dome, l_keel, _, _, _ = _staggered_scale_cell((x + y) * 125.0, (z - y * 0.6) * 125.0, jitter=0.14, aspect=1.0)
    # At bake time (before lift_monitor_anatomy), the toes sit at y in [0.012, 0.052] and ax > 0.24
    toe_ring = (z * 195.0) + (ax * 82.0)
    toe_frac = np.mod(toe_ring, 1.0)
    # Imbricated transverse digital plate with sharp dark inter-scale sulcus groove
    toe_plate_along = np.clip(toe_frac * 1.45, 0.0, 1.0) * np.clip((1.0 - toe_frac) * 5.5, 0.0, 1.0)
    toe_across = 1.0 - 0.35 * (np.sin((x - z * 0.35) * 210.0) ** 4)
    toe_scutella = (toe_plate_along * toe_across) ** 0.58
    w_toe_top = np.clip((0.056 - y) / 0.020, 0.0, 1.0) * np.clip((ax - 0.24) / 0.05, 0.0, 1.0) * np.clip(ny + 0.35, 0.0, 1.0)
    h_limb = (1.0 - w_toe_top) * (0.72 * l_dome + 0.28 * l_keel) + w_toe_top * (0.84 * toe_scutella + 0.16 * l_dome)

    # Combine regional heights smoothly
    h = h_dorsal
    h = (1.0 - w_belly) * h + w_belly * h_belly
    h = (1.0 - w_limb) * h + w_limb * h_limb
    h = (1.0 - w_tail) * h + w_tail * h_tail
    h = (1.0 - w_head) * h + w_head * h_head
    h = (1.0 - w_gular) * h + w_gular * h_gular

    # 7. Sculpted 3D osteoderm crests: longitudinal keratin ridges & growth striations
    scute_w = np.clip(scute_flag, 0.0, 1.0)
    scute_striae = 0.62 + 0.24 * np.sin(x * 420.0 + y * 380.0) + 0.14 * np.cos(z * 190.0)
    h = (1.0 - scute_w) * h + scute_w * scute_striae

    return np.clip(h, 0.0, 1.0), d_keel, (w_head, w_gular, w_labial, w_belly, w_limb, w_tail, scute_w, y_mouth)


def evaluate_pigment_and_roughness(pts, nrms, scute_flag, profile):
    """Evaluate 3D anatomical varanid albedo RGB and roughness."""
    h, d_keel, masks = evaluate_scale_field(pts, nrms, scute_flag)
    w_head, w_gular, w_labial, w_belly, w_limb, w_tail, scute_w, y_mouth = masks

    x = pts[:, 0]
    y = pts[:, 1]
    z = pts[:, 2]
    ny = nrms[:, 1]
    ax = np.abs(x)

    # Base colours (sRGB-like linear-ready values tuned for Godot & Blender PBR)
    c_slate_dark = np.array([0.052, 0.048, 0.035], dtype=np.float32)
    c_olive_mid = np.array([0.098, 0.089, 0.060], dtype=np.float32)
    c_ochre_ring = np.array([0.330, 0.238, 0.098], dtype=np.float32)
    c_ember_gold = np.array([0.435, 0.305, 0.115], dtype=np.float32)
    c_velvet_blk = np.array([0.032, 0.029, 0.021], dtype=np.float32)
    c_belly_buff = np.array([0.265, 0.228, 0.148], dtype=np.float32)
    c_crest_horn = np.array([0.245, 0.178, 0.082], dtype=np.float32)

    # 1. Dorsum & Flanks: Dark olive-slate ground with transverse rows of ocellated rosettes
    cy = np.interp(z, profile[:, 0], profile[:, 1])
    rx = np.interp(z, profile[:, 0], profile[:, 2])
    ry = np.interp(z, profile[:, 0], profile[:, 3])
    theta = np.arctan2(ax / np.maximum(rx, 0.025), (y - cy) / np.maximum(ry, 0.025))
    # 7.5 transverse rows per metre along neck & trunk
    ru = theta * 2.35
    rv = (z + 0.78) * 8.2
    r_row = np.floor(rv)
    r_col = np.floor(ru + (r_row % 2.0) * 0.45)
    r_seed = _hash2(r_col, r_row)
    du = (ru + (r_row % 2.0) * 0.45 - r_col) - 0.5 + (r_seed - 0.5) * 0.22
    dv = (rv - r_row) - 0.5 + (_hash2(r_col + 7.1, r_row - 13.7) - 0.5) * 0.20
    dist = np.sqrt(du * du * 1.15 + dv * dv)

    # Ocellated rosette: golden center (dist < 0.14), dark inner moat (0.14..0.24), amber outer ring (0.24..0.38)
    active_rosette = (r_seed > 0.22) & (theta > 0.18) & (theta < 2.45)
    center_dot = np.clip((0.15 - dist) / 0.05, 0.0, 1.0) * active_rosette
    outer_ring = np.exp(-(((dist - 0.30) / 0.065) ** 2)) * active_rosette
    dark_moat = np.exp(-(((dist - 0.20) / 0.055) ** 2)) * active_rosette

    # Secondary smaller golden speckles between rosettes
    su = theta * 9.5
    sv = z * 31.0
    s_row = np.floor(sv)
    s_col = np.floor(su + (s_row % 2.0) * 0.5)
    s_seed = _hash2(s_col + 31.0, s_row + 17.0)
    s_dist = (su + (s_row % 2.0) * 0.5 - s_col - 0.5) ** 2 + (sv - s_row - 0.5) ** 2
    speckle = np.clip((0.11 - s_dist) / 0.05, 0.0, 1.0) * (s_seed > 0.72) * (1.0 - active_rosette * np.clip((0.42 - dist) / 0.1, 0.0, 1.0))

    # Build dorsal base colour
    dorsal_spine_darken = np.exp(-((ax / 0.055) ** 2)) * np.clip(ny, 0.0, 1.0)
    rgb = c_slate_dark[None, :] * (0.45 + 0.55 * dorsal_spine_darken[:, None]) + c_olive_mid[None, :] * (0.55 * (1.0 - dorsal_spine_darken[:, None]))
    rgb = rgb * (1.0 - 0.45 * dark_moat[:, None]) + c_velvet_blk[None, :] * (0.45 * dark_moat[:, None])
    rgb = rgb * (1.0 - 0.85 * outer_ring[:, None]) + c_ochre_ring[None, :] * (0.85 * outer_ring[:, None])
    rgb = rgb * (1.0 - 0.90 * center_dot[:, None]) + c_ember_gold[None, :] * (0.90 * center_dot[:, None])
    rgb = rgb * (1.0 - 0.65 * speckle[:, None]) + c_ochre_ring[None, :] * (0.65 * speckle[:, None])

    # 2. Cranial markings: bronzed-olive brow/snout tables, dark temporal eye-band, warm subocular stripe, barred labials
    head_mottle = 0.5 + 0.5 * np.sin(x * 65.0 + z * 48.0) * np.cos(y * 72.0 - z * 35.0)
    head_rgb = c_slate_dark[None, :] * 0.85 + c_olive_mid[None, :] * (0.45 * head_mottle[:, None])
    # Supraorbital & canthus warm bronze highlight
    brow_warm = np.exp(-(((ax - 0.078) / 0.024) ** 2)) * np.exp(-(((y - 0.340) / 0.022) ** 2)) * np.clip(1.0 - np.abs(z + 0.99) / 0.14, 0.0, 1.0)
    head_rgb = head_rgb + c_ochre_ring[None, :] * (0.42 * brow_warm[:, None])
    # Dark temporal band behind eye (z in [-1.00, -0.82], y in [0.305, 0.338])
    temp_band = np.exp(-(((y - 0.322) / 0.014) ** 2)) * np.clip((z + 1.00) / 0.03, 0.0, 1.0) * np.clip((-0.82 - z) / 0.04, 0.0, 1.0) * np.clip((ax - 0.065) / 0.02, 0.0, 1.0)
    subocular_stripe = np.exp(-(((y - 0.301) / 0.011) ** 2)) * np.clip((z + 1.06) / 0.04, 0.0, 1.0) * np.clip((-0.83 - z) / 0.04, 0.0, 1.0) * np.clip((ax - 0.065) / 0.02, 0.0, 1.0)
    head_rgb = head_rgb * (1.0 - 0.65 * temp_band[:, None]) + c_velvet_blk[None, :] * (0.65 * temp_band[:, None])
    head_rgb = head_rgb * (1.0 - 0.65 * subocular_stripe[:, None]) + c_ochre_ring[None, :] * (0.65 * subocular_stripe[:, None])
    # Barred labial shields (alternating dark charcoal and warm buff-amber vertical bars)
    lab_bar = (0.5 + 0.5 * np.sin((z + 1.25) * 165.0)) ** 2.2
    lab_rgb = c_velvet_blk[None, :] * (1.0 - lab_bar[:, None]) + c_belly_buff[None, :] * (0.95 * lab_bar[:, None])
    head_rgb = head_rgb * (1.0 - w_labial[:, None]) + lab_rgb * w_labial[:, None]
    rgb = rgb * (1.0 - w_head[:, None]) + head_rgb * w_head[:, None]

    # 3. Mandibular sides & Gular throat pouch: warm buff-ochre with dark V-chevron reticulations
    s_jaw = ax + np.maximum(0.0, y_mouth - y) * 1.15
    gul_chev = (0.5 + 0.5 * np.sin((z + s_jaw * 1.35) * 95.0 + np.sin(x * 60.0) * 0.8)) ** 2.4
    gul_rgb = c_belly_buff[None, :] * (0.90 - 0.58 * gul_chev[:, None]) + c_slate_dark[None, :] * (0.58 * gul_chev[:, None])
    rgb = rgb * (1.0 - w_gular[:, None]) + gul_rgb * w_gular[:, None]

    # 4. Ventral belly countershading: warm parchment-ochre with subtle dark cross-bars
    bel_bar = (0.5 + 0.5 * np.sin(z * 38.0 + np.cos(x * 28.0))) ** 4.0
    bel_rgb = c_belly_buff[None, :] * (0.88 - 0.32 * bel_bar[:, None]) + c_slate_dark[None, :] * (0.32 * bel_bar[:, None])
    rgb = rgb * (1.0 - w_belly[:, None]) + bel_rgb * w_belly[:, None]

    # 5. Limbs & Digits: dark olive-slate with golden ocellated speckles, and bronzed-keratin digital scutellae on toes
    l_u = (x + y * 0.7) * 28.0
    l_v = (z - y * 0.5) * 28.0
    l_row = np.floor(l_v)
    l_col = np.floor(l_u + (l_row % 2.0) * 0.5)
    l_seed = _hash2(l_col - 19.0, l_row + 43.0)
    l_dist = (l_u + (l_row % 2.0) * 0.5 - l_col - 0.5) ** 2 + (l_v - l_row - 0.5) ** 2
    l_spot = np.clip((0.14 - l_dist) / 0.06, 0.0, 1.0) * (l_seed > 0.50) * np.clip((y - 0.032) / 0.035, 0.0, 1.0)
    toe_darken = np.clip((0.052 - y) / 0.025, 0.0, 1.0)
    limb_rgb = c_slate_dark[None, :] * (0.72 + 0.28 * toe_darken[:, None]) + c_olive_mid[None, :] * (0.28 * (1.0 - toe_darken[:, None]))
    limb_rgb = limb_rgb * (1.0 - 0.78 * l_spot[:, None]) + c_ochre_ring[None, :] * (0.78 * l_spot[:, None])
    # Highlight raised dorsal digital scutellae crowns on the toes with warm keratin-bronze and darken inter-scale sulci
    w_toe_top = np.clip((0.056 - y) / 0.020, 0.0, 1.0) * np.clip((ax - 0.24) / 0.05, 0.0, 1.0) * np.clip(ny + 0.35, 0.0, 1.0)
    toe_crown = np.clip((h - 0.42) / 0.45, 0.0, 1.0) * w_toe_top
    toe_sulcus = np.clip((0.40 - h) / 0.30, 0.0, 1.0) * w_toe_top
    limb_rgb = limb_rgb * (1.0 - 0.46 * toe_crown[:, None]) + c_crest_horn[None, :] * (0.46 * toe_crown[:, None])
    limb_rgb = limb_rgb * (1.0 - 0.55 * toe_sulcus[:, None]) + c_velvet_blk[None, :] * (0.55 * toe_sulcus[:, None])
    rgb = rgb * (1.0 - w_limb[:, None]) + limb_rgb * w_limb[:, None]

    # 6. Caudal tail: bold alternating charcoal-slate and warm amber-ochre rings
    tail_band_freq = (z - 0.45) * 19.5
    tail_band = (0.5 + 0.5 * np.sin(tail_band_freq + 0.35 * np.sin(y * 25.0))) ** 2.4
    # Dorsal tail sail crest stays darker bronzed-charcoal
    tail_crest_dark = np.clip((y - 0.23) / 0.045, 0.0, 1.0) * np.clip((1.6 - z) / 0.8, 0.0, 1.0)
    tail_rgb = c_velvet_blk[None, :] * (1.0 - tail_band[:, None]) + (c_ochre_ring * 0.88)[None, :] * tail_band[:, None]
    tail_rgb = tail_rgb * (1.0 - 0.55 * tail_crest_dark[:, None]) + c_slate_dark[None, :] * (0.55 * tail_crest_dark[:, None])
    rgb = rgb * (1.0 - w_tail[:, None]) + tail_rgb * w_tail[:, None]

    # 7. Sculpted 3D osteoderm crests: weathered dark horn base inheriting local coat with burnished bronze-amber keel ridges
    scute_keel = (scute_w ** 1.35) * (0.45 + 0.55 * h)
    scute_rgb = rgb * 0.72 + c_crest_horn[None, :] * (0.52 * scute_keel[:, None])
    rgb = rgb * (1.0 - 0.78 * scute_w[:, None]) + scute_rgb * (0.78 * scute_w[:, None])

    # 8. Scale-level ambient occlusion in interstitial sulci and specular sheen on raised keels
    scale_shade = 0.60 + 0.52 * (h ** 0.85) + 0.16 * d_keel
    micro_grain = 1.0 + 0.03 * np.sin(x * 490.0 + z * 430.0) * np.cos(y * 370.0 - z * 310.0)
    rgb = np.clip(rgb * (scale_shade * micro_grain)[:, None], 0.015, 0.85)

    # 9. Roughness map: polished on scale crowns/keels & labial shields, matte in interstitial skin sulci
    rough = 0.72 - 0.36 * h - 0.08 * d_keel - 0.08 * w_labial - 0.06 * scute_w + 0.05 * w_belly
    rough = np.clip(rough, 0.24, 0.82)

    return rgb, rough, h


def apply_3d_dermal_relief(body, amplitude=0.0011):
    """Displace body vertices along their smooth normals before or after union for physical 3D scale relief."""
    mesh = body.data
    n_verts = len(mesh.vertices)
    if n_verts == 0:
        return
    coords = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float32)
    normals = np.array([tuple(v.normal) for v in mesh.vertices], dtype=np.float32)
    scute_flag = np.zeros(n_verts, dtype=np.float32)
    h, d_keel, _ = evaluate_scale_field(coords, normals, scute_flag)

    x, y, z = coords[:, 0], coords[:, 1], coords[:, 2]
    # Protect plantar foot soles (y < 0.088) and eye/mouth apertures from normal displacement
    sole_mask = np.clip((y - 0.082) / 0.025, 0.0, 1.0)
    eye_dist = np.sqrt(((z + 1.006) / 0.030) ** 2 + ((y - 0.326) / 0.024) ** 2 + ((np.abs(x) - 0.086) / 0.024) ** 2)
    eye_mask = np.clip(eye_dist - 0.85, 0.0, 1.0)
    disp = ((h - 0.45) * amplitude + d_keel * (amplitude * 0.45)) * sole_mask * eye_mask
    new_coords = coords + normals * disp[:, None]
    for i, v in enumerate(mesh.vertices):
        v.co = tuple(new_coords[i])
    mesh.update()


def _dilate_map(arr, mask, iterations=10):
    """Dilate UV island borders outward so mipmapping never bleeds background seams."""
    cur_mask = mask.copy()
    out = arr.copy()
    for _ in range(iterations):
        prev = cur_mask.copy()
        for dy, dx in [(0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)]:
            shifted_m = np.roll(np.roll(prev, dy, axis=0), dx, axis=1)
            take = shifted_m & ~cur_mask
            if np.any(take):
                shifted_a = np.roll(np.roll(out, dy, axis=0), dx, axis=1)
                out[take] = shifted_a[take]
                cur_mask |= take
    return out, cur_mask


def bake_pigment(body, mat, out, profile):
    """Bake seam-free 2048x2048 albedo, tangent-space normal, and roughness maps in 3D space."""
    import bmesh
    size = 2048
    mesh = body.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    mesh.calc_loop_triangles()
    uv_layer_name = mesh.uv_layers.active.name
    mesh.calc_tangents(uvmap=uv_layer_name)
    profile_arr = np.array(profile, dtype=np.float32)

    coords = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float32)
    normals = np.array([tuple(v.normal) for v in mesh.vertices], dtype=np.float32)
    loop_tangents = np.array([tuple(l.tangent) for l in mesh.loops], dtype=np.float32)
    loop_bitangents = np.array([tuple(l.bitangent) for l in mesh.loops], dtype=np.float32)
    loop_normals = np.array([tuple(l.normal) for l in mesh.loops], dtype=np.float32)

    # Detect sculpted 3D osteoderm keel gradient via vertex color alpha < 0.99 (tagged in monitor_scutes_solo)
    scute_vert = np.zeros(len(mesh.vertices), dtype=np.float32)
    if 'Color' in mesh.color_attributes:
        col_attr = mesh.color_attributes['Color'].data
        for i in range(len(mesh.vertices)):
            a_val = float(col_attr[i].color[3])
            if a_val < 0.99:
                scute_vert[i] = min(1.0, max(0.0, (1.0 - a_val) / 0.55))

    # Set neutral white vertex Color attribute so glTF does not double-darken the baked albedo texture
    if 'Pigment' not in mesh.color_attributes:
        mesh.color_attributes.new(name='Pigment', type='FLOAT_COLOR', domain='POINT')
    vert_rgb, _, _ = evaluate_pigment_and_roughness(coords, normals, scute_vert, profile_arr)
    pig_attr = mesh.color_attributes['Pigment'].data
    col_attr = mesh.color_attributes['Color'].data
    for i, c in enumerate(vert_rgb):
        pig_attr[i].color = (float(c[0]), float(c[1]), float(c[2]), 1.0)
        col_attr[i].color = (1.0, 1.0, 1.0, 1.0)
    mesh.color_attributes.active_color = mesh.color_attributes['Color']

    # Rasterize 3D positions, normals, and UV tangent/bitangent frames onto the 2048x2048 chart
    # using signed-pixel-distance conservative gutter extrapolation so bilinear filtering across
    # UV island seams is 100% continuous and sub-pixel triangles are never missed.
    pos_buf = np.zeros((size, size, 3), dtype=np.float32)
    nrm_buf = np.zeros((size, size, 3), dtype=np.float32)
    tu_buf = np.zeros((size, size, 3), dtype=np.float32)
    tv_buf = np.zeros((size, size, 3), dtype=np.float32)
    scute_buf = np.zeros((size, size), dtype=np.float32)
    best_px_d = np.full((size, size), -1.85, dtype=np.float32)
    mask = np.zeros((size, size), dtype=bool)

    uv_data = mesh.uv_layers.active.data
    for tri in mesh.loop_triangles:
        vi = list(tri.vertices)
        li = list(tri.loops)
        uv0 = uv_data[li[0]].uv
        uv1 = uv_data[li[1]].uv
        uv2 = uv_data[li[2]].uv
        u = np.array([[uv0[0], uv0[1]], [uv1[0], uv1[1]], [uv2[0], uv2[1]]], dtype=np.float32) * size
        low = np.maximum(0, np.floor(u.min(axis=0) - 2.0).astype(int))
        high = np.minimum(size - 1, np.ceil(u.max(axis=0) + 2.0).astype(int))
        if high[0] < low[0] or high[1] < low[1]:
            continue
        a, b, c = u
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-8:
            continue

        area2 = abs(den)
        l0 = max(math.hypot(float(b[0] - c[0]), float(b[1] - c[1])), 1e-4)
        l1 = max(math.hypot(float(c[0] - a[0]), float(c[1] - a[1])), 1e-4)
        l2 = max(math.hypot(float(a[0] - b[0]), float(a[1] - b[1])), 1e-4)

        p0, p1, p2 = coords[vi[0]], coords[vi[1]], coords[vi[2]]

        xx, yy = np.meshgrid(np.arange(low[0], high[0] + 1) + 0.5, np.arange(low[1], high[1] + 1) + 0.5)
        w0 = ((b[1] - c[1]) * (xx - c[0]) + (c[0] - b[0]) * (yy - c[1])) / den
        w1 = ((c[1] - a[1]) * (xx - c[0]) + (a[0] - c[0]) * (yy - c[1])) / den
        w2 = 1.0 - w0 - w1

        px_d0 = w0 * (area2 / l0)
        px_d1 = w1 * (area2 / l1)
        px_d2 = w2 * (area2 / l2)
        min_px_d = np.minimum(np.minimum(px_d0, px_d1), px_d2)
        # Cap interior priority at 0.0 so interior texels always beat negative gutter texels
        priority = np.minimum(min_px_d, 0.0)

        sl_y = slice(low[1], high[1] + 1)
        sl_x = slice(low[0], high[0] + 1)
        cur_best = best_px_d[sl_y, sl_x]
        inside = priority > cur_best
        if not inside.any():
            continue

        # Clamp extrapolated barycentrics gently in the 1.85-pixel gutter so 3D positions stay on the local surface
        w0_c = np.clip(w0[inside], -0.35, 1.35)
        w1_c = np.clip(w1[inside], -0.35, 1.35)
        w2_c = np.clip(w2[inside], -0.35, 1.35)
        w_sum = np.maximum(w0_c + w1_c + w2_c, 1e-6)
        w0_i = (w0_c / w_sum)[:, None]
        w1_i = (w1_c / w_sum)[:, None]
        w2_i = (w2_c / w_sum)[:, None]

        pts = w0_i * p0 + w1_i * p1 + w2_i * p2
        nrms = w0_i * loop_normals[li[0]] + w1_i * loop_normals[li[1]] + w2_i * loop_normals[li[2]]
        n_len = np.linalg.norm(nrms, axis=-1, keepdims=True)
        nrms = nrms / np.maximum(n_len, 1e-6)
        tu_i = w0_i * loop_tangents[li[0]] + w1_i * loop_tangents[li[1]] + w2_i * loop_tangents[li[2]]
        tv_i = w0_i * loop_bitangents[li[0]] + w1_i * loop_bitangents[li[1]] + w2_i * loop_bitangents[li[2]]
        sc_i = (w0_i[:, 0] * scute_vert[vi[0]] + w1_i[:, 0] * scute_vert[vi[1]] + w2_i[:, 0] * scute_vert[vi[2]])

        pos_buf[sl_y, sl_x][inside] = pts
        nrm_buf[sl_y, sl_x][inside] = nrms
        tu_buf[sl_y, sl_x][inside] = tu_i
        tv_buf[sl_y, sl_x][inside] = tv_i
        scute_buf[sl_y, sl_x][inside] = sc_i
        best_px_d[sl_y, sl_x][inside] = priority[inside]
        mask[sl_y, sl_x] |= inside

    # Evaluate 3D pigment, roughness, and tangent-space normal vectors on all valid texels in chunks
    albedo_rgba = np.zeros((size, size, 4), dtype=np.float32)
    normal_rgba = np.zeros((size, size, 4), dtype=np.float32)
    rough_rgba = np.zeros((size, size, 4), dtype=np.float32)
    normal_rgba[..., 2] = 1.0
    normal_rgba[..., 3] = 1.0

    vy, vx = np.where(mask)
    total_valid = len(vy)
    chunk = 400000
    eps = 0.0014  # 1.4mm finite-difference step in 3D space
    normal_strength = 1.95

    for start in range(0, total_valid, chunk):
        end = min(start + chunk, total_valid)
        cy_idx = vy[start:end]
        cx_idx = vx[start:end]
        pts = pos_buf[cy_idx, cx_idx]
        nrms = nrm_buf[cy_idx, cx_idx]
        tu = tu_buf[cy_idx, cx_idx]
        tv = tv_buf[cy_idx, cx_idx]
        sc = scute_buf[cy_idx, cx_idx]

        # Gram-Schmidt orthogonalize tangent & bitangent against interpolated normal
        tu = tu - np.sum(tu * nrms, axis=-1, keepdims=True) * nrms
        tu = tu / np.maximum(np.linalg.norm(tu, axis=-1, keepdims=True), 1e-6)
        tv = tv - np.sum(tv * nrms, axis=-1, keepdims=True) * nrms
        tv = tv / np.maximum(np.linalg.norm(tv, axis=-1, keepdims=True), 1e-6)

        rgb, rough, h0 = evaluate_pigment_and_roughness(pts, nrms, sc, profile_arr)
        hu, _, _ = evaluate_scale_field(pts + eps * tu, nrms, sc)
        hv, _, _ = evaluate_scale_field(pts + eps * tv, nrms, sc)

        dh_du = (hu - h0) * normal_strength
        dh_dv = (hv - h0) * normal_strength
        ts_n = np.stack((-dh_du, -dh_dv, np.ones_like(h0)), axis=-1)
        ts_n = ts_n / np.maximum(np.linalg.norm(ts_n, axis=-1, keepdims=True), 1e-6)

        albedo_rgba[cy_idx, cx_idx, :3] = rgb
        albedo_rgba[cy_idx, cx_idx, 3] = 1.0
        normal_rgba[cy_idx, cx_idx, :3] = ts_n * 0.5 + 0.5
        normal_rgba[cy_idx, cx_idx, 3] = 1.0
        rough_rgba[cy_idx, cx_idx, :3] = rough[:, None]
        rough_rgba[cy_idx, cx_idx, 3] = 1.0

    # Dilate 10 pixels outside UV islands so mipmaps never bleed background colors
    albedo_rgba, dmask = _dilate_map(albedo_rgba, mask, iterations=10)
    normal_rgba, _ = _dilate_map(normal_rgba, mask, iterations=10)
    rough_rgba, _ = _dilate_map(rough_rgba, mask, iterations=10)

    albedo_rgba[~dmask] = (0.068, 0.062, 0.044, 1.0)
    normal_rgba[~dmask] = (0.5, 0.5, 1.0, 1.0)
    rough_rgba[~dmask] = (0.58, 0.58, 0.58, 1.0)

    def _save_image(img_name, filename, pixels, non_color=False):
        old = bpy.data.images.get(img_name)
        if old is not None:
            bpy.data.images.remove(old)
        img = bpy.data.images.new(img_name, width=size, height=size)
        if non_color:
            img.colorspace_settings.name = 'Non-Color'
        img.pixels.foreach_set(pixels.ravel())
        img.filepath_raw = str(out / filename)
        img.file_format = 'PNG'
        img.save()
        tex_dir = out / 'textures'
        if tex_dir.exists():
            img_copy = out / 'textures' / filename
            img_copy.write_bytes((out / filename).read_bytes())
        img.pack()
        return img

    img_pigment = _save_image('monitor_anatomical_pigment', 'body_pigment.png', albedo_rgba, non_color=False)
    _save_image('monitor_albedo', 'surface_albedo.png', albedo_rgba, non_color=False)
    img_normal = _save_image('monitor_normal', 'surface_normal.png', normal_rgba, non_color=True)
    img_rough = _save_image('monitor_roughness', 'surface_roughness.png', rough_rgba, non_color=True)

    # Build clean PBR material node tree without double-multiplying vertex color
    own = mat.copy()
    own.name = 'MonitorAnatomicalPigmentPBR'
    nt = own.node_tree
    nt.nodes.clear()
    out_node = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out_node.inputs['Surface'])

    tex_alb = nt.nodes.new('ShaderNodeTexImage')
    tex_alb.image = img_pigment
    nt.links.new(tex_alb.outputs['Color'], bsdf.inputs['Base Color'])

    tex_rgh = nt.nodes.new('ShaderNodeTexImage')
    tex_rgh.image = img_rough
    nt.links.new(tex_rgh.outputs['Color'], bsdf.inputs['Roughness'])

    tex_nrm = nt.nodes.new('ShaderNodeTexImage')
    tex_nrm.image = img_normal
    nm_node = nt.nodes.new('ShaderNodeNormalMap')
    nm_node.inputs['Strength'].default_value = 0.85
    nt.links.new(tex_nrm.outputs['Color'], nm_node.inputs['Color'])
    nt.links.new(nm_node.outputs['Normal'], bsdf.inputs['Normal'])

    if 'Specular IOR Level' in bsdf.inputs:
        bsdf.inputs['Specular IOR Level'].default_value = 0.58
    if 'Coat Weight' in bsdf.inputs:
        bsdf.inputs['Coat Weight'].default_value = 0.22
        bsdf.inputs['Coat Roughness'].default_value = 0.26

    body.data.materials.clear()
    body.data.materials.append(own)
    print('MONITOR_PIGMENT_BAKED 2048x2048 seam-free 3D tangent-space scales & ocellated rosettes', flush=True)
