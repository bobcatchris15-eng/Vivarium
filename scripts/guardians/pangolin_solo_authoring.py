"""Bounded anatomical guardian authoring, image PBR, explicit skinning and audits.

Authored for Giant Ground Pangolin (Smutsia temminckii / Smutsia gigantea).
Coordinates remain X-right / Y-up / -Z-forward in Blender.
Runtime +90X root correction preserved.
"""
import bpy, bmesh, math, json, os, csv, shutil
from pathlib import Path
from mathutils import Vector, Matrix, kdtree
import numpy as np

TAU = math.tau
ROOT = Path(__file__).resolve().parents[2] if "__file__" in globals() else Path(".")
REPORT = Path('.clanker/tasks/guardian-pangolin-solo/v16-authoring')

def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.fps = 60

def srgb_to_linear(rgb):
    return np.where(rgb <= 0.04045, rgb / 12.92, ((np.maximum(rgb, 0) + 0.055) / 1.055) ** 2.4)

def image(name, pixels, directory, noncolor=False, encode_linear_color=False):
    h, w, _ = pixels.shape
    if encode_linear_color:
        pixels = pixels.copy()
        rgb = pixels[:, :, :3]
        pixels[:, :, :3] = np.where(rgb <= .0031308, 12.92 * rgb, 1.055 * np.maximum(rgb, 0) ** (1 / 2.4) - .055)
    im = bpy.data.images.new(name, width=w, height=h, alpha=True)
    if noncolor:
        im.colorspace_settings.name = 'Non-Color'
    im.pixels.foreach_set(pixels.astype(np.float32).ravel())
    filepath = str((directory / (name + '.png')).resolve())
    im.filepath_raw = filepath
    im.file_format = 'PNG'
    im.save()
    if encode_linear_color:
        path = im.filepath_raw
        bpy.data.images.remove(im)
        im = bpy.data.images.load(path, check_existing=False)
        im.name = name
        im.colorspace_settings.name = 'sRGB'
    im.pack()
    # Also write with guardian_ prefix for Godot GLTF compatibility
    if not name.startswith('guardian_'):
        alt_path = directory / ('guardian_' + name + '.png')
        try:
            shutil.copyfile(filepath, str(alt_path.resolve()))
        except Exception:
            pass
    return im

