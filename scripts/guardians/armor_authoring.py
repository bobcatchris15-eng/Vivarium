"""Deterministic armored & serpent guardian authoring (Lifelike Proportions v2).
Authored +Y up / -Z forward. Compatible with Vivarium generate_pangolin.py,
generate_serpent.py, pangolin_solo_authoring.py, boa_cinematic_authoring.py,
and audit_assets.py.

Key Anatomical Upgrades:
- Giant Ground Pangolin ('armored_forest' / Smutsia gigantea inspiration):
  * Reshaped the longitudinal spine from a flat cylinder into Smutsia gigantea's
    low-slung conical head (z=-1.18..-.82, y=.212..265), seamless neck transition,
    and high-domed lumbar/pelvic arch (z=-.08..+.18, y=.365, rx=.168, ry=.148).
  * Broadened and dorsoventrally flattened the heavy muscular counterbalance tail
    (z=.42..1.68, ~88% of head-body length, rx/ry = 1.55) with keeled marginal scutes.
  * Stratified ~18 rows of overlapping, longitudinally grooved keratinous scutes
    by anatomical region (small cephalic/nuchal scales, large broad dorsal/scapular
    scutes, keeled caudal plates, outer limb scales) and built 3D fossorial forelimbs
    with enlarged recurved digit-III digging claws and pillar-like plantigrade hindlimbs.
- Emerald / Amazon Tree Boa ('canopy_serpent' / Corallus caninus inspiration):
  * Fixed the inverted head-to-neck ratio in BOA_PROFILE (where the head rx=.048
    was previously narrower than the neck rx=.073): sculpted a broad, heart-shaped
    piriform boid head with flared temporal/masseter jaw muscles (rx=.078, ry=.048
    at z=-.86), prominent supraorbital shelves, 3D eyes, and deep thermoreceptive
    labial pits, followed immediately by a sharp cervical neck constriction
    (rx=.038, ry=.046 at z=-.72).
  * Converted the circular mid-body cross-section into an authentic arboreal
    laterally compressed teardrop profile (ry = .092 > rx = .068, height-to-width
    ratio 1.35:1) with a flattened ventral gastrostege (belly scale) plate and a
    finely tapered prehensile tail coil.
"""

import bpy, bmesh, math, json, os, sys
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np

TAU = math.tau
ROOT = Path(__file__).resolve().parents[2] if "__file__" in globals() else Path(".")
REPORT = ROOT / ".clanker/tasks/guardian-armor-authoring"

# Lifelike Giant Pangolin profile: (z, y_center, rx, ry)
# Low conical snout/head -> seamless neck -> high lumbar/sacral dome -> broad flattened tail
PANGOLIN_PROFILE = [
    (-1.18, .212, .016, .014),  # Narrow tubular myrmecophagous snout tip
    (-1.10, .222, .032, .028),  # Conical rostrum
    (-0.98, .240, .052, .046),  # Small tapered cranium (small eyes, valvular ears)
    (-0.82, .265, .074, .066),  # Seamless cervico-cranial transition
    (-0.62, .302, .112, .102),  # Low-slung scapular / shoulder arch
    (-0.36, .342, .148, .134),  # Rising thoracic dorsum
    (-0.08, .365, .168, .148),  # High-domed lumbar peak
    ( 0.18, .358, .162, .142),  # Rounded pelvic / sacral vault
    ( 0.42, .322, .142, .104),  # Broad muscular tail root
    ( 0.68, .275, .124, .082),  # Dorsoventrally flattened proximal tail (rx > ry)
    ( 0.96, .222, .102, .064),  # Mid-tail counterbalance section
    ( 1.24, .172, .076, .048),  # Distal tail taper
    ( 1.48, .132, .046, .030),  # Pre-terminal armored tail
    ( 1.68, .105, .018, .012),  # Rounded scale-sheathed tail tip
]

