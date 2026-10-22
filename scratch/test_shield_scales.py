import numpy as np
from PIL import Image

def generate_shield_scales(size=2048, n_u=100, n_v=140):
    u = np.linspace(0, 1, size, endpoint=False)
    v = np.linspace(0, 1, size, endpoint=False)
    U, V = np.meshgrid(u, v)

    # We evaluate two overlapping staggered layers:
    # Layer 0: even rows
    # Layer 1: odd rows
    # Each scale has a shield profile that extends posteriorly (dy > 0) to overlap the row beneath it.

    v_scaled = V * n_v
    row_fl = np.floor(v_scaled)
    v_cell = v_scaled - row_fl # [0, 1)

    # Let's compute scale height for any given cell offset (dx, dy)
    def single_scale_height(dx, dy):
        # dx in [-0.5, 0.5], dy can extend from -0.40 to +0.65 (overlapping next row)
        # Shield width as function of dy
        # Upper (anterior) part: broad, rounded corners
        # Lower (posterior) part: tapers to a shield apex at dy = +0.55
        dy_norm = np.clip((dy + 0.35) / 0.90, 0.0, 1.0) # 0 at anterior, 1 at posterior tip
        
        # Shield width: broad at anterior (0.48), tapering toward tip (0.0 at dy=0.55)
        w = np.where(
            dy < 0.0,
            0.46 * (1.0 - 0.15 * (np.abs(dy) / 0.35)**2),
            0.46 * np.maximum(0.0, 1.0 - (np.maximum(0.0, dy) / 0.55)**1.4)
        )
        
        # Lateral distance: 1 at center, 0 at edge
        d_lat = np.maximum(0.0, 1.0 - np.abs(dx) / np.maximum(1e-4, w))
        
        # Inside shield mask
        in_shield = (np.abs(dx) < w) & (dy >= -0.35) & (dy <= 0.55)
        
        # Longitudinal ramp: slopes upward toward posterior tip, then drops
        # Imbrication ramp: rises steadily from 0.2 at anterior to 1.0 at dy=0.50, then sharp drop at 0.55
        ramp = 0.25 + 0.75 * np.clip((dy + 0.35) / 0.85, 0.0, 1.0)**0.9
        edge_bevel = d_lat ** 0.5
        
        # Central raised keel (ridge)
        keel_w = 0.08
        keel = 0.35 * np.maximum(0.0, 1.0 - np.abs(dx) / keel_w)**1.5 * ramp
        
        h = np.where(in_shield, (edge_bevel * 0.45 + ramp * 0.55) + keel, 0.0)
        return h

    # Current scale (center at dy = 0)
    u_scaled_curr = U * n_u + (row_fl % 2) * 0.5
    col_curr = np.floor(u_scaled_curr)
    dx_curr = (u_scaled_curr - col_curr) - 0.5
    dy_curr = v_cell - 0.40 # shift so scale center is around 0.40

    h_curr = single_scale_height(dx_curr, dy_curr)

    # Previous row scale (overlapping into current cell from above: row_fl - 1)
    # The previous row has staggered column offset: ((row_fl - 1) % 2) * 0.5
    u_scaled_prev = U * n_u + ((row_fl - 1) % 2) * 0.5
    col_prev = np.floor(u_scaled_prev)
    dx_prev = (u_scaled_prev - col_prev) - 0.5
    dy_prev = (v_cell + 1.0) - 0.40 # dy is 1.0 larger!

    h_prev = single_scale_height(dx_prev, dy_prev)

    # Next row scale (for bottom edges)
    u_scaled_next = U * n_u + ((row_fl + 1) % 2) * 0.5
    col_next = np.floor(u_scaled_next)
    dx_next = (u_scaled_next - col_next) - 0.5
    dy_next = (v_cell - 1.0) - 0.40

    h_next = single_scale_height(dx_next, dy_next)

    # True imbricate height is the maximum of overlapping layers
    h_total = np.maximum(h_curr, np.maximum(h_prev, h_next))

    # Add micro-keratin striations along the scales
    micro_keel = 0.04 * np.sin(U * n_u * 30.0) * (h_total > 0.1)
    h_total = np.clip(h_total + micro_keel, 0.0, 1.5)
    h_total /= np.max(h_total)

    # Compute normal map
    gy, gx = np.gradient(h_total * 4.0)
    n = np.stack((-gx * 2.2, -gy * 2.2, np.ones_like(h_total)), axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    normal_rgb = (n * 0.5 + 0.5) * 255.0

    # Albedo texture: charcoal body with lighter amber/gold on shield keels and margins
    base_val = 0.14 + 0.32 * h_total + 0.18 * (h_prev > h_curr) # highlight overlapping tips
    albedo = np.stack((base_val * 0.85, base_val * 0.88, base_val * 0.72), axis=-1) * 255.0

    # Roughness: semi-glossy scale shields, rougher crevices
    roughness = (0.38 + 0.35 * (1.0 - h_total)) * 255.0
    roughness_rgb = np.stack((roughness, roughness, roughness), axis=-1)

    Image.fromarray(np.uint8(normal_rgb)).save("scratch/test_imbricate_normal.png")
    Image.fromarray(np.uint8(np.clip(albedo, 0, 255))).save("scratch/test_imbricate_albedo.png")
    Image.fromarray(np.uint8(h_total * 255)).save("scratch/test_imbricate_height.png")
    Image.fromarray(np.uint8(np.clip(roughness_rgb, 0, 255))).save("scratch/test_imbricate_roughness.png")
    print("Generated true imbricate shield scales successfully!")

if __name__ == "__main__":
    generate_shield_scales()