def pbr(name, directory, kind, base, rough=.5):
    """High-fidelity procedural PBR textures for authentic keratin, skin, and horn."""
    n = 2048 if kind in ('pangolin_keratin', 'snake', 'ventral') else 1024
    y, x = np.mgrid[0:n, 0:n] / n

    if kind == 'pangolin_keratin':
        cells_x, cells_y = 4, 4
        cell_col = np.floor(x * cells_x).astype(int)
        cell_row = np.floor(y * cells_y).astype(int)
        cell_id = cell_col + cell_row * cells_x

        u = (x * cells_x) % 1.0
        v = (y * cells_y) % 1.0

        rng = np.random.default_rng(2026)
        cell_seeds = rng.uniform(0.0, 100.0, (16,))
        # Rich natural variegation across scales matching reference photographs
        cell_tint = np.array([
            [1.00, 1.00, 1.00], [1.10, 1.04, 0.94], [0.92, 0.90, 0.94], [1.14, 1.06, 0.92],
            [0.96, 0.97, 1.02], [1.08, 1.03, 0.96], [0.90, 0.88, 0.92], [1.12, 1.05, 0.95],
            [1.04, 1.02, 0.98], [0.94, 0.92, 0.96], [1.08, 1.04, 0.96], [0.96, 0.94, 0.98],
            [1.15, 1.08, 0.94], [0.91, 0.89, 0.93], [1.06, 1.03, 0.97], [1.02, 1.00, 0.98]
        ])
        seed = cell_seeds[cell_id]

        s = (u - 0.5) * 2.0
        dist_center = np.abs(s)
        fan_u = s * (1.0 + 0.35 * v)

        # Primary longitudinal ridges (7-9 dominant keels/striae per scale)
        ridge_freq = 7.0 + (cell_id % 3) * 0.5
        primary_ridges = np.cos(fan_u * math.pi * ridge_freq) * 0.5 + 0.5
        primary_ridges = primary_ridges ** 2.2

        # Central keel ridge
        central_keel = np.exp(-((s * 3.2) ** 2)) * np.sin(v * math.pi * 0.95)

        # Fine micro-fibrous keratin striae (35-45 fine longitudinal fibers)
        fine_freq = 40.0 + (cell_id % 5) * 2.0
        fine_striae = np.sin(fan_u * math.pi * fine_freq + np.sin(v * 26.0 + seed) * 0.3) * 0.5 + 0.5

        # Growth increments / rings
        ring_curve = v - 0.16 * (s ** 2)
        growth_rings = np.sin(ring_curve * math.pi * 18.0 + seed) * 0.5 + 0.5

        # Worn trailing margin (top 18% of scale length)
        edge_wear = np.clip((v - 0.82) / 0.18, 0.0, 1.0) ** 1.5
        edge_notches = np.sin(s * math.pi * 7.0 + seed) * np.sin(s * math.pi * 17.0)
        edge_wear = np.clip(edge_wear + edge_wear * 0.20 * edge_notches, 0.0, 1.0)

        # Subtle arid savannah dust in deep recessed grooves
        dust_pattern = (1.0 - primary_ridges) ** 2 * np.clip((v - 0.15) / 0.70, 0.0, 1.0)
        dust_noise = np.sin(u * math.pi * 53.0 + v * math.pi * 37.0 + seed) * 0.5 + 0.5
        dust = np.clip(dust_pattern * (0.65 + 0.35 * dust_noise) * (1.0 - edge_wear), 0.0, 1.0)

        # Exact reference colors in true LINEAR RGB (from Smutsia temminckii reference photos)
        c_root = srgb_to_linear(np.array([36, 26, 22]) / 255.0)    # Deep espresso root
        c_body = srgb_to_linear(np.array([88, 68, 54]) / 255.0)    # Rich dusky bronze walnut
        c_ridge = srgb_to_linear(np.array([120, 96, 75]) / 255.0)  # Warm bronze fibrous striations
        c_edge = srgb_to_linear(np.array([185, 162, 134]) / 255.0) # Weathered pale ivory/horn rim
        c_dust = srgb_to_linear(np.array([138, 108, 84]) / 255.0)  # Arid Kalahari sand

        root_blend = np.clip((v - 0.08) / 0.50, 0.0, 1.0)[:, :, None]
        base_grad = c_root[None, None, :] * (1.0 - root_blend) + c_body[None, None, :] * root_blend

        ridge_blend = (primary_ridges * 0.18 + fine_striae * 0.14 + central_keel * 0.22)[:, :, None]
        albedo = base_grad * (1.0 - ridge_blend * 0.35) + c_ridge[None, None, :] * ridge_blend
        albedo *= (0.96 + 0.08 * growth_rings)[:, :, None]
        albedo = albedo * (1.0 - dust[:, :, None] * 0.28) + c_dust[None, None, :] * (dust[:, :, None] * 0.28)
        albedo = albedo * (1.0 - edge_wear[:, :, None]) + c_edge[None, None, :] * edge_wear[:, :, None]

        albedo = np.clip(albedo * cell_tint[cell_id], 0.0, 1.0)
        rgba = np.ones((n, n, 4))
        rgba[:, :, :3] = albedo

        # Roughness map: satiny worn edges and polished ridges, matte dusty crevices
        val = np.clip(0.48 - 0.12 * edge_wear - 0.08 * primary_ridges + 0.22 * dust + 0.03 * fine_striae, 0.35, 0.78)

        # Tangent Normal Map
        height = (
            0.016 * (1.0 - np.clip(dist_center, 0.0, 1.0) ** 1.8) * np.sin(v * math.pi * 0.95)
            + 0.0050 * primary_ridges * (1.0 - edge_wear * 0.35)
            + 0.0035 * central_keel
            + 0.0014 * fine_striae
            + 0.0016 * growth_rings
            - 0.0035 * edge_wear
        )
        dy, dx = np.gradient(height * 360.0)
        nx = -dx; ny = -dy; nz = np.ones_like(nx)
        norm = np.sqrt(nx * nx + ny * ny + nz * nz)
        normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)

    elif kind == 'skin':
        # Leathery facial, belly and orbital skin in true LINEAR RGB
        rng = np.random.default_rng(1892)
        noise1 = rng.normal(0, 1, (n, n))
        for _ in range(3):
            noise1 = (noise1 * 4 + np.roll(noise1, 1, 0) + np.roll(noise1, -1, 0) + np.roll(noise1, 1, 1) + np.roll(noise1, -1, 1)) / 8
        noise1 /= max(float(noise1.std()), 1e-8)

        pebble = np.sin(x * TAU * 32.0 + noise1 * 2.0) * np.sin(y * TAU * 32.0 + np.roll(noise1, 5, 0) * 2.0)
        micro = np.sin(x * TAU * 96.0) * np.sin(y * TAU * 96.0) * 0.5 + 0.5
        wrinkles = np.sin(y * TAU * 8.0 + noise1 * 1.5) * 0.5 + 0.5

        # Authentic mammalian skin tones from Reference 1 snout patch
        skin_base = srgb_to_linear(np.array([135, 112, 102]) / 255.0)  # Soft mauve-grey taupe
        crease_tone = srgb_to_linear(np.array([85, 66, 60]) / 255.0)   # Shadowed crease
        dust_tone = srgb_to_linear(np.array([180, 148, 120]) / 255.0)  # Kalahari sand dusting

        c_blend = np.clip(0.55 + 0.25 * noise1 + 0.15 * pebble, 0.0, 1.0)[:, :, None]
        albedo = skin_base[None, None, :] * c_blend + crease_tone[None, None, :] * (1.0 - c_blend)
        dust_amount = np.clip(0.30 + 0.25 * noise1 - 0.20 * pebble, 0.0, 1.0)[:, :, None]
        albedo = albedo * (1.0 - dust_amount * 0.32) + dust_tone[None, None, :] * (dust_amount * 0.32)
        albedo = np.clip(albedo, 0.0, 1.0)

        rgba = np.ones((n, n, 4))
        rgba[:, :, :3] = albedo
        val = np.clip(0.70 + 0.08 * noise1 + 0.05 * wrinkles, 0.60, 0.88)

        height = 0.0014 * noise1 + 0.0010 * pebble + 0.0006 * wrinkles
        dy, dx = np.gradient(height * 200.0)
        nx = -dx; ny = -dy; nz = np.ones_like(nx)
        norm = np.sqrt(nx * nx + ny * ny + nz * nz)
        normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)

    elif kind == 'claw':
        # Weathered pale ivory horn with sepia root in true LINEAR RGB
        striae = np.sin(x * TAU * 24.0 + np.sin(y * TAU * 3.0) * 0.4) * 0.5 + 0.5
        fine_lines = np.sin(x * TAU * 120.0) * 0.5 + 0.5
        root_grad = np.clip(y / 0.32, 0.0, 1.0)[:, :, None]

        c_tip = srgb_to_linear(np.array([205, 175, 142]) / 255.0)   # Weathered pale ivory horn
        c_root = srgb_to_linear(np.array([75, 52, 38]) / 255.0)      # Dark amber-sepia root
        albedo = c_root[None, None, :] * (1.0 - root_grad) + c_tip[None, None, :] * root_grad
        albedo *= (0.95 + 0.06 * striae + 0.03 * fine_lines)[:, :, None]
        albedo = np.clip(albedo, 0.0, 1.0)

        rgba = np.ones((n, n, 4))
        rgba[:, :, :3] = albedo
        val = np.clip(0.32 + 0.16 * (1.0 - root_grad.squeeze()) - 0.04 * striae, 0.22, 0.55)

        height = 0.0022 * striae + 0.0008 * fine_lines
        dy, dx = np.gradient(height * 200.0)
        nx = -dx; ny = -dy; nz = np.ones_like(nx)
        norm = np.sqrt(nx * nx + ny * ny + nz * nz)
        normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)
    else:
        rgba = np.ones((n, n, 4))
        rgba[:, :, :3] = np.clip(np.array(base)[None, None, :], 0, 1)
        val = np.ones((n, n)) * rough
        normals = np.ones((n, n, 3))
        normals[:, :, :2] = 0.5; normals[:, :, 2] = 1.0

    color = image(name + '_basecolor', rgba, directory, encode_linear_color=True)
    r = np.ones_like(rgba); r[:, :, :3] = val[:, :, None]
    roughim = image(name + '_roughness', r, directory, True)
    nm = np.ones_like(rgba); nm[:, :, :3] = normals
    normalim = image(name + '_normal', nm, directory, True)

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    bsdf = nodes.get('Principled BSDF')

    for im, target in ((color, 'Base Color'), (roughim, 'Roughness')):
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = im
        links.new(tex.outputs['Color'], bsdf.inputs[target])

    tex = nodes.new('ShaderNodeTexImage')
    tex.image = normalim
    norm_node = nodes.new('ShaderNodeNormalMap')
    norm_node.inputs['Strength'].default_value = 1.0 if kind == 'pangolin_keratin' else 0.45
    links.new(tex.outputs['Color'], norm_node.inputs['Color'])
    links.new(norm_node.outputs['Normal'], bsdf.inputs['Normal'])

    if kind == 'pangolin_keratin':
        bsdf.inputs['Specular IOR Level'].default_value = 0.45
    elif kind == 'claw':
        bsdf.inputs['Specular IOR Level'].default_value = 0.40
    else:
        bsdf.inputs['Specular IOR Level'].default_value = 0.30

    return mat

def solid(name, color, rough=.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = rough
    if 'Eye' in name:
        b.inputs['Specular IOR Level'].default_value = 0.85
    return m

class Geometry:
    def __init__(self):
        self.v = []
        self.f = []
        self.uv = []
        self.weights = []
        self.mi = []

    def vertex(self, p, uv=(0, 0), weights=None):
        self.v.append(tuple(p))
        self.uv.append(uv)
        self.weights.append(weights or {'Root': 1})
        return len(self.v) - 1

    def face(self, indices, mat=0):
        self.f.append(tuple(indices))
        self.mi.append(mat)

    def object(self, name, mats, rig):
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.v, [], self.f)
        mesh.update()
        ob = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(ob)
        for m in mats:
            mesh.materials.append(m)
        for p, mi in zip(mesh.polygons, self.mi):
            p.material_index = mi
            p.use_smooth = True
        uv = mesh.uv_layers.new(name='UVMap')
        for p in mesh.polygons:
            for li in p.loop_indices:
                uv.data[li].uv = self.uv[mesh.loops[li].vertex_index]
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(mesh)
        bm.free()
        groups = {b.name: ob.vertex_groups.new(name=b.name) for b in rig.data.bones}
        for i, weights in enumerate(self.weights):
            for bname, w in weights.items():
                if w > 1e-5 and bname in groups:
                    groups[bname].add([i], w, 'REPLACE')
        ob.parent = rig
        mod = ob.modifiers.new('AnatomicalSkin', 'ARMATURE')
        mod.object = rig
        return ob

