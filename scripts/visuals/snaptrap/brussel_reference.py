"""Reference for the Latchjaw: a Brussels-sprout column of jaw-leaves under a large fleshy flower.

The latchjaw is a small snap-trap carnivore. This reference is the growth form it is rebuilt
against: a stout upright column clasped by overlapping jaw-leaves the way a Brussels sprout stalk
carries its buds, and a single large fleshy trumpet flower opening at the top of that column.

The real-life references behind it are:

  * Brassica oleracea var. gemmifera, the Brussels sprout: a stalk carrying overlapping, cupped,
    veined leaves and buds set close around it.
  * Hippeastrum / Amaryllis: a single large fleshy trumpet bloom on a bare scape, six thick tepals
    with a paler throat and a coloured throat line.

The engine generator that mirrors this is
``src/Vivarium.Sim/Geometry/OrganismMeshes.Snaptrap.cs``.

Usage:
    blender --background --python brussel_reference.py -- --mode reference --out DIR
    blender --background --python brussel_reference.py -- --mode detail   --out DIR
    blender --background --python brussel_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# anatomy
# --------------------------------------------------------------------------

# Bud width up the column: the lowest set are the largest, the top ones are still tight.
BUD_PROFILE = [
    (0.00, 0.42), (0.12, 0.78), (0.28, 1.00), (0.45, 1.00), (0.62, 0.90),
    (0.76, 0.74), (0.88, 0.55), (1.00, 0.34),
]

# A cupped leaf, widest a little below the middle, tapering to a point.
LEAF_PROFILE = [
    (0.00, 0.30), (0.14, 0.58), (0.30, 0.80), (0.48, 0.90), (0.66, 0.82),
    (0.82, 0.60), (0.93, 0.32), (1.00, 0.05),
]


def _catmull(points, t):
    if t <= points[0][0]:
        return points[0][1]
    if t >= points[-1][0]:
        return points[-1][1]
    for i in range(len(points) - 1):
        t0, v0 = points[i]
        t1, v1 = points[i + 1]
        if t0 <= t <= t1:
            p0 = points[max(i - 1, 0)]
            p3 = points[min(i + 2, len(points) - 1)]
            u = (t - t0) / (t1 - t0)
            u2, u3 = u * u, u * u * u
            return 0.5 * (
                (2 * v0)
                + (-p0[1] + v1) * u
                + (2 * p0[1] - 5 * v0 + 4 * v1 - p3[1]) * u2
                + (-p0[1] + 3 * v0 - 3 * v1 + p3[1]) * u3
            )
    return points[-1][1]


def bud_width(t):
    return _catmull(BUD_PROFILE, t)


def leaf_breadth(t):
    return _catmull(LEAF_PROFILE, t)


def new_mesh_object(name, verts, faces, uvs, collection, material=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
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


def make_column(name, base, height, radius, coll, material):
    """The stout, tapering stalk: a bundle of fibre bundles spiralling up it, not a smooth pole."""
    verts, faces = [], []
    rings, sides = 14, 12
    flutes = 6
    bundle = [0.55 + 0.9 * ((s * 7919) % 101) / 100.0 for s in range(sides)]
    for r in range(rings + 1):
        t = r / rings
        lump = 1.0 + 0.28 * math.exp(-t * 5.0) + 0.38 * (abs(math.sin(t * math.pi * 5.0)) ** 6) + 0.07 * math.sin(t * 29.0)
        rad = radius * (1.0 - 0.46 * t) * lump
        for s in range(sides + 1):
            th = 2.0 * math.pi * s / sides
            flute = 1.0 + 0.46 * (math.cos(flutes * th + 4.4 * t) * bundle[s % sides] - 0.5)
            rr = rad * flute
            verts.append((base.x + math.cos(th) * rr,
                          base.y + math.sin(th) * rr,
                          base.z + height * t))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(material)
    return obj


def make_shred(name, base, outward, length, width, coll, rng):
    """A narrow sliver of dead leaf base hanging from the stalk."""
    down = Vector((0, 0, -1))
    side = down.cross(outward)
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    rows = 5
    verts, faces = [], []
    for i in range(rows + 1):
        t = i / rows
        peel = t * t * length * 0.38
        centre = base + down * (t * length) + outward * peel
        w = width * (1.0 - 0.35 * t) * (0.5 + 0.5 * math.sin(t * 9.0 + rng.uniform(0, 3)))
        verts.append(tuple(centre - side * w))
        verts.append(tuple(centre + side * w))
    for i in range(rows):
        a = i * 2
        faces.append((a, a + 2, a + 1))
        faces.append((a + 1, a + 2, a + 3))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(stem_material())


def make_bristle(name, base, outward, length, coll, rng):
    """A fine hair on the stalk."""
    bend = Vector((rng.uniform(-0.4, 0.4), rng.uniform(-0.6, 0.2), rng.uniform(-0.4, 0.4))).normalized()
    mid = base + outward * (length * 0.55) + bend * (length * 0.12)
    tip = base + outward * length + bend * (length * 0.35)
    verts = [tuple(base), tuple(mid), tuple(tip)]
    obj = new_mesh_object(name, verts, [(0, 1, 2)], None, coll)
    obj.data.materials.append(stem_material())


def make_jaw_leaf(name, base, direction, normal, length, breadth, rows, cols, open_angle, coll,
                   top_colour, inner_colour, cilia_colour, hair_colour):
    """One jaw-leaf: a paired, cupped trap that clasps the column."""
    verts, faces, uvs = [], [], []
    side = normal.cross(direction)
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(direction).normalized()

    def surface(t, x):
        env = math.sin(math.pi * min(1.0, t ** 0.75)) ** 0.7
        w = breadth * length * 0.5 * leaf_breadth(t)
        # The blade cups: the margins fold forward over the throat.
        cup = math.cos(open_angle) * abs(x) + math.sin(open_angle) * x * x
        return (base + direction * (t * length) + side * (x * w)
                + up * (w * cup * 0.55))

    for i in range(rows + 1):
        t = i / rows
        for j in range(cols + 1):
            x = (j / cols) * 2.0 - 1.0
            verts.append(tuple(surface(t, x)))
            uvs.append((t, (x + 1.0) * 0.5))
    for i in range(rows):
        for j in range(cols):
            a = i * (cols + 1) + j
            faces.append((a, a + cols + 1, a + 1))
            faces.append((a + 1, a + cols + 1, a + cols + 2))
    obj = new_mesh_object(name, verts, faces, uvs, coll)
    obj.data.materials.append(trap_material(top_colour, inner_colour))
    return obj, surface, side, up


def make_cilia(name, surface, side, up, rows, coll, colour):
    """The curved marginal cilia that fringe a jaw-leaf's rim."""
    for k in range(rows):
        t = 0.10 + 0.80 * k / max(1, rows - 1)
        for sign in (-1.0, 1.0):
            p = Vector(surface(t, 1.0 if sign > 0 else -1.0))
            end = p + side * (sign * 0.012) + up * 0.022
            mid = p + side * (sign * 0.016) + up * 0.012
            verts = [tuple(p), tuple(mid), tuple(end)]
            obj = new_mesh_object("%s_cilia_%d_%d" % (name, k, int(sign)), verts, [(0, 1, 2)], None, coll)
            obj.data.materials.append(cilia_material(colour))


