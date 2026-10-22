"""Giant reed (Arundo donax) reference for the Vivarium 'veilblade'.

This is the real-life reference the veilblade species is rebuilt against. It builds a tall
wetland reed clump from scratch in Blender using the species' own anatomy:

  * a dense clump of long, broad, arching blades, each one rising from a SHEATHING base that
    wraps the culm, keeled along a midrib, and TWISTED as it runs out,
  * a blade tip that is still ROLLED, the way a monocot leaf unrolls from its sheath,
  * wiry, jointed culms that rise clear above the foliage and end in a feathery panicle of
    small spikelets.

The fantasy variant keeps that anatomy and changes one thing: the blade margins grow a fine
comb of marginal FILAMENTS, the kind cattail and iris carry and that people once wove, and
the filaments of neighbouring blades interlace - so the curtain of blades is literally
woven, and the panicles above it trail a long silk of seed filaments.

Usage:
    blender --background --python reed_reference.py -- --mode reference --out DIR
    blender --background --python reed_reference.py -- --mode fantasy   --out DIR
    blender --background --python reed_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# morphology (the species is 1.62 m tall and 1.36 m across)
# --------------------------------------------------------------------------

FAN_COUNT = 5
BLADES_PER_FAN = 15
BLADE_LENGTH = (1.05, 1.45)
BLADE_HALF_WIDTH = (0.026, 0.042)
CULM_COUNT = 5
CULM_HEIGHT = (1.60, 2.05)
PANICLE_LENGTH = (0.26, 0.40)
PANICLE_SPREAD = 0.13

FILAMENTS_PER_EDGE = 12

BLADE_GREEN = (0.115, 0.240, 0.105)
BLADE_TIP = (0.280, 0.430, 0.185)
SHEATH_GREEN = (0.235, 0.300, 0.150)
FILAMENT = (0.520, 0.520, 0.360)
SPIKELET = (0.330, 0.250, 0.190)

# Across the blade: margin, mid, keel, mid, margin.
SECTION_X = (-1.0, -0.44, 0.0, 0.44, 1.0)
SECTION_Y = (0.30, 0.04, -1.0, 0.04, 0.30)
# How deep the blade folds, as a fraction of its half width: low, because this is a wide
# thin strap of grass, not a keel.
FLATNESS = 0.38


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


def tube_into(acc, points, radii, sides=6):
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
            a = math.tau * s / sides
            acc["verts"].append(tuple(p + side * (math.cos(a) * radii[i]) + up * (math.sin(a) * radii[i])))
    for i in range(n - 1):
        for s in range(sides):
            a = base + i * (sides + 1) + s
            acc["faces"].append((a, a + sides + 1, a + 1))
            acc["faces"].append((a + 1, a + sides + 1, a + sides + 2))


def make_tube(name, points, radii, coll, material, sides=6):
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


def blade_material():
    mat = bpy.data.materials.new("reed_blade")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.38)
    _set(bsdf, "Subsurface Weight", 0.14)
    _set(bsdf, "Subsurface Radius", (0.004, 0.008, 0.003))
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["UV"], sep.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.05
    ramp.color_ramp.elements[0].color = BLADE_GREEN + (1.0,)
    ramp.color_ramp.elements[1].position = 0.95
    ramp.color_ramp.elements[1].color = BLADE_TIP + (1.0,)
    nt.links.new(sep.outputs["X"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def sheath_material():
    mat = bpy.data.materials.new("reed_sheath")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", SHEATH_GREEN + (1.0,))
    _set(bsdf, "Roughness", 0.52)
    return mat


def filament_material():
    mat = bpy.data.materials.new("reed_filament")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", FILAMENT + (1.0,))
    _set(bsdf, "Roughness", 0.60)
    return mat


def spikelet_material():
    mat = bpy.data.materials.new("reed_spikelet")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", SPIKELET + (1.0,))
    _set(bsdf, "Roughness", 0.70)
    return mat


def soil_material():
    mat = bpy.data.materials.new("soil")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 22.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.050, 0.042, 0.030, 1.0)
    ramp.color_ramp.elements[1].position = 0.70
    ramp.color_ramp.elements[1].color = (0.140, 0.118, 0.088, 1.0)
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


def make_blade(name, root, azimuth, length, half_width, coll, rng, filaments, rows=16):
    """One reed blade: sheathed, keeled, twisted, and rolled at the tip.

    The blade leaves the sheath at a set angle, arcs gently over and finishes drooping, and
    its section is a wide flat strap with only a shallow midrib fold.
    """
    verts, faces, uvs = [], [], []
    theta0 = rng.uniform(0.86, 1.20)     # 49-69 degrees above horizontal
    theta1 = rng.uniform(-0.50, 0.08)    # drooping at the tip
    twist = rng.uniform(-1.4, 1.4)
    roll_from = rng.uniform(0.86, 0.93)
    radial = Vector((math.cos(azimuth), 0.0, math.sin(azimuth)))

    # integrate the centreline from the turning angle
    centres = [Vector(root)]
    d_theta = (theta1 - theta0) / rows
    dl = length / rows
    p = Vector(root)
    for i in range(rows):
        a = theta0 + d_theta * (i + 0.5)
        d = radial * math.cos(a) + Vector((0, math.sin(a), 0))
        p = p + d * dl
        centres.append(p)

    def centre(t):
        f = max(0.0, min(1.0, t)) * rows
        i = min(rows - 1, int(f))
        return centres[i].lerp(centres[i + 1], f - i)

    edge_a, edge_b = [], []
    for i in range(rows + 1):
        t = i / rows
        c = centre(t)
        e = 0.004
        along = (centre(min(1.0, t + e)) - centre(max(0.0, t - e)))
        if along.length < 1e-9:
            along = radial
        along.normalize()
        across = along.cross(Vector((0, 1, 0)))
        if across.length < 1e-9:
            across = Vector((1, 0, 0))
        across.normalize()
        up = across.cross(along).normalized()

        w = half_width * (0.42 + 0.58 * math.sin(math.pi * min(1.0, 0.10 + 0.72 * t)))
        w *= max(0.05, 1.0 - ((max(0.0, t - 0.25) / 0.75) ** 1.9))
        w = max(w, half_width * 0.05)

        roll_t = min(1.0, max(0.0, (t - roll_from) / (1.0 - roll_from)))
        roll = roll_t * roll_t * math.pi * 2.4
        shrink = 1.0 - 0.78 * roll_t
        ta = twist * t
        for k, (sx, sy) in enumerate(zip(SECTION_X, SECTION_Y)):
            rx = sx * math.cos(roll) - sy * math.sin(roll)
            ry = sx * math.sin(roll) + sy * math.cos(roll)
            lx = rx * w * shrink
            # a wide flat strap: the fold is a fraction of the width, not a share of it
            ly = ry * w * shrink * FLATNESS
            tx = lx * math.cos(ta) - ly * math.sin(ta)
            ty = lx * math.sin(ta) + ly * math.cos(ta)
            p = c + across * tx + up * ty
            verts.append((p.x, p.y, p.z))
            uvs.append((t, 0.5 + sx * 0.5))
            if k == 0:
                edge_a.append(p)
            if k == 4:
                edge_b.append(p)
    n = len(SECTION_X)
    for i in range(rows):
        for k in range(n - 1):
            a = i * n + k
            faces.append((a, a + n, a + 1))
            faces.append((a + 1, a + n, a + n + 1))
    obj = new_mesh_object(name, verts, faces, uvs, coll, blade_material())

    if not filaments:
        return obj
    # The marginal filaments: they stand off both edges and lean forward. They are merged
    # into one mesh per blade, because a reference scene with thousands of tiny objects
    # does not render in a reasonable time.
    acc = {"verts": [], "faces": []}
    for e in (0, 1):
        for f in range(FILAMENTS_PER_EDGE):
            t = 0.10 + 0.78 * (f / (FILAMENTS_PER_EDGE - 1))
            i = min(rows - 1, max(0, int(t * rows)))
            at = edge_a[i] if e == 0 else edge_b[i]
            e2 = 0.004
            along = (centre(min(1.0, i / rows + e2)) - centre(max(0.0, i / rows - e2)))
            if along.length < 1e-9:
                along = radial
            along.normalize()
            across = along.cross(Vector((0, 1, 0)))
            if across.length < 1e-9:
                across = Vector((1, 0, 0))
            across.normalize()
            up = across.cross(along).normalized()
            outward = -across if e == 0 else across
            ln = length * rng.uniform(0.014, 0.032)
            tip = at + outward * (ln * 0.75) + along * (ln * rng.uniform(1.4, 2.6)) + up * (ln * rng.uniform(-0.25, 0.35))
            tube_into(acc, [at, at.lerp(tip, 0.5) - up * (ln * 0.05), tip],
                      [length * 0.0013, length * 0.0008, length * 0.00015], sides=3)
    new_mesh_object("%s_filaments" % name, acc["verts"], acc["faces"], None, coll,
                    filament_material())
    return obj


def make_panicle(name, base, up, length, spread, coll, rng, silk):
    acc = {"verts": [], "faces": []}
    path, radii = [], []
    for i in range(7):
        t = i / 6.0
        path.append(Vector(base) + Vector(up) * (length * t))
        radii.append(0.0075 * (1.0 - 0.55 * t))
    tube_into(acc, path, radii, sides=5)
    new_mesh_object(name + "_rachis", acc["verts"], acc["faces"], None, coll, sheath_material())

    side = Vector(up).cross(Vector((1, 0, 0)))
    if side.length < 1e-9:
        side = Vector(up).cross(Vector((0, 0, 1)))
    side.normalize()
    forward = side.cross(Vector(up)).normalized()

    whorls = 9
    for w in range(whorls):
        wt = (w + 0.4) / whorls
        w_base = Vector(base) + Vector(up) * (length * wt)
        reach = spread * (1.0 - 0.62 * wt) * rng.uniform(0.85, 1.15)
        for b in range(9):
            a = math.tau * b / 9 + w * 0.51 + rng.uniform(-0.2, 0.2)
            outward = side * math.cos(a) + forward * math.sin(a)
            b_tip = w_base + outward * reach + Vector(up) * (length * 0.10 * rng.uniform(0.3, 1.2))
            b_mid = w_base + outward * (reach * 0.5) + Vector(up) * (length * 0.05)
            acc = {"verts": [], "faces": []}
            tube_into(acc, [w_base, b_mid, b_tip], [0.0026, 0.0018, 0.0007], sides=3)
            new_mesh_object("%s_b_%d_%d" % (name, w, b), acc["verts"], acc["faces"], None, coll,
                            sheath_material())

            for s in range(4):
                st = 0.25 + 0.62 * (s / 3.0)
                at = w_base.lerp(b_tip, st)
                # a small faceted seed cluster, built by hand so the scene stays cheap
                r = 0.006
                verts = [(at.x, at.y + r * 1.6, at.z),
                         (at.x, at.y - r * 1.1, at.z),
                         (at.x + r, at.y, at.z),
                         (at.x - r, at.y, at.z),
                         (at.x, at.y, at.z + r),
                         (at.x, at.y, at.z - r)]
                faces = [(0, 2, 4), (0, 4, 3), (0, 3, 5), (0, 5, 2),
                         (1, 4, 2), (1, 3, 4), (1, 5, 3), (1, 2, 5)]
                new_mesh_object("%s_s_%d_%d_%d" % (name, w, b, s), verts, faces, None, coll,
                                spikelet_material(), smooth=False)
                if not silk:
                    continue
                # the long silk trailing from each spikelet
                silk_len = length * rng.uniform(0.10, 0.22)
                tip = at + Vector(up) * (-silk_len) + outward * (silk_len * rng.uniform(-0.35, 0.35))
                acc = {"verts": [], "faces": []}
                tube_into(acc, [at, at.lerp(tip, 0.5), tip], [0.0011, 0.0006, 0.00012], sides=3)
                new_mesh_object("%s_silk_%d_%d_%d" % (name, w, b, s), acc["verts"], acc["faces"],
                                None, coll, filament_material())

def build_reference_clump(rng, fantasy=False):
    coll = bpy.context.scene.collection
    for fan in range(FAN_COUNT):
        a = math.tau * fan / FAN_COUNT + rng.uniform(-0.32, 0.32)
        crown = Vector((math.cos(a), 0.0, math.sin(a))) * rng.uniform(0.10, 0.42)
        for k in range(BLADES_PER_FAN):
            age = abs(k - (BLADES_PER_FAN - 1) * 0.5) / ((BLADES_PER_FAN - 1) * 0.5)
            az = a + (k - (BLADES_PER_FAN - 1) * 0.5) * rng.uniform(0.17, 0.27) + rng.uniform(-0.12, 0.12)
            length = rng.uniform(*BLADE_LENGTH) * (1.0 - 0.26 * age)
            half_w = rng.uniform(*BLADE_HALF_WIDTH) * (1.0 - 0.22 * age)
            side = Vector((-math.sin(az), 0.0, math.cos(az)))
            root = crown + side * rng.uniform(-0.055, 0.055)
            # the sheathing base that wraps the stem
            acc = {"verts": [], "faces": []}
            tube_into(acc, [root - Vector((0, 0.02, 0)), root + Vector((0, 0.05, 0))],
                      [half_w * 0.52, half_w * 0.44], sides=5)
            new_mesh_object("sheath_%d_%d" % (fan, k), acc["verts"], acc["faces"], None, coll,
                            sheath_material())
            make_blade("blade_%d_%d" % (fan, k), root, az, length, half_w, coll, rng,
                       filaments=fantasy)

    for k in range(CULM_COUNT):
        a = rng.uniform(0, math.tau)
        foot = Vector((math.cos(a), 0.0, math.sin(a))) * rng.uniform(0.04, 0.30)
        top = foot + Vector((rng.uniform(-0.10, 0.10), rng.uniform(1.70, 2.10), rng.uniform(-0.10, 0.10)))
        rings = 9
        path, radii = [], []
        for i in range(rings + 1):
            t = i / rings
            path.append(foot.lerp(top, t) + Vector((0, 0.04 * math.sin(t * math.pi), 0)))
            radii.append(0.013 * (1.0 - 0.60 * t))
        acc = {"verts": [], "faces": []}
        tube_into(acc, path, radii, sides=6)
        new_mesh_object("culm_%d" % k, acc["verts"], acc["faces"], None, coll, sheath_material())
        for i in range(2, rings - 1, 3):
            acc = {"verts": [], "faces": []}
            tube_into(acc, [path[i] - Vector((0, 0.010, 0)), path[i] + Vector((0, 0.014, 0))],
                      [radii[i] * 1.18, radii[i] * 1.12], sides=6)
            new_mesh_object("culm_%d_joint_%d" % (k, i), acc["verts"], acc["faces"], None, coll,
                            sheath_material())
        up = (top - foot).normalized()
        make_panicle("panicle_%d" % k, top, up, rng.uniform(*PANICLE_LENGTH), PANICLE_SPREAD,
                     coll, rng, silk=fantasy)

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


def setup_camera_and_light(size=3.0, elevation=12.0, azimuth=36.0):
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
    sun_data.color = (1.0, 0.97, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(60.0), 0.0, math.radians(26.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 16.0
    fill_data.size = 2.0
    fill_data.color = (0.84, 0.90, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-1.6, 1.1, 1.2)
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
    ap.add_argument("--out", default="build/veilblade-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=23)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--scale", default="0.68,1.62,0.68")
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
        add_ground(0.90)
        setup_camera_and_light(size=3.0)
    else:
        build_reference_clump(rng, fantasy=(opts.mode == "fantasy"))
        add_ground(0.90)
        setup_camera_and_light(size=3.0)

    setup_render(res=opts.res, samples=opts.samples)
    path = os.path.join(opts.out, "reed_%s.png" % opts.mode)
    bpy.context.scene.render.filepath = path
    if opts.no_render:
        print("BLENDER_BUILD_OK objects", len(bpy.context.scene.objects))
        return
    bpy.ops.render.render(write_still=True)
    print("BLENDER_OK", path)


if __name__ == "__main__":
    main()
