"""Sculpted 3D anatomical osteoderm crests, supraorbital/temporal horn-plates,
double-crested caudal swimming sail, and longitudinal rows of keeled dermal osteoderms.

Replaces the 14,000 random flat hexagon discs with organized, anatomical, slightly
fantastical varanid osteoderm armor that joins into monitor_body for unified 3D UV baking.
"""
import bpy
import math
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def _add_keeled_plate(b, center, normal, forward, right, length, width, height, weights, tilt_back=0.35):
    """Adds a closed 3D imbricated keeled osteoderm/crest plate embedded into the skin."""
    center = Vector(center)
    normal = Vector(normal).normalized()
    forward = Vector(forward).normalized()
    right = Vector(right).normalized()
    back = -forward

    # 5 stations: anterior root (embedded), mid-anterior, crown keel, posterior slope, pointed apex
    start = len(b.v)
    plate_idx = start // 26
    grid_col = plate_idx % 24
    grid_row = (plate_idx // 24) % 24
    u_base = 0.722 + grid_col * (0.270 / 24.0)
    v_base = 0.762 + grid_row * (0.230 / 24.0)
    u_span = (0.270 / 24.0) * 0.78
    v_span = (0.230 / 24.0) * 0.78
    ring_specs = [
        # (longitudinal_offset, width_factor, height_offset)
        (-0.42 * length, 0.45, -0.0008),
        (-0.10 * length, 1.00,  0.42 * height),
        ( 0.22 * length, 0.82,  1.00 * height),
        ( 0.58 * length, 0.38,  0.65 * height),
    ]
    # 6 vertices around each ring: left margin, left-dorsal slope, sharp dorsal keel, right-dorsal slope, right margin, ventral base
    angles = [
        (-1.0,  0.00),  # 0: Left base margin
        (-0.48, 0.72),  # 1: Left dorsal facet
        ( 0.00, 1.00),  # 2: Sharp central longitudinal keel
        ( 0.48, 0.72),  # 3: Right dorsal facet
        ( 1.0,  0.00),  # 4: Right base margin
        ( 0.00, -0.15), # 5: Embedded underside root
    ]

    for r_idx, (along_d, w_fac, h_val) in enumerate(ring_specs):
        u_v = r_idx / 4.0
        for c_idx, (lat_f, up_f) in enumerate(angles):
            elev = h_val * up_f if up_f >= 0 else -0.0008
            sweep = max(0.0, up_f) * height * tilt_back * (r_idx / 3.0)
            p = (
                center
                + back * (along_d + sweep)
                + right * (lat_f * width * 0.5 * w_fac)
                + normal * elev
            )
            b.v.append(tuple(p))
            b.uv.append((u_base + (c_idx / len(angles)) * u_span, v_base + u_v * v_span))
            b.w.append(weights)
            alpha_tag = 1.0 - 0.55 * max(0.0, up_f)
            b.col.append((1.0, 1.0, 1.0, alpha_tag))

    # Pointed posterior apex vertex (embedded slightly above skin, sweeping backward)
    apex_idx = len(b.v)
    p_apex = center + back * (0.85 * length + height * tilt_back) + normal * (0.18 * height)
    b.v.append(tuple(p_apex))
    b.uv.append((u_base + 0.5 * u_span, v_base + 1.0 * v_span))
    b.w.append(weights)
    b.col.append((1.0, 1.0, 1.0, 0.45))

    # Anterior root cap vertex
    ant_idx = len(b.v)
    p_ant = center - back * (0.52 * length) - normal * 0.0008
    b.v.append(tuple(p_ant))
    b.uv.append((u_base + 0.5 * u_span, v_base))
    b.w.append(weights)
    b.col.append((1.0, 1.0, 1.0, 1.0))

    n_cols = len(angles)
    # Stitch rings
    for r in range(len(ring_specs) - 1):
        r0 = start + r * n_cols
        r1 = start + (r + 1) * n_cols
        for c in range(n_cols):
            cn = (c + 1) % n_cols
            b.f.append((r0 + c, r0 + cn, r1 + cn, r1 + c))

    # Stitch anterior cap and posterior apex
    first_r = start
    last_r = start + (len(ring_specs) - 1) * n_cols
    for c in range(n_cols):
        cn = (c + 1) % n_cols
        b.f.append((ant_idx, first_r + cn, first_r + c))
        b.f.append((last_r + c, last_r + cn, apex_idx))


def _body_weight_at_z(z):
    centers = [(-1.02, 'Head'), (-0.70, 'Neck'), (-0.35, 'Chest'), (0.08, 'Spine1'), (0.42, 'Pelvis')]
    if z <= centers[0][0]:
        return {centers[0][1]: 1.0}
    if z >= centers[-1][0]:
        return {centers[-1][1]: 1.0}
    for (a, an), (b, bn) in zip(centers, centers[1:]):
        if a <= z <= b:
            t = (z - a) / (b - a)
            t = t * t * (3.0 - 2.0 * t)
            return {an: 1.0 - t, bn: t}
    return {'Spine1': 1.0}


def scutes(Builder, rig, body, mat, tail_points=None):
    """Builds sculpted 3D osteoderm crests and dorsolateral armor rows."""
    b = Builder('monitor_sculpted_osteoderms', rig, mat)
    mesh = body.data
    mesh.calc_loop_triangles()
    bvh = BVHTree.FromPolygons(
        [tuple(v.co) for v in mesh.vertices],
        [tuple(t.vertices) for t in mesh.loop_triangles],
        all_triangles=True,
    )

    def project_dorsal(x, z, dir_vec=(0.0, -1.0, 0.0), start_y=0.60):
        loc, norm, idx, dist = bvh.ray_cast(Vector((x, start_y, z)), Vector(dir_vec).normalized(), 1.2)
        if loc is not None:
            return loc, norm
        return None, None

    # 1. SUPRAORBITAL BROW CRESTS & TEMPORAL CROWN PLATES (Slightly fantastical dragon-monitor brow)
    for side in [-1.0, 1.0]:
        # 7 sleek imbricated supraorbital & post-orbital horn-scutes per side directly over the brow ridge
        for k in range(7):
            t = k / 6.0
            z = -1.032 + t * 0.185  # From supraorbital front (-1.032) over orbit (-1.006) to temporal shelf (-0.847)
            ray_orig = Vector((side * 0.15, 0.41, z))
            ray_dir = Vector((-side * 0.62, -0.78, 0.0)).normalized()
            loc, norm = bvh.ray_cast(ray_orig, ray_dir, 0.4)[:2]
            if loc is None:
                continue
            out_n = (norm.normalized() * 0.65 + Vector((side * 0.48, 0.85, 0.16)).normalized() * 0.35).normalized()
            fwd = Vector((0.0, 0.0, -1.0))
            right = fwd.cross(out_n).normalized()
            fwd = out_n.cross(right).normalized()
            arch = math.sin(math.pi * (0.20 + 0.72 * t))
            length = 0.019 + 0.007 * arch
            width = 0.0085 + 0.0035 * arch
            height = 0.0018 + 0.0022 * (t ** 0.90)
            _add_keeled_plate(b, loc, out_n, fwd, right, length, width, height, {'Head': 1.0}, tilt_back=0.72)

    # Occipital / nuchal transverse crown bosses at posterior skull margin (z = -0.83..-0.79)
    for m_idx in [-2, -1, 0, 1, 2]:
        x_off = m_idx * 0.024
        z_occ = -0.825 + abs(m_idx) * 0.008
        loc, norm = project_dorsal(x_off, z_occ)
        if loc is not None:
            out_n = Vector((m_idx * 0.14, 0.96, 0.22)).normalized()
            fwd = Vector((0.0, 0.0, -1.0))
            right = fwd.cross(out_n).normalized()
            sz = 1.0 - 0.12 * abs(m_idx)
            _add_keeled_plate(b, loc, out_n, fwd, right, 0.026 * sz, 0.014 * sz, 0.0075 * sz, _body_weight_at_z(z_occ), tilt_back=0.50)

    # 2. MEDIAN NUCHAL & VERTEBRAL DORSAL CREST (Neck -> Chest -> Spine1 -> Pelvis)
    n_dorsal = 38
    for i in range(n_dorsal):
        t = i / (n_dorsal - 1)
        z = -0.77 + t * 1.24  # -0.77 to +0.47
        loc, norm = project_dorsal(0.0, z)
        if loc is None:
            continue
        # Nuchal peak on neck (t~0.12) and thoracic peak on mid-back (t~0.52)
        nuchal_peak = math.exp(-((t - 0.12) / 0.11) ** 2)
        thoracic_peak = math.exp(-((t - 0.50) / 0.28) ** 2)
        amp = 0.55 + 0.50 * max(nuchal_peak * 1.1, thoracic_peak)
        length = 0.028 * (0.85 + 0.25 * amp)
        width = 0.012 * (0.80 + 0.25 * amp)
        height = 0.0095 * amp
        out_n = Vector((0.0, 0.98, 0.18)).normalized()
        fwd = Vector((0.0, 0.0, -1.0))
        right = Vector((1.0, 0.0, 0.0))
        _add_keeled_plate(b, loc, out_n, fwd, right, length, width, height, _body_weight_at_z(z), tilt_back=0.48)

    # 3. SERRATED DOUBLE-CRESTED CAUDAL SWIMMING SAIL (Tail1 .. Tail16)
    if tail_points is None:
        tail_points = [
            (0.0, 0.245, 0.49), (0.0146875, 0.245625, 0.57875), (0.035, 0.245, 0.70),
            (0.0578125, 0.2409375, 0.825), (0.08, 0.235, 0.96), (0.100625, 0.228125, 1.101875),
            (0.11, 0.22, 1.25), (0.095625, 0.2109375, 1.403125), (0.065, 0.20, 1.56),
            (0.016875, 0.18625, 1.720625), (-0.035, 0.17, 1.88), (-0.0775, 0.1509375, 2.03375),
            (-0.11, 0.13, 2.18), (-0.1271875, 0.106875, 2.315625), (-0.13, 0.085, 2.44),
            (-0.11125, 0.0671875, 2.56125), (-0.09, 0.055, 2.65),
        ]

    n_tail_scutes = 44
    n_tp = len(tail_points) - 1
    for k in range(n_tail_scutes):
        u = (k + 0.5) / n_tail_scutes
        flt = u * n_tp
        seg_i = min(n_tp - 1, int(flt))
        frac = flt - seg_i
        p0 = Vector(tail_points[max(0, seg_i - 1)])
        p1 = Vector(tail_points[seg_i])
        p2 = Vector(tail_points[min(n_tp, seg_i + 1)])
        p3 = Vector(tail_points[min(n_tp, seg_i + 2)])
        c = 0.5 * (
            (2.0 * p1)
            + (-p0 + p2) * frac
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * (frac * frac)
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * (frac * frac * frac)
        )
        tang = (p2 - p0).normalized()
        fwd = -tang
        up_v = Vector((0.0, 1.0, 0.0))
        right_v = fwd.cross(up_v).normalized()
        up_v = right_v.cross(fwd).normalized()

        # Cubic B-spline tail weights matching the tail skin
        w_vals = [
            ((1.0 - frac) ** 3) / 6.0,
            (3.0 * (frac ** 3) - 6.0 * (frac ** 2) + 4.0) / 6.0,
            (-3.0 * (frac ** 3) + 3.0 * (frac ** 2) + 3.0 * frac + 1.0) / 6.0,
            (frac ** 3) / 6.0,
        ]
        ws = {}
        for idx_off, w_val in zip([seg_i - 1, seg_i, seg_i + 1, seg_i + 2], w_vals):
            bname = 'Tail' + str(max(1, min(n_tp, idx_off + 1)))
            ws[bname] = ws.get(bname, 0.0) + w_val

        taper = (1.0 - u) ** 0.75
        if u < 0.54:
            # Paired double-crest swimming keels converging toward mid-tail
            sep = 0.014 * (1.0 - (u / 0.54) ** 0.85) + 0.0025
            for side in [-1.0, 1.0]:
                probe_x = c.x + right_v.x * (side * sep)
                probe_z = c.z + right_v.z * (side * sep)
                loc, _ = project_dorsal(probe_x, probe_z, start_y=c.y + 0.25)
                if loc is None:
                    continue
                out_n = (up_v + right_v * (side * 0.18)).normalized()
                _add_keeled_plate(
                    b,
                    loc,
                    out_n,
                    fwd,
                    right_v,
                    length=0.036 * taper + 0.008,
                    width=0.0085 * taper + 0.002,
                    height=0.0095 * (0.7 + 0.5 * math.sin(math.pi * u / 0.54)),
                    weights=ws,
                    tilt_back=0.52,
                )
        else:
            # Fused single serrated caudal whip-blade crest
            loc, _ = project_dorsal(c.x, c.z, start_y=c.y + 0.25)
            if loc is None:
                continue
            blade_env = math.sin(math.pi * ((u - 0.50) / 0.50)) ** 0.65
            _add_keeled_plate(
                b,
                loc,
                up_v,
                fwd,
                right_v,
                length=0.034 * taper + 0.008,
                width=0.0075 * taper + 0.0018,
                height=0.0115 * blade_env + 0.0025,
                weights=ws,
                tilt_back=0.55,
            )

    # 4. ORGANIZED LONGITUDINAL DORSOLATERAL ROWS OF KEELED OSTEODERM ROSETTES
    # 5 longitudinal rows per side from neck (-0.74) across torso and caudofemoral tail base (+0.85)
    for row_idx in range(1, 6):
        n_along = 32 if row_idx <= 3 else 24
        z_start = -0.74 if row_idx <= 3 else -0.42
        z_end = 0.82 if row_idx <= 2 else 0.44
        for step in range(n_along):
            u = (step + 0.5 * (row_idx % 2)) / n_along
            if u > 1.0:
                continue
            z = z_start + u * (z_end - z_start)
            # Angular offset from dorsal midline (18 deg to 68 deg)
            theta = math.radians(16.0 * row_idx + 2.5 * math.sin(step * 0.9))
            for side in [-1.0, 1.0]:
                ray_dir = Vector((-side * math.sin(theta), -math.cos(theta), 0.0)).normalized()
                ray_orig = Vector((0.0, 0.27, z)) - ray_dir * 0.45
                loc, norm = bvh.ray_cast(ray_orig, ray_dir, 0.50)[:2]
                if loc is None or loc.y < 0.14:
                    continue
                if z <= 0.46:
                    ws = _body_weight_at_z(z)
                else:
                    t_idx = max(1, min(4, int((z - 0.46) / 0.12) + 1))
                    ws = {'Tail' + str(t_idx): 1.0}
                out_n = norm.normalized()
                fwd = Vector((0.0, 0.0, -1.0))
                fwd = (fwd - out_n * fwd.dot(out_n)).normalized()
                right = fwd.cross(out_n).normalized()
                sz = 1.0 - 0.10 * row_idx
                _add_keeled_plate(
                    b,
                    loc,
                    out_n,
                    fwd,
                    right,
                    length=0.022 * sz,
                    width=0.012 * sz,
                    height=0.0038 * sz,
                    weights=ws,
                    tilt_back=0.30,
                )

    return b.build()
