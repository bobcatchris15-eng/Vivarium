"""Deterministic canopy guardian authoring for the Brazilian Prehensile-Tailed Porcupine (quilled_arboreal).
Authored +Y up/-Z forward.

All geometry and textures are real exported mesh/PBR data.
Run with Blender 5.2 --background --python scripts/guardians/generate_porcupine.py.
"""
import bpy, math, random, json, struct, sys
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
ROOT = Path(__file__).resolve().parents[2]
REPORT = ROOT / '.clanker/tasks/guardian-porcupine-solo/v7-authoring'
TAU = math.tau

class Surface:
    def __init__(self):
        self.v = []
        self.f = []
        self.uv = []
        self.w = []
        self.mi = []

    def vertex(self, p, uv=(0, 0), weights='Body'):
        i = len(self.v)
        self.v.append(tuple(p))
        self.uv.append(uv)
        self.w.append({weights: 1.0} if isinstance(weights, str) else weights)
        return i

    def face(self, indices, mat):
        self.f.append(tuple(indices))
        self.mi.append(mat)

    def ellipsoid(self, c, r, mat, bone, seg=24, rings=14):
        start = len(self.v)
        c = Vector(c)
        for j in range(rings + 1):
            t = math.pi * j / rings
            for k in range(seg + 1):
                a = TAU * k / seg
                pt = c + Vector((r[0] * math.sin(t) * math.cos(a), r[1] * math.cos(t), r[2] * math.sin(t) * math.sin(a)))
                self.vertex(pt, (k / seg, j / rings), bone)
        for j in range(rings):
            for k in range(seg):
                a = start + j * (seg + 1) + k
                if j == 0:
                    self.face((a + 1, a + seg + 2, a + seg + 1), mat)
                elif j == rings - 1:
                    self.face((a, a + 1, a + seg + 2), mat)
                else:
                    self.face((a, a + 1, a + seg + 2, a + seg + 1), mat)

    def tube(self, points, radii, mat, weights, seg=12):
        pts = list(map(Vector, points))
        rows = []
        for j, p in enumerate(pts):
            tangent = (pts[min(j + 1, len(pts) - 1)] - pts[max(j - 1, 0)]).normalized()
            axis = Vector((1, 0, 0))
            axis = (axis - tangent * axis.dot(tangent)).normalized()
            if axis.length < 0.1:
                axis = Vector((0, 0, 1)).cross(tangent).normalized()
            other = tangent.cross(axis).normalized()
            row = []
            rr = radii[j]
            rx, ry = (rr, rr) if isinstance(rr, (int, float)) else rr
            w = weights[j] if isinstance(weights, list) else weights
            for k in range(seg + 1):
                a = TAU * k / seg
                row.append(self.vertex(p + axis * (math.cos(a) * rx) + other * (math.sin(a) * ry), (k / seg, j / max(1, len(pts) - 1)), w))
            rows.append(row)
        for j in range(len(rows) - 1):
            for k in range(seg):
                self.face((rows[j][k], rows[j][k + 1], rows[j + 1][k + 1], rows[j + 1][k]), mat)
        self.face(tuple(reversed(rows[0][:-1])), mat)
        self.face(rows[-1][:-1], mat)

    def fur_ribbon(self, root, end, width, normal, weights, phase):
        root = Vector(root)
        end = Vector(end)
        n = Vector(normal).normalized()
        direction = end - root
        cross = direction.cross(n).normalized()
        rows = []
        for j in range(3):
            t = j / 2
            center = root + direction * t + n * (math.sin(t * math.pi) * 0.0015) + cross * (math.sin(t * math.pi) * width * 0.25)
            w = width * (0.52 if j == 0 else (0.85 if j == 1 else 0.015))
            rows.append([self.vertex(center + cross * w * side, ((phase + side * 0.045) % 1.0, t), weights) for side in (-1, 1)])
        for j in range(2):
            self.face((rows[j][0], rows[j][1], rows[j + 1][1], rows[j + 1][0]), 0)

    def object(self, name, mats, rig):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.collection.objects.link(ob)
        for mat in mats:
            me.materials.append(mat)
        uv = me.uv_layers.new(name='SurfaceUV')
        for p, mat in zip(me.polygons, self.mi):
            p.material_index = mat
            p.use_smooth = True
            for li in p.loop_indices:
                uv.data[li].uv = self.uv[me.loops[li].vertex_index]
        for b in rig.data.bones:
            ob.vertex_groups.new(name=b.name)
        for i, w in enumerate(self.w):
            for bname, val in w.items():
                ob.vertex_groups[bname].add([i], val, 'REPLACE')
        grouped = {}
        for v in me.vertices:
            key = tuple(round(c, 7) for c in v.co)
            grouped.setdefault(key, []).append(v.index)
        normals = [v.normal.copy() for v in me.vertices]
        for ids in grouped.values():
            if len(ids) > 1:
                n = sum((normals[i] for i in ids), Vector()).normalized()
                for i in ids:
                    normals[i] = n
        me.normals_split_custom_set_from_vertices(normals)
        ob.parent = rig
        mod = ob.modifiers.new('AnatomicalSkin', 'ARMATURE')
        mod.object = rig
        return ob

