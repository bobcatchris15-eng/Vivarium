"""Horsetail / skunk-cabbage hybrid reference for the Vivarium 'ringreed'.

This is the real-life reference the ringreed species is rebuilt against. It builds a
wetland plant with two parents, from scratch in Blender:

  * the HORSETAIL gives the skeleton - thick, hollow, jointed stalks divided by dark
    ringed collars with black-toothed rims, the stalks RIBBED the way an Equisetum culm
    is, and tiered whorls of drooping branchlets below;

  * the SKUNK CABBAGE (Symplocarpus foetidus) gives the flesh - a crown of broad,
    heart-shaped, strongly veined leaves, and the big mottled, pleated hood (the spathe)
    that in the real plant encloses the flowering spadix;

  * a FERN's fiddlehead sits where the horsetail's spore cone used to be: the new frond is
    still coiled on its stalk, a tight crozier, inside the cabbage's hood.

The fantasy variant keeps that anatomy and changes one thing: the hood is opened out and
lit from within, so the crozier glows out of it.

Usage:
    blender --background --python skunkcabbage_reference.py -- --mode reference --out DIR
    blender --background --python skunkcabbage_reference.py -- --mode fantasy   --out DIR
    blender --background --python skunkcabbage_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# Module-level handles, so the part builders can reach them.
rng = random.Random(0)
rng_lift = 0.35


# --------------------------------------------------------------------------
# morphology (the species is 0.48 m tall and 0.44 m across)
# --------------------------------------------------------------------------

LEAF_COUNT = 8
LEAF_LENGTH = (0.18, 0.26)
LEAF_HALF_WIDTH = (0.10, 0.15)
LEAF_NOTCH = 0.30

STALK_COUNT = 6
STALK_HEIGHT = (0.30, 0.46)
STALK_RADIUS = 0.012
STALK_RIBS = 8
STALK_RIB_DEPTH = 0.13
STALK_NODES = 7

HOOD_HEIGHT = (0.055, 0.075)
COIL_RADIUS = (0.018, 0.025)
COIL_HEIGHT = (0.055, 0.080)

# The colours the two parents bring.
STALK_GREEN = (0.115, 0.300, 0.165)
STALK_RIDGE = (0.185, 0.390, 0.215)
STALK_GROOVE = (0.045, 0.125, 0.070)
SHEATH = (0.060, 0.110, 0.055)
TOOTH = (0.030, 0.030, 0.026)
LEAF_GREEN = (0.040, 0.115, 0.042)
LEAF_VEIN = (0.082, 0.168, 0.068)
SPATHE_PLUM = (0.255, 0.105, 0.135)
SPATHE_OLIVE = (0.185, 0.200, 0.085)
CURL = (0.300, 0.355, 0.130)
CURL_SCALE = (0.175, 0.225, 0.080)


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


def tube_into(acc, points, radii, sides=6, ribs=0, rib_depth=0.0):
    """A tube, optionally ribbed the way a horsetail culm is."""
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
    side = tangents[0].cross(Vector((0, 0, 1)))
    if side.length < 1e-8:
        side = tangents[0].cross(Vector((1, 0, 0)))
    side.normalize()
    for i, p in enumerate(points):
        t = tangents[i]
        side = side - t * side.dot(t)
        if side.length < 1e-8:
            side = t.cross(Vector((0, 0, 1)))
        side.normalize()
        up = side.cross(t).normalized()
        for s in range(sides + 1):
            th = math.tau * s / sides
            r = radii[i]
            if ribs:
                rib = 0.5 + 0.5 * math.cos(ribs * th)
                r = r * (1.0 - rib_depth * (1.0 - rib))
            acc["verts"].append(tuple(p + side * (math.cos(th) * r) + up * (math.sin(th) * r)))
    for i in range(n - 1):
        for s in range(sides):
            a = base + i * (sides + 1) + s
            acc["faces"].append((a, a + sides + 1, a + 1))
            acc["faces"].append((a + 1, a + sides + 1, a + sides + 2))


def make_tube(name, points, radii, coll, material, sides=6, ribs=0, rib_depth=0.0):
    acc = {"verts": [], "faces": []}
    tube_into(acc, points, radii, sides, ribs, rib_depth)
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


def stalk_material():
    mat = bpy.data.materials.new("horsetail_stalk")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.46)
    _set(bsdf, "Subsurface Weight", 0.10)
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["UV"], sep.inputs["Vector"])
    # the ribs: a hard vertical striping along the culm
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = 'BANDS'
    wave.bands_direction = 'X'
    wave.wave_profile = 'SAW'
    wave.inputs["Scale"].default_value = 8.0
    wave.inputs["Distortion"].default_value = 0.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = 'CONSTANT'
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = STALK_GROOVE + (1.0,)
    ramp.color_ramp.elements[1].position = 0.5
    ramp.color_ramp.elements[1].color = STALK_RIDGE + (1.0,)
    nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def sheath_material():
    mat = bpy.data.materials.new("horsetail_sheath")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", SHEATH + (1.0,))
    _set(bsdf, "Roughness", 0.58)
    return mat


def tooth_material():
    mat = bpy.data.materials.new("horsetail_tooth")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", TOOTH + (1.0,))
    _set(bsdf, "Roughness", 0.34)
    return mat


def branchlet_material():
    mat = bpy.data.materials.new("horsetail_branchlet")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", STALK_RIDGE + (1.0,))
    _set(bsdf, "Roughness", 0.52)
    return mat


def leaf_material():
    mat = bpy.data.materials.new("cabbage_leaf")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.30)
    _set(bsdf, "Subsurface Weight", 0.16)
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["UV"], sep.inputs["Vector"])
    absy = nt.nodes.new("ShaderNodeMath")
    absy.operation = 'ABSOLUTE'
    nt.links.new(sep.outputs["Y"], absy.inputs[0])
    sub = nt.nodes.new("ShaderNodeMath")
    sub.operation = 'SUBTRACT'
    nt.links.new(absy.outputs[0], sub.inputs[0])
    # the palmate veins radiate from the petiole
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type = 'RINGS'
    wave.rings_direction = 'SPHERICAL'
    wave.inputs["Scale"].default_value = 5.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.45
    ramp.color_ramp.elements[0].color = LEAF_GREEN + (1.0,)
    ramp.color_ramp.elements[1].position = 0.55
    ramp.color_ramp.elements[1].color = LEAF_VEIN + (1.0,)
    nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def spathe_material(fantasy=False):
    mat = bpy.data.materials.new("cabbage_spathe")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.52)
    if fantasy:
        _set(bsdf, "Emission Color", (0.35, 0.16, 0.10, 1.0))
        _set(bsdf, "Emission Strength", 0.35)
    # mottled plum over olive
    tex = nt.nodes.new("ShaderNodeTexCoord")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 9.0
    noise.inputs["Detail"].default_value = 5.0
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = SPATHE_OLIVE + (1.0,)
    mix.inputs[7].default_value = SPATHE_PLUM + (1.0,)
    nt.links.new(noise.outputs["Fac"], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def curl_material():
    mat = bpy.data.materials.new("fern_curl")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", CURL + (1.0,))
    _set(bsdf, "Roughness", 0.44)
    _set(bsdf, "Subsurface Weight", 0.22)
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
# plant parts
# --------------------------------------------------------------------------

def make_cabbage_leaf(name, root, azimuth, length, half_width, coll, material):
    """A broad heart-shaped leaf with a cordate sinus and a lobed pair of basal lobes."""
    verts, faces, uvs = [], [], []
    radial = Vector((math.cos(azimuth), 0.0, math.sin(azimuth)))
    across = Vector((-math.sin(azimuth), 0.0, math.cos(azimuth)))
    lift = rng_lift
    along = (radial * math.sqrt(1.0 - lift * lift) + Vector((0, lift, 0))).normalized()
    up = along.cross(across).normalized()
    if up.y < 0:
        up = -up
        across = -across

    ox, oy = [], []

    def bez(x0, y0, cx, cy, x1, y1, n):
        for i in range(n):
            t = i / n
            mt = 1 - t
            ox.append(mt * mt * x0 + 2 * mt * t * cx + t * t * x1)
            oy.append(mt * mt * y0 + 2 * mt * t * cy + t * t * y1)

    M, K = 10, 6
    notch = length * LEAF_NOTCH
    bez(0.30 * half_width, 0.92 * length, half_width * 1.06, 0.42 * length, half_width, -notch * 0.70, M)
    bez(half_width, -notch * 0.70, half_width * 0.82, -notch * 1.20, 0.0, 0.0, K)
    bez(0.0, 0.0, -half_width * 0.82, -notch * 1.20, -half_width, -notch * 0.70, K)
    bez(-half_width, -notch * 0.70, -half_width * 1.06, 0.42 * length, -0.30 * half_width, 0.92 * length, M)
    bez(-0.30 * half_width, 0.92 * length, 0.0, 1.04 * length, 0.30 * half_width, 0.92 * length, 6)

    n = len(ox)
    droop = rng.uniform(0.45, 0.85)
    dish = rng.uniform(0.10, 0.20)
    positions = []
    for i in range(n):
        x, y = ox[i], oy[i]
        theta = droop * (abs(y) / max(1e-6, length)) ** 1.5
        ct, st = math.cos(theta), math.sin(theta)
        fwd = along * ct - up * st
        lift_v = along * st + up * ct
        cup = dish * (1.0 - min(1.0, abs(x) / max(1e-6, half_width)))
        positions.append(Vector(root) + across * x + fwd * y + lift_v * (length * cup * 0.35))
    centre = Vector(root)
    for p in positions:
        verts.append(tuple(p))
        uvs.append((0.5, 0.5))
    verts.append(tuple(centre))
    uvs.append((0.5, 0.5))
    ci = len(verts) - 1
    for i in range(n):
        j = (i + 1) % n
        faces.append((ci, i, j))
    return new_mesh_object(name, verts, faces, uvs, coll, material)


def make_spathe(name, base, axis, forward, base_radius, height, coll, material):
    """The hood: a broad, pleated, lumpy bract that closes over the crozier."""
    up = axis.normalized()
    lean = (forward - up * forward.dot(up)).normalized()
    side = up.cross(lean).normalized()
    rings, sides = 6, 18
    verts, faces, uvs = [], [], []
    for r in range(rings + 1):
        v = r / rings
        rad = base_radius * (1.0 + 2.3 * math.sin(v * math.pi * 0.62))
        rad *= 1.0 - 0.72 * (v ** 6.0)
        lean_out = height * 0.55 * (v ** 2.4)
        centre = Vector(base) + up * (height * v) + lean * lean_out
        for s in range(sides + 1):
            u = s / sides
            ang = math.tau * u
            pleat = 1.0 + 0.13 * math.cos(ang * 7.0) * (0.35 + 0.65 * v)
            rim = 1.0 - 0.10 * math.cos(ang * 7.0)
            d = (side * math.cos(ang) + lean * math.sin(ang)).normalized()
            verts.append(tuple(centre + d * (rad * pleat * rim)))
            uvs.append((v, u))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            b = a + sides + 1
            faces.append((a, b, a + 1))
            faces.append((b, b + 1, a + 1))
    return new_mesh_object(name, verts, faces, uvs, coll, material)


def make_curl(name, centre, axis, forward, coil_radius, height, coll, material):
    """A crozier: a tight logarithmic spiral with scales down its outside."""
    up = axis.normalized()
    side = up.cross(forward).normalized()
    face = up.cross(side).normalized()
    steps = 26
    verts, faces, uvs = [], [], []
    turns = rng.uniform(1.15, 1.55)
    for i in range(steps + 1):
        s = i / steps
        r = coil_radius * math.exp(-1.35 * s)
        phi = -0.6 + s * turns * math.tau
        p = centre + side * (r * math.cos(phi)) + face * (r * math.sin(phi)) + up * (height * 0.42 * s)
        rad = coil_radius * (0.34 - 0.20 * s) + 0.0016
        for k in range(7):
            th = math.tau * k / 7
            verts.append(tuple(p + (side * math.cos(th) + face * math.sin(th)) * rad))
            uvs.append((s, k / 7.0))
    for i in range(steps):
        for k in range(7):
            a = i * 7 + k
            b = i * 7 + (k + 1) % 7
            c = a + 7
            d = b + 7
            faces.append((a, c, b))
            faces.append((b, c, d))
    return new_mesh_object(name, verts, faces, uvs, coll, material)


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


def build_hybrid(rng, fantasy=False):
    global rng_lift
    coll = bpy.context.scene.collection

    # the cabbage crown
    for i in range(LEAF_COUNT):
        rng_lift = rng.uniform(0.18, 0.48)
        az = math.tau * i / LEAF_COUNT + rng.uniform(-0.22, 0.22)
        age = i / (LEAF_COUNT - 1)
        length = rng.uniform(*LEAF_LENGTH) * (1.0 - 0.24 * age)
        root = Vector((math.cos(az) * rng.uniform(0.005, 0.04), 0.004, math.sin(az) * rng.uniform(0.005, 0.04)))
        make_cabbage_leaf("leaf_%d" % i, root, az, length, length * rng.uniform(0.60, 0.80),
                          coll, leaf_material())

    # the horsetail stalks
    for k in range(STALK_COUNT):
        az = rng.uniform(0, math.tau)
        dist = rng.uniform(0.01, 0.10)
        foot = Vector((math.cos(az) * dist, 0.0, math.sin(az) * dist))
        sway = Vector((math.cos(az + rng.uniform(-0.5, 0.5)), 0, math.sin(az + rng.uniform(-0.5, 0.5))))
        height = rng.uniform(*STALK_HEIGHT)
        path, radii = [], []
        for j in range(STALK_NODES + 1):
            t = j / STALK_NODES
            path.append(foot + sway * (0.04 * t * t) + Vector((0, height * t, 0)))
            radii.append(STALK_RADIUS * (1.0 - 0.52 * t))
        make_tube("stalk_%d" % k, path, radii, coll, stalk_material(), sides=24,
                  ribs=STALK_RIBS, rib_depth=STALK_RIB_DEPTH)

        # the dark ringed collars with black teeth
        for j in range(1, STALK_NODES):
            node = path[j]
            r = radii[j]
            sh = 0.010 * (1.0 - j / STALK_NODES * 0.35)
            collar = [node - Vector((0, sh * 0.5, 0)), node + Vector((0, sh * 0.5, 0))]
            make_tube("collar_%d_%d" % (k, j), collar, [r * 1.02, r * 1.18], coll, sheath_material(), sides=24)
            for t in range(9):
                ang = math.tau * t / 9
                d = Vector((math.cos(ang), 0.0, math.sin(ang)))
                rim = node + Vector((0, sh * 0.5, 0)) + d * (r * 1.18)
                ln = r * 0.85
                w = r * 0.52
                bit = d.cross(Vector((0, 1, 0))).normalized()
                tip = rim + Vector((0, ln, 0)) + d * (ln * 0.30)
                verts = [tuple(rim - bit * w), tuple(rim + bit * w), tuple(tip)]
                new_mesh_object("tooth_%d_%d_%d" % (k, j, t), verts, [(0, 1, 2)], None, coll,
                                tooth_material(), smooth=False)

        # tiered whorls of drooping branchlets on the lower nodes
        for j in range(2, 5):
            node = path[j]
            r = radii[j]
            reach = rng.uniform(0.10, 0.16) * (1.1 - (j - 2) / 2.0 * 0.45)
            for b in range(8):
                bang = math.tau * b / 8 + az + j * 0.42
                radial = Vector((math.cos(bang), 0.0, math.sin(bang)))
                p0 = node - Vector((0, 0.004, 0)) + radial * (r * 1.02)
                p1 = p0 + radial * (reach * 0.48) + Vector((0, 0.006, 0))
                p2 = p0 + radial * (reach * 0.85) - Vector((0, reach * 0.22, 0))
                p3 = p0 + radial * reach - Vector((0, reach * 0.52, 0))
                make_tube("bl_%d_%d_%d" % (k, j, b), [p0, p1, p2, p3],
                          [0.0022, 0.0017, 0.0011, 0.0006], coll, branchlet_material(), sides=4)

        # the hood and the crozier
        tip = path[STALK_NODES]
        axis = (path[STALK_NODES] - path[STALK_NODES - 1]).normalized()
        hood_h = rng.uniform(*HOOD_HEIGHT)
        make_spathe("hood_%d" % k, tip - axis * 0.006, axis, sway, radii[STALK_NODES] * 0.90,
                    hood_h, coll, spathe_material(fantasy))
        rim_point = tip + axis * hood_h + sway * (hood_h * 0.55)
        make_curl("curl_%d" % k, rim_point, axis, sway, rng.uniform(*COIL_RADIUS),
                  rng.uniform(*COIL_HEIGHT), coll, curl_material())

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
    _set(bsdf, "Roughness", 0.52)
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


def setup_camera_and_light(size=0.62, elevation=18.0, azimuth=38.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.34))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 2.8
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.96, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = math.radians(56.0), 0.0, math.radians(24.0)
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 9.0
    fill_data.size = 0.7
    fill_data.color = (0.84, 0.90, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.4, 0.32, 0.36)
    fill.rotation_euler = math.radians(62.0), 0.0, math.radians(-122.0)
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
    ap.add_argument("--out", default="build/ringreed-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=31)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--scale", default="0.22,0.48,0.22")
    opts = ap.parse_args(args)

    reset_scene()
    global rng
    rng = random.Random(opts.seed)
    os.makedirs(opts.out, exist_ok=True)

    if opts.mode == "mesh":
        if not opts.obj:
            print("MESH_MODE_NEEDS_OBJ")
            return
        scale = tuple(float(x) for x in opts.scale.split(","))
        build_from_obj(opts.obj, scale)
        add_ground(0.26)
        setup_camera_and_light(size=0.62)
    else:
        build_hybrid(rng, fantasy=(opts.mode == "fantasy"))
        add_ground(0.26)
        setup_camera_and_light(size=0.62)

    setup_render(res=opts.res, samples=opts.samples)
    path = os.path.join(opts.out, "hybrid_%s.png" % opts.mode)
    bpy.context.scene.render.filepath = path
    if opts.no_render:
        print("BLENDER_BUILD_OK objects", len(bpy.context.scene.objects))
        return
    bpy.ops.render.render(write_still=True)
    print("BLENDER_OK", path)


if __name__ == "__main__":
    main()
