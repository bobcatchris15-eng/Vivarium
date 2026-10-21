"""Pampas grass (Cortaderia selloana) reference tussock for the Vivarium 'plumegrass'.

This is the real-life reference the plumegrass species is rebuilt against. It builds a
pampas tussock from scratch in Blender using the species' own anatomy:

  * a dense tussock of long arching leaves, each one strongly KEeled - a V-shaped
    cross-section with the midrib folded underneath and a pale stripe running its length,
  * sheathing leaf bases that overlap into a crown at the ground,
  * a few dead, straw-coloured old leaves at the very bottom of the tussock,
  * tall flowering culms that tower well above the foliage, lightly jointed at the nodes,
  * on each culm, a LADDER of panicles set at intervals - the 'steps' the species is named
    for - each panicle a soft, feathery plume,
  * a panicle built the way a real one is: a central rachis carrying whorls of arching
    branchlets, each branchlet bearing small spikelets and a spray of long, silky,
    feathered stigmas, the branchlets longest low down and shortest at the tip.

The fantasy variant keeps that anatomy and changes one thing: the steps of the ladder
become prismatic, each plume a different band of the spectrum.

Usage:
    blender --background --python pampas_reference.py -- --mode reference --out DIR
    blender --background --python pampas_reference.py -- --mode fantasy   --out DIR
    blender --background --python pampas_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# morphology (real proportions, metres; scaled to the species' 0.88 m height)
# --------------------------------------------------------------------------

TUSSOCK_RADIUS = 0.20
LEAF_LENGTH = (0.28, 0.42)
LEAF_HALF_WIDTH = (0.0050, 0.0082)
LEAF_COUNT = 34
DEAD_LEAF_COUNT = 7

CULM_COUNT = 3
CULM_HEIGHT = (0.65, 0.82)
CULM_RADIUS = 0.0038

# The ladder of plumes up a culm: how many steps, and where they sit.
STEPS_PER_CULM = 5
STEP_FRACTIONS = (0.36, 0.535, 0.70, 0.865, 1.0)
PANICLE_LENGTH = (0.115, 0.185)
PANICLE_HALF_WIDTH = (0.034, 0.050)
WHORLS_PER_PANICLE = 6
BRANCHLETS_PER_WHORL = 14
SPOILETS_PER_BRANCHLET = 3
STIGMAS_PER_BRANCHLET = 4

# The fantasy: a prismatic scale the steps climb.
PRISM_STOPS = (
    (0.13, 0.08, 0.36),   # deep indigo
    (0.05, 0.32, 0.66),   # cyan
    (0.18, 0.56, 0.34),   # spring green
    (0.68, 0.50, 0.08),   # gold
    (0.72, 0.20, 0.22),   # rose
)


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


def tube_into(acc, p0, p1, r0, r1, segments=6, bend=None, steps=5):
    """Append a tapered tube to a shared vertex/face accumulator."""
    base = len(acc["verts"])
    for i in range(steps + 1):
        t = i / steps
        if bend is None:
            p = Vector(p0).lerp(Vector(p1), t)
        else:
            a = Vector(p0).lerp(Vector(bend), t)
            b = Vector(bend).lerp(Vector(p1), t)
            p = a.lerp(b, t)
        r = r0 + (r1 - r0) * t
        for s in range(segments + 1):
            ang = math.tau * s / segments
            acc["verts"].append((p.x + math.cos(ang) * r, p.y + math.sin(ang) * r, p.z))
    for i in range(steps):
        for s in range(segments):
            a = base + i * (segments + 1) + s
            acc["faces"].append((a, a + segments + 1, a + 1))
            acc["faces"].append((a + 1, a + segments + 1, a + segments + 2))


_ICO_V = []
_ICO_F = []


def _init_ico():
    t = (1.0 + math.sqrt(5.0)) / 2.0
    verts = [(-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0),
             (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
             (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1)]
    _ICO_V.extend(tuple(Vector(v).normalized()) for v in verts)
    _ICO_F.extend([(0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9),
                    (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2),
                    (3, 2, 6), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)])


def ico_into(acc, centre, radius, squash=1.0):
    base = len(acc["verts"])
    for (x, y, z) in _ICO_V:
        acc["verts"].append((centre[0] + x * radius,
                             centre[1] + y * radius * squash,
                             centre[2] + z * radius * squash))
    for (a, b, c) in _ICO_F:
        acc["faces"].append((base + a, base + b, base + c))


def tube_between(name, p0, p1, r0, r1, coll, material, segments=6, bend=None):
    """A tapered tube as its own object (used for the culms)."""
    acc = {"verts": [], "faces": []}
    tube_into(acc, p0, p1, r0, r1, segments=segments, bend=bend)
    return new_mesh_object(name, acc["verts"], acc["faces"], None, coll, material)


def keeled_blade(name, root, azimuth, length, half_width, coll, material, rows=16,
                 theta0=0.16, theta1=1.85):
    """One grass leaf: a V-shaped (keeled) cross-section, midrib folded underneath.

    The blade leaves the sheath almost vertically, arcs over, and finishes drooping - the
    arc is integrated along the length rather than faked with a curve, so the blade has a
    real arc and a real tip that hangs. The two halves of the blade meet at a keel on the
    underside; that keel is the midrib, and it carries the pale stripe up the face.
    """
    verts, faces, uvs = [], [], []
    # Local frame: the blade rises and sweeps out along `azimuth`.
    radial = Vector((math.cos(azimuth), 0.0, math.sin(azimuth)))
    across = Vector((-math.sin(azimuth), 0.0, math.cos(azimuth)))

    # integrate the arc
    pts = [Vector(root)]
    sweep = theta1 - theta0
    dtheta = sweep / rows
    dl = length / rows
    p = Vector(root)
    for i in range(rows):
        a = theta0 + dtheta * (i + 0.5)
        d = radial * math.sin(a) + Vector((0.0, 1.0, 0.0)) * math.cos(a)
        p = p + d * dl
        pts.append(p.copy())

    def section(t):
        # the blade swells just above the sheath, then tapers to a fine point
        if t < 0.10:
            w = half_width * (0.60 + 0.40 * (t / 0.10))
        else:
            u = (t - 0.10) / 0.90
            w = half_width * (1.0 - u ** 1.55)
        return max(w, 1e-5)

    # five points across the fold: margin, mid, keel, mid, margin
    SECTION = ((-1.0, 0.34), (-0.46, 0.06), (0.0, -1.0), (0.46, 0.06), (1.0, 0.34))
    for i in range(rows + 1):
        t = i / rows
        centre = pts[i]
        w = section(t)
        # the blade's own plane: perpendicular to the arc, tilted so the keeled face looks up
        nxt = pts[min(rows, i + 1)]
        prv = pts[max(0, i - 1)]
        tangent = nxt - prv
        if tangent.length < 1e-9:
            tangent = radial
        tangent.normalize()
        face_up = tangent.cross(across)
        if face_up.length < 1e-9:
            face_up = Vector((0.0, 1.0, 0.0))
        face_up.normalize()
        for (sx, sy) in SECTION:
            q = centre + across * (sx * w) + face_up * (sy * w * 0.46)
            verts.append((q.x, q.y, q.z))
            uvs.append((t, 0.5 + sx * 0.5))
    n = len(SECTION)
    for i in range(rows):
        for k in range(n - 1):
            a = i * n + k
            b = a + 1
            c = a + n
            d = c + 1
            faces.append((a, c, b))
            faces.append((b, c, d))
    return new_mesh_object(name, verts, faces, uvs, coll, material)


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


def leaf_material():
    mat = bpy.data.materials.new("pampas_leaf")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.46)
    _set(bsdf, "Subsurface Weight", 0.10)
    # Glaucous blue-green, with a pale midrib running the length of the blade.
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["UV"], sep.inputs["Vector"])
    absy = nt.nodes.new("ShaderNodeMath")
    absy.operation = 'ABSOLUTE'
    nt.links.new(sep.outputs["Y"], absy.inputs[0])
    sub = nt.nodes.new("ShaderNodeMath")
    sub.operation = 'SUBTRACT'
    nt.links.new(absy.outputs[0], sub.inputs[0])
    mid = nt.nodes.new("ShaderNodeMath")
    mid.operation = 'LESS_THAN'
    mid.inputs[1].default_value = 0.06
    nt.links.new(sub.outputs[0], mid.inputs[0])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = (0.105, 0.185, 0.140, 1.0)
    mix.inputs[7].default_value = (0.62, 0.66, 0.56, 1.0)
    nt.links.new(mid.outputs[0], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def dead_leaf_material():
    mat = bpy.data.materials.new("pampas_dead_leaf")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.52, 0.44, 0.30, 1.0))
    _set(bsdf, "Roughness", 0.78)
    return mat


def culm_material():
    mat = bpy.data.materials.new("pampas_culm")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.44, 0.45, 0.31, 1.0))
    _set(bsdf, "Roughness", 0.62)
    return mat


def plume_material(prism=None):
    mat = bpy.data.materials.new("pampas_plume")
    mat.use_nodes = True
    bsdf = _principled(mat)
    if prism is None:
        _set(bsdf, "Base Color", (0.86, 0.83, 0.76, 1.0))
        _set(bsdf, "Roughness", 0.72)
        _set(bsdf, "Subsurface Weight", 0.30)
        _set(bsdf, "Subsurface Radius", (0.006, 0.008, 0.012))
    else:
        _set(bsdf, "Base Color", (prism[0], prism[1], prism[2], 1.0))
        _set(bsdf, "Roughness", 0.44)
        _set(bsdf, "Subsurface Weight", 0.45)
        _set(bsdf, "Subsurface Radius", (0.010, 0.012, 0.020))
        _set(bsdf, "Emission Color", (prism[0], prism[1], prism[2], 1.0))
        _set(bsdf, "Emission Strength", 0.22)
    return mat


def spikelet_material():
    mat = bpy.data.materials.new("pampas_spikelet")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.38, 0.24, 0.24, 1.0))
    _set(bsdf, "Roughness", 0.66)
    return mat


def soil_material():
    mat = bpy.data.materials.new("soil")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 60.0
    noise.inputs["Detail"].default_value = 8.0
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
    """The plant is authored in the engine's Y-up frame; Blender is Z-up."""
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for obj in list(bpy.context.scene.objects):
        obj.matrix_world = x90 @ obj.matrix_world