def make_triggers(name, surface, up, coll, colour):
    """The trigger hairs inside the trap's throat."""
    for k in range(3):
        t = 0.28 + 0.20 * k
        p = Vector(surface(t, 0.0))
        end = p + up * 0.014
        verts = [tuple(p), tuple(end)]
        obj = new_mesh_object("%s_hair_%d" % (name, k), verts, [(0, 1)], None, coll)
        obj.data.materials.append(hair_material(colour))


def make_flower(name, base, axis, radius, coll, petal_outer, petal_inner, throat_colour, rng):
    """A single large fleshy trumpet flower: a deep throat flaring to a lobed rim."""
    axis = axis.normalized()
    side = axis.cross(Vector((0, 0, 1)))
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(axis).normalized()

    rows, cols = 14, 24
    verts, faces, uvs = [], [], []
    length = radius * 1.45
    phase = 0.7
    for i in range(rows + 1):
        t = i / rows
        # The tube is narrow at the throat and flares hard in the upper third.
        flare = radius * (0.13 + 0.87 * (t ** 2.3))
        droop = -radius * 0.10 * (t ** 2.4)
        for j in range(cols + 1):
            th = 2.0 * math.pi * j / cols
            # Six soft lobes round the rim, and the whole edge wanders and is torn.
            lobe = 1.0 + 0.075 * math.cos(6.0 * th) * (t ** 4)
            ruffle = 1.0 + (0.055 * math.sin(th * 7.0 + phase)
                            + 0.035 * math.sin(th * 13.0 + phase * 2.3)) * (t ** 3)
            ragged = radius * 0.055 * math.sin(th * 19.0 + phase * 3.1) * (t ** 4)
            r = flare * lobe * ruffle
            p = (base + axis * (length * t + droop + ragged)
                 + side * (math.cos(th) * r) + up * (math.sin(th) * r))
            verts.append(tuple(p))
            uvs.append((t, j / cols))
    for i in range(rows):
        for j in range(cols):
            a = i * (cols + 1) + j
            faces.append((a, a + cols + 1, a + 1))
            faces.append((a + 1, a + cols + 1, a + cols + 2))
    obj = new_mesh_object(name + "_bell", verts, faces, uvs, coll)
    obj.data.materials.append(petal_material(petal_outer, petal_inner))

    # Tatters: a few flaps of the rim left hanging where the edge has torn.
    for k in range(6):
        th = rng.uniform(0, 2 * math.pi)
        rim = (base + axis * length
               + side * (math.cos(th) * radius) + up * (math.sin(th) * radius))
        outward = (side * math.cos(th) + up * math.sin(th)).normalized()
        tang = (-side * math.sin(th) + up * math.cos(th)).normalized()
        ln = radius * rng.uniform(0.10, 0.22)
        wd = radius * rng.uniform(0.03, 0.06)
        verts = [tuple(rim - tang * wd), tuple(rim + tang * wd),
                 tuple(rim + outward * ln - tang * wd * 0.4),
                 tuple(rim + outward * ln + tang * wd * 0.4)]
        obj = new_mesh_object("%s_tatter_%d" % (name, k), verts, [(0, 2, 1), (1, 2, 3)], None, coll)
        obj.data.materials.append(petal_material(petal_outer, petal_inner))

    # The throat: a short, dark, fleshy tube the bell opens out of.
    rings, sides = 4, 16
    verts, faces = [], []
    for r in range(rings + 1):
        t = r / rings
        rad = radius * (0.17 + 0.05 * t)
        for s in range(sides + 1):
            th = 2.0 * math.pi * s / sides
            verts.append((base.x + axis.x * (length * 0.16 * t) + side.x * math.cos(th) * rad + up.x * math.sin(th) * rad,
                          base.y + axis.y * (length * 0.16 * t) + side.y * math.cos(th) * rad + up.y * math.sin(th) * rad,
                          base.z + axis.z * (length * 0.16 * t) + side.z * math.cos(th) * rad + up.z * math.sin(th) * rad))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name + "_throat", verts, faces, None, coll)
    obj.data.materials.append(throat_material(throat_colour))


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


