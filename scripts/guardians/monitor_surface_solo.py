"""Anatomical head organs, hinged trapezoidal mandible, recessed nares/tympanum,
circumorbital eyelids, and un-melted 5-digit articulated paws with recurved talons.
"""
import bpy
import math
from mathutils import Vector
import numpy as np


def mouth_y(z):
    """Height of the horizontal labial commissure (mouth line) before neck lift."""
    # Gentle varanid curve: slightly arched under premaxilla/naris, dipping under jugal cheek,
    # rising slightly to the retroarticular rictus corner at z = -0.86.
    if z < -1.05:
        t = max(0.0, min(1.0, (-1.05 - z) / 0.22))
        return 0.264 + 0.004 * math.sin(math.pi * t)
    t = max(0.0, min(1.0, (z + 1.05) / 0.20))
    return 0.264 + 0.008 * (t * t * (3.0 - 2.0 * t))


def upper_jaw_halfwidth(z):
    """Plan-view half-width of the upper skull along the labial margin."""
    pts = [
        (-1.27, 0.020),
        (-1.24, 0.043),
        (-1.19, 0.057),
        (-1.12, 0.072),
        (-1.05, 0.088),
        (-1.00, 0.098),
        (-0.93, 0.110),
        (-0.85, 0.112),
        (-0.77, 0.098),
        (-0.68, 0.100),
    ]
    if z <= pts[0][0]:
        return pts[0][1]
    if z >= pts[-1][0]:
        return pts[-1][1]
    for (z0, w0), (z1, w1) in zip(pts, pts[1:]):
        if z0 <= z <= z1:
            t = (z - z0) / (z1 - z0)
            t = t * t * (3.0 - 2.0 * t)
            return w0 * (1.0 - t) + w1 * t
    return 0.085


def images(out):
    """Initializes 2048x2048 PBR texture slots (populated in 3D space by bake_pigment)."""
    size = 2048
    u, v = np.meshgrid(np.arange(size, dtype=np.float32) / size, np.arange(size, dtype=np.float32) / size)
    rng = np.random.default_rng(902)
    x = u * 150.0 + (np.floor(v * 180.0) % 2.0) * 0.5 + 0.12 * np.sin(v * 94.0)
    y = v * 180.0 + 0.10 * np.sin(u * 65.0)
    dx = (x % 1.0) - 0.5
    dy = (y % 1.0) - 0.5
    h = np.maximum(0.0, 1.0 - 4.0 * dx * dx - 4.0 * dy * dy) ** 0.55
    cell = rng.random((181, 152), dtype=np.float32)
    variation = cell[np.minimum(180, y.astype(int)), np.minimum(151, x.astype(int))]
    value = 0.70 + 0.20 * h + 0.08 * variation
    rgb = np.stack((value * 0.90, value * 0.92, value * 0.80), axis=-1)

    def save(name, data, noncolor=False):
        im = bpy.data.images.new('monitor_' + name, width=size, height=size)
        if noncolor:
            im.colorspace_settings.name = 'Non-Color'
        rgba = np.concatenate((data, np.ones((size, size, 1), dtype=np.float32)), axis=-1).astype('float32')
        im.pixels.foreach_set(rgba.ravel())
        im.filepath_raw = str(out / (name + '.png'))
        im.file_format = 'PNG'
        im.save()
        im.pack()
        return im

    color = save('surface_albedo', rgb)
    gy, gx = np.gradient(h)
    n = np.stack((-gx * 1.6, -gy * 1.6, np.ones_like(h)), axis=-1)
    n /= np.linalg.norm(n, axis=-1)[..., None]
    normal = save('surface_normal', n * 0.5 + 0.5, True)
    rough = 0.48 + 0.18 * (1.0 - h) + 0.045 * variation
    rough_im = save('surface_roughness', np.stack((rough, rough, rough), axis=-1), True)
    return color, normal, rough_im


