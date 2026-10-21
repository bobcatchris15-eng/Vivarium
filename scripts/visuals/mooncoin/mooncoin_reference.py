"""Mooncoin reference mat for the Vivarium 'mooncoin' groundcover.

This is the real-life reference the mooncoin species is rebuilt against, plus the
fantastical twist the species carries in game. It builds a creeping peltate mat in
Blender from the botany of the round-leaved mat formers:

  * common pennywort (Hydrocotyle vulgaris) for the peltate orbicular blade with its
    shallowly lobed margin, its radiating palmate venation and the waxy boss where
    the petiole meets the leaf,
  * the creeping stolon that roots at the nodes and is clothed in fine root hairs,
  * the long, slender, arching petiole that lifts each blade clear of the mat.

The fantasy variant is the in-game Mooncoin: a xerophytic silver-sage whose blades
wear a dry farina bloom, a pale waxy pip, a cool moonlace flush in the hollows
between the veins, and one bleached silver sector as if moonlight had caught one side
of the mat.

The same morphology is mirrored by the engine generator in
``src/Vivarium.Sim/Geometry/OrganismMeshes.Mooncoin.cs``, so the reference render
and the in-game capture can be compared directly.

Usage:
    blender --background --python mooncoin_reference.py -- --mode reference --out DIR
    blender --background --python mooncoin_reference.py -- --mode fantasy   --out DIR
    blender --background --python mooncoin_reference.py -- --mode blade     --out DIR
    blender --background --python mooncoin_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# morphology
# --------------------------------------------------------------------------

# Blade margin: the number of shallow rounded lobes, and how deep they cut.
LOBES = 9
LOBE_DEPTH = 0.055

# Palmate venation: how many primary veins leave the pip, and how wide they are.
VEINS = 9
VEIN_HALF_WIDTH = 0.30

# Blade profile, as fractions of the blade radius.
DISH = 0.055          # how far the blade sags away from the pip
PIP_RISE = 0.016      # the low collar around the attachment
RIM_LIFT = 0.026      # the margin lifts again
WAVINESS = 0.010


def _smoothstep(a, b, x):
    if b <= a:
        return 0.0 if x < a else 1.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _smoothstep_inv(a, b, x):
    """1 below a, 0 above b."""
    return 1.0 - _smoothstep(a, b, x)


def rim_scale(theta, lobes=LOBES, phase=0.0, depth=LOBE_DEPTH):
    """Margin radius multiplier: broad, shallow, rounded lobes."""
    return 1.0 + depth * math.cos(lobes * theta + phase)


def vein_mask(theta, radius, veins=VEINS, phase=0.0):
    """Ridge profile of the primary palmate veins, 1 on a vein and 0 between them."""
    a = veins * theta + phase
    f = a - math.floor(a) - 0.5
    d = abs(f) * 2.0
    ridge = _smoothstep_inv(0.58, 0.34, d)
    return ridge * _smoothstep(0.0, 0.13, radius)


def blade_z(radius, theta, veins=VEINS, phase=0.0):
    """Height of the blade's mid surface above the blade plane."""
    dish = -DISH * _smoothstep(0.0, 0.58, radius)
    pip = PIP_RISE * math.exp(-((radius - 0.15) / 0.23) ** 2)
    rim = RIM_LIFT * (_smoothstep(0.46, 1.0, radius) ** 1.6)
    wob = WAVINESS * math.sin(3.0 * theta + phase * 1.7) * math.sin(math.pi * min(1.0, radius * 1.1))
    return dish + pip + rim + wob


# --------------------------------------------------------------------------
# scene helpers
# --------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_mesh_object(name, verts, faces, uvs, collection):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    if uvs:
        layer = mesh.uv_layers.new(name="UVMap")
        for poly in mesh.polygons:
            for li in poly.loop_indices:
                vi = mesh.loops[li].vertex_index
                layer.data[li].uv = uvs[vi]
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return obj


def _principled(mat):
    mat.use_nodes = True
    return mat.node_tree.nodes.get("Principled BSDF")


