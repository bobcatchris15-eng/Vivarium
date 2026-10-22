"""Purple pitcher plant (Sarracenia purpurea) reference clump for the Vivarium 'pitcher_plant'.

This is the real-life reference the Raincup Pitcher is rebuilt against, plus the fantastical twist
the species carries in game. It builds a Sarracenia clump in Blender from the species' own anatomy
and renders it under matched light:

  * a short rhizome crown throwing a rosette of upright traps,
  * each trap narrow at the foot and flaring into a trumpet only in its top third,
  * a thin, smooth, slightly flared mouth rim that is never a filled disc,
  * the broad, erect, kidney-shaped hood standing over the mouth and leaning forward, waved along
    its free margin, notched at the front and veined with a red areolate net that encloses pale
    windows,
  * long fine hairs on the inner face of the hood and fringing its margin,
  * the darker descending inner wall with its stiff nectar-guide hairs,
  * the collapsed, torn, brown remains of last year's pitchers around the crown,
  * one tall leafless scape carrying a single nodding flower.

The fantasy variant is the in-game Raincup: the cup keeps a skin of cold rain that never quite runs
off, and the plant has grown its nectar-guide hairs and its hood fringe as crystalline dew needles
instead of plain bristles, so the whole trap is rimed with a cold light.

The same morphology is mirrored by the engine generator in
``src/Vivarium.Sim/Geometry/OrganismMeshes.PitcherPlant.cs``.

Usage:
    blender --background --python pitcher_reference.py -- --mode reference --out DIR
    blender --background --python pitcher_reference.py -- --mode fantasy   --out DIR
    blender --background --python pitcher_reference.py -- --mode trap      --out DIR
    blender --background --python pitcher_reference.py -- --mode mesh --obj FILE --out DIR
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

TRAP_HEIGHT = 0.21      # metres, base to mouth
MOUTH_RADIUS = 0.030    # metres
HOOD_HEIGHT = 0.062     # metres the hood stands above the rim
VEINS = 9


def _smoothstep(a, b, x):
    if b <= a:
        return 0.0 if x < a else 1.0
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3.0 - 2.0 * t)


def _wrap(a):
    while a > math.pi:
        a -= math.tau
    while a < -math.pi:
        a += math.tau
    return a


def trap_radius(t):
    """The trumpet profile: a narrow foot opening into a wide mouth."""
    return MOUTH_RADIUS * (0.19 + 0.32 * math.sin(math.pi * t * 0.72) + 0.56 * t ** 5)


def hood_height(phi):
    return HOOD_HEIGHT * (0.70 + 0.30 * math.cos(phi))


def hood_margin_scale(phi):
    notch = 1.0 - 0.34 * math.exp(-((_wrap(phi - math.pi)) / 0.55) ** 2)
    wave = 1.0 + 0.030 * math.sin(4.0 * phi)
    return (0.62 - 0.10 * math.cos(phi)) * notch * wave


def areolate_net(phi, w, phase):
    """The red vein lattice of the hood and the pale windows it encloses."""
    u, v = phi / math.tau, w
    g1 = abs(math.sin(math.pi * (u * 3.5 + v * 1.1 + phase)))
    g2 = abs(math.sin(math.pi * (u * 1.3 - v * 2.4 + phase * 1.7)))
    g3 = abs(math.sin(math.pi * (u * 7.0 + v * 4.1)))
    major = 1.0 - _smoothstep(0.0, 0.48, min(g1, g2))
    minor = (1.0 - _smoothstep(0.0, 0.30, g3)) * 0.35
    return max(major, minor)


# --------------------------------------------------------------------------
# scene helpers
# --------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_mesh_object(name, verts, faces, coll, smooth=False):
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


def trap_material(fantasy=False):
    """A Sarracenia trap: waxy, deep wine red, or yellow green with red veins."""
    mat = bpy.data.materials.new("SarraceniaTrap")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.33, 0.07, 0.09, 1.0))
    else:
        _set(bsdf, "Base Color", (0.30, 0.06, 0.08, 1.0))
    _set(bsdf, "Roughness", 0.42)
    _set(bsdf, "Specular IOR Level", 0.38)
    return mat


def hood_material(fantasy=False):
    mat = bpy.data.materials.new("SarraceniaHood")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.40, 0.16, 0.10, 1.0))
    else:
        _set(bsdf, "Base Color", (0.38, 0.14, 0.09, 1.0))
    _set(bsdf, "Roughness", 0.46)
    _set(bsdf, "Specular IOR Level", 0.36)
    return mat


def window_material():
    mat = bpy.data.materials.new("SarraceniaWindow")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.62, 0.66, 0.34, 1.0))
    _set(bsdf, "Roughness", 0.50)
    return mat


def dew_material(fantasy=False):
    mat = bpy.data.materials.new("DewNeedle")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.72, 0.84, 0.92, 1.0))
        _set(bsdf, "Roughness", 0.10)
        _set(bsdf, "Transmission Weight", 0.55)
    else:
        _set(bsdf, "Base Color", (0.62, 0.58, 0.40, 1.0))
        _set(bsdf, "Roughness", 0.55)
    return mat


def throat_material():
    mat = bpy.data.materials.new("SarraceniaThroat")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.22, 0.05, 0.07, 1.0))
    _set(bsdf, "Roughness", 0.30)
    return mat


def rain_material(fantasy=False):
    mat = bpy.data.materials.new("RaincupWater")
    bsdf = _principled(mat)
    if fantasy:
        _set(bsdf, "Base Color", (0.03, 0.05, 0.07, 1.0))
        _set(bsdf, "Roughness", 0.03)
        _set(bsdf, "Emission Color", (0.30, 0.48, 0.62, 1.0))
        _set(bsdf, "Emission Strength", 0.55)
    else:
        _set(bsdf, "Base Color", (0.04, 0.07, 0.05, 1.0))
        _set(bsdf, "Roughness", 0.06)
    return mat


def old_material():
    mat = bpy.data.materials.new("SarraceniaOld")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.24, 0.15, 0.08, 1.0))
    _set(bsdf, "Roughness", 0.80)
    return mat


def crown_material():
    mat = bpy.data.materials.new("SarraceniaCrown")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.16, 0.12, 0.07, 1.0))
    _set(bsdf, "Roughness", 0.75)
    return mat


def flower_material():
    mat = bpy.data.materials.new("SarraceniaFlower")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.42, 0.07, 0.10, 1.0))
    _set(bsdf, "Roughness", 0.45)
    return mat


def scape_material():
    mat = bpy.data.materials.new("SarraceniaScape")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.24, 0.34, 0.10, 1.0))
    _set(bsdf, "Roughness", 0.55)
    return mat


def sphagnum_material():
    mat = bpy.data.materials.new("Sphagnum")
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.16, 0.20, 0.07, 1.0))
    _set(bsdf, "Roughness", 0.92)
    return mat


# --------------------------------------------------------------------------
# the plant
# --------------------------------------------------------------------------

def make_trap(name, base, axis, fwd, side, coll, rng, fantasy=False, green=False):
    """One Sarracenia trap."""
    height = TRAP_HEIGHT * rng.uniform(0.80, 1.20)
    mouth = MOUTH_RADIUS * rng.uniform(0.80, 1.25)
    hood_h = mouth * rng.uniform(2.0, 3.0)
    phase = rng.uniform(0.0, math.tau)

    def centre(t):
        return base + axis * (height * t) + fwd * (height * 0.09 * t * t)

    def surface(t, phi, offset=0.0):
        r = trap_radius(t) * (mouth / MOUTH_RADIUS) + offset
        d = fwd * math.cos(phi) + side * math.sin(phi)
        return centre(t) + d * r

    def rim_point(phi):
        d = fwd * math.cos(phi) + side * math.sin(phi)
        return centre(1.0) + d * (mouth * trap_radius(1.0) / MOUTH_RADIUS)

    def margin_point(phi):
        d = fwd * math.cos(phi) + side * math.sin(phi)
        h = hood_h * (0.70 + 0.30 * math.cos(phi))
        return (centre(1.0) + d * (mouth * hood_margin_scale(phi))
                + axis * h - fwd * (h * 0.48))

    def hood_point(phi, w):
        arch = math.sin(math.pi * w)
        arch_dir = (axis * 0.88 - fwd * 0.48).normalized()
        return (rim_point(phi).lerp(margin_point(phi), w)
                + arch_dir * (hood_h * 0.24 * arch))

    # Outer wall.
    rings, around = 16, 56
    verts, faces = [], []
    for i in range(rings + 1):
        t = i / rings
        for j in range(around + 1):
            phi = math.tau * j / around
            verts.append(surface(t, phi))
    for i in range(rings):
        for j in range(around):
            a = i * (around + 1) + j
            b = a + around + 1
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    outer = new_mesh_object(name + "Wall", verts, faces, coll, smooth=True)
    outer.data.materials.append(trap_material(fantasy))

    # Inner wall, darker, narrowing into the throat.
    verts, faces = [], []
    inner_rings = 10
    for i in range(inner_rings + 1):
        t = 1.0 - (i / inner_rings) * 0.87
        for j in range(around + 1):
            phi = math.tau * j / around
            verts.append(surface(t, phi, -0.0011))
    for i in range(inner_rings):
        for j in range(around):
            a = i * (around + 1) + j
            b = a + around + 1
            faces.append((a, b + 1, b))
            faces.append((a, a + 1, b + 1))
    inner = new_mesh_object(name + "Throat", verts, faces, coll, smooth=True)
    inner.data.materials.append(throat_material())

    # The rolled lip: a smooth half-round rim, never a filled disc.
    verts, faces = [], []
    lip_q = 8
    for j in range(around + 1):
        phi = math.tau * j / around
        d = fwd * math.cos(phi) + side * math.sin(phi)
        lip_centre = centre(1.0) + d * (mouth * trap_radius(1.0) / MOUTH_RADIUS - 0.00055)
        for q in range(lip_q + 1):
            theta = math.pi * q / lip_q
            verts.append(lip_centre + d * (0.0016 * math.cos(theta)) + axis * (0.0016 * math.sin(theta)))
    for j in range(around):
        for q in range(lip_q):
            a = j * (lip_q + 1) + q
            b = a + lip_q + 1
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    lip = new_mesh_object(name + "Lip", verts, faces, coll, smooth=True)
    lip.data.materials.append(hood_material(fantasy))

    # The hood, with its areolate net painted on as a second material.
    verts, faces, net_faces = [], [], []
    cols = 9
    for i in range(around + 1):
        phi = math.tau * i / around
        for j in range(cols + 1):
            w = j / cols
            verts.append(hood_point(phi, w))
    for i in range(around):
        for j in range(cols):
            a = i * (cols + 1) + j
            b = a + cols + 1
            t_mid = (i + 0.5) / around
            w_mid = (j + 0.5) / cols
            if areolate_net(math.tau * t_mid, w_mid, phase) > 0.45:
                net_faces.append((a, b, b + 1))
                net_faces.append((a, b + 1, a + 1))
            else:
                faces.append((a, b, b + 1))
                faces.append((a, b + 1, a + 1))
    hood = new_mesh_object(name + "Hood", verts, faces, coll, smooth=True)
    hood.data.materials.append(hood_material(fantasy))
    if net_faces:
        nets = new_mesh_object(name + "HoodNet", verts, net_faces, coll, smooth=True)
        nets.data.materials.append(window_material())

    # The crystalline fringe: long fine needles on the hood's inner face and along its margin.
    verts, faces = [], []
    for h in range(120):
        phi = rng.uniform(0.0, math.tau)
        w = rng.uniform(0.86, 1.0) if rng.random() < 0.45 else rng.uniform(0.10, 0.88)
        root = hood_point(phi, w)
        radial = (root - centre(1.0))
        radial = (radial - axis * radial.dot(axis)).normalized()
        length = hood_h * rng.uniform(0.06, 0.16)
        mid = root + radial * (length * 0.55) + axis * (length * 0.12)
        tip = root + radial * length + axis * (length * 0.04)
        start = len(verts)
        for pp, rr in ((root, 0.00018), (mid, 0.00009), (tip, 0.00002)):
            tang = (tip - root).normalized()
            sd = tang.cross(Vector((0.0, 0.0, 1.0)))
            if sd.length < 1e-6:
                sd = tang.cross(Vector((1.0, 0.0, 0.0)))
            sd.normalize()
            u2 = sd.cross(tang).normalized()
            for s in range(5):
                th = math.tau * s / 4
                verts.append(pp + (sd * math.cos(th) + u2 * math.sin(th)) * rr)
        for i in range(2):
            for s in range(4):
                a0 = start + i * 5 + s
                b0 = start + (i + 1) * 5 + s
                faces.append((a0, b0, b0 + 1))
                faces.append((a0, b0 + 1, a0 + 1))
    fringe = new_mesh_object(name + "DewFringe", verts, faces, coll, smooth=True)
    fringe.data.materials.append(dew_material(fantasy))

    # The rain, lying deep in the cup.
    t = 0.30
    r = trap_radius(t) * (mouth / MOUTH_RADIUS) * 0.90
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1.0,
                                        location=centre(t))
    rain = bpy.context.object
    rain.name = name + "Rain"
    rain.scale = (r, r * 0.90, r * 0.14)
    rain.data.materials.append(rain_material(fantasy))

    return hood


def make_old_trap(name, base, axis, fwd, side, coll, rng):
    """Last year's trap: collapsed, torn and brown."""
    height = TRAP_HEIGHT * rng.uniform(0.5, 0.8)
    radius = MOUTH_RADIUS * rng.uniform(0.6, 0.9)
    rings, around = 8, 20
    verts, faces = [], []
    ragged = [rng.uniform(0.5, 1.0) for _ in range(around + 1)]
    for i in range(rings + 1):
        t = i / rings
        for j in range(around + 1):
            phi = math.tau * j / around
            d = fwd * math.cos(phi) + side * math.sin(phi)
            squash = 0.28 + 0.26 * math.sin(math.pi * t)
            r = radius * (0.22 + 0.78 * math.sin(math.pi * 0.55 * t + 0.35)) * ragged[j]
            p = base + axis * (height * t) + fwd * (height * 0.12 * t * t) + d * (r * squash)
            verts.append(p)
    for i in range(rings):
        for j in range(around):
            a = i * (around + 1) + j
            b = a + around + 1
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    obj = new_mesh_object(name, verts, faces, coll, smooth=True)
    obj.data.materials.append(old_material())
    return obj