def trap_material(outer, inner):
    mat = bpy.data.materials.new("latchjaw_trap")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.36)
    _set(bsdf, "Subsurface Weight", 0.22)
    _set(bsdf, "Subsurface Radius", (0.004, 0.006, 0.004))
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = outer + (1.0,)
    mix.inputs[7].default_value = inner + (1.0,)
    nt.links.new(geo.outputs["Backfacing"], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def cilia_material(colour):
    mat = bpy.data.materials.new("latchjaw_cilia")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", colour + (1.0,))
    _set(bsdf, "Roughness", 0.42)
    _set(bsdf, "Subsurface Weight", 0.3)
    return mat


def hair_material(colour):
    mat = bpy.data.materials.new("latchjaw_hair")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", colour + (1.0,))
    _set(bsdf, "Roughness", 0.5)
    return mat


def stem_material():
    mat = bpy.data.materials.new("latchjaw_column")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.13, 0.20, 0.075, 1.0))
    _set(bsdf, "Roughness", 0.72)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 42.0
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.6
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def petal_material(outer, inner):
    mat = bpy.data.materials.new("latchjaw_petal")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.42)
    _set(bsdf, "Subsurface Weight", 0.3)
    _set(bsdf, "Subsurface Radius", (0.006, 0.004, 0.006))
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = outer + (1.0,)
    mix.inputs[7].default_value = inner + (1.0,)
    nt.links.new(geo.outputs["Backfacing"], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def throat_material(colour):
    mat = bpy.data.materials.new("latchjaw_throat")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", colour + (1.0,))
    _set(bsdf, "Roughness", 0.3)
    _set(bsdf, "Subsurface Weight", 0.4)
    return mat


def soil_material():
    mat = bpy.data.materials.new("soil")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 48.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.050, 0.044, 0.030, 1.0)
    ramp.color_ramp.elements[1].position = 0.68
    ramp.color_ramp.elements[1].color = (0.125, 0.105, 0.078, 1.0)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