TAIL_POINTS = [Vector(p) for p in [(0, .26, .33), (0, .23, .45), (0, .21, .57), (0, .21, .69), (0, .25, .80), (0, .315, .86), (0, .375, .84), (0, .398, .765)]]

def tail_center(u):
    f = max(0.0, min(1.0, u)) * 7.0
    j = min(6, int(f))
    t = f - j
    p0 = TAIL_POINTS[max(0, j - 1)]
    p1 = TAIL_POINTS[j]
    p2 = TAIL_POINTS[j + 1]
    p3 = TAIL_POINTS[min(7, j + 2)]
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)

def tail_weights(u):
    f = u * 7.0 - 0.5
    if f <= 0:
        return {'Tail1': 1.0}
    if f >= 6:
        return {'Tail7': 1.0}
    j = int(f)
    t = f - j
    t = t * t * (3.0 - 2.0 * t)
    return {'Tail' + str(j + 1): 1.0 - t, 'Tail' + str(j + 2): t}

def torso_weights(z):
    chest = max(0.0, min(1.0, (-z + 0.015) / 0.23))
    lumbar = max(0.0, min(1.0, (z - 0.025) / 0.24))
    return {k: v for k, v in {'Body': 1.0 - chest - lumbar, 'Chest': chest, 'Lumbar': lumbar}.items() if v > 0}

def skull_weights(z):
    head = max(0.0, min(1.0, (-z - 0.25) / 0.12))
    w = {k: v * (1.0 - head) for k, v in torso_weights(z).items()}
    if head > 0:
        w['Head'] = head
    return w

def body_loft(surface, profile, mat):
    seg = 36
    samples = []
    for j in range(len(profile) - 1):
        p0 = profile[max(0, j - 1)]
        p1 = profile[j]
        p2 = profile[j + 1]
        p3 = profile[min(len(profile) - 1, j + 2)]
        for k in range(5):
            t = k / 5.0
            sample = [0.5 * ((2 * p1[d]) + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t * t + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t * t * t) for d in range(4)]
            samples.append(sample)
    samples.append(profile[-1])
    rows = []
    for j, (z, y, rx, ry) in enumerate(samples):
        weights = skull_weights(z)
        row = []
        for k in range(seg + 1):
            a = TAU * k / seg
            x = math.sin(a) * max(0.004, rx)
            yy = y + math.cos(a) * max(0.004, ry)
            row.append(surface.vertex((x, yy, z), (k / seg, j / (len(samples) - 1)), weights))
        rows.append(row)
    for j in range(len(rows) - 1):
        for k in range(seg):
            surface.face((rows[j][k], rows[j + 1][k], rows[j + 1][k + 1], rows[j][k + 1]), mat)
    surface.face(rows[0][:-1], mat)
    surface.face(tuple(reversed(rows[-1][:-1])), mat)
    return samples

