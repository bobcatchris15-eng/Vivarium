"""Bounded anatomical guardian authoring, image PBR, explicit skinning and audits.

Authored for the Armored Forest Pangolin Guardian (Smutsia gigantea / Manis inspiration
with grounded fantastical forest-guardian armor specializations).
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
        # pixels are provided in linear RGB; convert once to sRGB values and save via
        # Non-Color buffer so Blender does not apply a second sRGB gamma curve on save.
        pixels = pixels.copy()
        rgb = np.clip(pixels[:, :, :3], 0.0, 1.0)
        pixels[:, :, :3] = np.where(
            rgb <= 0.0031308,
            12.92 * rgb,
            1.055 * (np.maximum(rgb, 0.0) ** (1.0 / 2.4)) - 0.055,
        )
    im = bpy.data.images.new(name, width=w, height=h, alpha=True)
    if noncolor or encode_linear_color:
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
    if not name.startswith('guardian_'):
        alt_path = directory / ('guardian_' + name + '.png')
        try:
            shutil.copyfile(filepath, str(alt_path.resolve()))
        except Exception:
            pass
    return im


def pbr(name, directory, kind, base, rough=.5):
    """High-fidelity procedural PBR textures for stratified keratin armor, mammalian skin, and horn."""
    n = 2048 if kind in ('pangolin_keratin', 'skin', 'snake', 'ventral') else 1024
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
        # 16-cell stratified palette:
        # - Cells 0..7: Classic rich Smutsia umber/walnut/bronze carapace shields
        # - Cells 8..11: Ancient dorsal crest & pauldron plates with warm gold-amber keels & subtle moss/lichen in basal grooves
        # - Cells 12..15: Dark obsidian-bronze cranial visor, limb bracers, and razor tail-blade scutes
        cell_tint = np.array([
            [1.00, 0.98, 0.94], [1.08, 1.02, 0.92], [0.92, 0.88, 0.86], [1.12, 1.04, 0.90],
            [0.96, 0.94, 0.92], [1.05, 1.00, 0.93], [0.89, 0.86, 0.84], [1.10, 1.02, 0.91],
            [1.06, 1.04, 0.92], [1.14, 1.08, 0.90], [0.98, 1.02, 0.90], [1.10, 1.05, 0.88],
            [0.76, 0.72, 0.68], [0.82, 0.77, 0.72], [0.72, 0.68, 0.65], [0.85, 0.80, 0.74],
        ])
        seed = cell_seeds[cell_id]

        s = (u - 0.5) * 2.0
        dist_center = np.abs(s)
        fan_u = s * (1.0 + 0.38 * v)

        # Primary longitudinal ridges (9-11 deep fluted keratin keels per scale)
        ridge_freq = 9.0 + (cell_id % 3) * 1.0
        primary_ridges = np.cos(fan_u * math.pi * ridge_freq) * 0.5 + 0.5
        primary_ridges = primary_ridges ** 1.85

        # Prominent central longitudinal carina (keel)
        keel_boost = np.where((cell_id >= 8) & (cell_id < 12), 1.45, 1.15)
        central_keel = np.exp(-((s * 4.2) ** 2)) * np.sin(v * math.pi * 0.96) * keel_boost

        # Fine micro-fibrous keratin striae (44-52 longitudinal horn fibrils)
        fine_freq = 44.0 + (cell_id % 5) * 2.0
        fine_striae = np.sin(fan_u * math.pi * fine_freq + np.sin(v * 28.0 + seed) * 0.35) * 0.5 + 0.5

        # Concentric growth increments / rings following ogive arch
        ring_curve = v - 0.22 * (s ** 2)
        growth_rings = np.sin(ring_curve * math.pi * 22.0 + seed) * 0.5 + 0.5

        # Weathered, sun-bleached horn trailing margin (outer 16% of visible scale tip)
        edge_zone = np.clip((v - 0.80 + 0.08 * (s ** 2)) / 0.16, 0.0, 1.0) ** 1.35
        edge_notches = np.sin(s * math.pi * 9.0 + seed) * np.cos(s * math.pi * 19.0 - seed * 0.5)
        edge_wear = np.clip(edge_zone * (0.88 + 0.24 * edge_notches), 0.0, 1.0)

        # Basal root & overlap shadow extending to v=0.60 so visible emerging scale bases are rich dark espresso
        basal_shadow = np.clip(1.0 - v / 0.60, 0.0, 1.0) ** 1.35
        crevice_mask = (1.0 - primary_ridges) ** 1.8 * np.clip((v - 0.15) / 0.68, 0.0, 1.0) * (1.0 - edge_wear)
        dust_noise = np.sin(u * math.pi * 59.0 + v * math.pi * 41.0 + seed) * 0.5 + 0.5
        dust = np.clip(crevice_mask * (0.60 + 0.40 * dust_noise), 0.0, 1.0)

        # Symbiotic ancient forest moss/lichen micro-patina in deep grooves of dorsal crest plates (cells 8..11)
        is_crest_cell = ((cell_id >= 8) & (cell_id < 12)).astype(np.float32)
        moss_mask = (
            is_crest_cell
            * np.clip(1.0 - np.abs(v - 0.48) / 0.26, 0.0, 1.0) ** 1.4
            * ((1.0 - primary_ridges) ** 1.6)
            * np.clip(np.sin(u * 47.0 + seed) * np.cos(v * 39.0 - seed) + 0.35, 0.0, 1.0)
        )

        # True linear RGB palette calibrated to real Smutsia & Manis keratin scales (ref_scales.png)
        # Shifted so the exposed distal half (v = 0.45..0.94) displays deep espresso -> roasted walnut -> horn rim
        c_root = srgb_to_linear(np.array([18, 12, 10]) / 255.0)     # Deep obsidian-espresso basal & overlap zone
        c_lower = srgb_to_linear(np.array([42, 27, 19]) / 255.0)    # Dark roasted chocolate-umber emerging plate
        c_body = srgb_to_linear(np.array([76, 52, 36]) / 255.0)     # Rich mahogany-walnut mid-plate
        c_ridge = srgb_to_linear(np.array([116, 84, 58]) / 255.0)   # Burnished bronze-amber striae & keel
        c_edge = srgb_to_linear(np.array([172, 142, 106]) / 255.0)  # Weathered pale horn/ivory cusp rim
        c_dust = srgb_to_linear(np.array([104, 78, 56]) / 255.0)    # Forest ochre loam in grooves
        c_moss = srgb_to_linear(np.array([52, 70, 36]) / 255.0)     # Velvety forest moss in ancient dorsal crevices

        # Three-stage longitudinal color gradient across the visible scale profile
        b1 = np.clip((v - 0.25) / 0.32, 0.0, 1.0)[:, :, None]
        b2 = np.clip((v - 0.52) / 0.32, 0.0, 1.0)[:, :, None]
        base_grad = c_root[None, None, :] * (1.0 - b1) + c_lower[None, None, :] * b1
        base_grad = base_grad * (1.0 - b2) + c_body[None, None, :] * b2

        ridge_blend = np.clip(primary_ridges * 0.30 + fine_striae * 0.15 + central_keel * 0.40, 0.0, 1.0)[:, :, None]
        albedo = base_grad * (1.0 - ridge_blend * 0.52) + c_ridge[None, None, :] * (ridge_blend * 0.52)
        albedo *= (0.88 + 0.14 * growth_rings - 0.28 * basal_shadow)[:, :, None]
        albedo = albedo * (1.0 - dust[:, :, None] * 0.22) + c_dust[None, None, :] * (dust[:, :, None] * 0.22)
        albedo = albedo * (1.0 - moss_mask[:, :, None] * 0.50) + c_moss[None, None, :] * (moss_mask[:, :, None] * 0.50)
        albedo = albedo * (1.0 - edge_wear[:, :, None] * 0.82) + c_edge[None, None, :] * (edge_wear[:, :, None] * 0.82)

        albedo = np.clip(albedo * cell_tint[cell_id], 0.0, 1.0)
        rgba = np.ones((n, n, 4), dtype=np.float32)
        rgba[:, :, :3] = albedo

        # Roughness map: polished central keel & worn horn tips (0.30..0.42), matte dusty/mossy basal crevices (0.68..0.82)
        val = np.clip(
            0.46
            - 0.12 * edge_wear
            - 0.11 * primary_ridges
            - 0.12 * central_keel
            + 0.20 * dust
            + 0.26 * moss_mask
            + 0.08 * basal_shadow,
            0.28,
            0.84,
        )

        # High-relief tangent normal map for deep keratin fluting, central carina, and growth annuli
        height = (
            0.020 * (1.0 - np.clip(dist_center, 0.0, 1.0) ** 1.6) * np.sin(v * math.pi * 0.95)
            + 0.0085 * primary_ridges * (1.0 - edge_wear * 0.25)
            + 0.0075 * central_keel
            + 0.0022 * fine_striae
            + 0.0024 * growth_rings
            - 0.0040 * edge_wear
        )
        dy, dx = np.gradient(height * 420.0)
        nx = -dx
        ny = -dy
        nz = np.ones_like(nx)
        norm = np.sqrt(nx * nx + ny * ny + nz * nz)
        normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)

    elif kind == 'skin':
        # Mammalian hide with dedicated moist cobbled rhinarium (nose pad) in UV quadrant [0..0.28, 0..0.28]
        rng = np.random.default_rng(1892)
        noise1 = rng.normal(0, 1, (n, n))
        for _ in range(3):
            noise1 = (
                noise1 * 4
                + np.roll(noise1, 1, 0)
                + np.roll(noise1, -1, 0)
                + np.roll(noise1, 1, 1)
                + np.roll(noise1, -1, 1)
            ) / 8.0
        noise1 /= max(float(noise1.std()), 1e-8)

        pebble = np.sin(x * TAU * 42.0 + noise1 * 2.2) * np.sin(y * TAU * 42.0 + np.roll(noise1, 7, 0) * 2.2)
        micro_pores = np.sin(x * TAU * 128.0 + noise1) * np.sin(y * TAU * 128.0) * 0.5 + 0.5
        wrinkles = np.sin(y * TAU * 14.0 + noise1 * 1.8) * 0.5 + 0.5
        # Sparse sensory dermal bristles on snout/cheeks
        bristle = (
            np.exp(-(((x * 64.0) % 1.0 - 0.5) ** 2) / 0.015)
            * np.clip(1.0 - ((y * 18.0) % 1.0), 0.0, 1.0)
            * (noise1 > 0.65).astype(np.float32)
        )

        # Rich dark mammalian hide palette (no double-gamma washout)
        skin_base = srgb_to_linear(np.array([58, 43, 37]) / 255.0)      # Earthy charcoal-taupe dermis
        facial_tone = srgb_to_linear(np.array([34, 25, 22]) / 255.0)    # Dark leathery rostral & orbital hide
        crease_tone = srgb_to_linear(np.array([22, 15, 14]) / 255.0)    # Deep wrinkle & fold shadow
        grain_tone = srgb_to_linear(np.array([82, 62, 53]) / 255.0)     # Raised dermal pebble & bristle highlight
        rhinarium_tone = srgb_to_linear(np.array([22, 15, 17]) / 255.0) # Moist dark plum-obsidian nose pad

        c_blend = np.clip(0.52 + 0.24 * noise1 + 0.18 * pebble - 0.15 * (1.0 - wrinkles), 0.0, 1.0)[:, :, None]
        albedo = crease_tone[None, None, :] * (1.0 - c_blend) + skin_base[None, None, :] * c_blend
        grain_amt = np.clip(0.22 * pebble + 0.38 * bristle, 0.0, 1.0)[:, :, None]
        albedo = albedo * (1.0 - grain_amt) + grain_tone[None, None, :] * grain_amt

        # Seamless longitudinal facial darkening (smooth low-frequency wave feathering)
        smooth_wave = 0.004 * np.cos(x * TAU * 2.0) + 0.002 * np.sin(x * TAU * 5.0)
        face_mask = np.clip((0.18 - y + smooth_wave) / 0.10, 0.0, 1.0)[:, :, None]
        albedo = albedo * (1.0 - 0.65 * face_mask) + facial_tone[None, None, :] * (0.65 * face_mask)

        # Seamless organic transition into the wet cobbled rhinarium nose pad at snout apex (y < 0.046)
        rhino_mask = np.clip((0.045 - y + smooth_wave) / 0.020, 0.0, 1.0)[:, :, None]
        rhino_cobble = (np.sin(x * TAU * 95.0 + noise1) * np.sin(y * TAU * 95.0))[:, :, None] * 0.5 + 0.5
        rhino_col = rhinarium_tone[None, None, :] * (0.84 + 0.24 * rhino_cobble)
        albedo = albedo * (1.0 - rhino_mask) + rhino_col * rhino_mask
        albedo = np.clip(albedo, 0.0, 1.0)

        rgba = np.ones((n, n, 4), dtype=np.float32)
        rgba[:, :, :3] = albedo

        skin_rough = np.clip(0.68 + 0.08 * noise1 + 0.06 * (1.0 - pebble) - 0.05 * micro_pores - 0.08 * face_mask.squeeze(), 0.48, 0.86)
        rhino_rough = np.clip(0.24 + 0.10 * (1.0 - rhino_cobble.squeeze()), 0.18, 0.36)
        val = skin_rough * (1.0 - rhino_mask.squeeze()) + rhino_rough * rhino_mask.squeeze()

        height = 0.0018 * noise1 + 0.0016 * pebble + 0.0012 * wrinkles + 0.0015 * bristle
        dy, dx = np.gradient(height * 260.0)
        nx = -dx
        ny = -dy
        nz = np.ones_like(nx)
        norm = np.sqrt(nx * nx + ny * ny + nz * nz)
        normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)

    elif kind == 'claw':
        # Fossorial excavating horn: dark sepia ungual base -> striated amber mid-shaft -> ivory-horn digging blade
        striae = np.sin(x * TAU * 28.0 + np.sin(y * TAU * 4.0) * 0.35) * 0.5 + 0.5
        fine_lines = np.sin(x * TAU * 130.0) * 0.5 + 0.5
        growth_bands = np.sin(y * TAU * 16.0) * 0.5 + 0.5
        b_mid = np.clip(y / 0.42, 0.0, 1.0)[:, :, None]
        b_tip = np.clip((y - 0.45) / 0.50, 0.0, 1.0)[:, :, None]

        c_root = srgb_to_linear(np.array([38, 25, 18]) / 255.0)   # Dark espresso ungual collar
        c_mid = srgb_to_linear(np.array([108, 78, 52]) / 255.0)   # Warm amber-horn shaft
        c_tip = srgb_to_linear(np.array([196, 168, 132]) / 255.0) # Soil-polished pale horn digging tip
        albedo = c_root[None, None, :] * (1.0 - b_mid) + c_mid[None, None, :] * b_mid
        albedo = albedo * (1.0 - b_tip) + c_tip[None, None, :] * b_tip
        albedo *= (0.90 + 0.12 * striae + 0.05 * fine_lines + 0.05 * growth_bands)[:, :, None]
        albedo = np.clip(albedo, 0.0, 1.0)

        rgba = np.ones((n, n, 4), dtype=np.float32)
        rgba[:, :, :3] = albedo
        val = np.clip(0.34 + 0.18 * (1.0 - b_mid.squeeze()) - 0.06 * b_tip.squeeze() - 0.05 * striae, 0.22, 0.58)

        height = 0.0032 * striae + 0.0010 * fine_lines + 0.0012 * growth_bands
        dy, dx = np.gradient(height * 240.0)
        nx = -dx
        ny = -dy
        nz = np.ones_like(nx)
        norm = np.sqrt(nx * nx + ny * ny + nz * nz)
        normals = np.stack([nx / norm * 0.5 + 0.5, ny / norm * 0.5 + 0.5, nz / norm * 0.5 + 0.5], axis=-1)
    else:
        rgba = np.ones((n, n, 4), dtype=np.float32)
        rgba[:, :, :3] = np.clip(np.array(base)[None, None, :], 0, 1)
        val = np.ones((n, n), dtype=np.float32) * rough
        normals = np.ones((n, n, 3), dtype=np.float32)
        normals[:, :, :2] = 0.5
        normals[:, :, 2] = 1.0

    color = image(name + '_basecolor', rgba, directory, encode_linear_color=True)
    r = np.ones_like(rgba)
    r[:, :, :3] = val[:, :, None]
    roughim = image(name + '_roughness', r, directory, True)
    nm = np.ones_like(rgba)
    nm[:, :, :3] = normals
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
    norm_node.inputs['Strength'].default_value = 1.15 if kind == 'pangolin_keratin' else 0.65
    links.new(tex.outputs['Color'], norm_node.inputs['Color'])
    links.new(norm_node.outputs['Normal'], bsdf.inputs['Normal'])

    if kind == 'pangolin_keratin':
        bsdf.inputs['Specular IOR Level'].default_value = 0.52
    elif kind == 'claw':
        bsdf.inputs['Specular IOR Level'].default_value = 0.48
    else:
        bsdf.inputs['Specular IOR Level'].default_value = 0.35

    return mat


def solid(name, color, rough=.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    b.inputs['Base Color'].default_value = (*color, 1)
    b.inputs['Roughness'].default_value = rough
    if 'Eye' in name:
        b.inputs['Specular IOR Level'].default_value = 0.90
    return m


def _sanitize_mesh_and_weights(ob):
    """Triangulates, removes ultra-tiny/degenerate triangles, and normalizes skin weights to 1.0."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    tiny = [f for f in bm.faces if f.calc_area() < 2.0e-9]
    if tiny:
        bmesh.ops.delete(bm, geom=tiny, context='FACES')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data)
    bm.free()
    ob.data.update()

    if ob.vertex_groups:
        for v in ob.data.vertices:
            pairs = [(g.group, g.weight) for g in v.groups if math.isfinite(g.weight) and g.weight > 1e-6]
            if not pairs:
                ob.vertex_groups[0].add([v.index], 1.0, 'REPLACE')
                continue
            pairs.sort(key=lambda item: -item[1])
            keep = pairs[:4]
            keep_ids = {gid for gid, _ in keep}
            for g in list(v.groups):
                if g.group not in keep_ids:
                    ob.vertex_groups[g.group].remove([v.index])
            total = sum(w for _, w in keep) or 1.0
            for gid, w in keep:
                ob.vertex_groups[gid].add([v.index], w / total, 'REPLACE')


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
        groups = {b.name: ob.vertex_groups.new(name=b.name) for b in rig.data.bones}
        for i, weights in enumerate(self.weights):
            for bname, w in weights.items():
                if w > 1e-5 and bname in groups:
                    groups[bname].add([i], w, 'REPLACE')
        _sanitize_mesh_and_weights(ob)
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
    if z <= centers[0][1]:
        return {centers[0][0]: 1.0}
    if z >= centers[-1][1]:
        return {centers[-1][0]: 1.0}
    for (a, za), (b, zb) in zip(centers, centers[1:]):
        if za <= z <= zb:
            if a == b:
                return {a: 1.0}
            t = (z - za) / (zb - za)
            t = t * t * (3.0 - 2.0 * t)
            return {a: 1.0 - t, b: t}
    return {centers[-1][0]: 1.0}


