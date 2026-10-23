"""Blender 5.2 Studio Renderer for Side-by-Side Comparison.

Calibrated to the exact Y-up, -Z forward coordinate system of Vivarium guardians:
- Head: Z ~ -1.1 to -1.35
- Torso: Z ~ -0.5 to +0.45
- Tail: Z ~ +0.45 to +2.65
- Back / Dorsum: +Y
- Belly: -Y
"""

import bpy, math, os, sys
from pathlib import Path
from mathutils import Vector, Matrix

OUT_DIR = Path(r"E:\Vivarium\scratch\monitor_realism\renders")
OUT_DIR.mkdir(parents=True, exist_ok=True)

BASELINE_GLB = r"E:\Vivarium\game\assets\guardians\great_ground\guardian.glb"
REALISTIC_GLB = r"E:\Vivarium\game\assets\guardians\great_ground\guardian.glb"

def setup_studio():
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.eevee.taa_render_samples = 32
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 960
    scene.render.resolution_percentage = 100

    # World
    if not scene.world:
        scene.world = bpy.data.worlds.new('StudioWorld')
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs[0].default_value = (0.04, 0.045, 0.055, 1.0) # Sleek slate studio backdrop
        bg.inputs[1].default_value = 0.85

    # Studio Floor at ground level (Y ~ 0)
    bpy.ops.mesh.primitive_plane_add(size=30, location=(0, -0.01, 0.5))
    floor = bpy.context.object
    floor.name = 'StudioFloor'
    # In Y-up, ground plane normal is (0, 1, 0)
    # Default plane is in XY (normal Z), so rotate 90 deg about X
    floor.rotation_euler = (math.pi / 2, 0, 0)

    mat_floor = bpy.data.materials.new('FloorMat')
    mat_floor.use_nodes = True
    p_floor = mat_floor.node_tree.nodes.get('Principled BSDF')
    if p_floor:
        p_floor.inputs['Base Color'].default_value = (0.10, 0.11, 0.13, 1.0)
        p_floor.inputs['Roughness'].default_value = 0.80
    floor.data.materials.append(mat_floor)

    # 3-Point Lighting tuned for Y-up world
    def create_light(name, loc, target, energy, size, color=(1,1,1)):
        ld = bpy.data.lights.new(name, 'AREA')
        ld.energy = energy
        ld.shape = 'DISK'
        ld.size = size
        ld.color = color
        lo = bpy.data.objects.new(name, ld)
        bpy.context.collection.objects.link(lo)
        lo.location = loc
        # Aim at target in Y-up world
        fwd = (Vector(target) - lo.location).normalized()
        up = Vector((0, 1, 0))
        right = fwd.cross(up).normalized()
        up = right.cross(fwd).normalized()
        lo.rotation_euler = Matrix((right, up, -fwd)).transposed().to_euler()
        return lo

    create_light('Key', (2.2, 2.8, -1.8), (0, 0.25, 0.2), 750, 3.0, (1.0, 0.98, 0.94))
    create_light('Fill', (-2.8, 1.8, -1.0), (0, 0.25, 0.2), 400, 4.0, (0.85, 0.92, 1.0))
    create_light('Rim', (0.0, 3.2, 2.5), (0, 0.25, 0.2), 850, 2.5, (1.0, 0.94, 0.82))

    # Cameras
    cam_data = bpy.data.cameras.new('StudioCam')
    cam_data.lens = 70.0
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

    # 1. Perspective Three-Quarter View (Looking from front-right-above)
    # Head is at -Z, tail at +Z, dorsum at +Y
    aim_cam(Vector((1.7, 1.4, -2.1)), Vector((0.0, 0.25, -0.2)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_perspective.png")
    bpy.ops.render.render(write_still=True)

    # 2. Top Overhead Dorsal View (Looking down +Y to -Y)
    # Head at top of image (-Z), tail at bottom (+Z)
    aim_cam(Vector((0.0, 3.8, 0.4)), Vector((0.0, 0.2, 0.4)), up_hint=(0, 0, -1))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_top.png")
    bpy.ops.render.render(write_still=True)

    # 3. Head & Cranium Portrait (Tight zoom on skull, eyes, canthus, nostrils)
    # Skull center: (0.0, 0.28, -1.12)
    aim_cam(Vector((0.42, 0.52, -1.65)), Vector((0.0, 0.28, -1.12)), up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_head.png")
    bpy.ops.render.render(write_still=True)

    # 4. Articulated Paws & Claws Closeup (Front left foot at X ~ 0.39, Y ~ 0.05, Z ~ -0.57)
    paw = Vector((0.39, 0.05, -0.57))
    aim_cam(paw + Vector((0.30, 0.28, -0.25)), paw, up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_claw.png")
    bpy.ops.render.render(write_still=True)

    # 5. Caudal Swimming Keel / Tail Profile (Tail mid-span Z ~ 1.3, Y ~ 0.22)
    tail = Vector((0.0, 0.22, 1.30))
    aim_cam(Vector((1.3, 0.65, 1.10)), tail, up_hint=(0, 1, 0))
    scene.render.filepath = str(OUT_DIR / f"{prefix}_tail.png")
    bpy.ops.render.render(write_still=True)

    print(f"Finished calibrated renders for {prefix}!")

def main():
    if not (OUT_DIR / "baseline_head.png").exists():
        render_model(BASELINE_GLB, "baseline")
    render_model(REALISTIC_GLB, "realistic")
    print("ALL CALIBRATED STUDIO RENDERS COMPLETED!")

if __name__ == '__main__':
    main()