def cinematic_limbs(s, limbs):
    for names, h, k, a, toe, side in limbs:
        upper, lower, foot = names
        pts = []
        rr = []
        ww = []
        for j in range(17):
            t = j / 16.0
            if t <= 0.5:
                q = t * 2.0
                point = h.lerp(k, q)
                radius = 0.067 * (1.0 - q) + 0.033 * q
                blend = max(0.0, min(1.0, (q - 0.72) / 0.28)) * 0.5
                w = {upper: 1.0 - blend, lower: blend}
                if q < 0.26:
                    w = {'Body': 0.65 * (1.0 - q / 0.26), upper: 1.0 - 0.65 * (1.0 - q / 0.26)}
            else:
                q = (t - 0.5) * 2.0
                point = k.lerp(a, q)
                radius = 0.033 * (1.0 - q) + 0.021 * q
                blend = 0.5 + 0.5 * min(1.0, q / 0.3)
                w = {upper: 1.0 - blend, lower: blend}
                if q > 0.78:
                    w = {lower: 1.0 - 0.35 * (q - 0.78) / 0.22, foot: 0.35 * (q - 0.78) / 0.22}
            if 0.25 < t < 0.75:
                q = (t - 0.25) / 0.5
                start = h.lerp(k, 0.5)
                stop = k.lerp(a, 0.5)
                point = (1.0 - q) ** 2 * start + 2.0 * (1.0 - q) * q * k + q * q * stop
                radius = 0.027 + 0.023 * (1.0 - q) ** 1.3 + 0.002 * math.sin(q * math.pi)
            if 'Body' in w:
                body = w.pop('Body')
                for n, v in torso_weights(h.z).items():
                    w[n] = w.get(n, 0.0) + body * v
            pts.append(point)
            rr.append((radius, radius * 0.98))
            ww.append(w)
        s.tube(pts, rr, 0, ww, 20)

        # Dense short limb fur
        fur_rng = random.Random(177 + int(side) + sum(map(ord, upper)))
        for j in range(1200):
            t = fur_rng.uniform(0.02, 0.94) * 16.0
            i = min(15, int(t))
            f = t - i
            point = pts[i].lerp(pts[i + 1], f)
            radius = rr[i][0] * (1.0 - f) + rr[i + 1][0] * f
            tangent = (pts[i + 1] - pts[i]).normalized()
            axis = Vector((1, 0, 0))
            axis = (axis - tangent * axis.dot(tangent)).normalized()
            other = tangent.cross(axis)
            angle = fur_rng.uniform(0, TAU)
            n = axis * math.cos(angle) + other * math.sin(angle)
            root = point + n * (radius - 0.0004)
            end = root + tangent * fur_rng.uniform(0.005, 0.012) + n * 0.002
            weights = {k: ww[i].get(k, 0.0) * (1.0 - f) + ww[i + 1].get(k, 0.0) * f for k in set(ww[i]) | set(ww[i + 1])}
            s.fur_ribbon(root, end, fur_rng.uniform(0.0008, 0.0015), n, weights, fur_rng.random())

        # Palm / Sole grasping pad
        s.ellipsoid(toe.lerp(a, 0.46), (0.040, 0.016, 0.032), 9, foot, 18, 10)

        # 4 distinct arboreal grasping digits with prominent hooked claws
        for d in range(4):
            x = (d - 1.5) * 0.022
            base = Vector((a.x + x * 0.35, 0.025, a.z - 0.012))
            mid = Vector((a.x + x * 1.05, 0.021, a.z - 0.048))
            end = Vector((a.x + x * 1.30, 0.019, a.z - 0.078 + abs(x) * 0.35))
            # Fleshy digit segments (Dark charcoal leathery climbing skin)
            s.tube([base, mid, end], [0.0085, 0.0068, 0.0045], 9, foot, 8)
            # Curved sickle claw hooked downward for grasping branches
            claw_base = end
            claw_mid = end + Vector((0, 0.006, -0.012))
            claw_tip = end + Vector((0, -0.010, -0.026))
            s.tube([claw_base, claw_mid, claw_tip], [0.0045, 0.0032, 0.0006], 4, foot, 7)

