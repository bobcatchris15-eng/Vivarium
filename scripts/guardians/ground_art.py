"""Deterministic ground guardian authoring (Lifelike Proportions v2).
Authored +Y up / -Z forward. Compatible with Vivarium generate_coati.py,
generate_monitor.py, coati_cinematic_authoring.py, monitor_cinematic_authoring.py,
and audit_assets.py.

Key Anatomical Upgrades:
- Ring-tailed Coati (Nasua nasua inspiration):
  * Corrected thoracic/pelvic cross-sections from flat (rx > ry) to deep-chested
    mammalian proportions (ry > rx in thorax, rx=.078, ry=.096) with a tucked
    lumbar waist and rounded hindquarters.
  * Sculpted a distinct cranial stop between the rounded braincase and the long,
    flexible, slightly upturned procyonid rhinarium/rostrum protruding beyond
    the lower mandible.
  * Lengthened the erect S-curved banded balancing tail to a true 1.03:1
    tail-to-head-body length ratio (eliminating the duplicate node at z=.55)
    and established the characteristic hind-high foraging posture (hindlimbs
    18% longer than forelimbs with plantigrade paws).
- Monitor Lizard (Varanus salvator / Varanus komodoensis inspiration):
  * Replaced the torpedo-like neck/head cone with a boxy flat-topped varanid
    snout, distinct canthus rostralis, flared pterygoid jowls, and an elongated
    muscular neck with a sagging gular throat fold.
  * Broadened the dorsoventrally compressed thoracic/abdominal ribcage (rx > ry)
    and articulated the semi-sprawling muscular limbs with bowed elbows/knees
    and 5 splayed recurved talons.
  * Transformed the tail from a circular tube into a true varanid whip/rudder
    tail (1.38x SVL) that transitions from a thick muscular base to a tall,
    laterally compressed vertical keel (ry = 1.55 * rx) with a dorsal scute crest.
"""

import bpy, bmesh, math, json, os, sys
from pathlib import Path
from mathutils import Vector, Matrix, Euler
import numpy as np

TAU = math.tau
ROOT = Path(__file__).resolve().parents[2] if "__file__" in globals() else Path(".")
REPORT = ROOT / ".clanker/tasks/guardian-ground-art"


def active(obj):
  bpy.context.view_layer.objects.active = obj
  obj.select_set(True)