def rig_create(name, segments):
    data = bpy.data.armatures.new(name)
    ob = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    root = data.edit_bones.new('Root')
    root.head = (0, 0, 0)
    root.tail = (0, .10, 0)
    for bname, p, q in segments:
        b = data.edit_bones.new(bname)
        b.head = p
        b.tail = q
        b.parent = root
        if bname.startswith('Blink'):
            b.parent = data.edit_bones['Head']
        if bname.endswith('Lower'):
            b.parent = data.edit_bones[bname[:-5] + 'Upper']
            b.use_connect = True
        elif bname.endswith('Foot'):
            b.parent = data.edit_bones[bname[:-4] + 'Lower']
            b.use_connect = True

    for child, parent in [('Body2', 'Body3'), ('Body1', 'Body2'), ('Body0', 'Body1'), ('Neck', 'Body0'), ('Head', 'Neck'), ('Tongue', 'Head')]:
        if child in data.edit_bones and parent in data.edit_bones:
            data.edit_bones[child].parent = data.edit_bones[parent]
    for i in range(8):
        tail_bone = f'Tail{i}'
        parent_bone = 'Body3' if i == 0 else f'Tail{i-1}'
        if tail_bone in data.edit_bones and parent_bone in data.edit_bones:
            data.edit_bones[tail_bone].parent = data.edit_bones[parent_bone]
    for label in ['FL', 'FR', 'HL', 'HR']:
        upper = label + 'Upper'
        if upper in data.edit_bones:
            data.edit_bones[upper].parent = data.edit_bones['Body0' if label.startswith('F') else 'Body3']

    bpy.ops.object.mode_set(mode='OBJECT')
    ob.animation_data_create()
    for b in ob.pose.bones:
        b.rotation_mode = 'QUATERNION'
    return ob

def longitudinal(z, centers):
    if z <= centers[0][1]: return {centers[0][0]: 1}
    if z >= centers[-1][1]: return {centers[-1][0]: 1}
    for (a, za), (b, zb) in zip(centers, centers[1:]):
        if za <= z <= zb:
            if a == b: return {a: 1}
            t = (z - za) / (zb - za)
            t = t * t * (3 - 2 * t)
            return {a: 1 - t, b: t}