def porcupine(s, m, limbs):
    # Hunched, compact, scansorial torso profile matching reference photos
    profile = [
        (-.575, .276, .050, .044),
        (-.545, .282, .068, .058),
        (-.500, .294, .100, .084),
        (-.435, .320, .134, .120),
        (-.330, .348, .164, .146),
        (-.180, .368, .188, .172),
        (-.020, .375, .200, .185),
        (.140, .358, .190, .176),
        (.270, .318, .155, .140),
        (.360, .265, .078, .070)
    ]
    samples = body_loft(s, profile, 0)

    def body_at(z):
        if z <= samples[0][0]:
            return samples[0][1:]
        for a, b in zip(samples, samples[1:]):
            if a[0] <= z <= b[0]:
                t = (z - a[0]) / (b[0] - a[0])
                return tuple(a[i] * (1.0 - t) + b[i] * t for i in range(1, 4))
        return samples[-1][1:]

    def face_x(y, z):
        cy, rx, ry = body_at(z)
        return rx * math.sqrt(max(0.025, 1.0 - ((y - cy) / ry) ** 2))

    # =========================================================================
    # 1. ICONIC BULBOUS RHINARIUM (The prominent fleshy pink snout)
    # =========================================================================
    nose_center = (0.0, 0.274, -0.588)
    nose_rx, nose_ry, nose_rz = 0.060, 0.048, 0.056
    # Main fleshy bulb
    s.ellipsoid(nose_center, (nose_rx, nose_ry, nose_rz), 2, 'Head', seg=32, rings=18)

    # Nostrils: two recessed openings on the lower-front curve of the bulbous nose
    for side in (-1, 1):
        nx = side * 0.021
        ny = 0.256
        nz = -0.636
        # Recessed dark interior bowl
        s.ellipsoid((nx, ny, nz + 0.004), (0.0075, 0.0058, 0.0068), 4, 'Head', seg=16, rings=10)
        # Raised fleshy nostril margin rim
        rim_pts = []
        for k in range(17):
            a = TAU * k / 16.0
            rx = math.cos(a) * 0.0075
            ry = math.sin(a) * 0.0058
            rim_pts.append((nx + rx, ny + ry, nz + 0.001))
        s.tube(rim_pts, [0.0018] * len(rim_pts), 2, 'Head', 6)

    # Upper and lower lip contours beneath bulbous nose
    s.tube([(-.022, .248, -.576), (0, .247, -.588), (.022, .248, -.576)], [.004, .0055, .004], 2, 'Head', 10)
    s.ellipsoid((0, .238, -.552), (.036, .017, .036), 0, 'Jaw', 24, 12)
    # Concealed incisors exposed slightly during feeding
    for side in (-1, 1):
        s.ellipsoid((side * .005, .254, -.579), (.0038, .0075, .0025), 5, 'Head', 12, 8)

    # =========================================================================
    # 2. EYES AND MOBILE EYELIDS
    # =========================================================================
    eye_y = 0.320
    eye_z = -0.440
    for side in (-1, 1):
        x = face_x(eye_y, eye_z)
        # Dark orbital rim
        s.ellipsoid((side * (x - 0.001), eye_y, eye_z), (0.0028, 0.0080, 0.0086), 4, 'Head', 24, 14)
        # Wet glossy black eye globe
        s.ellipsoid((side * (x + 0.0012), eye_y, eye_z - 0.0003), (0.0012, 0.0052, 0.0055), 3, 'Head', 24, 12)
        # Eyelid margins
        for upper in (True, False):
            pts = []
            for k in range(17):
                a = (0.0 if upper else math.pi) + math.pi * k / 16.0
                y = eye_y + math.sin(a) * (0.0090 if upper else 0.0082)
                z = eye_z + math.cos(a) * 0.0095
                pts.append((side * (face_x(y, z) + 0.0015), y, z))
            s.tube(pts, [0.0016 if upper else 0.0011] * len(pts), 0, 'Head', 7)

    # Skin eyelids weighted to LidUpper and LidLower
    for side, label in ((1, 'L'), (-1, 'R')):
        for upper in (True, False):
            grid = []
            sign = 1 if upper else -1
            bone = 'Lid' + ('Upper' if upper else 'Lower') + '_' + label
            for j in range(11):
                t = j / 10.0
                row = []
                for k in range(49):
                    a = math.pi * k / 48.0
                    z = eye_z + 0.0096 * math.cos(a)
                    y = eye_y + sign * ((1.0 - t) * 0.0102 * math.sin(a) + t * 0.004)
                    x = face_x(y, z) + 0.0018 + 0.0016 * math.sin(a) * t
                    row.append(s.vertex((side * x, y, z), (k / 48.0, t), {'Head': 1.0 - t, bone: t}))
                grid.append(row)
            for j in range(10):
                for k in range(48):
                    q = (grid[j][k], grid[j + 1][k], grid[j + 1][k + 1], grid[j][k + 1])
                    s.face(q if (side == 1) == upper else tuple(reversed(q)), 0)

    # Cupped ears
    for side in (-1, 1):
        ear_anchor = face_x(0.345, -0.370) - 0.003
        s.ellipsoid((side * ear_anchor, 0.345, -0.370), (0.004, 0.016, 0.013), 2, 'Head', 18, 12)

    # =========================================================================
    # 3. LONG PROMINENT BLACK WHISKERS (Vibrissae)
    # =========================================================================
    # 70 long black whiskers in 5 rows fanning out wide and radiating around the snout
    whisker_rng = random.Random(1992)
    for side in (-1, 1):
        for row in range(5):
            for col in range(7):
                origin_z = -0.588 + col * 0.007 + whisker_rng.uniform(-0.001, 0.001)
                origin_y = 0.252 + row * 0.0065 + whisker_rng.uniform(-0.001, 0.001)
                origin_x = side * (face_x(origin_y, origin_z) + 0.002)
                base = Vector((origin_x, origin_y, origin_z))
                # Radiating fan: upper whiskers fan slightly up, lower whiskers droop down
                fan_y = (row - 2.0) * 0.012 - (col * 0.004)
                spread_x = side * (0.075 + col * 0.011 + row * 0.005)
                sweep_z = 0.020 + col * 0.010 + row * 0.004
                end = base + Vector((spread_x, fan_y, sweep_z))
                mid = base.lerp(end, 0.40) + Vector((side * 0.008, fan_y * 0.35 - 0.004, -0.003))
                s.tube([base, mid, end], [0.00065, 0.00042, 0.00010], 5, 'Head', 5)

    # Limbs, paws, and hooked claws
    cinematic_limbs(s, limbs)

    # Tail tube (Dark charcoal leathery climbing skin)
    pts = [tail_center(i / 84.0) for i in range(85)]
    rr = [0.055 * (1.0 - i / 96.0) ** 1.3 + 0.004 for i in range(85)]
    s.tube(pts, rr, 9, [tail_weights(i / 84.0) for i in range(85)], 16)

    # =========================================================================
    # 4. CROWN & FOREHEAD SPINES (Zone A: High-Contrast Spiky Crest)
    # =========================================================================
    crown_rng = random.Random(2026)
    # Dense spines starting right above the bulbous nose (z = -0.582) onto the forehead, cheeks, and crown
    for j in range(46):
        z = -0.582 + j * ((-0.330 - -0.582) / 45.0)
        cy, rx, ry = body_at(z)
        # Wider arc covering forehead and upper cheeks down toward the whiskers
        arc = 1.82 if z < -0.48 else (1.88 if z < -0.42 else 1.95)
        count = 38 if z < -0.48 else 46
        for k in range(count):
            angle = -arc + k * (2.0 * arc / (count - 1)) + crown_rng.uniform(-0.04, 0.04)
            pz = z + crown_rng.uniform(-0.003, 0.003)
            cy, rx, ry = body_at(pz)
            px = math.sin(angle) * rx
            py = cy + math.cos(angle) * ry
            # Skip eye sockets
            if abs(abs(px) - face_x(eye_y, eye_z)) < 0.013 and abs(py - eye_y) < 0.013 and abs(pz - eye_z) < 0.014:
                continue
            # Direction:
            # - Above nose & anterior forehead (pz < -0.46): erect, pointing UPWARDS (high Y) and forward (-Z)
            # - Crown (pz >= -0.46): pointing upward and radiating back along skull
            if pz < -0.46:
                normal = Vector((math.sin(angle) * 0.58, max(0.85, math.cos(angle) * 0.95), -0.32 + (pz + 0.582) * 1.5)).normalized()
                length = crown_rng.uniform(0.022, 0.036) if pz < -0.52 else crown_rng.uniform(0.035, 0.052)
            else:
                normal = Vector((math.sin(angle) * 0.65, math.cos(angle) * 0.75, 0.40)).normalized()
                length = crown_rng.uniform(0.048, 0.070)
            p = Vector((px, py, pz)) - normal * 0.0015
            end = p + normal * length + Vector((0, 0.002, 0.003))
            weights = skull_weights(pz)
            s.tube([p, p + normal * length * 0.35, end], [0.0016, 0.0011, 0.00008], 7, weights, 4)

    # =========================================================================
    # 5. MAIN DORSAL & FLANK QUILLS (Zone B: Thousands of Banded Needles)
    # =========================================================================
    dorsal_rng = random.Random(4044)
    # 56 rows along the spine, 54 quills per row across dorsal arc = 3,024 quills
    for j in range(56):
        z = -0.330 + j * ((0.270 - -0.330) / 55.0)
        t = (z + 0.330) / 0.600
        cy, rx, ry = body_at(z)
        count = 54
        for k in range(count):
            angle = -2.15 + k * (4.30 / (count - 1)) + dorsal_rng.uniform(-0.04, 0.04)
            pz = z + dorsal_rng.uniform(-0.004, 0.004)
            cy, rx, ry = body_at(pz)
            px = math.sin(angle) * rx
            py = cy + math.cos(angle) * ry
            # Swept backward along spine with natural outward fan
            normal = Vector((math.sin(angle) * 0.58, max(0.12, math.cos(angle) * 0.68), 0.70)).normalized()
            p = Vector((px, py, pz)) - normal * 0.002
            # Longest quills along dorsal ridge, medium along flanks
            if abs(angle) < 0.65:
                length = dorsal_rng.uniform(0.068, 0.100)
            elif abs(angle) < 1.35:
                length = dorsal_rng.uniform(0.050, 0.076)
            else:
                length = dorsal_rng.uniform(0.036, 0.056)
            end = p + normal * length + Vector((0, -0.008, 0.016 * t))
            weights = skull_weights(pz)
            quill_mat = 8 if abs(angle) > 1.35 else 1
            s.tube([p, p + normal * length * 0.30, p.lerp(end, 0.68), end], [dorsal_rng.uniform(0.0018, 0.0026), 0.0018, 0.0010, 0.00008], quill_mat, weights, 4)

    # =========================================================================
    # 6. TAIL BASE QUILLS (Zone C: Quilled proximal prehensile tail)
    # =========================================================================
    tail_quill_rng = random.Random(7788)
    for j in range(28):
        u = j * (0.45 / 27.0)
        origin = tail_center(u)
        r = 0.055 * (1.0 - (u * 84.0) / 96.0) ** 1.3 + 0.004
        tangent = (tail_center(min(1.0, u + 0.02)) - tail_center(max(0.0, u - 0.02))).normalized()
        axis = Vector((1, 0, 0))
        axis = (axis - tangent * axis.dot(tangent)).normalized()
        up = tangent.cross(axis)
        count = 18
        for k in range(count):
            a = -1.65 + k * (3.30 / (count - 1)) + tail_quill_rng.uniform(-0.05, 0.05)
            n = axis * math.sin(a) + up * math.cos(a)
            p = origin + n * (r - 0.001)
            length = tail_quill_rng.uniform(0.028, 0.050) * (1.0 - u * 1.2)
            end = p + tangent * length + n * 0.004
            weights = tail_weights(u)
            s.tube([p, p.lerp(end, 0.5), end], [0.0016, 0.0010, 0.00008], 1, weights, 4)

    # =========================================================================
    # 7. DENSE DARK UNDERFUR PELT (Zone D: High-contrast backdrop)
    # =========================================================================
    underfur_rng = random.Random(9933)
    # Head and throat underfur
    for j in range(4500):
        z = underfur_rng.uniform(-0.540, -0.320)
        a = underfur_rng.uniform(-math.pi, math.pi)
        cy, rx, ry = body_at(z)
        px = math.sin(a) * rx
        py = cy + math.cos(a) * ry
        if abs(abs(px) - face_x(eye_y, eye_z)) < 0.018 and abs(py - eye_y) < 0.018 and abs(z - eye_z) < 0.020:
            continue
        n = Vector((math.sin(a), math.cos(a), 0))
        p = Vector((px, py, z)) - n * 0.0003
        length = underfur_rng.uniform(0.009, 0.020)
        end = p + n * underfur_rng.uniform(0.0015, 0.0035) + Vector((underfur_rng.uniform(-0.002, 0.002), -0.002, -length))
        s.fur_ribbon(p, end, underfur_rng.uniform(0.0011, 0.0021), n, skull_weights(z), underfur_rng.random())

    # Torso and belly underfur
    for i in range(12000):
        z = underfur_rng.uniform(-0.310, 0.355)
        a = underfur_rng.uniform(-math.pi, math.pi)
        cy, rx, ry = body_at(z)
        n = Vector((math.sin(a), math.cos(a), 0))
        p = Vector((math.sin(a) * rx, cy + math.cos(a) * ry, z)) - n * 0.0004
        length = underfur_rng.uniform(0.015, 0.035)
        end = p + n * underfur_rng.uniform(0.004, 0.010) + Vector((underfur_rng.uniform(-0.004, 0.004), -0.002, length))
        s.fur_ribbon(p, end, underfur_rng.uniform(0.0012, 0.0022), n, skull_weights(z), underfur_rng.random())