def interp(profile, z):
    for a, b in zip(profile, profile[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / max(1e-6, (b[0] - a[0]))
            t = t * t * (3.0 - 2.0 * t)
            return [a[k] * (1.0 - t) + b[k] * t for k in range(1, 4)]
    return list(profile[0][1:]) if z < profile[0][0] else list(profile[-1][1:])


def surface_point(profile, z, a):
    """Evaluates a point on the anatomical body loft at longitudinal z and azimuth a (0 = +Y dorsal)."""
    y, rx, ry = interp(profile, z)
    c = math.cos(a)
    s = math.sin(a)
    yc = c if c >= -0.72 else -0.72 + (c + 0.72) * 0.38
    x = rx * s
    y_val = y + ry * yc
    # Subtle myrmecophagous rostral shaping: slightly arched nasal bridge & recessed oral furrow under snout
    if z < -0.36:
        snout_t = max(0.0, min(1.0, (-0.36 - z) / 0.17))
        if c < -0.55:
            # Recessed edentulous mandibular cleft under the snout tip
            cleft = max(0.0, (-c - 0.55) / 0.45) * math.sin(math.pi * snout_t)
            y_val += 0.0035 * cleft
    return Vector((x, y_val, z))


def surface_frame(profile, z, a, dz_eps=0.012, da_eps=0.04):
    """Computes exact 3D surface point, circumferential tangent (across), longitudinal tangent (along), and outward normal."""
    p = surface_point(profile, z, a)
    p_z1 = surface_point(profile, min(profile[-1][0], z + dz_eps), a)
    p_z0 = surface_point(profile, max(profile[0][0], z - dz_eps), a)
    along = (p_z1 - p_z0).normalized()

    p_a1 = surface_point(profile, z, a + da_eps)
    p_a0 = surface_point(profile, z, a - da_eps)
    across = (p_a1 - p_a0).normalized()

    # In X-right, Y-up, +Z backward: at dorsal top (a=0), across=(1,0,0), along=(0,dy,1) -> along.cross(across) = (0,1,-dy) (+Y up!)
    normal = along.cross(across).normalized()
    across = normal.cross(along).normalized()
    return p, across, along, normal


def loft(g, profile, centers, step=.015, radial=48, mat=1):
    """Lofts continuous anatomical body envelope with smooth rounded rhinarium tip and tail apex."""
    zs = np.linspace(profile[0][0], profile[-1][0], int((profile[-1][0] - profile[0][0]) / step) + 1)
    rows = []
    for z in zs:
        row = []
        v_uv = float((z - zs[0]) / max(1e-5, (zs[-1] - zs[0])))
        for j in range(radial + 1):
            a = TAU * j / radial
            p = surface_point(profile, float(z), a)
            uv = (j / radial, v_uv)
            row.append(g.vertex(p, uv, longitudinal(float(z), centers)))
        rows.append(row)
    for ra, rb in zip(rows, rows[1:]):
        for j in range(radial):
            g.face((ra[j], rb[j], rb[j + 1], ra[j + 1]), mat)

    # Smooth hemispherical nose cap and tail tip cap instead of flat ngon disks
    p_front = surface_point(profile, float(zs[0]), 0.0) - Vector((0.0, 0.001, 0.004))
    i_front = g.vertex(p_front, (0.5, 0.0), longitudinal(float(zs[0]), centers))
    for j in range(radial):
        g.face((rows[0][j + 1], rows[0][j], i_front), mat)

    p_back = surface_point(profile, float(zs[-1]), 0.0) + Vector((0.0, 0.0, 0.005))
    i_back = g.vertex(p_back, (0.5, 1.0), longitudinal(float(zs[-1]), centers))
    for j in range(radial):
        g.face((rows[-1][j], rows[-1][j + 1], i_back), mat)


def plate(
    g,
    anchor,
    across,
    along,
    normal,
    width,
    length,
    thickness,
    weight_fn,
    cell=0,
    mat=0,
    keel_boost=1.0,
    cusp_point=0.46,
    pitch_out=0.06,
):
    """Closed, curved, 3D imbricated keratin shield with broad ogive-fan contour,
    central longitudinal keel (carina), transverse & longitudinal shell wrap, and shingled pitch.
    """
    anchor = Vector(anchor)
    across = Vector(across).normalized()
    along = Vector(along).normalized()
    normal = Vector(normal).normalized()

    # Slight outward pitch at the root combined with downward longitudinal wrap at the tip
    # so each scale arches cleanly over the next scale's root and hugs its dorsal plate.
    along_pitched = (along + normal * pitch_out).normalized()
    normal_pitched = along_pitched.cross(across).normalized()

    rows = []
    bottoms = []
    nx = 6
    ny = 7

    for j in range(ny + 1):
        t = j / ny
        ra = []
        rb = []
        for i in range(nx + 1):
            u = i / nx
            s = (u - 0.5) * 2.0  # [-1, 1]
            abs_s = abs(s)

            # Authentic broad pangolin shield outline (ref_scales.png):
            # Expands from embedded root (t=0) to broad rounded shoulders (t=0.52), then tapers
            # in a convex ogive fan toward the distal margin at t=1.0.
            if t < 0.52:
                u_t = t / 0.52
                w_factor = 0.78 + 0.22 * math.sin(u_t * math.pi * 0.5)
            else:
                u_t = (t - 0.52) / 0.48
                w_factor = 1.0 - (1.0 - cusp_point) * (u_t ** 1.55)

            # Broad convex trailing margin projecting forward at center (s=0)
            arch_proj = 1.0 - 0.24 * (abs_s ** 1.75) + 0.014 * math.cos(s * math.pi * 3.0)
            d_along = length * (t * (0.76 + 0.24 * arch_proj))

            # Transverse dome camber + 3D longitudinal keel ridge (carina) + primary fluted ribs
            camber = (1.0 - s * s) * thickness * 1.10 * math.sin(t * math.pi * 0.92)
            keel = (0.0020 * keel_boost) * math.exp(-((s * 3.2) ** 2)) * (math.sin(t * math.pi * 0.88) ** 0.8)
            flutes = 0.00045 * math.cos(s * math.pi * 5.0) * (1.0 - abs_s * 0.5) * math.sin(t * math.pi)
            # Root embeds into dermis at t=0; transverse & longitudinal wrap hugs curved anatomy tightly
            root_embed = -0.0022 * ((1.0 - t) ** 2.2)
            transverse_wrap = -width * 0.12 * (s * s) * (0.25 + 0.75 * t)
            tip_hug = -length * 0.075 * (t ** 2.1) - 0.0018 * (t ** 2.4) * (0.4 + 0.6 * abs_s)

            h_top = root_embed + camber + keel + flutes + transverse_wrap + tip_hug
            local_thk = thickness * (0.85 - 0.36 * (t ** 1.3)) * (1.0 - 0.32 * (abs_s ** 2))
            h_bot = h_top - max(0.0011, local_thk)

            lat_offset = s * width * 0.5 * w_factor
            p_top = anchor + across * lat_offset + along_pitched * d_along + normal_pitched * h_top
            p_bot = anchor + across * (lat_offset * 0.96) + along_pitched * (d_along * 0.985) + normal_pitched * h_bot

            u_atlas = ((cell % 4) + u * 0.88 + 0.06) / 4.0
            v_atlas = ((cell // 4) + t * 0.88 + 0.06) / 4.0

            w_top = weight_fn(p_top) if callable(weight_fn) else weight_fn
            w_bot = weight_fn(p_bot) if callable(weight_fn) else weight_fn

            ra.append(g.vertex(p_top, (u_atlas, v_atlas), w_top))
            rb.append(g.vertex(p_bot, (u_atlas, v_atlas), w_bot))
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
    """Solid, laterally compressed, longitudinally keeled fossorial digging sickle claw."""
    rows = []
    base_lateral = Vector((math.cos(fan_angle) * side, 0.0, math.sin(fan_angle))).normalized()
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
        if norm.y < 0:
            norm = -norm

        rx = rx_list[k]
        ry = ry_list[k]
        row = []
        for i in range(15):
            u = i / 14
            a = u * TAU
            # Dorsal longitudinal ridge & flattened ventral sole furrow of a fossorial digging claw
            dorsal_keel = 1.0 + 0.24 * max(0.0, math.sin(a)) ** 2 - 0.15 * max(0.0, -math.sin(a))
            flute = 1.0 + 0.04 * math.cos(a * 6.0)
            offset = lat * (math.cos(a) * rx * flute) + norm * (math.sin(a) * ry * dorsal_keel)
            row.append(g.vertex(p + offset, (u, k / (n_pts - 1)), {bone: 1.0}))
        rows.append(row)

    for ra, rb in zip(rows, rows[1:]):
        for i in range(14):
            g.face((ra[i], ra[i + 1], rb[i + 1], rb[i]), mat)

    # Cap root and sharp distal apex without n-gon degeneracies
    p_root = Vector(points[0])
    i_root = g.vertex(p_root, (0.5, 0.0), {bone: 1.0})
    for i in range(14):
        g.face((rows[0][i + 1], rows[0][i], i_root), mat)

    p_tip = Vector(points[-1]) + (Vector(points[-1]) - Vector(points[-2])).normalized() * 0.002
    i_tip = g.vertex(p_tip, (0.5, 1.0), {bone: 1.0})
    for i in range(14):
        g.face((rows[-1][i], rows[-1][i + 1], i_tip), mat)


def tube(g, points, radii, bone, mat=0, segments=12, v_range=(0.06, 0.16)):
    rows = []
    weights = bone if isinstance(bone, dict) else {bone: 1.0}
    v0, v1 = v_range
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
        v_uv = v0 + (v1 - v0) * (k / max(1, len(points) - 1))
        for i in range(segments + 1):
            u = i / segments
            a = u * TAU
            row.append(g.vertex(p + radii[k] * (math.cos(a) * axis + math.sin(a) * up), (u, v_uv), weights))
        rows.append(row)
    for ra, rb in zip(rows, rows[1:]):
        for i in range(segments):
            g.face((ra[i], ra[i + 1], rb[i + 1], rb[i]), mat)
    i_start = g.vertex(Vector(points[0]), (0.5, v0), weights)
    i_end = g.vertex(Vector(points[-1]), (0.5, v1), weights)
    for i in range(segments):
        g.face((rows[0][i + 1], rows[0][i], i_start), mat)
        g.face((rows[-1][i], rows[-1][i + 1], i_end), mat)


def ellipsoid(g, center, radius, bone, mat=0, segments=20, rings=12):
    center = Vector(center)
    radius = Vector(radius)
    weights = bone if isinstance(bone, dict) else {bone: 1.0}
    rows = []
    top = g.vertex(center + Vector((0, radius.y, 0)), (.5, 0), weights)
    for j in range(1, rings):
        row = []
        v = j / rings
        phi = math.pi * v
        for i in range(segments + 1):
            u = i / segments
            a = TAU * u
            p = center + Vector((math.sin(phi) * math.cos(a) * radius.x, math.cos(phi) * radius.y, math.sin(phi) * math.sin(a) * radius.z))
            row.append(g.vertex(p, (u, v), weights))
        rows.append(row)
    bottom = g.vertex(center - Vector((0, radius.y, 0)), (.5, 1), weights)
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


def limb_loft(g, label, h, k, w, front=True):
    """Muscular fossorial limb volume flowing smoothly into the wrist/ankle and metapodial paw."""
    side = 1 if h.x > 0 else -1
    paw_fwd = Vector((w.x - 0.005 * side, 0.032, w.z - (0.036 if front else 0.028)))
    points = [
        Vector((.118 * side, .322, h.z)),
        h,
        h.lerp(k, .32),
        h.lerp(k, .68),
        k,
        k.lerp(w, .34),
        k.lerp(w, .70),
        w,
        paw_fwd,
    ]
    radii = [.084, .080, .072, .062, .054, .048, .043, .038, .034]
    rows = []
    frames = []
    radial = 22
    n_pts = len(points)
    for j, (p, radius) in enumerate(zip(points, radii)):
        tangent = (points[min(j + 1, n_pts - 1)] - points[max(0, j - 1)]).normalized()
        across = tangent.cross(Vector((0, 0, 1))).normalized()
        if across.x * side < 0:
            across = -across
        out = tangent.cross(across).normalized()
        if out.z > 0:
            out = -out
        rows.append([])
        frames.append((p, across, out, radius))
        if j <= 3:
            weights = {label + 'Upper': 1.0}
        elif j == 4:
            weights = {label + 'Upper': .40, label + 'Lower': .60}
        elif j <= 6:
            weights = {label + 'Lower': 1.0}
        elif j == 7:
            weights = {label + 'Lower': .25, label + 'Foot': .75}
        else:
            weights = {label + 'Foot': 1.0}
        for i in range(radial + 1):
            u = i / radial
            a = u * TAU
            pos = p + radius * (math.cos(a) * across + math.sin(a) * out * .90)
            rows[-1].append(g.vertex(pos, (0.32 + 0.65 * u, 0.30 + 0.65 * (j / (n_pts - 1))), weights))
    for ra, rb in zip(rows, rows[1:]):
        for i in range(radial):
            g.face((ra[i], ra[i + 1], rb[i + 1], rb[i]), 1)
    i_top = g.vertex(points[0], (0.5, 0.30), {label + 'Upper': 1.0})
    i_bot = g.vertex(points[-1], (0.5, 0.95), {label + 'Foot': 1.0})
    for i in range(radial):
        g.face((rows[0][i + 1], rows[0][i], i_top), 1)
        g.face((rows[-1][i], rows[-1][i + 1], i_bot), 1)
    return frames


def continuous_skin(g, name, mats, rig, profile):
    """Voxel-union anatomical volumes into continuous seamless mammalian hide."""
    ob = g.object(name, mats, rig)
    ob.modifiers.clear()
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)

    mod = ob.modifiers.new('ContinuousAnatomicalUnion', 'REMESH')
    mod.mode = 'VOXEL'
    mod.voxel_size = .0062
    mod.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=mod.name)

    smooth = ob.modifiers.new('SoftTissueTransitions', 'SMOOTH')
    smooth.factor = .58
    smooth.iterations = 2
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    dec = ob.modifiers.new('SurfaceRetopologyBudget', 'DECIMATE')
    dec.ratio = .30
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

    # Exact foot anchors for deterministic planted-sole lock
    foot_anchors = {}
    for side, s_lbl in ((1, 'L'), (-1, 'R')):
        for front, p_lbl, fz in ((True, 'F', -0.08), (False, 'H', 0.38)):
            _, _, w_pos = limb_definition(side, front, fz)
            foot_anchors[p_lbl + s_lbl] = w_pos

    for v in ob.data.vertices:
        # Guarantee planted sole vertices are 100% locked to their respective Foot bone
        locked_foot = None
        for f_label, w_pos in foot_anchors.items():
            if v.co.y < 0.036 and abs(v.co.x - w_pos.x) < 0.075 and abs(v.co.z - w_pos.z) < 0.085:
                locked_foot = f_label + 'Foot'
                break

        if locked_foot is not None:
            groups[locked_foot].add([v.index], 1.0, 'REPLACE')
        else:
            neighbors = tree.find_n(v.co, 4)
            weights = {}
            for _, index, distance in neighbors:
                influence = 1.0 / max(distance, .002) ** 2
                for bone, w in g.weights[index].items():
                    weights[bone] = weights.get(bone, 0.0) + w * influence
            selected = sorted(weights.items(), key=lambda item: -item[1])[:4]
            norm = sum(w for _, w in selected) or 1.0
            for bone, w in selected:
                if bone in groups:
                    groups[bone].add([v.index], w / norm, 'REPLACE')

        # Continuous conformal UV mapping (no discontinuous UV seams on snout)
        y_center, rx, ry = interp(profile, v.co.z)
        dx = v.co.x / max(0.01, rx)
        dy = (v.co.y - y_center) / max(0.01, ry)
        angle = math.atan2(dx, dy)
        u_skin = angle / TAU + 0.5
        v_skin = max(0.0, min(1.0, (v.co.z - z_min) / max(1e-5, (z_max - z_min))))
        coords.append((u_skin, v_skin))

    for p in ob.data.polygons:
        p.use_smooth = True
        p.material_index = 1
        for li in p.loop_indices:
            uv.data[li].uv = coords[ob.data.loops[li].vertex_index]

    _sanitize_mesh_and_weights(ob)
    mod = ob.modifiers.new('AnatomicalSkin', 'ARMATURE')
    mod.object = rig
    return ob


def pose_matrix(rig, name, offset=(0, 0, 0), angle=0, axis='Y'):
    if name not in rig.data.bones:
        return
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
        rig.pose.bones[name].matrix_basis = bone.convert_local_to_pose(
            desired, bone.matrix_local, parent_matrix=parent_matrix, parent_matrix_local=bone.parent.matrix_local, invert=True
        )
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
    rig.pose.bones[label + 'Foot'].matrix_basis = foot.convert_local_to_pose(
        desired, foot.matrix_local, parent_matrix=lower_matrix, parent_matrix_local=lower.matrix_local, invert=True
    )
    return hip, knee, target


def keyall(rig, frame):
    for b in rig.pose.bones:
        for channel in ('location', 'rotation_quaternion', 'scale'):
            b.keyframe_insert(channel, frame=frame)


def animate(rig, callback, names=('idle', 'walk', 'feed', 'rest')):
    modifiers = [m for ob in bpy.data.objects if ob.type == 'MESH' for m in ob.modifiers if m.type == 'ARMATURE']
    for mod in modifiers:
        mod.show_viewport = False
    for name in names:
        act = bpy.data.actions.new(name)
        rig.animation_data.action = act
        for f in range(1, 122):
            for b in rig.pose.bones:
                b.matrix_basis = Matrix.Identity(4)
            bpy.context.view_layer.update()
            callback(name, (f - 1) / 120, rig)
            bpy.context.view_layer.update()
            keyall(rig, f)
        for layer in act.layers:
            for action_strip in layer.strips:
                for bag in action_strip.channelbags:
                    for curve in bag.fcurves:
                        for point in curve.keyframe_points:
                            point.interpolation = 'LINEAR'
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, act)
        strip.name = name
        track.mute = True
    rig.animation_data.action = None
    for b in rig.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    bpy.context.scene.frame_set(1)
    for mod in modifiers:
        mod.show_viewport = True
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
                dz = (
                    (2 * swing ** 3 - 3 * swing ** 2 + 1) * stride * .5
                    + (-2 * swing ** 3 + 3 * swing ** 2) * (-stride * .5)
                    + (swing ** 3 - 2 * swing ** 2 + swing) * v
                    + (swing ** 3 - swing ** 2) * v
                )
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

    mats = [
        pbr('AmberKeratin', out, 'pangolin_keratin', (.306, .141, .058), .44),
        pbr('PangolinSkin', out, 'skin', (.285, .210, .175), .70),
        pbr('DarkClaw', out, 'claw', (.677, .548, .355), .36),
        solid('GlossEye', (.003, .002, .002), .03),
        solid('WarmIris', (.085, .048, .018), .18),
        solid('Mouth', (.022, .012, .012), .72),
        solid('Tongue', (.38, .11, .13), .38),
    ]

    # Authentic Smutsia gigantea / Manis anatomical profile with tapered rounded rhinarium tip
    profile = [
        (-0.542, 0.076, 0.008, 0.007),  # Rounded moist rhinarium apex
        (-0.525, 0.081, 0.022, 0.017),  # Rhinarium pad & valvular nostrils
        (-0.485, 0.104, 0.039, 0.030),  # Tapered myrmecophagous rostrum
        (-0.435, 0.136, 0.060, 0.045),  # Nasal bridge & pre-orbital snout
        (-0.380, 0.170, 0.082, 0.062),  # Orbital region & hooded eyes
        (-0.300, 0.240, 0.126, 0.100),  # Temporal crown & valvular ear pinnae
        (-0.200, 0.322, 0.182, 0.146),  # Nuchal collar / cervical transition
        (-0.080, 0.396, 0.232, 0.186),  # Scapular arch / shoulder girdle
        ( 0.080, 0.438, 0.250, 0.202),  # High-domed thoracic carapace peak
        ( 0.240, 0.422, 0.246, 0.196),  # Lumbar arch
        ( 0.380, 0.366, 0.226, 0.166),  # Pelvic vault & hindquarter arch
        ( 0.480, 0.292, 0.196, 0.110),  # Broad dorsoventrally compressed tail base
        ( 0.540, 0.242, 0.176, 0.094),  # Proximal muscular tail
        ( 0.600, 0.196, 0.156, 0.080),  # Tail seg 2
        ( 0.660, 0.152, 0.136, 0.066),  # Mid tail
        ( 0.720, 0.112, 0.116, 0.054),  # Tail seg 4
        ( 0.780, 0.076, 0.096, 0.042),  # Distal tail
        ( 0.840, 0.048, 0.074, 0.031),  # Pre-terminal tail
        ( 0.900, 0.028, 0.050, 0.021),  # Terminal tail
        ( 0.960, 0.016, 0.018, 0.011),  # Armored tail tip
    ]

    centers = (
        [('Head', -0.42), ('Neck', -0.26), ('Body0', -0.10), ('Body1', 0.08), ('Body2', 0.24), ('Body3', 0.38)]
        + [(f'Tail{i}', 0.48 + i * 0.060) for i in range(8)]
    )

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

    eye_x, eye_y, eye_z = 0.078, 0.168, -0.380
    for side, suffix in [(1, 'L'), (-1, 'R')]:
        for label in ['Blink_' + suffix, 'BlinkLower_' + suffix]:
            seg.append((label, (side * eye_x, eye_y + .010, eye_z), (side * eye_x, eye_y - .010, eye_z)))

    seg.append(('Tongue', (0, .076, -.49), (0, .076, -.535)))
    rig = rig_create('PangolinRig', seg)

    skin = Geometry()
    scales = Geometry()
    detail = Geometry()

    loft(skin, profile, centers, mat=1, radial=48)

    def body_weight_fn(pt):
        return longitudinal(pt.z, centers)

    # =========================================================================
    # 1. CONTINUOUS PHYLLOTACTIC / DIAMOND-IMBRICATED CARAPACE (Rostrum -> Tail Tip)
    # Uses exact 3D surface normals & longitudinal tangents (zero barrel hoops or gaps!)
    # =========================================================================
    z = -0.490
    row = 0
    while z < 0.935:
        y, rx, ry = interp(profile, z)
        if z < -0.22:
            # Cephalic & nuchal visor region (tightly shingled rostral & cranial shields)
            u_cep = max(0.0, min(1.0, (z - (-0.490)) / (-0.22 - (-0.490))))
            dz = 0.013 + 0.012 * u_cep
            arc = 2.05 + 1.40 * u_cep
            count = int(round(4.0 + 6.0 * u_cep))
            thk = 0.0028 + 0.0028 * u_cep
        elif z < 0.45:
            # Thoracic, scapular, lumbar & pelvic dome
            u_tor = max(0.0, min(1.0, (z - (-0.22)) / (0.45 - (-0.22))))
            arch_bell = math.sin(u_tor * math.pi)
            dz = 0.026 + 0.005 * arch_bell
            arc = 3.58 + 0.35 * arch_bell
            count = int(round(11.0 + 3.0 * arch_bell))
            thk = 0.0060 + 0.0020 * arch_bell
        else:
            # Broad muscular counterbalance tail tapering to armored apex
            u_tail = max(0.0, min(1.0, (z - 0.45) / (0.935 - 0.45)))
            dz = 0.027 * (1.0 - 0.35 * u_tail)
            arc = 2.98 * (1.0 - 0.18 * u_tail)
            count = max(4, int(round(10.0 * (1.0 - 0.62 * u_tail))))
            thk = 0.0062 * (1.0 - 0.38 * u_tail)

        stagger = 0.5 * (row % 2)
        n_slots = count + (1 if row % 2 == 1 else 0)
        da = arc / max(1, count)
        # Broad circumferential overlap so adjacent diagonal columns never leave bare skin stripes
        avg_r = 0.55 * rx + 0.45 * ry
        sc_wid = max(0.023, da * avg_r * (1.92 if z < -0.22 else 1.58))
        sc_len = dz * (2.75 if z < -0.22 else 2.48)

        for j in range(n_slots):
            a = -arc * 0.5 + (j + 0.5 - stagger) * da
            if abs(a) > arc * 0.54:
                continue

            anchor_s, across, along, normal = surface_frame(profile, z, a)
            anchor = anchor_s + normal * 0.0010

            # Clear orbital window & valvular ear aperture so scales frame (never clip) the eye & ear
            d_eye_L = (anchor - Vector((eye_x, eye_y, eye_z))).length
            d_eye_R = (anchor - Vector((-eye_x, eye_y, eye_z))).length
            if min(d_eye_L, d_eye_R) < 0.025:
                continue
            d_ear_L = (anchor - Vector((0.098, 0.195, -0.335))).length
            d_ear_R = (anchor - Vector((-0.098, 0.195, -0.335))).length
            if min(d_ear_L, d_ear_R) < 0.019:
                continue

            is_midline = abs(a) < da * 0.42
            is_margin = abs(a) > arc * 0.40

            # Grounded Fantastical Guardian Specializations:
            # 1) Median Vertebral Dragon-Crest Row: amplified central carina & ancient moss-grooved dorsal cells (8..11)
            # 2) Cranial Visor & Flank/Tail Margin Guard Plates: dark ironwood-bronze cells (12..15)
            if is_midline and z > -0.36:
                cell_idx = 8 + (row % 4)
                keel_b = 1.65
                cusp_pt = 0.34
                w_mult = 1.08
                l_mult = 1.10
                pitch = 0.075
            elif z < -0.30 or is_margin:
                cell_idx = 12 + ((row * 3 + j) % 4)
                keel_b = 1.25
                cusp_pt = 0.30 if is_margin else 0.48
                w_mult = 1.06
                l_mult = 1.06 if is_margin else 1.02
                pitch = 0.085 if is_margin else 0.045
            else:
                cell_idx = (row * 5 + j * 3) % 8
                keel_b = 1.05
                cusp_pt = 0.46
                w_mult = 1.02
                l_mult = 1.00
                pitch = 0.055

            plate(
                scales,
                anchor,
                across,
                along,
                normal,
                sc_wid * w_mult,
                sc_len * l_mult,
                thk,
                body_weight_fn,
                cell=cell_idx,
                mat=0,
                keel_boost=keel_b,
                cusp_point=cusp_pt,
                pitch_out=pitch,
            )

        z += dz
        row += 1

    # =========================================================================
    # 2. SUPRAORBITAL BROW VISOR & SUBORBITAL JUGAL CHEEK PLATES
    # Surface-anchored via surface_frame() so they hug the cranial dome cleanly
    # =========================================================================
    for side in (-1, 1):
        # Supraorbital brow visor arching directly above the eye socket
        for k_brow in range(4):
            t_b = k_brow / 3.0
            bz = -0.408 + t_b * 0.095
            a_brow = side * (0.94 + 0.14 * t_b)
            b_anchor_s, b_across, b_along, b_normal = surface_frame(profile, bz, a_brow)
            plate(
                scales,
                b_anchor_s + b_normal * 0.0012,
                b_across,
                b_along,
                b_normal,
                width=0.023 + 0.006 * t_b,
                length=0.032 + 0.010 * t_b,
                thickness=0.0042 + 0.0014 * t_b,
                weight_fn=body_weight_fn,
                cell=12 + k_brow,
                mat=0,
                keel_boost=1.40,
                cusp_point=0.38,
                pitch_out=0.055,
            )
        # Sub-orbital / jugal cheek guard plates below and behind the eye
        for k_chk in range(3):
            t_c = k_chk / 2.0
            cz = -0.368 + t_c * 0.070
            a_chk = side * (1.36 + 0.08 * t_c)
            c_anchor_s, c_across, c_along, c_normal = surface_frame(profile, cz, a_chk)
            plate(
                scales,
                c_anchor_s + c_normal * 0.0010,
                c_across,
                c_along,
                c_normal,
                width=0.022 + 0.005 * t_c,
                length=0.030 + 0.008 * t_c,
                thickness=0.0038,
                weight_fn=body_weight_fn,
                cell=13 + k_chk,
                mat=0,
                keel_boost=1.20,
                cusp_point=0.42,
                pitch_out=0.05,
            )

    # =========================================================================
    # 3. SERRATED LATERAL CAUDAL MACE-BLADE & VENTRAL TAIL PLATES
    # Turns the heavy counterbalance tail into an interlocking armored blade
    # =========================================================================
    tz = 0.46
    trow = 0
    while tz < 0.94:
        ty, trx, try_val = interp(profile, tz)
        u_t = (tz - 0.46) / (0.94 - 0.46)
        tstep = 0.030 * (1.0 - 0.28 * u_t)

        # Razor-keeled lateral saw-tooth marginal scutes along left & right edges of tail
        for l_side in (-1, 1):
            lat_anchor = Vector((trx * l_side * 0.94, ty - 0.006 * (1.0 - u_t), tz))
            lat_along = Vector((0.22 * l_side, -0.05, 0.97)).normalized()
            lat_normal = Vector((0.88 * l_side, 0.46, 0.08)).normalized()
            lat_across = lat_normal.cross(lat_along).normalized()
            lat_len = tstep * 2.40
            lat_wid = max(0.022, 0.048 * (1.0 - 0.56 * u_t))
            plate(
                scales,
                lat_anchor,
                lat_across,
                lat_along,
                lat_normal,
                width=lat_wid,
                length=lat_len,
                thickness=0.0060 * (1.0 - 0.35 * u_t),
                weight_fn=body_weight_fn,
                cell=12 + (trow % 4),
                mat=0,
                keel_boost=1.65,
                cusp_point=0.20,
                pitch_out=0.12,
            )

        # Ventrolateral sub-caudal armor plates so the underside/sides of the tail have zero bare gaps
        for l_side in (-1, 1):
            sub_a = l_side * 1.95
            p_sub, ac_sub, al_sub, n_sub = surface_frame(profile, tz, sub_a)
            plate(
                scales,
                p_sub + n_sub * 0.001,
                ac_sub,
                al_sub,
                n_sub,
                width=max(0.020, 0.044 * (1.0 - 0.55 * u_t)),
                length=tstep * 2.30,
                thickness=0.0048 * (1.0 - 0.35 * u_t),
                weight_fn=body_weight_fn,
                cell=(trow * 3 + (1 if l_side == 1 else 2)) % 8,
                mat=0,
                keel_boost=1.15,
                cusp_point=0.40,
                pitch_out=0.06,
            )

        tz += tstep
        trow += 1

    # =========================================================================
    # 4. LIMBS, IMBRICATED BRACER/GREAVE ARMOR, AND FOSSORIAL DIGGING PAWS
    # =========================================================================
    for side in (-1, 1):
        for front, prefix, pz in ((True, 'F', -0.08), (False, 'H', 0.38)):
            label = prefix + ('L' if side == 1 else 'R')
            upper = label + 'Upper'
            lower = label + 'Lower'
            foot = label + 'Foot'
            h, k, w = limb_definition(side, front, pz)
            frames = limb_loft(skin, label, h, k, w, front=front)

            def make_limb_weight_fn(q_val, up_b=upper, low_b=lower, ft_b=foot):
                if q_val < 3.4:
                    return {up_b: 1.0}
                elif q_val < 4.6:
                    t_w = (q_val - 3.4) / 1.2
                    return {up_b: 1.0 - t_w, low_b: t_w}
                elif q_val < 6.7:
                    return {low_b: 1.0}
                else:
                    t_w = min(1.0, (q_val - 6.7) / 1.3)
                    return {low_b: 1.0 - t_w, ft_b: t_w}

            # Dense diamond-shingled bracer/greave armor wrapping the entire anterior, dorsal & lateral limb
            n_lrows = 12
            for lrow in range(n_lrows):
                u_l = lrow / (n_lrows - 1)
                q = 0.50 + u_l * 7.40  # Spans from upper shoulder/thigh (0.50) down onto dorsal paw knuckles (7.90)
                lo = int(q)
                hi = min(len(frames) - 1, lo + 1)
                frac = q - lo
                p0, across0, out0, r0 = frames[lo]
                p1, across1, out1, r1 = frames[hi]
                p = p0.lerp(p1, frac)
                across = across0.lerp(across1, frac).normalized()
                radial_out = out0.lerp(out1, frac).normalized()
                radius = r0 * (1.0 - frac) + r1 * frac
                direction = (p1 - p0).normalized()

                n_cols = 7 if lrow < 8 else 6
                stagger_l = 0.5 * (lrow % 2)
                # Wrap from anterior-medial (-1.45 rad) across anterior/dorsal (0) to posterior-lateral (+1.45 rad)
                for col in range(n_cols):
                    ang = -1.42 + (col + 0.5 * stagger_l) / max(1, n_cols - 1) * 2.75
                    norm = (across * math.cos(ang) + radial_out * math.sin(ang)).normalized()
                    sc_anchor = p + norm * (radius - 0.0022)
                    lateral = norm.cross(direction).normalized()
                    sc_w = 0.045 - 0.012 * u_l
                    sc_l = 0.052 - 0.014 * u_l
                    w_dict = make_limb_weight_fn(q)
                    cell_idx = 12 + ((lrow + col) % 4) if lrow >= 7 else ((lrow * 3 + col) % 8)
                    plate(
                        scales,
                        sc_anchor,
                        lateral,
                        direction,
                        norm,
                        width=sc_w,
                        length=sc_l,
                        thickness=0.0038 * (1.0 - 0.25 * u_l),
                        weight_fn=w_dict,
                        cell=cell_idx,
                        mat=0,
                        keel_boost=1.15,
                        cusp_point=0.44,
                        pitch_out=0.045,
                    )

            # =================================================================
            # PAW PADS, DIGITAL KNUCKLES, UNGUAL HOODS & FOSSORIAL SICKLE CLAWS
            # =================================================================
            if front:
                # Plantigrade/knuckle support sole pad grounded at y ~ 0.0045 for exact planted IK contact
                ellipsoid(skin, (w.x, .026, w.z - .025), (.046, .022, .054), foot, 1)

                # Dorsal carpal/metacarpal paw shield row bridging wrist bracer to the 5 digital ungual hoods
                for m_idx in range(-2, 3):
                    mx = w.x + m_idx * 0.013 * side
                    mz = w.z - 0.018 + abs(m_idx) * 0.004
                    my = 0.046 - abs(m_idx) * 0.002
                    m_splay = m_idx * 0.11
                    m_along = Vector((math.sin(m_splay) * side, -0.36, -math.cos(m_splay))).normalized()
                    m_norm = Vector((0.12 * m_idx * side, 0.90, -0.40)).normalized()
                    m_across = m_norm.cross(m_along).normalized()
                    plate(
                        scales,
                        Vector((mx, my, mz)),
                        m_across,
                        m_along,
                        m_norm,
                        width=0.026,
                        length=0.032,
                        thickness=0.0034,
                        weight_fn={foot: 1.0},
                        cell=12 + (abs(m_idx) % 4),
                        mat=0,
                        keel_boost=1.20,
                        cusp_point=0.42,
                        pitch_out=0.04,
                    )

                # 5 Forefoot Digits with authentic fossorial asymmetry (ref_forefoot2.png):
                # Digit III is the massive central pickaxe; Digits II & IV are large flanking sickles;
                # Digits I & V are shorter outer stabilizing talons. All curve FORWARD (-Z) and downward!
                fore_digits = [
                    # (d_idx, x_off, fwd_len, base_rx, base_ry, splay_rad, tip_y)
                    (-2, -0.031, 0.042, 0.0065, 0.0085, -0.28, 0.010),  # Digit I (medial dew-talon)
                    (-1, -0.016, 0.076, 0.0105, 0.0145, -0.11, 0.007),  # Digit II (inner digging sickle)
                    ( 0,  0.000, 0.096, 0.0130, 0.0175,  0.00, 0.006),  # Digit III (massive central pickaxe!)
                    ( 1,  0.016, 0.072, 0.0100, 0.0138,  0.11, 0.007),  # Digit IV (outer digging sickle)
                    ( 2,  0.030, 0.038, 0.0060, 0.0080,  0.28, 0.010),  # Digit V (lateral talon)
                ]
                for d_idx, x_off, fwd_len, brx, bry, splay, tip_y in fore_digits:
                    cx = w.x + x_off * side
                    cz_root = w.z - 0.036 + abs(d_idx) * 0.006
                    cy_root = 0.034 - abs(d_idx) * 0.002

                    # Fleshy digital knuckle pad on skin mesh
                    ellipsoid(skin, (cx, cy_root - 0.004, cz_root - 0.006), (brx * 1.25, bry * 0.95, 0.018), foot, 1)

                    # Keratinized ungual hood scale sheathing the claw root snugly
                    u_anchor = Vector((cx, cy_root + bry * 0.45, cz_root + 0.008))
                    u_along = Vector((math.sin(splay) * side, -0.28, -math.cos(splay))).normalized()
                    u_norm = Vector((0.14 * d_idx * side, 0.93, -0.32)).normalized()
                    u_across = u_norm.cross(u_along).normalized()
                    plate(
                        scales,
                        u_anchor,
                        u_across,
                        u_along,
                        u_norm,
                        width=brx * 2.6,
                        length=0.028,
                        thickness=0.0032,
                        weight_fn={foot: 1.0},
                        cell=12 + (abs(d_idx) % 4),
                        mat=0,
                        keel_boost=1.25,
                        cusp_point=0.40,
                        pitch_out=0.04,
                    )

                    # Forward-arching fossorial sickle talon (5 stations from ungual root to soil-piercing apex)
                    dir_x = math.sin(splay) * side
                    dir_z = -math.cos(splay)
                    c_pts = []
                    for st_i, u_s in enumerate((0.0, 0.22, 0.50, 0.78, 1.0)):
                        reach = u_s * fwd_len
                        arch_y = (
                            cy_root * (1.0 - u_s)
                            + tip_y * u_s
                            + (0.010 if abs(d_idx) <= 1 else 0.005) * math.sin(u_s * math.pi * 0.88)
                        )
                        inward_curl = -0.005 * side * (u_s ** 2.2)
                        px = cx + dir_x * reach + inward_curl
                        pz_c = cz_root + dir_z * reach
                        c_pts.append(Vector((px, max(0.0055, arch_y), pz_c)))

                    rx_list = [brx * 0.92, brx * 0.86, brx * 0.70, brx * 0.42, 0.0012]
                    ry_list = [bry * 1.05, bry * 1.12, bry * 0.92, bry * 0.55, 0.0016]
                    claw_talon(detail, c_pts, rx_list, ry_list, foot, mat=2, side=side, fan_angle=splay)
            else:
                # Hindfoot plantigrade sole grounded at y ~ 0.0040 for exact planted IK contact
                ellipsoid(skin, (w.x, .024, w.z + .010), (.048, .020, .058), foot, 1)
                for d_idx in range(-2, 3):
                    cx = w.x + d_idx * 0.0155 * side
                    cz_root = w.z - 0.026 + abs(d_idx) * 0.005
                    cy_root = 0.028 - abs(d_idx) * 0.0015
                    c_len = 0.044 if abs(d_idx) <= 1 else 0.032
                    splay = d_idx * 0.10
                    dir_x = math.sin(splay) * side
                    dir_z = -math.cos(splay)

                    # Hind toe pad & dorsal ungual scale
                    ellipsoid(skin, (cx, cy_root - 0.004, cz_root - 0.004), (0.009, 0.008, 0.015), foot, 1)
                    plate(
                        scales,
                        Vector((cx, cy_root + 0.005, cz_root + 0.006)),
                        Vector((side, 0.0, 0.0)),
                        Vector((dir_x, -0.26, dir_z)).normalized(),
                        Vector((0.0, 0.95, -0.31)).normalized(),
                        width=0.020,
                        length=0.024,
                        thickness=0.0030,
                        weight_fn={foot: 1.0},
                        cell=13 + (abs(d_idx) % 3),
                        mat=0,
                        keel_boost=1.15,
                        cusp_point=0.40,
                        pitch_out=0.04,
                    )

                    c_pts = [
                        Vector((cx, cy_root, cz_root)),
                        Vector((cx + dir_x * c_len * 0.35, cy_root + 0.002, cz_root + dir_z * c_len * 0.35)),
                        Vector((cx + dir_x * c_len * 0.72, 0.015, cz_root + dir_z * c_len * 0.72)),
                        Vector((cx + dir_x * c_len, 0.0075, cz_root + dir_z * c_len)),
                    ]
                    rx_list = [0.0075, 0.0068, 0.0045, 0.0010]
                    ry_list = [0.0095, 0.0090, 0.0060, 0.0014]
                    claw_talon(detail, c_pts, rx_list, ry_list, foot, mat=2, side=side, fan_angle=splay)

        # =====================================================================
        # 5. CRANIAL ORGANS: HOODED OBSIDIAN-ESPRESSO EYES, VALVULAR NARES & PINNAE
        # =====================================================================
        # Cohesive glossy dark obsidian-espresso eyeball nestled inside orbital socket
        ellipsoid(detail, (eye_x * side, eye_y, eye_z), (.0074, .0074, .0082), 'Head', 3, 20, 12)
        ellipsoid(detail, ((eye_x + .0006) * side, eye_y, eye_z), (.0062, .0052, .0056), 'Head', 4, 16, 10)

        # Sculpted dark leathery circumorbital palpebral hood (replaces cartoon white ring)
        for sign, v_off, r_rad in ((1, 0.0022, 0.0034), (-1, -0.0012, 0.0028)):
            pts = []
            radii_lid = []
            for k_p in range(15):
                u_p = k_p / 14.0
                a_p = math.pi * u_p
                arch_s = math.sin(a_p)
                px = side * (eye_x - 0.0005 + 0.0036 * arch_s)
                py = eye_y + v_off + sign * 0.0072 * arch_s
                pz = eye_z + 0.0112 * math.cos(a_p)
                pts.append((px, py, pz))
                radii_lid.append(r_rad * (0.55 + 0.45 * arch_s))
            tube(detail, pts, radii_lid, 'Head', 5, 10, v_range=(0.055, 0.095))

        # Sculpted crescentic valvular nostril (dark nasal cavity + fleshy lateral narial wing)
        naris_z = -0.526
        naris_y = 0.083
        naris_x = 0.0135 * side
        ellipsoid(detail, (naris_x, naris_y, naris_z), (.0032, .0026, .0052), 'Head', 5, 14, 10)
        n_wing = []
        for k_n in range(9):
            a_n = math.pi * k_n / 8.0
            n_wing.append((
                naris_x + side * (0.0018 * math.sin(a_n)),
                naris_y + 0.0024 * math.sin(a_n),
                naris_z + 0.0055 * math.cos(a_n),
            ))
        tube(detail, n_wing, [0.0016] * 9, 'Head', 1, 8, v_range=(0.015, 0.035))

        # Sculpted C-shaped muscular valvular ear pinna & dark auditory meatus behind temporal shield
        ear_x, ear_y, ear_z = 0.096 * side, 0.194, -0.336
        ellipsoid(detail, (ear_x - 0.003 * side, ear_y, ear_z), (.0030, .0055, .0045), 'Head', 5, 12, 8)
        ear_rim = []
        ear_r = []
        for k_e in range(13):
            u_e = k_e / 12.0
            a_e = -math.pi * 0.75 + u_e * math.pi * 1.50
            ear_rim.append((
                ear_x + side * (0.0035 * math.sin(u_e * math.pi)),
                ear_y + 0.0105 * math.sin(a_e),
                ear_z + 0.0085 * math.cos(a_e),
            ))
            ear_r.append(0.0032 * (0.6 + 0.4 * math.sin(u_e * math.pi)))
        tube(detail, ear_rim, ear_r, 'Head', 1, 10, v_range=(0.075, 0.125))

    from pangolin_lids import add_lids
    add_lids(Geometry, rig, detail, eye_x=eye_x, eye_y=eye_y, eye_z=eye_z)

    # Dark recessed oral commissure & vermiform myrmecophagous tongue tucked inside mouth at rest
    ellipsoid(detail, (0.0, 0.074, -0.534), (0.0065, 0.0022, 0.0080), 'Head', 5, 12, 8)
    tube(detail, [(0, .075, -.488), (0, .075, -.510), (0, .075, -.528)], [.0026, .0022, .0014], 'Tongue', 6, 8)

    bodies = [
        continuous_skin(skin, 'PangolinContinuousSkin', mats, rig, profile),
        scales.object('ClosedOverlappingArmor', mats, rig),
        detail.object('FaceDigitsClaws', mats, rig),
    ]

    rig['art_revision'] = 'pangolin-solo-v17-guardian-realism'
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
        export_apply=False,
    )
    bpy.ops.wm.save_as_mainfile(filepath=str((REPORT / (species + '-source.blend')).resolve()))
    # Also save guardian.blend alongside guardian.glb in game/assets/guardians/armored_forest
    bpy.ops.wm.save_as_mainfile(filepath=str((out / 'guardian.blend').resolve()))
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
    for b in rig.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    scene.frame_set(1)

    if species == 'armored_forest':
        audit['planted_ik'] = audit_planted_ik(rig, bodies)
        rig.animation_data.action = None
        for b in rig.pose.bones:
            b.matrix_basis = Matrix.Identity(4)
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
            traces.append([
                round(frame, 3), round(seconds, 6), label, u < .68,
                float(paw.head.x), float(paw.head.y), float(paw.head.z),
                float(sole.x), float(sole.y), float(sole.z), float(sole.z - .2 * seconds),
            ])
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
        'pass': gap < 1e-5 and length_error < 1e-5 and slip < .001 and flat_degrees < .1,
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
                for i in range(len(ob.data.materials)):
                    ob.data.materials[i] = clay
        for view, position in views.items():
            cam.location = position
            forward = (target - cam.location).normalized()
            right = forward.cross(Vector((0, 1, 0))).normalized()
            up = right.cross(forward)
            cam.rotation_euler = Matrix((right, up, -forward)).transposed().to_euler()
            scene.render.filepath = str((REPORT / (species + '-' + mode + '-' + view + '.png')).resolve())
            bpy.ops.render.render(write_still=True)

    for ob, mats in zip(bodies, original):
        for i, m in enumerate(mats):
            ob.data.materials[i] = m

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
    for b in rig.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
    print('ANATOMICAL_GUARDIAN_COMPLETE', species)