def interp(profile, z):
    for a, b in zip(profile, profile[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            t = t * t * (3 - 2 * t)
            return [a[k] * (1 - t) + b[k] * t for k in range(1, 4)]
    return profile[0][1:] if z < profile[0][0] else profile[-1][1:]

def loft(g, profile, centers, step=.018, radial=40, mat=1):
    """Lofts continuous anatomical body envelope with smooth elliptical cross-sections."""
    zs = np.linspace(profile[0][0], profile[-1][0], int((profile[-1][0] - profile[0][0]) / step) + 1)
    rows = []
    for z in zs:
        y, rx, ry = interp(profile, z)
        row = []
        for j in range(radial + 1):
            a = TAU * j / radial
            c = math.cos(a)
            yc = c if c >= -0.75 else -0.75 + (c + 0.75) * 0.4
            p = Vector((rx * math.sin(a), y + ry * yc, z))
            uv = (j / radial, (z - zs[0]) / (zs[-1] - zs[0]) * 2.5)
            row.append(g.vertex(p, uv, longitudinal(z, centers)))
        rows.append(row)
    for ra, rb in zip(rows, rows[1:]):
        for j in range(radial):
            g.face((ra[j], rb[j], rb[j + 1], ra[j + 1]), mat)
    g.face(tuple(reversed(rows[0][:-1])), mat)
    g.face(rows[-1][:-1], mat)

def plate(g, anchor, across, along, normal, width, length, thickness, bone, cell=0, mat=0):
    """Closed, curved, 3D imbricated keratin shield with camber, keel, and rounded scalloped margin."""
    anchor = Vector(anchor)
    across = Vector(across).normalized()
    along = Vector(along).normalized()
    normal = Vector(normal).normalized()
    rows = []
    bottoms = []
    nx = 8
    ny = 8

    for j in range(ny + 1):
        t = j / ny
        ra = []
        rb = []
        for i in range(nx + 1):
            u = i / nx
            s = (u - 0.5) * 2.0  # [-1, 1]

            # Trailing edge reaches full length with convex arc and subtle scallop matching Reference 1 & 2
            edge_factor = 1.0 - 0.16 * (s ** 2) + 0.025 * math.cos(s * math.pi * 3.0)
            curved_len = length * (t * (1.0 - 0.25 * (1.0 - t)) * (1.0 - (1.0 - edge_factor) * t))

            # Width expands from root (0.74) to body (1.0) and curves into a wide rounded fan (0.90)
            if t < 0.65:
                w_factor = 0.74 + 0.26 * math.sin(t / 0.65 * math.pi * 0.5)
            else:
                w_factor = 1.0 - 0.10 * ((t - 0.65) / 0.35) ** 2

            camber = (1.0 - s ** 2) * thickness * 1.30 * math.sin(t * math.pi * 0.95)
            down_curl = -0.0035 * (t ** 1.8)
            keel = 0.0022 * math.exp(-((s * 3.5) ** 2)) * math.sin(t * math.pi)

            h_top = camber + down_curl + keel
            h_bot = h_top - thickness * (0.85 - 0.25 * t)

            p_top = anchor + across * (s * width * 0.5 * w_factor) + along * curved_len + normal * h_top
            p_bot = anchor + across * (s * width * 0.5 * w_factor * 0.96) + along * (curved_len * 0.98) + normal * h_bot

            u_atlas = ((cell % 4) + u * 0.92 + 0.04) / 4.0
            v_atlas = ((cell // 4) + t * 0.92 + 0.04) / 4.0

            ra.append(g.vertex(p_top, (u_atlas, v_atlas), {bone: 1}))
            rb.append(g.vertex(p_bot, (u_atlas, v_atlas), {bone: 1}))
        rows.append(ra)
        bottoms.append(rb)

    for j in range(ny):
        for i in range(nx):
            g.face((rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]), mat)
            g.face((bottoms[j][i], bottoms[j + 1][i], bottoms[j + 1][i + 1], bottoms[j][i + 1]), mat)

    perimeter = rows[0] + [r[-1] for r in rows[1:]] + list(reversed(rows[-1][:-1])) + [r[0] for r in reversed(rows[1:-1])]
    bottom = bottoms[0] + [r[-1] for r in bottoms[1:]] + list(reversed(bottoms[-1][:-1])) + [r[0] for r in reversed(bottoms[1:-1])]
    for i in range(len(perimeter)):
        g.face((perimeter[i], bottom[i], bottom[(i + 1) % len(bottom)], perimeter[(i + 1) % len(perimeter)]), mat)

def claw_talon(g, points, rx_list, ry_list, bone, mat=2, side=1, fan_angle=0.0):
    """Solid, blade-like fossorial digging sickle claw with stable parallel-transport frame."""
    rows = []
    base_lateral = Vector((math.cos(fan_angle) * side, 0, math.sin(fan_angle))).normalized()
    n_pts = len(points)

    for k, p in enumerate(points):
        p = Vector(p)
        if k == 0:
            tangent = (Vector(points[1]) - p).normalized()
        elif k == n_pts - 1:
            tangent = (p - Vector(points[-2])).normalized()
        else:
            tangent = (Vector(points[k + 1]) - Vector(points[k - 1])).normalized()

        lat = (base_lateral - tangent * base_lateral.dot(tangent)).normalized()
        norm = lat.cross(tangent).normalized()

        rx = rx_list[k]
        ry = ry_list[k]
        row = []
        for i in range(13):
            u = i / 12
            a = u * TAU
            ridge_shape = 1.0 + 0.20 * math.sin(a)
            offset = lat * (math.cos(a) * rx) + norm * (math.sin(a) * ry * ridge_shape)
            row.append(g.vertex(p + offset, (u, k / (n_pts - 1)), {bone: 1}))
        rows.append(row)

    for ra, rb in zip(rows, rows[1:]):
        for i in range(12):
            g.face((ra[i], ra[i + 1], rb[i + 1], rb[i]), mat)
    g.face(tuple(reversed(rows[0][:-1])), mat)
    g.face(rows[-1][:-1], mat)

def tube(g, points, radii, bone, mat=0, segments=12):
    rows = []
    for k, p in enumerate(points):
        p = Vector(p)
        tangent = Vector(points[min(k + 1, len(points) - 1)]) - Vector(points[max(0, k - 1)])
        tangent.normalize()
        ref = Vector((0, 1, 0))
        if abs(tangent.dot(ref)) > .95:
            ref = Vector((1, 0, 0))
        axis = tangent.cross(ref).normalized()
        up = axis.cross(tangent).normalized()
        row = []
        for i in range(segments + 1):
            u = i / segments
            a = u * TAU
            row.append(g.vertex(p + radii[k] * (math.cos(a) * axis + math.sin(a) * up), (u, k / (len(points) - 1)), {bone: 1}))
        rows.append(row)
    for ra, rb in zip(rows, rows[1:]):
        for i in range(segments):
            g.face((ra[i], ra[i + 1], rb[i + 1], rb[i]), mat)
    g.face(tuple(reversed(rows[0][:-1])), mat)
    g.face(rows[-1][:-1], mat)

def ellipsoid(g, center, radius, bone, mat=0, segments=20, rings=12):
    center = Vector(center)
    radius = Vector(radius)
    rows = []
    top = g.vertex(center + Vector((0, radius.y, 0)), (.5, 0), {bone: 1})
    for j in range(1, rings):
        row = []
        v = j / rings
        phi = math.pi * v
        for i in range(segments + 1):
            u = i / segments
            a = TAU * u
            p = center + Vector((math.sin(phi) * math.cos(a) * radius.x, math.cos(phi) * radius.y, math.sin(phi) * math.sin(a) * radius.z))
            row.append(g.vertex(p, (u, v), {bone: 1}))
        rows.append(row)
    bottom = g.vertex(center - Vector((0, radius.y, 0)), (.5, 1), {bone: 1})
    for i in range(segments):
        g.face((top, rows[0][i], rows[0][i + 1]), mat)
        g.face((bottom, rows[-1][i + 1], rows[-1][i]), mat)
    for left, right in zip(rows, rows[1:]):
        for i in range(segments):
            g.face((left[i], left[i + 1], right[i + 1], right[i]), mat)

def limb_definition(side, front, z):
    if front:
        h = Vector((.150 * side, .290, -.08))
        k = Vector((.210 * side, .165, -.01))
        w = Vector((.165 * side, .048, -.11))
    else:
        h = Vector((.170 * side, .310, .38))
        k = Vector((.220 * side, .170, .30))
        w = Vector((.175 * side, .048, .41))
    return h, k, w

def limb_loft(g, label, h, k, w):
    """Muscular limb volume with continuous taper toward strong foot joints."""
    side = 1 if h.x > 0 else -1
    points = [Vector((.120 * side, .315, h.z)), h, h.lerp(k, .30), h.lerp(k, .65), k, k.lerp(w, .35), k.lerp(w, .72), w]
    radii = [.080, .078, .070, .058, .050, .042, .036, .030]
    rows = []
    frames = []
    radial = 20
    for j, (p, radius) in enumerate(zip(points, radii)):
        tangent = (points[min(j + 1, len(points) - 1)] - points[max(0, j - 1)]).normalized()
        across = tangent.cross(Vector((0, 0, 1))).normalized()
        out = across.cross(tangent).normalized()
        rows.append([])
        frames.append((p, across, out, radius))
        if j <= 3: weights = {label + 'Upper': 1}
        elif j == 4: weights = {label + 'Upper': .35, label + 'Lower': .65}
        elif j == 7: weights = {label + 'Lower': .35, label + 'Foot': .65}
        else: weights = {label + 'Lower': 1}
        for i in range(radial + 1):
            u = i / radial
            a = u * TAU
            pos = p + radius * (math.cos(a) * across + math.sin(a) * out * .88)
            rows[-1].append(g.vertex(pos, (u, j / 7), weights))
    for ra, rb in zip(rows, rows[1:]):
        for i in range(radial):
            g.face((ra[i], ra[i + 1], rb[i + 1], rb[i]), 1)
    g.face(tuple(reversed(rows[0][:-1])), 1)
    g.face(rows[-1][:-1], 1)
    return frames

def continuous_skin(g, name, mats, rig, profile):
    """Voxel-union anatomical volumes into continuous seamless skin."""
    ob = g.object(name, mats, rig)
    ob.modifiers.clear()
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)

    mod = ob.modifiers.new('ContinuousAnatomicalUnion', 'REMESH')
    mod.mode = 'VOXEL'
    mod.voxel_size = .0065
    mod.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=mod.name)

    smooth = ob.modifiers.new('SoftTissueTransitions', 'SMOOTH')
    smooth.factor = .65
    smooth.iterations = 2
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    dec = ob.modifiers.new('SurfaceRetopologyBudget', 'DECIMATE')
    dec.ratio = .28
    bpy.ops.object.modifier_apply(modifier=dec.name)

    tree = kdtree.KDTree(len(g.v))
    for i, p in enumerate(g.v):
        tree.insert(Vector(p), i)
    tree.balance()

    ob.vertex_groups.clear()
    groups = {b.name: ob.vertex_groups.new(name=b.name) for b in rig.data.bones}
    uv = ob.data.uv_layers.new(name='UVMap') if not ob.data.uv_layers else ob.data.uv_layers.active
    coords = []
    z_min = profile[0][0]
    z_max = profile[-1][0]

    for v in ob.data.vertices:
        neighbors = tree.find_n(v.co, 4)
        weights = {}
        total = 0
        for _, index, distance in neighbors:
            influence = 1 / max(distance, .002) ** 2
            total += influence
            for bone, w in g.weights[index].items():
                weights[bone] = weights.get(bone, 0) + w * influence

        # Conformal elliptical cylindrical projection around spine axis prevents snout texture stretching
        y_center, rx, ry = interp(profile, v.co.z)
        dx = v.co.x / max(0.01, rx)
        dy = (v.co.y - y_center) / max(0.01, ry)
        angle = math.atan2(dx, dy)
        u_skin = angle / TAU + 0.5
        v_skin = (v.co.z - z_min) / (z_max - z_min)
        coords.append((u_skin * 2.0, v_skin * 2.5))

        selected = sorted(weights.items(), key=lambda item: -item[1])[:4]
        norm = sum(w for _, w in selected)
        for bone, w in selected:
            if bone in groups:
                groups[bone].add([v.index], w / norm, 'REPLACE')

    for p in ob.data.polygons:
        p.use_smooth = True
        p.material_index = 1
        for li in p.loop_indices:
            uv.data[li].uv = coords[ob.data.loops[li].vertex_index]

    mod = ob.modifiers.new('AnatomicalSkin', 'ARMATURE')
    mod.object = rig
    return ob

def pose_matrix(rig, name, offset=(0, 0, 0), angle=0, axis='Y'):
    if name not in rig.data.bones: return
    b = rig.data.bones[name]
    p = b.head_local
    transform = Matrix.Translation(Vector(offset)) @ Matrix.Translation(p) @ Matrix.Rotation(angle, 4, axis) @ Matrix.Translation(-p)
    rig.pose.bones[name].matrix = transform @ b.matrix_local
    bpy.context.view_layer.update()

def segment_pose(rig, name, head, tail, parent_matrix=None):
    bone = rig.data.bones[name]
    rotation = (bone.tail_local - bone.head_local).rotation_difference(Vector(tail) - Vector(head)).to_matrix().to_4x4()
    transform = Matrix.Translation(Vector(head)) @ rotation @ Matrix.Translation(-bone.head_local)
    desired = transform @ bone.matrix_local
    if bone.parent:
        parent_matrix = parent_matrix if parent_matrix is not None else rig.pose.bones[bone.parent.name].matrix
        rig.pose.bones[name].matrix_basis = bone.convert_local_to_pose(desired, bone.matrix_local, parent_matrix=parent_matrix, parent_matrix_local=bone.parent.matrix_local, invert=True)
    else:
        rig.pose.bones[name].matrix = desired
    return desired

def solve_leg(rig, label, target):
    """Two-link IK maintaining exact bone lengths and planted stability."""
    upper = rig.data.bones[label + 'Upper']
    lower = rig.data.bones[label + 'Lower']
    parent = upper.parent
    motion = rig.pose.bones[parent.name].matrix @ parent.matrix_local.inverted() if parent else Matrix.Identity(4)
    hip = motion @ upper.head_local
    rest_knee = motion @ upper.tail_local
    target = Vector(target)
    a = (upper.tail_local - upper.head_local).length
    b = (lower.tail_local - lower.head_local).length
    delta = target - hip
    distance = delta.length
    if distance >= a + b - .0001 or distance <= abs(a - b) + .0001:
        raise RuntimeError('Unreachable planted paw ' + label)
    direction = delta / distance
    bend = rest_knee - hip - direction * (rest_knee - hip).dot(direction)
    bend.normalize()
    along = (a * a - b * b + distance * distance) / (2 * distance)
    height = math.sqrt(max(0, a * a - along * along))
    knee = hip + direction * along + bend * height
    upper_matrix = segment_pose(rig, label + 'Upper', hip, knee)
    lower_matrix = segment_pose(rig, label + 'Lower', knee, target, upper_matrix)
    foot = rig.data.bones[label + 'Foot']
    offset = target - foot.head_local
    desired = Matrix.Translation(offset) @ foot.matrix_local
    rig.pose.bones[label + 'Foot'].matrix_basis = foot.convert_local_to_pose(desired, foot.matrix_local, parent_matrix=lower_matrix, parent_matrix_local=lower.matrix_local, invert=True)
    return hip, knee, target

def keyall(rig, frame):
    for b in rig.pose.bones:
        for channel in ('location', 'rotation_quaternion', 'scale'):
            b.keyframe_insert(channel, frame=frame)

def animate(rig, callback, names=('idle', 'walk', 'feed', 'rest')):
    modifiers = [m for ob in bpy.data.objects if ob.type == 'MESH' for m in ob.modifiers if m.type == 'ARMATURE']
    for mod in modifiers: mod.show_viewport = False
    for name in names:
        act = bpy.data.actions.new(name)
        rig.animation_data.action = act
        for f in range(1, 122):
            for b in rig.pose.bones: b.matrix_basis = Matrix.Identity(4)
            bpy.context.view_layer.update()
            callback(name, (f - 1) / 120, rig)
            bpy.context.view_layer.update()
            keyall(rig, f)
        for layer in act.layers:
            for action_strip in layer.strips:
                for bag in action_strip.channelbags:
                    for curve in bag.fcurves:
                        for point in curve.keyframe_points: point.interpolation = 'LINEAR'
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, act)
        strip.name = name
        track.mute = True
    rig.animation_data.action = None
    for b in rig.pose.bones: b.matrix_basis = Matrix.Identity(4)
    bpy.context.scene.frame_set(1)
    for mod in modifiers: mod.show_viewport = True
    bpy.context.view_layer.update()

