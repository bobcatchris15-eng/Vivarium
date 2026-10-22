"""Blackberry / raspberry (Rubus) thicket reference for the Vivarium 'hookthicket' brake.

This is the real-life reference the hookthicket is rebuilt against. It builds a bramble thicket
from the species' own anatomy and renders it under matched light:

  * a knotted perennial crown sending up arching, angular canes,
  * stout recurved prickles in rows down the canes and out along the petioles,
  * palmately compound leaves of five serrated, rugose leaflets,
  * narrow stipules clasping the petiole base,
  * pale five-petalled flowers and dark aggregate berries,
  * a cane tip that reaches the soil, roots, and throws up a fresh shoot of its own.

The engine generator that mirrors this is
``src/Vivarium.Sim/Geometry/OrganismMeshes.Hookthicket.cs``.

Usage:
    blender --background --python bramble_reference.py -- --mode reference --out DIR
    blender --background --python bramble_reference.py` -- --mode detail   --out DIR
    blender --background --python bramble_reference.py -- --mode mesh --obj FILE --out DIR
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

# Leaflet breadth along its length, as a fraction of its own length. A blackberry leaflet is
# broadest a little below the middle and tapers to a sharp point.
LEAFLET_PROFILE = [
    (0.00, 0.18), (0.10, 0.42), (0.22, 0.62), (0.36, 0.78), (0.50, 0.86),
    (0.64, 0.82), (0.76, 0.68), (0.86, 0.48), (0.94, 0.26), (1.00, 0.05),
]

# A bramble leaflet is sharply toothed: the teeth point at the tip.
TEETH = 11


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


def leaflet_breadth(t):
    return _catmull(LEAFLET_PROFILE, t)


def tooth_offset(t, phase):
    s = t * TEETH + phase
    frac = s - math.floor(s)
    return (1.0 - frac) ** 1.3


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


def make_leaflet(name, base, direction, normal, length, breadth, rows, cols, phase, relief, coll,
                 top_colour, back_colour):
    """One bramble leaflet: ovate, sharply toothed, rugose between the veins."""
    verts, faces, uvs = [], [], []
    side = normal.cross(direction)
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(direction).normalized()

    def surface(t, x, tooth):
        w = breadth * length * 0.5 * leaflet_breadth(t)
        lateral = x * w * (1.0 + tooth)
        # A bramble leaf is rugose: it bulges between the lateral veins and dips along them.
        vein = math.cos(t * math.pi * 6.0 + phase * 6.0)
        wrinkle = relief["wrinkle"] * vein * (1.0 - abs(x) * 0.6)
        keel = relief["keel"] * (1.0 - abs(x)) ** 2
        droop = -relief["droop"] * t * t
        return base + direction * (t * length) + side * lateral + up * ((keel + wrinkle + droop) * length)

    for i in range(rows):
        t = i / (rows - 1)
        tooth = relief["tooth"] * (tooth_offset(t, phase) - 0.45) if i % 2 == 0 else \
            relief["tooth"] * (tooth_offset(t - 0.5 / TEETH, phase) - 0.45)
        for j in range(cols):
            x = (j / (cols - 1)) - 0.5
            x = x * 2.0
            edge = tooth if j in (0, cols - 1) else 0.0
            verts.append(tuple(surface(t, x, edge)))
            uvs.append((t, (x + 1.0) * 0.5))
    for i in range(rows - 1):
        for j in range(cols - 1):
            a = i * cols + j
            faces.append((a, a + cols, a + 1))
            faces.append((a + 1, a + cols, a + cols + 1))
    obj = new_mesh_object(name, verts, faces, uvs, coll)
    obj.data.materials.append(leaf_material(top_colour, back_colour))
    return obj


def make_cane(name, points, radii, coll, colour, sides=7):
    """An angular bramble cane: a bramble stem is pentagonal in section, not round."""
    verts, faces = [], []
    irregular = [1.0, 0.82, 1.12, 0.78, 1.05, 0.86, 1.08]
    for i, p in enumerate(points):
        if i == 0:
            tangent = points[1] - points[0]
        elif i == len(points) - 1:
            tangent = points[-1] - points[-2]
        else:
            tangent = points[i + 1] - points[i - 1]
        tangent.normalize()
        ref = Vector((0, 0, 1)) if abs(tangent.z) < 0.9 else Vector((1, 0, 0))
        side = tangent.cross(ref).normalized()
        up = side.cross(tangent).normalized()
        for s in range(sides + 1):
            a = 2.0 * math.pi * s / sides
            r = radii[i] * (irregular[s % sides] if s < sides else irregular[0])
            verts.append(tuple(p + side * (math.cos(a) * r) + up * (math.sin(a) * r)))
    for i in range(len(points) - 1):
        for s in range(sides):
            a = i * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(cane_material(colour))
    return obj


def make_prickle(name, base, outward, tangent, length, coll):
    """A stout, recurved, barbed prickle."""
    side = outward.cross(tangent)
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    low = base - tangent * length * 0.22
    elbow = base + outward * length * 0.62
    tip = base + outward * length - tangent * length * 0.72
    verts = [tuple(low), tuple(base - side * length * 0.10), tuple(elbow), tuple(tip),
             tuple(base + side * length * 0.10)]
    faces = [(0, 2, 1), (0, 3, 2), (0, 4, 3), (1, 2, 4), (2, 3, 4)]
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(prickle_material())


def make_berry(name, centre, radius, coll):
    """An aggregate berry: a cluster of drupelets."""
    verts, faces = [], []
    drupelets = 14
    for d in range(drupelets):
        y = 1.0 - (d + 0.5) / drupelets
        rad = math.sqrt(max(0.0, 1.0 - y * y))
        theta = d * 2.399963
        c = Vector((centre.x + math.cos(theta) * rad * radius * 0.86,
                    centre.y + y * radius * 0.92,
                    centre.z + math.sin(theta) * rad * radius * 0.86))
        r = radius * 0.30
        base = len(verts)
        rings, sides = 3, 6
        for rr in range(rings + 1):
            phi = math.pi * rr / rings
            for s in range(sides + 1):
                th = 2.0 * math.pi * s / sides
                verts.append((c.x + math.sin(phi) * math.cos(th) * r,
                              c.y + math.cos(phi) * r,
                              c.z + math.sin(phi) * math.sin(th) * r))
        for rr in range(rings):
            for s in range(sides):
                a = base + rr * (sides + 1) + s
                faces.append((a, a + sides + 1, a + 1))
                faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(berry_material())


def make_flower(name, centre, radius, coll, rng):
    """A five-petalled bramble flower with a crown of stamens."""
    for p in range(5):
        a = p * 2.0 * math.pi / 5.0 + rng.uniform(-0.12, 0.12)
        direction = Vector((math.cos(a), 0.0, math.sin(a)))
        side = Vector((0, 1, 0)).cross(direction).normalized()
        base = centre + direction * radius * 0.16
        tip = centre + direction * radius + Vector((0, radius * 0.10, 0))
        verts = [tuple(base - side * radius * 0.30), tuple(base + side * radius * 0.30), tuple(tip)]
        obj = new_mesh_object("%s_petal_%d" % (name, p), verts, [(0, 1, 2)], None, coll)
        obj.data.materials.append(flower_material())
    verts, faces = [], []
    sides = 10
    for s in range(sides + 1):
        th = 2.0 * math.pi * s / sides
        verts.append((centre.x + math.cos(th) * radius * 0.12,
                      centre.y + radius * 0.10,
                      centre.z + math.sin(th) * radius * 0.12))
    for s in range(sides):
        faces.append((0, s + 1, s + 2))
    obj = new_mesh_object(name + "_centre", verts, faces, None, coll)
    obj.data.materials.append(stamen_material())


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


def leaf_material(top, back):
    mat = bpy.data.materials.new("bramble_leaf")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.52)
    _set(bsdf, "Subsurface Weight", 0.18)
    _set(bsdf, "Subsurface Radius", (0.004, 0.010, 0.003))
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = top + (1.0,)
    mix.inputs[7].default_value = back + (1.0,)
    nt.links.new(geo.outputs["Backfacing"], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def cane_material(colour):
    mat = bpy.data.materials.new("bramble_cane")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", colour + (1.0,))
    _set(bsdf, "Roughness", 0.80)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 26.0
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.65
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def prickle_material():
    mat = bpy.data.materials.new("bramble_prickle")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.085, 0.065, 0.050, 1.0))
    _set(bsdf, "Roughness", 0.34)
    _set(bsdf, "Specular IOR Level", 0.6)
    return mat


def berry_material():
    mat = bpy.data.materials.new("bramble_berry")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.055, 0.020, 0.035, 1.0))
    _set(bsdf, "Roughness", 0.22)
    _set(bsdf, "Emission Color", (0.85, 0.20, 0.30, 1.0))
    _set(bsdf, "Emission Strength", 0.9)
    return mat


def flower_material():
    mat = bpy.data.materials.new("bramble_flower")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.88, 0.85, 0.82, 1.0))
    _set(bsdf, "Roughness", 0.62)
    _set(bsdf, "Subsurface Weight", 0.25)
    return mat


def stamen_material():
    mat = bpy.data.materials.new("bramble_stamen")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.92, 0.78, 0.30, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def soil_material():
    mat = bpy.data.materials.new("soil")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 48.0
    noise.inputs["Detail"].default_value = 8.0
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.048, 0.040, 0.028, 1.0)
    ramp.color_ramp.elements[1].position = 0.68
    ramp.color_ramp.elements[1].color = (0.120, 0.100, 0.072, 1.0)
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


def y_up_to_z_up():
    """The thicket is authored in the engine's Y-up frame; Blender is Z-up."""
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for obj in list(bpy.context.scene.objects):
        obj.matrix_world = x90 @ obj.matrix_world


