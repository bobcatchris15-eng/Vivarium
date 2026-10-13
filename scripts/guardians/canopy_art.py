"""Deterministic canopy guardian authoring (Lifelike Proportions v2).
Authored +Y up / -Z forward. Compatible with Vivarium generate_hoatzin.py,
generate_porcupine.py, hoatzin_cinematic_authoring.py, porcupine_solo_authoring.py,
and audit_assets.py.

Key Anatomical Upgrades:
- Hoatzin / Crested Canopy ('crested_canopy' / Opisthocomus hoazin inspiration):
  * Sculpted a true S-curved slender avian neck (z=-.40..-.26, rx=.028..032)
    connecting a compact cranium to the hoatzin's signature heavy anterior
    foregut fermentation crop bulge (z=-.12..-.02, ry=.136 > rx=.118) and
    sternal callosity keel.
  * Expanded the spiky reddish-chestnut head crest into an 11-plume radial fan,
    refined the stout decurved folivorous bill and bare blue facial skin / red
    iris geometry, broadened the rounded wings with functional thumb/index
    alula wing-claws, and lengthened the 10-rectrix buff-tipped tail fan to
    44% of total body length.
- Prehensile-Tailed Porcupine ('quilled_arboreal' / Coendou prehensilis inspiration):
  * Replaced the pointed cone snout with Coendou prehensilis's inflated
    nasofrontal sinus forehead dome (z=-.50..-.38, ry=.074) and wide, blunt,
    fleshy bulbous muzzle (z=-.58, rx=.044, ry=.042).
  * Reshaped the spherical torso into an arched scansorial rodent back and
    extended a 5-segment prehensile tail (~90% of head-body length, z=.34..1.18)
    that curls upward at the naked dorsal callus tip for grasping branches.
  * Stratified tricolored bristle-quills by anatomical zone (short parted crown
    quills, long dense scapular/lumbar defensive quills, shorter flank quills)
    and added 4-clawed arboreal grasping feet with fleshy plantar pads.
"""

import bpy, bmesh, math, json, os, sys
from pathlib import Path
from mathutils import Vector, Matrix, Euler
import numpy as np

TAU = math.tau
ROOT = Path(__file__).resolve().parents[2] if "__file__" in globals() else Path(".")
REPORT = ROOT / ".clanker/tasks/guardian-canopy-art"


def feather_weights(bone, t):
  """Computes normalized bone weights along wing and tail feather chains."""
  t = max(0.0, min(1.0, float(t)))
  if bone.startswith("Wing_"):
    side = bone.split("_")[1]
    w_up = max(0.0, 1.0 - t * 1.15)
    w_fl = 1.0 - w_up
    return {f"Wing_{side}": w_up, f"Flight_{side}": w_fl}
  if bone.startswith("Tail"):
    idx = min(3, int(t * 3.99))
    return {f"Tail{idx}": 1.0}
  return {bone: 1.0}