def pangolin_motion(clip, t, rig):
    phase = t * TAU
    for name in ('Body3', 'Body2', 'Body1', 'Body0', 'Neck', 'Head'):
        z = rig.data.bones[name].head_local.z
        amplitude = .008 if clip == 'walk' else .0025
        x = amplitude * math.sin(phase - z * 2.5)
        angle = -2.5 * amplitude * math.cos(phase - z * 2.5)
        y = (-.030 if clip == 'rest' else 0) + (.003 * math.sin(phase) ** 2 if clip != 'walk' else .002 * math.sin(phase * 2))
        pose_matrix(rig, name, offset=(x, y, 0), angle=angle, axis='Y')
        if name in ['Neck', 'Head'] and clip == 'feed':
            dip = math.sin(math.pi * t) ** 2
            current = rig.pose.bones[name].matrix.copy()
            p = current.translation.copy()
            rig.pose.bones[name].matrix = Matrix.Translation(p) @ Matrix.Rotation(-(.12 if name == 'Neck' else .22) * dip, 4, 'X') @ Matrix.Translation(-p) @ current

    amplitude = .020 if clip == 'walk' else .006
    for i in range(8):
        s = i * .06
        envelope = (s / 0.48) ** 2
        a = phase - s * 2.2
        x = amplitude * envelope * math.sin(a)
        derivative = amplitude * (2 * s / 0.48 ** 2 * math.sin(a) - 2.2 * envelope * math.cos(a))
        pose_matrix(rig, f'Tail{i}', offset=(x, -.020 * (1 - s / 0.48) if clip == 'rest' else 0, 0), angle=math.atan(derivative))

    for label, offset in [('FL', 0), ('HR', .25), ('FR', .5), ('HL', .75)]:
        target = rig.data.bones[label + 'Foot'].head_local.copy()
        if clip == 'walk':
            u = (t + offset) % 1
            duty = .68
            stride = .4 * duty
            if u < duty:
                lift = 0
                dz = -stride * .5 + stride * u / duty
            else:
                swing = (u - duty) / (1 - duty)
                v = .4 * (1 - duty)
                dz = (2 * swing ** 3 - 3 * swing ** 2 + 1) * stride * .5 + (-2 * swing ** 3 + 3 * swing ** 2) * (-stride * .5) + (swing ** 3 - 2 * swing ** 2 + swing) * v + (swing ** 3 - swing ** 2) * v
                lift = .040 * math.sin(math.pi * swing)
            target += Vector((0, lift, dz))
        solve_leg(rig, label, target)

    flick = 0
    if clip == 'feed':
        for center in [.29, .66]:
            flick = max(flick, max(0, min(1, (t - center + .025) / .025)) * max(0, min(1, (center + .10 - t) / .10)))
    width = max(.001, min(1, flick * 10))
    if 'Tongue' in rig.pose.bones:
        rig.pose.bones['Tongue'].scale = (width, max(.001, flick), width)
        rig.pose.bones['Tongue'].location = rig.data.bones['Tongue'].matrix_local.to_3x3().inverted() @ Vector((0, .020 * (1 - flick), .030 * (1 - flick) - .050 * flick))
    bpy.context.view_layer.update()
    from pangolin_lids import lid_pose
    lid_pose(rig, clip, t)

