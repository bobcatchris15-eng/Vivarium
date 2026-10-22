"""Great horsetail (Equisetum telmateia) reference for the Vivarium 'glassrush'.

This is the real-life reference the glassrush species is rebuilt against. It builds a
clump of the great horsetail from scratch in Blender using the species' own anatomy:

  * a compact fan of unbranched, jointed, hollow culms rising from a basal clump, pale and
    glaucous rather than grassy green,
  * at every node a SHEATH - a short collar clasping the culm - whose upper rim is cut into
    a ring of BLACK, upward-pointing TEETH. The teeth are the great horsetail's clearest
    diagnostic and they are what makes a joint read as a joint at a glance,
  * whorls of fine, simple, jointed branchlets springing from the nodes of the fertile
    culms, drooping away from the stem,
  * a foot of old, brown, papery sheaths where the culms leave the mud,
  * the cut top of a culm showing its hollow bore.

The fantasy variant keeps all of that and changes one thing: the horsetail is made of
GLASS. The culms are hollow and faintly translucent, the black teeth are cut from black
crystal, and the seed spike is a smoky glass cylinder with a pale crown.

Usage:
    blender --background --python horsetail_reference.py -- --mode reference --out DIR
    blender --background --python horsetail_reference.py -- --mode fantasy   --out DIR
    blender --background --python horsetail_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# morphology (the species is 0.28 m tall and 0.36 m across)
# --------------------------------------------------------------------------

CLUMP_RADIUS = 0.11
CULM_HEIGHT = (0.19, 0.30)
CULM_RADIUS = 0.0024
NODES = 8
SHEATH_LENGTH = 0.008
TEETH_PER_NODE = 11
BRANCHLETS_PER_NODE = 8
FERTILE_FRACTION = 0.4
SPIKE_COUNT = 3

# The fantasy: the plant is made of glass.
GLASS_CULM = (0.34, 0.52, 0.50, 1.0)
GLASS_TOOTH = (0.02, 0.025, 0.035, 1.0)


# --------------------------------------------------------------------------
# mesh helpers
# --------------------------------------------------------------------------

def new_mesh_object(name, verts, faces, uvs, collection, material=None, smooth=True):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    if smooth:
        for poly in mesh.polygons:
            poly.use_smooth = True
    if uvs:
        uv_layer = mesh.uv_layers.new(name="UVMap")
        for loop in mesh.loops:
            uv_layer.data[loop.index].uv = uvs[loop.vertex_index]
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    if material is not None:
        obj.data.materials.append(material)
    return obj


def tube_into(acc, points, radii, sides=7, up_hint=None):
    base = len(acc["verts"])
    n = len(points)
    tangents = []
    for i in range(n):
        if i == 0:
            d = points[1] - points[0]
        elif i == n - 1:
            d = points[-1] - points[-2]
        else:
            d = points[i + 1] - points[i - 1]
        tangents.append(d.normalized())
    hint = Vector(up_hint) if up_hint else Vector((0, 0, 1))
    side = tangents[0].cross(hint)
    if side.length < 1e-8:
        side = tangents[0].cross(Vector((1, 0, 0)))
    side.normalize()
    for i, p in enumerate(points):
        t = tangents[i]
        side = side - t * side.dot(t)
        if side.length < 1e-8:
            side = t.cross(hint)
            if side.length < 1e-8:
                side = t.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(t).normalized()
        for s in range(sides + 1):
            a = math.tau * s / sides
            acc["verts"].append(tuple(p + side * (math.cos(a) * radii[i]) + up * (math.sin(a) * radii[i])))
    for i in range(n - 1):
        for s in range(sides):
            a = base + i * (sides + 1) + s
            acc["faces"].append((a, a + sides + 1, a + 1))
            acc["faces"].append((a + 1, a + sides + 1, a + sides + 2))


def make_tube(name, points, radii, coll, material, sides=7):
    acc = {"verts": [], "faces": []}
    tube_into(acc, points, radii, sides)
    return new_mesh_object(name, acc["verts"], acc["faces"], None, coll, material)


# --------------------------------------------------------------------------
# materials
# --------------------------------------------------------------------------

def _principled(mat):
    return mat.node_tree.nodes.get("Principled BSDF")


def _set(node, name, value):
    if node is None:
        return
    if name in node.inputs:
        node.inputs[name].default_value = value


def culm_material(fantasy=False):
    mat = bpy.data.materials.new("horsetail_culm")
    mat.use_nodes = True
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", GLASS_CULM)
        _set(bsdf, "Roughness", 0.12)
        _set(bsdf, "Transmission Weight", 0.55)
        _set(bsdf, "IOR", 1.46)
    else:
        _set(bsdf, "Base Color", (0.115, 0.205, 0.150, 1.0))
        _set(bsdf, "Roughness", 0.42)
        _set(bsdf, "Subsurface Weight", 0.12)
    return mat


def sheath_material():
    mat = bpy.data.materials.new("horsetail_sheath")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.155, 0.235, 0.170, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def tooth_material(fantasy=False):
    mat = bpy.data.materials.new("horsetail_tooth")
    mat.use_nodes = True
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", GLASS_TOOTH)
        _set(bsdf, "Roughness", 0.08)
        _set(bsdf, "Transmission Weight", 0.35)
        _set(bsdf, "Emission Color", (0.10, 0.16, 0.30, 1.0))
        _set(bsdf, "Emission Strength", 0.35)
    else:
        _set(bsdf, "Base Color", (0.020, 0.020, 0.024, 1.0))
        _set(bsdf, "Roughness", 0.30)
    return mat


def old_material():
    mat = bpy.data.materials.new("horsetail_old")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.180, 0.130, 0.080, 1.0))
    _set(bsdf, "Roughness", 0.80)
    return mat


def spike_material(fantasy=False):
    mat = bpy.data.materials.new("horsetail_spike")
    mat.use_nodes = True
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.42, 0.46, 0.47, 1.0))
        _set(bsdf, "Roughness", 0.10)
        _set(bsdf, "Transmission Weight", 0.55)
        _set(bsdf, "IOR", 1.48)
    else:
        _set(bsdf, "Base Color", (0.230, 0.215, 0.165, 1.0))
        _set(bsdf, "Roughness", 0.72)
    return mat


def soil_material():
    mat = bpy.data.materials.new("mud")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.90)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 70.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.035, 0.030, 0.024, 1.0)
    ramp.color_ramp.elements[1].position = 0.70
    ramp.color_ramp.elements[1].color = (0.095, 0.082, 0.062, 1.0)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


# --------------------------------------------------------------------------
# scene assembly
# --------------------------------------------------------------------------

def reset_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for block in list(bpy.data.meshes):
        if block.users == 0:
            bpy.data.meshes.remove(block)


def y_up_to_z_up():
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for obj in list(bpy.context.scene.objects):
        obj.matrix_world = x90 @ obj.matrix_world


def build_culm(name, foot, lean, height, radius, nodes, fertile, coll, rng, fantasy):
    """A jointed culm with a sheathing collar and a ring of black teeth at every node."""
    path, radii = [], []
    for j in range(nodes + 1):
        t = j / nodes
        path.append(Vector(foot) + Vector((0, 1, 0)) * (height * t) + Vector(lean) * (t * t * 0.85))
        radii.append(radius * (1.0 - 0.30 * t))
    make_tube(name, path, radii, coll, culm_material(fantasy), sides=8)

    for j in range(1, nodes):
        node = path[j]
        r = radii[j]
        # the sheath collar
        collar = [node - Vector((0, SHEATH_LENGTH * 0.5, 0)),
                  node + Vector((0, SHEATH_LENGTH * 0.5, 0))]
        make_tube("%s_sheath_%d" % (name, j), collar, [r * 1.16, r * 1.30], coll, sheath_material(), sides=8)

        # the black teeth around the rim
        for t in range(TEETH_PER_NODE):
            a = math.tau * t / TEETH_PER_NODE
            d = Vector((math.cos(a), 0.0, math.sin(a)))
            rim = node + Vector((0, SHEATH_LENGTH * 0.5, 0)) + d * (r * 1.30)
            ln = r * rng.uniform(0.85, 1.55)
            w = r * rng.uniform(0.55, 0.85)
            bit = d.cross(Vector((0, 1, 0))).normalized()
            tip = rim + Vector((0, ln, 0)) + d * (ln * 0.10)
            verts = [tuple(rim - bit * w), tuple(rim + bit * w), tuple(tip)]
            new_mesh_object("%s_tooth_%d_%d" % (name, j, t), verts, [(0, 1, 2)], None, coll,
                            tooth_material(fantasy), smooth=False)

        if not fertile:
            continue
        # whorls of fine, jointed, drooping branchlets
        for b in range(BRANCHLETS_PER_NODE):
            a = math.tau * b / BRANCHLETS_PER_NODE + rng.uniform(-0.22, 0.22)
            d = Vector((math.cos(a), 0.0, math.sin(a)))
            blen = rng.uniform(0.020, 0.036) * (1.0 - 0.42 * (j / nodes))
            root = node + d * (r * 1.2)
            mid = root + d * (blen * 0.45) + Vector((0, blen * 0.12, 0))
            tip = root + d * (blen * 0.72) + Vector((0, -blen * 0.04, 0))
            make_tube("%s_bl_%d_%d" % (name, j, b), [root, mid, tip],
                      [r * 0.20, r * 0.13, r * 0.05], coll, culm_material(fantasy), sides=4)

    # the hollow bore at the cut top
    top = path[nodes]
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=radii[nodes] * 0.72,
                                          location=(top.x, top.y, top.z))
    bore = bpy.context.active_object
    bore.name = name + "_bore"
    bore.scale = (1.0, 0.35, 1.0)
    bore.data.materials.append(tooth_material(fantasy))


def build_spike(name, base, direction, length, width, coll, rng, fantasy):
    path, radii = [], []
    profile = [0.26, 0.78, 0.97, 1.00, 0.93, 0.68, 0.14]
    for j in range(7):
        t = j / 6.0
        path.append(Vector(base) + Vector(direction) * (length * t))
        radii.append(width * profile[j])
    make_tube(name, path, radii, coll, spike_material(fantasy), sides=9)
    # a haze of fine fibres
    for h in range(40):
        t = rng.uniform(0.10, 0.95)
        a = rng.uniform(0, math.tau)
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        at = Vector(base) + Vector(direction) * (length * t)
        r = width * (0.26 + 0.74 * math.sin(math.pi * t))
        root = at + d * r
        tip = root + d * rng.uniform(0.0015, 0.005) + Vector(direction) * rng.uniform(-0.001, 0.003)
        make_tube("%s_hair_%d" % (name, h), [root, tip], [0.0005, 0.0001], coll,
                  spike_material(fantasy), sides=3)


def build_reference_clump(rng, fantasy=False):
    coll = bpy.context.scene.collection
    n = 34
    for k in range(n):
        a = rng.uniform(0, math.tau)
        rad = math.sqrt(rng.random()) * CLUMP_RADIUS
        foot = Vector((math.cos(a) * rad, -0.01, math.sin(a) * rad))
        lean = Vector((math.cos(a), 0, math.sin(a))) * rng.uniform(0.02, 0.10)
        height = rng.uniform(*CULM_HEIGHT)
        podded = k % 6 == 0
        fertile = podded or rng.random() < FERTILE_FRACTION
        build_culm("culm_%02d" % k, foot, lean, height, CULM_RADIUS * rng.uniform(0.8, 1.2),
                   NODES, fertile, coll, rng, fantasy)
        if not podded:
            continue
        top = foot + Vector((0, height, 0)) + lean * 0.85
        build_spike("spike_%02d" % k, top, Vector((0, 1, 0)), rng.uniform(0.030, 0.044),
                    rng.uniform(0.006, 0.009), coll, rng, fantasy)

    # the old, papery sheaths at the foot of the clump
    for k in range(12):
        a = rng.uniform(0, math.tau)
        rad = rng.uniform(0.01, 0.055)
        foot = Vector((math.cos(a) * rad, -0.009, math.sin(a) * rad))
        h = rng.uniform(0.012, 0.030)
        make_tube("old_%02d" % k, [foot, foot + Vector((0, h, 0))], [0.0028, 0.0014],
                  coll, old_material(), sides=5)

    y_up_to_z_up()


def build_from_obj(path, scale):
    coll = bpy.context.scene.collection
    bpy.ops.wm.obj_import(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for o in imported:
        o.matrix_world = x90 @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
        o.data.materials.clear()
        # The engine emits Godot's winding, where a triangle's right-hand normal points into
        # the surface. Blender expects the opposite, so reverse every face on import.
        for poly in o.data.polygons:
            poly.flip()
            poly.use_smooth = True
        o.data.update()
        attribute = o.data.color_attributes[0].name if o.data.color_attributes else None
        print("OBJ_MESH", o.name, "verts", len(o.data.vertices),
              "colour_attributes", [a.name for a in o.data.color_attributes])
        o.data.materials.append(imported_material(attribute))
    return imported


def imported_material(attribute=None):
    mat = bpy.data.materials.new("engine_vertex_colour")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.48)
    _set(bsdf, "Subsurface Weight", 0.0)
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = attribute or "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    normal_attr = nt.nodes.new("ShaderNodeAttribute")
    normal_attr.attribute_name = "Normal"
    nt.links.new(normal_attr.outputs["Vector"], bsdf.inputs["Normal"])
    return mat


def add_ground(radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=radius, depth=0.02,
                                        location=(0, 0, -0.012))
    ground = bpy.context.active_object
    ground.data.materials.append(soil_material())
    return ground


def setup_camera_and_light(size=0.46, elevation=14.0, azimuth=40.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.42))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 2.6
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.97, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(56.0), 0.0, math.radians(24.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 9.0
    fill_data.size = 0.6
    fill_data.color = (0.82, 0.90, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.35, 0.28, 0.32)
    fill.rotation_euler = (math.radians(62.0), 0.0, math.radians(-122.0))
    bpy.context.scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.40, 0.44, 0.50, 1.0)
    bg.inputs[1].default_value = 0.42
    bpy.context.scene.world = world


def setup_render(res=1024, samples=96):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False


def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference", choices=["reference", "fantasy", "mesh"])
    ap.add_argument("--out", default="build/glassrush-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=13)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--scale", default="0.18,0.28,0.18")
    opts = ap.parse_args(args)

    reset_scene()
    rng = random.Random(opts.seed)
    os.makedirs(opts.out, exist_ok=True)

    if opts.mode == "mesh":
        if not opts.obj:
            print("MESH_MODE_NEEDS_OBJ")
            return
        scale = tuple(float(x) for x in opts.scale.split(","))
        build_from_obj(opts.obj, scale)
        add_ground(0.20)
        setup_camera_and_light(size=0.46)
    else:
        build_reference_clump(rng, fantasy=(opts.mode == "fantasy"))
        add_ground(0.20)
        setup_camera_and_light(size=0.46)

    setup_render(res=opts.res, samples=opts.samples)
    path = os.path.join(opts.out, "horsetail_%s.png" % opts.mode)
    bpy.context.scene.render.filepath = path
    if opts.no_render:
        print("BLENDER_BUILD_OK objects", len(bpy.context.scene.objects))
        return
    bpy.ops.render.render(write_still=True)
    print("BLENDER_OK", path)


if __name__ == "__main__":
    main()
