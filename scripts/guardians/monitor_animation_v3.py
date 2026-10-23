"""Paired curved skin lids and corneal iris/pupil for the Great Ground Guardian.

In the rest/bind pose (amount=0), the mobile upper and lower eyelids sit in their
open anatomical position framing the palpebral fissure so the eye is unobstructed.
During blinks (idle/walk/feed) and rest (amount->1), the lids glide over the
corneal dome to meet along the horizontal palpebral seam.
"""
import math
from mathutils import Vector, Quaternion

EYE_X = 0.086
EYE_Y = 0.326
EYE_Z = -1.006
EYE_R = 0.013


def lids(Builder, rig, mat):
    """Builds open-rest upper and lower mobile eyelids weighted to Blink_L/R and BlinkLower_L/R."""
    out_lids = []
    for side, suffix in [(-1, 'R'), (1, 'L')]:
        for upper, name in [(True, 'Blink_' + suffix), (False, 'BlinkLower_' + suffix)]:
            b = Builder('monitor_mobile_lid_' + name, rig, mat)
            seg = 36
            rings = 7
            sign = 1.0 if upper else -1.0
            # In open rest pose (amount=0), the lid margin sits at ~36 deg above/below
            # the horizontal eye equator, sweeping back to 82 deg inside the socket.
            for ring in range(rings):
                t = ring / (rings - 1)
                elev = math.radians(34.0 + 48.0 * t) * sign
                cy = math.sin(elev) * 0.0138
                r_horiz = math.cos(elev) * 0.0138
                for j in range(seg):
                    # Azimuth across the lateral outward hemisphere (-85 deg to +85 deg)
                    az = -math.pi * 0.48 + (math.pi * 0.96) * (j / (seg - 1))
                    x = side * (EYE_X + 0.0094 * math.cos(elev) * math.cos(az))
                    y = EYE_Y + cy
                    z = EYE_Z + r_horiz * math.sin(az) * 1.12
                    b.v.append((x, y, z))
                    b.uv.append((j / (seg - 1), t))
                    b.w.append({name: 1.0})
                    b.col.append((1.0, 1.0, 1.0, 1.0))
            for ring in range(rings - 1):
                for j in range(seg - 1):
                    a = ring * seg + j
                    q = a + 1
                    if (side == 1) == upper:
                        b.f.append((a, q, q + seg, a + seg))
                    else:
                        b.f.append((a, a + seg, q + seg, q))
            out_lids.append(b.build())
    return out_lids


def pose(rig, amount):
    """Glides the mobile upper and lower lids over the cornea when amount > 0."""
    amount = max(0.0, min(1.0, float(amount)))
    for suffix in ['L', 'R']:
        side = 1.0 if suffix == 'L' else -1.0
        for sign, name in [(1.0, 'Blink_' + suffix), (-1.0, 'BlinkLower_' + suffix)]:
            if name not in rig.pose.bones:
                continue
            pb = rig.pose.bones[name]
            # At amount=0 (open), offset is zero so rest pose has open eyes.
            # At amount=1 (closed), lids translate slightly outward over corneal apex and meet vertically.
            offset = Vector((side * 0.0018 * amount, -sign * 0.0068 * amount, 0.0))
            pb.location = rig.data.bones[name].matrix_local.to_3x3().inverted() @ offset


def closure(name, t):
    if name == 'rest':
        return 1.0
    center = {'idle': 0.64, 'feed': 0.78, 'walk': 0.84}.get(name, 0.64)
    d = t - center
    if -0.055 < d < 0.0:
        return (d + 0.055) / 0.055
    if 0.0 <= d < 0.035:
        return 1.0
    if 0.035 <= d < 0.11:
        return 1.0 - (d - 0.035) / 0.075
    return 0.0


def curved_eye(Builder, rig, mats, side):
    """Curved golden-amber iris and round dark pupil conforming to the corneal dome."""
    for kind, radius, depth in [('iris', 0.0076, 0.00022), ('pupil', 0.0040, 0.00038)]:
        mat_use = mats['iris' if kind == 'iris' else 'eye']
        b = Builder('monitor_' + kind + '_curved_' + str(side), rig, mat_use)
        seg = 48
        for ring in range(9):
            r = radius * max(0.04, ring / 8.0)
            for j in range(seg):
                angle = math.tau * j / seg
                y = r * math.sin(angle)
                z = r * math.cos(angle)
                dome = math.sqrt(max(0.0, 1.0 - (y / EYE_R) ** 2 - (z / EYE_R) ** 2))
                x = side * (EYE_X + 0.0085 * dome + depth)
                b.v.append((x, EYE_Y + y, EYE_Z + z))
                b.uv.append((0.5 + y / (2.0 * radius), 0.5 + z / (2.0 * radius)))
                b.w.append({'Head': 1.0})
                # Subtle limbal ring darkening at outer edge of iris
                if kind == 'iris':
                    radial_t = ring / 8.0
                    shade = 1.0 - 0.42 * (radial_t ** 2.4)
                    b.col.append((shade, shade * 0.92, shade * 0.75, 1.0))
                else:
                    b.col.append((1.0, 1.0, 1.0, 1.0))
        pole = len(b.v)
        b.v.append((side * (EYE_X + 0.0085 + depth), EYE_Y, EYE_Z))
        b.uv.append((0.5, 0.5))
        b.w.append({'Head': 1.0})
        b.col.append((1.0, 1.0, 1.0, 1.0))
        for j in range(seg):
            if side == 1:
                b.f.append((pole, j, (j + 1) % seg))
            else:
                b.f.append((pole, (j + 1) % seg, j))
        for ring in range(8):
            for j in range(seg):
                a = ring * seg + j
                q = ring * seg + (j + 1) % seg
                if side == 1:
                    b.f.append((a, q, q + seg, a + seg))
                else:
                    b.f.append((a, a + seg, q + seg, q))
        b.build()