def build_pangolin():
    global REPORT
    REPORT = Path('.clanker/tasks/guardian-pangolin-solo/v16-authoring')
    clean()
    out = Path('game/assets/guardians/armored_forest')
    out.mkdir(parents=True, exist_ok=True)

    # Authored high-fidelity PBR materials with exact reference colors
    mats = [
        pbr('AmberKeratin', out, 'pangolin_keratin', (.306, .141, .058), .46),
        pbr('PangolinSkin', out, 'skin', (.355, .218, .165), .75),
        pbr('DarkClaw', out, 'claw', (.677, .548, .355), .38),
        solid('GlossEye', (.002, .002, .002), .04),
        solid('WarmIris', (.065, .042, .020), .25),
        solid('Mouth', (.055, .028, .022), .70),
        solid('Tongue', (.42, .12, .12), .45)
    ]

    # Authentic Smutsia temminckii anatomical profile
    # Compact foraging head -> nuchal slope -> high dorsal arch -> broad flattened paddle tail
    profile = [
        (-0.52, 0.082, 0.030, 0.022),  # Blunt conical rhinarium tip close to ground
        (-0.46, 0.120, 0.048, 0.036),  # Rostrum bridge
        (-0.38, 0.170, 0.082, 0.060),  # Cranial base & hooded eyes
        (-0.30, 0.240, 0.125, 0.098),  # Cranial crown & valvular ears
        (-0.20, 0.320, 0.180, 0.145),  # Nuchal collar / neck transition
        (-0.08, 0.395, 0.230, 0.185),  # Scapular arch / shoulders & front legs
        ( 0.08, 0.435, 0.248, 0.200),  # High-domed thoracic peak
        ( 0.24, 0.420, 0.245, 0.195),  # Lumbar arch
        ( 0.38, 0.365, 0.225, 0.165),  # Pelvic vault & hind legs
        ( 0.48, 0.290, 0.195, 0.108),  # Broad dorsoventrally compressed tail root
        ( 0.54, 0.240, 0.175, 0.092),  # Proximal muscular tail
        ( 0.60, 0.195, 0.155, 0.078),  # Tail seg 2
        ( 0.66, 0.150, 0.135, 0.065),  # Mid tail
        ( 0.72, 0.110, 0.115, 0.052),  # Tail seg 4
        ( 0.78, 0.075, 0.095, 0.040),  # Distal tail
        ( 0.84, 0.048, 0.072, 0.030),  # Pre-terminal tail
        ( 0.90, 0.028, 0.048, 0.020),  # Terminal tail
        ( 0.96, 0.016, 0.015, 0.010),  # Rounded paddle tip
    ]

    centers = [('Head', -0.42), ('Neck', -0.26), ('Body0', -0.10), ('Body1', 0.08), ('Body2', 0.24), ('Body3', 0.38)] + [(f'Tail{i}', 0.48 + i * 0.060) for i in range(8)]

    seg = []
    for n, z in centers:
        if n.startswith('Tail'):
            nextz = min(0.96, z + 0.060)
            seg.append((n, (0, interp(profile, z)[0], z), (0, interp(profile, nextz)[0], nextz)))
        else:
            seg.append((n, (0, interp(profile, z)[0] * 0.8, z), (0, interp(profile, z)[0] * 0.8, z + 0.08)))

    for side, label in ((1, 'L'), (-1, 'R')):
        for front, prefix, z in ((True, 'F', -0.08), (False, 'H', 0.38)):
            lbl = prefix + ('L' if side == 1 else 'R')
            h, k, w = limb_definition(side, front, z)
            seg.extend([(lbl + 'Upper', h, k), (lbl + 'Lower', k, w), (lbl + 'Foot', w, (w.x, 0.025, w.z - .04))])

    eye_x, eye_y, eye_z = 0.084, 0.170, -0.380
    for side, suffix in [(1, 'L'), (-1, 'R')]:
        for label in ['Blink_' + suffix, 'BlinkLower_' + suffix]:
            seg.append((label, (side * eye_x, eye_y + .010, eye_z), (side * eye_x, eye_y - .010, eye_z)))

    seg.append(('Tongue', (0, .080, -.48), (0, .080, -.54)))
    rig = rig_create('PangolinRig', seg)

    skin = Geometry()
    scales = Geometry()
    detail = Geometry()

    loft(skin, profile, centers, mat=1, radial=48)

    # Seamless Imbricated (Overlapping) Scale Carapace
    # One unbroken cascade from bridge of snout (z = -0.48) to pelvis (z = 0.48)
    # Every scale row shingles backward toward the tail, completely covering head, neck, and torso.
    z = -0.48
    row = 0

    while z < 0.48:
        y, rx, ry = interp(profile, z)
        # Normalized anatomical progression: 0 at snout bridge (-0.48), 1 at dorsal thoracic arch (0.08)
        u_body = max(0.0, min(1.0, (z - (-0.48)) / (0.08 - (-0.48))))

        step = 0.026 * (1.0 - u_body) + 0.058 * u_body
        arc = 3.10 * (1.0 - u_body) + 3.90 * u_body
        count = int(round(6.0 * (1.0 - u_body) + max(11, rx * TAU / 0.125) * u_body))
        count = max(4, count)
        thk = 0.0035 * (1.0 - u_body) + 0.0075 * u_body

        # Scale width and length smoothly transition from 1:1 polygonal scutes to broad fan shields
        sc_wid = (rx * arc / count) * (1.18 * (1.0 - u_body) + 1.28 * u_body)
        sc_len = sc_wid * (1.02 * (1.0 - u_body) + 1.22 * u_body)

        for j in range(count):
            stagger = 0.5 * (row % 2)
            # Evenly distributed across entire arc with alternating half-step stagger, keeping all flank scales
            a = -arc * 0.5 + (j + 0.5 + 0.5 * (stagger - 0.25)) / count * arc

            normal = Vector((math.sin(a), math.cos(a), 0)).normalized()
            tangent = Vector((math.cos(a), -math.sin(a), 0)).normalized()
            anchor = Vector((rx * math.sin(a), y + ry * math.cos(a), z)) + normal * (0.0016 + 0.0006 * u_body)

            # Local eye exclusion: keep only the immediate eye globe aperture clear
            d_eye_L = (anchor - Vector((eye_x, eye_y, eye_z))).length
            d_eye_R = (anchor - Vector((-eye_x, eye_y, eye_z))).length
            if min(d_eye_L, d_eye_R) < 0.016:
                continue

            ny, nrx, nry = interp(profile, min(z + sc_len, profile[-1][0]))
            delta = Vector(((nrx - rx) * math.sin(a), ny + nry * math.cos(a) - y - ry * math.cos(a), sc_len))

            bone_dict = longitudinal(z + 0.03, centers)
            bone = max(bone_dict, key=bone_dict.get)

            cell_idx = (row * 7 + j * 3) % 16
            plate(scales, anchor, tangent, delta.normalized(), normal, sc_wid, delta.length, thk, bone, cell=cell_idx, mat=0)

        z += step * 0.65  # ~50% continuous longitudinal overlap
        row += 1

    # Tail Armor Plates: Broad dorsal plates + Lateral serrated keeled crest scales
    tz = 0.48
    trow = 0
    while tz < 0.94:
        ty, trx, _ = interp(profile, tz)
        tstep = 0.055
        tcount = max(3, min(5, int(trx * 22.0)))
        tarc = 2.65
        t_len = tstep * 1.85
        t_wid = (trx * tarc / tcount) * 1.25
        tthk = 0.0065

        # Dorsal tail plates
        for j in range(tcount):
            stagger = 0.5 * (trow % 2)
            a = -tarc * 0.5 + (j + stagger) / max(1, tcount - 1) * tarc
            normal = Vector((math.sin(a), math.cos(a), 0)).normalized()
            tangent = Vector((math.cos(a), -math.sin(a), 0)).normalized()
            anchor = Vector((trx * math.sin(a), ty + interp(profile, tz)[2] * math.cos(a), tz)) + normal * 0.0020
            t_ny, t_nrx, t_nry = interp(profile, min(tz + t_len, profile[-1][0]))
            delta = Vector(((t_nrx - trx) * math.sin(a), t_ny + t_nry * math.cos(a) - ty - interp(profile, tz)[2] * math.cos(a), t_len))
            bone_dict = longitudinal(tz + 0.03, centers)
            bone = max(bone_dict, key=bone_dict.get)
            cell_idx = (trow * 5 + j * 3) % 16
            plate(scales, anchor, tangent, delta.normalized(), normal, t_wid, delta.length, tthk, bone, cell=cell_idx, mat=0)

        # Dedicated lateral keeled edge scales (saw-tooth lateral tail crest)
        for l_side in (-1, 1):
            edge_x = trx * l_side * 0.98
            edge_y = ty
            edge_z = tz
            lat_anchor = Vector((edge_x, edge_y, edge_z))
            lat_along = Vector((0.30 * l_side, -0.12, 0.94)).normalized()
            lat_normal = Vector((0.85 * l_side, 0.52, 0.0)).normalized()
            lat_across = lat_along.cross(lat_normal).normalized()
            lat_len = tstep * 1.65
            lat_wid = max(0.024, 0.044 * (1.0 - (tz - 0.48) / 0.46 * 0.55))
            cell_idx = (trow * 5 + (1 if l_side == 1 else 2)) % 16
            bone_dict = longitudinal(tz + 0.02, centers)
            bone = max(bone_dict, key=bone_dict.get)
            plate(scales, lat_anchor, lat_across, lat_along, lat_normal, lat_wid, lat_len, 0.0060, bone, cell=cell_idx, mat=0)

        tz += tstep
        trow += 1

    # Limb Lofts & Digits
    for side in (-1, 1):
        for front, prefix, pz in ((True, 'F', -0.08), (False, 'H', 0.38)):
            label = prefix + ('L' if side == 1 else 'R')
            upper = label + 'Upper'
            lower = label + 'Lower'
            foot = label + 'Foot'
            h, k, w = limb_definition(side, front, pz)
            frames = limb_loft(skin, label, h, k, w)

            # Robust overlapping limb scales along the outer thigh/forearm
            for lrow in range(8):
                q = 1.0 + lrow * 0.70
                lo = int(q); hi = min(7, lo + 1); frac = q - lo
                p0, across0, out0, r0 = frames[lo]
                p1, across1, out1, r1 = frames[hi]
                p = p0.lerp(p1, frac)
                across = across0.lerp(across1, frac).normalized()
                radial_out = out0.lerp(out1, frac).normalized()
                radius = r0 * (1 - frac) + r1 * frac
                direction = (p1 - p0).normalized()

                for col in range(4):
                    ang = -0.90 + col * 0.55 + 0.08 * (lrow % 2)
                    norm = (across * math.cos(ang) + radial_out * math.sin(ang)).normalized()
                    if norm.x * side < 0: norm = -norm
                    sc_anchor = p + norm * (radius - 0.002)
                    lateral = direction.cross(norm).normalized()
                    cell_idx = (lrow * 4 + col * 3) % 16
                    plate(scales, sc_anchor, lateral, direction, norm, 0.028, 0.040, 0.0042, upper if q < 4.0 else lower, cell=cell_idx, mat=0)

            # Feet and Claws
            if front:
                # Main forefoot paw pad / knuckle
                ellipsoid(skin, (w.x, .026, w.z - .025), (.045, .022, .052), foot, 1)
                # Three massive recurved sickle digging claws (Digits II, III, IV) emerging from knuckle and curling under
                claw_specs = [
                    (-1, .110, .014, -0.15),  # Digit II: large sickle claw, angled slightly inward
                    ( 0, .145, .017,  0.00),  # Digit III: massive central digging sickle!
                    ( 1, .098, .013,  0.15),  # Digit IV: large sickle claw, angled slightly outward
                ]
                for idx, c_len, c_rad, c_ang in claw_specs:
                    cx = w.x + idx * .018 * side
                    c_pts = [
                        Vector((cx, .042, w.z - .048)),
                        Vector((cx - idx * .002 * side, .034, w.z - .062)),
                        Vector((cx - idx * .006 * side, .016, w.z - .042)),
                        Vector((cx - idx * .012 * side, .018, w.z + .018)),
                        Vector((cx - idx * .018 * side, .026, w.z + .065 * (c_len / 0.145)))
                    ]
                    rx_list = [c_rad * 0.72, c_rad * 0.65, c_rad * 0.52, c_rad * 0.28, 0.001]
                    ry_list = [c_rad * 1.25, c_rad * 1.15, c_rad * 0.85, c_rad * 0.42, 0.001]
                    claw_talon(detail, c_pts, rx_list, ry_list, foot, mat=2, side=side, fan_angle=c_ang)
                # Outer reduced digits I and V
                for idx, c_len in ((-2, .035), (2, .032)):
                    cx = w.x + idx * .018 * side
                    c_pts = [
                        (cx, .032, w.z - .042),
                        (cx, .022, w.z - .045),
                        (cx, .016, w.z - .025)
                    ]
                    tube(detail, c_pts, [.006, .004, .001], foot, 2, segments=8)
            else:
                # Hindfoot plantigrade sole and 5 stout blunt claws
                ellipsoid(skin, (w.x, .024, w.z + .035), (.048, .020, .060), foot, 1)
                for idx in range(-2, 3):
                    cx = w.x + idx * .015 * side
                    c_len = .036 if abs(idx) <= 1 else .025
                    c_pts = [
                        (cx, .026, w.z - .015),
                        (cx, .018, w.z - .015 - c_len * .6),
                        (cx, .010, w.z - .015 - c_len)
                    ]
                    tube(detail, c_pts, [.007, .005, .001], foot, 2, segments=8)

        # Eyes: small, dark, deeply hooded globes protruding on lateral cheek surface
        ellipsoid(detail, (eye_x * side, eye_y, eye_z), (.008, .008, .009), 'Head', 3, 20, 12)
        ellipsoid(detail, ((eye_x + .003) * side, eye_y, eye_z), (.0025, .005, .005), 'Head', 4, 16, 10)
        # Hooded fleshy eyelid folds wrapping the eye
        for sign in (-1, 1):
            pts = []
            for k in range(13):
                a = math.pi * k / 12
                pts.append((side * (eye_x + .001 + .002 * math.sin(a)), eye_y + sign * .010 * math.sin(a), eye_z + .011 * math.cos(a)))
            tube(detail, pts, [.0024] * 13, 'Head', 1, 8)

        # Nostril apertures at blunt snout tip
        ellipsoid(detail, (.008 * side, .085, -.515), (.0030, .0024, .0048), 'Head', 5, 12, 8)
        # Folded fleshy ear pinna behind cheek as seen in Reference 1
        ellipsoid(detail, (.088 * side, .185, -.355), (.0035, .0065, .0050), 'Head', 1, 14, 10)

    from pangolin_lids import add_lids
    add_lids(Geometry, rig, detail, eye_x=eye_x, eye_y=eye_y, eye_z=eye_z)

    # Tongue cleft
    tube(detail, [(0, .078, -.490), (0, .078, -.510), (0, .078, -.525)], [.0028, .0025, .0018], 'Tongue', 6, 8)

    bodies = [
        continuous_skin(skin, 'PangolinContinuousSkin', mats, rig, profile),
        scales.object('ClosedOverlappingArmor', mats, rig),
        detail.object('FaceDigitsClaws', mats, rig)
    ]

    rig['art_revision'] = 'pangolin-solo-v16-photoreal'
    animate(rig, pangolin_motion)
    finish('armored_forest', out, rig, bodies)