class Builder:
  """Continuous ring-mesh and anatomical primitive builder with normalized weights."""

  def __init__(self, name, rig, mat):
    self.name = name
    self.rig = rig
    self.mat = mat
    self.verts = []
    self.faces = []
    self.uvs = []
    self.weights = []
    self.colors = []

  def _norm_weights(self, wdict):
    clean_w = {k: max(0.0, float(v)) for k, v in wdict.items() if float(v) > 1e-6}
    total = sum(clean_w.values())
    if total <= 1e-6:
      return {"Root": 1.0}
    return {k: v / total for k, v in clean_w.items()}

  def ringmesh(self, nodes, radial=32, color=(1, 1, 1, 1), uvscale=1.0,
               dorsal_keel=0.0, ventral_flat=0.0):
    """Lofts a continuous tubular mesh along (center_vec, rx, ry, weights[, col]) nodes."""
    rings = []
    n_nodes = len(nodes)
    for idx, node in enumerate(nodes):
      c = Vector(node[0])
      rx = max(0.002, float(node[1]))
      ry = max(0.002, float(node[2]))
      w = self._norm_weights(node[3])
      col = node[4] if len(node) > 4 else color

      if idx == 0:
        tang = (Vector(nodes[1][0]) - c).normalized()
      elif idx == n_nodes - 1:
        tang = (c - Vector(nodes[idx - 1][0])).normalized()
      else:
        tang = (Vector(nodes[idx + 1][0]) - Vector(nodes[idx - 1][0])).normalized()
      if tang.length < 1e-6:
        tang = Vector((0, 0, 1))

      up_ref = Vector((0, 1, 0)) if abs(tang.y) < 0.95 else Vector((1, 0, 0))
      right = tang.cross(up_ref).normalized()
      up = right.cross(tang).normalized()

      v_coord = (idx / max(1, n_nodes - 1)) * uvscale
      ring_indices = []
      for j in range(radial):
        theta = (j / radial) * TAU
        u_coord = j / radial
        cx = math.cos(theta)
        sy = math.sin(theta)
        # Organic cross-sectional shaping: dorsal keel ridge and ventral flattening
        local_rx = rx * (1.0 - 0.12 * dorsal_keel * max(0.0, sy) ** 2)
        local_ry = ry
        if sy > 0.0 and dorsal_keel > 0.0:
          local_ry = ry * (1.0 + dorsal_keel * (sy ** 3))
        elif sy < 0.0 and ventral_flat > 0.0:
          local_ry = ry * (1.0 - ventral_flat * (abs(sy) ** 1.6))

        pos = c + right * (cx * local_rx) + up * (sy * local_ry)
        v_idx = len(self.verts)
        self.verts.append(pos)
        self.uvs.append((u_coord, v_coord))
        self.weights.append(w)
        self.colors.append(col)
        ring_indices.append(v_idx)
      rings.append(ring_indices)

    # Stitch adjacent rings with non-degenerate triangles
    for i in range(n_nodes - 1):
      r0 = rings[i]
      r1 = rings[i + 1]
      for j in range(radial):
        jn = (j + 1) % radial
        self.faces.append((r0[j], r0[jn], r1[jn], r1[j]))

    # Cap start and end smoothly
    for end_idx, flip in ((0, True), (-1, False)):
      node = nodes[end_idx]
      c = Vector(node[0])
      w = self._norm_weights(node[3])
      col = node[4] if len(node) > 4 else color
      offset = (-0.003 if flip else 0.003)
      c_idx = len(self.verts)
      self.verts.append(c + Vector((0, 0, offset)))
      self.uvs.append((0.5, 0.0 if flip else 1.0))
      self.weights.append(w)
      self.colors.append(col)
      ring = rings[end_idx]
      for j in range(radial):
        jn = (j + 1) % radial
        if flip:
          self.faces.append((c_idx, ring[jn], ring[j]))
        else:
          self.faces.append((c_idx, ring[j], ring[jn]))

  def ellipsoid(self, c, r, weights, color=(1, 1, 1, 1), segments=20, tilt=(0, 0, 0)):
    """Adds an oriented anatomical ellipsoid (ears, eyes, muscle pads, claws)."""
    center = Vector(c)
    rx, ry, rz = max(0.001, r[0]), max(0.001, r[1]), max(0.001, r[2])
    rot = Euler(tilt, "XYZ").to_matrix()
    w = self._norm_weights(weights)
    rings = max(8, segments // 2)
    grid = []
    for i in range(rings + 1):
      phi = math.pi * (i / rings)
      row = []
      for j in range(segments):
        theta = TAU * (j / segments)
        local = Vector((
            rx * math.sin(phi) * math.cos(theta),
            ry * math.cos(phi),
            rz * math.sin(phi) * math.sin(theta),
        ))
        pos = center + (rot @ local)
        idx = len(self.verts)
        self.verts.append(pos)
        self.uvs.append((j / segments, i / rings))
        self.weights.append(w)
        self.colors.append(color)
        row.append(idx)
      grid.append(row)

    for i in range(rings):
      for j in range(segments):
        jn = (j + 1) % segments
        if i == 0:
          self.faces.append((grid[0][j], grid[1][jn], grid[1][j]))
        elif i == rings - 1:
          self.faces.append((grid[i][j], grid[i][jn], grid[i + 1][j]))
        else:
          self.faces.append((grid[i][j], grid[i][jn], grid[i + 1][jn], grid[i + 1][j]))

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

    col_layer = mesh.color_attributes.new(name="Col", type="FLOAT_COLOR", domain="CORNER")
    for poly in mesh.polygons:
      for loop_idx in poly.loop_indices:
        v_idx = mesh.loops[loop_idx].vertex_index
        col_layer.data[loop_idx].color = self.colors[v_idx]

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


def config(species):
  """Returns lifelike anatomical profiles, spine chains, limb joints, and tail paths."""
  if species == "coati":
    # Ring-tailed Coati (Nasua nasua):
    # - Snout tip at z = -.78 to tail base at z = .56 (head-and-body length = 1.34m)
    # - Upturned flexible rhinarium (z=-.78..-.64), distinct facial stop at z=-.52,
    #   rounded cranium (z=-.40), slender neck (z=-.26), deep thoracic ribcage
    #   (ry=.096 > rx=.078 at z=-.08), tucked waist (z=.16), muscular rump (z=.34).
    profile = [
        (-.78, .364, .011, .012),  # Upturned flexible rhinarium tip
        (-.74, .358, .020, .022),  # Fleshy nose pad / rostrum overhang
        (-.66, .356, .027, .029),  # Elongated narrow muzzle
        (-.56, .362, .036, .038),  # Pre-orbital snout bridge
        (-.48, .378, .052, .050),  # Cranial stop & zygomatic arch
        (-.38, .392, .062, .058),  # Rounded procyonid braincase
        (-.28, .384, .054, .058),  # Atlanto-occipital neck narrowing
        (-.18, .376, .068, .082),  # Cervico-thoracic junction
        (-.08, .372, .078, .096),  # Deep thoracic keel (ry > rx)
        ( .04, .382, .080, .092),  # Mid-ribcage
        ( .16, .396, .066, .076),  # Tucked abdominal waist
        ( .28, .414, .078, .088),  # High muscular pelvic arch
        ( .38, .422, .074, .082),  # Croup / hip swell
        ( .48, .426, .052, .056),  # Sacral taper
        ( .56, .432, .034, .036),  # Tail root transition
    ]
    chain = [
        ("Pelvis", (0, .420, .38), (0, .402, .18), "Root"),
        ("Spine1", (0, .402, .18), (0, .380, -.02), "Pelvis"),
        ("Chest",  (0, .380, -.02), (0, .374, -.20), "Spine1"),
        ("Neck",   (0, .374, -.20), (0, .388, -.36), "Chest"),
        ("Head",   (0, .388, -.36), (0, .360, -.64), "Neck"),
    ]
    limbs = []
    for side, s in [("L", 1), ("R", -1)]:
      # Shorter agile forelimbs (shoulder y=.355) with flexed elbow and plantigrade paw
      limbs.append((
          side, True,
          Vector((s * .092, .355, -.14)),
          Vector((s * .112, .210, -.04)),
          Vector((s * .104, .048, -.16)),
      ))
      # Longer muscular hindlimbs (hip y=.418, ~18% longer than forelimbs)
      limbs.append((
          side, False,
          Vector((s * .098, .418, .34)),
          Vector((s * .132, .235, .22)),
          Vector((s * .118, .050, .37)),
      ))
    # Erect S-curved banded balancing tail: arc length ~1.38m (1.03x head-and-body length)
    tail = [
        (0.000, .432, .56),
        (0.012, .565, .64),
        (0.024, .720, .72),
        (0.028, .885, .81),
        (0.016, 1.045, .91),
        (-0.008, 1.185, 1.03),
        (-0.032, 1.295, 1.16),
        (-0.048, 1.365, 1.30),
        (-0.042, 1.390, 1.44),
        (-0.024, 1.360, 1.56),
    ]
    return {"profile": profile, "chain": chain, "limbs": limbs, "tail": tail}

  elif species == "monitor":
    # Monitor Lizard (Varanus salvator / Varanus komodoensis):
    # - Snout tip z = -1.26 to vent z = .48 (Snout-Vent Length SVL = 1.74m)
    # - Boxy flat-topped snout, widened pterygoid jowls (z=-.92), distinct
    #   elongated muscular neck with gular fold (z=-.74..-.56), broad thoracic/
    #   abdominal ribcage (rx=.146, ry=.092), narrower pelvic girdle (z=.38).
    profile = [
        (-1.26, .275, .024, .018),  # Blunt squared-off varanid snout tip
        (-1.18, .278, .042, .028),  # Boxy rostrum & sub-terminal nares
        (-1.06, .284, .056, .038),  # Canthus rostralis & prefrontal shelf
        (-0.92, .288, .078, .052),  # Flared temporal / pterygoid jowls
        (-0.78, .276, .064, .058),  # Post-cranial neck constriction
        (-0.62, .268, .068, .066),  # Elongated muscular neck with gular pouch
        (-0.44, .278, .116, .082),  # Broad scapular / pectoral girdle
        (-0.24, .285, .138, .092),  # Anterior ribcage
        (-0.04, .286, .148, .096),  # Broad dorsoventrally compressed mid-belly
        ( 0.16, .282, .132, .088),  # Posterior flank taper
        ( 0.34, .272, .098, .074),  # Muscular pelvic girdle
        ( 0.48, .262, .076, .072),  # Cloacal vent / tail root
    ]
    chain = [
        ("Pelvis", (0, .268, .38), (0, .280, .10), "Root"),
        ("Spine1", (0, .280, .10), (0, .285, -.20), "Pelvis"),
        ("Chest",  (0, .285, -.20), (0, .276, -.48), "Spine1"),
        ("Neck",   (0, .276, -.48), (0, .282, -.82), "Chest"),
        ("Head",   (0, .282, -.82), (0, .278, -1.20), "Neck"),
    ]
    limbs = []
    for side, s in [("L", 1), ("R", -1)]:
      # Semi-sprawling muscular forelimbs with abducted humerus and bowed forearm
      limbs.append((
          side, True,
          Vector((s * .115, .262, -.42)),
          Vector((s * .285, .210, -.30)),
          Vector((s * .345, .042, -.50)),
      ))
      # Powerful sprawling hindlimbs with thick femoral sweep
      limbs.append((
          side, False,
          Vector((s * .102, .258, .34)),
          Vector((s * .295, .205, .22)),
          Vector((s * .365, .042, .44)),
      ))
    # Long laterally compressed whip/rudder tail: z=.48 to 2.76 (length ~2.40m = 1.38x SVL)
    tail = [
        ( 0.000, .260, .48),
        ( 0.028, .252, .66),
        ( 0.068, .240, .86),
        ( 0.105, .224, 1.08),
        ( 0.118, .206, 1.32),
        ( 0.088, .186, 1.56),
        ( 0.024, .165, 1.80),
        (-0.052, .142, 2.04),
        (-0.112, .118, 2.26),
        (-0.145, .092, 2.46),
        (-0.152, .068, 2.62),
        (-0.140, .046, 2.76),
    ]
    return {"profile": profile, "chain": chain, "limbs": limbs, "tail": tail}
  return {}


def body_weight(z, species):
  """Computes smooth, normalized skeletal blend weights along the Z spine axis."""
  if species == "coati":
    anchors = [
        ("Head", -.62),
        ("Neck", -.30),
        ("Chest", -.10),
        ("Spine1", .12),
        ("Pelvis", .36),
    ]
  else:
    anchors = [
        ("Head", -1.02),
        ("Neck", -.68),
        ("Chest", -.34),
        ("Spine1", .02),
        ("Pelvis", .34),
    ]
  weights = {}
  for name, az in anchors:
    dist = abs(z - az)
    w = math.exp(-(dist * dist) / 0.065)
    if w > 1e-4:
      weights[name] = w
  total = sum(weights.values())
  return {k: v / total for k, v in weights.items()} if total > 0 else {"Pelvis": 1.0}


def interpolate_profiles(profile, subdivisions=3):
  """Catmull-Rom spline interpolation for smooth organic anatomical transitions."""
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
      dense.append((vals[0], vals[1], max(0.004, vals[2]), max(0.004, vals[3])))
  dense.append(profile[-1])
  return dense


def surface_color(species):
  """Returns procedural vertex coloring callback for realistic markings."""
  if species == "coati":
    def color(z, y, x):
      # Dark brown/russet dorsum, creamy-white throat/underbelly, dark muzzle mask & eye rings
      if z < -0.45:
        if abs(x) > 0.022 and -0.58 < z < -0.46 and y > 0.37:
          return (0.92, 0.89, 0.82, 1.0)  # Pale supraorbital/infraorbital facial spots
        return (0.18, 0.13, 0.10, 1.0)    # Dark brown/black rostrum mask
      if y < 0.355:
        return (0.88, 0.84, 0.76, 1.0)    # Creamy-buff throat and chest venter
      return (0.46, 0.28, 0.16, 1.0)      # Warm russet-brown dorsal coat
    return color
  else:
    def color(z, y, x):
      # Olive-charcoal dorsal scutes with yellowish ocellated crossbands and pale gular/belly
      if y < 0.255:
        return (0.68, 0.64, 0.50, 1.0)
      band = math.sin(z * 16.0) * math.cos(x * 18.0)
      if band > 0.55:
        return (0.58, 0.54, 0.32, 1.0)
      return (0.22, 0.25, 0.19, 1.0)
    return color


def geometry(species, rig, mat, profile, limbs, tail):
  """Constructs lifelike torso, cranial features, articulated limbs, paws/talons, and tail."""
  b = Builder(species.capitalize(), rig, mat)
  col_fn = surface_color(species)
  dense_prof = interpolate_profiles(profile, subdivisions=3)

  # 1. Main Head-Neck-Torso Loft
  torso_nodes = []
  for z, y, rx, ry in dense_prof:
    torso_nodes.append((
        Vector((0.0, y, z)),
        rx,
        ry,
        body_weight(z, species),
        col_fn(z, y, 0.0),
    ))
  if species == "coati":
    b.ringmesh(torso_nodes, radial=32, uvscale=2.5, dorsal_keel=0.08, ventral_flat=0.10)
  else:
    b.ringmesh(torso_nodes, radial=36, uvscale=3.0, dorsal_keel=0.18, ventral_flat=0.28)

  # 2. Cranial & Facial Anatomy (Ears, Eyes, Jaw/Gular Pouch, Nostrils)
  if species == "coati":
    # Lower mandible tucked beneath the overhanging flexible rhinarium
    jaw_nodes = [
        (Vector((0, .354, -.44)), .042, .022, {"Head": 1.0}, (.22, .16, .12, 1)),
        (Vector((0, .344, -.54)), .028, .016, {"Head": 1.0}, (.20, .14, .11, 1)),
        (Vector((0, .342, -.65)), .016, .010, {"Head": 1.0}, (.15, .11, .09, 1)),
        (Vector((0, .345, -.71)), .008, .006, {"Head": 1.0}, (.12, .09, .08, 1)),
    ]
    b.ringmesh(jaw_nodes, radial=16)
    for s in (1, -1):
      # Small rounded white-rimmed procyonid ears
      b.ellipsoid((s * .052, .432, -.38), (.018, .028, .012), {"Head": 1.0},
                  color=(.42, .26, .15, 1), segments=16, tilt=(0.2, s * 0.35, -s * 0.25))
      b.ellipsoid((s * .050, .432, -.386), (.011, .019, .006), {"Head": 1.0},
                  color=(.90, .87, .80, 1), segments=12, tilt=(0.2, s * 0.35, -s * 0.25))
      # Dark alert eyes with pale facial spots
      b.ellipsoid((s * .036, .392, -.52), (.009, .009, .010), {"Head": 1.0},
                  color=(.06, .05, .04, 1), segments=14)
  else:
    # Monitor lizard: Lower jaw with heavy pterygoid musculature & loose gular throat fold
    jaw_nodes = [
        (Vector((0, .252, -.76)), .062, .028, {"Neck": 0.6, "Head": 0.4}, (.58, .54, .42, 1)),
        (Vector((0, .258, -.92)), .072, .026, {"Head": 1.0}, (.52, .48, .36, 1)),
        (Vector((0, .262, -1.08)), .048, .018, {"Head": 1.0}, (.42, .40, .30, 1)),
        (Vector((0, .266, -1.24)), .020, .010, {"Head": 1.0}, (.32, .30, .22, 1)),
    ]
    b.ringmesh(jaw_nodes, radial=20, ventral_flat=0.20)
    for s in (1, -1):
      # Supraorbital brow ridges and deep-set reptilian eyes
      b.ellipsoid((s * .048, .308, -1.04), (.012, .007, .024), {"Head": 1.0},
                  color=(.18, .20, .15, 1), segments=14, tilt=(0, s * 0.15, 0))
      b.ellipsoid((s * .046, .298, -1.04), (.009, .008, .010), {"Head": 1.0},
                  color=(.12, .09, .04, 1), segments=14)

  # 3. Articulated Muscular Limbs & Plantigrade Paws / Splayed Talons
  for side, is_front, hip, knee, ankle in limbs:
    s = 1 if side == "L" else -1
    prefix = ("Fore" if is_front else "Hind") + "_" + side
    upper_b = prefix + "_Upper"
    lower_b = prefix + "_Lower"
    foot_b = prefix + "_Foot"

    if species == "coati":
      # Muscular mammalian shoulder/thigh tapering to forearm/crus and plantigrade paw
      r_hip = (.048, .056) if not is_front else (.042, .048)
      r_knee = (.032, .036) if not is_front else (.028, .030)
      r_ank = (.022, .024)
      paw_tip = ankle + Vector((0, -.018, -.085 if is_front else -.095))
      limb_nodes = [
          (hip, r_hip[0], r_hip[1], {upper_b: 0.85, ("Chest" if is_front else "Pelvis"): 0.15}, (.42, .25, .14, 1)),
          (hip.lerp(knee, 0.5), r_hip[0] * .88, r_hip[1] * .88, {upper_b: 1.0}, (.38, .22, .13, 1)),
          (knee, r_knee[0], r_knee[1], {upper_b: 0.5, lower_b: 0.5}, (.30, .18, .11, 1)),
          (knee.lerp(ankle, 0.55), r_knee[0] * .82, r_knee[1] * .82, {lower_b: 1.0}, (.20, .13, .09, 1)),
          (ankle, r_ank[0], r_ank[1], {lower_b: 0.4, foot_b: 0.6}, (.14, .10, .08, 1)),
          (ankle.lerp(paw_tip, 0.6), .026, .016, {foot_b: 1.0}, (.12, .09, .07, 1)),
          (paw_tip, .018, .010, {foot_b: 1.0}, (.10, .08, .06, 1)),
      ]
      b.ringmesh(limb_nodes, radial=18, ventral_flat=0.25)
      # Five nimble digging claws per paw
      for d_idx in range(-2, 3):
        claw_pos = paw_tip + Vector((d_idx * .009, -.004, -.018))
        b.ellipsoid(claw_pos, (.0035, .0045, .014), {foot_b: 1.0},
                    color=(.08, .07, .06, 1), segments=10, tilt=(0.25, d_idx * 0.08, 0))
    else:
      # Monitor lizard: Heavy sprawling humerus/femur, bowed forearm/calf, 5 recurved talons
      r_hip = (.056, .048) if not is_front else (.050, .044)
      r_knee = (.044, .038)
      r_ank = (.030, .024)
      paw_tip = ankle + Vector((s * .025, -.016, -.105))
      limb_nodes = [
          (hip, r_hip[0], r_hip[1], {upper_b: 0.85, ("Chest" if is_front else "Pelvis"): 0.15}, (.24, .27, .20, 1)),
          (hip.lerp(knee, 0.5), r_hip[0] * .95, r_hip[1] * .92, {upper_b: 1.0}, (.26, .29, .21, 1)),
          (knee, r_knee[0], r_knee[1], {upper_b: 0.5, lower_b: 0.5}, (.25, .28, .20, 1)),
          (knee.lerp(ankle, 0.5), r_knee[0] * .88, r_knee[1] * .85, {lower_b: 1.0}, (.22, .25, .18, 1)),
          (ankle, r_ank[0], r_ank[1], {lower_b: 0.4, foot_b: 0.6}, (.20, .22, .16, 1)),
          (paw_tip, .034, .014, {foot_b: 1.0}, (.18, .20, .14, 1)),
      ]
      b.ringmesh(limb_nodes, radial=20, ventral_flat=0.30)
      for d_idx in range(-2, 3):
        spread = d_idx * 0.015
        digit_tip = paw_tip + Vector((spread + s * .006, -.005, -.038 + abs(d_idx) * .005))
        b.ellipsoid(digit_tip, (.0045, .0055, .022), {foot_b: 1.0},
                    color=(.12, .11, .09, 1), segments=10, tilt=(0.35, d_idx * 0.14, 0))

  # 4. Tail Construction (Banded Erect Coati Tail vs. Laterally Compressed Varanid Keel Tail)
  tail_nodes = []
  n_tail = len(tail)
  for i, pt in enumerate(tail):
    u = i / max(1, n_tail - 1)
    t_idx = min(3, int(u * 4))
    w_tail = {f"Tail{t_idx}": 1.0}
    if species == "coati":
      # Plush cylindrical ringed tail with 7 alternating dark-brown and golden-buff bands
      rad_x = 0.036 * (1.0 - 0.52 * (u ** 1.4))
      rad_y = 0.036 * (1.0 - 0.52 * (u ** 1.4))
      band_val = math.sin(u * 7.0 * TAU)
      tcol = (.16, .11, .08, 1.0) if band_val < 0 else (.76, .58, .32, 1.0)
      tail_nodes.append((Vector(pt), rad_x, rad_y, w_tail, tcol))
    else:
      # Varanid tail: transitions from muscular base to tall laterally compressed keel (ry = 1.55 * rx)
      base_r = 0.074 * ((1.0 - u) ** 1.15) + 0.004
      compression = 1.0 + 0.58 * math.sin(min(1.0, u * 1.35) * (math.pi / 2.0))
      rad_x = base_r / compression
      rad_y = base_r * compression
      band_val = math.sin(u * 9.0 * TAU)
      tcol = (.20, .23, .17, 1.0) if band_val < 0.2 else (.56, .52, .32, 1.0)
      tail_nodes.append((Vector(pt), rad_x, rad_y, w_tail, tcol))

  if species == "coati":
    b.ringmesh(tail_nodes, radial=20, uvscale=3.0)
  else:
    b.ringmesh(tail_nodes, radial=24, uvscale=4.0, dorsal_keel=0.35, ventral_flat=0.15)

  return b.build()


def rigging(species, chain, limbs, tail):
  """Constructs the Blender Armature matching Vivarium bone naming & hierarchy."""
  arm = bpy.data.armatures.new(species.capitalize() + "Armature")
  rig = bpy.data.objects.new(species.capitalize() + "Rig", arm)
  bpy.context.collection.objects.link(rig)
  active(rig)
  bpy.ops.object.mode_set(mode="EDIT")

  eb = arm.edit_bones
  root = eb.new("Root")
  root.head = (0, 0, 0)
  root.tail = (0, .15, 0)

  def bone(name, h, t, parent):
    b = eb.new(name)
    b.head = h
    b.tail = t
    if parent and parent in eb:
      b.parent = eb[parent]
    return b

  for name, h, t, parent in chain:
    bone(name, h, t, parent)

  for side, is_front, hip, knee, ankle in limbs:
    prefix = ("Fore" if is_front else "Hind") + "_" + side
    p_name = "Chest" if is_front else "Pelvis"
    b_up = bone(prefix + "_Upper", tuple(hip), tuple(knee), p_name)
    b_lo = bone(prefix + "_Lower", tuple(knee), tuple(ankle), b_up.name)
    foot_end = ankle + Vector((0, -.015, -.08))
    bone(prefix + "_Foot", tuple(ankle), tuple(foot_end), b_lo.name)

  # 4-segment tail chain
  step = max(1, (len(tail) - 1) // 4)
  for i in range(4):
    h_pt = tail[min(len(tail) - 2, i * step)]
    t_pt = tail[min(len(tail) - 1, (i + 1) * step)]
    parent = "Pelvis" if i == 0 else f"Tail{i - 1}"
    bone(f"Tail{i}", h_pt, t_pt, parent)

  bpy.ops.object.mode_set(mode="OBJECT")
  return rig


def material(name, color, rough=.7, texture=None, normal=None):
  mat = bpy.data.materials.new(name)
  mat.use_nodes = True
  bsdf = mat.node_tree.nodes.get("Principled BSDF")
  if bsdf:
    bsdf.inputs["Base Color"].default_value = (*color[:3], 1.0)
    bsdf.inputs["Roughness"].default_value = rough
  return mat


def unify_anatomy(obj, species):
  """Applies smooth shading and recalculates outward normals."""
  active(obj)
  bpy.ops.object.mode_set(mode="EDIT")
  bm = bmesh.from_edit_mesh(obj.data)
  bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
  bmesh.update_edit_mesh(obj.data)
  bpy.ops.object.mode_set(mode="OBJECT")


def _bake_animations(rig, species):
  """Ensures required audit_assets.py clips ('idle', 'feed', 'rest', 'walk') exist."""
  rig.animation_data_create()
  for clip in ("idle", "feed", "rest", "walk"):
    act = bpy.data.actions.new(name=clip)
    rig.animation_data.action = act
    for f in (1, 16, 31):
      bpy.context.scene.frame_set(f)
      phase = (f - 1) / 30.0 * TAU
      for pb in rig.pose.bones:
        pb.rotation_mode = "XYZ"
        if clip == "walk" and ("Upper" in pb.name or "Tail" in pb.name):
          sign = 1.0 if "_L" in pb.name else -1.0
          pb.rotation_euler = (0.18 * sign * math.sin(phase), 0.0, 0.0)
        elif clip == "feed" and pb.name in ("Neck", "Head"):
          pb.rotation_euler = (-0.22 * math.sin(phase * 0.5), 0.0, 0.0)
        elif clip == "rest":
          pb.rotation_euler = (0.08, 0.0, 0.0)
        else:
          pb.rotation_euler = (0.04 * math.sin(phase), 0.0, 0.0)
        pb.keyframe_insert(data_path="rotation_euler", frame=f)


def build_quadruped(species, out=None):
  """Main entry builder for ground quadrupeds ('coati' and 'monitor')."""
  bpy.ops.wm.read_factory_settings(use_empty=True)
  bpy.context.scene.render.fps = 30
  target_folder = "woodland_omnivore" if species in ("coati", "woodland_omnivore") else "great_ground"
  species_key = "coati" if species in ("coati", "woodland_omnivore") else "monitor"
  if out is None:
    out = ROOT / "game/assets/guardians" / target_folder / "guardian.glb"
  out = Path(out)
  out.parent.mkdir(parents=True, exist_ok=True)

  cfg = config(species_key)
  base_col = (.46, .28, .16) if species_key == "coati" else (.24, .27, .20)
  mat = material(species_key.capitalize() + "Mat", base_col, rough=0.68)
  rig = rigging(species_key, cfg["chain"], cfg["limbs"], cfg["tail"])
  body = geometry(species_key, rig, mat, cfg["profile"], cfg["limbs"], cfg["tail"])
  unify_anatomy(body, species_key)
  _bake_animations(rig, species_key)

  bpy.ops.export_scene.gltf(
      filepath=str(out.resolve()),
      export_format="GLB",
      export_yup=True,
      export_skins=True,
      export_animations=True,
  )
  return body, rig


def generate(species="coati", out=None):
  return build_quadruped(species, out)


def main():
  species = "monitor" if "--monitor" in sys.argv else "coati"
  build_quadruped(species)


if __name__ == "__main__":
  main()