def aim_bone(rig, name, h, t):
    pb = rig.pose.bones[name]
    rest = pb.bone
    q = (rest.tail_local - rest.head_local).rotation_difference(Vector(t) - Vector(h))
    pb.matrix = Matrix.Translation(Vector(h)) @ q.to_matrix().to_4x4() @ rest.matrix_local.to_3x3().to_4x4()

def solve_limb(rig, limb, phase, walking, stride, feeding=False):
    names, h, k, a, toe, side = limb
    upper, lower, foot = names
    p = phase % 1.0
    target = a.copy()
    if feeding and upper == 'FrontUpper_L':
        lift = 0.5 - 0.5 * math.cos(phase * TAU)
        target = a.lerp(Vector((side * 0.075, 0.178, -0.425)), lift)
        target.x += 0.005 * math.sin(phase * TAU * 2.0) * lift
    if walking:
        if p < 0.68:
            target.z += stride * (p / 0.68 - 0.5)
            target.y = a.y
        else:
            q = (p - 0.68) / 0.32
            target.z += stride * (0.5 - q)
            target.y += 0.028 * math.sin(q * math.pi)
    l1 = (k - h).length
    l2 = (a - k).length
    delta = target - h
    dist = min(delta.length, l1 + l2 - 0.00001)
    direction = delta.normalized()
    target = h + direction * dist
    along = (l1 * l1 - l2 * l2 + dist * dist) / (2.0 * dist)
    height = math.sqrt(max(0.0, l1 * l1 - along * along))
    pole = k - h
    pole = (pole - direction * pole.dot(direction)).normalized()
    knee = h + direction * along + pole * height
    aim_bone(rig, upper, h, knee)
    bpy.context.view_layer.update()
    aim_bone(rig, lower, knee, target)
    bpy.context.view_layer.update()
    aim_bone(rig, foot, target, target + (toe - a))
    bpy.context.view_layer.update()