def finish(species, out, rig, bodies):
    scene = bpy.context.scene
    scene.frame_start = 1
    scene.frame_end = 121
    bpy.ops.export_scene.gltf(
        filepath=str((out / 'guardian.glb').resolve()),
        export_format='GLB',
        export_animations=True,
        export_skins=True,
        export_animation_mode='NLA_TRACKS',
        export_force_sampling=True,
        export_apply=False
    )
    bpy.ops.wm.save_as_mainfile(filepath=str((REPORT / (species + '-source.blend')).resolve()))
    audit = {
        'species': species,
        'source_axis': 'Y up, -Z forward; preserve runtime root +90X',
        'vertices': sum(len(o.data.vertices) for o in bodies),
        'triangles': sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in bodies),
        'joints': len(rig.data.bones),
        'clips': [a.name for a in bpy.data.actions],
        'glb_bytes': (out / 'guardian.glb').stat().st_size,
        'materials': [m.name for m in bpy.data.materials],
        'textures': [im.name for im in bpy.data.images if im.type == 'IMAGE'],
        'animation_bounds': {},
        'nominal_source_speed_mps': .20,
        'clip_duration_seconds': 2,
    }
    deps = bpy.context.evaluated_depsgraph_get()
    for action in bpy.data.actions:
        rig.animation_data.action = action
        low = 1e9
        high = -1e9
        for frame in range(1, 122):
            scene.frame_set(frame)
            deps.update()
            for ob in bodies:
                ev = ob.evaluated_get(deps)
                mesh = ev.to_mesh()
                coords = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.vertices.foreach_get('co', coords)
                coords = coords.reshape((-1, 3))
                if not np.isfinite(coords).all():
                    raise RuntimeError('Non-finite deformation')
                low = min(low, float(coords[:, 1].min()))
                high = max(high, float(coords[:, 1].max()))
                ev.to_mesh_clear()
        audit['animation_bounds'][action.name] = {'min_y': round(low, 6), 'max_y': round(high, 6), 'sampled_frames': 121}

    rig.animation_data.action = None
    for b in rig.pose.bones: b.matrix_basis = Matrix.Identity(4)
    scene.frame_set(1)

    if species == 'armored_forest':
        audit['planted_ik'] = audit_planted_ik(rig, bodies)
        rig.animation_data.action = None
        for b in rig.pose.bones: b.matrix_basis = Matrix.Identity(4)
        scene.frame_set(1)

    (REPORT / (species + '-audit.json')).write_text(json.dumps(audit, indent=2))
    if os.environ.get('GUARDIAN_SKIP_EVIDENCE') != '1':
        render_views(species, bodies)