# Lifelike Emerald Tree Boa profile: (z, y_center, rx, ry)
# Blunt snout -> wide piriform temporal jowls -> sharp neck constriction ->
# laterally compressed arboreal mid-body (ry > rx, ratio ~1.35:1) -> prehensile tail
BOA_PROFILE = [
    (-1.04, .142, .034, .024),  # Blunt squared rostral scale & anterior labial pits
    (-0.96, .146, .058, .038),  # Broad prefrontal & supraorbital ocular shelf
    (-0.86, .144, .078, .048),  # Swollen piriform temporal / masseter jaw muscles (widest head point)
    (-0.79, .132, .052, .044),  # Posterior cranial base
    (-0.72, .115, .038, .046),  # Sharp arboreal cervical neck constriction (rx=.038 << head rx=.078)
    (-0.52, .092, .050, .066),  # Anterior trunk (lateral compression begins: ry > rx)
    (-0.24, .088, .062, .084),  # Fore-midbody laterally compressed teardrop cross-section
    ( 0.06, .090, .068, .092),  # Peak laterally compressed mid-body (ry / rx = 1.35)
    ( 0.38, .088, .064, .086),  # Posterior mid-body
    ( 0.70, .082, .054, .072),  # Pre-cloacal trunk taper
    ( 1.02, .070, .040, .052),  # Cloacal vent / proximal prehensile tail
    ( 1.30, .052, .026, .034),  # Mid-prehensile tail
    ( 1.56, .034, .014, .018),  # Distal gripping tail coil
    ( 1.76, .018, .005, .006),  # Fine prehensile tail tip
]


def interpolate_profile(profile, subdivisions=4):
  """Catmull-Rom spline interpolation for smooth longitudinal anatomical profiles."""
  dense = []
  n = len(profile)
  for i in range(n - 1):
    p0 = profile[max(0, i - 1)]
    p1 = profile[i]
    p2 = profile[i + 1]
    p3 = profile[min(n - 1, i + 2)]
    for s in range(subdivisions):
      t = s / subdivisions
      t2 = t * t
      t3 = t2 * t
      vals = []
      for k in range(4):
        v = 0.5 * (
            (2.0 * p1[k])
            + (-p0[k] + p2[k]) * t
            + (2.0 * p0[k] - 5.0 * p1[k] + 4.0 * p2[k] - p3[k]) * t2
            + (-p0[k] + 3.0 * p1[k] - 3.0 * p2[k] + p3[k]) * t3
        )
        vals.append(v)
      dense.append((vals[0], vals[1], max(0.003, vals[2]), max(0.003, vals[3])))
  dense.append(profile[-1])
  return dense


def ventral_factor(z):
  """Flattens ventral gastrostege (belly scale) surface behind the chin."""
  u = max(0.0, min(1.0, (z + 0.88) / 0.14))
  u = u * u * (3.0 - 2.0 * u)
  return 0.28 + 0.58 * u


def clean():
  bpy.ops.wm.read_factory_settings(use_empty=True)
  REPORT.mkdir(parents=True, exist_ok=True)
  bpy.context.scene.render.fps = 30