def make_flower(name, base, height, coll):
    """The scape and its single nodding flower."""
    steps = 10
    verts, faces = [], []
    for i in range(steps + 1):
        t = i / steps
        p = base + Vector((0.030 * math.sin(t * 1.5), 0.018 * t * t, height * t))
        r = 0.0034 * (1.0 - 0.42 * t)
        tang = Vector((0.030 * 1.5 * math.cos(t * 1.5), 0.036 * t, height))
        tang.normalize()
        sd = tang.cross(Vector((0.0, 0.0, 1.0)))
        if sd.length < 1e-6:
            sd = tang.cross(Vector((1.0, 0.0, 0.0)))
        sd.normalize()
        u2 = sd.cross(tang).normalized()
        for s in range(9):
            th = math.tau * s / 8
            verts.append(p + (sd * math.cos(th) + u2 * math.sin(th)) * r)
    for i in range(steps):
        for s in range(8):
            a = i * 9 + s
            b = a + 9
            faces.append((a, b, b + 1))
            faces.append((a, b + 1, a + 1))
    scape = new_mesh_object(name + "Scape", verts, faces, coll, smooth=True)
    scape.data.materials.append(scape_material())

    top = base + Vector((0.030 * math.sin(1.5), 0.018, height))
    # The nodding flower: a domed style over five broad spreading sepals and five hanging petals.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=1.0,
                                        location=top + Vector((0.0, 0.0, 0.004)))
    style = bpy.context.object
    style.name = name + "Style"
    style.scale = (0.011, 0.011, 0.009)
    style.data.materials.append(flower_material())

    for k in range(5):
        a = k * math.tau / 5 + 0.3
        d = Vector((math.cos(a), 0.0, math.sin(a)))
        sd = Vector((-d.z, 0.0, d.x))
        verts, faces = [], []
        for i in range(7):
            t = i / 6
            w = (t - 0.5) * 2
            p = (top + d * (0.046 * t) + Vector((0.0, 0.0, 0.007 * math.sin(math.pi * t) - 0.006 * t))
                 + sd * (0.030 * w * math.sin(math.pi * t * 0.9 + 0.35)))
            verts.append(p)
        for i in range(6):
            a0 = i
            b0 = i + 1
            faces.append((a0, b0, b0 + 1))
            faces.append((a0, b0 + 1, a0 + 1))
        petal = new_mesh_object(name + "Sepal_%d" % k, verts, faces, coll, smooth=True)
        petal.data.materials.append(flower_material())

        verts, faces = [], []
        for i in range(7):
            t = i / 6
            w = (t - 0.5) * 2
            p = (top + d * (0.026 * t) - Vector((0.0, 0.0, 0.040 * t))
                 + sd * (0.024 * w * math.sin(math.pi * t * 0.85 + 0.4)))
            verts.append(p)
        for i in range(6):
            a0 = i
            b0 = i + 1
            faces.append((a0, b0, b0 + 1))
            faces.append((a0, b0 + 1, a0 + 1))
        petal = new_mesh_object(name + "Petal_%d" % k, verts, faces, coll, smooth=True)
        petal.data.materials.append(flower_material())