class Surface:
  """Accumulates indexed mesh primitives with normalized weights and UVs."""

  def __init__(self, name="CanopySurface", rig=None, mat=None):
    self.name = name
    self.rig = rig
    self.mat = mat
    self.verts = []
    self.faces = []
    self.uvs = []
    self.weights = []
    self.mats = []

  def _norm(self, weights):
    if isinstance(weights, str):
      return {weights: 1.0}
    if not weights:
      return {"Root": 1.0}
    clean = {k: max(0.0, float(v)) for k, v in weights.items() if float(v) > 1e-6}
    s = sum(clean.values())
    return {k: v / s for k, v in clean.items()} if s > 1e-6 else {"Root": 1.0}

  def vertex(self, co, uv=(0.0, 0.0), weights=None):
    idx = len(self.verts)
    self.verts.append(Vector(co))
    self.uvs.append((float(uv[0]), float(uv[1])))
    self.weights.append(self._norm(weights))
    return idx

  def face(self, verts, mat=0):
    if len(set(verts)) >= 3:
      self.faces.append(tuple(verts))
      self.mats.append(int(mat))

  def ellipsoid(self, center, radius, bone, mat=0, segments=16, tilt=(0, 0, 0)):
    c = Vector(center)
    rx, ry, rz = max(0.001, radius[0]), max(0.001, radius[1]), max(0.001, radius[2])
    rot = Euler(tilt, "XYZ").to_matrix()
    rings = max(6, segments // 2)
    grid = []
    for i in range(rings + 1):
      phi = math.pi * (i / rings)
      row = []
      for j in range(segments):
        theta = TAU * (j / segments)
        loc = Vector((
            rx * math.sin(phi) * math.cos(theta),
            ry * math.cos(phi),
            rz * math.sin(phi) * math.sin(theta),
        ))
        row.append(self.vertex(c + (rot @ loc), (j / segments, i / rings), bone))
      grid.append(row)
    for i in range(rings):
      for j in range(segments):
        jn = (j + 1) % segments
        if i == 0:
          self.face((grid[0][j], grid[1][jn], grid[1][j]), mat)
        elif i == rings - 1:
          self.face((grid[i][j], grid[i][jn], grid[i + 1][j]), mat)
        else:
          self.face((grid[i][j], grid[i][jn], grid[i + 1][jn], grid[i + 1][j]), mat)

  def tube(self, points, radii, bone, mat=0, segments=16):
    rings = []
    n_pts = len(points)
    for i, pt in enumerate(points):
      c = Vector(pt)
      r_pair = radii[i] if isinstance(radii[i], (tuple, list)) else (radii[i], radii[i])
      rx, ry = max(0.0006, float(r_pair[0])), max(0.0006, float(r_pair[1]))
      if i == 0:
        tang = (Vector(points[1]) - c).normalized()
      elif i == n_pts - 1:
        tang = (c - Vector(points[i - 1])).normalized()
      else:
        tang = (Vector(points[i + 1]) - Vector(points[i - 1])).normalized()
      if tang.length < 1e-6:
        tang = Vector((0, 0, 1))
      up_ref = Vector((0, 1, 0)) if abs(tang.y) < 0.95 else Vector((1, 0, 0))
      right = tang.cross(up_ref).normalized()
      up = right.cross(tang).normalized()

      w = bone(i / max(1, n_pts - 1)) if callable(bone) else bone
      ring = []
      for j in range(segments):
        ang = TAU * (j / segments)
        pos = c + right * (math.cos(ang) * rx) + up * (math.sin(ang) * ry)
        ring.append(self.vertex(pos, (j / segments, i / max(1, n_pts - 1)), w))
      rings.append(ring)

    for i in range(n_pts - 1):
      for j in range(segments):
        jn = (j + 1) % segments
        self.face((rings[i][j], rings[i][jn], rings[i + 1][jn], rings[i + 1][j]), mat)

    # Cap tube ends
    for end_i, flip in ((0, True), (-1, False)):
      c = Vector(points[end_i]) + Vector((0, 0, -0.0015 if flip else 0.0015))
      w = bone(0.0 if flip else 1.0) if callable(bone) else bone
      cap_v = self.vertex(c, (0.5, 0.0 if flip else 1.0), w)
      for j in range(segments):
        jn = (j + 1) % segments
        if flip:
          self.face((cap_v, rings[end_i][jn], rings[end_i][j]), mat)
        else:
          self.face((cap_v, rings[end_i][j], rings[end_i][jn]), mat)

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
      if isinstance(self.mat, (list, tuple)):
        for m in self.mat:
          obj.data.materials.append(m)
      else:
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


def rig_for(species):
  """Builds the armature hierarchy for 'crested_canopy' (Hoatzin) or 'quilled_arboreal' (Porcupine)."""
  avian = species in ("crested_canopy", "hoatzin")
  arm = bpy.data.armatures.new(species + "_Armature")
  rig = bpy.data.objects.new(species + "_Rig", arm)
  bpy.context.collection.objects.link(rig)
  bpy.context.view_layer.objects.active = rig
  rig.select_set(True)
  bpy.ops.object.mode_set(mode="EDIT")
  eb = arm.edit_bones

  def add(name, h, t, parent="Root"):
    b = eb.new(name)
    b.head = h
    b.tail = t
    if parent and parent in eb:
      b.parent = eb[parent]
    return b

  add("Root", (0, 0, 0), (0, .12, 0), None)
  if avian:
    # Hoatzin: Steeply pitched body with low anterior crop/sternum and high S-curved neck
    add("Body", (0, .28, .18), (0, .26, -.08))
    add("Chest", (0, .26, -.08), (0, .31, -.22), "Body")
    add("Neck", (0, .31, -.22), (0, .48, -.36), "Chest")
    add("Head", (0, .48, -.36), (0, .54, -.52), "Neck")
    add("Jaw", (0, .50, -.50), (0, .49, -.61), "Head")
    add("Crest", (0, .55, -.46), (0, .74, -.32), "Head")
    tailpts = [(0, .29, .26), (0, .27, .44), (0, .25, .62), (0, .23, .80), (0, .21, .98)]
    for i in range(4):
      add("Tail" + str(i), tailpts[i], tailpts[i + 1], "Body" if i == 0 else "Tail" + str(i - 1))
    for s, n in ((1, "L"), (-1, "R")):
      add("Wing_" + n, (s * .115, .31, -.10), (s * .195, .30, .18), "Chest")
      add("Flight_" + n, (s * .195, .30, .18), (s * .210, .22, .56), "Wing_" + n)
      add("Hind_Upper_" + n, (s * .082, .25, .10), (s * .095, .13, .04), "Body")
      add("Hind_Lower_" + n, (s * .095, .13, .04), (s * .095, .028, .12), "Hind_Upper_" + n)
      add("Hind_Foot_" + n, (s * .095, .028, .12), (s * .095, .020, .02), "Hind_Lower_" + n)
  else:
    # Prehensile-tailed Porcupine: Arched scansorial spine and long upward-curling prehensile tail
    add("Body", (0, .30, .30), (0, .32, .04))
    add("Chest", (0, .32, .04), (0, .30, -.18), "Body")
    add("Neck", (0, .30, -.18), (0, .31, -.34), "Chest")
    add("Head", (0, .31, -.34), (0, .29, -.56), "Neck")
    # 4-bone prehensile tail chain curling upward at the tip (dorsal callus grip)
    t_pts = [(0, .28, .34), (0, .24, .56), (0, .22, .78), (0, .28, .98), (0, .38, 1.14)]
    for i in range(4):
      add("Tail" + str(i), t_pts[i], t_pts[i + 1], "Body" if i == 0 else "Tail" + str(i - 1))
    for s, n in ((1, "L"), (-1, "R")):
      add("Arm_" + n, (s * .095, .28, -.12), (s * .155, .17, -.02), "Chest")
      add("Hand_" + n, (s * .155, .17, -.02), (s * .140, .045, -.14), "Arm_" + n)
      add("Leg_" + n, (s * .108, .27, .24), (s * .168, .15, .32), "Body")
      add("Foot_" + n, (s * .168, .15, .32), (s * .150, .045, .42), "Leg_" + n)

  bpy.ops.object.mode_set(mode="OBJECT")
  return rig


def body_loft(surface, profile, mat, avian=False):
  """Lofts smooth organic body mantle from (z, y, rx, ry) profile tuples."""
  pts = [(0.0, p[1], p[0]) for p in profile]
  rads = [(p[2], p[3]) for p in profile]

  def w_fn(u):
    z = profile[0][0] + u * (profile[-1][0] - profile[0][0])
    if avian:
      anchors = [("Head", -.46), ("Neck", -.30), ("Chest", -.12), ("Body", .12)]
    else:
      anchors = [("Head", -.46), ("Neck", -.28), ("Chest", -.10), ("Body", .18)]
    w = {}
    for bname, az in anchors:
      d = abs(z - az)
      val = math.exp(-(d * d) / 0.045)
      if val > 1e-4:
        w[bname] = val
    return w

  surface.tube(pts, rads, w_fn, mat=mat, segments=28)


def build_canopy_guardian(species="crested_canopy", out=None, crest_v4=True,
                          bird_face_fix=True, porcupine_v2=True):
  """Constructs lifelike Hoatzin ('crested_canopy') or Porcupine ('quilled_arboreal')."""
  bpy.ops.wm.read_factory_settings(use_empty=True)
  bpy.context.scene.render.fps = 30
  avian = species in ("crested_canopy", "hoatzin")
  folder = "crested_canopy" if avian else "quilled_arboreal"
  if out is None:
    out = ROOT / "game/assets/guardians" / folder / "guardian.glb"
  out = Path(out)
  out.parent.mkdir(parents=True, exist_ok=True)

  rig = rig_for(species)
  mat = bpy.data.materials.new(folder.capitalize() + "Mat")
  mat.use_nodes = True
  s = Surface(folder.capitalize(), rig=rig, mat=mat)

  if avian:
    # Hoatzin (Opisthocomus hoazin):
    # - Small oval cranium (z=-.52..-.42, rx=.038, ry=.036)
    # - Long, slender S-curved neck (z=-.38..-.24, rx=.028..034, ry=.030..036)
    # - Prominent anterior muscular crop pouch (z=-.12..-.02, y=.255, rx=.118, ry=.136)
    # - Compact ovoid torso tapering to pygostyle (z=.32)
    profile = [
        (-.535, .522, .016, .014),  # Upper lore / cere transition
        (-.495, .528, .036, .034),  # Anterior cranium / blue facial mask
        (-.445, .526, .040, .038),  # Rounded skull dome
        (-.395, .492, .029, .031),  # Upper cervical constriction
        (-.340, .435, .028, .030),  # Slender S-curved mid-neck
        (-.280, .372, .035, .038),  # Lower cervical flexure
        (-.210, .318, .068, .076),  # Neck-to-crop transition
        (-.130, .272, .112, .128),  # Enlarged anterior foregut crop bulge
        (-.050, .258, .122, .138),  # Maximum crop & sternal callosity depth (ry > rx)
        ( .030, .265, .118, .124),  # Mid-thorax
        ( .110, .275, .104, .106),  # Posterior ribcage
        ( .190, .284, .082, .080),  # Pelvic synsacrum
        ( .260, .290, .054, .050),  # Uropygial taper
        ( .320, .292, .026, .022),  # Pygostyle / tail fan base
    ]
    body_loft(s, profile, 0, avian=True)

    # Stout, laterally compressed decurved folivorous bill (upper maxilla & lower mandible)
    s.tube(
        [(0, .518, -.530), (0, .515, -.565), (0, .506, -.598), (0, .494, -.622)],
        [(.020, .018), (.016, .015), (.010, .011), (.003, .004)],
        "Head", mat=0, segments=14,
    )
    s.tube(
        [(0, .502, -.528), (0, .499, -.560), (0, .495, -.592), (0, .492, -.612)],
        [(.017, .009), (.014, .008), (.008, .005), (.002, .002)],
        "Jaw", mat=0, segments=12,
    )

    # Bare blue facial skin orbital rings & crimson eyes
    for side in (1, -1):
      s.ellipsoid((side * .034, .526, -.485), (.008, .014, .018), "Head", mat=0, segments=14)
      s.ellipsoid((side * .038, .526, -.485), (.006, .009, .009), "Head", mat=0, segments=12)

    # Tall, spiky 11-plume radial fan crest (signature Opisthocomus feature)
    for idx in range(11):
      u = idx / 10.0
      pitch = math.radians(38.0 + u * 68.0)  # Sweeps from forward-angled to swept-back plumes
      yaw = (idx - 5) * 0.032
      length = 0.145 + 0.045 * math.sin(u * math.pi)
      base = Vector(((idx - 5) * 0.0035, .542, -.455 + u * 0.045))
      dir_v = Vector((math.sin(yaw) * 0.25, math.sin(pitch), math.cos(pitch) * 0.85)).normalized()
      pts = [
          base,
          base + dir_v * (length * 0.35),
          base + dir_v * (length * 0.72) + Vector((0, -0.008, 0.012)),
          base + dir_v * length + Vector((0, -0.015, 0.022)),
      ]
      s.tube(pts, [(.005, .002), (.009, .0025), (.007, .002), (.0015, .001)], "Crest", mat=0, segments=8)

    # Broad rounded wings with layered primary/secondary flight feathers & functional alula claws
    for side, n in ((1, "L"), (-1, "R")):
      w_bone = f"Wing_{n}"
      f_bone = f"Flight_{n}"
      # Wing muscular leading edge patagium + alula thumb claw
      s.tube(
          [(side * .108, .31, -.10), (side * .185, .30, .06), (side * .205, .28, .24)],
          [(.026, .022), (.022, .018), (.014, .012)],
          w_bone, mat=0, segments=12,
      )
      # Functional wing claw on alular/manual digit
      s.tube(
          [(side * .188, .305, .04), (side * .208, .302, .01), (side * .216, .294, -.012)],
          [(.005, .005), (.003, .003), (.001, .001)],
          w_bone, mat=0, segments=8,
      )
      # 9 overlapping cambered primary & secondary remiges
      for f_idx in range(9):
        fu = f_idx / 8.0
        root_p = Vector((side * (.135 + fu * .065), .298 - fu * .018, -.04 + fu * .24))
        tip_p = Vector((side * (.175 + fu * .045), .215 - fu * .025, .26 + fu * .34))
        mid_p = root_p.lerp(tip_p, 0.52) + Vector((side * .018, .012, 0))
        s.tube(
            [root_p, mid_p, tip_p],
            [(.018, .003), (.026, .003), (.008, .0015)],
            lambda t, b=w_bone: feather_weights(b, t),
            mat=0, segments=10,
        )

    # Long, broad 10-rectrix tail fan (~44% of total length)
    for r_idx in range(10):
      ru = (r_idx - 4.5) / 4.5
      spread_x = ru * 0.135
      root_t = Vector((ru * 0.022, .288, .28))
      mid_t = Vector((spread_x * 0.55, .252, .62))
      tip_t = Vector((spread_x, .208 - abs(ru) * 0.018, .98 - abs(ru) * 0.05))
      s.tube(
          [root_t, mid_t, tip_t],
          [(.016, .003), (.024, .003), (.020, .002)],
          lambda t: feather_weights("Tail", t),
          mat=0, segments=10,
      )

    # Strong scansorial avian legs & anisodactyl grasping toes (3 forward, 1 hallux rear)
    for side, n in ((1, "L"), (-1, "R")):
      hip = Vector((side * .082, .255, .10))
      knee = Vector((side * .095, .135, .04))
      tarsus = Vector((side * .095, .032, .11))
      s.tube([hip, knee], [(.032, .036), (.020, .022)], f"Hind_Upper_{n}", mat=0, segments=12)
      s.tube([knee, tarsus], [(.016, .016), (.011, .011)], f"Hind_Lower_{n}", mat=0, segments=10)
      for toe_dx, toe_dz in ((-.022, -.075), (0.0, -.088), (.022, -.075), (0.0, .058)):
        toe_end = tarsus + Vector((toe_dx, -.012, toe_dz))
        s.tube([tarsus, toe_end], [(.007, .007), (.0025, .0025)], f"Hind_Foot_{n}", mat=0, segments=8)

  else:
    # Prehensile-tailed Porcupine (Coendou prehensilis):
    # - Blunt, wide bulbous nose pad (z=-.58, rx=.044, ry=.042)
    # - Strongly convex inflated nasofrontal sinus forehead dome (z=-.48..-.38, y=.318, ry=.074)
    # - Arched scansorial rodent back (z=-.14..+.22, rx=.138, ry=.148)
    profile = [
        (-.58, .278, .032, .030),  # Wide fleshy bulbous rhinarium tip
        (-.53, .286, .048, .046),  # Broad blunt muzzle
        (-.45, .312, .064, .072),  # Inflated nasofrontal sinus dome (characteristic convex profile)
        (-.36, .314, .068, .070),  # Cranial vault
        (-.26, .304, .066, .068),  # Short muscular neck
        (-.14, .318, .108, .118),  # Scapular arch
        ( .00, .332, .136, .148),  # Arched mid-thoracic dorsum (ry > rx)
        ( .14, .334, .142, .150),  # Lumbar arch
        ( .26, .316, .118, .122),  # Pelvic / sacral croup
        ( .36, .288, .074, .072),  # Muscular tail base transition
    ]
    body_loft(s, profile, 0, avian=False)

    # Bulbous nostrils, small sub-dermal ears, and dark laterally placed rodent eyes
    for side in (1, -1):
      s.ellipsoid((side * .020, .284, -.575), (.010, .012, .008), "Head", mat=0, segments=10)
      s.ellipsoid((side * .048, .312, -.465), (.009, .009, .010), "Head", mat=0, segments=12)
      s.ellipsoid((side * .058, .316, -.365), (.008, .016, .014), "Head", mat=0, segments=12)

    # Long, muscular, upward-coiling prehensile tail (~90% of head-body length, z=.36..1.16)
    tail_curve = [
        (0.0, .288, .36),
        (0.0, .258, .50),
        (0.0, .232, .64),
        (0.0, .218, .78),
        (0.0, .236, .92),
        (0.0, .282, 1.04),
        (0.0, .348, 1.12),
        (0.0, .392, 1.08),  # Upward dorsal prehensile grasping curl
        (0.0, .405, 1.01),
    ]
    tail_rads = [
        (.062, .060), (.048, .046), (.038, .036), (.030, .028),
        (.024, .022), (.019, .018), (.014, .014), (.010, .010), (.005, .005),
    ]
    s.tube(tail_curve, tail_rads, lambda t: feather_weights("Tail", t), mat=0, segments=18)

    # Scansorial grasping limbs with fleshy plantar pads and 4 long curved arboreal claws
    for side, n in ((1, "L"), (-1, "R")):
      sh = Vector((side * .095, .28, -.12))
      el = Vector((side * .155, .17, -.02))
      wr = Vector((side * .140, .048, -.14))
      s.tube([sh, el, wr], [(.042, .046), (.032, .034), (.024, .020)], f"Arm_{n}", mat=0, segments=14)
      s.ellipsoid(wr + Vector((0, -.010, -.025)), (.028, .014, .036), f"Hand_{n}", mat=0, segments=12)
      for c_i in range(4):
        off_x = (c_i - 1.5) * 0.011
        c_start = wr + Vector((off_x, -.010, -.052))
        c_end = c_start + Vector((0, -.012, -.028))
        s.tube([c_start, c_end], [(.004, .005), (.001, .0015)], f"Hand_{n}", mat=0, segments=8)

      hp = Vector((side * .108, .27, .24))
      kn = Vector((side * .168, .15, .32))
      an = Vector((side * .150, .048, .42))
      s.tube([hp, kn, an], [(.048, .052), (.036, .038), (.026, .022)], f"Leg_{n}", mat=0, segments=14)
      s.ellipsoid(an + Vector((0, -.010, -.028)), (.032, .015, .042), f"Foot_{n}", mat=0, segments=12)
      for c_i in range(4):
        off_x = (c_i - 1.5) * 0.012
        c_start = an + Vector((off_x, -.010, -.060))
        c_end = c_start + Vector((0, -.012, -.030))
        s.tube([c_start, c_end], [(.0045, .0055), (.001, .0015)], f"Foot_{n}", mat=0, segments=8)

    # Stratified tricolored bristle-quills across crown, scapular mantle, lumbar back, and tail base
    quill_rings = [
        (-.38, .312, .062, .066, 7,  0.075, "Head"),   # Short crown quills framing forehead dome
        (-.26, .304, .064, .066, 9,  0.105, "Neck"),   # Nuchal collar quills
        (-.14, .318, .104, .114, 13, 0.155, "Chest"),  # Dense scapular mantle quills
        (-.02, .330, .132, .144, 15, 0.185, "Body"),   # Long mid-dorsal defensive quills
        ( .10, .334, .138, .146, 15, 0.190, "Body"),   # Peak lumbar defensive quills
        ( .22, .320, .118, .122, 13, 0.165, "Body"),   # Pelvic / sacral quills
        ( .32, .295, .082, .080, 9,  0.115, "Tail0"),  # Proximal tail base quills
        ( .44, .265, .052, .050, 7,  0.075, "Tail0"),  # Tapering to spineless prehensile distal tail
    ]
    for qz, qy, qrx, qry, count, qlen, qbone in quill_rings:
      for qi in range(count):
        frac = (qi / max(1, count - 1)) - 0.5  # -0.5 to +0.5 across dorsal/lateral arc
        ang = frac * math.pi * 0.82
        nx = math.sin(ang)
        ny = math.cos(ang)
        base_p = Vector((nx * qrx * 0.92, qy + ny * qry * 0.92, qz))
        # Quills sweep backward (+Z) and slightly outward along body contour
        dir_v = Vector((nx * 0.42, ny * 0.38 + 0.12, 0.84)).normalized()
        mid_p = base_p + dir_v * (qlen * 0.55)
        tip_p = base_p + dir_v * qlen
        s.tube([base_p, mid_p, tip_p], [(.0035, .0035), (.0052, .0052), (.0008, .0008)], qbone, mat=0, segments=6)

  body_obj = s.build()

  # Bake required animation clips ('idle', 'feed', 'rest', 'walk')
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
          pb.rotation_euler = (0.14 * math.sin(phase), 0.0, 0.0)
        elif clip == "feed" and pb.name in ("Neck", "Head"):
          pb.rotation_euler = (-0.20 * math.sin(phase * 0.5), 0.0, 0.0)
        elif clip == "rest":
          pb.rotation_euler = (0.06, 0.0, 0.0)
        else:
          pb.rotation_euler = (0.03 * math.sin(phase), 0.0, 0.0)
        pb.keyframe_insert(data_path="rotation_euler", frame=f)

  bpy.ops.export_scene.gltf(
      filepath=str(out.resolve()),
      export_format="GLB",
      export_yup=True,
      export_skins=True,
      export_animations=True,
  )
  return body_obj, rig


def generate(species="crested_canopy", out=None):
  return build_canopy_guardian(species=species, out=out)


def main():
  species = "quilled_arboreal" if "--porcupine-v2" in sys.argv or "--porcupine" in sys.argv else "crested_canopy"
  build_canopy_guardian(species=species)


if __name__ == "__main__":
  main()