def build_panicle(name, base, direction, up, length, half_width, coll, rng, prism=None, fantasy=False):
    """One pampas panicle: a rachis with whorls of arching branchlets.

    The branchlets are longest low on the panicle and shortest at the tip, which is what
    gives a real plume its soft, tapering, feather-duster silhouette. The whole panicle -
    rachis, branchlets, spikelets and stigmas - is merged into one mesh, because a real
    plume is thousands of filaments and one object per filament does not render.
    """
    side = up.cross(direction).normalized()
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    plume = plume_material(prism)
    rachis = {"verts": [], "faces": []}
    hairs = {"verts": [], "faces": []}
    spikelets = {"verts": [], "faces": []}

    tube_into(rachis, base, base + up * length, 0.0016, 0.0006, segments=5)

    for w in range(WHORLS_PER_PANICLE):
        wt = (w + 0.5) / WHORLS_PER_PANICLE
        # branchlets taper toward the tip of the panicle
        reach = half_width * (1.0 - 0.62 * wt) * rng.uniform(0.82, 1.18)
        # the whorl sits on the rachis and rises as it goes out
        wp = base + up * (length * wt)
        for b in range(BRANCHLETS_PER_WHORL):
            ang = math.tau * b / BRANCHLETS_PER_WHORL + w * 0.42 + rng.uniform(-0.14, 0.14)
            out = side * math.cos(ang) + direction * math.sin(ang)
            tip = wp + out * reach + up * (length * 0.10 * rng.uniform(0.4, 1.4))
            mid = wp + out * (reach * 0.55) + up * (length * 0.05)
            tube_into(rachis, wp, tip, 0.00075, 0.00018, segments=4, bend=mid)
            # spikelets clustered along the branchlet
            for s in range(SPOILETS_PER_BRANCHLET):
                st = 0.25 + 0.7 * (s / max(1, SPOILETS_PER_BRANCHLET - 1))
                sp = wp.lerp(tip, st)
                ico_into(spikelets, (sp.x, sp.y, sp.z), 0.0016)
            # the long silky stigmas that give the plume its haze
            for s in range(STIGMAS_PER_BRANCHLET):
                st = 0.45 + 0.5 * (s / max(1, STIGMAS_PER_BRANCHLET - 1))
                sp = wp.lerp(tip, st)
                for side_sign in (-1, 1):
                    drift = side * math.cos(ang + side_sign * 0.8) + direction * math.sin(ang + side_sign * 0.8)
                    end = sp + drift * rng.uniform(0.012, 0.026) + up * rng.uniform(0.004, 0.016)
                    tube_into(hairs, sp, end, 0.00030, 0.00006, segments=3)

    objs = [new_mesh_object(name + "_rachis", rachis["verts"], rachis["faces"], None, coll, plume),
            new_mesh_object(name + "_hairs", hairs["verts"], hairs["faces"], None, coll, plume),
            new_mesh_object(name + "_spikelets", spikelets["verts"], spikelets["faces"], None, coll,
                            spikelet_material())]
    return objs


