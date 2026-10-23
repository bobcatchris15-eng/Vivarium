shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/living_leaf_optics.gdshaderinc"
#include "res://Shaders/leaf_direct_light.gdshaderinc"

uniform float leaf_detail = 1.0;
uniform float sway_strength = 0.0;
uniform float review_time = -1.0;
varying vec3 tissue_color;
varying float individuality;
varying float baked_ao;
varying float old_stem;

void vertex() {
    tissue_color = srgb_to_linear(COLOR.rgb);
    baked_ao = UV2.x > .01 ? UV2.x : .94;
    // UV2.y is reserved here for the runner mesh's senescent stem tissue; existing meshes use zero.
    old_stem = UV2.y;
    // Stable instance position varies tissue without consuming ecological RNG draws.
    individuality = fract(sin(dot(MODEL_MATRIX[3].xz, vec2(12.9898, 78.233))) * 43758.5453);
    float clock = review_time >= 0.0 ? review_time : TIME;
    float rooted = smoothstep(0.003, 0.055, VERTEX.y) * sway_strength;
    if (rooted > 0.0) {
        VERTEX.x += sin(clock * 1.8 + individuality * 6.283) * .0018 * rooted;
        VERTEX.z += cos(clock * 1.3 + individuality * 6.283) * .0011 * rooted;
    }
}

void fragment() {
    tissue_fiber_view = vec3(1.0, 0.0, 0.0);
    tissue_fiber_mask = 0.0;
    tissue_phase_mask = 1.0 - clamp(old_stem, 0.0, 1.0);
    if (!FRONT_FACING) NORMAL = -NORMAL;
    // Blades now carry real u across the width and v base-to-tip; the venation network below is
    // live rather than sampling a constant UV. Stems (UV2.y = 1) keep a mid-blade profile.
    float blade = 1.0 - clamp(old_stem, 0.0, 1.0);
    float blade_v = mix(0.5, UV.y, blade);
    float across = abs(UV.x - .5);
    float midrib = 1.0 - smoothstep(.018, .07, across);
    float vein_phase = blade_v * 7.0 - across * 3.0;
    float aa = max(fwidth(vein_phase), .025);
    float vein_distance = abs(fract(vein_phase) - .5);
    float veins = (1.0 - smoothstep(.035, .035 + aa, vein_distance))
        * smoothstep(.035, .12, across) * (1.0 - smoothstep(.35, .5, across));
    // Basal sheath is thick and veinless; the hyaline tip edge is the thinnest tissue in the blade.
    float sheath = (1.0 - smoothstep(.02, .11, blade_v)) * blade;
    float hyaline = smoothstep(.88, 1.0, blade_v) * smoothstep(.10, .42, across) * blade;
    float anatomy = clamp(midrib * .65 + veins * .28, 0.0, 1.0) * leaf_detail * (1.0 - sheath * .9);
    vec3 pigment = tissue_color * mix(.90, 1.08, individuality);
    pigment *= mix(.94, 1.09, anatomy);
    pigment = mix(pigment, pigment * vec3(1.35, .91, .52), old_stem * .72);
    // Tips senesce first; some individuals dry off far ahead of their neighbours.
    float senescent = smoothstep(.48, 1.0, blade_v) * mix(.20, .70, individuality) * blade;
    pigment = mix(pigment, pigment * vec3(1.24, .97, .58), max(old_stem * .72, senescent * .45));
    pigment = mix(pigment, pigment * vec3(1.12, 1.08, .88), float(!FRONT_FACING) * .22);
    pigment = mix(pigment * .84, pigment, smoothstep(.0, .16, blade_v));
    float crown = clamp(baked_ao, .15, 1.0);
    float crown_ao = mix(.45, 1.0, crown);
    ALBEDO = pigment * mix(.75, 1.0, sqrt(crown));
    ROUGHNESS = mix(.83, .66 + across * .14, leaf_detail);
    SPECULAR = .28;
    BACKLIGHT = leaf_through(pigment, .28 * mix(1., .2, old_stem), anatomy, .48 + hyaline * .34, float(!FRONT_FACING));
    vec3 dndx = dFdx(NORMAL), dndy = dFdy(NORMAL);
    float variance = dot(dndx, dndx) + dot(dndy, dndy);
    ROUGHNESS = clamp(sqrt(ROUGHNESS * ROUGHNESS + min(2.0 * variance, .25)), .35, 1.0);
    AO = crown_ao;
    AO_LIGHT_AFFECT = .10;
}