# --------------------------------------------------------------------------
# assembly
# --------------------------------------------------------------------------

def reset_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for block in list(bpy.data.meshes):
        if block.users == 0:
            bpy.data.meshes.remove(block)


def build_column(rng, height=0.20, column_radius=0.012, buds=11):
    """One latchjaw: a column of jaw-leaves under a single large fleshy flower."""
    coll = bpy.context.scene.collection
    green = (0.085, 0.215, 0.075)
    red = (0.42, 0.075, 0.085)
    inner = (0.50, 0.13, 0.13)
    cilia = (0.52, 0.58, 0.22)
    hair = (0.22, 0.12, 0.08)
    petal_outer = (0.36, 0.52, 0.16)
    petal_inner = (0.62, 0.11, 0.14)
    throat = (0.34, 0.06, 0.09)

    base = Vector((0, 0, 0))
    column_h = height * 0.76
    make_column("column", base, column_h, column_radius, coll, stem_material())

    # Fibrous slivers of dead leaf base hanging from the stalk, and fine bristles over it.
    for k in range(26):
        a = rng.uniform(0, 2 * math.pi)
        t = rng.uniform(0.02, 0.74)
        outward = Vector((math.cos(a), math.sin(a), 0.0)).normalized()
        base_p = (outward * (column_radius * rng.uniform(0.8, 1.0))
                  + Vector((0, 0, column_h * t)))
        make_shred("shred_%d" % k, base_p, outward,
                   column_h * rng.uniform(0.14, 0.30),
                   column_radius * rng.uniform(0.18, 0.42), coll, rng)
    for k in range(46):
        a = rng.uniform(0, 2 * math.pi)
        t = rng.uniform(0.0, 0.95)
        outward = Vector((math.cos(a), math.sin(a), 0.0)).normalized()
        base_p = outward * column_radius + Vector((0, 0, column_h * t))
        make_bristle("bristle_%d" % k, base_p, outward,
                     column_radius * rng.uniform(0.35, 0.8), coll, rng)

    # Jaw-leaves set close around the column, spiralling up it.
    for k in range(buds):
        t = 0.06 + 0.80 * (k / max(1, buds - 1))
        az = k * 2.39996
        # A golden-angle spiral so the leaves never sit in a ring.
        outward = Vector((math.cos(az), math.sin(az), 0.0)).normalized()
        height_here = column_h * t
        width = bud_width(t) * height * 0.21
        # The leaf leaves the column low and tilts up, clasping it as it goes.
        attach = Vector((base.x + outward.x * column_radius * 0.9,
                         base.y + outward.y * column_radius * 0.9,
                         base.z + height_here))
        lean = rng.uniform(0.80, 1.15) - 0.35 * t
        direction = (outward * math.cos(lean)
                     + Vector((0, 0, 1)) * math.sin(lean)).normalized()
        normal = Vector((0, 0, 1)).cross(direction)
        if normal.length < 1e-6:
            normal = Vector((1, 0, 0))
        normal.normalize()
        open_angle = rng.uniform(0.55, 0.95)
        obj, surface, side, up = make_jaw_leaf(
            "jaw_%d" % k, attach, direction, normal, width, 0.92, 10, 5, open_angle, coll,
            green, red, cilia, hair)
        make_cilia("jaw_%d" % k, surface, side, up, 9, coll, cilia)
        make_triggers("jaw_%d" % k, surface, up, coll, hair)

    # The flower: one large fleshy trumpet opening at the top of the column.
    flower_base = Vector((base.x, base.y, base.z + column_h * 1.06))
    axis = Vector((rng.uniform(-0.12, 0.12), rng.uniform(-0.12, 0.12), 1.0)).normalized()
    make_flower("flower", flower_base, axis, height * 0.26, coll, petal_outer, petal_inner, throat, rng)

    # A crown of small bracts where the flower meets the column.
    for k in range(6):
        az = k * 2.39996
        outward = Vector((math.cos(az), math.sin(az), 0.0)).normalized()
        direction = (outward * 0.55 + Vector((0, 0, 1)) * 0.84).normalized()
        normal = Vector((0, 0, 1)).cross(direction)
        if normal.length < 1e-6:
            normal = Vector((1, 0, 0))
        normal.normalize()
        make_jaw_leaf("bract_%d" % k, flower_base - Vector((0, 0, height * 0.075)),
                       direction, normal, height * 0.075, 0.7, 6, 4, 0.7, coll,
                       green, red, cilia, hair)