def audit_planted_ik(rig, bodies):
    scene = bpy.context.scene
    rig.animation_data.action = bpy.data.actions['walk']
    groups = {}
    sole_groups = {}
    gap = 0
    length_error = 0
    flat_degrees = 0
    sole_y = []
    traces = []
    skin = bodies[0]
    representative = {}
    for label in ('FL', 'FR', 'HL', 'HR'):
        group_index = skin.vertex_groups[label + 'Foot'].index
        candidates = [v for v in skin.data.vertices if v.co.y < .03 and any(g.group == group_index and g.weight > .95 for g in v.groups)]
        if not candidates:
            candidates = [v for v in skin.data.vertices if any(g.group == group_index for g in v.groups)]
        representative[label] = min(candidates, key=lambda v: v.co.y).index

    deps = bpy.context.evaluated_depsgraph_get()
    for half_frame in range(241):
        frame = 1 + half_frame * .5
        scene.frame_set(int(frame), subframe=frame - int(frame))
        t = (frame - 1) / 120
        seconds = t * 2
        evaluated = skin.evaluated_get(deps)
        mesh = evaluated.to_mesh()
        for label, phase_offset in (('FL', 0), ('FR', .5), ('HL', .75), ('HR', .25)):
            u = (t + phase_offset) % 1
            up = rig.pose.bones[label + 'Upper']
            low = rig.pose.bones[label + 'Lower']
            paw = rig.pose.bones[label + 'Foot']
            gap = max(gap, (up.tail - low.head).length, (low.tail - paw.head).length)
            for pb in (up, low):
                length_error = max(length_error, abs((pb.tail - pb.head).length - pb.bone.length))
            relative = paw.matrix.to_3x3() @ paw.bone.matrix_local.to_3x3().inverted()
            flat_degrees = max(flat_degrees, math.degrees(relative.to_quaternion().angle))
            if u < .68:
                group = (label, math.floor(t + phase_offset))
                p = paw.head.copy()
                p.z -= .2 * seconds
                groups.setdefault(group, []).append(p)
                sole_y.append(paw.head.y)
                sole = mesh.vertices[representative[label]].co.copy()
                sole.z -= .2 * seconds
                sole_groups.setdefault(group, []).append(sole)
            sole = mesh.vertices[representative[label]].co
            traces.append([round(frame, 3), round(seconds, 6), label, u < .68, float(paw.head.x), float(paw.head.y), float(paw.head.z), float(sole.x), float(sole.y), float(sole.z), float(sole.z - .2 * seconds)])
        evaluated.to_mesh_clear()

    slip = max((p - group[0]).length for group in groups.values() for p in group)
    sole_slip = max((p - group[0]).length for group in sole_groups.values() for p in group) if sole_groups else 0
    planted_soles = [p.y for group in sole_groups.values() for p in group] if sole_groups else [0.01]

    with (REPORT / 'pangolin-v2-contact-trace.csv').open('w', newline='') as handle:
        writer = csv.writer(handle)
        writer.writerow(['frame', 'seconds', 'foot', 'stance', 'bone_x', 'bone_y', 'bone_z', 'sole_x', 'sole_y', 'sole_z', 'virtual_world_sole_z'])
        writer.writerows(traces)

    result = {
        'sampled_half_frames': 241,
        'max_joint_gap_m': gap,
        'max_segment_length_error_m': length_error,
        'max_virtual_world_planted_paw_drift_m': slip,
        'max_virtual_world_planted_sole_drift_m': sole_slip,
        'planted_sole_y_range_m': [min(planted_soles), max(planted_soles)],
        'max_paw_orientation_error_degrees': flat_degrees,
        'planted_paw_origin_y_range_m': [min(sole_y), max(sole_y)] if sole_y else [0, 0],
        'source_forward_speed_mps': .2,
        'pass': gap < 1e-5 and length_error < 1e-5 and slip < .001 and flat_degrees < .1
    }
    if not result['pass']:
        raise RuntimeError('Planted IK contact audit failed ' + json.dumps(result))
    return result

def render_views(species, bodies):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1000
    scene.render.resolution_y = 700
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('NeutralWorld')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.12, .14, .16, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = .7
    scene.view_settings.view_transform = 'AgX'

    bpy.ops.mesh.primitive_plane_add(size=200, location=(0, -.006, 0), rotation=(math.pi / 2, 0, 0))
    plane = bpy.context.object
    plane.data.materials.append(solid('StudioGround', (.16, .18, .20), .75))

    for name, pos, power, size in [('Key', (2.5, 4, -2), 650, 4), ('Fill', (-3, 2, -.5), 380, 3), ('Rim', (1, 3.5, 3), 750, 3)]:
        data = bpy.data.lights.new(name, 'AREA')
        data.energy = power
        data.shape = 'DISK'
        data.size = size
        ob = bpy.data.objects.new(name, data)
        scene.collection.objects.link(ob)
        ob.location = pos
        ob.rotation_euler = (Vector((0, .25, .15)) - ob.location).to_track_quat('-Z', 'Y').to_euler()

    camdata = bpy.data.cameras.new('EvidenceCamera')
    cam = bpy.data.objects.new('EvidenceCamera', camdata)
    scene.collection.objects.link(cam)
    scene.camera = cam
    camdata.type = 'ORTHO'
    camdata.ortho_scale = 2.45
    target = Vector((0, .26, .15))
    views = {'side': (3.6, 1.1, .15), 'front': (0, 1.1, -3.6), 'three_quarter': (2.8, 1.8, -2.6)}
    clay = solid('NeutralClay', (.38, .40, .42), .6)
    original = [[m for m in ob.data.materials] for ob in bodies]

    for mode in ('pbr', 'clay'):
        if mode == 'clay':
            for ob in bodies:
                for i in range(len(ob.data.materials)): ob.data.materials[i] = clay
        for view, position in views.items():
            cam.location = position
            forward = (target - cam.location).normalized()
            right = forward.cross(Vector((0, 1, 0))).normalized()
            up = right.cross(forward)
            cam.rotation_euler = Matrix((right, up, -forward)).transposed().to_euler()
            scene.render.filepath = str((REPORT / (species + '-' + mode + '-' + view + '.png')).resolve())
            bpy.ops.render.render(write_still=True)

    for ob, mats in zip(bodies, original):
        for i, m in enumerate(mats): ob.data.materials[i] = m

    # Face close-up render
    camdata.ortho_scale = 0.44
    aim = Vector((0, .15, -.38))
    cam.location = (.55, .32, -.92)
    forward = (aim - cam.location).normalized()
    right = forward.cross(Vector((0, 1, 0))).normalized()
    up = right.cross(forward)
    cam.rotation_euler = Matrix((right, up, -forward)).transposed().to_euler()
    scene.render.filepath = str((REPORT / (species + '-face-closeup.png')).resolve())
    bpy.ops.render.render(write_still=True)

    camdata.ortho_scale = 2.45
    rig = bodies[0].parent
    for clip, frame in (('walk', 16), ('feed', 31), ('rest', 1)):
        rig.animation_data.action = bpy.data.actions[clip]
        scene.frame_set(frame)
        aim = target
        cam.location = (2.8, 1.8, -2.6)
        forward = (aim - cam.location).normalized()
        right = forward.cross(Vector((0, 1, 0))).normalized()
        up = right.cross(forward)
        cam.rotation_euler = Matrix((right, up, -forward)).transposed().to_euler()
        scene.render.filepath = str((REPORT / (species + '-pose-' + clip + '.png')).resolve())
        bpy.ops.render.render(write_still=True)

    rig.animation_data.action = None
    for b in rig.pose.bones: b.matrix_basis = Matrix.Identity(4)
    print('ANATOMICAL_GUARDIAN_COMPLETE', species)
