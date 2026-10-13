"""Shared guardian surface/face pass; invoked by each reproducible authoring script."""
import bpy
import math
from mathutils import Vector

EYES = {
    'crested_canopy': (0.063, 0.525, -0.55, 0.015, (0.65, 0.22, 0.08, 1)),
    'armored_forest': (0.084, 0.25, -0.55, 0.014, (0.29, 0.15, 0.07, 1)),
    'canopy_serpent': (0.082, 0.185, -1.12, 0.018, (0.75, 0.44, 0.08, 1)),
    'great_ground': (0.088, 0.275, -1.05, 0.019, (0.63, 0.40, 0.08, 1)),
    'woodland_omnivore': (0.081, 0.44, -0.48, 0.019, (0.26, 0.12, 0.055, 1)),
    'quilled_arboreal': (0.091, 0.28, -0.42, 0.018, (0.21, 0.12, 0.06, 1)),
}

def reset_pose(armature):
    # Each clip must own all channels: otherwise sleep translations and curls
    # survive transitions into walking or feeding on sparsely keyed bones.
    for bone in armature.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.location = (0, 0, 0)
        bone.rotation_euler = (0, 0, 0)
        bone.scale = (1, 1, 1)
        for channel in ('location', 'rotation_euler', 'scale'):
            bone.keyframe_insert(channel, frame=1)
            bone.keyframe_insert(channel, frame=40)

def material(name, color, roughness):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Roughness'].default_value = roughness
    return mat

def sphere(name, center, radii, mat, armature):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1, location=center)
    obj = bpy.context.object
    obj.name = name
    # Bake all coordinates before skinning, so glTF bind matrices are unambiguous.
    for vertex in obj.data.vertices:
        vertex.co = Vector(center) + Vector((vertex.co.x*radii[0], vertex.co.y*radii[1], vertex.co.z*radii[2]))
    obj.location = (0, 0, 0)
    obj.data.materials.append(mat)
    for face in obj.data.polygons: face.use_smooth = True
    obj.parent = armature
    obj.vertex_groups.new(name='Head').add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
    mod = obj.modifiers.new('FaceSkin', 'ARMATURE'); mod.object = armature

def polish(archetype):
    armature = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
    # The old sleep curls rotate successive local bone axes as if they were
    # world axes, tipping whole animals nose-down and raising their tails.
    # Use a stable supported rest until species-specific curls have a solved rig.
    old_rest = bpy.data.actions.get('rest')
    if old_rest:
        if armature.animation_data.action == old_rest: armature.animation_data.action = None
        bpy.data.actions.remove(old_rest)
    rest = bpy.data.actions.new('rest')
    armature.animation_data.action = rest
    reset_pose(armature)
    for frame in (1, 10, 20, 30, 40):
        phase = (frame-1)/39 * math.tau
        chest = armature.pose.bones.get('Chest') or armature.pose.bones.get('Spine1')
        if chest:
            chest.scale = (1+math.sin(phase)*0.008, 1, 1+math.sin(phase)*0.008)
            chest.keyframe_insert('scale', frame=frame)
        head = armature.pose.bones.get('Head')
        if head:
            head.rotation_euler = (math.sin(phase)*0.015, 0, 0)
            head.keyframe_insert('rotation_euler', frame=frame)
    bodies = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    for body in bodies:
        if archetype in ('woodland_omnivore', 'great_ground'):
            # Apply subdivision before deformation; preserve skin groups and colors.
            bpy.context.view_layer.objects.active = body
            mod = body.modifiers.new('RoundedAnatomy', 'SUBSURF'); mod.levels = 1
            bpy.ops.object.modifier_move_up(modifier=mod.name)
            bpy.ops.object.modifier_apply(modifier=mod.name)
        if archetype == 'great_ground':
            color = body.data.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')
            for vertex in body.data.vertices:
                x, y, z = vertex.co
                bands = math.sin(z * 31 + x * 19) * math.sin(x * 43 + z * 11)
                c = (0.63, 0.57, 0.32, 1) if bands > 0.4 and y > 0.2 else (0.14, 0.19, 0.115, 1)
                if y < 0.15: c = (0.43, 0.40, 0.27, 1)
                color.data[vertex.index].color = c
            mat = body.data.materials[0]
            attr = mat.node_tree.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'Color'
            bsdf = mat.node_tree.nodes.get('Principled BSDF')
            bsdf.inputs['Base Color'].default_value = (1, 1, 1, 1)
            mat.node_tree.links.new(attr.outputs['Color'], bsdf.inputs['Base Color'])
    x, y, z, radius, iris_color = EYES[archetype]
    iris = material('GuardianIris', iris_color, 0.23)
    pupil = material('GuardianPupil', (0.006, 0.009, 0.006, 1), 0.12)
    for side in (-1, 1):
        sphere('Eye'+str(side), (x*side, y, z), (radius*0.52, radius, radius), iris, armature)
        slit = 0.30 if archetype == 'canopy_serpent' else 0.65
        sphere('Pupil'+str(side), ((x+radius*0.45)*side, y, z-radius*0.1),
               (radius*0.22, radius*0.70, radius*slit), pupil, armature)