def add_ground(radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=radius, depth=0.02,
                                        location=(0, 0, -0.012))
    ground = bpy.context.active_object
    ground.data.materials.append(soil_material())
    return ground


def setup_camera_and_light(size=0.34, elevation=20.0, azimuth=35.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.30))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 2.8
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.96, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(50.0), 0.0, math.radians(32.0))
    bpy.context.scene.collection.objects.link(sun)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.32, 0.40, 0.48, 1.0)
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


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference", choices=["reference", "detail", "mesh"])
    ap.add_argument("--obj", default="")
    ap.add_argument("--out", default=".")
    ap.add_argument("--seed", type=int, default=3)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    args = ap.parse_args(argv)

    reset_scene()
    rng = random.Random(args.seed)
    os.makedirs(args.out, exist_ok=True)

    if args.mode == "mesh":
        build_from_obj(args.obj)
        add_ground(0.22)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_latchjaw.png")
    elif args.mode == "detail":
        build_column(rng, height=0.20, column_radius=0.012, buds=11)
        add_ground(0.22)
        setup_camera_and_light(size=0.20, elevation=18.0, azimuth=25.0)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "latchjaw_detail.png")
    else:
        build_column(rng, height=0.20, column_radius=0.012, buds=11)
        add_ground(0.22)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "latchjaw.png")

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


def build_from_obj(path):
    bpy.ops.wm.obj_import(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for o in imported:
        # The engine mesh is authored at unit radius/height; the latchjaw scales it to its own
        # footprint and height, and the engine's frame is Y up.
        o.matrix_world = x90 @ Matrix.Diagonal((0.27, 0.20, 0.27, 1.0))
        o.data.materials.clear()
        o.data.materials.append(imported_material())
        for poly in o.data.polygons:
            poly.use_smooth = True
        o.data.update()


def imported_material():
    mat = bpy.data.materials.new("engine_vertex_colour")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.45)
    _set(bsdf, "Subsurface Weight", 0.0)
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


if __name__ == "__main__":
    main()
