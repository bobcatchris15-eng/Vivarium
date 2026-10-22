"""Helms' crassula (Crassula helmsii) reference clump for the Vivarium 'fenbead' groundcover.

This is the real-life reference the fenbead species is rebuilt against, plus the fantastical twist
the species carries in game. It builds a water-cressula clump in Blender from the species' own
anatomy and renders it under matched light:

  * fleshy stems that run prostrate over wet mud and then arch up, reddened where they lie against
    the ground and green above it,
  * swollen node rings on the stem,
  * the connate collar where each pair of leaves is fused at its base and clasps the stem,
  * opposite decussate pairs of narrow, plump, glossy club-shaped leaves with bronze-red tips,
  * four-petalled white flowers with a cluster of brown anthers on short terminal stalks,
  * bronze-red buds at the upper nodes.

The fantasy variant is the in-game Fenbead: the beads are pearls, each leaf carrying a soft
nacreous lustre band along its upper face and a blush of colour at the tip, and the blossoms are
pearl-white with a golden eye.

The same morphology is mirrored by the engine generator in
``src/Vivarium.Sim/Geometry/OrganismMeshes.Fenbead.cs``.

Usage:
    blender --background --python crassula_reference.py -- --mode reference --out DIR
    blender --background --python crassula_reference.py -- --mode fantasy   --out DIR
    blender --background --python crassula_reference.py -- --mode clump     --out DIR
    blender --background --python crassula_reference.py -- --mode mesh --obj FILE --out DIR
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

STEM_RADIUS = 0.0026
LEAF_LENGTH = 0.0165

# The leaf's girth along its length: width factor, then thickness factor.
LEAF_PROFILE = [
    (0.00, 0.10, 0.16),
    (0.08, 0.34, 0.46),
    (0.20, 0.72, 0.82),
    (0.34, 0.94, 1.00),
    (0.50, 1.00, 0.94),
    (0.68, 0.93, 0.78),
    (0.84, 0.74, 0.56),
    (0.94, 0.46, 0.34),
    (1.00, 0.13, 0.11),
]


def profile(t):
    """Samples the leaf profile at a normalised position along the leaf."""
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


def _smoothstep(a, b, x):
    if b <= a:
        return 0.0 if x < a else 1.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


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


def stem_material():
    mat = bpy.data.materials.new("CrassulaStem")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.42, 0.24, 0.19, 1.0))
    _set(bsdf, "Roughness", 0.52)
    return mat


def leaf_material(fantasy=False):
    mat = bpy.data.materials.new("CrassulaLeaf")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.30, 0.46, 0.26, 1.0))
        _set(bsdf, "Roughness", 0.22)
        _set(bsdf, "Subsurface Weight", 0.18)
        _set(bsdf, "Coat Weight", 0.35)
        _set(bsdf, "Coat Roughness", 0.10)
    else:
        _set(bsdf, "Base Color", (0.26, 0.44, 0.20, 1.0))
        _set(bsdf, "Roughness", 0.34)
        _set(bsdf, "Subsurface Weight", 0.10)
    return mat


def blush_material():
    mat = bpy.data.materials.new("CrassulaBlush")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.56, 0.26, 0.20, 1.0))
    _set(bsdf, "Roughness", 0.40)
    return mat


def petal_material():
    mat = bpy.data.materials.new("CrassulaPetal")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.96, 0.95, 0.92, 1.0))
    _set(bsdf, "Roughness", 0.42)
    _set(bsdf, "Subsurface Weight", 0.12)
    return mat


def anther_material():
    mat = bpy.data.materials.new("CrassulaAnther")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.42, 0.30, 0.15, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def mud_material():
    mat = bpy.data.materials.new("Mud")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.07, 0.06, 0.045, 1.0))
    _set(bsdf, "Roughness", 0.92)
    return mat


# --------------------------------------------------------------------------
# the plant
# --------------------------------------------------------------------------

def make_bead(name, attach, fwd, side, top, length, width, girth, coll, rng, fantasy=False):
    """One plump, keeled, three-dimensional club leaf."""
    rings, around = 8, 10
    verts, faces = [], []
    for i in range(rings + 1):
        t = i / rings
        wf, tf = profile(t)
        lift = math.sin(math.pi * t) * length * 0.10
        for j in range(around + 1):
            th = math.tau * j / around
            rx = width * wf
            ry = girth * tf
            k = 1.0 - 0.18 * max(0.0, math.sin(th)) * wf
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


def make_flower(name, centre, normal, radius, coll, rng):
    """A crassula flower: four white petals over a cluster of brown anthers."""
    normal = normal.normalized()
    side = normal.cross(Vector((1.0, 0.0, 0.0)) if abs(normal.z) > 0.9 else Vector((0.0, 0.0, 1.0)))
    side.normalize()
    up = side.cross(normal).normalized()

    petals, steps = 4, 24
    verts = [centre]
    for i in range(steps + 1):
        th = math.tau * i / steps
        r = radius * (0.52 + 0.48 * math.cos(th * petals))
        verts.append(centre + side * (math.cos(th) * r) + up * (math.sin(th) * r))
    faces = []
    for i in range(steps):
        faces.append((0, i + 1, i + 2))
    petal = new_mesh_object(name + "Petals", verts, faces, coll)
    petal.data.materials.append(petal_material())

    verts, faces = [], []
    for a in range(5):
        th = a * math.tau / 5 + 0.4
        d = side * math.cos(th) + up * math.sin(th)
        base = centre + normal * (radius * 0.04)
        stalk = centre + d * (radius * 0.18) + normal * (radius * 0.10)
        tip = stalk + d * (radius * 0.16) + normal * (radius * 0.14)
        start = len(verts)
        for p, r in ((base, radius * 0.022), (stalk, radius * 0.018), (tip, radius * 0.030)):
            verts.append(p + d * r)
        for i in range(2):
            faces.append((start + i, start + i + 1, start + i + 2))
    anther = new_mesh_object(name + "Anthers", verts, faces, coll, smooth=False)
    anther.data.materials.append(anther_material())


def build_reference_clump(rng, fantasy=False, stems=14):
    coll = bpy.context.scene.collection

    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1.0, location=(0.0, 0.0, 0.004))
    crown = bpy.context.object
    crown.name = "Crown"
    crown.scale = (0.014, 0.014, 0.006)
    crown.data.materials.append(stem_material())

    for k in range(stems):
        az = math.tau * k / stems + rng.uniform(-0.24, 0.24)
        radial = Vector((math.cos(az), math.sin(az), 0.0))
        reach = rng.uniform(0.08, 0.165)
        rise = rng.uniform(0.055, 0.098)

        p0 = radial * rng.uniform(0.004, 0.020) + Vector((0.0, 0.0, 0.004))
        p1 = radial * (reach * 0.55) + Vector((0.0, 0.0, rise * 0.22))
        p2 = radial * reach + Vector((0.0, 0.0, rise))

        segs = 7
        pts, rads = [], []
        for si in range(segs + 1):
            t = si / segs
            u = 1.0 - t
            pts.append(p0 * (u * u) + p1 * (2 * u * t) + p2 * (t * t))
            ring = 1.0 + 0.34 * (abs(math.sin(t * math.pi * segs)) ** 3)
            rads.append(STEM_RADIUS * (1.0 - 0.42 * t) * ring)
        verts, faces = [], []
        sides = 8
        for i, p in enumerate(pts):
            tang = (pts[min(segs, i + 1)] - pts[max(0, i - 1)]).normalized()
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
        stem = new_mesh_object("stem_%d" % k, verts, faces, coll)
        stem.data.materials.append(stem_material())

        for si in range(1, segs + 1):
            attach = pts[si]
            fwd = (pts[si] - pts[si - 1]) if si == segs else (pts[si + 1] - pts[si - 1])
            fwd.normalize()
            side0 = fwd.cross(Vector((0.0, 0.0, 1.0)))
            if side0.length < 1e-6:
                side0 = fwd.cross(Vector((1.0, 0.0, 0.0)))
            side0.normalize()
            up0 = side0.cross(fwd).normalized()

            rot = 0.0 if si % 2 == 0 else math.pi * 0.5
            pair = side0 * math.cos(rot) + up0 * math.sin(rot)

            # The connate collar: the leaf bases are fused into a ring that clasps the stem.
            verts, faces = [], []
            cc = attach - fwd * (STEM_RADIUS * 0.6)
            for i, p in enumerate((cc - fwd * (STEM_RADIUS * 0.8), cc + fwd * (STEM_RADIUS * 0.8))):
                for s in range(9):
                    th = math.tau * s / 8
                    verts.append(p + (side0 * math.cos(th) + up0 * math.sin(th)) * STEM_RADIUS * 1.12)
            for s in range(8):
                faces.append((s, s + 9, s + 10))
                faces.append((s, s + 10, s + 1))
            collar = new_mesh_object("collar_%d_%d" % (k, si), verts, faces, coll)
            collar.data.materials.append(stem_material())

            ln = LEAF_LENGTH * rng.uniform(0.68, 1.15) * (1.0 - 0.22 * si / segs)
            wid = ln * rng.uniform(0.24, 0.32)
            girth = ln * rng.uniform(0.20, 0.27)
            for s in (-1, 1):
                d = (pair * s + fwd * rng.uniform(0.10, 0.34) + Vector((0.0, 0.0, rng.uniform(0.02, 0.16))))
                d.normalize()
                across = d.cross(Vector((0.0, 0.0, 1.0)))
                if across.length < 1e-6:
                    across = d.cross(Vector((1.0, 0.0, 0.0)))
                make_bead("leaf_%d_%d_%d" % (k, si, s), attach, d, across, Vector((0.0, 0.0, 1.0)),
                          ln, wid, girth, coll, rng, fantasy=fantasy)

            if si >= 3 and rng.random() < 0.28:
                bud_dir = (fwd * 0.5 + Vector((0.0, 0.0, 0.8))).normalized()
                make_bead("bud_%d_%d" % (k, si), attach + bud_dir * (ln * 0.2), bud_dir,
                          bud_dir.cross(Vector((0.0, 0.0, 1.0))), Vector((0.0, 0.0, 1.0)),
                          ln * 0.34, ln * 0.13, ln * 0.12, coll, rng, fantasy=False)

        if rise > 0.062 and rng.random() < 0.55:
            tip = pts[-1]
            tdir = (pts[-1] - pts[-2]).normalized()
            fr = 0.0056
            stalk_top = tip + tdir * (fr * 2.4)
            verts, faces = [], []
            for i, p in enumerate((tip, stalk_top)):
                for s in range(6):
                    th = math.tau * s / 5
                    r = 0.0009 - 0.0003 * i
                    verts.append(p + (tdir.cross(Vector((0.0, 0.0, 1.0))).normalized() * math.cos(th)
                              + Vector((0.0, 0.0, 1.0)) * math.sin(th)) * r)
            for s in range(5):
                faces.append((s, s + 6, s + 7))
                faces.append((s, s + 7, s + 1))
            stalk = new_mesh_object("flstalk_%d" % k, verts, faces, coll)
            stalk.data.materials.append(stem_material())
            make_flower("flower_%d" % k, stalk_top + tdir * (fr * 0.4), tdir, fr, coll, rng)


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
        build_from_obj(args.obj, (0.18, 0.10, 0.18))
        add_ground(0.297)
        setup_camera_and_light(elevation=args.elevation)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_mesh.png")
    elif args.mode == "clump":
        build_reference_clump(rng, fantasy=(args.seed % 2 == 1))
        setup_camera_and_light(size=0.26, elevation=32.0, azimuth=35.0, target=(0.0, 0.0, 0.04))
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "crassula_clump_detail.png")
    else:
        build_reference_clump(rng, fantasy=(args.mode == "fantasy"))
        add_ground(0.297)
        setup_camera_and_light(size=0.52, elevation=24.0, azimuth=45.0, target=(0.0, 0.0, 0.03))
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "crassula_%s.png" % args.mode)

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


if __name__ == "__main__":
    main()