def rig_for(species):
    defs = {}
    def add(name, h, t, parent='Root'):
        defs[name] = (h, t, parent)
    add('Root', (0, 0, 0), (0, .1, 0), None)
    add('Body', (0, .27, .18), (0, .30, -.12))
    add('Chest', (0, .32, -.19), (0, .33, -.04), 'Body')
    add('Lumbar', (0, .32, .08), (0, .28, .30), 'Body')
    add('Neck', (0, .30, -.19), (0, .45, -.37), 'Body')
    add('Head', (0, .29, -.31), (0, .30, -.48), 'Chest')
    add('Jaw', (0, .22, -.45), (0, .23, -.55), 'Head')
    pts = [tail_center(i / 7.0) for i in range(8)]
    for side in ('L', 'R'):
        for lid in ('Upper', 'Lower'):
            add('Lid' + lid + '_' + side, (0, .320, -.440), (0, .340, -.440), 'Head')
    for i in range(7):
        add('Tail' + str(i + 1), pts[i], pts[i + 1], 'Lumbar' if i == 0 else 'Tail' + str(i))
    limbs = []
    for s, n in ((1, 'L'), (-1, 'R')):
        specs = [
            ('Front', (s * .145, .29, -.15), (s * .19, .16, -.12), (s * .18, .036, -.23), (s * .18, .027, -.29)),
            ('Hind', (s * .14, .26, .22), (s * .19, .14, .16), (s * .18, .036, .27), (s * .18, .027, .21))
        ]
        for role, h, k, a, toe in specs:
            u = role + 'Upper_' + n
            l = role + 'Lower_' + n
            f = role + 'Foot_' + n
            add(u, h, k, 'Chest' if role == 'Front' else 'Lumbar')
            add(l, k, a, u)
            add(f, a, toe, l)
            limbs.append(((u, l, f), Vector(h), Vector(k), Vector(a), Vector(toe), s))

    arm = bpy.data.armatures.new('GuardianArmature')
    rig = bpy.data.objects.new('Armature', arm)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (h, t, parent) in defs.items():
        eb = arm.edit_bones.new(name)
        eb.head = h
        eb.tail = t
        if parent:
            eb.parent = arm.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig, limbs

