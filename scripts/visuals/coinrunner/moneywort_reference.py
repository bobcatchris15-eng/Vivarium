"""Moneywort reference mat for the Vivarium 'coinrunner' groundcover.

This is the real-life reference the coinrunner species is rebuilt against, plus the fantastical
twist the species carries in game. It builds a creeping reniform-leafed mat in Blender from the
botany of the round-leaved mat formers:

  * moneywort and marsh pennywort (Hydrocotyle vulgaris, H. americana) for the creeping stolon
    that roots at the nodes, the long petiole, the round blade held flat at the end of it and the
    minute umbel thrown above the mat,
  * kidneyweed (Dichondra repens) for the reniform blade itself, with its deep basal sinus, the
    fine crenate margin and the fan of veins that leaves the sinus,
  * white clover (Trifolium repens) for the mat's construction: the runner that swells at the node,
    the rooting tuft, the reduced bract and the shingled canopy a stolon mat actually carries.

The fantasy variant is the in-game Coinrunner: the blades read like old coins gone green, with a
verdigris patina, warm bronze showing through, pale gold tracery along the veins and a raised
circular bead just inside the margin, and every runner tip carries a warm sap-light because this
is the plant that is always stitching new ground into the mat.

The same morphology is mirrored by the engine generator in
``src/Vivarium.Sim/Geometry/OrganismMeshes.Coinrunner.cs``.

Usage:
    blender --background --python moneywort_reference.py -- --mode reference --out DIR
    blender --background --python moneywort_reference.py -- --mode fantasy   --out DIR
    blender --background --python moneywort_reference.py -- --mode blade     --out DIR
    blender --background --python moneywort_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Vector

# --------------------------------------------------------------------------
# morphology
# --------------------------------------------------------------------------

LOBES = 13                 # fine crenations around the margin
LOBE_DEPTH = 0.026
VEINS = 6                  # primary veins fanning out of the sinus
SINUS_OFFSET = 0.34        # disc centre distance from the attachment, as a fraction of the radius
SINUS_WIDTH = 0.34         # half width of the sinus, radians

DISH = 0.055               # sag of the blade, fraction of the radius
RIM_LIFT = 0.026
BEAD = 0.036               # the coin's raised border
WAVINESS = 0.013


def _smoothstep(a, b, x):
    if b <= a:
        return 0.0 if x < a else 1.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _smoothstep_inv(a, b, x):
    return 1.0 - _smoothstep(a, b, x)


def _wrap(a):
    while a > math.pi:
        a -= math.tau
    while a < -math.pi:
        a += math.tau
    return a


def sinus_profile(theta):
    """1 at the bottom of the sinus, 0 outside it."""
    d = _wrap(theta + math.pi * 0.5)
    k = min(1.0, max(0.0, 1.0 - abs(d) / SINUS_WIDTH))
    return math.sqrt(k)


def rim_scale(theta):
    notch = 1.0 - (1.0 - SINUS_OFFSET) * sinus_profile(theta)
    cren = 1.0 + LOBE_DEPTH * math.cos(LOBES * theta)
    return notch * cren


def vein_ridge(alpha, dist):
    """Ridge of the primary veins, spaced by the angle they make at the attachment."""
    step = math.pi / VEINS
    a = alpha / step
    f = a - math.floor(a) - 0.5
    d = abs(f) * 2.0
    return _smoothstep_inv(0.62, 0.30, d) * _smoothstep(0.05, 0.20, dist)


def bead_mask(t, theta):
    ring = _smoothstep(0.66, 0.78, t) * (1.0 - _smoothstep(0.87, 0.97, t))
    return ring * (1.0 - sinus_profile(theta))


def blade_z(t, theta):
    dish = -DISH * _smoothstep(0.0, 0.62, t)
    rim = RIM_LIFT * (_smoothstep(0.55, 1.0, t) ** 1.6)
    wob = WAVINESS * math.sin(3.0 * theta) * math.sin(math.pi * min(1.0, t * 1.1))
    wrinkle = WAVINESS * 0.42 * math.sin(7.0 * theta) * math.sin(math.pi * min(1.0, t * 1.2))
    return dish + rim + wob + wrinkle


# --------------------------------------------------------------------------
# scene helpers
# --------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_mesh_object(name, verts, faces, coll):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    return obj


def _principled(mat):
    mat.use_nodes = True
    return mat.node_tree.nodes.get("Principled BSDF")


def _set(node, name, value):
    if name in node.inputs:
        node.inputs[name].default_value = value


def leaf_material(fantasy=False):
    mat = bpy.data.materials.new("CoinrunnerLeaf")
    bsdf = _principled(mat)
    if fantasy:
        # Old coin gone green: a cool verdigris film over the living green.
        _set(bsdf, "Base Color", (0.115, 0.285, 0.105, 1.0))
        _set(bsdf, "Roughness", 0.52)
        _set(bsdf, "Subsurface Weight", 0.10)
        _set(bsdf, "Specular IOR Level", 0.30)
    else:
        _set(bsdf, "Base Color", (0.085, 0.290, 0.055, 1.0))
        _set(bsdf, "Roughness", 0.46)
        _set(bsdf, "Subsurface Weight", 0.16)
        _set(bsdf, "Specular IOR Level", 0.34)
    return mat


def vein_material(fantasy=False):
    mat = bpy.data.materials.new("CoinrunnerVein")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.33, 0.29, 0.13, 1.0))
        _set(bsdf, "Roughness", 0.42)
    else:
        _set(bsdf, "Base Color", (0.20, 0.40, 0.12, 1.0))
        _set(bsdf, "Roughness", 0.34)
    return mat


def petiole_material():
    mat = bpy.data.materials.new("CoinrunnerPetiole")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.16, 0.36, 0.09, 1.0))
    _set(bsdf, "Roughness", 0.46)
    return mat


def stolon_material(fantasy=False):
    mat = bpy.data.materials.new("CoinrunnerStolon")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.20, 0.34, 0.11, 1.0))
    else:
        _set(bsdf, "Base Color", (0.14, 0.30, 0.09, 1.0))
    _set(bsdf, "Roughness", 0.54)
    return mat


def root_material():
    mat = bpy.data.materials.new("CoinrunnerRoot")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.26, 0.21, 0.13, 1.0))
    _set(bsdf, "Roughness", 0.80)
    return mat


def floret_material():
    mat = bpy.data.materials.new("CoinrunnerFloret")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.92, 0.93, 0.84, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def soil_material():
    mat = bpy.data.materials.new("CoinrunnerSoil")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.048, 0.040, 0.030, 1.0))
    _set(bsdf, "Roughness", 0.95)
    return mat


# --------------------------------------------------------------------------
# the plant
# --------------------------------------------------------------------------

def make_blade(name, origin, up, body, radius, coll, rng, fantasy=False, veins=VEINS):
    """One reniform coin.

    The blade is a circle whose centre is offset from the petiole attachment, with a deep sinus
    cut back to that attachment, a fine crenate margin, a fan of veins leaving the sinus and a
    raised bead just inside the rim.
    """
    up = Vector(up).normalized()
    body = Vector(body)
    body = (body - up * body.dot(up)).normalized()
    side = up.cross(body).normalized()
    body = side.cross(up).normalized()

    offset = SINUS_OFFSET * radius
    centre = origin + body * offset

    thickness = radius * 0.030
    vein_crown = radius * 0.009
    vein_rib = radius * 0.014

    rings, sectors = 7, 48

    verts = []
    faces = []
    vein_verts = []
    vein_faces = []
    stride = sectors + 1

    def point(t, theta, upper):
        r = t * radius * rim_scale(theta)
        sb = offset + r * math.sin(theta)
        sc = r * math.cos(theta)
        alpha = math.atan2(sb, sc)
        dist = math.sqrt(sb * sb + sc * sc) / radius
        vm = vein_ridge(alpha, dist)
        z = blade_z(t, theta) * radius
        z += BEAD * radius * bead_mask(t, theta)
        if upper:
            z += thickness * 0.5 + vein_crown * vm
        else:
            z -= thickness * 0.5 + vein_rib * vm
        return origin + body * sb + side * sc + up * z, alpha, dist, vm

    for i in range(rings + 1):
        t = 0.5 * (1.0 - math.cos(math.pi * i / rings))
        for j in range(sectors + 1):
            theta = math.tau * j / sectors
            p, a, d, vm = point(t, theta, True)
            verts.append(p)
            vein_verts.append(p + up * (vein_crown * (0.9 + 0.5 * vm)))

    for i in range(rings):
        for j in range(sectors):
            a0 = i * stride + j
            b0 = (i + 1) * stride + j
            c0 = (i + 1) * stride + j + 1
            d0 = i * stride + j + 1
            faces.append((a0, b0, c0))
            faces.append((a0, c0, d0))

    # The veins as their own narrow strips, so they read as tracery and not as painted wedges.
    for v in range(veins):
        centre_angle = math.tau * v / veins
        for j in range(sectors):
            theta = math.tau * (j + 0.5) / sectors
            if abs(_wrap(theta - centre_angle)) > 0.020:
                continue
            for i in range(rings):
                a0 = i * stride + j
                b0 = (i + 1) * stride + j
                c0 = (i + 1) * stride + j + 1
                d0 = i * stride + j + 1
                vein_faces.append((a0, b0, c0))
                vein_faces.append((a0, c0, d0))

    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(leaf_material(fantasy))
    vobj = new_mesh_object(name + "Veins", vein_verts, vein_faces, coll)
    vobj.data.materials.append(vein_material(fantasy))
    vobj.parent = obj
    return obj


def make_petiole(name, base, height, lean, coll, radius=0.00125, spread=0.010):
    base = Vector(base)
    lean = Vector(lean).normalized()
    steps = 4
    path = []
    for i in range(steps + 1):
        t = i / steps
        rise = height * (t ** 0.92)
        reach = spread * (t ** 1.6)
        path.append(base + Vector((0.0, 0.0, rise)) + lean * reach)
    radii = [radius * (1.0 - 0.32 * i / steps) for i in range(steps + 1)]
    radii[-1] = radius * 1.10

    verts = []
    faces = []
    sides = 8
    for i, p in enumerate(path):
        nxt = path[min(steps, i + 1)]
        prv = path[max(0, i - 1)]
        tangent = (nxt - prv).normalized()
        sd = tangent.cross(Vector((0.0, 0.0, 1.0)))
        if sd.length < 1e-6:
            sd = tangent.cross(Vector((1.0, 0.0, 0.0)))
        sd.normalize()
        u2 = sd.cross(tangent).normalized()
        for s in range(sides + 1):
            th = math.tau * s / sides
            verts.append(p + (sd * math.cos(th) + u2 * math.sin(th)) * radii[i])
    for i in range(steps):
        for s in range(sides):
            a0 = i * (sides + 1) + s
            b0 = (i + 1) * (sides + 1) + s
            faces.append((a0, b0, b0 + 1))
            faces.append((a0, b0 + 1, a0 + 1))
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(petiole_material())
    return obj, path[-1]


def make_stolon(name, path, radius, coll, rng, fantasy=False):
    steps = len(path) - 1
    verts = []
    faces = []
    sides = 8
    for i, p in enumerate(path):
        nxt = path[min(steps, i + 1)]
        prv = path[max(0, i - 1)]
        tangent = (nxt - prv).normalized()
        sd = tangent.cross(Vector((0.0, 0.0, 1.0)))
        if sd.length < 1e-6:
            sd = tangent.cross(Vector((1.0, 0.0, 0.0)))
        sd.normalize()
        u2 = sd.cross(tangent).normalized()
        r = radius * (1.0 - 0.26 * i / steps)
        if fantasy:
            # The leading tip carries a warm sap-light.
            r *= 1.0 + 0.10 * max(0.0, (i / steps - 0.72) / 0.28)
        for s in range(sides + 1):
            th = math.tau * s / sides
            verts.append(p + (sd * math.cos(th) + u2 * math.sin(th)) * r)
    for i in range(steps):
        for s in range(sides):
            a0 = i * (sides + 1) + s
            b0 = (i + 1) * (sides + 1) + s
            faces.append((a0, b0, b0 + 1))
            faces.append((a0, b0 + 1, a0 + 1))
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(stolon_material(fantasy))
    return obj


def make_node_roots(name, node, coll, rng, count=3):
    verts = []
    faces = []
    sides = 5
    for r in range(count):
        a = rng.uniform(0.0, math.tau)
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        length = rng.uniform(0.0050, 0.0135)
        pts = [node + d * 0.0005,
               node + d * (length * 0.45) - Vector((0.0, 0.0, 0.0036)),
               node + d * length - Vector((0.0, 0.0, 0.0104))]
        radii = [0.00046, 0.00026, 0.00009]
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
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(root_material())
    return obj


def make_umbel(name, base, height, coll, rng):
    verts = []
    faces = []
    centre = Vector(base) + Vector((0.0, 0.0, height))
    for f in range(rng.randint(7, 11)):
        theta = f * 2.399963229728653
        ring = 0.35 + 0.65 * (f / 9.0)
        d = Vector((math.cos(theta) * ring, math.sin(theta) * ring, 0.55 - 0.45 * ring)).normalized()
        size = rng.uniform(0.0009, 0.0015)
        c = centre + d * rng.uniform(0.0010, 0.0020)
        sd = Vector((0.0, 0.0, 1.0)).cross(d)
        if sd.length < 1e-6:
            sd = Vector((1.0, 0.0, 0.0))
        sd.normalize()
        u2 = d.cross(sd).normalized()
        start = len(verts)
        verts.append(c)
        for k in range(6):
            a = math.tau * k / 5.0
            notch = 0.86 + 0.14 * math.cos(5.0 * a)
            verts.append(c + (sd * math.cos(a) + u2 * math.sin(a)) * (size * notch) + d * (size * 0.16))
        for k in range(5):
            faces.append((start, start + 1 + k, start + 2 + k))
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(floret_material())
    return obj


def build_reference_mat(rng, fantasy=False, radius=0.29, height=0.05):
    coll = bpy.context.scene.collection
    runners = 9
    for k in range(runners):
        base_az = math.tau * k / runners + rng.uniform(-0.20, 0.20)
        reach = radius * rng.uniform(0.82, 1.04)
        steps = 10
        meander = rng.uniform(0.0, math.tau)
        path = []
        for s in range(steps + 1):
            t = s / steps
            ang = base_az + 0.32 * math.sin(t * 2.9 + meander) + 0.14 * math.sin(t * 7.3 + meander * 1.8)
            r = t * reach
            y = 0.0019 + 0.0012 * math.sin(t * 5.3 + meander) + 0.0008 * math.sin(t * 10.7 + meander * 2.1)
            path.append(Vector((math.cos(ang) * r, math.sin(ang) * r, y)))
        make_stolon("stolon_%d" % k, path, 0.00165, coll, rng, fantasy=fantasy)

        for idx, ns in enumerate([3, 5, 7, 9]):
            node = path[ns]
            make_node_roots("roots_%d_%d" % (k, idx), node, coll, rng, count=rng.randint(2, 3))
            for p in range(2 if rng.random() < 0.28 else 1):
                az = rng.uniform(0.0, math.tau)
                lean = Vector((math.cos(az), math.sin(az), 0.0))
                blade_r = rng.uniform(0.020, 0.046)
                pet_h = height * rng.uniform(0.18, 0.58)
                spread = blade_r * rng.uniform(0.18, 0.52)
                _, tip = make_petiole("petiole_%d_%d_%d" % (k, idx, p), node, pet_h, lean, coll,
                                      spread=spread)
                tilt = rng.uniform(0.05, 0.36)
                up = Vector((0.0, 0.0, 1.0)) * math.cos(tilt) + lean * math.sin(tilt)
                up.normalize()
                make_blade("blade_%d_%d_%d" % (k, idx, p), tip, up, lean, blade_r, coll, rng,
                           fantasy=fantasy, veins=rng.choice([5, 6, 7]))

    for f in range(2):
        fa = rng.uniform(0.0, math.tau)
        fr = radius * rng.uniform(0.18, 0.55)
        base = Vector((math.cos(fa) * fr, math.sin(fa) * fr, 0.0018))
        make_umbel("umbel_%d" % f, base, height * rng.uniform(0.74, 0.96), coll, rng)


def build_blade_detail(rng, fantasy=False):
    """A single blade, large in frame, to study the reniform anatomy."""
    coll = bpy.context.scene.collection
    make_blade("detail_blade", Vector((0.0, 0.0, 0.0)), Vector((0.0, 0.0, 1.0)),
               Vector((0.0, 1.0, 0.0)), 0.045, coll, rng, fantasy=fantasy, veins=6)


def build_from_obj(path, scale):
    coll = bpy.context.scene.collection
    bpy.ops.wm.obj_import(filepath=path)
    for o in bpy.context.selected_objects:
        o.scale = scale
        o.name = "Engine" + o.name


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
        build_from_obj(args.obj, (0.30, 0.05, 0.30))
        add_ground(0.297)
        setup_camera_and_light(elevation=args.elevation)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_mesh.png")
    elif args.mode == "blade":
        build_blade_detail(rng, fantasy=(args.seed % 2 == 1))
        setup_camera_and_light(size=0.11, elevation=52.0, azimuth=35.0)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "moneywort_blade_detail.png")
    else:
        build_reference_mat(rng, fantasy=(args.mode == "fantasy"))
        add_ground(0.297)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "moneywort_%s.png" % args.mode)

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


if __name__ == "__main__":
    main()
