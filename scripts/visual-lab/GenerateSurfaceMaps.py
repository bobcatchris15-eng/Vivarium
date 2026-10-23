#!/usr/bin/env python3
"""
GenerateSurfaceMaps.py - Surface PBR Map Synthesizer (WP-05)

Generates physical 16-bit Height/Displacement maps and 8-bit Cavity/Ambient Occlusion maps
from OpenGL normal maps via 2D FFT Poisson integration and multi-scale Horizon Ambient Occlusion (HBAO).
Also updates and configures Godot 4 .import files for normal-variance roughness mipmaps
and lossless/high-quality texture compression.
"""

import os
import sys
import re
import numpy as np
from PIL import Image
from pathlib import Path

def solve_poisson_height(normal_img_path: Path) -> np.ndarray:
    """
    Solves Poisson equation nabla^2 h = div(p, q) via 2D FFT:
    p = -n_x / n_z, q = -n_y / n_z.
    Recovers continuous surface elevation h(x,y) normalized to [0..1] with mean plane 0.5.
    """
    img = Image.open(normal_img_path).convert('RGB')
    arr = np.array(img, dtype=np.float32) / 255.0
    
    # Tangent-space normal unpacking (OpenGL convention: +X right, +Y up, +Z out)
    nx = arr[:, :, 0] * 2.0 - 1.0
    ny = arr[:, :, 1] * 2.0 - 1.0
    nz = arr[:, :, 2] * 2.0 - 1.0
    
    length = np.sqrt(nx**2 + ny**2 + nz**2)
    nx /= np.maximum(length, 1e-6)
    ny /= np.maximum(length, 1e-6)
    nz /= np.maximum(length, 1e-6)
    
    # Surface gradients: p = -nx/nz, q = -ny/nz
    p = -nx / np.maximum(nz, 1e-4)
    q = -ny / np.maximum(nz, 1e-4)
    
    # Grid dimensions
    H_dim, W_dim = p.shape
    ky = 2.0 * np.pi * np.fft.fftfreq(H_dim)
    kx = 2.0 * np.pi * np.fft.fftfreq(W_dim)
    KY, KX = np.meshgrid(ky, kx, indexing='ij')
    
    # In image coordinates: row index i increases downwards (-Y in GL), col index j increases right (+X)
    # div in image array coordinates: dp/dj + d(-q)/di = dp/dj - dq/di
    P = np.fft.fft2(p)
    Q = np.fft.fft2(q)
    div_fft = 1j * KX * P - 1j * KY * Q
    
    denom = -(KX**2 + KY**2)
    denom[0, 0] = 1.0  # Prevent zero division at DC component
    
    H = div_fft / denom
    H[0, 0] = 0.0      # Zero DC mean
    
    h = np.real(np.fft.ifft2(H))
    
    # Normalize to [0..1] with mean plane at 0.5
    h = h - np.mean(h)
    max_dev = max(abs(np.min(h)), abs(np.max(h)))
    if max_dev > 1e-7:
        h_norm = 0.5 + 0.499 * (h / max_dev)
    else:
        h_norm = np.full_like(h, 0.5)
        
    return np.clip(h_norm, 0.0, 1.0)


