"""Small cycad reference for the Vivarium 'vaultfern'.

This is the real-life reference the vaultfern species is rebuilt against. It builds a
small, short-trunked cycad - the dwarf-zamia kind of plant, not a tall tree fern -
from scratch in Blender using the group's own anatomy:

  * a stout, BARREL-shaped caudex, armoured in the spiral rhombic leaf scars a cycad
    keeps from every frond it has ever shed, each scar a raised plate with a boss,
  * a crown of stiff, arching PINNATE fronds springing evenly from the caudex crown,
  * each frond a rachis with a spiny basal petiole and regular opposite pairs of
    ENTIRE, leathery pinnae - a cycad leaflet is never divided again the way a fern
    pinna is,
  * pinnae that overlap like shingles, each with a raised midrib, so the frond reads
    as a solid blade rather than a comb,
  * circinate vernation: the newest fronds still stand as tight coiled FIDDLEHEADS,
    the way a cycad pushes a frond up and only then lets it unfurl.

The fantasy variant keeps that anatomy and changes one thing: the crown's fronds are
laid out as the ribs of a vault, each frond springing to meet its neighbour, so the
plant becomes a small arcade of armour and stone rather than a rosette of leaves.

Usage:
    blender --background --python cycad_reference.py -- --mode reference --out DIR
    blender --background --python cycad_reference.py -- --mode fantasy   --out DIR
    blender --background --python cycad_reference.py -- --mode mesh --obj FILE --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# morphology (real proportions, metres; the species is 1.5 m tall, 1.27 m radius)
# --------------------------------------------------------------------------

CAUDEX_HEIGHT = (0.58, 0.78)
CAUDEX_RADIUS = 0.135
CAUDEX_SHOULDER = 0.78     # radius at the narrowest point of the barrel
SCAR_ROWS = 5
SCAR_CELLS = 4             # big leaf scars around any horizontal circle
SHEAR_PER_ROW = 1.53       # golden angle of 137.5 degrees, in cells

FROND_COUNT = 16
FROND_LENGTH = (0.80, 1.05)
FROND_PAIRS = 22
PINNA_LENGTH = (0.12, 0.24)
PINNA_WIDTH = 0.020
SPINE_COUNT = 7

FIDDLEHEADS = 3
FIDDLEHEAD_HEIGHT = 0.30

# The fantasy: the crown's fronds are the ribs of a vault.
VAULT_RIB_REACH = 1.02
VAULT_RIB_TILT = 0.30


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


def tube_into(acc, points, radii, sides=8, up_hint=None):
    """A tube through a polyline, parallel-transported so it does not corkscrew."""
    base = len(acc["verts"])
    n = len(points)
    tangents = []
    for i in range(n):
        if i == 0:
            d = points[1] - points[0]
        elif i == n - 1:
            d = points[-1] - points[-2]
        else:
            d = points[i + 1] - points[i - 1]
        tangents.append(d.normalized())
    hint = Vector(up_hint) if up_hint else Vector((0, 0, 1))
    side = tangents[0].cross(hint)
    if side.length < 1e-8:
        side = tangents[0].cross(Vector((1, 0, 0)))
    side.normalize()
    for i, p in enumerate(points):
        t = tangents[i]
        side = (side - t * side.dot(t))
        if side.length < 1e-8:
            side = t.cross(hint)
            if side.length < 1e-8:
                side = t.cross(Vector((1, 0, 0)))
        side.normalize()
        up = side.cross(t).normalized()
        for s in range(sides + 1):
            a = math.tau * s / sides
            r = radii[i]
            acc["verts"].append(tuple(p + side * (math.cos(a) * r) + up * (math.sin(a) * r)))
    for i in range(n - 1):
        for s in range(sides):
            a = base + i * (sides + 1) + s
            acc["faces"].append((a, a + sides + 1, a + 1))
            acc["faces"].append((a + 1, a + sides + 1, a + sides + 2))


def make_tube(name, points, radii, coll, material, sides=8):
    acc = {"verts": [], "faces": []}
    tube_into(acc, points, radii, sides)
    return new_mesh_object(name, acc["verts"], acc["faces"], None, coll, material)


def make_pinna(name, base, along, across, up, length, half_width, coll, material, rows=6):
    """One cycad pinna: an entire, leathery leaflet with a raised midrib.

    A cycad leaflet is never divided again, so this is a single blade with a keel down
    its middle and a margin that tapers to a fine point.
    """
    verts, faces, uvs = [], [], []
    for i in range(rows + 1):
        t = i / rows
        # widest just above the base, then a long taper to the point
        w = half_width * (0.62 + 0.38 * math.sin(math.pi * min(1.0, 0.16 + 0.84 * t))) * (1 - t ** 2.2)
        w = max(w, 1e-5)
        # the leaflet sweeps slightly forward and the keel deepens toward the base
        sweep = length * 0.10 * t * t
        keel = w * 0.55 * (1 - 0.4 * t)
        c = base + along * (length * t) + across * sweep
        for k, (sx, sy) in enumerate(((-1.0, 0.10), (0.0, -1.0), (1.0, 0.10))):
            q = c + across * (sx * w) + up * (sy * keel * 0.9)
            verts.append((q.x, q.y, q.z))
            uvs.append((t, 0.5 + sx * 0.5))
    for i in range(rows):
        for k in range(2):
            a = i * 3 + k
            faces.append((a, a + 3, a + 1))
            faces.append((a + 1, a + 3, a + 4))
    return new_mesh_object(name, verts, faces, uvs, coll, material)


def make_fiddlehead(name, base, coll, rng, material, sheath_material):
    """A circinate young frond: a tight logarithmic coil on a short upright stub."""
    acc = {"verts": [], "faces": []}
    height = FIDDLEHEAD_HEIGHT * rng.uniform(0.8, 1.2)
    turns = rng.uniform(2.2, 2.8)
    r0 = height * 0.17
    steps = 26
    points, radii = [], []
    for i in range(steps + 1):
        t = i / steps
        a = t * turns * math.tau
        r = r0 * math.exp(-2.1 * t)
        # the coil climbs and tightens as it goes
        y = height * (0.28 + 0.72 * t)
        points.append(Vector((base.x + math.cos(a) * r, base.y + y, base.z + math.sin(a) * r)))
        radii.append(r * 0.34 + height * 0.006)
    tube_into(acc, points, radii, 6)
    coil = new_mesh_object(name + "_coil", acc["verts"], acc["faces"], None, coll, material)

    # the pale bud sheath that wraps the base of the coil
    sv, sf = [], []
    rings, sides = 4, 10
    for r in range(rings + 1):
        phi = (math.pi * 0.5) * r / rings
        for s in range(sides + 1):
            th = math.tau * s / sides
            x = math.cos(phi) * math.cos(th) * r0 * 0.85
            z = math.cos(phi) * math.sin(th) * r0 * 0.85
            y = math.sin(phi) * height * 0.30
            sv.append((base.x + x, base.y + y, base.z + z))
    for r in range(rings):
        for s in range(sides):
            a = r * (sides + 1) + s
            sf.append((a, a + sides + 1, a + 1))
            sf.append((a + 1, a + sides + 1, a + sides + 2))
    sheath = new_mesh_object(name + "_sheath", sv, sf, None, coll, sheath_material)
    return coil, sheath


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


def caudex_material():
    mat = bpy.data.materials.new("cycad_caudex")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.72)
    # cold, hard stone-grey: the caudex is armour, not wood
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.25
    nt.links.new(attr.outputs["Color"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def frond_material():
    mat = bpy.data.materials.new("cycad_frond")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.055, 0.105, 0.082, 1.0))
    _set(bsdf, "Roughness", 0.38)
    _set(bsdf, "Subsurface Weight", 0.12)
    return mat


def sheath_material():
    mat = bpy.data.materials.new("cycad_sheath")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.80, 0.76, 0.66, 1.0))
    _set(bsdf, "Roughness", 0.52)
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


def build_caudex(coll, rng, height, radius):
    """A barrel caudex armoured in spiral rhombic leaf scars."""
    rings = 26                # vertical rings up the trunk
    rows = SCAR_ROWS          # rows of leaf scars
    cols = 24
    cells = SCAR_CELLS
    verts, faces, cols_out = [], [], []
    for i in range(rings + 1):
        v = i / rings
        y = height * v
        # a barrel: broad at the base, drawn in at the shoulder, domed at the crown
        if v < 0.18:
            r = radius * (1.06 - 0.22 * (v / 0.18))
        elif v < 0.80:
            r = radius * (0.84 + 0.14 * math.sin((v - 0.18) / 0.62 * math.pi))
        else:
            r = radius * (0.98 - 0.42 * ((v - 0.80) / 0.20) ** 1.5)
        for s in range(cols + 1):
            u = s / cols
            th = math.tau * u
            # The plates spiral: the lattice is sheared by a golden angle for every row
            # of scars, so the armour marches around the trunk instead of banding it.
            row = v * rows
            row_f = row - math.floor(row)
            sx = u * cells + row * SHEAR_PER_ROW
            f = sx - math.floor(sx)
            # Distance to the nearest cell centre: 0 at the middle of a plate, half a cell
            # in the groove between two plates.
            du = min(f, 1.0 - f)
            dv = abs(row_f - 0.5) * 2.0
            d = math.sqrt(du * du + dv * dv * 0.9) / 0.62
            pillow = max(0.0, 1.0 - d)
            relief = 0.13 * (pillow ** 0.7)
            rr = r * (1.0 + relief)
            verts.append((math.cos(th) * rr, y, math.sin(th) * rr))
            # the plate is a lighter stone, the groove between plates is dark
            shade = 0.18 + 0.82 * (min(1.0, pillow * 1.35) ** 0.7)
            cols_out.append((0.185 * shade, 0.198 * shade, 0.192 * shade))
    for i in range(rings):
        for s in range(cols):
            a = i * (cols + 1) + s
            faces.append((a, a + 1, a + cols + 1))
            faces.append((a + 1, a + cols + 2, a + cols + 1))
    obj = new_mesh_object("caudex", verts, faces, None, coll, caudex_material())
    # the plate shading rides on the vertex colours
    attr = obj.data.color_attributes.new(name="Col", type='FLOAT_COLOR', domain='POINT')
    for i, c in enumerate(cols_out):
        attr.data[i].color = (c[0], c[1], c[2], 1.0)
    return obj


def build_reference_cycad(rng, fantasy=False):
    """One small cycad, authored at true world proportions."""
    coll = bpy.context.scene.collection
    height = rng.uniform(*CAUDEX_HEIGHT)
    radius = CAUDEX_RADIUS * rng.uniform(0.92, 1.08)
    build_caudex(coll, rng, height, radius)

    crown = Vector((0.0, height * 0.99, 0.0))
    for f in range(FROND_COUNT):
        az = math.tau * f / FROND_COUNT + rng.uniform(-0.10, 0.10)
        radial = Vector((math.cos(az), 0.0, math.sin(az)))
        across = Vector((-math.sin(az), 0.0, math.cos(az)))
        length = rng.uniform(*FROND_LENGTH)
        if fantasy:
            # the vault: every rib springs to the same ring and meets its neighbour
            reach = VAULT_RIB_REACH
            lift = VAULT_RIB_TILT
        else:
            reach = length * 0.86
            lift = rng.uniform(0.55, 0.95)
        steps = 11
        path, radii = [], []
        for s in range(steps + 1):
            t = s / steps
            # the frond leaves the crown steeply and arches over to the tip
            p = crown + radial * (reach * t ** 0.85)
            p.y += length * (lift * math.sin(t * math.pi * 0.62) - 0.30 * t * t)
            path.append(p)
            radii.append(0.013 * (1.0 - 0.72 * t) + 0.0022)
        make_tube("frond_%02d" % f, path, radii, coll, frond_material(), sides=6)

        # spiny basal petiole: the prickles a cycad carries on the lower rachis
        for k in range(SPINE_COUNT):
            t = 0.05 + 0.16 * (k / max(1, SPINE_COUNT - 1))
            a = min(steps - 1, int(t * steps))
            node = path[a]
            tip = node + across * rng.uniform(-1, 1) * 0.016 - radial * 0.010
            make_tube("spine_%02d_%d" % (f, k), [node, tip], [0.0035, 0.0004], coll,
                      frond_material(), sides=4)

        # regular opposite pairs of entire pinnae
        for p in range(1, FROND_PAIRS + 1):
            t = p / (FROND_PAIRS + 1)
            a = min(steps - 1, int(t * steps))
            node = path[a].lerp(path[a + 1], t * steps - a)
            # longest about a third of the way along, tapering to both ends
            pl = rng.uniform(*PINNA_LENGTH) * math.sin(math.pi * min(1.0, 0.18 + 0.82 * t)) ** 0.6
            for sign in (-1, 1):
                d = (across * sign * 0.94 + radial * 0.30 + Vector((0, 1, 0)) * -0.10).normalized()
                make_pinna("pinna_%02d_%d_%d" % (f, p, sign), node, d, radial, Vector((0, 1, 0)),
                           pl, PINNA_WIDTH * rng.uniform(0.85, 1.15), coll, frond_material())

    # the newest fronds, still coiled
    for i in range(FIDDLEHEADS):
        az = math.tau * i / FIDDLEHEADS + rng.uniform(-0.3, 0.3)
        base = crown + Vector((math.cos(az), -0.03, math.sin(az))) * 0.02
        make_fiddlehead("fiddle_%d" % i, base, coll, rng, frond_material(), sheath_material())

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


def setup_camera_and_light(size=2.9, elevation=18.0, azimuth=36.0):
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
    sun_data.angle = math.radians(3.5)
    sun_data.color = (1.0, 0.96, 0.88)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun.rotation_euler = (math.radians(52.0), 0.0, math.radians(26.0))
    bpy.context.scene.collection.objects.link(sun)

    fill_data = bpy.data.lights.new("Fill", 'AREA')
    fill_data.energy = 14.0
    fill_data.size = 1.6
    fill_data.color = (0.80, 0.88, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.location = (-1.0, 0.7, 0.8)
    fill.rotation_euler = (math.radians(62.0), 0.0, math.radians(-122.0))
    bpy.context.scene.collection.objects.link(fill)

    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.40, 0.44, 0.50, 1.0)
    bg.inputs[1].default_value = 0.44
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
    ap.add_argument("--out", default="build/vaultfern-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=5)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=96)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--scale", default="1.27,1.5,1.27",
                    help="engine mesh frame -> metres: radius, height, radius")
    opts = ap.parse_args(args)

    reset_scene()
    rng = random.Random(opts.seed)
    os.makedirs(opts.out, exist_ok=True)

    if opts.mode == "mesh":
        if not opts.obj:
            print("MESH_MODE_NEEDS_OBJ")
            return
        scale = tuple(float(x) for x in opts.scale.split(","))
        build_from_obj(opts.obj, scale)
        add_ground(1.3)
        setup_camera_and_light(size=2.9)
    else:
        build_reference_cycad(rng, fantasy=(opts.mode == "fantasy"))
        add_ground(1.3)
        setup_camera_and_light(size=2.9)

    setup_render(res=opts.res, samples=opts.samples)
    path = os.path.join(opts.out, "cycad_%s.png" % opts.mode)
    bpy.context.scene.render.filepath = path
    if opts.no_render:
        print("BLENDER_BUILD_OK objects", len(bpy.context.scene.objects))
        return
    bpy.ops.render.render(write_still=True)
    print("BLENDER_OK", path)


if __name__ == "__main__":
    main()
