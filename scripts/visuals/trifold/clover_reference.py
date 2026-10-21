"""White clover (Trifolium repens) reference mat for the Vivarium 'trifold' groundcover.

This is the real-life reference the trifold species is rebuilt against. It builds a
creeping white clover mat from scratch in Blender using the species' own anatomy:

  * prostrate stolons that root at the nodes (the mat is clonal, not seeded),
  * long petioles carrying palmately trifoliate leaves with obcordate leaflets,
  * finely serrate leaflet margins and a shallow apical cleft,
  * a raised midrib fold and a pale chevron watermark across each leaflet,
  * a pair of membranous stipules at every petiole base,
  * globose cream heads on long peduncles that blush pink as they age.

The same profile maths is mirrored by the engine generator in
``src/Vivarium.Sim/Geometry/OrganismMeshes.cs`` (TrifoliateLeaf), so the reference
render and the in-game capture can be compared directly.

Usage:
    blender --background --python clover_reference.py -- --mode reference --out DIR
    blender --background --python clover_reference.py -- --mode fantasy   --out DIR
    blender --background --python clover_reference.py -- --mode mesh --obj FILE --out DIR
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

# Obcordate half-width profile of a white clover leaflet, as (t, fraction of the
# maximum half width). Widest above the middle, narrowing to a cuneate base and
# to a broad apex that carries the cleft.
PROFILE = [
    (0.00, 0.05), (0.06, 0.17), (0.14, 0.33), (0.24, 0.53), (0.36, 0.72),
    (0.50, 0.88), (0.62, 0.97), (0.72, 1.00), (0.80, 0.97), (0.88, 0.86),
    (0.94, 0.71), (1.00, 0.50),
]

CLEFT_T = 0.845      # where the midrib stops and the apical cleft begins
LOBE_X = 0.40        # lateral position of a lobe tip, as a fraction of max half width
CLEFT_POWER = 1.30   # >1 keeps the cleft narrow low down and opens it near the tip


def _catmull(points, t):
    """Piecewise Catmull-Rom through the profile control points."""
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


def half_width(t):
    """Half width of the leaflet at normalised along-position t."""
    return max(0.0, _catmull(PROFILE, t))


def inner_edge(t):
    """Lateral position of the inner boundary: the midrib, then the cleft edge."""
    if t <= CLEFT_T:
        return 0.0
    s = (t - CLEFT_T) / (1.0 - CLEFT_T)
    return LOBE_X * (s ** CLEFT_POWER)


def tooth_wave(t, teeth, phase):
    """Serrate margin: teeth point toward the apex, amplitude tapers at both ends.

    A sawtooth with one period per two rows resolves cleanly at the row spacing the
    leaf grid uses; a smooth wave at this frequency would alias into a smooth edge.
    """
    if teeth <= 0:
        return 1.0
    s = t * teeth + phase
    frac = s - math.floor(s)
    tooth = (1.0 - frac) ** 1.6
    taper = math.sin(math.pi * min(1.0, max(0.0, t))) ** 0.45
    return 1.0 + 0.085 * tooth * taper


def leaflet_grid(length, half_w, rows, cols, teeth, phase, relief):
    """Return (points, faces, uvs) for one leaflet, in its own (across, along, up) frame.

    The blade is built as two mirrored half grids so the apical cleft is real
    geometry rather than a painted notch.
    """
    verts, faces, uvs = [], [], []
    max_hw = half_w

    def surface(t, x_frac, side):
        """x_frac: 0 at the inner boundary, 1 at the margin."""
        ix = inner_edge(t)
        ox = half_width(t) * tooth_wave(t, teeth, phase)
        px = ix + (ox - ix) * x_frac
        py = t * length
        # cross section: raised midrib ridge, blade falling to the margin, margin flicked up
        ridge = relief["ridge"] * (1.0 - _smoothstep(0.0, 0.30, x_frac))
        drop = -relief["drop"] * (x_frac ** 1.8)
        flick = relief["flick"] * _smoothstep(0.78, 1.0, x_frac)
        curl = -relief["curl"] * _smoothstep(0.68, 1.0, t)
        wrinkle = relief["wrinkle"] * math.sin(t * 21.0 + phase * 6.0) * x_frac
        pz = ridge + drop + flick + curl + wrinkle
        return (side * px * max_hw, py, pz * max_hw)

    for side in (1, -1):
        base = len(verts)
        for i in range(rows):
            t = i / (rows - 1)
            for j in range(cols):
                x_frac = j / (cols - 1)
                verts.append(surface(t, x_frac, side))
                # UV: x runs base->tip, y runs across the blade (0.5 on the midrib)
                across = 0.5 + 0.5 * side * (x_frac * (half_width(t) * tooth_wave(t, teeth, phase)) / max_hw)
                uvs.append((t, min(1.0, max(0.0, across))))
        for i in range(rows - 1):
            for j in range(cols - 1):
                a = base + i * cols + j
                b = a + 1
                c = a + cols
                d = c + 1
                if side > 0:
                    faces.append((a, c, b))
                    faces.append((b, c, d))
                else:
                    faces.append((a, b, c))
                    faces.append((b, d, c))
    return verts, faces, uvs


def _smoothstep(a, b, x):
    if b == a:
        return 0.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def new_mesh_object(name, verts, faces, uvs, collection):
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
    return obj


# --------------------------------------------------------------------------
# plant parts
# --------------------------------------------------------------------------

def make_leaflet(name, length, half_w, rows, cols, teeth, phase, relief, coll, matrix):
    verts, faces, uvs = leaflet_grid(length, half_w, rows, cols, teeth, phase, relief)
    obj = new_mesh_object(name, verts, faces, uvs, coll)
    obj.data.materials.append(leaf_material())
    obj.matrix_world = matrix
    return obj


def make_leaf_group(name, origin, azimuth, scale, relief, coll, rng, leaflets=3):
    """Palmately arranged obcordate leaflets on a short petiolule."""
    objs = []
    base = Matrix.Translation(origin)
    for k in range(leaflets):
        angle = azimuth + 2.0 * math.pi * k / leaflets
        length = scale * rng.uniform(0.88, 1.10)
        hw = length * rng.uniform(0.38, 0.46)
        rows = 16
        cols = 5
        # A tooth needs two rows to resolve, so the serration count follows the row count.
        teeth = max(3, (rows - 1) // 2)
        # leaflet local frame: +Y along the leaflet, +X across, +Z up
        rot = Matrix.Rotation(angle, 4, 'Z')
        tilt = Matrix.Rotation(math.radians(rng.uniform(-16.0, 6.0)), 4, 'Y')
        local = base @ rot @ Matrix.Translation(Vector((0.0, length * 0.10, 0.0))) @ tilt
        objs.append(make_leaflet(
            "%s_leaflet_%d" % (name, k), length, hw, rows, cols, teeth,
            rng.uniform(0.0, 1.0), relief, coll, local))
    return objs


def make_petiole(name, base, height, lean_dir, coll, rng, radius=0.0022):
    """Arching petiole from the stolon node up to the leaf group."""
    steps = 8
    path = []
    for i in range(steps + 1):
        t = i / steps
        rise = height * (t ** 0.72)
        out = lean_dir * (height * 0.30 * (t ** 1.6))
        wobble = Vector((rng.uniform(-1, 1), 0, rng.uniform(-1, 1))) * (0.0016 * math.sin(t * 5.0))
        path.append(base + out + Vector((0.0, rise, 0.0)) + wobble)
    verts, faces = [], []
    sides = 6
    for i, p in enumerate(path):
        if i == 0:
            tangent = path[1] - path[0]
        elif i == len(path) - 1:
            tangent = path[-1] - path[-2]
        else:
            tangent = path[i + 1] - path[i - 1]
        tangent.normalize()
        ref = Vector((0, 0, 1)) if abs(tangent.z) < 0.9 else Vector((1, 0, 0))
        side = tangent.cross(ref).normalized()
        up = side.cross(tangent).normalized()
        r = radius * (1.0 - 0.35 * i / steps)
        for s in range(sides + 1):
            a = 2.0 * math.pi * s / sides
            verts.append(tuple(p + side * (math.cos(a) * r) + up * (math.sin(a) * r)))
    for i in range(steps):
        for s in range(sides):
            a = i * (sides + 1) + s
            b = a + 1
            c = a + sides + 1
            d = c + 1
            faces.append((a, c, b))
            faces.append((b, c, d))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(stolon_material())
    return obj


def make_stipules(name, base, azimuth, coll, rng, size=0.006):
    """The paired membranous stipules that sheath the base of a clover petiole."""
    objs = []
    for k in range(2):
        a = azimuth + k * math.pi
        verts, faces = [], []
        rows, cols = 4, 3
        for i in range(rows):
            t = i / (rows - 1)
            width = size * 0.42 * math.sin(math.pi * (0.12 + 0.88 * t)) ** 0.7
            for j in range(cols):
                x = (j / (cols - 1) - 0.5) * 2.0
                y = t * size
                z = 0.0009 * (1.0 - x * x) + rng.uniform(-0.0002, 0.0002)
                verts.append((x * width, y, z))
        for i in range(rows - 1):
            for j in range(cols - 1):
                a0 = i * cols + j
                faces.append((a0, a0 + cols, a0 + 1))
                faces.append((a0 + 1, a0 + cols, a0 + cols + 1))
        obj = new_mesh_object("%s_stipule_%d" % (name, k), verts, faces, None, coll)
        obj.matrix_world = Matrix.Translation(base) @ Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.radians(72.0), 4, 'Y')
        objs.append(obj)
    return objs


def make_flower_head(name, centre, radius, coll, rng, florets=54, blush=0.0, fantasy=False):
    """Globose head of small papilionaceous florets on a hemispherical calyx."""
    objs = []
    verts, faces = [], []
    rings, sides = 4, 14
    for r in range(rings + 1):
        phi = (math.pi * 0.5) * r / rings
        for s in range(sides + 1):
            th = 2.0 * math.pi * s / sides
            x = math.cos(phi) * math.cos(th) * radius
            y = math.sin(phi) * radius
            z = math.cos(phi) * math.sin(th) * radius
            verts.append((centre[0] + x, centre[1] + y, centre[2] + z))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name + "_calyx", verts, faces, None, coll)
    obj.data.materials.append(calyx_material())
    objs.append(obj)

    for f in range(florets):
        # fibonacci-ish distribution over the head
        y = 1.0 - (f + 0.5) / florets
        rad = math.sqrt(max(0.0, 1.0 - y * y))
        theta = f * 2.399963
        px = math.cos(theta) * rad
        pz = math.sin(theta) * rad
        py = y
        base = Vector((centre[0] + px * radius * 0.62, centre[1] + py * radius * 0.62, centre[2] + pz * radius * 0.62))
        direction = Vector((px, py, pz))
        if direction.length < 1e-6:
            direction = Vector((0, 1, 0))
        direction.normalize()
        length = radius * rng.uniform(0.55, 0.85)
        tip = base + direction * length
        # a small curved petal: 3 segments, 2 across
        side = direction.cross(Vector((0, 0, 1)))
        if side.length < 1e-5:
            side = Vector((1, 0, 0))
        side.normalize()
        up = side.cross(direction).normalized()
        fw = length * 0.30
        v2, f2 = [], []
        cols = 3
        for i in range(4):
            tt = i / 3.0
            centre_p = base.lerp(tip, tt)
            bow = up * (math.sin(math.pi * tt) * length * 0.16)
            for j in range(cols):
                x = (j / (cols - 1) - 0.5) * 2.0
                p = centre_p + bow + side * (x * fw * math.sin(math.pi * (0.25 + 0.75 * tt)))
                v2.append(tuple(p))
        for i in range(3):
            for j in range(cols - 1):
                a0 = i * cols + j
                f2.append((a0, a0 + cols, a0 + 1))
                f2.append((a0 + 1, a0 + cols, a0 + cols + 1))
        petal = new_mesh_object("%s_floret_%02d" % (name, f), v2, f2, None, coll)
        petal.data.materials.append(floret_material(blush, fantasy))
        objs.append(petal)
    return objs


def make_stolon(name, path, radius, coll, rng, nodes):
    """Prostrate runner with nodal swelling and adventitious roots."""
    objs = []
    verts, faces = [], []
    sides = 6
    for i, (p, r) in enumerate(zip(path, radius)):
        if i == 0:
            tangent = Vector(path[1]) - Vector(path[0])
        elif i == len(path) - 1:
            tangent = Vector(path[-1]) - Vector(path[-2])
        else:
            tangent = Vector(path[i + 1]) - Vector(path[i - 1])
        tangent.normalize()
        ref = Vector((0, 0, 1))
        side = tangent.cross(ref).normalized()
        up = side.cross(tangent).normalized()
        for s in range(sides + 1):
            a = 2.0 * math.pi * s / sides
            verts.append(tuple(Vector(p) + side * (math.cos(a) * r) + up * (math.sin(a) * r)))
    for i in range(len(path) - 1):
        for s in range(sides):
            a = i * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(stolon_material())
    objs.append(obj)

    for idx in nodes:
        p = Vector(path[idx])
        # nodal swelling
        n = len(path)
        t = idx / (n - 1)
        r = radius[idx] * 1.55
        nv, nf = [], []
        for s in range(sides + 1):
            a = 2.0 * math.pi * s / sides
            nv.append((p.x + math.cos(a) * r, p.y, p.z + math.sin(a) * r))
        for s in range(sides):
            nf.append((0, s, s + 1))
        swell = new_mesh_object("%s_node_%d" % (name, idx), nv, nf, None, coll)
        swell.data.materials.append(stolon_material())
        objs.append(swell)
        # adventitious roots reaching into the soil
        rv, rf = [], []
        for k in range(3):
            a = rng.uniform(0, 2 * math.pi)
            tip = p + Vector((math.cos(a) * r * 2.2, -0.012, math.sin(a) * r * 2.2))
            base = len(rv)
            rv.append(tuple(p))
            rv.append(tuple(p.lerp(tip, 0.55) + Vector((0, 0, 0))))
            rv.append(tuple(tip))
            rf.append((base, base + 1, base + 2))
        root = new_mesh_object("%s_root_%d" % (name, idx), rv, rf, None, coll)
        root.data.materials.append(root_material())
        objs.append(root)
    return objs


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
    mat = bpy.data.materials.new("clover_leaf")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.16, 0.36, 0.10, 1.0))
    _set(bsdf, "Roughness", 0.52)
    _set(bsdf, "Subsurface Weight", 0.16)
    _set(bsdf, "Subsurface Radius", (0.006, 0.012, 0.004))
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["UV"], sep.inputs["Vector"])
    # pale chevron watermark: a V pointing at the apex
    absy = nt.nodes.new("ShaderNodeMath")
    absy.operation = 'ABSOLUTE'
    nt.links.new(sep.outputs["Y"], absy.inputs[0])
    sub = nt.nodes.new("ShaderNodeMath")
    sub.operation = 'SUBTRACT'
    nt.links.new(sep.outputs["X"], sub.inputs[0])
    nt.links.new(absy.outputs[0], sub.inputs[1])
    mul = nt.nodes.new("ShaderNodeMath")
    mul.operation = 'MULTIPLY'
    mul.inputs[1].default_value = 0.34
    nt.links.new(sub.outputs[0], mul.inputs[0])
    add = nt.nodes.new("ShaderNodeMath")
    add.operation = 'ADD'
    nt.links.new(mul.outputs[0], add.inputs[0])
    add.inputs[1].default_value = 0.40
    band = nt.nodes.new("ShaderNodeMath")
    band.operation = 'SUBTRACT'
    nt.links.new(sep.outputs["X"], band.inputs[0])
    nt.links.new(add.outputs[0], band.inputs[1])
    absband = nt.nodes.new("ShaderNodeMath")
    absband.operation = 'ABSOLUTE'
    nt.links.new(band.outputs[0], absband.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = 'EASE'
    ramp.color_ramp.elements[0].position = 0.030
    ramp.color_ramp.elements[0].color = (1.0, 1.0, 1.0, 1.0)
    ramp.color_ramp.elements[1].position = 0.075
    ramp.color_ramp.elements[1].color = (0.0, 0.0, 0.0, 1.0)
    nt.links.new(absband.outputs[0], ramp.inputs["Fac"])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = (0.15, 0.34, 0.09, 1.0)
    mix.inputs[7].default_value = (0.52, 0.62, 0.33, 1.0)
    nt.links.new(ramp.outputs["Color"], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def calyx_material():
    mat = bpy.data.materials.new("clover_calyx")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.19, 0.33, 0.12, 1.0))
    _set(bsdf, "Roughness", 0.62)
    return mat


def floret_material(blush=0.0, fantasy=False):
    mat = bpy.data.materials.new("clover_floret")
    mat.use_nodes = True
    bsdf = _principled(mat)
    base = (0.94 - blush * 0.18, 0.93 - blush * 0.10, 0.88 + blush * 0.02, 1.0)
    if fantasy:
        base = (0.86, 0.90, 1.0, 1.0)
    _set(bsdf, "Base Color", base)
    _set(bsdf, "Roughness", 0.44)
    _set(bsdf, "Subsurface Weight", 0.35)
    _set(bsdf, "Subsurface Radius", (0.004, 0.006, 0.010))
    return mat


def stolon_material():
    mat = bpy.data.materials.new("clover_stolon")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.24, 0.36, 0.15, 1.0))
    _set(bsdf, "Roughness", 0.58)
    return mat


def root_material():
    mat = bpy.data.materials.new("clover_root")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.42, 0.34, 0.24, 1.0))
    _set(bsdf, "Roughness", 0.85)
    return mat


def soil_material():
    mat = bpy.data.materials.new("soil")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 55.0
    noise.inputs["Detail"].default_value = 8.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.055, 0.045, 0.032, 1.0)
    ramp.color_ramp.elements[1].position = 0.68
    ramp.color_ramp.elements[1].color = (0.135, 0.112, 0.082, 1.0)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.5
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
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


def build_reference_mat(rng, fantasy=False, radius=0.22, height=0.08):
    """One creeping white clover plant, authored at true world proportions."""
    coll = bpy.context.scene.collection
    relief = {"ridge": 0.055, "drop": 0.030, "flick": 0.016, "curl": 0.045, "wrinkle": 0.006}

    # creeping stolons
    n_stolons = 9
    for k in range(n_stolons):
        az = 2.0 * math.pi * k / n_stolons + rng.uniform(-0.18, 0.18)
        reach = radius * rng.uniform(0.82, 1.0)
        steps = 9
        path, radii = [], []
        for s in range(steps + 1):
            t = s / steps
            wander = 0.10 * math.sin(t * math.pi * 2.0 + k * 1.5)
            a = az + wander
            r = t * reach
            path.append((math.cos(a) * r, 0.004 + 0.004 * math.sin(t * 3.0), math.sin(a) * r))
            radii.append(0.0022 * (1.0 - 0.25 * t))
        make_stolon("stolon_%d" % k, path, radii, coll, rng, [2, 4, 6, 8])

        for s in range(1, steps + 1):
            node = Vector(path[s])
            for p in range(rng.choice([2, 2, 3])):
                la = rng.uniform(0, 2 * math.pi)
                pet_h = rng.uniform(0.030, 0.060)
                lean = Vector((math.cos(la), 0, math.sin(la)))
                make_petiole("petiole_%d_%d_%d" % (k, s, p), node, pet_h, lean, coll, rng)
                leaflets = 4 if (fantasy and rng.random() < 0.12) else 3
                group_origin = node + lean * (pet_h * 0.30) + Vector((0, pet_h, 0))
                make_leaf_group("leaf_%d_%d_%d" % (k, s, p), group_origin, la, 0.021,
                                relief, coll, rng, leaflets)
                make_stipules("stip_%d_%d_%d" % (k, s, p), node, la, coll, rng)

    # central crown
    for c in range(26):
        la = 2.0 * math.pi * c / 26 + rng.uniform(-0.2, 0.2)
        pet_h = rng.uniform(0.028, 0.055)
        lean = Vector((math.cos(la), 0, math.sin(la)))
        base = lean * (radius * rng.uniform(0.02, 0.30))
        make_petiole("crown_petiole_%d" % c, base, pet_h, lean, coll, rng)
        leaflets = 4 if (fantasy and rng.random() < 0.14) else 3
        group_origin = base + lean * (pet_h * 0.30) + Vector((0, pet_h, 0))
        make_leaf_group("crown_leaf_%d" % c, group_origin, la, 0.021, relief, coll, rng, leaflets)
        make_stipules("crown_stip_%d" % c, base, la, coll, rng)

    # flower heads on peduncles
    for f in range(7):
        a = 2.0 * math.pi * f / 7 + rng.uniform(-0.25, 0.25)
        rr = radius * rng.uniform(0.20, 0.62)
        base = Vector((math.cos(a) * rr * 0.6, 0.004, math.sin(a) * rr * 0.6))
        top = Vector((math.cos(a) * rr, height * rng.uniform(0.80, 1.0), math.sin(a) * rr))
        make_petiole("peduncle_%d" % f, base, (top.y - base.y), Vector((math.cos(a), 0, math.sin(a))) * 0.4, coll, rng, radius=0.0018)
        blush = 0.0 if f < 5 else 0.6
        make_flower_head("head_%d" % f, top, 0.009, coll, rng, florets=54, blush=blush,
                         fantasy=fantasy and f == 6)
    y_up_to_z_up()


def build_from_obj(path, scale, flat=False):
    coll = bpy.context.scene.collection
    bpy.ops.wm.obj_import(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for o in imported:
        o.matrix_world = x90 @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
        o.data.materials.clear()
        # The export already reverses the face order for OBJ's winding convention.
        for poly in o.data.polygons:
            poly.use_smooth = True
        o.data.update()
        attribute = o.data.color_attributes[0].name if o.data.color_attributes else None
        print("OBJ_MESH", o.name, "verts", len(o.data.vertices),
              "colour_attributes", [a.name for a in o.data.color_attributes],
              "attributes", [a.name for a in o.data.attributes])
        sample = o.data.polygons[0]
        print("OBJ_NORMAL", list(sample.normal), "first_vertex_colour",
              list(o.data.color_attributes[0].data[0].color) if o.data.color_attributes else None)
        o.data.materials.append(flat_material() if flat else imported_material(attribute))
    return imported


def flat_material():
    mat = bpy.data.materials.new("engine_flat")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.25, 0.5, 0.2, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def imported_material(attribute=None):
    """Shows the engine's vertex colours so the export can be compared directly."""
    mat = bpy.data.materials.new("engine_vertex_colour")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.52)
    # The engine's leaf blades are single sheets. Cycles returns black for subsurface scattering on
    # open surfaces, so the comparison material keeps the blades opaque.
    _set(bsdf, "Subsurface Weight", 0.0)
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = attribute or "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    # Shade from the engine's own vertex normals. The engine uses Godot's winding convention, so
    # the geometric normal of an imported face points the other way; the exported normals do not.
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