def compute_multiscale_hbao(h_norm: np.ndarray, num_dirs: int = 8, radii: list = None) -> np.ndarray:
    """
    Computes multi-scale Horizon Ambient Occlusion (HBAO / cavity depth) from elevation h(x,y).
    Depressions/crevices receive low AO (0.15-0.5), ridges/peaks receive 1.0.
    """
    if radii is None:
        # Scale radii according to resolution
        scale_fac = max(1, h_norm.shape[0] // 1024)
        radii = [max(1, int(r * scale_fac)) for r in [1, 2, 4, 8, 16, 24, 36]]
        radii = sorted(list(set(radii)))
        
    angles = np.linspace(0, 2.0 * np.pi, num_dirs, endpoint=False)
    dirs = [(np.cos(a), np.sin(a)) for a in angles]
    
    total_occlusion = np.zeros_like(h_norm, dtype=np.float32)
    h_physical = h_norm * 45.0  # Aspect ratio tuning for micro-relief
    
    for dx, dy in dirs:
        max_tan = np.zeros_like(h_norm, dtype=np.float32)
        for r in radii:
            rx = int(round(r * dx))
            ry = int(round(r * dy))
            if rx == 0 and ry == 0:
                continue
            dist = np.sqrt(rx * rx + ry * ry)
            h_sample = np.roll(np.roll(h_physical, -ry, axis=0), -rx, axis=1)
            slope = (h_sample - h_physical) / dist
            max_tan = np.maximum(max_tan, slope)
            
        sin_theta = max_tan / np.sqrt(1.0 + max_tan**2)
        total_occlusion += sin_theta
        
    ao = 1.0 - (total_occlusion / len(dirs))
    ao = np.clip(ao, 0.0, 1.0)
    
    # Map to [0.15 .. 1.0] with contrast curve
    ao_scaled = 0.15 + 0.85 * (ao ** 1.35)
    return np.clip(ao_scaled, 0.0, 1.0)


def create_import_config(file_path: Path, is_linear: bool = True, is_normal: bool = False, roughness_src: str = ""):
    """
    Creates or rewrites a Godot 4 .import file with required settings.
    """
    import_path = file_path.with_name(file_path.name + ".import")
    res_path = f"res://Textures/{file_path.name}"
    
    roughness_mode = 1 if roughness_src else 0
    normal_map_flag = 1 if is_normal else 0
    
    content = f"""[remap]

importer="texture"
type="CompressedTexture2D"

[deps]

source_file="{res_path}"
dest_files=[]

[params]

compress/mode=2
compress/high_quality=true
compress/lossy_quality=1.0
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map={normal_map_flag}
compress/channel_pack=0
mipmaps/generate=true
mipmaps/limit=-1
roughness/mode={roughness_mode}
roughness/src_normal="{roughness_src}"
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=0
srgb={0 if is_linear else 1}
"""
    with open(import_path, "w", encoding="utf-8") as f:
        f.write(content)


def update_existing_import_file(import_path: Path, updates: dict):
    """
    Updates key/value parameters in an existing Godot .import file, preserving headers and UID.
    """
    if not import_path.exists():
        return
        
    with open(import_path, "r", encoding="utf-8") as f:
        lines = f.readlines()
        
    new_lines = []
    seen_keys = set()
    
    for line in lines:
        stripped = line.strip()
        matched = False
        for k, v in updates.items():
            if stripped.startswith(f"{k}="):
                new_lines.append(f"{k}={v}\n")
                seen_keys.add(k)
                matched = True
                break
        if not matched:
            new_lines.append(line)
            
    # Add any missing parameters in [params] section
    for k, v in updates.items():
        if k not in seen_keys:
            new_lines.append(f"{k}={v}\n")
            
    with open(import_path, "w", encoding="utf-8") as f:
        f.writelines(new_lines)


def process_texture_directory(textures_dir: Path):
    print(f"Scanning surface textures in {textures_dir}...")
    
    # 1. Identify all normal maps
    normal_maps = sorted([p for p in textures_dir.iterdir() if "NormalGL" in p.name and not p.name.endswith(".import") and p.suffix.lower() in [".jpg", ".png"]])
    
    for nrm_path in normal_maps:
        print(f"--> Processing {nrm_path.name}...")
        stem = nrm_path.name.replace("_NormalGL.jpg", "").replace("_NormalGL.png", "")
        
        disp_path = textures_dir / f"{stem}_Displacement.png"
        ao_path = textures_dir / f"{stem}_AmbientOcclusion.png"
        
        # 1. Solve Poisson equation for elevation field
        h_norm = solve_poisson_height(nrm_path)
        
        # 2. Save 16-bit PNG Displacement map
        h_16bit = np.clip(h_norm * 65535.0, 0, 65535).astype(np.uint16)
        Image.fromarray(h_16bit, mode="I;16").save(disp_path)
        print(f"    Saved 16-bit heightfield: {disp_path.name}")
        create_import_config(disp_path, is_linear=True)
        
        # 3. Compute multi-scale HBAO
        ao_norm = compute_multiscale_hbao(h_norm)
        ao_8bit = np.clip(ao_norm * 255.0, 0, 255).astype(np.uint8)
        Image.fromarray(ao_8bit, mode="L").save(ao_path)
        print(f"    Saved 8-bit cavity AO: {ao_path.name}")
        create_import_config(ao_path, is_linear=True)
        
    # 2. Update existing .import files according to WP-05 specifications
    print("\nUpdating existing .import files...")
    
    # A. LichenThallus.png.import: ensure mipmaps/generate=true
    lichen_import = textures_dir / "LichenThallus.png.import"
    if lichen_import.exists():
        print("  Updating LichenThallus.png.import (mipmaps/generate=true)...")
        update_existing_import_file(lichen_import, {
            "mipmaps/generate": "true",
            "compress/high_quality": "true"
        })
        
    # B. All *_Color.*.import: ensure compress/high_quality=true, compress/lossy_quality=1.0
    for col_import in textures_dir.glob("*_Color.*.import"):
        print(f"  Updating {col_import.name} (high_quality=true, lossy_quality=1.0)...")
        update_existing_import_file(col_import, {
            "compress/high_quality": "true",
            "compress/lossy_quality": "1.0"
        })
        
    # C. All *_NormalGL.*.import: ensure compress/normal_map=1, compress/high_quality=true, compress/lossy_quality=1.0
    for nrm_import in textures_dir.glob("*_NormalGL.*.import"):
        print(f"  Updating {nrm_import.name} (normal_map=1, high_quality=true, lossy_quality=1.0)...")
        update_existing_import_file(nrm_import, {
            "compress/normal_map": "1",
            "compress/high_quality": "true",
            "compress/lossy_quality": "1.0"
        })
        
    # D. All *_Roughness.*.import: ensure roughness/mode=1 and roughness/src_normal points to corresponding NormalGL
    for rgh_import in textures_dir.glob("*_Roughness.*.import"):
        base_tex_name = rgh_import.name.replace(".import", "")
        # Find corresponding normal map
        nrm_target_jpg = base_tex_name.replace("Roughness", "NormalGL").replace(".png", ".jpg")
        nrm_target_png = base_tex_name.replace("Roughness", "NormalGL").replace(".jpg", ".png")
        
        matched_nrm = ""
        if (textures_dir / nrm_target_png).exists():
            matched_nrm = f"res://Textures/{nrm_target_png}"
        elif (textures_dir / nrm_target_jpg).exists():
            matched_nrm = f"res://Textures/{nrm_target_jpg}"
            
        print(f"  Updating {rgh_import.name} (roughness/mode=1, src_normal={matched_nrm})...")
        update_existing_import_file(rgh_import, {
            "roughness/mode": "1",
            "roughness/src_normal": f'"{matched_nrm}"',
            "compress/high_quality": "true",
            "compress/lossy_quality": "1.0"
        })


def main():
    repo_root = Path(__file__).resolve().parent.parent.parent
    textures_dir = repo_root / "game" / "Textures"
    if not textures_dir.exists():
        print(f"Error: Textures directory not found at {textures_dir}")
        sys.exit(1)
        
    process_texture_directory(textures_dir)
    print("\nGenerateSurfaceMaps complete.")


if __name__ == "__main__":
    main()
