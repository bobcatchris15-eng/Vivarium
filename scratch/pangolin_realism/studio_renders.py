"""Blender 5.2 Studio Renderer for Side-by-Side Pangolin Comparison.

Calibrated to the exact Y-up, -Z forward coordinate system of the Armored Forest Pangolin:
- Snout / Head: Z ~ -0.52 to -0.25, Y ~ 0.08 to 0.28
- Thoracic / Lumbar Carapace Dome: Z ~ -0.20 to +0.42, Y ~ 0.32 to 0.63
- Tail Armor & Serrated Margin: Z ~ +0.45 to +0.96, Y ~ 0.02 to 0.35
- Front Left Fossorial Paw & Claws: X ~ +0.165, Y ~ 0.04, Z ~ -0.13
"""

import bpy, math, os, sys
from pathlib import Path
from mathutils import Vector, Matrix

OUT_DIR = Path(r"E:\Vivarium\scratch\pangolin_realism\renders")
OUT_DIR.mkdir(parents=True, exist_ok=True)

BASELINE_GLB = r"E:\Vivarium\scratch\pangolin_realism\baseline\guardian_baseline.glb"
REALISTIC_GLB = r"E:\Vivarium\game\assets\guardians\armored_forest\guardian.glb"


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
        bg.inputs[0].default_value = (0.11, 0.12, 0.135, 1.0)
        bg.inputs[1].default_value = 0.85

    # Studio Floor at ground level (Y ~ -0.005)
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, -0.005, 0.2))
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

    create_light('Key', (2.0, 2.8, -1.8), (0, 0.26, 0.12), 720, 3.2, (1.0, 0.98, 0.94))
    create_light('Fill', (-2.4, 1.8, -0.8), (0, 0.26, 0.12), 420, 3.8, (0.86, 0.92, 1.0))
    create_light('Rim', (0.4, 3.0, 2.2), (0, 0.26, 0.12), 820, 2.6, (1.0, 0.94, 0.84))

    cam_data = bpy.data.cameras.new('StudioCam')
    cam_data.lens = 65.0
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

    # 1. Perspective Three-Quarter View (Full body armored silhouette)
    aim_cam(Vector((1.35, 0.92, -1.25)), Vector((0.0, 0.27, 0.14)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_perspective.png")
    bpy.ops.render.render(write_still=True)

    # 2. Dorsal Overhead View (Looking down +Y to -Y at imbricated scale lattice)
    aim_cam(Vector((0.0, 2.55, 0.20)), Vector((0.0, 0.25, 0.20)), up_hint=(0, 0, -1))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_top.png")
    bpy.ops.render.render(write_still=True)

    # 3. Cranial, Rhinarium & Hooded Eye Portrait
    aim_cam(Vector((0.38, 0.34, -0.82)), Vector((0.0, 0.16, -0.38)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_head.png")
    bpy.ops.render.render(write_still=True)

    # 4. Fossorial Forelimb & Sickle Digging Claws Closeup
    paw = Vector((0.165, 0.08, -0.12))
    aim_cam(paw + Vector((0.28, 0.20, -0.28)), paw, up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_claw.png")
    bpy.ops.render.render(write_still=True)

    # 5. Armored Paddle Tail & Serrated Lateral Margin
    tail = Vector((0.0, 0.18, 0.66))
    aim_cam(Vector((0.78, 0.52, 0.42)), tail, up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_tail.png")
    bpy.ops.render.render(write_still=True)

    print(f"Finished calibrated renders for {prefix}!")


def main():
    if not (OUT_DIR / "baseline_head.png").exists():
        render_model(BASELINE_GLB, "baseline")
    render_model(REALISTIC_GLB, "realistic")
    print("ALL CALIBRATED PANGOLIN STUDIO RENDERS COMPLETED!")


if __name__ == '__main__':
    main()