def build_reference_tussock(rng, fantasy=False):
    """One pampas tussock, authored at true world proportions."""
    coll = bpy.context.scene.collection

    # --- the tussock of arching leaves
    for i in range(LEAF_COUNT):
        az = math.tau * i / LEAF_COUNT + rng.uniform(-0.16, 0.16)
        length = rng.uniform(*LEAF_LENGTH)
        half_w = rng.uniform(*LEAF_HALF_WIDTH)
        root = Vector((math.cos(az) * rng.uniform(0.0, 0.022), 0.004,
                       math.sin(az) * rng.uniform(0.0, 0.022)))
        # older leaves lie further over; the inner ones stand up
        theta0 = rng.uniform(0.10, 0.26)
        theta1 = rng.uniform(1.55, 2.10)
        keeled_blade("leaf_%02d" % i, root, az, length, half_w, coll, leaf_material(),
                     theta0=theta0, theta1=theta1)

    # --- a few dead old leaves at the bottom of the tussock
    for i in range(DEAD_LEAF_COUNT):
        az = math.tau * i / DEAD_LEAF_COUNT + rng.uniform(-0.3, 0.3)
        length = rng.uniform(*LEAF_LENGTH) * 0.8
        root = Vector((math.cos(az) * 0.02, 0.002, math.sin(az) * 0.02))
        keeled_blade("dead_%02d" % i, root, az, length, rng.uniform(*LEAF_HALF_WIDTH) * 0.8,
                     coll, dead_leaf_material(), rows=9,
                     theta0=rng.uniform(0.30, 0.50), theta1=rng.uniform(2.0, 2.5))

    # --- the flowering culms
    for c in range(CULM_COUNT):
        az = math.tau * c / CULM_COUNT + rng.uniform(-0.3, 0.3)
        height = rng.uniform(*CULM_HEIGHT)
        base = Vector((math.cos(az) * 0.02, 0.0, math.sin(az) * 0.02))
        top = base + Vector((math.cos(az) * height * 0.055, height, math.sin(az) * height * 0.055))
        tube_between("culm_%d" % c, base, top, CULM_RADIUS, CULM_RADIUS * 0.42,
                     coll, culm_material(), segments=7)
        up = (top - base).normalized()

        # --- the ladder of plumes
        for s, frac in enumerate(STEP_FRACTIONS[:STEPS_PER_CULM]):
            wp = base.lerp(top, frac)
            prism = None
            if fantasy:
                idx = s * (len(PRISM_STOPS) - 1) / max(1, STEPS_PER_CULM - 1)
                i0 = int(idx)
                i1 = min(len(PRISM_STOPS) - 1, i0 + 1)
                f = idx - i0
                prism = tuple(PRISM_STOPS[i0][k] * (1 - f) + PRISM_STOPS[i1][k] * f for k in range(3))
            plen = rng.uniform(*PANICLE_LENGTH) * (1.0 - 0.48 * frac)
            phw = rng.uniform(*PANICLE_HALF_WIDTH) * (1.0 - 0.48 * frac)
            # Each step's plume leaves the culm at an angle and to one side, so the ladder
            # reads as separate treads rather than one long column.
            side_sign = 1.0 if s % 2 == 0 else -1.0
            az2 = az + math.pi * 0.5 * side_sign + rng.uniform(-0.20, 0.20)
            tilt = rng.uniform(0.40, 0.80)
            axis = (Vector((0.0, 1.0, 0.0)) * math.cos(tilt)
                    + Vector((math.cos(az2), 0.0, math.sin(az2))) * math.sin(tilt)).normalized()
            build_panicle("panicle_%d_%d" % (c, s), wp, axis, axis, plen, phw, coll, rng,
                          prism=prism, fantasy=fantasy)

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
    """Shows the engine's vertex colours so the export can be compared directly."""
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