def build_reference_clump(rng, fantasy=False, traps=7):
    coll = bpy.context.scene.collection

    # The rhizome crown.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=1.0,
                                        location=(0.0, 0.0, 0.012))
    crown = bpy.context.object
    crown.name = "Crown"
    crown.scale = (0.040, 0.040, 0.016)
    crown.data.materials.append(crown_material())

    for k in range(traps):
        az = math.tau * k / traps + rng.uniform(-0.20, 0.20)
        outward = Vector((math.cos(az), 0.0, math.sin(az)))
        lean = rng.uniform(0.10, 0.30)
        axis = (Vector((0.0, 0.0, 1.0)) * math.cos(lean) + outward * math.sin(lean)).normalized()
        fwd = (outward - axis * outward.dot(axis)).normalized()
        side = axis.cross(fwd).normalized()
        base = outward * rng.uniform(0.012, 0.055) + Vector((0.0, 0.0, 0.014))
        make_trap("trap_%d" % k, base, axis, fwd, side, coll, rng, fantasy=fantasy,
                  green=(rng.random() < 0.4))

    # Last year's remains.
    for k in range(3):
        az = rng.uniform(0.0, math.tau)
        outward = Vector((math.cos(az), 0.0, math.sin(az)))
        lean = rng.uniform(0.55, 1.05)
        axis = (Vector((0.0, 0.0, 1.0)) * math.cos(lean) + outward * math.sin(lean)).normalized()
        fwd = (outward - axis * outward.dot(axis)).normalized()
        side = axis.cross(fwd).normalized()
        make_old_trap("old_%d" % k, outward * rng.uniform(0.02, 0.07) + Vector((0.0, 0.0, 0.010)),
                      axis, fwd, side, coll, rng)

    make_flower("flower", Vector((-0.02, 0.01, 0.010)), 0.38, coll)


