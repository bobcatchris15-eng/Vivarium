"""Hollowcrown (hollowcrown_tree) botanical reference and procedural mesh visualizer.

Constructs a botanical reference model of an ancient veteran pollard oak:
  * Muscular, fluted trunk with flared root buttresses cradling an authentic vulva-shaped cleft
  * Double-lipped callus wound-wood anatomy:
      - Outer labia (labia majora / outer wound roll) converging into an overarching upper hood
        and fusing below into a raised basal fourchette / threshold lip
      - Inter-labial sulcus crease
      - Soft, undulating inner labia (labia minora / inner cambium folds) tucked inside
  * Deeply fluted, fibrous hollow log interior with vertical rot channels, horizontal decay checks,
    internal decayed heartwood ribs/septa, domed cavity ceiling, and mounded humus floor
  * Organic velvety bryophyte / moss cushions nestled into inner wall niches and crevice thresholds
  * Pollard bolling head with swollen knobby knuckles matching the scaffold limb azimuths
  * 5 primary scaffold limbs with fluted basal collars fusing smoothly into bolling knuckles
  * Forked secondary and tertiary boughs with swollen branch collars and Quercus oak foliage sprays

Supports:
  blender --background --python hollowcrown_reference.py -- --mode reference --view whole --out DIR
  blender --background --python hollowcrown_reference.py -- --mode reference --view macro --out DIR
  blender --background --python hollowcrown_reference.py -- --mode mesh --obj FILE --view whole --out DIR
  blender --background --python hollowcrown_reference.py -- --mode mesh --obj FILE --view macro --out DIR
"""

import argparse
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# Species Proportions & Botanical Parameters
# --------------------------------------------------------------------------
TOTAL_HEIGHT = 4.40
MAX_RADIUS = 1.55
BOLE_HEIGHT = TOTAL_HEIGHT * 0.64       # ~2.816 m
BASE_RADIUS = MAX_RADIUS * 0.38         # ~0.589 m

OPEN_DIR = math.pi * 0.28                # Facing front 3/4 camera (~50.4 deg)
Y_BOT = BOLE_HEIGHT * 0.06              # Lower fourchette ~0.169 m
Y_TOP = BOLE_HEIGHT * 0.68              # Upper apex commissure ~1.915 m
DOOR_MAX_HALF_ANGLE = 0.32              # Organic almond cleft (~36 deg max opening)              # Max half angle of opening cleft (~57 deg total opening)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def radial(az):
    return Vector((math.cos(az), math.sin(az), 0.0))  # Local XY (or XZ in Godot)


def radial_3d(az):
    # In Blender: Z is up, X is right, Y is fwd.
    return Vector((math.cos(az), math.sin(az), 0.0))


def _principled(mat):
    for n in mat.node_tree.nodes:
        if n.type == 'BSDF_PRINCIPLED':
            return n
    return mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')


def _set(bsdf, name, val):
    if name in bsdf.inputs:
        bsdf.inputs[name].default_value = val


def make_material(name, base_col, roughness=0.85, specular=0.15, subsurface=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (*base_col, 1.0))
    _set(bsdf, "Roughness", roughness)
    _set(bsdf, "Specular IOR Level", specular)
    if "Subsurface Weight" in bsdf.inputs and subsurface > 0:
        _set(bsdf, "Subsurface Weight", subsurface)
        if "Subsurface Radius" in bsdf.inputs:
            bsdf.inputs["Subsurface Radius"].default_value = (0.05, 0.03, 0.01)
    return mat