def setup_camera_and_light(size=1.05, elevation=14.0, azimuth=38.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.40))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 3.0
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.97, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(58.0), 0.0, math.radians(28.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 12.0
    fill_data.size = 1.2
    fill_data.color = (0.84, 0.90, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.7, 0.5, 0.5)
    fill.rotation_euler = (math.radians(62.0), 0.0, math.radians(-122.0))
    bpy.context.scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.42, 0.45, 0.50, 1.0)
    bg.inputs[1].default_value = 0.45
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
    ap.add_argument("--mode", default="reference",
                    choices=["reference", "fantasy", "mesh"])
    ap.add_argument("--out", default="build/plumegrass-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--scale", default="0.16,0.88,0.16",
                    help="engine mesh frame -> metres: radius, height, radius")
    opts = ap.parse_args(args)

    reset_scene()
    _init_ico()
    rng = random.Random(opts.seed)
    os.makedirs(opts.out, exist_ok=True)

    if opts.mode == "mesh":
        if not opts.obj:
            print("MESH_MODE_NEEDS_OBJ")
            return
        scale = tuple(float(x) for x in opts.scale.split(","))
        build_from_obj(opts.obj, scale)
        add_ground(0.26)
        setup_camera_and_light(size=1.05)
    else:
        build_reference_tussock(rng, fantasy=(opts.mode == "fantasy"))
        add_ground(0.26)
        setup_camera_and_light(size=1.05)

    setup_render(res=opts.res, samples=opts.samples)
    path = os.path.join(opts.out, "pampas_%s.png" % opts.mode)
    bpy.context.scene.render.filepath = path
    if opts.no_render:
        print("BLENDER_BUILD_OK objects", len(bpy.context.scene.objects))
        return
    bpy.ops.render.render(write_still=True)
    print("BLENDER_OK", path)


if __name__ == "__main__":
    main()
