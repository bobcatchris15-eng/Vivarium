"""Realistic Water Monitor (Varanus salvator) Guardian Authoring - Revision 2.

Incorporates targeted feedback:
1. Shield-shaped, overlapping (imbricate) scales with central keels and multi-layer tiling.
2. Thicker side-to-side tail base (caudofemoralis mass) seamlessly blended with pelvis and torso dorsum/ventrum.
3. Cranial overhaul:
   - Pronounced occipital bone / nuchal crest at posterior cranial margin.
   - Distinct trapezoidal jawline widening from snout to quadrate/temporal joints.
   - Blunt, roughly square boxy snout/muzzle with broad nostrils.
   - Broad 'cheekbones' (suborbital jugal arches) and robust trapezoidal mandible.
4. Claws corrected:
   - Fixed orientation: arcs forward along digit direction and curves downward into substrate.
   - Sharp dorsal keratin keel on top, lateral compression, teardrop cross-section.
5. Bulky, muscular limbs:
   - Thick, sprawling varanid musculature (triceps, biceps, gastrocnemius, caudofemoral masses).
   - Anatomical oval/elliptical cross-sections with defined muscle bellies and robust condyles.
6. Preserves exact 40-bone skeletal armature, hierarchy, and 4 animation cycles (idle, walk, feed, rest).
"""

import bpy, math, json, struct, random, os, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
import numpy as np

TAU = math.tau
ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = Path(__file__).resolve().parent / "models"
RENDER_DIR = Path(__file__).resolve().parent / "renders"
OUT_DIR.mkdir(parents=True, exist_ok=True)
RENDER_DIR.mkdir(parents=True, exist_ok=True)