def build_thicket(rng, radius=0.9, height=1.15, canes=11):
    """One bramble thicket, authored at true world proportions."""
    coll = bpy.context.scene.collection
    leaf_top = (0.075, 0.155, 0.055)
    leaf_back = (0.145, 0.170, 0.135)
    cane_colour = (0.115, 0.085, 0.060)
    relief = {"wrinkle": 0.020, "keel": 0.030, "droop": 0.045, "tooth": 0.16}

    for k in range(canes):
        az = 2.0 * math.pi * k / canes + rng.uniform(-0.22, 0.22)
        dir = Vector((math.cos(az), 0.0, math.sin(az)))
        side = Vector((-dir.z, 0.0, dir.x))
        root = dir * rng.uniform(0.04, 0.16)
        reach = radius * rng.uniform(0.62, 0.92)
        arch = height * rng.uniform(0.55, 0.80)

        def point(t):
            # The cane rises out of the crown, arches over and comes back down to the soil.
            return (root + dir * (reach * (1.0 - math.cos(math.pi * t)) * 0.5)
                    + Vector((0, arch * math.sin(math.pi * t), 0))
                    + side * (0.16 * math.sin(k * 0.7) * math.sin(math.pi * t)))

        steps = 40
        pts = [point(i / steps) for i in range(steps + 1)]
        radii = [0.0135 * (1.0 - 0.74 * (i / steps)) for i in range(steps + 1)]
        make_cane("cane_%d" % k, pts, radii, coll, cane_colour)

        # Prickles in rows down the cane.
        for j in range(1, 22):
            t = j / 23.0
            p = point(t)
            tangent = (point(min(1.0, t + 0.01)) - point(max(0.0, t - 0.01))).normalized()
            for sgn in (-1.0, 1.0):
                out = (side * sgn * 0.86 + tangent * 0.5).normalized()
                make_prickle("prickle_%d_%d_%d" % (k, j, int(sgn)), p, out, tangent,
                             0.028 * (1.2 - 0.6 * t) * rng.uniform(0.8, 1.2), coll)

        # Leaves: palmately compound, five leaflets on a long petiole.
        for j in range(2, 19):
            t = 0.10 + 0.78 * (j / 19.0)
            p = point(t)
            tangent = (point(min(1.0, t + 0.01)) - point(max(0.0, t - 0.01))).normalized()
            leaf_az = rng.uniform(0, 2 * math.pi)
            leaf_dir = Vector((math.cos(leaf_az), 0.0, math.sin(leaf_az)))
            pet_len = rng.uniform(0.12, 0.24)
            petiole_end = p + leaf_dir * pet_len + Vector((0, pet_len * 0.35, 0))
            make_cane("petiole_%d_%d" % (k, j), [p, petiole_end],
                      [0.0042, 0.0022], coll, cane_colour, sides=6)
            for leaflet in range(5):
                a = leaf_az + (leaflet - 2) * math.pi / 2.6
                fwd = Vector((math.cos(a), 0.0, math.sin(a)))
                length = rng.uniform(0.13, 0.19) if leaflet == 2 else rng.uniform(0.10, 0.15)
                make_leaflet("leaf_%d_%d_%d" % (k, j, leaflet), petiole_end, fwd,
                             Vector((0, 1, 0)), length, rng.uniform(0.62, 0.82),
                             9, 5, rng.uniform(0, 1), relief, coll, leaf_top, leaf_back)

        # Flowers and berries on the second-year wood.
        for f in range(2):
            t = rng.uniform(0.45, 0.85)
            p = point(t)
            out = dir * rng.uniform(0.05, 0.12) + Vector((0, -0.04, 0))
            centre = p + out
            if rng.random() < 0.5:
                make_flower("flower_%d_%d" % (k, f), centre, 0.020, coll, rng)
            else:
                make_berry("berry_%d_%d" % (k, f), centre, 0.018, coll)

        # Tip layering: the cane reaches the soil, roots, and throws up a fresh shoot.
        tip = point(1.0)
        for r in range(3):
            end = tip + dir * (0.02 + r * 0.012) + side * ((r - 1) * 0.02) - Vector((0, 0.02, 0))
            make_cane("root_%d_%d" % (k, r), [tip, (tip + end) * 0.5, end],
                      [0.0032, 0.0018, 0.0008], coll, cane_colour, sides=6)
        for s in range(3):
            a = rng.uniform(0, 2 * math.pi)
            up_dir = Vector((math.cos(a) * 0.35, 1.0, math.sin(a) * 0.35)).normalized()
            shoot_top = tip + up_dir * rng.uniform(0.08, 0.16)
            make_cane("shoot_%d_%d" % (k, s), [tip, shoot_top], [0.0055, 0.0022], coll,
                      (0.16, 0.13, 0.08), sides=6)
            for leaflet in range(3):
                fa = rng.uniform(0, 2 * math.pi)
                fwd = Vector((math.cos(fa), 0.0, math.sin(fa)))
                make_leaflet("shootleaf_%d_%d_%d" % (k, s, leaflet), shoot_top, fwd,
                             Vector((0, 1, 0)), rng.uniform(0.05, 0.08), 0.7, 7, 5,
                             rng.uniform(0, 1), relief, coll, leaf_top, leaf_back)

    # The crown: a knotted burl of old wood where every cane leaves.
    verts, faces = [], []
    rings, sides = 4, 12
    for r in range(rings + 1):
        phi = math.pi * 0.5 * r / rings
        for s in range(sides + 1):
            th = 2.0 * math.pi * s / sides
            bulge = 1.0 + 0.22 * math.sin(s * 2.3) + 0.12 * math.sin(s * 5.1 + r)
            verts.append((math.cos(phi) * math.cos(th) * 0.075 * bulge,
                          math.sin(phi) * 0.075,
                          math.cos(phi) * math.sin(th) * 0.075 * bulge))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    crown = new_mesh_object("crown", verts, faces, None, coll)
    crown.data.materials.append(cane_material(cane_colour))
    y_up_to_z_up()