def roughness(mat, image):
    for node in mat.node_tree.nodes:
        if node.type == 'NORMAL_MAP':
            node.inputs['Strength'].default_value = 0.78
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    mat.node_tree.links.new(tex.outputs['Color'], mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'])


def build_digits_and_claws(Builder, rig, mat, mats, limbs):
    """Builds 5 distinct, articulated, un-melted varanid digits and recurved talons per foot."""
    digits_builder = Builder('monitor_articulated_digits', rig, mat)

    # Authentic Varanus pedal asymmetry: Digit I inner shortest, Digit IV longest, Digit V divergent outer
    digit_specs = [
        # (idx, length_m, spread_rad, base_r, knuckle_arch)
        (0, 0.056, -0.46, 0.0125, 0.005),  # Digit I (hallux/pollex)
        (1, 0.078, -0.22, 0.0138, 0.007),  # Digit II
        (2, 0.098,  0.00, 0.0148, 0.009),  # Digit III
        (3, 0.116,  0.22, 0.0155, 0.011),  # Digit IV (diagnostic longest varanid toe)
        (4, 0.072,  0.50, 0.0132, 0.006),  # Digit V (divergent outer toe)
    ]

    for limb_idx, (side, front, h, k, f) in enumerate(limbs):
        s = 1.0 if side == 'L' else -1.0
        distal_bone = ('Hand_' if front else 'Foot_') + side
        scale_len = 0.90 if front else 1.06

        for d_idx, base_len, spread, r0, arch_h in digit_specs:
            d_len = base_len * scale_len
            # Splay outward relative to foot forward (-Z) and lateral (s * +X)
            angle = spread + (0.06 if front else 0.10)
            d_dir = Vector((s * math.sin(angle), 0.0, -math.cos(angle))).normalized()
            d_lat = Vector((d_dir.z * s, 0.0, -d_dir.x * s)).normalized()

            # Root embedded deep inside the carpal/tarsal pad so there is no seam or gap
            d_root = f + Vector((s * (d_idx - 2.0) * 0.013, -0.020, -0.018))
            sole_y = f.y - 0.036

            # 9 stations along the 3 phalanges with anatomical knuckle swells
            d_nodes = []
            n_stations = 9
            for st in range(n_stations):
                u = st / (n_stations - 1)
                dist = u * d_len
                # Phalangeal arch: rises over proximal/middle knuckles, plants at distal pad
                arch = arch_h * math.sin(math.pi * (u ** 0.85))
                cur_y = (d_root.y * (1.0 - u) + (sole_y + 0.008) * u) + arch
                pos = Vector((d_root.x + d_dir.x * dist, cur_y, d_root.z + d_dir.z * dist))

                # Knuckle swells at u=0.35 and u=0.68, plus ungual collar swell at u=0.92
                knuckle = (
                    1.0
                    + 0.14 * math.exp(-((u - 0.35) / 0.10) ** 2)
                    + 0.12 * math.exp(-((u - 0.68) / 0.09) ** 2)
                    + 0.10 * math.exp(-((u - 0.92) / 0.07) ** 2)
                )
                r_cur = r0 * (1.0 - 0.42 * (u ** 1.1)) * knuckle
                rx = r_cur * 1.05
                ry = r_cur * 0.88
                d_nodes.append((tuple(pos), rx, ry, {distal_bone: 1.0}))

            # Distal toe tip position before ungual taper
            d_end = Vector(d_nodes[-1][0])
            c_r0 = r0 * 0.64
            # Add tapered ungual sheath tip station that tucks directly inside the claw root
            ungual_tip = d_end + d_dir * 0.004 + Vector((0.0, -0.001, 0.0))
            d_nodes.append((tuple(ungual_tip), c_r0 * 0.58, c_r0 * 0.95, {distal_bone: 1.0}))

            uv_start = len(digits_builder.uv)
            digits_builder.ringmesh(d_nodes, radial=16, color=(1.0, 1.0, 1.0, 1.0), uvscale=1.0)
            # Map each toe's parametric UV grid into its own dedicated non-overlapping tile in [0.004, 0.464] x [0.758, 0.990]
            u_min = 0.004 + d_idx * 0.092
            u_span = 0.084
            v_min = 0.758 + limb_idx * 0.058
            v_span = 0.048
            for uv_i in range(uv_start, len(digits_builder.uv) - 2):
                uu, vv = digits_builder.uv[uv_i]
                digits_builder.uv[uv_i] = (u_min + uu * u_span, v_min + vv * v_span)
            digits_builder.uv[-2] = (u_min + 0.5 * u_span, v_min - 0.002)
            digits_builder.uv[-1] = (u_min + 0.5 * u_span, v_min + v_span + 0.002)

            # Recurved keratin talon emerging seamlessly from the ungual sheath
            claw = Builder(f"monitor_claw_{distal_bone}{d_idx}", rig, mats['claw'])
            claw_nodes = []
            c_steps = 8
            c_len = 0.035 * (d_len / 0.088)
            c_start = d_end - d_dir * 0.003 + Vector((0.0, 0.0012, 0.0))

            for step in range(c_steps + 1):
                prog = step / c_steps
                # Smooth downward hook (arcs forward along d_dir and curves down toward substrate)
                curve_ang = prog * 1.08
                fwd_d = (math.sin(curve_ang) / 1.08) * c_len
                down_d = ((1.0 - math.cos(curve_ang)) / 1.08) * c_len * 0.82
                c_pos = c_start + d_dir * fwd_d + Vector((0.0, -down_d, 0.0))
                # Clamp claw tip above ground support plane (f.y - 0.044)
                c_pos.y = max(f.y - 0.043, c_pos.y)

                taper = (1.0 - prog) ** 0.80
                rx_c = max(0.0005, c_r0 * taper * 0.72)
                ry_c = max(0.0007, c_r0 * taper * 1.28)
                claw_nodes.append((tuple(c_pos), rx_c, ry_c, {distal_bone: 1.0}))

            claw.ringmesh(claw_nodes, radial=14, color=(0.18, 0.15, 0.10, 1.0))
            claw.build()

    return digits_builder.build()


def face(Builder, rig, body, mat, mats):
    """Builds the hinged trapezoidal mandible, oral cavity, circumorbital eyelids,
    recessed dorsolateral nares, and recessed tympanic ear cups.
    Returns the list of skin-material mesh objects to join into monitor_body before UV baking.
    """
    skin_parts = []

    # 1. HINGED TRAPEZOIDAL MANDIBLE & PLEATED GULAR THROAT POUCH
    jaw = Builder('monitor_mandible_sculpted', rig, mat)
    zs = np.linspace(-1.262, -0.73, 30)
    radial = 36
    start_v = len(jaw.v)

    for i, z in enumerate(zs):
        ym = mouth_y(z)
        w_up = upper_jaw_halfwidth(z)
        # Jaw matches upper skull plan-view contour closely (tucked 1.5mm inside upper labial overhang)
        rx = max(0.016, w_up - 0.0018)

        # Mandibular depth and pleated gular pouch profile
        if z < -0.92:
            u = (z - (-1.262)) / (-0.92 - (-1.262))
            depth = 0.017 + 0.022 * (u ** 0.85)
            gular_pleat = 0.0
        else:
            u = (z - (-0.92)) / (-0.73 - (-0.92))
            # Tapers smoothly into ventral neck while forming 3 transverse gular folds
            envelope = math.sin(math.pi * min(1.0, (1.0 - u) * 0.85 + 0.15))
            gular_pleat = 0.0035 * math.sin((z + 0.92) * 65.0) * (1.0 - u)
            depth = (0.039 * (1.0 - 0.35 * (u ** 1.4))) + gular_pleat

        # Smooth skeletal blend from Jaw (ramus/chin) to Head/Neck (gular hinge)
        if z <= -0.93:
            ws = {'Jaw': 1.0}
        elif z >= -0.76:
            ws = {'Head': 0.25, 'Neck': 0.75}
        else:
            t = (z - (-0.93)) / (-0.76 - (-0.93))
            t = t * t * (3.0 - 2.0 * t)
            ws = {'Jaw': 1.0 - t, 'Head': 0.35 * t, 'Neck': 0.65 * t}

        for j in range(radial + 1):
            q = math.tau * j / radial
            sq = math.sin(q)
            cq = math.cos(q)
            # Squared-off chin at front, broad trapezoidal rami along sides
            box = (abs(sq) ** 2.6 + abs(cq) ** 2.6) ** (-1.0 / 2.6) if z < -1.12 else 1.0
            x = rx * sq * (0.94 + 0.06 * box)
            if cq >= 0.0:
                # Upper mandibular surface: meets labial line all around rostral tip & sides, dips into oral floor inside mouth
                lip_rim = ym - 0.0002
                front_seal = max(0.0, min(1.0, (float(z) + 1.220) / 0.065))
                back_seal = max(0.0, min(1.0, (-0.86 - float(z)) / 0.04))
                oral_dip = 0.0050 * (1.0 - sq * sq) * front_seal * back_seal
                y = lip_rim - oral_dip + 0.0012 * cq
            else:
                # Ventral mandibular & gular contour: broad rounded-flat underside with gular sag
                vent_shape = abs(cq) ** 0.78
                y = (ym - 0.001) - depth * vent_shape

            jaw.v.append((x, y, float(z)))
            jaw.uv.append((0.472 + (j / radial) * 0.170, 0.760 + (i / (len(zs) - 1)) * 0.228))
            jaw.w.append(ws)
            jaw.col.append((1.0, 1.0, 1.0, 1.0))

    n_rings = len(zs)
    for i in range(n_rings - 1):
        for j in range(radial):
            k = start_v + i * (radial + 1) + j
            jaw.f.append((k, k + radial + 1, k + radial + 2, k + 1))

    # Cap front chin tip and posterior gular transition
    for idx_ring, rev in [(0, True), (n_rings - 1, False)]:
        k_center = len(jaw.v)
        z_end = float(zs[idx_ring])
        ym_end = mouth_y(z_end)
        jaw.v.append((0.0, ym_end - 0.008, z_end))
        jaw.uv.append((0.472 + 0.085, 0.757 if rev else 0.991))
        jaw.w.append({'Jaw': 1.0} if rev else {'Head': 0.25, 'Neck': 0.75})
        jaw.col.append((1.0, 1.0, 1.0, 1.0))
        r_base = start_v + idx_ring * (radial + 1)
        for j in range(radial):
            jaw.f.append((r_base + j + 1, r_base + j, k_center) if rev else (r_base + j, r_base + j + 1, k_center))

    skin_parts.append(jaw.build())

    # 2. RECESSED DARK ORAL COMMISSURE & PALATE LINING (MATS['mouth'])
    palate = Builder('monitor_oral_palate', rig, mats['mouth'])
    p_zs = np.linspace(-1.255, -0.865, 18)
    p_nodes = []
    for z in p_zs:
        ym = mouth_y(z)
        w = max(0.012, upper_jaw_halfwidth(z) - 0.0022)
        p_nodes.append(((0.0, ym + 0.0005, float(z)), w, 0.0025, {'Head': 1.0}))
    palate.ringmesh(p_nodes, radial=16, color=(0.035, 0.022, 0.020, 1.0))
    palate.build()

    # 3. SCULPTED CIRCUMORBITAL EYELID COLLARS (Bridges socket to almond palpebral fissure)
    for s in [-1, 1]:
        b = Builder('monitor_eyelid_' + str(s), rig, mat)
        seg = 48
        for r in range(5):
            t = r / 4.0
            for j in range(seg):
                a = math.tau * j / seg
                # Outer ring conforms to brow & jugal socket; inner ring forms almond aperture around cornea
                oy = 0.326 + math.sin(a) * 0.0185
                oz = -1.006 + math.cos(a) * 0.0285
                hit, p_hit, _, _ = body.ray_cast(Vector((s * 0.30, oy, oz)), Vector((-s, 0.0, 0.0)), distance=1.0)
                ox = abs(p_hit.x) + 0.0004 if hit else 0.094

                # Inner almond aperture with sharp canthal corners
                almond_y = math.sin(a) * 0.0082
                almond_z = math.cos(a) * 0.0135
                iy = 0.326 + almond_y
                iz = -1.006 + almond_z
                ix = 0.086 + 0.0080 * math.sqrt(max(0.05, 1.0 - (almond_y / 0.013) ** 2 - (almond_z / 0.013) ** 2))

                # Raised palpebral rim bevel at t=0.75
                rim_bulge = 0.0018 * math.sin(math.pi * t)
                x = s * (ox * (1.0 - t) + ix * t + rim_bulge)
                y = oy * (1.0 - t) + iy * t
                z = oz * (1.0 - t) + iz * t
                b.v.append((x, y, z))
                b.uv.append((j / seg, t))
                b.w.append({'Head': 1.0})
                b.col.append((1.0, 1.0, 1.0, 1.0))
        for r in range(4):
            for j in range(seg):
                a = r * seg + j
                q = r * seg + (j + 1) % seg
                if s == 1:
                    b.f.append((a, q, q + seg, a + seg))
                else:
                    b.f.append((a, a + seg, q + seg, q))
        skin_parts.append(b.build())

        # 4. RECESSED DORSOLATERAL NOSTRIL CAVITY (Naris rim + recessed dark pit)
        naris = Builder('monitor_naris_rim_' + str(s), rig, mat)
        n_seg = 28
        ny_c, nz_c = 0.292, -1.188
        for r in range(4):
            t = r / 3.0
            for j in range(n_seg):
                a = math.tau * j / n_seg
                # Oblique oval naris oriented along canthus rostralis
                dy = (math.sin(a) * 0.0055 + math.cos(a) * 0.0018) * (1.0 - 0.68 * t)
                dz = (math.cos(a) * 0.0082 - math.sin(a) * 0.0018) * (1.0 - 0.68 * t)
                y = ny_c + dy
                z = nz_c + dz
                hit, loc, _, _ = body.ray_cast(Vector((s * 0.25, y, z)), Vector((-s, 0.0, 0.0)), distance=1.0)
                surf_x = abs(loc.x) if hit else 0.053
                # Outer lip rises 1.2mm to form fleshy narial torus, then recesses 4.5mm inward
                if r == 0:
                    dx = 0.0002
                elif r == 1:
                    dx = 0.0014
                elif r == 2:
                    dx = -0.0012
                else:
                    dx = -0.0042
                naris.v.append((s * (surf_x + dx), y, z))
                naris.uv.append((j / n_seg, t))
                naris.w.append({'Head': 1.0})
                naris.col.append((1.0, 1.0, 1.0, 1.0))
        for r in range(3):
            for j in range(n_seg):
                a = r * n_seg + j
                q = r * n_seg + (j + 1) % n_seg
                if s == 1:
                    naris.f.append((a, q, q + n_seg, a + n_seg))
                else:
                    naris.f.append((a, a + n_seg, q + n_seg, q))
        skin_parts.append(naris.build())

        # Dark internal nasal floor inside the recessed naris pit
        npit = Builder('monitor_nostril_pit_' + str(s), rig, mats['mouth'])
        hit, loc, _, _ = body.ray_cast(Vector((s * 0.25, ny_c, nz_c)), Vector((-s, 0.0, 0.0)), distance=1.0)
        pit_x = max(0.015, (abs(loc.x) if hit else 0.053) - 0.0022)
        npit.ellipsoid((s * pit_x, ny_c, nz_c), (0.0012, 0.0032, 0.0048), {'Head': 1.0}, (0.015, 0.012, 0.010, 1.0))
        npit.build()

        # 5. RECESSED TYMPANIC AUDITORY CUP
        ear = Builder('monitor_aural_cup_' + str(s), rig, mat)
        e_seg = 32
        ey_c, ez_c = 0.298, -0.878
        for r in range(4):
            t = r / 3.0
            for j in range(e_seg):
                a = math.tau * j / e_seg
                y = ey_c + math.sin(a) * (0.014 * (1.0 - 0.65 * t))
                z = ez_c + math.cos(a) * (0.0095 * (1.0 - 0.65 * t))
                hit, loc, _, _ = body.ray_cast(Vector((s * 0.30, y, z)), Vector((-s, 0.0, 0.0)), distance=1.0)
                surf_x = abs(loc.x) if hit else 0.106
                # Raised pre-auricular anterior fold (cos(a) < 0), recessed tympanic floor (t -> 1)
                pre_fold = 0.0018 * max(0.0, -math.cos(a)) * math.sin(math.pi * (1.0 - t))
                recess = -0.0058 * (t ** 1.2)
                ear.v.append((s * (surf_x + pre_fold + recess), y, z))
                ear.uv.append((j / e_seg, t))
                ear.w.append({'Head': 1.0})
                ear.col.append((1.0, 1.0, 1.0, 1.0))
        for r in range(3):
            for j in range(e_seg):
                a = r * e_seg + j
                q = r * e_seg + (j + 1) % e_seg
                if s == 1:
                    ear.f.append((a, q, q + e_seg, a + e_seg))
                else:
                    ear.f.append((a, a + e_seg, q + e_seg, q))
        skin_parts.append(ear.build())

    return skin_parts
