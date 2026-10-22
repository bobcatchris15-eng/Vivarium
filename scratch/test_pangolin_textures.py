import numpy as np
from PIL import Image
import math

n = 2048
y, x = np.mgrid[0:n, 0:n] / n

cells_x, cells_y = 4, 4
cell_col = np.floor(x * cells_x).astype(int)
cell_row = np.floor(y * cells_y).astype(int)
cell_id = cell_col + cell_row * cells_x

u = (x * cells_x) % 1.0
v = (y * cells_y) % 1.0

rng = np.random.default_rng(2026)
cell_seeds = rng.uniform(0.0, 100.0, (16,))
# Subtle individual scale variation (some more golden, some darker sepia)
cell_tint = np.array([
    [1.00, 1.00, 1.00], [1.08, 1.04, 0.96], [0.94, 0.92, 0.95], [1.12, 1.06, 0.94],
    [0.96, 0.98, 1.02], [1.05, 1.02, 0.98], [0.92, 0.90, 0.92], [1.10, 1.05, 0.96],
    [1.02, 1.01, 0.99], [0.95, 0.94, 0.96], [1.07, 1.03, 0.97], [0.98, 0.97, 1.01],
    [1.14, 1.08, 0.95], [0.93, 0.91, 0.94], [1.04, 1.02, 0.98], [1.00, 1.00, 1.00]
])
seed = cell_seeds[cell_id]

# Centered transverse coordinate [-1, 1]
s = (u - 0.5) * 2.0
dist_center = np.abs(s)

# Fanning longitudinal coordinate (radiates toward rounded trailing edge)
fan_u = s * (1.0 + 0.35 * v)

# Primary longitudinal ridges (7-9 dominant keels/striae per scale)
ridge_freq = 7.0 + (cell_id % 3) * 0.5
primary_ridges = np.cos(fan_u * math.pi * ridge_freq) * 0.5 + 0.5
primary_ridges = primary_ridges ** 2.0

# Central keel ridge (stronger down the scale midline)
central_keel = np.exp(-((s * 3.2) ** 2)) * np.sin(v * math.pi * 0.95)

# Fine micro-fibrous keratin striae (30-40 fine longitudinal fibers)
fine_freq = 38.0 + (cell_id % 5) * 2.0
fine_striae = np.sin(fan_u * math.pi * fine_freq + np.sin(v * 26.0 + seed) * 0.3) * 0.5 + 0.5

# Growth increments / rings (concentric curved bands)
ring_curve = v - 0.16 * (s ** 2)
growth_rings = np.sin(ring_curve * math.pi * 16.0 + seed) * 0.5 + 0.5

# Worn trailing margin (top 26% of scale length)
edge_wear = np.clip((v - 0.70) / 0.28, 0.0, 1.0) ** 1.3
edge_notches = np.sin(s * math.pi * 7.0 + seed) * np.sin(s * math.pi * 17.0)
edge_wear = np.clip(edge_wear + edge_wear * 0.20 * edge_notches, 0.0, 1.0)

# Dusty savannah grime in recessed grooves and crevices
dust_pattern = (1.0 - primary_ridges) * np.clip((v - 0.12) / 0.75, 0.0, 1.0)
dust_noise = np.sin(u * math.pi * 53.0 + v * math.pi * 37.0 + seed) * 0.5 + 0.5
dust = np.clip(dust_pattern * (0.65 + 0.35 * dust_noise) * (1.0 - edge_wear * 0.75), 0.0, 1.0)

# Exact reference colors in LINEAR RGB
c_root = np.array([0.024, 0.015, 0.010])    # Deep charcoal sepia
c_body = np.array([0.082, 0.052, 0.033])    # Warm earthy golden walnut
c_ridge = np.array([0.165, 0.115, 0.075])   # Warm caramel amber ridges
c_edge = np.array([0.340, 0.280, 0.215])    # Weathered pale ivory/horn at rim
c_dust = np.array([0.190, 0.145, 0.090])    # Arid savannah dust

# Linear RGB composition
root_blend = np.clip(v / 0.42, 0.0, 1.0)[:, :, None]
base_grad = c_root[None, None, :] * (1.0 - root_blend) + c_body[None, None, :] * root_blend

ridge_blend = (primary_ridges * 0.42 + fine_striae * 0.12 + central_keel * 0.25)[:, :, None]
albedo = base_grad * (1.0 - ridge_blend * 0.5) + c_ridge[None, None, :] * ridge_blend
albedo *= (0.92 + 0.12 * growth_rings)[:, :, None]
albedo = albedo * (1.0 - dust[:, :, None] * 0.45) + c_dust[None, None, :] * (dust[:, :, None] * 0.45)
albedo = albedo * (1.0 - edge_wear[:, :, None]) + c_edge[None, None, :] * edge_wear[:, :, None]

# Per-cell tint
albedo = np.clip(albedo * cell_tint[cell_id], 0.0, 1.0)

# Convert linear RGB to sRGB for saving the PNG byte image!
srgb = np.where(albedo <= 0.0031308, 12.92 * albedo, 1.055 * (np.maximum(albedo, 0) ** (1 / 2.4)) - 0.055)
srgb = np.clip(srgb, 0.0, 1.0)

# Roughness
roughness = np.clip(0.52 - 0.16 * edge_wear - 0.10 * primary_ridges + 0.28 * dust + 0.04 * fine_striae, 0.32, 0.88)

# Normal Map
height = (
    0.016 * (1.0 - np.clip(dist_center, 0.0, 1.0) ** 1.8) * np.sin(v * math.pi * 0.95)
    + 0.0055 * primary_ridges * (1.0 - edge_wear * 0.35)
    + 0.0035 * central_keel
    + 0.0012 * fine_striae
    + 0.0016 * growth_rings
    - 0.0035 * edge_wear
)
dy, dx = np.gradient(height * 380.0)
nx = -dx; ny = -dy; nz = np.ones_like(nx)
norm = np.sqrt(nx * nx + ny * ny + nz * nz)
normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)

Image.fromarray((srgb * 255).astype(np.uint8)).save("scratch/test_keratin_albedo_fixed.png")
Image.fromarray((roughness * 255).astype(np.uint8)).save("scratch/test_keratin_roughness_fixed.png")
Image.fromarray((normals * 255).astype(np.uint8)).save("scratch/test_keratin_normal_fixed.png")
print("Saved fixed textures!")