def add_ground(radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=radius, depth=0.02,
                                        location=(0, 0, -0.012))
    ground = bpy.context.active_object
    ground.data.materials.append(soil_material())
    return ground


def setup_camera_and_light(size=2.1, elevation=16.0, azimuth=35.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.21))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 3.0
    sun_data.angle = math.radians(2.0)
    sun_data.color = (1.0, 0.95, 0.88)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(54.0), 0.0, math.radians(30.0))
    bpy.context.scene.collection.objects.link(sun)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.30, 0.38, 0.46, 1.0)
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


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference", choices=["reference", "detail", "mesh"])
    ap.add_argument("--obj", default="")
    ap.add_argument("--out", default=".")
    ap.add_argument("--seed", type=int, default=5)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    args = ap.parse_args(argv)

    reset_scene()
    rng = random.Random(args.seed)
    os.makedirs(args.out, exist_ok=True)

    if args.mode == "mesh":
        build_from_obj(args.obj)
        add_ground(1.0)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_bramble.png")
    elif args.mode == "detail":
        build_thicket(rng, radius=0.9, height=1.15, canes=3)
        add_ground(1.0)
        setup_camera_and_light(size=0.62, elevation=22.0, azimuth=28.0)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "bramble_detail.png")
    else:
        build_thicket(rng, radius=0.9, height=1.15)
        add_ground(1.0)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "bramble.png")

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


def build_from_obj(path):
    bpy.ops.wm.obj_import(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for o in imported:
        o.matrix_world = x90 @ Matrix.Diagonal((0.9, 1.15, 0.9, 1.0))
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
    _set(bsdf, "Roughness", 0.60)
    _set(bsdf, "Subsurface Weight", 0.0)
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


if __name__ == "__main__":
    main()