def bark_material():
    mat = bpy.data.materials.new("HollowBark")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.90)
    _set(bsdf, "Specular IOR Level", 0.12)
    # Texture coordinate & noise for organic bark bump
    geom = nt.nodes.new("ShaderNodeNewGeometry")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (35.0, 35.0, 4.0)
    nt.links.new(geom.outputs["Position"], mapping.inputs["Vector"])

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.0
    noise.inputs["Detail"].default_value = 8.0
    noise.inputs["Roughness"].default_value = 0.7
    nt.links.new(mapping.outputs["Vector"], noise.inputs["Vector"])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.2
    ramp.color_ramp.elements[0].color = (0.12, 0.10, 0.08, 1.0)
    ramp.color_ramp.elements[1].position = 0.8
    ramp.color_ramp.elements[1].color = (0.34, 0.30, 0.25, 1.0)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.4
    bump.inputs["Distance"].default_value = 0.05
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def decayed_heartwood_material():
    mat = bpy.data.materials.new("DecayedHeartwood")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.95)
    _set(bsdf, "Specular IOR Level", 0.08)

    geom = nt.nodes.new("ShaderNodeNewGeometry")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (35.0, 35.0, 4.0)
    nt.links.new(geom.outputs["Position"], mapping.inputs["Vector"])

    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.0
    noise.inputs["Detail"].default_value = 10.0
    nt.links.new(mapping.outputs["Vector"], noise.inputs["Vector"])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.1
    ramp.color_ramp.elements[0].color = (0.08, 0.05, 0.03, 1.0)  # Charred / dark rot
    ramp.color_ramp.elements[1].position = 0.9
    ramp.color_ramp.elements[1].color = (0.24, 0.16, 0.10, 1.0)  # Punk wood
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])

    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.5
    bump.inputs["Distance"].default_value = 0.04
    nt.links.new(noise.outputs["Fac"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def callus_outer_material():
    return make_material("CallusOuter", (0.42, 0.32, 0.22), roughness=0.82, specular=0.18)


def callus_inner_material():
    # Warm living cambium tones (rose-amber / ochre-brown) with slight translucency
    return make_material("CallusInner", (0.54, 0.38, 0.24), roughness=0.76, specular=0.22, subsurface=0.2)


def moss_material():
    mat = bpy.data.materials.new("VelvetMoss")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = _principled(mat)
    _set(bsdf, "Roughness", 0.92)
    _set(bsdf, "Specular IOR Level", 0.10)
    if "Subsurface Weight" in bsdf.inputs:
        _set(bsdf, "Subsurface Weight", 0.35)
        if "Subsurface Radius" in bsdf.inputs:
            bsdf.inputs["Subsurface Radius"].default_value = (0.08, 0.20, 0.05)

    tex_coord = nt.nodes.new("ShaderNodeTexCoord")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 32.0
    nt.links.new(tex_coord.outputs["Object"], noise.inputs["Vector"])

    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].position = 0.15
    ramp.color_ramp.elements[0].color = (0.10, 0.22, 0.09, 1.0)  # Shaded base
    ramp.color_ramp.elements[1].position = 0.85
    ramp.color_ramp.elements[1].color = (0.32, 0.72, 0.36, 1.0)  # Bioluminescent crest
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def foliage_material():
    mat = bpy.data.materials.new("OakFoliage")
    mat.use_nodes = True
    bsdf = _principled(mat)
    _set(bsdf, "Base Color", (0.18, 0.32, 0.12, 1.0))
    _set(bsdf, "Roughness", 0.48)
    _set(bsdf, "Specular IOR Level", 0.35)
    if "Subsurface Weight" in bsdf.inputs:
        _set(bsdf, "Subsurface Weight", 0.25)
        if "Subsurface Radius" in bsdf.inputs:
            bsdf.inputs["Subsurface Radius"].default_value = (0.1, 0.2, 0.05)
    return mat


def imported_mesh_material(attr_name="Color"):
    mat = bpy.data.materials.new("EngineMeshMaterial")
    mat.use_nodes = True
    nt = mat.node_tree
    nodes = nt.nodes
    links = nt.links
    nodes.clear()

    out = nodes.new("ShaderNodeOutputMaterial")

    # Vertex Color
    vcol = nodes.new("ShaderNodeAttribute")
    vcol.attribute_name = attr_name

    sep = nodes.new("ShaderNodeSeparateColor")
    links.new(vcol.outputs["Color"], sep.inputs["Color"])

    # Procedural 3D noise for wood & bark with anisotropic vertical wood grain
    geom = nodes.new("ShaderNodeNewGeometry")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (35.0, 35.0, 4.0)
    links.new(geom.outputs["Position"], mapping.inputs["Vector"])

    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 1.0
    noise.inputs["Detail"].default_value = 8.0
    noise.inputs["Roughness"].default_value = 0.65
    links.new(mapping.outputs["Vector"], noise.inputs["Vector"])

    # ColorRamp for bark base tone (weathered oak cork)
    bark_ramp = nodes.new("ShaderNodeValToRGB")
    bark_ramp.color_ramp.elements[0].position = 0.15
    bark_ramp.color_ramp.elements[0].color = (0.24, 0.20, 0.16, 1.0)
    bark_ramp.color_ramp.elements[1].position = 0.85
    bark_ramp.color_ramp.elements[1].color = (0.48, 0.42, 0.34, 1.0)
    links.new(noise.outputs["Fac"], bark_ramp.inputs["Fac"])

    bark_bump = nodes.new("ShaderNodeBump")
    bark_bump.inputs["Strength"].default_value = 0.35
    bark_bump.inputs["Distance"].default_value = 0.035
    links.new(noise.outputs["Fac"], bark_bump.inputs["Height"])

    # Callus / Cambium tone: scale linear vertex color
    callus_scale = nodes.new("ShaderNodeVectorMath")
    callus_scale.operation = 'SCALE'
    callus_scale.inputs["Scale"].default_value = 2.0
    links.new(vcol.outputs["Color"], callus_scale.inputs["Vector"])

    rg_diff = nodes.new("ShaderNodeMath")
    rg_diff.operation = 'SUBTRACT'
    links.new(sep.outputs["Red"], rg_diff.inputs[0])
    links.new(sep.outputs["Green"], rg_diff.inputs[1])

    callus_map = nodes.new("ShaderNodeMapRange")
    callus_map.inputs["From Min"].default_value = 0.02
    callus_map.inputs["From Max"].default_value = 0.10
    links.new(rg_diff.outputs["Value"], callus_map.inputs["Value"])

    wood_color_mix = nodes.new("ShaderNodeMix")
    wood_color_mix.data_type = 'RGBA'
    links.new(callus_map.outputs["Result"], wood_color_mix.inputs["Factor"])
    links.new(bark_ramp.outputs["Color"], wood_color_mix.inputs[6])
    links.new(callus_scale.outputs["Vector"], wood_color_mix.inputs[7])

    # Decayed Heartwood / Cavity Rot Tone
    rot_ramp = nodes.new("ShaderNodeValToRGB")
    rot_ramp.color_ramp.elements[0].position = 0.10
    rot_ramp.color_ramp.elements[0].color = (0.07, 0.045, 0.025, 1.0)
    rot_ramp.color_ramp.elements[1].position = 0.90
    rot_ramp.color_ramp.elements[1].color = (0.24, 0.16, 0.09, 1.0)
    links.new(noise.outputs["Fac"], rot_ramp.inputs["Fac"])

    rot_color_mult = nodes.new("ShaderNodeMix")
    rot_color_mult.data_type = 'RGBA'
    rot_color_mult.blend_type = 'MULTIPLY'
    rot_color_mult.inputs["Factor"].default_value = 0.85
    links.new(rot_ramp.outputs["Color"], rot_color_mult.inputs[6])
    links.new(callus_scale.outputs["Vector"], rot_color_mult.inputs[7])

    lum_add = nodes.new("ShaderNodeMath")
    lum_add.operation = 'ADD'
    links.new(sep.outputs["Red"], lum_add.inputs[0])
    links.new(sep.outputs["Green"], lum_add.inputs[1])

    lum = nodes.new("ShaderNodeMath")
    lum.operation = 'MULTIPLY'
    lum.inputs[1].default_value = 0.5
    links.new(lum_add.outputs["Value"], lum.inputs[0])

    is_rot = nodes.new("ShaderNodeMapRange")
    is_rot.inputs["From Min"].default_value = 0.22
    is_rot.inputs["From Max"].default_value = 0.14
    is_rot.inputs["To Min"].default_value = 0.0
    is_rot.inputs["To Max"].default_value = 1.0
    links.new(lum.outputs["Value"], is_rot.inputs["Value"])

    final_wood_color = nodes.new("ShaderNodeMix")
    final_wood_color.data_type = 'RGBA'
    links.new(is_rot.outputs["Result"], final_wood_color.inputs["Factor"])
    links.new(wood_color_mix.outputs[2], final_wood_color.inputs[6])
    links.new(rot_color_mult.outputs[2], final_wood_color.inputs[7])

    rot_rough = nodes.new("ShaderNodeMapRange")
    rot_rough.inputs["To Min"].default_value = 0.88
    rot_rough.inputs["To Max"].default_value = 0.96
    links.new(is_rot.outputs["Result"], rot_rough.inputs["Value"])

    # Wood BSDF
    bsdf_wood = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf_wood.inputs["Specular IOR Level"].default_value = 0.12
    links.new(final_wood_color.outputs[2], bsdf_wood.inputs["Base Color"])
    links.new(rot_rough.outputs["Result"], bsdf_wood.inputs["Roughness"])
    links.new(bark_bump.outputs["Normal"], bsdf_wood.inputs["Normal"])

    # Moss BSDF
    bsdf_moss = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf_moss.inputs["Roughness"].default_value = 0.94
    bsdf_moss.inputs["Specular IOR Level"].default_value = 0.08
    moss_color_scale = nodes.new("ShaderNodeVectorMath")
    moss_color_scale.operation = 'SCALE'
    moss_color_scale.inputs["Scale"].default_value = 1.4
    links.new(vcol.outputs["Color"], moss_color_scale.inputs["Vector"])
    links.new(moss_color_scale.outputs["Vector"], bsdf_moss.inputs["Base Color"])

    # Leaf BSDF: Principled + Translucent for 2-sided transmission
    bsdf_leaf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf_leaf.inputs["Base Color"].default_value = (0.18, 0.36, 0.12, 1.0)
    bsdf_leaf.inputs["Roughness"].default_value = 0.45
    bsdf_leaf.inputs["Specular IOR Level"].default_value = 0.25

    bsdf_trans = nodes.new("ShaderNodeBsdfTranslucent")
    bsdf_trans.inputs["Color"].default_value = (0.24, 0.44, 0.16, 1.0)

    mix_leaf = nodes.new("ShaderNodeMixShader")
    mix_leaf.inputs["Factor"].default_value = 0.45
    links.new(bsdf_leaf.outputs["BSDF"], mix_leaf.inputs[1])
    links.new(bsdf_trans.outputs["BSDF"], mix_leaf.inputs[2])

    # Classification factors
    moss_diff = nodes.new("ShaderNodeMath")
    moss_diff.operation = 'SUBTRACT'
    links.new(sep.outputs["Green"], moss_diff.inputs[0])
    r_mult = nodes.new("ShaderNodeMath")
    r_mult.operation = 'MULTIPLY'
    r_mult.inputs[1].default_value = 1.25
    links.new(sep.outputs["Red"], r_mult.inputs[0])
    links.new(r_mult.outputs["Value"], moss_diff.inputs[1])

    is_moss = nodes.new("ShaderNodeMapRange")
    is_moss.inputs["From Min"].default_value = 0.04
    is_moss.inputs["From Max"].default_value = 0.12
    links.new(moss_diff.outputs["Value"], is_moss.inputs["Value"])

    leaf_diff = nodes.new("ShaderNodeMath")
    leaf_diff.operation = 'SUBTRACT'
    links.new(sep.outputs["Green"], leaf_diff.inputs[0])
    r_leaf_mult = nodes.new("ShaderNodeMath")
    r_leaf_mult.operation = 'MULTIPLY'
    r_leaf_mult.inputs[1].default_value = 1.05
    links.new(sep.outputs["Red"], r_leaf_mult.inputs[0])
    links.new(r_leaf_mult.outputs["Value"], leaf_diff.inputs[1])

    is_leaf = nodes.new("ShaderNodeMapRange")
    is_leaf.inputs["From Min"].default_value = 0.01
    is_leaf.inputs["From Max"].default_value = 0.05
    links.new(leaf_diff.outputs["Value"], is_leaf.inputs["Value"])

    # Mix wood and leaf
    mix_wood_leaf = nodes.new("ShaderNodeMixShader")
    links.new(is_leaf.outputs["Result"], mix_wood_leaf.inputs["Factor"])
    links.new(bsdf_wood.outputs["BSDF"], mix_wood_leaf.inputs[1])
    links.new(mix_leaf.outputs["Shader"], mix_wood_leaf.inputs[2])

    # Mix with moss
    mix_final = nodes.new("ShaderNodeMixShader")
    links.new(is_moss.outputs["Result"], mix_final.inputs["Factor"])
    links.new(mix_wood_leaf.outputs["Shader"], mix_final.inputs[1])
    links.new(bsdf_moss.outputs["BSDF"], mix_final.inputs[2])

    links.new(mix_final.outputs["Shader"], out.inputs["Surface"])
    return mat


# --------------------------------------------------------------------------
# Procedural Reference Builder (BMesh)
# --------------------------------------------------------------------------

def build_reference_hollowcrown(rng, coll):
    """Builds a high-detail reference veteran pollard oak tree."""
    import bmesh

    # 1. Main Bole & Cavity with Vulva-Shaped Cleft
    # ----------------------------------------------------------------------
    bm = bmesh.new()

    sectors = 40
    rings = 42

    buttress_angles = [
        OPEN_DIR + 0.82,
        OPEN_DIR + math.pi * 0.52,
        OPEN_DIR + math.pi * 0.94,
        OPEN_DIR + math.pi * 1.36,
        OPEN_DIR + math.pi * 1.66,
        OPEN_DIR + 2.0 * math.pi - 0.82
    ]

    # Bolling knuckles matching 5 scaffold limbs
    BASE_KNUCKLE_AZ = OPEN_DIR + math.pi * 0.20
    knuckle_angles = [
        BASE_KNUCKLE_AZ + 2.0 * math.pi * k / 5.0 for k in range(5)
    ]

    def mean_outer_radius(t):
        base_flare = 1.0 + 0.35 * math.pow(max(0.0, 1.0 - t / 0.28), 2.2)
        mid_column = 0.88 + 0.08 * (0.0 if t < 0.72 else (1.0 if t > 1.0 else (t - 0.72) / 0.28))
        return BASE_RADIUS * (base_flare * 0.88 + mid_column * 0.12)

    def outer_radius(t, th):
        r = mean_outer_radius(t)
        r *= 1.0 + 0.06 * math.sin(8.0 * th + 0.6 * t) * (0.35 + 0.65 * (1.0 - 0.35 * t))
        r *= 1.0 + 0.02 * math.sin(16.0 * th)
        if t > 0.65:
            u_boll = (t - 0.65) / 0.35
            u_boll = u_boll * u_boll * (3.0 - 2.0 * u_boll)
            k_sum = 0.0
            for ka in knuckle_angles:
                d = abs(th - ka)
                if d > math.pi:
                    d = 2.0 * math.pi - d
                k_sum += math.pow(max(0.0, math.cos(d * 2.5)), 2.8)
            r += BASE_RADIUS * (0.14 * u_boll + 0.34 * math.pow(u_boll, 1.5) * k_sum)

        if t < 0.38:
            decay = math.pow(1.0 - t / 0.38, 2.2)
            b_sum = 0.0
            for b_idx, ba in enumerate(buttress_angles):
                diff = abs(th - ba)
                if diff > math.pi:
                    diff = 2.0 * math.pi - diff
                weight = 0.55 if (b_idx == 0 or b_idx == len(buttress_angles) - 1) else 0.42
                b_sum += math.pow(max(0.0, math.cos(diff * 1.9)), 3.0) * weight
            r += BASE_RADIUS * b_sum * decay
        return r

    def inner_radius(t, th):
        r_mean = max(0.18, mean_outer_radius(t) - BASE_RADIUS * 0.25)
        heartwood_rids = 0.09 * math.pow(abs(math.sin(14.0 * th + 0.5 * math.sin(4.0 * t))), 0.45) \
                         + 0.04 * math.cos(22.0 * th + 1.2 * math.sin(6.0 * t))
        rot_flute = 0.10 * math.cos(6.0 * th + 0.4) \
                    + 0.05 * math.sin(11.0 * th + 1.8 * t) \
                    + 0.02 * math.sin(31.0 * th + 4.0 * t)
        checks = 0.025 * math.sin(t * 26.0 + 2.0 * math.cos(4.0 * th))
        basal_rot = 0.10 * math.pow(max(0.0, 1.0 - t / 0.35), 1.8) * (1.0 + 0.4 * math.cos(3.0 * th))
        return r_mean * (1.0 + rot_flute + heartwood_rids + checks + basal_rot)

    def vulva_half_angle(y):
        if y <= Y_BOT or y >= Y_TOP:
            return 0.0
        u = (y - Y_BOT) / (Y_TOP - Y_BOT)
        return DOOR_MAX_HALF_ANGLE * math.pow(math.sin(math.pi * u), 1.35) * (1.0 + 0.28 * math.sin(math.pi * (1.0 - u)) - 0.12 * math.sin(2.0 * math.pi * u))

    def trunk_center(t):
        # Natural veteran oak lean
        lean_x = 0.05 * BASE_RADIUS * math.sin(math.pi * t * 0.85)
        lean_y = -0.04 * BASE_RADIUS * math.sin(math.pi * t * 0.70)
        return Vector((lean_x, lean_y, BOLE_HEIGHT * t))

    # Build outer and inner grids
    outer_verts = []
    inner_verts = []

    for i in range(rings + 1):
        t = i / rings
        y = BOLE_HEIGHT * t
        center = trunk_center(t)
        gap = vulva_half_angle(y)
        is_open = gap > 1e-4

        start_angle = OPEN_DIR + gap
        sweep_angle = math.tau - 2.0 * gap

        o_row = []
        i_row = []

        for j in range(sectors + 1):
            u_sec = j / sectors
            th = start_angle + sweep_angle * u_sec
            rad_dir = radial_3d(th)

            r_out = outer_radius(t, th)
            r_in = inner_radius(t, th)

            p_out = center + rad_dir * r_out
            p_in = center + rad_dir * r_in

            vo = bm.verts.new(p_out)
            vi = bm.verts.new(p_in)
            o_row.append(vo)
            i_row.append(vi)

        outer_verts.append(o_row)
        inner_verts.append(i_row)

    bm.verts.ensure_lookup_table()

    # Outer surface faces
    for i in range(rings):
        for j in range(sectors):
            v00 = outer_verts[i][j]
            v01 = outer_verts[i][j + 1]
            v10 = outer_verts[i + 1][j]
            v11 = outer_verts[i + 1][j + 1]
            bm.faces.new((v00, v10, v11, v01))

    # Inner surface faces (normals point inward into cavity)
    for i in range(rings):
        for j in range(sectors):
            v00 = inner_verts[i][j]
            v01 = inner_verts[i][j + 1]
            v10 = inner_verts[i + 1][j]
            v11 = inner_verts[i + 1][j + 1]
            bm.faces.new((v00, v01, v11, v10))

    # Rolled bolling top lip: curve over the top from outer to inner
    top_rings = 4
    bolling_top_verts = [outer_verts[rings]]
    for tr in range(1, top_rings):
        frac = tr / top_rings
        top_row = []
        for j in range(sectors + 1):
            vo = outer_verts[rings][j].co
            vi = inner_verts[rings][j].co
            # Dome slightly upward
            h_arch = 0.045 * math.sin(math.pi * frac)
            p_mid = vo.lerp(vi, frac) + Vector((0, 0, h_arch))
            top_row.append(bm.verts.new(p_mid))
        bolling_top_verts.append(top_row)
    bolling_top_verts.append(inner_verts[rings])

    for tr in range(top_rings):
        for j in range(sectors):
            v00 = bolling_top_verts[tr][j]
            v01 = bolling_top_verts[tr][j + 1]
            v10 = bolling_top_verts[tr + 1][j]
            v11 = bolling_top_verts[tr + 1][j + 1]
            bm.faces.new((v00, v01, v11, v10))

    # 2. Organic Double-Lipped Vulva Callus Border
    # ----------------------------------------------------------------------
    # Sculpt double-lipped callus with 8 cross-sectional stations:
    # Outer labia rolls, sulcus crease, inner labia folds, upper hood, lower fourchette
    c_sub = 8

    # Find rings that intersect the opening
    open_ring_indices = [
        i for i in range(rings + 1)
        if vulva_half_angle(BOLE_HEIGHT * (i / rings)) > 1e-4
    ]

    if open_ring_indices:
        i_min = open_ring_indices[0]
        i_max = open_ring_indices[-1]

        left_callus_grid = []
        right_callus_grid = []

        for idx in range(i_min, i_max + 1):
            y = BOLE_HEIGHT * (idx / rings)
            u_cleft = (y - Y_BOT) / (Y_TOP - Y_BOT)
            gap = vulva_half_angle(y)

            # Left side (j = 0)
            p_out_l = outer_verts[idx][0].co
            p_in_l = inner_verts[idx][0].co
            wall_t_l = (p_out_l - p_in_l).length

            in_dir_l = radial_3d(OPEN_DIR + gap - math.pi * 0.5)  # Tangent pointing inward into cleft
            out_norm_l = radial_3d(OPEN_DIR + gap)

            l_row = [outer_verts[idx][0]]
            for s in range(1, c_sub):
                frac = s / c_sub  # 0 to 1 across callus profile
                p_base = p_out_l.lerp(p_in_l, frac)

                # Double-labia profile matching C# callus cross-section
                majora_rad = 0.32 * math.sin(math.pi * (frac / 0.45)) if frac <= 0.45 else 0.0
                majora_in = 0.28 * math.sin(math.pi * (frac / 0.45)) if frac <= 0.45 else 0.0

                u_minora = (frac - 0.45) / 0.47
                inner_wave = math.pow(math.sin(math.pi * u_cleft), 0.75) * (0.75 + 0.35 * math.sin(math.pi * math.pow(u_cleft, 0.7)) + 0.08 * math.sin(5.0 * u_cleft))
                minora_rad = 0.22 * math.sin(math.pi * u_minora) * inner_wave if (0.45 < frac <= 0.92) else 0.0
                minora_in = 0.38 * math.sin(math.pi * u_minora) * inner_wave if (0.45 < frac <= 0.92) else 0.0

                hood_swell = 0.28 * math.pow((u_cleft - 0.75) / 0.25, 1.8) * math.sin(math.pi * frac) if u_cleft > 0.75 else 0.0
                hood_peak = 0.16 * math.sin(math.pi * (u_cleft - 0.82) / 0.14) * math.exp(-math.pow((frac - 0.50) / 0.18, 2.0)) if (0.82 <= u_cleft <= 0.96) else 0.0

                gap_scale = min(1.0, gap / 0.16) * math.sin(math.pi * u_cleft)
                rad_swell_l = (majora_rad + minora_rad + hood_swell) * wall_t_l
                cleft_bulge_l = ((majora_in + minora_in) * gap_scale + hood_peak * gap_scale) * wall_t_l

                p_callus = p_base + out_norm_l * rad_swell_l + in_dir_l * cleft_bulge_l
                l_row.append(bm.verts.new(p_callus))
            l_row.append(inner_verts[idx][0])
            left_callus_grid.append(l_row)

            # Right side (j = sectors)
            p_out_r = outer_verts[idx][sectors].co
            p_in_r = inner_verts[idx][sectors].co
            wall_t_r = (p_out_r - p_in_r).length

            in_dir_r = radial_3d(OPEN_DIR - gap + math.pi * 0.5)
            out_norm_r = radial_3d(OPEN_DIR - gap)

            r_row = [outer_verts[idx][sectors]]
            for s in range(1, c_sub):
                frac = s / c_sub
                p_base = p_out_r.lerp(p_in_r, frac)

                majora_rad = 0.32 * math.sin(math.pi * (frac / 0.45)) if frac <= 0.45 else 0.0
                majora_in = 0.28 * math.sin(math.pi * (frac / 0.45)) if frac <= 0.45 else 0.0

                u_minora = (frac - 0.45) / 0.47
                inner_wave = math.pow(math.sin(math.pi * u_cleft), 0.75) * (0.75 + 0.35 * math.sin(math.pi * math.pow(u_cleft, 0.7)) + 0.08 * math.sin(5.0 * u_cleft))
                minora_rad = 0.22 * math.sin(math.pi * u_minora) * inner_wave if (0.45 < frac <= 0.92) else 0.0
                minora_in = 0.38 * math.sin(math.pi * u_minora) * inner_wave if (0.45 < frac <= 0.92) else 0.0

                hood_swell = 0.28 * math.pow((u_cleft - 0.75) / 0.25, 1.8) * math.sin(math.pi * frac) if u_cleft > 0.75 else 0.0
                hood_peak = 0.16 * math.sin(math.pi * (u_cleft - 0.82) / 0.14) * math.exp(-math.pow((frac - 0.50) / 0.18, 2.0)) if (0.82 <= u_cleft <= 0.96) else 0.0

                gap_scale = min(1.0, gap / 0.16) * math.sin(math.pi * u_cleft)
                rad_swell_r = (majora_rad + minora_rad * 0.94 + hood_swell) * wall_t_r
                cleft_bulge_r = ((majora_in + minora_in * 0.94) * gap_scale + hood_peak * gap_scale) * wall_t_r

                p_callus = p_base + out_norm_r * rad_swell_r + in_dir_r * cleft_bulge_r
                r_row.append(bm.verts.new(p_callus))
            r_row.append(inner_verts[idx][sectors])
            right_callus_grid.append(r_row)

        # Skin left callus
        for r_idx in range(len(left_callus_grid) - 1):
            for s in range(c_sub):
                v00 = left_callus_grid[r_idx][s]
                v01 = left_callus_grid[r_idx][s + 1]
                v10 = left_callus_grid[r_idx + 1][s]
                v11 = left_callus_grid[r_idx + 1][s + 1]
                bm.faces.new((v00, v10, v11, v01))

        # Skin right callus
        for r_idx in range(len(right_callus_grid) - 1):
            for s in range(c_sub):
                v00 = right_callus_grid[r_idx][s]
                v01 = right_callus_grid[r_idx][s + 1]
                v10 = right_callus_grid[r_idx + 1][s]
                v11 = right_callus_grid[r_idx + 1][s + 1]
                bm.faces.new((v00, v01, v11, v10))

        # Seal upper commissure hood apex where left and right meet
        top_l = left_callus_grid[-1]
        top_r = right_callus_grid[-1]
        for s in range(c_sub):
            bm.faces.new((top_l[s], top_l[s + 1], top_r[s + 1], top_r[s]))

        # Seal lower fourchette threshold cleft
        bot_l = left_callus_grid[0]
        bot_r = right_callus_grid[0]
        for s in range(c_sub):
            bm.faces.new((bot_l[s], bot_r[s], bot_r[s + 1], bot_l[s + 1]))

    # 3. Cavity Humus Mound & Rotten Wood Floor
    # ----------------------------------------------------------------------
    floor_rings = 6
    floor_sectors = sectors
    c_floor = trunk_center(0.0) + Vector((0, 0, 0.01))
    floor_verts = []

    for fr in range(floor_rings + 1):
        frac = fr / floor_rings
        row = []
        for fs in range(floor_sectors + 1):
            u_sec = fs / floor_sectors
            th = OPEN_DIR + math.tau * u_sec
            rad_dir = radial_3d(th)
            r_cur = inner_radius(0.0, th) * frac
            # Organic mounded basin
            dish = 0.025 * (frac * frac) - 0.015 * math.sin(frac * math.pi)
            p = c_floor + rad_dir * r_cur + Vector((0, 0, dish))
            row.append(bm.verts.new(p))
        floor_verts.append(row)

    for fr in range(floor_rings):
        for fs in range(floor_sectors):
            v00 = floor_verts[fr][fs]
            v01 = floor_verts[fr][fs + 1]
            v10 = floor_verts[fr + 1][fs]
            v11 = floor_verts[fr + 1][fs + 1]
            bm.faces.new((v00, v01, v11, v10))

    # Connect floor outer ring to bole bottom inner vertices
    for fs in range(floor_sectors):
        bm.faces.new((floor_verts[floor_rings][fs], floor_verts[floor_rings][fs + 1],
                      inner_verts[0][fs + 1], inner_verts[0][fs]))

    # 4. Internal Decayed Heartwood Ribs & Splintered Septa
    # ----------------------------------------------------------------------
    num_ribs = 6
    for rib in range(num_ribs):
        rib_az = OPEN_DIR + math.pi + (rib - num_ribs / 2.0 + 0.5) * (math.pi * 0.95 / num_ribs)
        rib_height = BOLE_HEIGHT * (0.45 + 0.40 * rng.random())
        rib_stations = 12

        rib_v_out = []
        rib_v_in = []
        for st in range(rib_stations):
            st_t = st / (rib_stations - 1)
            st_y = rib_height * st_t
            t_trunk = st_y / BOLE_HEIGHT

            c_t = trunk_center(t_trunk)
            th_wobble = rib_az + 0.08 * math.sin(st_t * 6.0)
            rad_d = radial_3d(th_wobble)
            in_d = -rad_d

            r_w = inner_radius(t_trunk, th_wobble)
            p_wall = c_t + rad_d * r_w
            # Splintered rib reaches into the cavity
            rib_protrusion = BASE_RADIUS * 0.12 * math.sin(st_t * math.pi) * (1.0 + 0.3 * math.sin(st_t * 14.0))
            p_ridge = p_wall + in_d * rib_protrusion

            rib_v_out.append(bm.verts.new(p_wall))
            rib_v_in.append(bm.verts.new(p_ridge))

        for st in range(rib_stations - 1):
            bm.faces.new((rib_v_out[st], rib_v_in[st], rib_v_in[st + 1], rib_v_out[st + 1]))

    # Finalize BMesh and create trunk object
    bm.normal_update()
    for f in bm.faces:
        f.smooth = True

    mesh = bpy.data.meshes.new("HollowcrownTrunk")
    bm.to_mesh(mesh)
    bm.free()

    trunk_obj = bpy.data.objects.new("HollowcrownTrunk", mesh)
    coll.objects.link(trunk_obj)
    trunk_obj.data.materials.append(bark_material())

    # 5. Velvet Bryophyte / Moss Cushions
    # ----------------------------------------------------------------------
    num_cushions = 10
    for c in range(num_cushions):
        c_th = OPEN_DIR + math.pi + (c - num_cushions / 2.0 + 0.5) * (math.pi * 0.85 / num_cushions)
        c_t = 0.05 + 0.42 * rng.random()
        r_w = inner_radius(c_t, c_th) - 0.003
        c_center = trunk_center(c_t) + radial_3d(c_th) * r_w

        c_norm = (-radial_3d(c_th) + Vector((0, 0, 0.15))).normalized()
        c_tan = c_norm.cross(Vector((0, 0, 1))).normalized()
        c_up = c_tan.cross(c_norm).normalized()

        pad_bm = bmesh.new()
        p_rows, p_cols = 4, 6
        rx = BASE_RADIUS * (0.08 + 0.04 * rng.random())
        ry = BASE_RADIUS * (0.06 + 0.03 * rng.random())
        h_bulge = BASE_RADIUS * 0.035

        p_grid = []
        for r in range(p_rows + 1):
            vr = (r / p_rows) * 2.0 - 1.0
            row = []
            for col in range(p_cols + 1):
                vc = (col / p_cols) * 2.0 - 1.0
                dist_sq = vr * vr + vc * vc
                dome = max(0.0, 1.0 - dist_sq)
                bulge = math.pow(dome, 0.75) * h_bulge
                p = c_center + c_tan * (vc * rx) + c_up * (vr * ry) + c_norm * bulge
                row.append(pad_bm.verts.new(p))
            p_grid.append(row)

        for r in range(p_rows):
            for col in range(p_cols):
                pad_bm.faces.new((p_grid[r][col], p_grid[r][col + 1],
                                  p_grid[r + 1][col + 1], p_grid[r + 1][col]))

        pad_bm.normal_update()
        for f in pad_bm.faces:
            f.smooth = True
        m_pad = bpy.data.meshes.new(f"MossPad_{c}")
        pad_bm.to_mesh(m_pad)
        pad_bm.free()

        pad_obj = bpy.data.objects.new(f"MossPad_{c}", m_pad)
        coll.objects.link(pad_obj)
        pad_obj.data.materials.append(moss_material())

    # 6. Pollard Scaffold Limbs, Collared Branching & Oak Foliage
    # ----------------------------------------------------------------------
    num_limbs = 5
    for k in range(num_limbs):
        l_az = knuckle_angles[k]
        outward = radial_3d(l_az)
        ortho = outward.cross(Vector((0, 0, 1))).normalized()

        # Limb starts nested inside bolling knuckle wall
        knuckle_r = (outer_radius(0.96, l_az) + inner_radius(0.96, l_az)) * 0.50
        p_root = trunk_center(0.94) + outward * knuckle_r - Vector((0, 0, BOLE_HEIGHT * 0.06))
        limb_reach = 1.45 * (0.92 + 0.16 * rng.random())
        limb_rise = 1.10 * (0.90 + 0.20 * rng.random())
        p_end = p_root + outward * limb_reach + ortho * (rng.random() * 0.3 - 0.15) + Vector((0, 0, limb_rise))

        # Steep launch upward, spreading arch outward
        start_tan = (outward * 0.30 + Vector((0, 0, 0.95))).normalized()
        c0 = p_root + start_tan * (limb_reach * 0.45)
        end_tan = (outward * 0.85 + Vector((0, 0, 0.35))).normalized()
        c1 = p_end - end_tan * (limb_reach * 0.38)

        limb_bm = bmesh.new()
        stations = 12
        sides = 10
        limb_rings = []

        base_r = BASE_RADIUS * 0.23
        tip_r = base_r * 0.34

        for s in range(stations):
            t = s / (stations - 1)
            u = 1.0 - t
            # Bézier position
            p_c = (p_root * (u * u * u) +
                   c0 * (3.0 * u * u * t) +
                   c1 * (3.0 * u * t * t) +
                   p_end * (t * t * t))

            # Modest basal collar flare that stays fully nested inside the knuckle
            collar_flare = 1.0 + 0.22 * math.pow(max(0.0, 1.0 - t / 0.25), 1.8)
            r_cur = (base_r * (1.0 - t * 0.66)) * collar_flare

            # Parallel transport / tangent frame
            dt = 0.01
            t_next = min(1.0, t + dt)
            t_prev = max(0.0, t - dt)
            u_n = 1.0 - t_next
            u_p = 1.0 - t_prev
            p_next = p_root * (u_n**3) + c0 * (3 * u_n**2 * t_next) + c1 * (3 * u_n * t_next**2) + p_end * (t_next**3)
            p_prev = p_root * (u_p**3) + c0 * (3 * u_p**2 * t_prev) + c1 * (3 * u_p * t_prev**2) + p_end * (t_prev**3)
            tang = (p_next - p_prev).normalized()

            side_v = tang.cross(Vector((0, 0, 1)))
            if side_v.length < 1e-4:
                side_v = tang.cross(Vector((1, 0, 0)))
            side_v.normalize()
            up_v = side_v.cross(tang).normalized()

            ring = []
            for sd in range(sides):
                a = math.tau * sd / sides
                flute = 1.0 + 0.08 * math.cos(4.0 * a)
                pos = p_c + side_v * (math.cos(a) * r_cur * flute) + up_v * (math.sin(a) * r_cur * flute)
                ring.append(limb_bm.verts.new(pos))
            limb_rings.append(ring)

        for s in range(stations - 1):
            for sd in range(sides):
                next_sd = (sd + 1) % sides
                limb_bm.faces.new((limb_rings[s][sd], limb_rings[s + 1][sd],
                                   limb_rings[s + 1][next_sd], limb_rings[s][next_sd]))

        limb_bm.normal_update()
        for f in limb_bm.faces:
            f.smooth = True

        m_limb = bpy.data.meshes.new(f"ScaffoldLimb_{k}")
        limb_bm.to_mesh(m_limb)
        limb_bm.free()

        limb_obj = bpy.data.objects.new(f"ScaffoldLimb_{k}", m_limb)
        coll.objects.link(limb_obj)
        limb_obj.data.materials.append(bark_material())

        # Secondary forking boughs for reference tree
        for b_idx in range(2):
            b_frac = 0.45 if b_idx == 0 else 0.70
            b_root = p_root.lerp(p_end, b_frac)
            b_sign = 1.0 if b_idx == 0 else -1.0
            b_az = l_az + b_sign * 0.55
            b_out = radial_3d(b_az)
            b_reach = 0.75 * (0.85 + 0.3 * rng.random())
            b_rise = 0.45 * (0.80 + 0.4 * rng.random())
            b_end = b_root + b_out * b_reach + Vector((0, 0, b_rise))

            b_bm = bmesh.new()
            b_stations = 8
            b_sides = 8
            b_rings = []
            for bs in range(b_stations):
                bt = bs / (b_stations - 1)
                bp = b_root.lerp(b_end, bt) + Vector((0, 0, math.sin(bt * math.pi) * 0.12))
                br = (base_r * 0.55 * (1.0 - bt * 0.55)) * (1.0 + 0.5 * pow(max(0.0, 1.0 - bt / 0.3), 2.0))
                b_ring = []
                for bsd in range(b_sides):
                    ba = math.tau * bsd / b_sides
                    b_ring.append(b_bm.verts.new(bp + ortho * (math.cos(ba) * br) + Vector((0, 0, math.sin(ba) * br))))
                b_rings.append(b_ring)

            for bs in range(b_stations - 1):
                for bsd in range(b_sides):
                    next_bsd = (bsd + 1) % b_sides
                    b_bm.faces.new((b_rings[bs][bsd], b_rings[bs + 1][bsd], b_rings[bs + 1][next_bsd], b_rings[bs][next_bsd]))

            b_bm.normal_update()
            for f in b_bm.faces: f.smooth = True
            m_bough = bpy.data.meshes.new(f"Bough_{k}_{b_idx}")
            b_bm.to_mesh(m_bough)
            b_bm.free()
            b_obj = bpy.data.objects.new(f"Bough_{k}_{b_idx}", m_bough)
            coll.objects.link(b_obj)
            b_obj.data.materials.append(bark_material())

            # Leaf spray on secondary bough
            fol_bm = bmesh.new()
            for l in range(24):
                l_dir = (Vector((rng.random() - 0.5, rng.random() - 0.5, rng.random() * 0.6 + 0.2))).normalized()
                l_side = l_dir.cross(Vector((0, 0, 1))).normalized()
                l_len = 0.28 * (0.85 + 0.3 * rng.random())
                l_wid = l_len * 0.52
                p0 = fol_bm.verts.new(b_end)
                p_mid_l = fol_bm.verts.new(b_end + l_dir * (l_len * 0.5) - l_side * (l_wid * 0.5))
                p_mid_r = fol_bm.verts.new(b_end + l_dir * (l_len * 0.5) + l_side * (l_wid * 0.5))
                p_tip = fol_bm.verts.new(b_end + l_dir * l_len + Vector((0, 0, -0.02)))
                fol_bm.faces.new((p0, p_mid_l, p_tip))
                fol_bm.faces.new((p0, p_tip, p_mid_r))

            fol_bm.normal_update()
            m_fol = bpy.data.meshes.new(f"FoliageBough_{k}_{b_idx}")
            fol_bm.to_mesh(m_fol)
            fol_bm.free()
            fol_obj = bpy.data.objects.new(f"FoliageBough_{k}_{b_idx}", m_fol)
            coll.objects.link(fol_obj)
            fol_obj.data.materials.append(foliage_material())

        # Foliage spray clusters at limb end and fork unions
        for f_idx, f_pos in enumerate([p_end, p_root.lerp(p_end, 0.65), p_root + start_tan * 0.35]):
            num_leaves = 32 if f_idx == 0 else 18
            fol_bm = bmesh.new()
            for l in range(num_leaves):
                l_dir = (Vector((rng.random() - 0.5, rng.random() - 0.5, rng.random() * 0.6 + 0.2))).normalized()
                l_side = l_dir.cross(Vector((0, 0, 1))).normalized()
                l_len = 0.28 * (0.85 + 0.3 * rng.random())
                l_wid = l_len * 0.52

                p0 = fol_bm.verts.new(f_pos)
                p_mid_l = fol_bm.verts.new(f_pos + l_dir * (l_len * 0.5) - l_side * (l_wid * 0.5))
                p_mid_r = fol_bm.verts.new(f_pos + l_dir * (l_len * 0.5) + l_side * (l_wid * 0.5))
                p_tip = fol_bm.verts.new(f_pos + l_dir * l_len + Vector((0, 0, -0.02)))
                fol_bm.faces.new((p0, p_mid_l, p_tip))
                fol_bm.faces.new((p0, p_tip, p_mid_r))

            fol_bm.normal_update()
            m_fol = bpy.data.meshes.new(f"Foliage_{k}_{f_idx}")
            fol_bm.to_mesh(m_fol)
            fol_bm.free()

            fol_obj = bpy.data.objects.new(f"Foliage_{k}_{f_idx}", m_fol)
            coll.objects.link(fol_obj)
            fol_obj.data.materials.append(foliage_material())

    # 7. Muscular Splayed Ground Surface Roots
    # ----------------------------------------------------------------------
    num_roots = 6
    for r_idx in range(num_roots):
        r_az = buttress_angles[r_idx]
        r_out = radial_3d(r_az)
        r_ortho = r_out.cross(Vector((0, 0, 1))).normalized()

        # Launch deep inside trunk buttress core
        y_launch = BOLE_HEIGHT * 0.12
        r_launch = outer_radius(0.12, r_az) * 0.60
        p_root_launch = trunk_center(0.12) + r_out * r_launch + Vector((0, 0, y_launch - BOLE_HEIGHT * 0.12))

        # Toe hugs the ground at -0.01
        reach = (1.50 if (r_idx == 0 or r_idx == 5) else 1.30) * BASE_RADIUS * (0.95 + 0.1 * rng.random())
        p_root_toe = trunk_center(0.0) + r_out * (outer_radius(0.0, r_az) * 1.30 + reach) + Vector((0, 0, -0.01))

        # Downward tangent heading inside buttress flute
        c0_root = p_root_launch + Vector((0, 0, -BOLE_HEIGHT * 0.08)) + r_out * (reach * 0.25)
        c1_root = p_root_toe - r_out * (reach * 0.40) + Vector((0, 0, 0.04))

        root_bm = bmesh.new()
        root_stations = 10
        root_sides = 8
        root_rings = []
        base_rr = BASE_RADIUS * 0.18
        tip_rr = base_rr * 0.30

        for rs in range(root_stations):
            rt = rs / (root_stations - 1)
            ru = 1.0 - rt
            rp = p_root_launch * (ru**3) + c0_root * (3 * ru**2 * rt) + c1_root * (3 * ru * rt**2) + p_root_toe * (rt**3)
            # Buttress flare decay
            rf = 1.0 + 0.30 * math.pow(max(0.0, 1.0 - rt / 0.35), 1.6)
            rr = (base_rr * (1.0 - rt * 0.70)) * rf

            dt = 0.01
            rt_next = min(1.0, rt + dt)
            rt_prev = max(0.0, rt - dt)
            ru_n = 1.0 - rt_next
            ru_p = 1.0 - rt_prev
            p_n = p_root_launch * (ru_n**3) + c0_root * (3 * ru_n**2 * rt_next) + c1_root * (3 * ru_n * rt_next**2) + p_root_toe * (rt_next**3)
            p_p = p_root_launch * (ru_p**3) + c0_root * (3 * ru_p**2 * rt_prev) + c1_root * (3 * ru_p * rt_prev**2) + p_root_toe * (rt_prev**3)
            rtang = (p_n - p_p).normalized()

            rside = rtang.cross(Vector((0, 0, 1)))
            if rside.length < 1e-4:
                rside = rtang.cross(Vector((1, 0, 0)))
            rside.normalize()
            rup = rside.cross(rtang).normalized()

            rring = []
            for rsd in range(root_sides):
                ra = math.tau * rsd / root_sides
                pos = rp + rside * (math.cos(ra) * rr) + rup * (math.sin(ra) * rr * 0.75)
                rring.append(root_bm.verts.new(pos))
            root_rings.append(rring)

        for rs in range(root_stations - 1):
            for rsd in range(root_sides):
                next_rsd = (rsd + 1) % root_sides
                root_bm.faces.new((root_rings[rs][rsd], root_rings[rs + 1][rsd],
                                   root_rings[rs + 1][next_rsd], root_rings[rs][next_rsd]))

        root_bm.normal_update()
        for f in root_bm.faces: f.smooth = True
        m_root = bpy.data.meshes.new(f"SurfaceRoot_{r_idx}")
        root_bm.to_mesh(m_root)
        root_bm.free()
        root_obj = bpy.data.objects.new(f"SurfaceRoot_{r_idx}", m_root)
        coll.objects.link(root_obj)
        root_obj.data.materials.append(bark_material())


# --------------------------------------------------------------------------
# Mesh Import from C# Procedural OBJ
# --------------------------------------------------------------------------

def build_from_obj(obj_path, coll):
    """Imports the OBJ exported from the C# procedural mesh generator."""
    if not os.path.exists(obj_path):
        raise FileNotFoundError(f"OBJ file not found: {obj_path}")

    # Blender 5.2 uses wm.obj_import
    bpy.ops.wm.obj_import(filepath=obj_path)

    imported = [o for o in bpy.context.selected_objects if o.type == 'MESH']
    mat = imported_mesh_material("Color")

    # Transform from engine unit frame (Y up, scaled by 1/r, 1/h, 1/r) to Blender world frame (Z up, metres)
    M = Matrix((
        (1.55, 0.0, 0.0, 0.0),
        (0.0, 0.0, 1.55, 0.0),
        (0.0, 4.4, 0.0, 0.0),
        (0.0, 0.0, 0.0, 1.0),
    ))

    for o in imported:
        o.matrix_world = M
        o.data.materials.clear()
        for poly in o.data.polygons:
            poly.use_smooth = True
        o.data.materials.append(mat)
        o.data.update()
    return imported


# --------------------------------------------------------------------------
# Lighting, Camera & Scene Setup
# --------------------------------------------------------------------------

def add_ground(radius, coll):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=96, radius=radius, depth=0.03, location=(0, 0, -0.015)
    )
    ground = bpy.context.active_object
    mat = make_material("WoodlandSoil", (0.08, 0.06, 0.05), roughness=0.96, specular=0.06)
    ground.data.materials.append(mat)
    return ground


