"""Ice-plant (Aizoaceae) reference mat for the Vivarium 'glassfinger' groundcover.

This is the real-life reference the glassfinger species is rebuilt against. It builds a
prostrate ice-plant mat from scratch in Blender using the family's own anatomy:

  * prostrate, sprawling, repeatedly-branching stems, faintly 3-ribbed in cross-section,
  * opposite and DECUSSATE leaves (each node rotated 90 degrees from the one above),
  * succulent leaves that are triangular in cross-section, keeled underneath, widest a
    third of the way along and tapering to an acute point,
  * a sheathing leaf base that clasps the stem, and a nodal swelling where it meets,
  * the dense coat of epidermal bladder cells that gives the family its name,
  * anthocyanin flush at the leaf tips and margins under strong light,
  * daisy-like flowers: many narrow petals over a ring of stamens with a yellow centre.

The fantasy variant keeps the real anatomy and only changes one thing: the bladder cells
are azure crystal rather than clear vesicles, and they carry the blue.

Usage:
    blender --background --python iceplant_reference.py -- --mode reference --out DIR
    blender --background --python iceplant_reference.py -- --mode fantasy   --out DIR
    blender --background --python iceplant_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# morphology  (real values, metres)
# --------------------------------------------------------------------------

# A prostrate ice plant is a low mat: the whole shoot system hugs the ground and the
# leaves stand a few centimetres proud of it.
MAT_RADIUS = 0.115          # radius of one plant's footprint
SHOOT_LENGTH = 0.115        # length of one primary shoot
STEM_RADIUS = 0.0026        # stem radius at the base, tapering to the tip
LEAF_LENGTH = 0.036         # mature leaf length (36 mm)
LEAF_WIDTH = 0.0036         # leaf half-width at the widest point
LEAF_TRI = 0.34             # cross-section triangle strength (0 = round, 1 = sharp)

# Bladder cells: the epidermal vesicles that glitter on every ice-plant surface.
CELL_RADIUS = 0.00062
CELL_COUNT_PER_LEAF = 46

FLOWER_RADIUS = 0.0105      # corolla radius (21 mm across)
FLOWER_PETALS = 44

# The fantasy blue.
AZURE = (0.055, 0.40, 0.92, 1.0)
AZURE_DEEP = (0.02, 0.16, 0.62, 1.0)


# --------------------------------------------------------------------------
# cross-section maths
# --------------------------------------------------------------------------

def section_radius(theta, base, tri, twist, phase):
    """Radius of the leaf cross-section at angle `theta`.

    A rounded triangle is a circle with a three-fold harmonic added to it: the lobes
    are the three corners, the shallower flanks between them are the rounded edges.
    The keel of an ice-plant leaf points down, so one lobe is aimed downward.
    """
    a = theta - phase
    r = base * (1.0 + tri * math.cos(3.0 * a) + 0.16 * tri * math.cos(6.0 * a + 0.7))
    return r * (1.0 + twist * math.sin(2.0 * a))


def leaf_width_profile(t):
    """Half-width of the leaf blade at along-position t.

    Real ice-plant leaves have a narrow sheathing base, a slight waist where the blade
    leaves the sheath, are widest about a third of the way along and taper to a point.
    """
    sheath = 0.58
    waist = 0.74
    blade = 1.00
    if t < 0.10:
        u = t / 0.10
        return sheath + (waist - sheath) * (u * u)
    if t < 0.22:
        u = (t - 0.10) / 0.12
        return waist + (blade - waist) * math.sin(u * math.pi * 0.5)
    # long even taper to an acute point
    u = (t - 0.22) / 0.78
    return blade * (1.0 - u) ** 0.85


def leaf_thickness_profile(t):
    """Blade thickness relative to the half-width: thin at the tip, fleshy at the base."""
    return 0.52 * (1.0 - 0.55 * t) + 0.16 * (1.0 - t) ** 2


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


# Unit icosahedron, used to build the bladder cells as one merged mesh per leaf.
_ICO_V = []
_ICO_F = []


def _init_ico():
    t = (1.0 + math.sqrt(5.0)) / 2.0
    verts = [
        (-1, t, 0), (1, t, 0), (-1, -t, 0), (1, -t, 0),
        (0, -1, t), (0, 1, t), (0, -1, -t), (0, 1, -t),
        (t, 0, -1), (t, 0, 1), (-t, 0, -1), (-t, 0, 1),
    ]
    verts = [tuple(Vector(v).normalized()) for v in verts]
    faces = [
        (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9),
        (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8), (3, 9, 4), (3, 4, 2),
        (3, 2, 6), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
    ]
    _ICO_V.extend(verts)
    _ICO_F.extend(faces)


def append_cell(verts, faces, uvs, centre, radius, squash, u, v):
    """Append one bladder cell (a squashed icosahedron) to a shared vertex list."""
    base = len(verts)
    for (x, y, z) in _ICO_V:
        verts.append((centre[0] + x * radius,
                      centre[1] + y * radius * squash,
                      centre[2] + z * radius * squash))
        uvs.append((u, v))
    for (a, b, c) in _ICO_F:
        faces.append((base + a, base + b, base + c))


# --------------------------------------------------------------------------
# plant parts
# --------------------------------------------------------------------------

def make_leaf(name, origin, along, across, up, length, half_width, rows, sides,
              coll, rng, cells=0, cell_radius=CELL_RADIUS, fantasy=False, phase=None):
    """One ice-plant leaf.

    The blade is lofted along `along`; `across` is the lateral axis and `up` the
    upward normal. The cross-section is a rounded triangle with the keel pointing
    away from `up`, so the leaf has a real wedge silhouette rather than a flat card.
    """
    verts, faces, uvs = [], [], []
    tri = LEAF_TRI
    keel_phase = phase if phase is not None else rng.uniform(0.0, math.tau)
    twist = rng.uniform(-0.05, 0.05)
    # the leaf arcs: it lifts off the stem, flattens, then the tip droops a little
    arc = rng.uniform(0.05, 0.13)
    droop = rng.uniform(0.05, 0.12)

    def frame(t):
        # curvature of the midrib
        lift = math.sin(t * math.pi * 0.85) * arc
        tip_fall = -math.pow(max(0.0, t - 0.72) / 0.28, 2.0) * droop
        p = origin + along * (t * length) + up * (lift + tip_fall) * length
        return p

    def section(t):
        w = half_width * leaf_width_profile(t)
        h = half_width * leaf_thickness_profile(t)
        return w, h

    for i in range(rows + 1):
        t = i / rows
        p = frame(t)
        w, h = section(t)
        # the keel: one lobe of the triangle points down (-up)
        for j in range(sides + 1):
            th = math.tau * j / sides
            r = section_radius(th, w, tri, twist, keel_phase)
            # flatten the top into a shallow gabled surface, keep the keel below
            lateral = r * math.cos(th)
            vertical = r * math.sin(th)
            # squeeze the upper half so the leaf is a wedge, not a cone
            if vertical > 0.0:
                vertical *= 0.62
            else:
                vertical *= 1.20
            # narrow the blade sideways relative to its depth: a flat-ish top
            lateral *= 0.90
            pos = p + across * lateral + up * vertical
            verts.append((pos.x, pos.y, pos.z))
            uvs.append((t, j / sides))

    for i in range(rows):
        for j in range(sides):
            a = i * (sides + 1) + j
            b = a + 1
            c = a + sides + 1
            d = c + 1
            faces.append((a, c, b))
            faces.append((b, c, d))

    obj = new_mesh_object(name, verts, faces, uvs, coll, leaf_material(fantasy=fantasy))

    if cells > 0:
        cverts, cfaces, cuvs = [], [], []
        for _ in range(cells):
            # the bladder cells crowd the distal half and the two upper shoulders,
            # which is where the light catches them on a real plant
            t = rng.uniform(0.20, 1.0)
            th = rng.uniform(math.radians(26.0), math.radians(154.0))
            p = frame(t)
            w, h = section(t)
            r = section_radius(th, w, tri, twist, keel_phase)
            lateral = r * math.cos(th) * 0.90
            vertical = r * math.sin(th) * (0.62 if vertical_is_up(th) else 1.20)
            pos = p + across * lateral + up * vertical
            rad = cell_radius * rng.uniform(0.62, 1.45) * (0.62 + 0.72 * t)
            append_cell(cverts, cfaces, cuvs, (pos.x, pos.y, pos.z), rad, 0.66, t, 0.5)
        cellobj = new_mesh_object(name + "_cells", cverts, cfaces, cuvs, coll,
                                  cell_material(fantasy=fantasy))
        cellobj.parent = obj
        obj = cellobj
    return obj


def vertical_is_up(th):
    """True when the cross-section angle points toward the upper face."""
    return math.sin(th) > 0.0


def make_stem(name, path, radii, coll, rng, node_indices, tri=0.22):
    """Prostrate stem with a faint three-ribbed section and nodal swellings."""
    verts, faces = [], []
    sides = 9
    phase = rng.uniform(0.0, math.tau)
    for i, (p, r) in enumerate(zip(path, radii)):
        if i == 0:
            tangent = Vector(path[1]) - Vector(path[0])
        elif i == len(path) - 1:
            tangent = Vector(path[-1]) - Vector(path[-2])
        else:
            tangent = Vector(path[i + 1]) - Vector(path[i - 1])
        tangent.normalize()
        ref = Vector((0, 0, 1)) if abs(tangent.z) < 0.9 else Vector((1, 0, 0))
        side = tangent.cross(ref).normalized()
        up = side.cross(tangent).normalized()
        for s in range(sides + 1):
            th = math.tau * s / sides
            rr = r * (1.0 + tri * math.cos(3.0 * (th - phase)))
            verts.append(tuple(Vector(p) + side * (math.cos(th) * rr) + up * (math.sin(th) * rr)))
    for i in range(len(path) - 1):
        for s in range(sides):
            a = i * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll, stem_material())
    return obj


def make_flower(name, centre, radius, coll, rng, fantasy=False):
    """Daisy-like corolla: many narrow petals over a ring of stamens."""
    objs = []
    # receptacle
    verts, faces = [], []
    rings, sides = 3, 12
    for r in range(rings + 1):
        phi = (math.pi * 0.5) * r / rings
        for s in range(sides + 1):
            th = math.tau * s / sides
            x = math.cos(phi) * math.cos(th) * radius * 0.30
            y = math.sin(phi) * radius * 0.30
            z = math.cos(phi) * math.sin(th) * radius * 0.30
            verts.append((centre[0] + x, centre[1] + y, centre[2] + z))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    calyx = new_mesh_object(name + "_calyx", verts, faces, None, coll, calyx_material())
    objs.append(calyx)

    # petals
    for p in range(FLOWER_PETALS):
        a = math.tau * p / FLOWER_PETALS + rng.uniform(-0.02, 0.02)
        direction = Vector((math.cos(a), 0.0, math.sin(a)))
        side = Vector((-direction.z, 0.0, direction.x))
        base = Vector(centre) + direction * (radius * 0.22)
        length = radius * rng.uniform(0.80, 1.0)
        width = length * 0.115
        pv, pf = [], []
        cols, steps = 3, 4
        for i in range(steps + 1):
            tt = i / steps
            mid = base + direction * (length * tt)
            # the petal lifts and then curls back
            lift = math.sin(tt * math.pi * 0.8) * length * 0.10 - (tt ** 3) * length * 0.22
            for j in range(cols):
                x = (j / (cols - 1) - 0.5) * 2.0
                w = width * (0.35 + 0.65 * math.sin(math.pi * (0.18 + 0.82 * tt)))
                pv.append(tuple(mid + side * (x * w) + Vector((0.0, lift, 0.0))))
        for i in range(steps):
            for j in range(cols - 1):
                a0 = i * cols + j
                pf.append((a0, a0 + cols, a0 + 1))
                pf.append((a0 + 1, a0 + cols, a0 + cols + 1))
        petal = new_mesh_object("%s_petal_%02d" % (name, p), pv, pf, None, coll,
                                petal_material(fantasy=fantasy))
        objs.append(petal)

    # stamens: a ring of fine filaments with anthers
    sv, sf = [], []
    for s in range(26):
        a = math.tau * s / 26 + rng.uniform(-0.05, 0.05)
        rr = radius * rng.uniform(0.10, 0.20)
        base = Vector((centre[0] + math.cos(a) * rr, centre[1] + rng.uniform(0.0, radius * 0.08),
                       centre[2] + math.sin(a) * rr))
        tip = base + Vector((math.cos(a) * radius * 0.06, radius * 0.10, math.sin(a) * radius * 0.06))
        idx = len(sv)
        sv.append(tuple(base))
        sv.append(tuple(tip))
        sf.append((idx, idx + 1, idx))
    stamens = new_mesh_object(name + "_stamens", sv, sf, None, coll, stamen_material())
    objs.append(stamens)
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


def leaf_material(fantasy=False):
    mat = bpy.data.materials.new("iceplant_leaf")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.40)
    _set(bsdf, "Subsurface Weight", 0.22)
    _set(bsdf, "Subsurface Radius", (0.010, 0.016, 0.008))
    # glaucous green at the base, anthocyanin flush at the tip
    tex = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tex.outputs["UV"], sep.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.interpolation = 'EASE'
    ramp.color_ramp.elements[0].position = 0.10
    ramp.color_ramp.elements[0].color = (0.115, 0.235, 0.135, 1.0)
    mid = ramp.color_ramp.elements.new(0.62)
    mid.color = (0.165, 0.290, 0.170, 1.0)
    ramp.color_ramp.elements[-1].position = 0.97
    ramp.color_ramp.elements[-1].color = (0.360, 0.085, 0.105, 1.0)
    nt.links.new(sep.outputs["X"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def cell_material(fantasy=False):
    mat = bpy.data.materials.new("iceplant_bladder_cell")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", AZURE)
        _set(bsdf, "Roughness", 0.10)
        _set(bsdf, "Transmission Weight", 0.20)
        _set(bsdf, "Emission Color", AZURE_DEEP)
        _set(bsdf, "Emission Strength", 2.6)
    else:
        _set(bsdf, "Base Color", (0.86, 0.94, 0.88, 1.0))
        _set(bsdf, "Roughness", 0.12)
        _set(bsdf, "Transmission Weight", 0.55)
        _set(bsdf, "Emission Color", (0.70, 0.88, 0.95, 1.0))
        _set(bsdf, "Emission Strength", 0.10)
    return mat


def stem_material():
    mat = bpy.data.materials.new("iceplant_stem")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.215, 0.225, 0.150, 1.0))
    _set(bsdf, "Roughness", 0.66)
    return mat


def calyx_material():
    mat = bpy.data.materials.new("iceplant_calyx")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.170, 0.260, 0.140, 1.0))
    _set(bsdf, "Roughness", 0.62)
    return mat


def petal_material(fantasy=False):
    mat = bpy.data.materials.new("iceplant_petal")
    mat.use_nodes = True
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.92, 0.93, 1.0, 1.0))
    else:
        _set(bsdf, "Base Color", (0.86, 0.36, 0.46, 1.0))
    _set(bsdf, "Roughness", 0.42)
    _set(bsdf, "Subsurface Weight", 0.35)
    _set(bsdf, "Subsurface Radius", (0.004, 0.006, 0.010))
    return mat


def stamen_material():
    mat = bpy.data.materials.new("iceplant_stamen")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.92, 0.74, 0.20, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def soil_material():
    mat = bpy.data.materials.new("soil")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 65.0
    noise.inputs["Detail"].default_value = 8.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.050, 0.042, 0.030, 1.0)
    ramp.color_ramp.elements[1].position = 0.70
    ramp.color_ramp.elements[1].color = (0.145, 0.122, 0.090, 1.0)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.6
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


def build_reference_mat(rng, fantasy=False):
    """One prostrate ice-plant mat, authored at true world proportions."""
    coll = bpy.context.scene.collection

    n_shoots = 7
    for k in range(n_shoots):
        az = math.tau * k / n_shoots + rng.uniform(-0.22, 0.22)
        steps = 7
        path, radii = [], []
        for s in range(steps + 1):
            t = s / steps
            # the shoot creeps outward and wanders
            a = az + 0.16 * math.sin(t * math.pi * 1.6 + k)
            r = t * SHOOT_LENGTH * rng.uniform(0.85, 1.05)
            # the stem lifts a little at the crown then settles onto the ground
            lift = 0.004 * (1.0 - t) ** 2 + 0.0015 * math.sin(t * 5.0)
            path.append((math.cos(a) * r, lift, math.sin(a) * r))
            radii.append(STEM_RADIUS * (1.0 - 0.42 * t))
        make_stem("shoot_%d" % k, path, radii, coll, rng, [2, 4, 6])

        # leaves: opposite and decussate, so every node rotates 90 degrees
        node_az = az + rng.uniform(0.0, math.tau)
        for s in range(1, steps + 1):
            node = Vector(path[s])
            for pair in (0, 1):
                la = node_az + pair * math.pi
                node_az += math.pi * 0.5
                # the leaf lies along the stem, then lifts and turns outward
                along = Vector((math.cos(la), 0.0, math.sin(la)))
                lift = Vector((0.0, 1.0, 0.0))
                across = lift.cross(along).normalized()
                tilt = math.radians(rng.uniform(6.0, 30.0))
                along = (along * math.cos(tilt) + lift * math.sin(tilt)).normalized()
                up = across.cross(along).normalized()
                length = LEAF_LENGTH * rng.uniform(0.72, 1.0)
                make_leaf("leaf_%d_%d_%d" % (k, s, pair), node, along, across, up,
                          length, LEAF_WIDTH * (length / LEAF_LENGTH),
                          rows=12, sides=10, coll=coll, rng=rng,
                          cells=CELL_COUNT_PER_LEAF, fantasy=fantasy)

    # flowers on short peduncles from the shoot tips
    for f in range(5):
        a = math.tau * f / 5 + rng.uniform(-0.3, 0.3)
        rr = MAT_RADIUS * rng.uniform(0.35, 0.85)
        base = Vector((math.cos(a) * rr * 0.55, 0.003, math.sin(a) * rr * 0.55))
        top = Vector((math.cos(a) * rr, rng.uniform(0.016, 0.030), math.sin(a) * rr))
        # peduncle
        pv, pf = [], []
        steps = 4
        for i in range(steps + 1):
            t = i / steps
            p = base.lerp(top, t)
            pv.append((p.x, p.y, p.z))
        for i in range(steps):
            pf.append((i, i + 1, i + 2))
        ped = new_mesh_object("peduncle_%d" % f, pv, pf, None, coll, stem_material())
        make_flower("flower_%d" % f, top, FLOWER_RADIUS, coll, rng, fantasy=fantasy)

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


def setup_camera_and_light(size=0.30, elevation=24.0, azimuth=42.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.05))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 3.1
    sun_data.angle = math.radians(2.4)
    sun_data.color = (1.0, 0.96, 0.88)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(48.0), 0.0, math.radians(32.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 9.0
    fill_data.size = 0.8
    fill_data.color = (0.86, 0.92, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.45, 0.30, 0.40)
    fill.rotation_euler = (math.radians(58.0), 0.0, math.radians(-125.0))
    bpy.context.scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.40, 0.44, 0.48, 1.0)
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


def build_leaf_detail(rng, fantasy=False):
    """A single leaf, large in frame, to study the blade anatomy and bladder cells."""
    coll = bpy.context.scene.collection
    base = Vector((0.0, 0.0, 0.0))
    along = Vector((1.0, 0.0, 0.0))
    up = Vector((0.0, 1.0, 0.0))
    across = up.cross(along).normalized()
    make_leaf("detail_leaf", base, along, across, up, LEAF_LENGTH * 1.35,
              LEAF_WIDTH * 1.35, rows=22, sides=16, coll=coll, rng=rng,
              cells=CELL_COUNT_PER_LEAF * 3, fantasy=fantasy)
    y_up_to_z_up()


def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference",
                    choices=["reference", "fantasy", "mesh", "detail"])
    ap.add_argument("--out", default="build/glassfinger-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--scale", default="0.125,0.1,0.125",
                    help="engine mesh frame -> metres: radius, height, radius")
    opts = ap.parse_args(argv[argv.index("--") + 1:] if "--" in argv else [])

    _init_ico()
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
        setup_camera_and_light(size=0.30)
    elif opts.mode == "detail":
        build_leaf_detail(rng, fantasy=(opts.mode == "fantasy"))
        setup_camera_and_light(size=0.055, elevation=20.0, azimuth=35.0)
    else:
        build_reference_mat(rng, fantasy=(opts.mode == "fantasy"))
        add_ground(MAT_RADIUS * 1.5)
        setup_camera_and_light(size=MAT_RADIUS * 2.6)

    setup_render(res=opts.res, samples=opts.samples)
    path = os.path.join(opts.out, "iceplant_%s.png" % opts.mode)
    bpy.context.scene.render.filepath = path
    if opts.no_render:
        print("BLENDER_BUILD_OK objects", len(bpy.context.scene.objects))
        return
    bpy.ops.render.render(write_still=True)
    print("BLENDER_OK", path)


if __name__ == "__main__":
    main()