def setup_camera_and_light(size=0.594, elevation=26.3, azimuth=45.0):
    # Blender is Z-up; the plant is authored in the engine's Y-up frame and rotated once
    # by build_reference_mat, so the camera works in Blender's own convention here.
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.06))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 2.4
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.97, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(52.0), 0.0, math.radians(38.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 8.0
    fill_data.size = 0.8
    fill_data.color = (0.72, 0.82, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.55, 0.35, 0.45)
    fill.rotation_euler = (math.radians(58.0), 0.0, math.radians(-125.0))
    bpy.context.scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.30, 0.40, 0.52, 1.0)
    bg.inputs[1].default_value = 0.35
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
    scene.view_settings.view_transform = 'Filmic' if 'Filmic' in [
        t.name for t in scene.view_settings.bl_rna.properties['view_transform'].enum_items
    ] else scene.view_settings.view_transform


def build_leaf_detail(rng, fantasy=False):
    """A single leaf group, large in frame, to study the leaflet anatomy."""
    coll = bpy.context.scene.collection
    relief = {"ridge": 0.055, "drop": 0.030, "flick": 0.016, "curl": 0.045, "wrinkle": 0.006}
    base = Vector((0.0, 0.0, 0.0))
    make_petiole("detail_petiole", base, 0.030, Vector((1.0, 0.0, 0.0)), coll, rng)
    origin = base + Vector((0.009, 0.0, 0.030))
    make_leaf_group("detail_leaf", origin, 0.0, 0.021, relief, coll, rng, 3)
    y_up_to_z_up()


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference", choices=["reference", "fantasy", "mesh", "leaf"])
    ap.add_argument("--obj", default="")
    ap.add_argument("--out", default=".")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--flat", action="store_true")
    ap.add_argument("--elevation", type=float, default=26.3)
    args = ap.parse_args(argv)

    reset_scene()
    rng = random.Random(args.seed)
    os.makedirs(args.out, exist_ok=True)

    if args.mode == "mesh":
        build_from_obj(args.obj, (0.22, 0.08, 0.22), flat=args.flat)
        add_ground(0.297)
        setup_camera_and_light(elevation=args.elevation)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_mesh.png")
    elif args.mode == "leaf":
        build_leaf_detail(rng)
        setup_camera_and_light(size=0.075, elevation=48.0, azimuth=35.0)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "clover_leaf_detail.png")
    else:
        build_reference_mat(rng, fantasy=(args.mode == "fantasy"))
        add_ground(0.297)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "clover_%s.png" % args.mode)

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


if __name__ == "__main__":
    main()