def _set(node, name, value):
    if name in node.inputs:
        node.inputs[name].default_value = value


def leaf_material(fantasy=False):
    """A glaucous, matte blade. The real plant is a waxy mid green; the fantasy
    variant is the silver-sage Mooncoin with a dry farina bloom."""
    mat = bpy.data.materials.new("MooncoinLeaf")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.128, 0.158, 0.135, 1.0))
        _set(bsdf, "Roughness", 0.82)
        _set(bsdf, "Subsurface Weight", 0.02)
        _set(bsdf, "Specular IOR Level", 0.22)
    else:
        _set(bsdf, "Base Color", (0.075, 0.185, 0.045, 1.0))
        _set(bsdf, "Roughness", 0.34)
        _set(bsdf, "Subsurface Weight", 0.12)
        _set(bsdf, "Specular IOR Level", 0.42)
    return mat


def vein_material(fantasy=False):
    mat = bpy.data.materials.new("MooncoinVein")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.30, 0.33, 0.30, 1.0))
        _set(bsdf, "Roughness", 0.70)
    else:
        _set(bsdf, "Base Color", (0.16, 0.29, 0.11, 1.0))
        _set(bsdf, "Roughness", 0.40)
    return mat


def petiole_material(fantasy=False):
    mat = bpy.data.materials.new("MooncoinPetiole")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.30, 0.34, 0.18, 1.0))
    else:
        _set(bsdf, "Base Color", (0.22, 0.44, 0.12, 1.0))
    _set(bsdf, "Roughness", 0.52)
    return mat


def stolon_material():
    mat = bpy.data.materials.new("MooncoinStolon")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.16, 0.085, 0.040, 1.0))
    _set(bsdf, "Roughness", 0.72)
    return mat


def root_material():
    mat = bpy.data.materials.new("MooncoinRoot")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.22, 0.15, 0.09, 1.0))
    _set(bsdf, "Roughness", 0.80)
    return mat


def floret_material(fantasy=False):
    mat = bpy.data.materials.new("MooncoinFloret")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.90, 0.90, 0.82, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def soil_material():
    mat = bpy.data.materials.new("MooncoinSoil")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.055, 0.045, 0.033, 1.0))
    _set(bsdf, "Roughness", 0.95)
    return mat


# --------------------------------------------------------------------------
# the plant
# --------------------------------------------------------------------------

