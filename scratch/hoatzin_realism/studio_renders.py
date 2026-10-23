"""Blender 5.2 Studio Renderer for Side-by-Side Hoatzin (crested_canopy) Comparison.

Calibrated to the exact Y-up, -Z forward coordinate system of the Crested Canopy Hoatzin:
- Bill / Head / Sagittal Crest Fan: Z ~ -0.26 to -0.05, Y ~ 0.48 to 0.78
- Streaked Neck & Folivorous Crop / Breast: Z ~ -0.21 to +0.05, Y ~ 0.22 to 0.52
- Folded Wing Mantle, Coverts & Rufous Primaries: X ~ +-0.10 to +-0.22, Y ~ 0.18 to 0.40, Z ~ -0.10 to +0.34
- Tarsometatarsus & Perching Talons: X ~ +-0.068, Y ~ 0.00 to 0.24, Z ~ -0.09 to +0.07
- Graduated Banded Tail Fan (10-12 rectrices): Z ~ +0.16 to +0.62, Y ~ 0.08 to 0.28
"""

import bpy, math, os, sys
from pathlib import Path
from mathutils import Vector, Matrix

OUT_DIR = Path(r"E:\Vivarium\scratch\hoatzin_realism\renders")
OUT_DIR.mkdir(parents=True, exist_ok=True)

BASELINE_GLB = r"E:\Vivarium\scratch\hoatzin_realism\baseline\guardian_baseline.glb"
REALISTIC_GLB = r"E:\Vivarium\game\assets\guardians\crested_canopy\guardian.glb"


def setup_studio():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.eevee.taa_render_samples = 32
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 960
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = 'AgX'

    if not scene.world:
        scene.world = bpy.data.worlds.new('StudioWorld')
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs[0].default_value = (0.11, 0.125, 0.135, 1.0)
        bg.inputs[1].default_value = 0.85

    # Studio Floor at ground level (Y ~ -0.002)
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, -0.002, 0.1))
    floor = bpy.context.object
    floor.name = 'StudioFloor'
    floor.rotation_euler = (math.pi / 2, 0, 0)

    mat_floor = bpy.data.materials.new('FloorMat')
    mat_floor.use_nodes = True
    p_floor = mat_floor.node_tree.nodes.get('Principled BSDF')
    if p_floor:
        p_floor.inputs['Base Color'].default_value = (0.16, 0.175, 0.195, 1.0)
        p_floor.inputs['Roughness'].default_value = 0.78
    floor.data.materials.append(mat_floor)

    def create_light(name, loc, target, energy, size, color=(1.0, 1.0, 1.0)):
        ld = bpy.data.lights.new(name, 'AREA')
        ld.energy = energy
        ld.shape = 'DISK'
        ld.size = size
        ld.color = color
        lo = bpy.data.objects.new(name, ld)
        bpy.context.collection.objects.link(lo)
        lo.location = loc
        fwd = (Vector(target) - lo.location).normalized()
        up = Vector((0, 1, 0))
        right = fwd.cross(up).normalized()
        up = right.cross(fwd).normalized()
        lo.rotation_euler = Matrix((right, up, -fwd)).transposed().to_euler()
        return lo

    create_light('Key', (1.9, 2.6, -1.7), (0, 0.38, 0.04), 720, 3.0, (1.0, 0.98, 0.94))
    create_light('Fill', (-2.2, 1.7, -0.9), (0, 0.38, 0.04), 440, 3.6, (0.86, 0.93, 1.0))
    create_light('Rim', (0.3, 2.8, 2.0), (0, 0.35, 0.12), 780, 2.6, (1.0, 0.94, 0.85))

    cam_data = bpy.data.cameras.new('StudioCam')
    cam_data.lens = 50.0
    cam_data.sensor_width = 36.0
    cam = bpy.data.objects.new('StudioCam', cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam

    def aim_cam(loc, target, up_hint=(0, 1, 0)):
        cam.location = loc
        fwd = (Vector(target) - cam.location).normalized()
        up = Vector(up_hint)
        right = fwd.cross(up).normalized()
        up = right.cross(fwd).normalized()
        cam.rotation_euler = Matrix((right, up, -fwd)).transposed().to_euler()

    return scene, cam, aim_cam


def render_model(glb_path, prefix):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene, cam, aim_cam = setup_studio()

    print(f"Importing {glb_path}...")
    bpy.ops.import_scene.gltf(filepath=glb_path)

    # 1. Perspective Three-Quarter View (Full avian silhouette, crest to tail)
    aim_cam(Vector((1.48, 0.78, -1.18)), Vector((0.0, 0.38, -0.06)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_perspective.png")
    bpy.ops.render.render(write_still=True)

    # 2. Dorsal-Three-Quarter Mantle & Tail Fan View
    aim_cam(Vector((0.85, 1.45, 0.45)), Vector((0.0, 0.34, 0.14)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_top.png")
    bpy.ops.render.render(write_still=True)

    # 3. Head, Sagittal Punk Crest, Turquoise Orbital Skin & Ruby Eye Portrait
    aim_cam(Vector((0.42, 0.62, -0.78)), Vector((0.0, 0.56, -0.50)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_head.png")
    bpy.ops.render.render(write_still=True)

    # 4. Folded Wing Coverts, Buff Terminal Bars & Rufous Primaries Closeup
    aim_cam(Vector((0.68, 0.44, -0.14)), Vector((0.09, 0.30, 0.10)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_wing.png")
    bpy.ops.render.render(write_still=True)

    # 5. Avian Tarsometatarsus, Perching Talons & Banded Rectrix Fan Closeup
    aim_cam(Vector((0.78, 0.28, -0.05)), Vector((0.02, 0.15, 0.22)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_tail_feet.png")
    bpy.ops.render.render(write_still=True)

    print(f"Finished calibrated renders for {prefix}!")


def main():
    if not (OUT_DIR / "baseline_perspective.png").exists():
        render_model(BASELINE_GLB, "baseline")
    else:
        print("Baseline renders already exist, skipping baseline render pass.")
    render_model(REALISTIC_GLB, "realistic")
    print("ALL CALIBRATED HOATZIN STUDIO RENDERS COMPLETED!")


if __name__ == '__main__':
    main()