def build_trap_detail(rng, fantasy=False):
    """A single trap, large in frame, to study the hood and the throat."""
    coll = bpy.context.scene.collection
    axis = Vector((0.0, 0.0, 1.0))
    fwd = Vector((1.0, 0.0, 0.0))
    side = axis.cross(fwd).normalized()
    make_trap("detail", Vector((0.0, 0.0, 0.0)), axis, fwd, side, coll, rng, fantasy=fantasy)


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
    ground.data.materials.append(sphagnum_material())


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
    ap.add_argument("--mode", default="reference", choices=["reference", "fantasy", "mesh", "trap"])
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
        build_from_obj(args.obj, (0.34, 0.58, 0.34))
        add_ground(0.297)
        setup_camera_and_light(elevation=args.elevation)
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "engine_mesh.png")
    elif args.mode == "trap":
        build_trap_detail(rng, fantasy=(args.seed % 2 == 1))
        setup_camera_and_light(size=0.34, elevation=40.0, azimuth=35.0, target=(0.0, 0.0, 0.11))
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "sarracenia_trap_detail.png")
    else:
        build_reference_clump(rng, fantasy=(args.mode == "fantasy"))
        add_ground(0.297)
        setup_camera_and_light(size=0.66, elevation=22.0, azimuth=45.0, target=(0.0, 0.0, 0.14))
        setup_render(args.res, args.samples)
        out = os.path.join(args.out, "sarracenia_%s.png" % args.mode)

    bpy.context.scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("RENDERED", out)


if __name__ == "__main__":
    main()
