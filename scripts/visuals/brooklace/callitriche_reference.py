"""Water starwort (Callitriche stagnalis) reference mat for the Vivarium 'brooklace' groundcover.

This is the real-life reference the brooklace species is rebuilt against, plus the fantastical twist
the species carries in game. It builds a creeping water-starwort mat in Blender from the species' own
anatomy and renders it under matched light:

  * thin, pale, thread-like runners that creep over wet mud and lift towards the surface,
  * a swollen node ring at every node where the leaf pair rises,
  * opposite, glossy, fleshy spatulate leaves, narrow on the submerged stem and broader in the
    floating rosettes at the runner's tip,
  * white adventitious rootlets branching into fine threads at the nodes,
  * tiny four-petalled flowers in the leaf axils.

The fantasy variant is the in-game Brooklace: the rootlets are not roots but lace, branching again
and again into fine white threads that knit the mud together, and the leaves are glassy and
translucent at their margins the way wet starwort leaves are.

The same morphology is mirrored by the engine generator in
``src/Vivarium.Sim/Geometry/OrganismMeshes.Brooklace.cs``.

Usage:
    blender --background --python callitriche_reference.py -- --mode reference --out DIR
    blender --background --python callitriche_reference.py -- --mode fantasy   --out DIR
    blender --background --python callitriche_reference.py -- --mode clump     --out DIR
    blender --background --python callitriche_reference.py -- --mode mesh --obj FILE --out DIR
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

RUNNER_RADIUS = 0.0013
LEAF_LENGTH = 0.0105

# The leaf's girth along its length: width factor, then thickness factor.
LEAF_PROFILE = [
    (0.00, 0.10, 0.14),
    (0.10, 0.40, 0.54),
    (0.26, 0.72, 0.88),
    (0.46, 0.92, 1.00),
    (0.66, 1.00, 0.95),
    (0.82, 0.93, 0.74),
    (0.93, 0.72, 0.50),
    (1.00, 0.28, 0.18),
]


def profile(t):
    u = min(1.0, max(0.0, t))
    for i in range(len(LEAF_PROFILE) - 1):
        t0, w0, h0 = LEAF_PROFILE[i]
        t1, w1, h1 = LEAF_PROFILE[i + 1]
        if u < t0 or u > t1:
            continue
        f = (u - t0) / max(1e-9, t1 - t0)
        f = f * f * (3.0 - 2.0 * f)
        return (w0 + (w1 - w0) * f, h0 + (h1 - h0) * f)
    return (LEAF_PROFILE[0][1], LEAF_PROFILE[0][2])


# --------------------------------------------------------------------------
# scene helpers
# --------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_mesh_object(name, verts, faces, coll, smooth=True):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], faces)
    mesh.update()
    if smooth:
        for poly in mesh.polygons:
            poly.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    return obj


def _principled(mat):
    mat.use_nodes = True
    return mat.node_tree.nodes.get("Principled BSDF")


def _set(node, name, value):
    if name in node.inputs:
        node.inputs[name].default_value = value


def runner_material():
    mat = bpy.data.materials.new("StarwortRunner")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.30, 0.52, 0.20, 1.0))
    _set(bsdf, "Roughness", 0.40)
    return mat


def leaf_material(fantasy=False):
    mat = bpy.data.materials.new("StarwortLeaf")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.26, 0.56, 0.22, 1.0))
        _set(bsdf, "Roughness", 0.08)
        _set(bsdf, "Coat Weight", 0.5)
        _set(bsdf, "Coat Roughness", 0.04)
        _set(bsdf, "Subsurface Weight", 0.22)
    else:
        _set(bsdf, "Base Color", (0.22, 0.52, 0.18, 1.0))
        _set(bsdf, "Roughness", 0.16)
        _set(bsdf, "Subsurface Weight", 0.16)
    return mat


def lace_material():
    mat = bpy.data.materials.new("LaceRootlet")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.92, 0.94, 0.88, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def petal_material():
    mat = bpy.data.materials.new("BrooklacePetal")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.72, 0.64, 0.88, 1.0))
    _set(bsdf, "Roughness", 0.40)
    return mat


def eye_material():
    mat = bpy.data.materials.new("BrooklaceEye")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.96, 0.90, 0.38, 1.0))
    _set(bsdf, "Roughness", 0.35)
    return mat


def mud_material():
    mat = bpy.data.materials.new("Silt")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.055, 0.048, 0.036, 1.0))
    _set(bsdf, "Roughness", 0.90)
    return mat


# --------------------------------------------------------------------------
# the plant
# --------------------------------------------------------------------------

def make_leaf(name, attach, fwd, side, top, length, width, girth, coll, rng, fantasy=False):
    """A glossy, keeled spatulate starwort leaf."""
    rings, around = 8, 10
    verts, faces = [], []
    for i in range(rings + 1):
        t = i / rings
        wf, tf = profile(t)
        lift = math.sin(math.pi * t) * length * 0.07
        for j in range(around + 1):
            th = math.tau * j / around
            rx = width * wf
            ry = girth * tf
            k = 1.0 - 0.22 * max(0.0, -math.sin(th)) * wf
            verts.append(attach + fwd * (length * t) + top * lift
                         + side * (math.cos(th) * rx) + top * (math.sin(th) * ry * k))
    for i in range(rings):
        for j in range(around):
            a = i * (around + 1) + j
            b = a + around + 1
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(leaf_material(fantasy))
    return obj


def make_rootlet(name, origin, direction, length, radius, depth, coll, rng):
    """A white rootlet, branching again and again: the lace."""
    verts, faces = [], []
    tip = origin + direction * length
    mid = origin + direction * (length * 0.5)
    sides = 4
    pts = (origin, mid, tip)
    rads = (radius, radius * 0.62, radius * 0.22)
    for i, p in enumerate(pts):
        tang = (pts[min(2, i + 1)] - pts[max(0, i - 1)]).normalized()
        sd = tang.cross(Vector((0.0, 0.0, 1.0)))
        if sd.length < 1e-6:
            sd = tang.cross(Vector((1.0, 0.0, 0.0)))
        sd.normalize()
        u2 = sd.cross(tang).normalized()
        for s in range(sides + 1):
            th = math.tau * s / sides
            verts.append(p + (sd * math.cos(th) + u2 * math.sin(th)) * rads[i])
    for i in range(2):
        for s in range(sides):
            a = i * (sides + 1) + s
            b = a + sides + 1
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(lace_material())

    if depth <= 0:
        return
    for b in range(3 if rng.random() < 0.55 else 2):
        a = rng.uniform(0.0, math.tau)
        side = Vector((math.cos(a), math.sin(a), 0.0))
        nd = (direction * 0.72 + side * 0.55).normalized()
        make_rootlet(name + "_b", tip, nd, length * rng.uniform(0.45, 0.72), radius * 0.62, depth - 1, coll, rng)


def make_flower(name, centre, normal, radius, coll, rng):
    """An axillary starwort blossom: four pale lilac petals with a golden eye."""
    normal = normal.normalized()
    side = normal.cross(Vector((1.0, 0.0, 0.0)) if abs(normal.z) > 0.9 else Vector((0.0, 0.0, 1.0)))
    side.normalize()
    up = side.cross(normal).normalized()

    petals, steps = 4, 24
    verts = [centre]
    for i in range(steps + 1):
        th = math.tau * i / steps
        r = radius * (0.56 + 0.44 * math.cos(th * petals))
        verts.append(centre + side * (math.cos(th) * r) + up * (math.sin(th) * r))
    faces = [(0, i + 1, i + 2) for i in range(steps)]
    obj = new_mesh_object(name, verts, faces, coll)
    obj.data.materials.append(petal_material())

    verts, faces = [], []
    for a in range(4):
        th = a * math.tau / 4 + 0.6
        d = side * math.cos(th) + up * math.sin(th)
        start = len(verts)
        verts.append(centre + normal * (radius * 0.06))
        verts.append(centre + d * (radius * 0.75) + normal * (radius * 0.18))
        faces.append((start, start + 1, start + 2))
    eye = new_mesh_object(name + "Eye", verts, faces, coll, smooth=False)
    eye.data.materials.append(eye_material())


def build_reference_mat(rng, fantasy=False, runners=8):
    coll = bpy.context.scene.collection

    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=8, radius=1.0, location=(0.0, 0.0, 0.004))
    crown = bpy.context.object
    crown.name = "Crown"
    crown.scale = (0.009, 0.009, 0.004)
    crown.data.materials.append(runner_material())

    for k in range(runners):
        base_ang = math.tau * k / runners + rng.uniform(-0.26, 0.26)
        total_reach = rng.uniform(0.080, 0.110)
        segs = 7
        path, rads = [], []
        ang = base_ang
        pos = Vector((0.0, 0.0, 0.005))
        path.append(pos)
        rads.append(RUNNER_RADIUS * 1.3)
        for s in range(1, segs + 1):
            st = s / segs
            ang += rng.uniform(-0.26, 0.26)
            step = (total_reach / segs) * rng.uniform(0.88, 1.12)
            z = 0.004 + 0.016 * (st * st)
            pos = Vector((pos.x + math.cos(ang) * step, pos.y + math.sin(ang) * step, z))
            path.append(pos)
            rads.append(RUNNER_RADIUS * (1.0 - 0.42 * st) * (1.0 + (0.30 if s % 2 == 0 else 0.0)))
        verts, faces = [], []
        sides = 6
        for i, p in enumerate(path):
            tang = (path[min(segs, i + 1)] - path[max(0, i - 1)]).normalized()
            sd = tang.cross(Vector((0.0, 0.0, 1.0)))
            if sd.length < 1e-6:
                sd = tang.cross(Vector((1.0, 0.0, 0.0)))
            sd.normalize()
            u2 = sd.cross(tang).normalized()
            for s in range(sides + 1):
                th = math.tau * s / sides
                verts.append(p + (sd * math.cos(th) + u2 * math.sin(th)) * rads[i])
        for i in range(segs):
            for s in range(sides):
                a = i * (sides + 1) + s
                b = a + sides + 1
                faces.append((a, b, b + 1))
                faces.append((a, b + 1, a + 1))
        runner = new_mesh_object("runner_%d" % k, verts, faces, coll)
        runner.data.materials.append(runner_material())

        for j in range(1, segs):
            node = path[j]
            fwd = (path[j + 1] - path[j - 1]).normalized()
            side = fwd.cross(Vector((0.0, 0.0, 1.0)))
            if side.length < 1e-6:
                side = fwd.cross(Vector((1.0, 0.0, 0.0)))
            side.normalize()

            for r in range(1 + (1 if rng.random() < 0.5 else 0)):
                a = rng.uniform(0.0, math.tau)
                d = (Vector((math.cos(a), math.sin(a), 0.0)) * 0.8 - Vector((0.0, 0.0, 0.9))).normalized()
                make_rootlet("lace_%d_%d_%d" % (k, j, r), node - Vector((0.0, 0.0, 0.0008)), d,
                             rng.uniform(0.006, 0.013), 0.00030, 2, coll, rng)

            for s in (-1, 1):
                d = (side * s * 0.86 + fwd * rng.uniform(0.10, 0.30) + Vector((0.0, 0.0, rng.uniform(0.08, 0.26))))
                d.normalize()
                across = d.cross(Vector((0.0, 0.0, 1.0)))
                if across.length < 1e-6:
                    across = d.cross(Vector((1.0, 0.0, 0.0)))
                ln = LEAF_LENGTH * rng.uniform(0.72, 1.20) * (1.0 - 0.18 * j / segs)
                make_leaf("leaf_%d_%d_%d" % (k, j, s), node, d, across, Vector((0.0, 0.0, 1.0)),
                          ln, ln * 0.30, ln * 0.19, coll, rng, fantasy=fantasy)

            if j >= 2 and rng.random() < 0.38:
                fd = (side * rng.uniform(-0.7, 0.7) + Vector((0.0, 0.0, 0.9)) + fwd * 0.2).normalized()
                stalk = node + fd * 0.006
                make_flower("flower_%d_%d" % (k, j), stalk + fd * 0.0012, fd, 0.0030, coll, rng)

        # The floating rosette at the runner's tip.
        tip = path[-1]
        tip_fwd = (path[-1] - path[-2]).normalized()
        count = 8 + rng.randint(0, 3)
        for r in range(count):
            a = math.tau * r / count + rng.uniform(-0.14, 0.14)
            d = (Vector((math.cos(a), math.sin(a), 0.0)) * 0.86 + tip_fwd * 0.42 + Vector((0.0, 0.0, 0.06)))
            d.normalize()
            across = d.cross(Vector((0.0, 0.0, 1.0)))
            if across.length < 1e-6:
                across = d.cross(Vector((1.0, 0.0, 0.0)))
            ln = LEAF_LENGTH * rng.uniform(1.15, 1.65) * (0.88 + 0.20 * math.cos(a))
            make_leaf("rosette_%d_%d" % (k, r), tip, d, across, Vector((0.0, 0.0, 1.0)),
                      ln, ln * 0.38, ln * 0.17, coll, rng, fantasy=fantasy)


def build_from_obj(path, scale):
    bpy.ops.wm.obj_import(filepath=path)
    for o in bpy.context.selected_objects:
        o.scale = scale
        o.name = "Engine" + o.name


def add_ground(radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=radius, depth=0.025,
                                        location=(0.0, 0.0, -0.013))
    ground = bpy.context.object
    ground.name = "Ground"
    ground.data.materials.append(mud_material())


def setup_camera_and_light(size=0.594, elevation=26.3, azimuth=45.0, target=(0.0, 0.0, 0.0)):
    scene = bpy.context.scene
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    e = math.radians(elevation)
    a = math.radians(azimuth)
    cam.location = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e))) * 1.6 + Vector(target)
    tgt = bpy.data.objects.new("Target", None)
    tgt.location = target
    scene.collection.objects.link(tgt)
    track = cam.constraints.new('TRACK_TO')
    track.target = tgt
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
    ap.add_argument("--mode", default="reference", choices=["reference", "fantasy", "mesh", "clump"])
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
        build_from_obj(args.obj, (0.11, 0.10, 0.11))
        add_ground(0.297)
        setup_camera_and_light(elevation=args.elevation)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_mesh.png")
    elif args.mode == "clump":
        build_reference_mat(rng, fantasy=(args.seed % 2 == 1))
        setup_camera_and_light(size=0.20, elevation=34.0, azimuth=35.0, target=(0.0, 0.0, 0.02))
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "callitriche_clump_detail.png")
    else:
        build_reference_mat(rng, fantasy=(args.mode == "fantasy"))
        add_ground(0.297)
        setup_camera_and_light(size=0.34, elevation=30.0, azimuth=45.0, target=(0.0, 0.0, 0.01))
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "callitriche_%s.png" % args.mode)

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


if __name__ == "__main__":
    main()