def make_blade(name, centre, up, radius, coll, rng, fantasy=False, veins=VEINS,
               lobe_phase=0.0, vein_phase=0.0, silver_sector=None, rings=7, sectors=48):
    """One peltate coin.

    The blade is a shallow dished disc with broad rounded lobes, a pale pip where
    the petiole plugs in from underneath, and primary veins that radiate from that
    pip out towards the lobe tips.
    """
    up = Vector(up).normalized()
    ref = Vector((math.cos(rng.uniform(0, math.tau)), 0.0, math.sin(rng.uniform(0, math.tau))))
    side = up.cross(ref)
    if side.length < 1e-6:
        side = up.cross(Vector((1.0, 0.0, 0.0)))
    side.normalize()
    ref = side.cross(up).normalized()

    thickness = radius * 0.038
    vein_crown = radius * 0.014
    vein_rib = radius * 0.020
    boss = radius * 0.052

    verts = []
    uvs = []
    faces = []
    vein_faces = []

    def point(t, theta, upper):
        r = t * radius * rim_scale(theta, phase=lobe_phase)
        z = blade_z(t, theta, veins=veins, phase=vein_phase) * radius
        vm = vein_mask(theta, t, veins=veins, phase=vein_phase)
        if upper:
            z += thickness * 0.5 + vein_crown * vm
        else:
            z -= thickness * 0.5 + boss * (1.0 - _smoothstep(0.0, 0.20, t)) + vein_rib * vm
        return centre + ref * (r * math.cos(theta)) + side * (r * math.sin(theta)) + up * z

    # Upper face.
    top_start = len(verts)
    for i in range(rings + 1):
        t = 0.5 * (1.0 - math.cos(math.pi * i / rings))
        for j in range(sectors + 1):
            theta = math.tau * j / sectors
            verts.append(point(t, theta, True))
            uvs.append((0.026 * j / sectors, 0.5 + 0.5 * t))
    top_count = len(verts)

    # Lower face.
    under_start = len(verts)
    under_rings = max(2, rings // 2)
    for i in range(under_rings + 1):
        t = 0.5 * (1.0 - math.cos(math.pi * i / under_rings))
        for j in range(sectors + 1):
            theta = math.tau * j / sectors
            verts.append(point(t, theta, False))
            uvs.append((0.026 * j / sectors, 0.5 + 0.5 * t))

    stride = sectors + 1

    def quad(a, b, c, d, flip=False):
        if flip:
            faces.append((a, d, c))
            faces.append((a, c, b))
        else:
            faces.append((a, b, c))
            faces.append((a, c, d))

    for i in range(rings):
        for j in range(sectors):
            a = top_start + i * stride + j
            b = top_start + (i + 1) * stride + j
            c = top_start + (i + 1) * stride + j + 1
            d = top_start + i * stride + j + 1
            quad(a, b, c, d)
    for i in range(under_rings):
        for j in range(sectors):
            a = under_start + i * stride + j
            b = under_start + (i + 1) * stride + j
            c = under_start + (i + 1) * stride + j + 1
            d = under_start + i * stride + j + 1
            quad(a, b, c, d, flip=True)

    obj = new_mesh_object(name, verts, faces, uvs, coll)
    obj.data.materials.append(leaf_material(fantasy))
    obj.data.materials.append(vein_material(fantasy))

    # The veins get their own narrow strips so they read as ribs rather than as painted wedges.
    vmesh = bpy.data.meshes.new(name + "Veins")
    vverts = []
    vfaces = []
    vuvs = []
    for i in range(rings + 1):
        t = 0.5 * (1.0 - math.cos(math.pi * i / rings))
        for j in range(sectors + 1):
            theta = math.tau * j / sectors
            p = point(t, theta, True)
            vm = vein_mask(theta, t, veins=veins, phase=vein_phase)
            vverts.append(p + up * (vein_crown * vm * 0.55))
            vuvs.append((0.026 * j / sectors, 0.5 + 0.5 * t))
    for v in range(veins):
        centre = (-vein_phase + math.tau * v) / veins
        for j in range(sectors):
            theta = math.tau * (j + 0.5) / sectors
            if abs(math.atan2(math.sin(theta - centre), math.cos(theta - centre))) > 0.030:
                continue
            for i in range(rings):
                a = i * stride + j
                b = (i + 1) * stride + j
                c = (i + 1) * stride + j + 1
                d = i * stride + j + 1
                vfaces.append((a, b, c))
                vfaces.append((a, c, d))
    vmesh.from_pydata([tuple(v) for v in vverts], [], vfaces)
    vmesh.update()
    vobj = bpy.data.objects.new(name + "Veins", vmesh)
    coll.objects.link(vobj)
    vobj.data.materials.append(vein_material(fantasy))
    vobj.parent = obj

    return obj


def make_petiole(name, base, height, lean, coll, rng, fantasy=False, radius=0.0011, spread=0.006):
    """A long, slender stalk that leaves the node, stands up, then arches over."""
    base = Vector(base)
    lean = Vector(lean).normalized()
    steps = 6
    path = []
    for i in range(steps + 1):
        t = i / steps
        rise = height * (t ** 0.92)
        reach = spread * (t ** 1.7)
        path.append(base + Vector((0.0, 0.0, rise)) + lean * reach)
    radii = [radius * (1.0 - 0.42 * i / steps) for i in range(steps + 1)]
    radii[-1] = radius * 1.18

    verts = []
    faces = []
    sides = 8
    for i, p in enumerate(path):
        nxt = path[min(steps, i + 1)]
        prv = path[max(0, i - 1)]
        tangent = (nxt - prv).normalized()
        side = tangent.cross(Vector((0.0, 0.0, 1.0)))
        if side.length < 1e-6:
            side = tangent.cross(Vector((1.0, 0.0, 0.0)))
        side.normalize()
        up2 = side.cross(tangent).normalized()
        for s in range(sides + 1):
            th = math.tau * s / sides
            verts.append(p + (side * math.cos(th) + up2 * math.sin(th)) * radii[i])
    for i in range(steps):
        for s in range(sides):
            a = i * (sides + 1) + s
            b = (i + 1) * (sides + 1) + s
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(petiole_material(fantasy))
    return obj, path[-1]


def make_stolon(name, path, radius, coll, rng, bristles=True):
    """A creeping runner: swollen at the nodes, clothed in fine root hairs."""
    steps = len(path) - 1
    verts = []
    faces = []
    sides = 8
    for i, p in enumerate(path):
        nxt = path[min(steps, i + 1)]
        prv = path[max(0, i - 1)]
        tangent = (nxt - prv).normalized()
        side = tangent.cross(Vector((0.0, 0.0, 1.0)))
        if side.length < 1e-6:
            side = tangent.cross(Vector((1.0, 0.0, 0.0)))
        side.normalize()
        up2 = side.cross(tangent).normalized()
        r = radius * (1.0 - 0.28 * i / steps)
        for s in range(sides + 1):
            th = math.tau * s / sides
            verts.append(p + (side * math.cos(th) + up2 * math.sin(th)) * r)
    for i in range(steps):
        for s in range(sides):
            a = i * (sides + 1) + s
            b = (i + 1) * (sides + 1) + s
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(stolon_material())

    if bristles:
        bverts = []
        bfaces = []
        bsides = 4
        for i in range(1, steps):
            p = path[i]
            tangent = (path[i + 1] - path[i - 1]).normalized()
            for h in range(rng.randint(1, 3)):
                a = rng.uniform(0.0, math.tau)
                out = Vector((math.cos(a), 0.0, math.sin(a))) + tangent * 0.55
                out.normalize()
                length = rng.uniform(0.0011, 0.0026)
                base = p + out * (radius * 0.55)
                tip = base + out * length + Vector((0.0, 0.0, length * 0.30))
                start = len(bverts)
                for k, (pp, rr) in enumerate(((base, 0.00019), (tip, 0.00007))):
                    tang = (tip - base).normalized()
                    sd = tang.cross(Vector((0.0, 0.0, 1.0)))
                    if sd.length < 1e-6:
                        sd = tang.cross(Vector((1.0, 0.0, 0.0)))
                    sd.normalize()
                    u2 = sd.cross(tang).normalized()
                    for s in range(bsides + 1):
                        th = math.tau * s / bsides
                        bverts.append(pp + (sd * math.cos(th) + u2 * math.sin(th)) * rr)
                for s in range(bsides):
                    a0 = start + s
                    b0 = start + bsides + 1 + s
                    bfaces.append((a0, b0, b0 + 1))
                    bfaces.append((a0, b0 + 1, a0 + 1))
        bobj = new_mesh_object(name + "Bristles", bverts, bfaces, None, coll)
        bobj.data.materials.append(root_material())
        bobj.parent = obj
    return obj


def make_node_roots(name, node, coll, rng, count=3):
    """Adventitious roots pegging the runner to the soil."""
    verts = []
    faces = []
    sides = 5
    for r in range(count):
        a = rng.uniform(0.0, math.tau)
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        length = rng.uniform(0.0045, 0.0125)
        pts = [node + d * 0.0004,
               node + d * (length * 0.45) - Vector((0.0, 0.0, 0.0034)),
               node + d * length - Vector((0.0, 0.0, 0.0098))]
        radii = [0.00042, 0.00024, 0.00008]
        start = len(verts)
        for i, p in enumerate(pts):
            nxt = pts[min(len(pts) - 1, i + 1)]
            prv = pts[max(0, i - 1)]
            tang = (nxt - prv).normalized()
            sd = tang.cross(Vector((0.0, 0.0, 1.0)))
            if sd.length < 1e-6:
                sd = tang.cross(Vector((1.0, 0.0, 0.0)))
            sd.normalize()
            u2 = sd.cross(tang).normalized()
            for s in range(sides + 1):
                th = math.tau * s / sides
                verts.append(p + (sd * math.cos(th) + u2 * math.sin(th)) * radii[i])
        for i in range(len(pts) - 1):
            for s in range(sides):
                a0 = start + i * (sides + 1) + s
                b0 = start + (i + 1) * (sides + 1) + s
                faces.append((a0, b0, b0 + 1))
                faces.append((a0, b0 + 1, a0 + 1))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(root_material())
    return obj


def make_umbel(name, base, height, coll, rng, fantasy=False):
    """The tiny head of minute green-white florets a pennywort throws above the mat."""
    verts = []
    faces = []
    centre = Vector(base) + Vector((0.0, 0.0, height))
    for f in range(rng.randint(7, 11)):
        theta = f * 2.399963229728653
        ring = 0.35 + 0.65 * (f / 9.0)
        d = Vector((math.cos(theta) * ring, 0.55 - 0.45 * ring, math.sin(theta) * ring)).normalized()
        size = rng.uniform(0.0007, 0.0011)
        c = centre + d * rng.uniform(0.0009, 0.0020)
        sd = Vector((0.0, 0.0, 1.0)).cross(d)
        if sd.length < 1e-6:
            sd = Vector((1.0, 0.0, 0.0))
        sd.normalize()
        u2 = d.cross(sd).normalized()
        start = len(verts)
        verts.append(c)
        for k in range(6):
            a = math.tau * k / 5.0
            petal = 1.0 + 0.35 * math.cos(5.0 * a)
            verts.append(c + (sd * math.cos(a) + u2 * math.sin(a)) * (size * petal) + d * (size * 0.18))
        for k in range(5):
            faces.append((start, start + 1 + k, start + 2 + k))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(floret_material(fantasy))
    return obj


def build_reference_mat(rng, fantasy=False, radius=0.19, height=0.055):
    coll = bpy.context.scene.collection
    runners = 8
    for k in range(runners):
        base_az = math.tau * k / runners + rng.uniform(-0.22, 0.22)
        reach = radius * rng.uniform(0.80, 1.04)
        steps = 11
        path = []
        meander = rng.uniform(0.0, math.tau)
        for s in range(steps + 1):
            t = s / steps
            ang = base_az + 0.30 * math.sin(t * 3.1 + meander) + 0.13 * math.sin(t * 7.7 + meander * 1.9)
            r = t * reach
            y = 0.0017 + 0.0011 * math.sin(t * 5.7 + meander) + 0.0007 * math.sin(t * 11.3 + meander * 2.3)
            path.append(Vector((math.cos(ang) * r, math.sin(ang) * r, y)))
        make_stolon("stolon_%d" % k, path, 0.0015, coll, rng)

        node_steps = [3, 5, 7, 9]
        for idx, ns in enumerate(node_steps):
            node = path[ns]
            tangent = (path[min(steps, ns + 1)] - path[max(0, ns - 1)]).normalized()
            make_node_roots("roots_%d_%d" % (k, idx), node, coll, rng, count=rng.randint(2, 3))
            petioles = 2 if rng.random() < 0.18 else 1
            for p in range(petioles):
                az = rng.uniform(0.0, math.tau)
                lean = Vector((math.cos(az), math.sin(az), 0.0))
                blade_r = rng.uniform(0.015, 0.034)
                pet_h = height * rng.uniform(0.38, 0.88)
                spread = blade_r * rng.uniform(0.12, 0.45)
                _, tip = make_petiole("petiole_%d_%d_%d" % (k, idx, p), node, pet_h, lean, coll, rng,
                                      fantasy=fantasy, radius=0.00105, spread=spread)
                tilt = rng.uniform(0.04, 0.32)
                up = Vector((0.0, 0.0, 1.0)) * math.cos(tilt) + lean * math.sin(tilt)
                up.normalize()
                silver = None
                if fantasy and rng.random() < 0.20:
                    silver = rng.uniform(0.0, math.tau)
                make_blade("blade_%d_%d_%d" % (k, idx, p), tip + up * (blade_r * 0.030), up,
                           blade_r, coll, rng, fantasy=fantasy,
                           veins=rng.choice([7, 8, 9]),
                           lobe_phase=rng.uniform(0.0, math.tau),
                           vein_phase=rng.uniform(0.0, math.tau),
                           silver_sector=silver)

    # A couple of flower heads above the mat.
    for f in range(2):
        fa = rng.uniform(0.0, math.tau)
        fr = radius * rng.uniform(0.18, 0.55)
        base = Vector((math.cos(fa) * fr, math.sin(fa) * fr, 0.0016))
        make_umbel("umbel_%d" % f, base, height * rng.uniform(0.72, 0.95), coll, rng, fantasy=fantasy)


def build_blade_detail(rng, fantasy=False):
    """A single blade, large in frame, to study the peltate anatomy."""
    coll = bpy.context.scene.collection
    centre = Vector((0.0, 0.0, 0.0))
    make_blade("detail_blade", centre, Vector((0.0, 0.0, 1.0)), 0.030, coll, rng,
               fantasy=fantasy, veins=9, rings=9, sectors=64)


def build_from_obj(path, scale):
    coll = bpy.context.scene.collection
    bpy.ops.wm.obj_import(filepath=path)
    objs = [o for o in bpy.context.selected_objects]
    for o in objs:
        o.scale = scale
        o.name = "Engine" + o.name
    return objs


def add_ground(radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=radius, depth=0.025,
                                        location=(0.0, 0.0, -0.013))
    ground = bpy.context.object
    ground.name = "Ground"
    ground.data.materials.append(soil_material())


def setup_camera_and_light(size=0.594, elevation=26.3, azimuth=45.0):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    e = math.radians(elevation)
    a = math.radians(azimuth)
    cam.location = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e))) * 1.6
    target = bpy.data.objects.new("Target", None)
    target.location = (0.0, 0.0, 0.0)
    scene.collection.objects.link(target)
    track = cam.constraints.new('TRACK_TO')
    track.target = target
    track.track_axis = 'TRACK_NEGATIVE_Z'
    track.up_axis = 'UP_Y'

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 4.0
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.96, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(38.0), math.radians(12.0), math.radians(-40.0))
    scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 8.0
    fill_data.size = 0.8
    fill_data.color = (0.72, 0.82, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.55, 0.35, 0.45)
    fill.rotation_euler = (math.radians(58.0), 0.0, math.radians(-125.0))
    scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.30, 0.40, 0.52, 1.0)
    bg.inputs[1].default_value = 0.35
    scene.world = world


def setup_render(res=1024, samples=96):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Filmic' if 'Filmic' in [
        t.name for t in scene.view_settings.bl_rna.properties['view_transform'].enum_items
    ] else scene.view_settings.view_transform


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference", choices=["reference", "fantasy", "mesh", "blade"])
    ap.add_argument("--obj", default="")
    ap.add_argument("--out", default=".")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--elevation", type=float, default=26.3)
    args = ap.parse_args(argv)

    reset_scene()
    rng = random.Random(args.seed)
    os.makedirs(args.out, exist_ok=True)

    if args.mode == "mesh":
        build_from_obj(args.obj, (0.20, 0.06, 0.20))
        add_ground(0.297)
        setup_camera_and_light(elevation=args.elevation)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_mesh.png")
    elif args.mode == "blade":
        build_blade_detail(rng, fantasy=(args.seed % 2 == 1))
        setup_camera_and_light(size=0.075, elevation=48.0, azimuth=35.0)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "mooncoin_blade_detail.png")
    else:
        build_reference_mat(rng, fantasy=(args.mode == "fantasy"))
        add_ground(0.297)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "mooncoin_%s.png" % args.mode)

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


if __name__ == "__main__":
    main()