def setup_camera_and_lighting(view_mode="whole"):
    scene = bpy.context.scene

    cam_data = bpy.data.cameras.new("Camera")
    cam = bpy.data.objects.new("Camera", cam_data)
    scene.collection.objects.link(cam)

    if view_mode == "macro":
        # Macro close-up framing of the vulva-shaped cleft and interior cavity
        cam_data.type = 'PERSP'
        cam_data.lens = 55.0  # Portrait / macro focal length
        # Opening is centered at OPEN_DIR (~50.4 deg) and height y ~ 1.0 m
        target = Vector((
            math.cos(OPEN_DIR) * (BASE_RADIUS * 0.35),
            math.sin(OPEN_DIR) * (BASE_RADIUS * 0.35),
            BOLE_HEIGHT * 0.38
        ))
        # Camera placed ~2.1m away looking directly into cleft
        cam_dist = 2.15
        cam_elev = math.radians(14.0)
        cam_az = OPEN_DIR - math.radians(8.0)  # Slight angle to catch callus relief & interior depth
        cam_offset = Vector((
            math.cos(cam_elev) * math.cos(cam_az),
            math.cos(cam_elev) * math.sin(cam_az),
            math.sin(cam_elev)
        )) * cam_dist
        cam.location = target + cam_offset
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    else:
        # Full tree 3/4 framing (canopy dome, muscular trunk, root buttresses)
        cam_data.type = 'PERSP'
        cam_data.lens = 42.0
        target = Vector((0.0, 0.0, TOTAL_HEIGHT * 0.44))
        cam_dist = 6.4
        cam_elev = math.radians(15.0)
        cam_az = OPEN_DIR - math.radians(18.0)
        cam_offset = Vector((
            math.cos(cam_elev) * math.cos(cam_az),
            math.cos(cam_elev) * math.sin(cam_az),
            math.sin(cam_elev)
        )) * cam_dist
        cam.location = target + cam_offset
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()

    scene.camera = cam

    # Sun key light positioned in front-left of the cleft looking directly at the cleft
    sun_data = bpy.data.lights.new("Sun", 'SUN')
    sun_data.energy = 3.8
    sun_data.angle = math.radians(4.0)
    sun_data.color = (1.0, 0.96, 0.90)
    sun = bpy.data.objects.new("Sun", sun_data)
    sun_az = OPEN_DIR - math.radians(28.0)
    sun_pos = target + Vector((math.cos(sun_az) * 6.0, math.sin(sun_az) * 6.0, 5.2))
    sun_dir = (target - sun_pos).normalized()
    sun.rotation_euler = sun_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(sun)

    # Cool sky fill light from opposite flank
    sky_data = bpy.data.lights.new("SkyFill", 'AREA')
    sky_data.energy = 45.0
    sky_data.size = 4.0
    sky_data.color = (0.76, 0.84, 0.98)
    sky = bpy.data.objects.new("SkyFill", sky_data)
    fill_az = OPEN_DIR + math.radians(75.0)
    sky.location = target + Vector((math.cos(fill_az) * 3.5, math.sin(fill_az) * 3.5, 3.2))
    sky_dir = (target - sky.location).normalized()
    sky.rotation_euler = sky_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(sky)

    # Cavity interior bounce fill (placed inside the cavern chamber to illuminate punk wood and moss)
    cav_data = bpy.data.lights.new("CavityBounce", 'POINT')
    cav_data.energy = 85.0
    cav_data.shadow_soft_size = 0.45
    cav_data.color = (1.0, 0.90, 0.75)
    cav_light = bpy.data.objects.new("CavityBounce", cav_data)
    cav_center = Vector((math.cos(OPEN_DIR + math.pi * 0.12) * 0.10, math.sin(OPEN_DIR + math.pi * 0.12) * 0.10, BOLE_HEIGHT * 0.35))
    cav_light.location = cav_center
    scene.collection.objects.link(cav_light)

    # World background
    world = bpy.data.worlds.new("ForestWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.16, 0.18, 0.20, 1.0)
        bg.inputs[1].default_value = 0.55
    scene.world = world


def setup_render_settings(res=1024, samples=64):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False


# --------------------------------------------------------------------------
# Main Execution
# --------------------------------------------------------------------------

def main():
    argv = sys.argv
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="reference", choices=["reference", "mesh"])
    ap.add_argument("--view", default="whole", choices=["whole", "macro"])
    ap.add_argument("--out", default="build/hollowcrown-review/blender")
    ap.add_argument("--obj", default="")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--res", type=int, default=1024)
    ap.add_argument("--samples", type=int, default=64)
    ap.add_argument("--no-render", action="store_true")
    opts = ap.parse_args(args)
    opts.out = os.path.abspath(opts.out)
    if opts.obj:
        opts.obj = os.path.abspath(opts.obj)

    reset_scene()
    rng = random.Random(opts.seed)
    os.makedirs(opts.out, exist_ok=True)

    coll = bpy.context.scene.collection

    if opts.mode == "mesh":
        if not opts.obj:
            print("ERROR: --mode mesh requires --obj <path>")
            sys.exit(1)
        build_from_obj(opts.obj, coll)
        add_ground(radius=2.6, coll=coll)
        setup_camera_and_lighting(view_mode=opts.view)
    else:
        build_reference_hollowcrown(rng, coll)
        add_ground(radius=2.6, coll=coll)
        setup_camera_and_lighting(view_mode=opts.view)

    setup_render_settings(res=opts.res, samples=opts.samples)

    filename = f"hollowcrown_{opts.mode}_{opts.view}.png"
    out_path = os.path.join(opts.out, filename)
    bpy.context.scene.render.filepath = out_path

    if opts.no_render:
        print(f"BLENDER_SCENE_BUILT: {len(bpy.context.scene.objects)} objects")
        return

    print(f"Rendering {opts.mode} view={opts.view} to {out_path} ...")
    bpy.ops.render.render(write_still=True)
    print(f"RENDER_COMPLETE: {out_path}")


if __name__ == "__main__":
    main()
