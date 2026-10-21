"""Ostrich fern (Matteuccia struthiopteris) reference for the Vivarium 'veilfern' understory.

This is the real-life reference the veilfern is rebuilt against. It builds an ostrich fern from
scratch in Blender using the species' own anatomy:

  * a scaly crown holding a vase of arching sterile fronds,
  * lanceolate fronds carrying many pairs of sub-opposite, forward-swept pinnae,
  * pinnae that are themselves lobed, the way an ostrich fern's are,
  * golden-brown chaffy scales on the emerging croziers,
  * the bronze "shuttlecock" of persistent fertile fronds, its pinnae margins rolled into beads
    of sori.

The engine generator that mirrors this is
``src/Vivarium.Sim/Geometry/OrganismMeshes.Veilfern.cs``.

Usage:
    blender --background --python ostrich_fern_reference.py -- --mode reference --out DIR
    blender --background --python ostrich_fern_reference.py -- --mode detail   --out DIR
    blender --background --python ostrich_fern_reference.py -- --mode mesh --obj FILE --out DIR
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

# Pinna length along the frond, as a fraction of the maximum. An ostrich fern's pinnae are
# longest a little above the base and shrink steadily to the frond tip.
PINNA_PROFILE = [
    (0.00, 0.42), (0.08, 0.72), (0.18, 0.93), (0.30, 1.00), (0.45, 0.97),
    (0.60, 0.88), (0.72, 0.76), (0.84, 0.60), (0.93, 0.42), (1.00, 0.24),
]

# Pinna breadth as a fraction of its own length. Ostrich fern pinnae are broad, roughly a third
# as wide as they are long, and taper to a point.
PINNA_WIDTH = 0.42

# Lobed margin: the number of lobes down each side of a pinna.
PINNA_LOBES = 5


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


def pinna_length_fraction(t):
    return _catmull(PINNA_PROFILE, t)


def lobe_offset(t, row, phase):
    """Asymmetric lobe wave: fern pinnae lobes point towards the pinna tip."""
    s = t * PINNA_LOBES + phase
    frac = s - math.floor(s)
    return (1.0 - frac) ** 1.35


def make_pinna(name, base, direction, normal, length, half_width, rows, cols, phase, relief, coll,
               colour_top, colour_bottom):
    """One lobed pinna: a broad lanceolate blade with a raised midrib and lobed margins."""
    verts, faces, uvs = [], [], []
    side = normal.cross(direction)
    if side.length < 1e-6:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(direction).normalized()

    def surface(t, x):
        # x: -1 at one margin, 0 at the midrib, +1 at the other margin.
        lobe = 1.0 + relief["lobe"] * (lobe_offset(t, 0, phase) - 0.45) * abs(x)
        w = half_width * math.sin(math.pi * min(1.0, t ** 0.62)) ** 0.72 * lobe
        lateral = x * w
        # A raised midrib with the blade falling away to the margins, and a curled tip.
        keel = relief["keel"] * (1.0 - abs(x)) ** 2
        fall = -relief["fall"] * abs(x) ** 1.7
        curl = -relief["curl"] * max(0.0, (t - 0.72) / 0.28) ** 2
        return base + direction * (t * length) + side * lateral + up * (keel + fall + curl) * half_width

    for i in range(rows):
        t = i / (rows - 1)
        for j in range(cols):
            x = (j / (cols - 1)) - 0.5
            p = surface(t, x * 2.0)
            verts.append(tuple(p))
            uvs.append((t, (x + 0.5)))
    for i in range(rows - 1):
        for j in range(cols - 1):
            a = i * cols + j
            faces.append((a, a + cols, a + 1))
            faces.append((a + 1, a + cols, a + cols + 1))
    obj = new_mesh_object(name, verts, faces, uvs, coll)
    obj.data.materials.append(leaf_material(colour_top, colour_bottom))
    return obj


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


def bezier(p0, p1, p2, p3, t):
    u = 1.0 - t
    return p0 * (u ** 3) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t ** 3)


def bezier_tangent(p0, p1, p2, p3, t):
    u = 1.0 - t
    d = (p1 - p0) * (3 * u * u) + (p2 - p1) * (6 * u * t) + (p3 - p2) * (3 * t * t)
    return d.normalized() if d.length > 1e-9 else Vector((0, 1, 0))


def make_rachis(name, path, radii, coll, colour, sides=6):
    verts, faces = [], []
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
        r = radii[i]
        for s in range(sides + 1):
            a = 2.0 * math.pi * s / sides
            verts.append(tuple(p + side * (math.cos(a) * r) + up * (math.sin(a) * r)))
    for i in range(len(path) - 1):
        for s in range(sides):
            a = i * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(rachis_material(colour))
    return obj


def make_crozier(name, base, direction, height, coil_radius, coll, rng, chaff=14):
    """A fiddlehead: a stalk that coils at the tip, sheathed in golden-brown scales."""
    path, radii = [], []
    steps = 10
    for i in range(steps + 1):
        t = i / steps
        path.append(base + direction * (height * 0.62 * t) + Vector((0, 0, height * 0.30 * t)))
        radii.append(0.0032 * (1.0 - 0.35 * t))
    # The coil itself.
    centre = path[-1] + direction * coil_radius
    coil_steps = 18
    for j in range(1, coil_steps + 1):
        theta = math.pi * 2.0 * j / coil_steps
        r = coil_radius * (1.0 - 0.12 * j / coil_steps)
        p = centre + direction * (r * math.sin(theta)) + Vector((0, 0, r * (1 - math.cos(theta))))
        path.append(p)
        radii.append(0.0026 * (1.0 - 0.55 * j / coil_steps))
    make_rachis(name, path, radii, coll, (0.42, 0.34, 0.18), sides=7)

    # Chaffy scales: the golden-brown fleece an ostrich fern crozier carries as it emerges.
    for k in range(chaff):
        t = rng.uniform(0.15, 0.95)
        idx = int(t * steps)
        p = path[min(idx, len(path) - 1)]
        a = rng.uniform(0, 2 * math.pi)
        out = Vector((math.cos(a), math.sin(a), 0.25)).normalized()
        tip = p + out * rng.uniform(0.006, 0.013) + direction * rng.uniform(-0.004, 0.006)
        verts = [tuple(p), tuple(p.lerp(tip, 0.55) + out * 0.001), tuple(tip)]
        obj = new_mesh_object("%s_scale_%d" % (name, k), verts, [(0, 1, 2)], None, coll)
        obj.data.materials.append(chaff_material())


def make_sori_bead(name, centre, radius, coll):
    verts, faces = [], []
    rings, sides = 3, 6
    for r in range(rings + 1):
        phi = math.pi * r / rings
        for s in range(sides + 1):
            th = 2 * math.pi * s / sides
            verts.append((
                centre.x + math.sin(phi) * math.cos(th) * radius,
                centre.y + math.sin(phi) * math.sin(th) * radius,
                centre.z + math.cos(phi) * radius,
            ))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    obj = new_mesh_object(name, verts, faces, None, coll)
    obj.data.materials.append(sori_material())
    return obj


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


def leaf_material(top, bottom):
    mat = bpy.data.materials.new("fern_blade")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", top + (1.0,))
    _set(bsdf, "Roughness", 0.55)
    _set(bsdf, "Subsurface Weight", 0.22)
    _set(bsdf, "Subsurface Radius", (0.004, 0.010, 0.003))
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.inputs[6].default_value = top + (1.0,)
    mix.inputs[7].default_value = bottom + (1.0,)
    nt.links.new(geo.outputs["Backfacing"], mix.inputs["Factor"])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return mat


def rachis_material(colour):
    mat = bpy.data.materials.new("fern_rachis")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", colour + (1.0,))
    _set(bsdf, "Roughness", 0.60)
    return mat


def chaff_material():
    mat = bpy.data.materials.new("fern_chaff")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.52, 0.36, 0.16, 1.0))
    _set(bsdf, "Roughness", 0.78)
    return mat


def sori_material():
    mat = bpy.data.materials.new("fern_sori")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.62, 0.42, 0.16, 1.0))
    _set(bsdf, "Roughness", 0.42)
    _set(bsdf, "Emission Color", (0.35, 0.95, 0.70, 1.0))
    _set(bsdf, "Emission Strength", 2.4)
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


def build_fern(rng, radius=0.28, height=0.30, fantasy=True):
    """One ostrich fern, authored at true world proportions."""
    coll = bpy.context.scene.collection
    green_top = (0.085, 0.215, 0.075)
    green_bottom = (0.135, 0.265, 0.110)
    bronze = (0.24, 0.135, 0.055)
    bronze_bottom = (0.17, 0.095, 0.040)
    relief = {"lobe": 0.11, "keel": 0.075, "fall": 0.045, "curl": 0.065}

    # 1. Sterile fronds: a vase of arching plumes from the crown.
    fronds = 9
    for k in range(fronds):
        az = 2.0 * math.pi * k / fronds + rng.uniform(-0.14, 0.14)
        length = height * rng.uniform(1.15, 1.45)
        pitch = rng.uniform(0.55, 0.78)
        h = Vector((math.cos(az), 0.0, math.sin(az)))
        base = h * rng.uniform(0.008, 0.022)
        p0 = base
        p1 = base + Vector((0, 0, length * 0.62)) + h * (length * 0.10 * math.sin(pitch))
        p2 = base + Vector((0, 0, length * 0.86)) + h * (length * 0.62)
        p3 = base + Vector((0, 0, length * 0.22)) + h * (length * 1.02)

        steps = 13
        path, radii = [], []
        for i in range(steps + 1):
            t = i / steps
            path.append(bezier(p0, p1, p2, p3, t))
            radii.append(0.0055 * (1.0 - 0.80 * t))
        make_rachis("frond_%d_rachis" % k, path, radii, coll, (0.40, 0.36, 0.16))

        pairs = 26
        for i in range(pairs):
            t = 0.12 + 0.86 * (i / (pairs - 1))
            frac = pinna_length_fraction(t)
            pinna_len = length * 0.185 * frac
            if pinna_len < 0.006:
                continue
            for sgn in (-1.0, 1.0):
                tt = t + (0.012 if sgn > 0 else 0.0)
                rp = bezier(p0, p1, p2, p3, min(tt, 1.0))
                rt = bezier_tangent(p0, p1, p2, p3, min(tt, 1.0))
                rn = (Vector((0, 0, 1)) - rt * rt.z).normalized()
                rs = rt.cross(rn).normalized()
                # Pinnae sweep forwards towards the frond tip, as an ostrich fern's do.
                direction = (rs * (sgn * 0.72) + rt * 0.66 + rn * (0.16 * sgn)).normalized()
                normal = direction.cross(rt).normalized()
                if normal.dot(rn) < 0:
                    normal = -normal
                base_p = rp + rs * (sgn * 0.005)
                make_pinna("frond_%d_pinna_%d_%d" % (k, i, int(sgn)),
                           base_p, direction, normal, pinna_len,
                           pinna_len * PINNA_WIDTH * 0.5, 9, 5, rng.uniform(0, 1),
                           relief, coll, green_top, green_bottom)

    # 2. The fertile shuttlecock: short bronze fronds standing in the crown, their pinna
    #    margins rolled into beads of sori. This is how an ostrich fern overwinters.
    fertile = 5
    for k in range(fertile):
        az = 2.0 * math.pi * k / fertile + rng.uniform(-0.2, 0.2)
        h = Vector((math.cos(az), 0.0, math.sin(az)))
        length = height * rng.uniform(0.62, 0.80)
        base = h * rng.uniform(0.004, 0.012)
        p0 = base
        p1 = base + Vector((0, 0, length * 0.70)) + h * 0.004
        p2 = base + Vector((0, 0, length * 0.96)) + h * 0.012
        p3 = base + Vector((0, 0, length * 0.80)) + h * 0.030
        steps = 9
        path, radii = [], []
        for i in range(steps + 1):
            t = i / steps
            path.append(bezier(p0, p1, p2, p3, t))
            radii.append(0.0030 * (1.0 - 0.70 * t))
        make_rachis("fertile_%d_rachis" % k, path, radii, coll, (0.30, 0.18, 0.08))

        pairs = 9
        for i in range(pairs):
            t = 0.16 + 0.74 * (i / (pairs - 1))
            frac = pinna_length_fraction(t)
            pinna_len = length * 0.15 * frac
            if pinna_len < 0.004:
                continue
            for sgn in (-1.0, 1.0):
                rp = bezier(p0, p1, p2, p3, t)
                rt = bezier_tangent(p0, p1, p2, p3, t)
                rn = (Vector((0, 0, 1)) - rt * rt.z).normalized()
                rs = rt.cross(rn).normalized()
                direction = (rs * (sgn * 0.62) + rt * 0.72 + rn * (0.12 * sgn)).normalized()
                normal = direction.cross(rt).normalized()
                if normal.dot(rn) < 0:
                    normal = -normal
                base_p = rp + rs * (sgn * 0.003)
                make_pinna("fertile_%d_pinna_%d_%d" % (k, i, int(sgn)),
                           base_p, direction, normal, pinna_len,
                           pinna_len * 0.26 * 0.5, 7, 5, rng.uniform(0, 1),
                           dict(relief, lobe=0.12), coll, bronze, bronze_bottom)
                # Sori: a row of beads along the rolled margin of the lower pinnae.
                if i < pairs * 0.7:
                    for b in range(4):
                        f = 0.25 + 0.5 * b / 3.0
                        p = base_p + direction * (pinna_len * f)
                        p = p + rs * (sgn * pinna_len * PINNA_WIDTH * 0.30)
                        make_sori_bead("sori_%d_%d_%d_%d" % (k, i, int(sgn), b),
                                       p, 0.0016, coll)

    # 3. Emerging croziers in the middle of the crown.
    for k in range(3):
        az = rng.uniform(0, 2 * math.pi)
        h = Vector((math.cos(az), 0.0, math.sin(az)))
        base = h * rng.uniform(0.004, 0.012)
        make_crozier("crozier_%d" % k, base, h, height * rng.uniform(0.55, 0.80),
                     height * rng.uniform(0.07, 0.11), coll, rng)

    # 4. The crown itself: a stubby scaly caudex.
    verts, faces = [], []
    rings, sides = 3, 10
    for r in range(rings + 1):
        phi = math.pi * 0.5 * r / rings
        for s in range(sides + 1):
            th = 2.0 * math.pi * s / sides
            verts.append((
                math.cos(phi) * math.cos(th) * 0.028,
                math.cos(phi) * math.sin(th) * 0.028,
                0.006 + math.sin(phi) * 0.030,
            ))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            faces.append((a, a + sides + 1, a + 1))
            faces.append((a + 1, a + sides + 1, a + sides + 2))
    crown = new_mesh_object("caudex", verts, faces, None, coll)
    crown.data.materials.append(chaff_material())


def add_ground(radius):
    bpy.ops.mesh.primitive_cylinder_add(vertices=96, radius=radius, depth=0.02,
                                        location=(0, 0, -0.012))
    ground = bpy.context.active_object
    ground.data.materials.append(soil_material())
    return ground


def setup_camera_and_light(size=0.72, elevation=24.0, azimuth=40.0):
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size
    cam = bpy.data.objects.new("Camera", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    dist = size * 2.2
    el = math.radians(elevation)
    az = math.radians(azimuth)
    target = Vector((0.0, 0.0, size * 0.16))
    offset = Vector((math.cos(el) * math.cos(az), math.cos(el) * math.sin(az), math.sin(el))) * dist
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam

    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 2.6
    sun_data.angle = math.radians(3.0)
    sun_data.color = (1.0, 0.97, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(48.0), 0.0, math.radians(34.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 10.0
    fill_data.size = 0.9
    fill_data.color = (0.72, 0.82, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-0.55, 0.35, 0.45)
    fill.rotation_euler = (math.radians(58.0), 0.0, math.radians(-125.0))
    bpy.context.scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.32, 0.42, 0.50, 1.0)
    bg.inputs[1].default_value = 0.40
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
    ap.add_argument("--seed", type=int, default=11)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    args = ap.parse_args(argv)

    reset_scene()
    rng = random.Random(args.seed)
    os.makedirs(args.out, exist_ok=True)

    if args.mode == "mesh":
        build_from_obj(args.obj)
        add_ground(0.30)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_fern.png")
    elif args.mode == "detail":
        build_fern(rng, radius=0.28, height=0.30)
        add_ground(0.30)
        setup_camera_and_light(size=0.22, elevation=32.0, azimuth=25.0)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "ostrich_fern_detail.png")
    else:
        build_fern(rng, radius=0.28, height=0.30)
        add_ground(0.30)
        setup_camera_and_light()
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "ostrich_fern.png")

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


def build_from_obj(path):
    bpy.ops.wm.obj_import(filepath=path)
    imported = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    x90 = Matrix.Rotation(math.radians(90.0), 4, 'X')
    for o in imported:
        # The engine mesh is authored at unit radius/height; the veilfern scales it to its own
        # footprint and height, and the engine's frame is Y up.
        o.matrix_world = x90 @ Matrix.Diagonal((0.28, 0.30, 0.28, 1.0))
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
    _set(bsdf, "Roughness", 0.55)
    _set(bsdf, "Subsurface Weight", 0.0)
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Color"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


if __name__ == "__main__":
    main()
