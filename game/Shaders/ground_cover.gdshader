shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/living_leaf_optics.gdshaderinc"
#include "res://Shaders/leaf_direct_light.gdshaderinc"

uniform float leaf_detail = 1.0;
uniform float sway_strength = 0.0;
uniform float review_time = -1.0;
uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform vec4 field_rect = vec4(0.0);
uniform bool instance_shape_variation = false;
varying vec3 tissue_color;
varying float individuality;
varying float baked_ao;
varying float old_stem;
varying vec2 habitat_uv;

float cover_hash(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
    tissue_color = srgb_to_linear(COLOR.rgb);
    baked_ao = UV2.x > .01 ? UV2.x : .94;
    // UV2.y is reserved here for the runner mesh's senescent stem tissue; existing meshes use zero.
    old_stem = UV2.y;
    // Static instance-specific bending, with its exact inverse-transpose normal.
    // Terrain-conforming bank meshes keep their checked world-coordinate support.
    vec2 origin = MODEL_MATRIX[3].xz;
    individuality = cover_hash(origin);
    if (instance_shape_variation && VERTEX.y > 0.0) {
        vec2 lean = (vec2(cover_hash(origin + 3.17), cover_hash(origin - 7.31)) - .5) * .38;
        vec2 curl = (vec2(cover_hash(origin + 11.9), cover_hash(origin - 17.3)) - .5) * 6.0;
        float y = max(VERTEX.y, 0.0);
        VERTEX.xz += lean * y + curl * y * y;
        vec2 slope = lean + 2.0 * curl * y;
        NORMAL = normalize(vec3(NORMAL.x, NORMAL.y - dot(slope, NORMAL.xz), NORMAL.z));
    }
    vec2 world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
    habitat_uv = (world_xz - field_rect.xy) / max(field_rect.zw, vec2(.001));
    if (!instance_shape_variation) individuality = cover_hash(world_xz);
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
    // Terrain's live R/A channels are moisture and canopy-adjusted exposure.
    // Moisture controls value; exposure independently controls yellow/blue hue.
    vec2 habitat = vec2(.45, .6);
    if (field_rect.z > .001 && field_rect.w > .001) habitat = texture(field_tex, habitat_uv).ra;
    float moisture = smoothstep(.08, .85, clamp(habitat.x, 0.0, 1.0));
    float exposure = smoothstep(.10, .85, clamp(habitat.y, 0.0, 1.0));
    vec3 hue = mix(vec3(.20, .53, .58), vec3(.68, .76, .13), exposure);
    vec3 pigment = srgb_to_linear(hue * mix(1.10, .42, moisture)) * tissue_color
        * mix(.95, 1.05, individuality);
    // Keep the shared chlorophyll transmission lobe from painting every blade
    // the same bright green irrespective of its requested habitat pigment.
    tissue_phase_mask *= clamp(dot(pigment, vec3(.2126, .7152, .0722)) * 5.0, 0.0, .9);
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
    ROUGHNESS = .94 + .035 * individuality;
    SPECULAR = .035;
    vec3 dndx = dFdx(NORMAL), dndy = dFdy(NORMAL);
    float variance = dot(dndx, dndx) + dot(dndy, dndy);
    ROUGHNESS = clamp(sqrt(ROUGHNESS * ROUGHNESS + min(2.0 * variance, .25)), .90, 1.0);
    AO = crown_ao;
    AO_LIGHT_AFFECT = .10;
}