def active(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

class Builder:
    def __init__(self, name, rig, mat):
        self.name = name
        self.rig = rig
        self.mat = mat
        self.v = []
        self.f = []
        self.uv = []
        self.w = []
        self.col = []

    def ringmesh(self, nodes, radial=32, color=(1,1,1,1), uvscale=1):
        start = len(self.v)
        n = len(nodes)
        for i, (c, rx, ry, weights) in enumerate(nodes):
            c = Vector(c)
            d = Vector(nodes[min(i+1, n-1)][0]) - Vector(nodes[max(i-1, 0)][0])
            if d.length < 1e-6:
                d = Vector((0, 0, -1))
            else:
                d.normalize()
            ref = Vector((0, 1, 0)) if abs(d.y) < 0.92 else Vector((0, 0, 1))
            a = d.cross(ref).normalized()
            b = a.cross(d).normalized()
            for j in range(radial + 1):
                q = TAU * j / radial
                rad_x = rx(q) if callable(rx) else rx
                rad_y = ry(q) if callable(ry) else ry
                pos = c + a * (math.sin(q) * rad_x) + b * (math.cos(q) * rad_y)
                self.v.append(tuple(pos))
                self.uv.append((j / radial, i / max(1, n - 1) * uvscale))
                self.w.append(weights)
                self.col.append(color(c, q, i) if callable(color) else color)

        for i in range(n - 1):
            for j in range(radial):
                k = start + i * (radial + 1) + j
                self.f.append((k, k + radial + 1, k + radial + 2, k + 1))

        # End caps
        for i, reverse in [(0, True), (n - 1, False)]:
            k = len(self.v)
            c = nodes[i][0]
            self.v.append(c)
            self.uv.append((0.5, i / max(1, n - 1) * uvscale))
            self.w.append(nodes[i][3])
            self.col.append(color(Vector(c), 0, i) if callable(color) else color)
            r = start + i * (radial + 1)
            for j in range(radial):
                self.f.append((r + j + 1, r + j, k) if reverse else (r + j, r + j + 1, k))

    def ellipsoid(self, c, r, weights, color=(1,1,1,1), segments=24):
        nodes = []
        for i in range(17):
            t = -math.pi / 2 + math.pi * i / 16
            nodes.append((
                (c[0], c[1], c[2] + r[2] * math.sin(t)),
                max(0.0003, r[0] * math.cos(t)),
                max(0.0003, r[1] * math.cos(t)),
                weights
            ))
        self.ringmesh(nodes, segments, color)

    def build(self):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata(self.v, [], self.f)
        me.update()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.collection.objects.link(ob)
        me.materials.append(self.mat)
        uv = me.uv_layers.new(name='SurfaceUV')
        for p in me.polygons:
            p.use_smooth = True
            for li in p.loop_indices:
                uv.data[li].uv = self.uv[me.loops[li].vertex_index]
        col = me.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')
        for i, c in enumerate(self.col):
            col.data[i].color = c
        for name in self.rig.data.bones.keys():
            ob.vertex_groups.new(name=name)
        for i, ws in enumerate(self.w):
            for name, weight in ws.items():
                if weight > 0:
                    ob.vertex_groups[name].add([i], weight, 'REPLACE')
        mod = ob.modifiers.new('AnatomicalSkin', 'ARMATURE')
        mod.object = self.rig
        ob.parent = self.rig
        active(ob)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        return ob

def material(name, color, rough=0.7, texture=None, normal=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = rough
    if texture:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = texture
        col = m.node_tree.nodes.new('ShaderNodeVertexColor')
        col.layer_name = 'Color'
        mul = m.node_tree.nodes.new('ShaderNodeMixRGB')
        mul.blend_type = 'MULTIPLY'
        mul.inputs[0].default_value = 1
        m.node_tree.links.new(tex.outputs['Color'], mul.inputs[1])
        m.node_tree.links.new(col.outputs['Color'], mul.inputs[2])
        m.node_tree.links.new(mul.outputs[0], p.inputs['Base Color'])
    if normal:
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = normal
        nm = m.node_tree.nodes.new('ShaderNodeNormalMap')
        nm.inputs['Strength'].default_value = 0.95
        m.node_tree.links.new(tex.outputs['Color'], nm.inputs['Color'])
        m.node_tree.links.new(nm.outputs[0], p.inputs['Normal'])
    return m

def generate_reptile_pbr_textures(out_dir):
    """Procedurally generates high-resolution 2048x2048 anatomical PBR textures.
    Features:
    - Shield-shaped, overlapping (imbricate) scales with raised central keels.
    - Staggered multi-layer elevation where posterior scale apices overlap anterior margins.
    - High-contrast tangent-space normal map and semi-glossy keratin roughness map.
    - Varanus dark charcoal ground with golden-amber highlights on scale keels and margins.
    """
    size = 2048
    n_u = 110
    n_v = 150
    u = np.linspace(0, 1, size, endpoint=False)
    v = np.linspace(0, 1, size, endpoint=False)
    U, V = np.meshgrid(u, v)

    v_scaled = V * n_v
    row_fl = np.floor(v_scaled)
    v_cell = v_scaled - row_fl

    def single_scale_height(dx, dy):
        # Shield width profile: broad at anterior, tapering to pointed apex at dy = +0.55
        w = np.where(
            dy < 0.0,
            0.46 * (1.0 - 0.15 * (np.abs(dy) / 0.35)**2),
            0.46 * np.maximum(0.0, 1.0 - (np.maximum(0.0, dy) / 0.55)**1.4)
        )
        d_lat = np.maximum(0.0, 1.0 - np.abs(dx) / np.maximum(1e-4, w))
        in_shield = (np.abs(dx) < w) & (dy >= -0.35) & (dy <= 0.55)
        ramp = 0.25 + 0.75 * np.clip((dy + 0.35) / 0.85, 0.0, 1.0)**0.9
        edge_bevel = d_lat ** 0.5
        keel_w = 0.08
        keel = 0.38 * np.maximum(0.0, 1.0 - np.abs(dx) / keel_w)**1.5 * ramp
        h = np.where(in_shield, (edge_bevel * 0.45 + ramp * 0.55) + keel, 0.0)
        return h

    u_curr = U * n_u + (row_fl % 2) * 0.5
    col_curr = np.floor(u_curr)
    dx_curr = (u_curr - col_curr) - 0.5
    dy_curr = v_cell - 0.40
    h_curr = single_scale_height(dx_curr, dy_curr)

    u_prev = U * n_u + ((row_fl - 1) % 2) * 0.5
    col_prev = np.floor(u_prev)
    dx_prev = (u_prev - col_prev) - 0.5
    dy_prev = (v_cell + 1.0) - 0.40
    h_prev = single_scale_height(dx_prev, dy_prev)

    u_next = U * n_u + ((row_fl + 1) % 2) * 0.5
    col_next = np.floor(u_next)
    dx_next = (u_next - col_next) - 0.5
    dy_next = (v_cell - 1.0) - 0.40
    h_next = single_scale_height(dx_next, dy_next)

    # Imbrication: maximum height of overlapping scale shields
    h_total = np.maximum(h_curr, np.maximum(h_prev, h_next))

    # Micro-keel keratin striations
    micro_keel = 0.035 * np.sin(U * n_u * 32.0) * (h_total > 0.1)
    h_total = np.clip(h_total + micro_keel, 0.0, 1.6)
    h_total /= np.max(h_total)

    # Normal map from height gradient
    gy, gx = np.gradient(h_total * 4.2)
    n = np.stack((-gx * 2.4, -gy * 2.4, np.ones_like(h_total)), axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    normal_rgb = n * 0.5 + 0.5

    # Albedo texture
    base_val = 0.55 + 0.32 * h_total + 0.13 * (h_prev > h_curr)
    albedo_rgb = np.stack((base_val * 0.86, base_val * 0.88, base_val * 0.74), axis=-1)

    # Roughness
    roughness = 0.36 + 0.38 * (1.0 - h_total)
    roughness_rgb = np.stack((roughness, roughness, roughness), axis=-1)

    def save_image(name, data, noncolor=False):
        im = bpy.data.images.new(name, width=size, height=size)
        if noncolor:
            im.colorspace_settings.name = 'Non-Color'
        rgba = np.concatenate((data, np.ones((size, size, 1))), axis=-1).astype(np.float32)
        im.pixels.foreach_set(rgba.ravel())
        path = str(out_dir / f"{name}.png")
        im.filepath_raw = path
        im.file_format = 'PNG'
        im.save()
        im.pack()
        return im

    col_img = save_image('surface_albedo', albedo_rgb)
    norm_img = save_image('surface_normal', normal_rgb, noncolor=True)
    rough_img = save_image('surface_roughness', roughness_rgb, noncolor=True)
    return col_img, norm_img, rough_img

def body_weight(z):
    centers = [(-1.02, 'Head'), (-0.70, 'Neck'), (-0.35, 'Chest'), (0.08, 'Spine1'), (0.42, 'Pelvis')]
    if z <= centers[0][0]:
        return {centers[0][1]: 1.0}
    if z >= centers[-1][0]:
        return {centers[-1][1]: 1.0}
    for (a, an), (b, bn) in zip(centers, centers[1:]):
        if a <= z <= b:
            t = (z - a) / (b - a)
            t = t * t * (3 - 2 * t)
            return {an: 1.0 - t, bn: t}

def create_rigging():
    ar = bpy.data.armatures.new('monitor_skeleton')
    ob = bpy.data.objects.new('monitor_rig', ar)
    bpy.context.collection.objects.link(ob)
    active(ob)
    bpy.ops.object.mode_set(mode='EDIT')

    def bone(name, h, t, parent):
        b = ar.edit_bones.new(name)
        b.head = h
        b.tail = t
        if parent:
            b.parent = ar.edit_bones[parent]
        return b

    bone('Root', (0, 0, 0), (0, 0, -0.15), None)
    chain = [
        ('Pelvis', (0, 0.25, 0.42), (0, 0.26, 0.15), 'Root'),
        ('Spine1', (0, 0.26, 0.15), (0, 0.27, -0.20), 'Pelvis'),
        ('Chest', (0, 0.27, -0.20), (0, 0.29, -0.58), 'Spine1'),
        ('Neck', (0, 0.29, -0.58), (0, 0.30, -0.84), 'Chest'),
        ('Head', (0, 0.30, -0.84), (0, 0.28, -1.22), 'Neck'),
        ('Jaw', (0, 0.275, -0.90), (0, 0.235, -1.24), 'Head')
    ]
    for name, h, t, parent in chain:
        bone(name, h, t, parent)

    # Updated Blink bone positions matching broadened skull cheekbones
    for side, suffix in [(-1, 'R'), (1, 'L')]:
        for name in ['Blink_' + suffix, 'BlinkLower_' + suffix]:
            bone(name, (side * 0.118, 0.326, -1.006), (side * 0.118, 0.306, -1.006), 'Head')
    bone('Tongue', (0, 0.269, -1.235), (0, 0.269, -1.355), 'Head')

    tail_points = [
        (0.0, 0.245, 0.49), (0.015, 0.246, 0.58), (0.035, 0.245, 0.70), (0.058, 0.241, 0.825),
        (0.080, 0.235, 0.96), (0.101, 0.228, 1.102), (0.110, 0.220, 1.25), (0.096, 0.211, 1.403),
        (0.065, 0.200, 1.56), (0.017, 0.186, 1.721), (-0.035, 0.170, 1.88), (-0.078, 0.151, 2.034),
        (-0.110, 0.130, 2.18), (-0.127, 0.107, 2.316), (-0.130, 0.085, 2.44), (-0.111, 0.067, 2.561),
        (-0.090, 0.055, 2.65)
    ]
    for i in range(len(tail_points) - 1):
        bone('Tail' + str(i + 1), tail_points[i], tail_points[i + 1], 'Pelvis' if i == 0 else 'Tail' + str(i))

    for side, s in [('L', 1), ('R', -1)]:
        # Forelimbs
        bone('UpperArm_' + side, (s * 0.145, 0.26, -0.46), (s * 0.30, 0.22, -0.36), 'Chest')
        bone('Forearm_' + side, (s * 0.30, 0.22, -0.36), (s * 0.39, 0.05, -0.57), 'UpperArm_' + side)
        bone('Hand_' + side, (s * 0.39, 0.05, -0.57), (s * 0.39, 0.05, -0.66), 'Forearm_' + side)
        # Hindlimbs
        bone('Thigh_' + side, (s * 0.145, 0.245, 0.39), (s * 0.32, 0.205, 0.29), 'Pelvis')
        bone('Shin_' + side, (s * 0.32, 0.205, 0.29), (s * 0.40, 0.05, 0.49), 'Thigh_' + side)
        bone('Foot_' + side, (s * 0.40, 0.05, 0.49), (s * 0.40, 0.05, 0.40), 'Shin_' + side)

    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in ob.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return ob, tail_points

def build_lifelike_anatomy(rig, mats):
    skin = Builder('monitor_body', rig, mats['body'])

    # 1. ANATOMICAL CROSS-SECTIONS ALONG BODY AXIS
    # Upgraded with blunt square muzzle, trapezoidal skull, pronounced occiput, and smooth tail base
    profile_data = [
        # (z, center_y, rx, ry, section_type)
        (-1.25, 0.270, 0.060, 0.038, 'blunt_snout_apex'),
        (-1.21, 0.274, 0.074, 0.046, 'rostrum_muzzle'),
        (-1.13, 0.282, 0.098, 0.056, 'canthal_ridge'),
        (-1.04, 0.295, 0.126, 0.068, 'suborbital_cheek'),
        (-0.95, 0.306, 0.146, 0.078, 'cheekbone_orbit'),
        (-0.88, 0.308, 0.144, 0.086, 'temporal_adductor'),
        (-0.83, 0.306, 0.135, 0.098, 'occipital_crest'),
        (-0.74, 0.290, 0.122, 0.096, 'nuchal_neck_transition'),
        (-0.62, 0.288, 0.128, 0.104, 'cervical_column'),
        (-0.49, 0.282, 0.185, 0.150, 'pectoral_shoulder'),
        (-0.32, 0.282, 0.230, 0.162, 'anterior_thorax'),
        (-0.12, 0.268, 0.232, 0.156, 'mid_torso_flank'),
        ( 0.10, 0.258, 0.208, 0.138, 'posterior_torso'),
        ( 0.28, 0.264, 0.215, 0.148, 'lumbar_flank'),
        ( 0.42, 0.256, 0.190, 0.138, 'pelvic_girdle'),
        ( 0.49, 0.245, 0.155, 0.130, 'caudofemoral_base')
    ]

    body_nodes = []
    for i in range(len(profile_data) - 1):
        p0 = profile_data[max(0, i - 1)]
        p1 = profile_data[i]
        p2 = profile_data[i + 1]
        p3 = profile_data[min(len(profile_data) - 1, i + 2)]
        steps = 5
        for s in range(steps):
            t = s / steps
            t2 = t * t
            t3 = t2 * t
            z  = 0.5 * ((2*p1[0]) + (-p0[0] + p2[0])*t + (2*p0[0] - 5*p1[0] + 4*p2[0] - p3[0])*t2 + (-p0[0] + 3*p1[0] - 3*p2[0] + p3[0])*t3)
            cy = 0.5 * ((2*p1[1]) + (-p0[1] + p2[1])*t + (2*p0[1] - 5*p1[1] + 4*p2[1] - p3[1])*t2 + (-p0[1] + 3*p1[1] - 3*p2[1] + p3[1])*t3)
            rx = 0.5 * ((2*p1[2]) + (-p0[2] + p2[2])*t + (2*p0[2] - 5*p1[2] + 4*p2[2] - p3[2])*t2 + (-p0[2] + 3*p1[2] - 3*p2[2] + p3[2])*t3)
            ry = 0.5 * ((2*p1[3]) + (-p0[3] + p2[3])*t + (2*p0[3] - 5*p1[3] + 4*p2[3] - p3[3])*t2 + (-p0[3] + 3*p1[3] - 3*p2[3] + p3[3])*t3)

            def shaped_rx(angle, r=rx, cur_z=z):
                # 1. Blunt square snout superellipse
                if cur_z < -1.16:
                    box_factor = (math.sin(angle)**4 + math.cos(angle)**4)**(-0.20)
                    return r * box_factor
                # 2. Broad cheekbones (jugal arch)
                if -1.06 < cur_z < -0.88:
                    cheek = 0.16 * math.exp(-((cur_z + 0.96) / 0.08)**2) * (abs(math.sin(angle))**1.2)
                    return r * (1.0 + cheek)
                # 3. Lateral torso skin folds
                if -0.45 < cur_z < 0.35:
                    flank = 0.08 * math.exp(-((abs(math.sin(angle)) - 1.0) / 0.3)**2)
                    return r * (1.0 + flank)
                return r

            def shaped_ry(angle, r=ry, cur_z=z):
                # 1. Pronounced occipital bone / nuchal crest at skull posterior
                if -0.87 < cur_z < -0.80:
                    occiput = 0.28 * math.exp(-((cur_z + 0.835) / 0.035)**2) * max(0.0, math.cos(angle))**1.8
                    return r * (1.0 + occiput)
                # 2. Flat dorsal skull table & deep gular throat fold
                if cur_z < -0.85:
                    dorsal_flat = -0.10 * max(0.0, math.cos(angle)) if math.cos(angle) > 0.2 else 0.0
                    gular_sag = 0.14 * max(0.0, -math.cos(angle)) if math.cos(angle) < -0.2 else 0.0
                    return r * (1.0 + dorsal_flat + gular_sag)
                # 3. Flat reptilian belly and dorsal spine
                if -0.40 < cur_z < 0.38:
                    flat_belly = -0.06 * max(0.0, -math.cos(angle))
                    vertebral_ridge = 0.04 * max(0.0, math.cos(angle))**3.0
                    return r * (1.0 + flat_belly + vertebral_ridge)
                return r

            body_nodes.append(((0, cy, z), shaped_rx, shaped_ry, body_weight(z)))

    def monitor_color(c, q, i):
        belly = math.cos(q) < -0.38
        if belly:
            return (0.18, 0.165, 0.115, 1.0)
        band = math.sin(c.z * 18.0) * math.cos(q * 2.0)
        if band > 0.35:
            return (0.21, 0.175, 0.088, 1.0)
        return (0.075, 0.068, 0.046, 1.0)

    skin.ringmesh(body_nodes, 48, monitor_color)

    # 2. THICK CAUDOFEMORAL TAIL BASE SMOOTHLY BLENDED INTO PELVIS WITH SWIMMING KEEL
    tail_points = [
        (0.0, 0.245, 0.49), (0.015, 0.246, 0.58), (0.035, 0.245, 0.70), (0.058, 0.241, 0.825),
        (0.080, 0.235, 0.96), (0.101, 0.228, 1.102), (0.110, 0.220, 1.25), (0.096, 0.211, 1.403),
        (0.065, 0.200, 1.56), (0.017, 0.186, 1.721), (-0.035, 0.170, 1.88), (-0.078, 0.151, 2.034),
        (-0.110, 0.130, 2.18), (-0.127, 0.107, 2.316), (-0.130, 0.085, 2.44), (-0.111, 0.067, 2.561),
        (-0.090, 0.055, 2.65)
    ]
    pelvis_rx = 0.155
    pelvis_ry = 0.130

    tn = []
    for i in range(len(tail_points) - 1):
        for j in range(8):
            t = j / 8.0
            u = (i + t) / (len(tail_points) - 1)
            p0 = Vector(tail_points[max(0, i - 1)])
            p1 = Vector(tail_points[i])
            p2 = Vector(tail_points[i + 1])
            p3 = Vector(tail_points[min(len(tail_points) - 1, i + 2)])
            c = (2*p1 + (-p0 + p2)*t + (2*p0 - 5*p1 + 4*p2 - p3)*(t*t) + (-p0 + 3*p1 - 3*p2 + p3)*(t*t*t)) * 0.5
            weights = [(1-t)**3/6.0, (3*t**3 - 6*t*t + 4)/6.0, (-3*t**3 + 3*t*t + 3*t + 1)/6.0, (t**3)/6.0]
            ws = {}
            for k, w in zip([i - 1, i, i + 1, i + 2], weights):
                bname = 'Tail' + str(max(1, min(len(tail_points) - 1, k + 1)))
                ws[bname] = ws.get(bname, 0.0) + w

            # Lateral width: thick caudofemoral muscular base at pelvis junction (u=0: rx=0.155)
            # tapering smoothly into laterally compressed swimming paddle
            def tail_rx_func(angle, prog=u):
                base_w = pelvis_rx * (1.0 - 0.72 * (prog**0.80))
                return max(0.003, base_w)

            # Vertical height: seamlessly matches pelvis height at u=0 (ry=0.130)
            def tail_ry_func(angle, prog=u):
                base_h = pelvis_ry * (1.0 - 0.65 * (prog**1.05))
                # Double swimming crest along dorsal edge downstream
                if 0.18 < prog < 0.88:
                    keel = 0.022 * math.sin(math.pi * (prog - 0.18) / 0.70) * math.exp(-((abs(math.sin(angle)) - 0.22) / 0.12)**2) * (1.0 if math.cos(angle) > 0 else 0.0)
                    return max(0.0035, base_h + keel)
                return max(0.0035, base_h)

            tn.append((tuple(c), tail_rx_func, tail_ry_func, ws))

    def tail_color(c, q, i):
        band = (0.5 + 0.5 * math.sin(i * 1.15))**3
        return (0.072 + 0.10 * band, 0.063 + 0.08 * band, 0.042 + 0.04 * band, 1.0)

    skin.ringmesh(tn, 32, tail_color)

    # 3. BULKY, MUSCULAR SPRAWLING LIMBS WITH ANATOMICAL OVAL CROSS-SECTIONS
    limbs_config = [
        # (side, s_val, is_front, h, k, f)
        ('L',  1, True,  Vector(( 0.145, 0.260, -0.460)), Vector(( 0.300, 0.220, -0.360)), Vector(( 0.390, 0.050, -0.570))),
        ('R', -1, True,  Vector((-0.145, 0.260, -0.460)), Vector((-0.300, 0.220, -0.360)), Vector((-0.390, 0.050, -0.570))),
        ('L',  1, False, Vector(( 0.145, 0.245,  0.390)), Vector(( 0.320, 0.205,  0.290)), Vector(( 0.400, 0.050,  0.490))),
        ('R', -1, False, Vector((-0.145, 0.245,  0.390)), Vector((-0.320, 0.205,  0.290)), Vector((-0.400, 0.050,  0.490)))
    ]

    for side, s, front, h, k, f in limbs_config:
        upper_name = ('UpperArm_' if front else 'Thigh_') + side
        lower_name = ('Forearm_' if front else 'Shin_') + side
        distal_name = ('Hand_' if front else 'Foot_') + side

        # Increased bulky muscular radii
        r_upper = (0.125, 0.088) if front else (0.155, 0.105)
        r_lower = (0.082, 0.052) if front else (0.096, 0.060)

        ln = []
        # Upper limb segment (h -> k)
        for j in range(10):
            t = j / 9.0
            pos = h.lerp(k, t)
            bulge = 1.0 + 0.32 * math.sin(math.pi * t)
            r_base = (r_upper[0] * (1.0 - t) + r_upper[1] * t) * bulge
            ws = {upper_name: 1.0 - max(0.0, (t - 0.65) / 0.35) * 0.5, lower_name: max(0.0, (t - 0.65) / 0.35) * 0.5}

            def upper_rx(q, r=r_base):
                return r * 1.35
            def upper_ry(q, r=r_base):
                # Extensor triceps/caudofemoralis belly bulge on posterior
                extensor = 0.25 * max(0.0, -math.cos(q))**1.5
                return r * (0.90 + extensor)

            ln.append((tuple(pos), upper_rx, upper_ry, ws))

        # Lower limb segment (k -> f)
        for j in range(1, 10):
            t = j / 9.0
            pos = k.lerp(f, t)
            bulge = 1.0 + 0.26 * (math.sin(math.pi * t)**0.8)
            r_base = (r_lower[0] * (1.0 - t) + r_lower[1] * t) * bulge
            ws = {upper_name: max(0.0, (0.35 - t) / 0.35) * 0.5, lower_name: 1.0 - max(0.0, (0.35 - t) / 0.35) * 0.5}

            def lower_rx(q, r=r_base):
                return r * 1.25
            def lower_ry(q, r=r_base):
                # Gastrocnemius / flexor belly on posterior + anterior tibial crest
                belly = 0.22 * max(0.0, -math.cos(q))**1.6
                tibial = 0.12 * max(0.0, math.cos(q))**2.0
                return r * (0.88 + belly + tibial)

            ln.append((tuple(pos), lower_rx, lower_ry, ws))

        skin.ringmesh(ln, 28, (0.078, 0.069, 0.047, 1.0))

        # Anatomical elbow / knee condyle processes
        condyle_r = 0.082 if front else 0.095
        skin.ellipsoid(tuple(k), (condyle_r * 0.90, condyle_r * 1.15, condyle_r * 0.95), {upper_name: 0.5, lower_name: 0.5}, (0.076, 0.067, 0.045, 1.0))

        # Robust shoulder / hip girdle transitions
        girdle_r = 0.130 if front else 0.155
        skin.ellipsoid(tuple(h), (girdle_r * 0.95, girdle_r, girdle_r * 1.15), {upper_name: 0.75, ('Chest' if front else 'Pelvis'): 0.25}, (0.074, 0.065, 0.044, 1.0))

        # Broad fleshy palm / tarsal plantar pad
        pad_center = f + Vector((0, -0.022, -0.020))
        skin.ellipsoid(tuple(pad_center), (0.066, 0.026, 0.080), {distal_name: 1.0}, (0.082, 0.073, 0.050, 1.0))

        # 4. ARTICULATED DIGITS & RECURVED TALONS (CORRECTED DOWNWARD FORWARD ARC)
        digit_specs = [
            (0, 0.055, -0.38),  # Digit I (inner)
            (1, 0.076, -0.18),  # Digit II
            (2, 0.098,  0.02),  # Digit III
            (3, 0.124,  0.24),  # Digit IV (longest, diagnostic)
            (4, 0.068,  0.50)   # Digit V (outer)
        ]

        for d_idx, d_len, d_spread in digit_specs:
            f_forward = Vector((0, 0, -1))
            f_right = Vector((s * 1.0, 0, 0))
            d_dir = (f_forward * math.cos(d_spread) + f_right * math.sin(d_spread)).normalized()

            d_base = f + Vector((s * (d_idx - 2) * 0.018, -0.016, -0.025))
            ph1 = d_base + d_dir * (d_len * 0.38) + Vector((0, -0.004, 0))
            ph2 = ph1 + d_dir * (d_len * 0.34) + Vector((0, -0.006, 0))
            d_end = ph2 + d_dir * (d_len * 0.28) + Vector((0, -0.005, 0))

            # Muscular digit phalanges with knuckle nodes
            digit_nodes = [
                (tuple(d_base), 0.015, 0.013, {distal_name: 1.0}),
                (tuple(d_base.lerp(ph1, 0.5)), 0.013, 0.011, {distal_name: 1.0}),
                (tuple(ph1), 0.014, 0.012, {distal_name: 1.0}),
                (tuple(ph1.lerp(ph2, 0.5)), 0.011, 0.010, {distal_name: 1.0}),
                (tuple(ph2), 0.012, 0.011, {distal_name: 1.0}),
                (tuple(d_end), 0.009, 0.008, {distal_name: 1.0})
            ]
            skin.ringmesh(digit_nodes, 14, (0.080, 0.071, 0.048, 1.0))

            # RECURVED TALONS: Curved downward and outward along digit direction into ground
            claw = Builder(f"monitor_claw_{distal_name}_{d_idx}", rig, mats['claw'])
            claw_nodes = []
            claw_arc_steps = 7
            c_radius_base = 0.0075
            c_len = 0.034 * (d_len / 0.08)

            for step in range(claw_arc_steps + 1):
                prog = step / claw_arc_steps
                # Curvature angle: 0 at base, curling down towards -Y
                angle = prog * 1.15
                forward_dist = math.sin(angle) / 1.15 * c_len
                down_dist = (1.0 - math.cos(angle)) / 1.15 * c_len
                c_pos = d_end + d_dir * forward_dist + Vector((0, -down_dist, 0))

                # Laterally compressed cross section with sharp dorsal ridge on top
                rx_c = max(0.0006, c_radius_base * (1.0 - prog)**0.85 * 0.65)
                ry_c = max(0.0008, c_radius_base * (1.0 - prog)**0.80 * 1.45)

                def claw_rx_func(q, r=rx_c):
                    # Teardrop shape: sharp dorsal crest on top (q=0 facing +Y)
                    return r * (1.0 - 0.35 * max(0.0, math.cos(q)))

                claw_nodes.append((tuple(c_pos), claw_rx_func, ry_c, {distal_name: 1.0}))

            claw.ringmesh(claw_nodes, 12, (0.13, 0.11, 0.085, 1.0))
            claw.build()

    # Build main body mesh and apply continuous organic union
    body = skin.build()
    unify_lifelike_anatomy(body)

    # 5. ANATOMICAL HEAD STRUCTURES: EYEBALLS, EYELIDS, NOSTRILS, JAW
    setup_head_organs(Builder, rig, body, mats)
    return body

def unify_lifelike_anatomy(obj):
    """Organic voxel remesh union with surface relaxation and quad-preserving decimation."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(obj.data.vertices))
    samples = []
    colors = obj.data.color_attributes['Color']
    groupnames = {g.index: g.name for g in obj.vertex_groups}

    for v in obj.data.vertices:
        kd.insert(v.co, v.index)
        samples.append(({groupnames[g.group]: g.weight for g in v.groups}, tuple(colors.data[v.index].color)))
    kd.balance()

    active(obj)
    # Voxel size 0.007m preserves cranial crests, occipital bone, and swimming keel
    remesh = obj.modifiers.new('ContinuousAnatomyUnion', 'REMESH')
    remesh.mode = 'VOXEL'
    remesh.voxel_size = 0.007
    remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)

    smooth = obj.modifiers.new('OrganicSurfaceRelax', 'SMOOTH')
    smooth.factor = 0.45
    smooth.iterations = 3
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    dec = obj.modifiers.new('GameRetopology', 'DECIMATE')
    dec.ratio = 0.55
    bpy.ops.object.modifier_apply(modifier=dec.name)

    # Inset orbital cavities for true recessed reptilian eyes
    for side in [-1, 1]:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16, location=(side * 0.122, 0.326, -1.006))
        cutter = bpy.context.object
        cutter.scale = (0.020, 0.016, 0.024)
        active(cutter)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        active(obj)
        mod = obj.modifiers.new('MonitorInsetOrbit', 'BOOLEAN')
        mod.operation = 'DIFFERENCE'
        mod.solver = 'EXACT'
        mod.object = cutter
        bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.data.objects.remove(cutter, do_unlink=True)

    # Transfer skinning weights and vertex colors back to unified mesh
    obj.vertex_groups.clear()
    for name in obj.parent.data.bones.keys():
        obj.vertex_groups.new(name=name)
    for attr in list(obj.data.color_attributes):
        obj.data.color_attributes.remove(attr)
    color = obj.data.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')

    for v in obj.data.vertices:
        nearest = kd.find_n(v.co, 4)
        weights = {}
        col = [0, 0, 0, 0]
        total = 0.0
        for co, idx, dist in nearest:
            w = 1.0 / max(0.0002, dist)**2
            total += w
            ws, c = samples[idx]
            for n, value in ws.items():
                weights[n] = weights.get(n, 0.0) + value * w
            for j in range(4):
                col[j] += c[j] * w

        ws = sorted(weights.items(), key=lambda p: -p[1])[:4]
        norm = sum(w for n, w in ws)
        for n, w in ws:
            obj.vertex_groups[n].add([v.index], w / norm, 'REPLACE')
        color.data[v.index].color = [c / total for c in col]

    for p in obj.data.polygons:
        p.use_smooth = True

    active(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.008)
    bpy.ops.object.mode_set(mode='OBJECT')

def setup_head_organs(Builder, rig, body, mats):
    """Articulates eyes, eyelids, nostrils, tongue, and trapezoidal mandible."""
    eye_z = -1.006
    eye_y = 0.326
    eye_x = 0.116
    eye_r = 0.0145

    for s in [-1, 1]:
        # Eyeball cornea
        eye = Builder(f"monitor_eye_{s}", rig, mats['eye'])
        eye.ellipsoid((s * eye_x, eye_y, eye_z - 0.004), (eye_r * 0.70, eye_r, eye_r), {'Head': 1.0}, (0.014, 0.010, 0.006, 1.0))
        eye.build()

        # Amber iris
        iris = Builder(f"monitor_iris_{s}", rig, mats['iris'])
        iris.ellipsoid((s * (eye_x + 0.006), eye_y, eye_z - 0.004), (0.0006, eye_r * 0.42, eye_r * 0.42), {'Head': 1.0}, (0.24, 0.12, 0.03, 1.0))
        iris.build()

        # Recessed Auditory Cup (Tympanic membrane)
        ear = Builder(f"monitor_ear_{s}", rig, mats['mouth'])
        ear.ellipsoid((s * 0.115, 0.302, -0.870), (0.0015, 0.014, 0.011), {'Head': 1.0}, (0.035, 0.030, 0.020, 1.0))
        ear.build()

        # Nostril with fleshy alar margin on blunt square rostrum
        nostril = Builder(f"monitor_nostril_{s}", rig, mats['mouth'])
        hit, loc, norm, idx = body.ray_cast(Vector((s * 0.30, 0.298, -1.180)), Vector((-s, 0, 0)), distance=1.0)
        nx = abs(loc.x) + 0.0003 if hit else 0.068
        nostril.ellipsoid((s * nx, 0.298, -1.180), (0.0028, 0.0075, 0.011), {'Head': 1.0}, (0.025, 0.020, 0.014, 1.0))
        nostril.build()

    # Broad trapezoidal mandible (lower jaw) matching the skull jawline
    jaw = Builder("monitor_mandible", rig, mats['body'])
    jaw_nodes = [
        # Broad blunt square chin
        ((0, 0.234, -1.240), 0.054, 0.022, {'Jaw': 1.0}),
        ((0, 0.237, -1.180), 0.068, 0.025, {'Jaw': 1.0}),
        ((0, 0.242, -1.080), 0.096, 0.029, {'Jaw': 1.0}),
        ((0, 0.246, -0.960), 0.128, 0.034, {'Jaw': 1.0}),
        ((0, 0.250, -0.860), 0.142, 0.032, {'Jaw': 1.0}), # Broad retroarticular process
    ]
    def jaw_rx(q, base_r):
        return base_r * (1.0 + 0.08 * math.cos(q * 2))
    def jaw_ry(q, base_r):
        gular = 0.24 * max(0.0, -math.cos(q))
        return base_r * (1.0 + gular)

    jaw_shaped_nodes = [
        (c, lambda q, r=rx: jaw_rx(q, r), lambda q, r=ry: jaw_ry(q, r), ws)
        for c, rx, ry, ws in jaw_nodes
    ]
    jaw.ringmesh(jaw_shaped_nodes, 28, (0.19, 0.17, 0.12, 1.0))
    jaw.build()

    # Forked varanid tongue
    tongue = Builder("monitor_forked_tongue", rig, mats['tongue'])
    tongue.ringmesh([
        ((0, 0.269, -1.238), 0.0035, 0.0015, {'Tongue': 1.0}),
        ((0, 0.268, -1.310), 0.0030, 0.0013, {'Tongue': 1.0}),
        ((0, 0.267, -1.350), 0.0025, 0.0010, {'Tongue': 1.0})
    ], 10, (0.055, 0.015, 0.032, 1.0))
    for side in [-1, 1]:
        tongue.ringmesh([
            ((side * 0.0015, 0.267, -1.345), 0.0017, 0.0008, {'Tongue': 1.0}),
            ((side * 0.0120, 0.264, -1.375), 0.0004, 0.0004, {'Tongue': 1.0})
        ], 8, (0.055, 0.015, 0.032, 1.0))
    tongue.build()

def setup_animations(rig, tail_points):
    """Bakes standard 4-cycle actions: idle, walk, feed, rest."""
    modifiers = [m for o in bpy.data.objects if o.type == 'MESH' for m in o.modifiers if m.type == 'ARMATURE']
    for mod in modifiers:
        mod.show_viewport = False

    def world_rotation(name, axis, angle):
        return Quaternion(rig.data.bones[name].matrix_local.to_3x3().inverted() @ Vector(axis), angle)

    rig.animation_data_create()
    acts = {}

    for name, length in [('idle', 60), ('walk', 32), ('feed', 72), ('rest', 60)]:
        act = bpy.data.actions.new(name)
        rig.animation_data.action = act
        acts[name] = act

        for frame in range(1, length + 2):
            t = (frame - 1) / length
            phase = t * TAU

            # Reset pose
            for b in rig.pose.bones:
                b.location = (0, 0, 0)
                b.rotation_quaternion = (1, 0, 0, 0)
                b.scale = (1, 1, 1)

            if name == 'idle':
                rig.pose.bones['Chest'].scale = (1.0 + 0.014 * math.sin(phase), 1.0 + 0.008 * math.sin(phase), 1.0)
                look = 0.045 * math.tanh(3.0 * math.sin(phase))
                rig.pose.bones['Head'].rotation_quaternion = world_rotation('Head', (0, 1, 0), look) * world_rotation('Head', (1, 0, 0), 0.010 * math.sin(phase))
                rig.pose.bones['Neck'].rotation_quaternion = world_rotation('Neck', (0, 1, 0), -0.014 * math.sin(phase))
                flick = (max(0.0, min(1.0, (t - 0.265) / 0.018)) * max(0.0, min(1.0, (0.365 - t) / 0.082)))
                rig.pose.bones['Tongue'].scale = (1.0, max(0.002, flick), 1.0)
                if flick > 0:
                    rig.pose.bones['Jaw'].rotation_quaternion = world_rotation('Jaw', (1, 0, 0), -0.028 * flick)

            elif name == 'walk':
                rig.pose.bones['Spine1'].rotation_quaternion = world_rotation('Spine1', (0, 1, 0), 0.035 * math.sin(phase))
                rig.pose.bones['Chest'].rotation_quaternion = world_rotation('Chest', (0, 1, 0), -0.028 * math.sin(phase))
                rig.pose.bones['Pelvis'].rotation_quaternion = world_rotation('Pelvis', (0, 1, 0), 0.030 * math.sin(phase))

            elif name == 'feed':
                dip = (1.0 - math.cos(phase)) * 0.5
                rig.pose.bones['Neck'].rotation_quaternion = world_rotation('Neck', (1, 0, 0), -0.22 * dip)
                rig.pose.bones['Head'].rotation_quaternion = world_rotation('Head', (1, 0, 0), -0.16 * dip)
                rig.pose.bones['Jaw'].rotation_quaternion = world_rotation('Jaw', (1, 0, 0), -0.15 * dip * (0.5 + 0.5 * math.sin(phase * 4)))

            elif name == 'rest':
                delta = Vector((0, -0.07, 0))
                pb = rig.pose.bones['Pelvis']
                pb.location = rig.data.bones['Pelvis'].matrix_local.to_3x3().inverted() @ delta
                rig.pose.bones['Chest'].scale = (1.0 + 0.010 * math.sin(phase), 1.0, 1.0)

            # Tail wave
            for i in range(1, len(tail_points)):
                rig.pose.bones['Tail' + str(i)].rotation_quaternion = (
                    world_rotation('Tail' + str(i), (0, 1, 0), 0.028 * math.sin(phase - i * 0.22)) *
                    world_rotation('Tail' + str(i), (1, 0, 0), 0.003 * math.sin(phase - i * 0.30))
                )

            for pb in rig.pose.bones:
                pb.keyframe_insert('location', frame=frame)
                pb.keyframe_insert('rotation_quaternion', frame=frame)
                pb.keyframe_insert('scale', frame=frame)

        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, act)
        track.mute = True

    rig.animation_data.action = None
    bpy.context.scene.frame_set(1)
    for mod in modifiers:
        mod.show_viewport = True
    bpy.context.view_layer.update()
    return acts

def consolidate_and_export(rig, out_dir):
    blend_path = str(out_dir / "guardian_realistic.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print(f"Saved source blend to: {blend_path}")

    glb_path = str(out_dir / "guardian_realistic.glb")
    if os.path.exists(glb_path):
        try:
            os.remove(glb_path)
        except Exception:
            pass
    active(rig)
    for o in bpy.data.objects:
        o.select_set(True)

    bpy.ops.export_scene.gltf(
        filepath=glb_path,
        export_format='GLB',
        use_selection=False,
        export_apply=False,
        export_armature_object_remove=False,
        export_skins=True,
        export_all_influences=False,
        export_morph=True,
        export_animations=True,
        export_nla_strips=True,
        export_def_bones=True,
        export_yup=True
    )
    print(f"Exported candidate GLB to: {glb_path} ({os.path.getsize(glb_path):,} bytes)")

def main():
    print("=== Authoring Realistic Water Monitor Guardian v2 ===")
    bpy.ops.wm.read_factory_settings(use_empty=True)

    tex_col, tex_norm, tex_rough = generate_reptile_pbr_textures(OUT_DIR)
    mats = {
        'body': material('RealisticMonitorScales', (0.12, 0.11, 0.08), rough=0.6, texture=tex_col, normal=tex_norm),
        'claw': material('MonitorKeratinClaw', (0.16, 0.13, 0.09), rough=0.35),
        'eye': material('MonitorCornea', (0.01, 0.01, 0.01), rough=0.05),
        'iris': material('MonitorIrisAmber', (0.28, 0.14, 0.03), rough=0.25),
        'mouth': material('MonitorMucosa', (0.04, 0.02, 0.02), rough=0.4),
        'tongue': material('MonitorForkedTongue', (0.06, 0.015, 0.035), rough=0.3)
    }

    rig, tail_points = create_rigging()
    build_lifelike_anatomy(rig, mats)
    setup_animations(rig, tail_points)
    consolidate_and_export(rig, OUT_DIR)
    print("=== Realistic Monitor Guardian Authoring v2 Complete ===")

if __name__ == "__main__":
    main()