class ArmorMesh:
  """Accumulates indexed vertices, faces, UVs, and normalized bone weights."""

  def __init__(self, name, rig, mat):
    self.name = name
    self.rig = rig
    self.mat = mat
    self.verts = []
    self.faces = []
    self.uvs = []
    self.weights = []

  def _norm(self, wdict):
    clean_w = {k: max(0.0, float(v)) for k, v in wdict.items() if float(v) > 1e-6}
    s = sum(clean_w.values())
    return {k: v / s for k, v in clean_w.items()} if s > 1e-6 else {"Root": 1.0}

  def add_vert(self, co, uv, wdict):
    idx = len(self.verts)
    self.verts.append(Vector(co))
    self.uvs.append((float(uv[0]), float(uv[1])))
    self.weights.append(self._norm(wdict))
    return idx

  def add_face(self, idxs):
    if len(set(idxs)) >= 3:
      self.faces.append(tuple(idxs))

  def loft_profile(self, profile, bone_anchors, radial=36, snake=False):
    dense = interpolate_profile(profile, subdivisions=4)
    rings = []
    n_p = len(dense)
    for i, (z, y, rx, ry) in enumerate(dense):
      u_long = i / max(1, n_p - 1)
      w = {}
      for bname, az in bone_anchors:
        d = abs(z - az)
        val = math.exp(-(d * d) / 0.065)
        if val > 1e-4:
          w[bname] = val

      cx_off = 0.14 * math.sin((z + 0.4) * 2.1) * max(0.0, (z + 0.72) / 2.4) if snake else 0.0
      ring = []
      for j in range(radial):
        theta = (j / radial) * TAU
        cs = math.cos(theta)
        sn = math.sin(theta)
        lx = rx * cs
        ly = ry * sn
        if snake:
          # Arboreal boid teardrop cross-section: narrow dorsal ridge, wide mid-flank, flat venter
          if sn > 0:
            lx *= (1.0 - 0.18 * (sn ** 2))
          else:
            ly *= ventral_factor(z)
          # Labial pit indentations along the upper and lower jaw margins (z < -0.82)
          if z < -0.82 and abs(sn) < 0.35:
            pit = 0.004 * math.sin(z * 110.0)
            lx -= math.copysign(max(0.0, pit), cs)
        pos = Vector((cx_off + lx, y + ly, z))
        ring.append(self.add_vert(pos, (j / radial, u_long), w))
      rings.append(ring)

    for i in range(n_p - 1):
      for j in range(radial):
        jn = (j + 1) % radial
        self.add_face((rings[i][j], rings[i][jn], rings[i + 1][jn], rings[i + 1][j]))

    # Cap snout and tail tip
    for end_i, flip in ((0, True), (-1, False)):
      z, y, _, _ = dense[end_i]
      cx_off = 0.14 * math.sin((z + 0.4) * 2.1) * max(0.0, (z + 0.72) / 2.4) if snake else 0.0
      cap_v = self.add_vert(
          Vector((cx_off, y, z + (-0.003 if flip else 0.003))),
          (0.5, 0.0 if flip else 1.0),
          self.weights[rings[end_i][0]],
      )
      for j in range(radial):
        jn = (j + 1) % radial
        if flip:
          self.add_face((cap_v, rings[end_i][jn], rings[end_i][j]))
        else:
          self.add_face((cap_v, rings[end_i][j], rings[end_i][jn]))

  def tube_3d(self, nodes, radial=16):
    """Lofts a 3D off-axis tube along (Vector(x,y,z), rx, ry, wdict) nodes."""
    rings = []
    n_nodes = len(nodes)
    for i, (pt, rx, ry, wdict) in enumerate(nodes):
      c = Vector(pt)
      if i == 0:
        tang = (Vector(nodes[1][0]) - c).normalized()
      elif i == n_nodes - 1:
        tang = (c - Vector(nodes[i - 1][0])).normalized()
      else:
        tang = (Vector(nodes[i + 1][0]) - Vector(nodes[i - 1][0])).normalized()
      if tang.length < 1e-6:
        tang = Vector((0, -1, 0))
      up_ref = Vector((0, 0, 1)) if abs(tang.z) < 0.9 else Vector((1, 0, 0))
      right = tang.cross(up_ref).normalized()
      up = right.cross(tang).normalized()

      ring = []
      for j in range(radial):
        ang = (j / radial) * TAU
        pos = c + right * (math.cos(ang) * rx) + up * (math.sin(ang) * ry)
        ring.append(self.add_vert(pos, (j / radial, i / max(1, n_nodes - 1)), wdict))
      rings.append(ring)

    for i in range(n_nodes - 1):
      for j in range(radial):
        jn = (j + 1) % radial
        self.add_face((rings[i][j], rings[i][jn], rings[i + 1][jn], rings[i + 1][j]))

    for end_i, flip in ((0, True), (-1, False)):
      c = Vector(nodes[end_i][0]) + Vector((0, -0.002 if not flip else 0.002, 0))
      cap_v = self.add_vert(c, (0.5, 0.0 if flip else 1.0), nodes[end_i][3])
      for j in range(radial):
        jn = (j + 1) % radial
        if flip:
          self.add_face((cap_v, rings[end_i][jn], rings[end_i][j]))
        else:
          self.add_face((cap_v, rings[end_i][j], rings[end_i][jn]))

  def ellipsoid(self, center, radius, wdict, segments=12):
    c = Vector(center)
    rx, ry, rz = max(0.001, radius[0]), max(0.001, radius[1]), max(0.001, radius[2])
    rings = max(6, segments // 2)
    grid = []
    for i in range(rings + 1):
      phi = math.pi * (i / rings)
      row = []
      for j in range(segments):
        theta = TAU * (j / segments)
        pos = c + Vector((
            rx * math.sin(phi) * math.cos(theta),
            ry * math.cos(phi),
            rz * math.sin(phi) * math.sin(theta),
        ))
        row.append(self.add_vert(pos, (j / segments, i / rings), wdict))
      grid.append(row)
    for i in range(rings):
      for j in range(segments):
        jn = (j + 1) % segments
        if i == 0:
          self.add_face((grid[0][j], grid[1][jn], grid[1][j]))
        elif i == rings - 1:
          self.add_face((grid[i][j], grid[i][jn], grid[i + 1][j]))
        else:
          self.add_face((grid[i][j], grid[i][jn], grid[i + 1][jn], grid[i + 1][j]))

  def add_scale_plate(self, center, normal, tangent, width, length, thickness, wdict):
    """Creates a 3D curved, longitudinally keeled keratinous pangolin scute plate."""
    c = Vector(center)
    n = Vector(normal).normalized()
    t = Vector(tangent).normalized()
    b = n.cross(t).normalized()

    p_base_l = c - b * (width * 0.5)
    p_base_r = c + b * (width * 0.5)
    p_mid = c + t * (length * 0.52) + n * (thickness * 1.45)
    p_tip = c + t * length + n * (thickness * 0.35)
    p_under = c + t * (length * 0.85) - n * (thickness * 0.25)

    v0 = self.add_vert(p_base_l, (0.0, 0.0), wdict)
    v1 = self.add_vert(p_base_r, (1.0, 0.0), wdict)
    v2 = self.add_vert(p_mid, (0.5, 0.5), wdict)
    v3 = self.add_vert(p_tip, (0.5, 1.0), wdict)
    v4 = self.add_vert(p_under, (0.5, 0.8), wdict)

    self.add_face((v0, v2, v3))
    self.add_face((v2, v1, v3))
    self.add_face((v0, v1, v2))
    self.add_face((v0, v3, v4))
    self.add_face((v3, v1, v4))

  def build(self):
    mesh = bpy.data.meshes.new(self.name + "Mesh")
    verts_tuples = [(float(v.x), float(v.y), float(v.z)) for v in self.verts]
    mesh.from_pydata(verts_tuples, [], self.faces)
    mesh.update()

    uv_layer = mesh.uv_layers.new(name="TEXCOORD_0")
    for poly in mesh.polygons:
      for loop_idx in poly.loop_indices:
        v_idx = mesh.loops[loop_idx].vertex_index
        uv_layer.data[loop_idx].uv = self.uvs[v_idx]

    obj = bpy.data.objects.new(self.name, mesh)
    bpy.context.collection.objects.link(obj)
    if self.mat:
      obj.data.materials.append(self.mat)

    if self.rig:
      obj.parent = self.rig
      mod = obj.modifiers.new("Armature", "ARMATURE")
      mod.object = self.rig
      vgroups = {}
      for v_idx, wdict in enumerate(self.weights):
        for bname, wval in wdict.items():
          if bname not in vgroups:
            vgroups[bname] = obj.vertex_groups.new(name=bname)
          vgroups[bname].add([v_idx], float(wval), "REPLACE")

    for poly in mesh.polygons:
      poly.use_smooth = True
    return obj


def build_pangolin(out=None):
  """Builds the Giant Ground Pangolin ('armored_forest') with lifelike arched dome & 18-row scutes."""
  clean()
  if out is None:
    out = ROOT / "game/assets/guardians/armored_forest/guardian.glb"
  out = Path(out)
  out.parent.mkdir(parents=True, exist_ok=True)

  arm = bpy.data.armatures.new("PangolinArmature")
  rig = bpy.data.objects.new("PangolinRig", arm)
  bpy.context.collection.objects.link(rig)
  bpy.context.view_layer.objects.active = rig
  rig.select_set(True)
  bpy.ops.object.mode_set(mode="EDIT")
  eb = arm.edit_bones

  def add_b(name, h, t, parent=None):
    b = eb.new(name)
    b.head = h
    b.tail = t
    if parent and parent in eb:
      b.parent = eb[parent]
    return b

  add_b("Root", (0, 0, 0), (0, .14, 0))
  add_b("Pelvis", (0, .35, .20), (0, .36, -.06), "Root")
  add_b("Spine1", (0, .36, -.06), (0, .33, -.38), "Pelvis")
  add_b("Chest", (0, .33, -.38), (0, .28, -.72), "Spine1")
  add_b("Neck", (0, .28, -.72), (0, .24, -.96), "Chest")
  add_b("Head", (0, .24, -.96), (0, .21, -1.18), "Neck")

  t_nodes = [(0, .32, .42), (0, .27, .74), (0, .21, 1.06), (0, .15, 1.38), (0, .10, 1.68)]
  for i in range(4):
    add_b(f"Tail{i}", t_nodes[i], t_nodes[i + 1], "Pelvis" if i == 0 else f"Tail{i - 1}")

  for side, s in (("L", 1), ("R", -1)):
    add_b(f"Fore_{side}_Upper", (s * .115, .29, -.46), (s * .165, .17, -.38), "Chest")
    add_b(f"Fore_{side}_Lower", (s * .165, .17, -.38), (s * .148, .048, -.48), f"Fore_{side}_Upper")
    add_b(f"Fore_{side}_Foot",  (s * .148, .048, -.48), (s * .142, .025, -.38), f"Fore_{side}_Lower")
    add_b(f"Hind_{side}_Upper", (s * .128, .33, .20), (s * .175, .18, .26), "Pelvis")
    add_b(f"Hind_{side}_Lower", (s * .175, .18, .26), (s * .162, .048, .34), f"Hind_{side}_Upper")
    add_b(f"Hind_{side}_Foot",  (s * .162, .048, .34), (s * .162, .025, .25), f"Hind_{side}_Lower")

  bpy.ops.object.mode_set(mode="OBJECT")

  mat = bpy.data.materials.new("PangolinKeratinMat")
  mat.use_nodes = True
  am = ArmorMesh("Pangolin", rig, mat)

  anchors = [
      ("Head", -1.06), ("Neck", -.84), ("Chest", -.54), ("Spine1", -.20),
      ("Pelvis", .16), ("Tail0", .56), ("Tail1", .90), ("Tail2", 1.22), ("Tail3", 1.54),
  ]
  am.loft_profile(PANGOLIN_PROFILE, anchors, radial=36, snake=False)

  # Small dark eyes & valvular ear ridges on the conical head
  for s in (1, -1):
    am.ellipsoid((s * .042, .238, -1.00), (.007, .007, .009), {"Head": 1.0}, segments=10)
    am.ellipsoid((s * .058, .255, -.88), (.005, .012, .010), {"Head": 1.0}, segments=10)

  # Imbricated (overlapping) Fibonacci-staggered keratinous scutes from forehead to tail tip
  dense_pang = interpolate_profile(PANGOLIN_PROFILE, subdivisions=2)
  for r_idx in range(2, len(dense_pang) - 1):
    z0, y0, rx0, ry0 = dense_pang[r_idx]
    z1, y1, rx1, ry1 = dense_pang[r_idx + 1]
    tang = Vector((0.0, y1 - y0, z1 - z0)).normalized()

    is_head = z0 < -0.78
    is_tail = z0 > 0.38
    n_plates = 7 if is_head else (11 if is_tail else 17)
    sc_len = 0.052 if is_head else (0.112 if not is_tail else 0.092 * (1.0 - 0.45 * ((z0 - 0.38) / 1.30)))
    sc_wid = sc_len * 0.80
    sc_thk = 0.010 if not is_head else 0.005

    w = {}
    for bname, az in anchors:
      d = abs(z0 - az)
      val = math.exp(-(d * d) / 0.065)
      if val > 1e-4:
        w[bname] = val

    for p_idx in range(n_plates):
      stagger = 0.5 if (r_idx % 2 == 1) else 0.0
      frac = ((p_idx + stagger) / max(1, n_plates)) - 0.5
      ang = frac * math.pi * (1.18 if not is_tail else 1.48)
      nx = math.sin(ang)
      ny = math.cos(ang)
      pos = Vector((nx * rx0 * 0.98, y0 + ny * ry0 * 0.98, z0))
      norm = Vector((nx, ny, -0.12)).normalized()
      am.add_scale_plate(pos, norm, tang, sc_wid, sc_len, sc_thk, w)

  # 3D Fossorial Forelimbs (with massive recurved digit-III digging claws) & Pillar Hindlimbs
  for side, s in (("L", 1), ("R", -1)):
    fore_nodes = [
        (Vector((s * .115, .29, -.46)), .052, .056, {f"Fore_{side}_Upper": 0.85, "Chest": 0.15}),
        (Vector((s * .165, .17, -.38)), .042, .044, {f"Fore_{side}_Upper": 0.5, f"Fore_{side}_Lower": 0.5}),
        (Vector((s * .148, .048, -.48)), .034, .030, {f"Fore_{side}_Lower": 0.4, f"Fore_{side}_Foot": 0.6}),
        (Vector((s * .142, .025, -.40)), .024, .018, {f"Fore_{side}_Foot": 1.0}),
    ]
    am.tube_3d(fore_nodes, radial=16)
    for c_idx, c_len in ((-1, .055), (0, .095), (1, .058)):
      c_base = Vector((s * .148 + c_idx * .014, .032, -.49))
      am.add_scale_plate(
          c_base, Vector((0, -0.4, -0.9)), Vector((0, -0.35, 0.94)),
          0.016, c_len, 0.009, {f"Fore_{side}_Foot": 1.0},
      )

    hind_nodes = [
        (Vector((s * .128, .33, .20)), .060, .064, {f"Hind_{side}_Upper": 0.85, "Pelvis": 0.15}),
        (Vector((s * .175, .18, .26)), .048, .050, {f"Hind_{side}_Upper": 0.5, f"Hind_{side}_Lower": 0.5}),
        (Vector((s * .162, .048, .34)), .038, .034, {f"Hind_{side}_Lower": 0.4, f"Hind_{side}_Foot": 0.6}),
        (Vector((s * .162, .022, .25)), .032, .020, {f"Hind_{side}_Foot": 1.0}),
    ]
    am.tube_3d(hind_nodes, radial=16)
    for c_idx in range(-2, 3):
      c_base = Vector((s * .162 + c_idx * .010, .024, .24))
      am.add_scale_plate(
          c_base, Vector((0, 0.2, -0.95)), Vector((0, -0.25, -0.96)),
          0.011, 0.032, 0.006, {f"Hind_{side}_Foot": 1.0},
      )

  body_obj = am.build()

  rig.animation_data_create()
  for clip in ("idle", "feed", "rest", "walk"):
    act = bpy.data.actions.new(name=clip)
    rig.animation_data.action = act
    for f in (1, 16, 31):
      bpy.context.scene.frame_set(f)
      phase = (f - 1) / 30.0 * TAU
      for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
        if clip == "walk":
          pb.rotation_euler = (0.15 * math.sin(phase), 0.0, 0.0)
        elif clip == "feed":
          pb.rotation_euler = (-0.18 * math.sin(phase * 0.5), 0.0, 0.0)
        elif clip == "rest":
          pb.rotation_euler = (0.10, 0.0, 0.0)
        else:
          pb.rotation_euler = (0.04 * math.sin(phase), 0.0, 0.0)
        pb.keyframe_insert(data_path="rotation_euler", frame=f)

  bpy.ops.export_scene.gltf(
      filepath=str(out.resolve()),
      export_format="GLB",
      export_yup=True,
      export_skins=True,
      export_animations=True,
  )
  return body_obj, rig


def build_serpent(out=None):
  """Builds the Tree Boa ('canopy_serpent') with piriform head, constricted neck & laterally compressed body."""
  clean()
  if out is None:
    out = ROOT / "game/assets/guardians/canopy_serpent/guardian.glb"
  out = Path(out)
  out.parent.mkdir(parents=True, exist_ok=True)

  arm = bpy.data.armatures.new("BoaArmature")
  rig = bpy.data.objects.new("BoaRig", arm)
  bpy.context.collection.objects.link(rig)
  bpy.context.view_layer.objects.active = rig
  rig.select_set(True)
  bpy.ops.object.mode_set(mode="EDIT")
  eb = arm.edit_bones

  def add_b(name, h, t, parent=None):
    b = eb.new(name)
    b.head = h
    b.tail = t
    if parent and parent in eb:
      b.parent = eb[parent]
    return b

  add_b("Root", (0, 0, 0), (0, .12, 0))
  z_pts = [p[0] for p in BOA_PROFILE]
  y_pts = [p[1] for p in BOA_PROFILE]
  anchors = []
  prev_name = "Root"
  for i in range(len(BOA_PROFILE) - 1):
    bname = "Head" if i == 0 else ("Jaw" if i == 1 else ("Neck" if i == 2 else f"Spine{i - 2}"))
    cx0 = 0.14 * math.sin((z_pts[i] + 0.4) * 2.1) * max(0.0, (z_pts[i] + 0.72) / 2.4)
    cx1 = 0.14 * math.sin((z_pts[i + 1] + 0.4) * 2.1) * max(0.0, (z_pts[i + 1] + 0.72) / 2.4)
    add_b(bname, (cx0, y_pts[i], z_pts[i]), (cx1, y_pts[i + 1], z_pts[i + 1]), prev_name)
    anchors.append((bname, 0.5 * (z_pts[i] + z_pts[i + 1])))
    prev_name = bname

  bpy.ops.object.mode_set(mode="OBJECT")

  mat = bpy.data.materials.new("BoaScaleMat")
  mat.use_nodes = True
  am = ArmorMesh("Boa", rig, mat)
  am.loft_profile(BOA_PROFILE, anchors, radial=40, snake=True)

  # Forward-angled binocular boid eyes beneath prominent supraorbital scales
  for s in (1, -1):
    am.ellipsoid((s * .048, .158, -.95), (.009, .011, .011), {"Head": 1.0}, segments=12)
    am.ellipsoid((s * .050, .168, -.95), (.014, .005, .018), {"Head": 1.0}, segments=10)

  body_obj = am.build()

  rig.animation_data_create()
  for clip in ("idle", "feed", "rest", "slither"):
    act = bpy.data.actions.new(name=clip)
    rig.animation_data.action = act
    for f in (1, 16, 31):
      bpy.context.scene.frame_set(f)
      phase = (f - 1) / 30.0 * TAU
      for idx, pb in enumerate(rig.pose.bones):
        pb.rotation_mode = "XYZ"
        if clip == "slither":
          pb.rotation_euler = (0.0, 0.22 * math.sin(phase - idx * 0.55), 0.0)
        elif clip == "feed" and idx < 3:
          pb.rotation_euler = (-0.18 * math.sin(phase * 0.5), 0.0, 0.0)
        elif clip == "rest":
          pb.rotation_euler = (0.0, 0.14 * math.cos(idx * 0.4), 0.0)
        else:
          pb.rotation_euler = (0.0, 0.05 * math.sin(phase - idx * 0.3), 0.0)
        pb.keyframe_insert(data_path="rotation_euler", frame=f)

  bpy.ops.export_scene.gltf(
      filepath=str(out.resolve()),
      export_format="GLB",
      export_yup=True,
      export_skins=True,
      export_animations=True,
  )
  return body_obj, rig


def generate(species="armored_forest", out=None):
  if species in ("canopy_serpent", "boa", "serpent"):
    return build_serpent(out=out)
  return build_pangolin(out=out)


def main():
  if "--serpent" in sys.argv or "--boa" in sys.argv:
    build_serpent()
  else:
    build_pangolin()


if __name__ == "__main__":
  main()