def animations(rig, limbs, species):
    rig.animation_data_create()
    for name in ('idle', 'walk', 'feed', 'rest'):
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        rig.animation_data.action = act
        end = 46 if name == 'walk' else 121
        for frame in range(1, end + 1):
            phase = 0.0 if frame == end else (frame - 1) / (end - 1)
            wave = math.sin(phase * TAU)
            for pb in rig.pose.bones:
                pb.location = (0, 0, 0)
                pb.rotation_quaternion = (1, 0, 0, 0)
                pb.scale = (1, 1, 1)
            bpy.context.view_layer.update()

            def rotate(bone, axis, angle):
                pb = rig.pose.bones[bone]
                local = pb.bone.matrix_local.to_3x3().inverted() @ Vector(axis)
                pb.rotation_quaternion = Quaternion(local, angle)

            rotate('Head', (0, 1, 0), 0.035 * wave if name in ('idle', 'walk') else 0.014 * wave)
            if name == 'feed':
                rotate('Head', (1, 0, 0), -0.20 + 0.018 * wave)
                rotate('Jaw', (1, 0, 0), -0.14 * (0.5 + 0.5 * math.sin(phase * TAU * 3.0)))
            if name == 'rest':
                rotate('Head', (1, 0, 0), -0.065 + 0.01 * wave)
            rotate('Chest', (1, 0, 0), (0.016 if name == 'walk' else 0.004) * wave)
            rotate('Lumbar', (1, 0, 0), (-0.013 if name == 'walk' else -0.003) * wave)
            rig.pose.bones['Chest'].location = rig.pose.bones['Chest'].bone.matrix_local.to_3x3().inverted() @ Vector((0, 0.0008 * wave, 0))
            for i in range(1, 8):
                rotate('Tail' + str(i), (0, 1, 0), (0.004 + i * 0.0016) * math.sin(phase * TAU - i * 0.30))
                pb = rig.pose.bones['Tail' + str(i)]
                local = pb.bone.matrix_local.to_3x3().inverted() @ Vector((1, 0, 0))
                curl = (0.002 + i * 0.0015) * math.sin(phase * TAU - i * 0.25) + (0.008 if name == 'rest' and i > 3 else 0.0)
                pb.rotation_quaternion = pb.rotation_quaternion @ Quaternion(local, curl)

            closure = 1.0 if name == 'rest' else max(0.0, 1.0 - abs(phase - 0.75) / 0.045) if name == 'idle' else 0.0
            for side in ('L', 'R'):
                for lid, sign in (('Upper', -1), ('Lower', 1)):
                    pb = rig.pose.bones['Lid' + lid + '_' + side]
                    pb.location = pb.bone.matrix_local.to_3x3().inverted() @ Vector((0, sign * 0.0045 * closure, 0))

            for i, limb in enumerate(limbs):
                offset = [0.0, 0.50, 0.75, 0.25][i]
                solve_limb(rig, limb, phase + (offset if name == 'walk' else 0.0), name == 'walk', 0.0612, name == 'feed')

            for pb in rig.pose.bones:
                for channel in ('location', 'rotation_quaternion', 'scale'):
                    pb.keyframe_insert(channel, frame=frame)
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, act)
        strip.action_frame_start = 1
        strip.action_frame_end = end
        track.mute = True
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.scene.frame_set(1)

