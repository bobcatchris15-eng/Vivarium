"""Read-only donor audit and CPU previews. Run Blender with --disable-autoexec.

Never saves the loaded source, never runs embedded scripts, and writes only to
the explicitly supplied evidence directory. Originals stay outside Godot imports.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--input', type=Path, required=True)
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--render', action='store_true')
    args = ap.parse_args(sys.argv[sys.argv.index('--') + 1:])
    source = args.input.resolve()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)
    if source.suffix.lower() == '.blend':
        bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        if source.suffix.lower() == '.obj':
            bpy.ops.wm.obj_import(filepath=str(source))
        elif source.suffix.lower() == '.glb':
            bpy.ops.import_scene.gltf(filepath=str(source))
        else:
            raise ValueError('Unsupported donor format: ' + str(source))
    bpy.context.scene.frame_set(1)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    report = {'source': str(source), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
              'blender': bpy.app.version_string, 'meshes': [], 'rigs': [],
              'actions': [], 'images': [], 'embedded_texts_not_executed': list(bpy.data.texts.keys())}
    for obj in meshes:
        mesh = obj.data
        mesh.calc_loop_triangles()
        weighted = sum(any(g.weight > 0 for g in v.groups) for v in mesh.vertices)
        report['meshes'].append({'name': obj.name, 'vertices': len(mesh.vertices),
            'triangles': len(mesh.loop_triangles), 'uv_layers': len(mesh.uv_layers),
            'weighted_vertices': weighted, 'vertex_groups': len(obj.vertex_groups),
            'materials': [s.material.name if s.material else None for s in obj.material_slots],
            'modifiers': [m.type for m in obj.modifiers],
            'finite_vertices': all(math.isfinite(c) for v in mesh.vertices for c in v.co)})
    for obj in bpy.context.scene.objects:
        if obj.type == 'ARMATURE':
            report['rigs'].append({'name': obj.name, 'bones': len(obj.data.bones),
                                  'bone_names': list(obj.data.bones.keys())})
    for act in bpy.data.actions:
        report['actions'].append({'name': act.name, 'frame_range': list(act.frame_range)})
    for im in bpy.data.images:
        report['images'].append({'name': im.name, 'size': list(im.size),
                                'packed': bool(im.packed_file), 'path': im.filepath})
    if not meshes:
        raise RuntimeError('No donor meshes')
    points = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    center = (low + high) * .5
    extent = max(high - low)
    report['bounds'] = {'min': list(low), 'max': list(high), 'size': list(high - low)}
    (out / 'audit.json').write_text(json.dumps(report, indent=2))
    if not args.render:
        return
    scene = bpy.context.scene
    # Imported lamps, cameras and authoring helpers must not contaminate review.
    for obj in list(scene.objects):
        if obj.type not in {'MESH', 'ARMATURE'}:
            obj.hide_render = True
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 16
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new('DonorReviewBlack')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (0, 0, 0, 1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0
    scene.view_settings.view_transform = 'AgX'
    clay = bpy.data.materials.new('DonorReviewClay')
    clay.diffuse_color = (.32, .40, .35, 1)
    scene.view_layers[0].material_override = clay
    for name, direction, power, color in [
        ('key', (1, -2, 3), 120, (1, .88, .7)),
        ('fill', (-2, -1, 1), 65, (.65, .8, 1)),
        ('rim', (0, 2, 2), 150, (1, 1, .85))]:
        light = bpy.data.lights.new(name, 'AREA')
        light.energy = power * extent * extent
        light.color = color
        light.shape = 'DISK'
        light.size = extent * 1.5
        obj = bpy.data.objects.new(name, light)
        scene.collection.objects.link(obj)
        obj.location = center + Vector(direction) * extent
        obj.rotation_euler = (center - obj.location).to_track_quat('-Z', 'Y').to_euler()
    cam = bpy.data.objects.new('DonorReviewCamera', bpy.data.cameras.new('DonorReviewCamera'))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = extent * 1.45
    cam.data.clip_start = max(.00001, extent * .0001)
    cam.data.clip_end = extent * 100
    for name, direction in [('threequarter', (1.5, -2.3, 1.1)), ('side', (0, -3, .4))]:
        cam.location = center + Vector(direction) * extent
        cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(out / (name + '-clay.png'))
        bpy.ops.render.render(write_still=True)
    print('CC0_DONOR_REVIEW_OK ' + str(source), flush=True)


if __name__ == '__main__':
    main()