def audit(path, species, ob, rig):
    raw = path.read_bytes()
    length = struct.unpack_from('<I', raw, 12)[0]
    data = json.loads(raw[20:20 + length])
    primitives = [p for m in data.get('meshes', []) for p in m['primitives']]
    report = {
        'species': species,
        'axis_contract': 'Authored Y-up in Blender; standard glTF conversion; runtime +90X retained',
        'blender_vertices': len(ob.data.vertices),
        'blender_triangles': sum(len(p.vertices) - 2 for p in ob.data.polygons),
        'glb_vertices': sum(data['accessors'][p['attributes']['POSITION']]['count'] for p in primitives),
        'glb_triangles': sum(data['accessors'][p['indices']]['count'] // 3 for p in primitives),
        'joints': [len(s['joints']) for s in data.get('skins', [])],
        'clips': [a.get('name') for a in data.get('animations', [])],
        'images': len(data.get('images', [])),
        'materials': len(data.get('materials', [])),
        'bytes': len(raw),
        'uv_primitives': sum('TEXCOORD_0' in p['attributes'] for p in primitives),
        'primitive_count': len(primitives),
        'extensions_used': data.get('extensionsUsed', []),
        'nominal_walk_speed_m_s': 0.12,
        'walk_cycle_seconds': 0.75,
        'walk_stance_fraction': 0.68
    }
    (REPORT / (species + '-audit.json')).write_text(json.dumps(report, indent=2))
    print(json.dumps(report))

def create_pbr_material(name, color, rough=0.65, image_path=None, normal_path=None, normal_strength=0.30):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1.0)
    p.inputs['Roughness'].default_value = rough
    if image_path and Path(image_path).exists():
        im = bpy.data.images.load(str(image_path))
        im.pack()
        node = m.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = im
        m.node_tree.links.new(node.outputs['Color'], p.inputs['Base Color'])
    if normal_path and Path(normal_path).exists():
        norm_im = bpy.data.images.load(str(normal_path))
        norm_im.colorspace_settings.name = 'Non-Color'
        norm_im.pack()
        tex = m.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = norm_im
        n = m.node_tree.nodes.new('ShaderNodeNormalMap')
        n.inputs['Strength'].default_value = normal_strength
        m.node_tree.links.new(tex.outputs['Color'], n.inputs['Color'])
        m.node_tree.links.new(n.outputs['Normal'], p.inputs['Normal'])
    return m

def generate(species):
    assert species == 'quilled_arboreal', 'Porcupine-only authoring'
    bpy.ops.wm.read_factory_settings(use_empty=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    out = ROOT / 'game/assets/guardians' / species
    out.mkdir(parents=True, exist_ok=True)
    texdir = out / 'textures'
    texdir.mkdir(exist_ok=True)

    # 1. High-definition PBR textures are pre-authored into texdir

    quill_tex = texdir / 'quill.png'
    crown_quill_tex = texdir / 'quill_crown.png'
    flank_quill_tex = texdir / 'quill_flank.png'
    underfur_tex = texdir / 'underfur.png'
    nose_tex = texdir / 'nose_skin.png'
    nose_norm = texdir / 'nose_normal.png'
    surf_norm = texdir / 'surface-normal.png'
    paw_tex = texdir / 'paw_skin.png'

    # 2. Setup materials
    mats = [
        # 0: DenseUnderfur
        create_pbr_material('DenseUnderfur', (.06, .045, .036), .92, underfur_tex, surf_norm, .18),
        # 1: BandedQuills (Main dorsal and tail quills)
        create_pbr_material('BandedQuills', (.93, .90, .79), .38, quill_tex, surf_norm, .25),
        # 2: NoseEarsAndGripSkin (Bulbous rose-pink nose and ears)
        create_pbr_material('NoseEarsAndGripSkin', (.68, .44, .46), .58, nose_tex, nose_norm, .35),
        # 3: WetEyePupil
        create_pbr_material('WetEyePupil', (.004, .005, .005), .05),
        # 4: ClawsNostrilsAndMouth (Hooked climbing claws, nostril recesses)
        create_pbr_material('ClawsNostrilsAndMouth', (.04, .035, .03), .28),
        # 5: WhiskersAndIncisors (Long glossy black vibrissae)
        create_pbr_material('WhiskersAndIncisors', (.02, .02, .02), .18),
        # 6: WetChestnutIris
        create_pbr_material('WetChestnutIris', (.025, .018, .012), .08),
        # 7: BandedQuillsCrown (Crown and forehead quills)
        create_pbr_material('BandedQuillsCrown', (.95, .92, .82), .40, crown_quill_tex, surf_norm, .25),
        # 8: BandedQuillsFlank (Flank quills)
        create_pbr_material('BandedQuillsFlank', (.91, .88, .76), .40, flank_quill_tex, surf_norm, .25),
        # 9: PawAndGripSkin (Dark charcoal leathery climbing skin on paws and tactile tail)
        create_pbr_material('PawAndGripSkin', (.12, .11, .10), .65, paw_tex, surf_norm, .30)
    ]

    # 3. Build skeleton and geometry
    rig, limbs = rig_for(species)
    s = Surface()
    porcupine(s, mats, limbs)
    ob = s.object('PorcupineAnatomy', mats, rig)
    animations(rig, limbs, species)

    bpy.context.scene.render.fps = 60
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 121
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.gltf(
        filepath=str(out / 'guardian.glb'),
        export_format='GLB',
        use_selection=True,
        export_animations=True,
        export_animation_mode='ACTIONS',
        export_anim_slide_to_zero=True,
        export_skins=True,
        export_apply=False,
        export_yup=True,
        export_cameras=False,
        export_lights=False
    )
    audit(out / 'guardian.glb', species, ob, rig)
    bpy.ops.wm.save_as_mainfile(filepath=str(REPORT / (species + '-authoring.blend')))
    print('CANOPY_AUTHORING_COMPLETE ' + species)

if __name__ == '__main__':
    generate('quilled_arboreal')
