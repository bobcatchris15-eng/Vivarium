// Rich mineral loam for the integrated terrain surface.
// The soil_* samplers intentionally match IslandRenderer's existing Ground048 binding.
// Leaf litter stays a separate renderer/material layer; scanned microdetail stays in the loam material.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform vec4 field_rect; // origin.x, origin.z, width.x, width.z
uniform float review_time = -1.0; // retained renderer contract; this material is static

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

// Relative material controls, with dimensions in world metres.
// Suggested native review profiles:
// dry: dampness .25, roughness .94, clump relief .018m
// loam: dampness .65, roughness .84, clump relief .012m
// saturated: dampness .90, roughness .74, clump relief .006m
uniform float loam_dampness = 0.65;
uniform float loam_roughness = 0.84;
uniform float loam_clump_relief = 0.012;
uniform float loam_vertex_relief_strength = 1.0; // set 0 when colocated geometry follows the heightfield directly
uniform float loam_seed = 17.0;
uniform float loam_texture_tile_metres = 0.82;

varying vec3 loam_world_pos;

float loam_hash(vec2 p) {
    uvec2 q = uvec2(ivec2(floor(p)));
    uint n = (q.x * 1597334677u) ^ (q.y * 3812015801u);
    n = (n ^ (n >> 16u)) * 2246822519u;
    n ^= n >> 13u;
    return float(n & 16777215u) / 16777215.0;
}

float loam_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    float a = loam_hash(i);
    float b = loam_hash(i + vec2(1.0, 0.0));
    float c = loam_hash(i + vec2(0.0, 1.0));
    float d = loam_hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float loam_macro_height(vec2 xz) {
    // Broad, low-amplitude lift survives the terrain mesh's roughly 10cm vertex spacing.
    vec2 p = xz * 4.2 + vec2(loam_seed * 0.17, loam_seed * 0.31);
    float broad = loam_noise(p) - 0.5;
    float shoulder = loam_noise(p * 1.83 + 12.4) - 0.5;
    return (broad * 1.35 + shoulder * 0.40) * clamp(loam_clump_relief, 0.004, 0.030);
}

float loam_meso_height(vec2 xz) {
    // Warped continuous noise makes joined, uneven humus aggregates instead of round repeated domes.
    vec2 warp = (vec2(loam_noise(xz * 5.3 + 8.4 + loam_seed),
        loam_noise(xz * 5.3 + 23.1 + loam_seed)) - 0.5) * 1.65;
    vec2 p = xz * 26.0 + warp + vec2(loam_seed * 1.7, loam_seed * 2.3);
    float aggregate = loam_noise(p);
    float fine = loam_noise(p * 2.15 + 17.8);
    float pore = loam_noise(p * 0.83 + 41.2);
    float clod_field = smoothstep(0.36, 0.72, aggregate) - 0.43;
    float pore_field = smoothstep(0.70, 0.91, pore);
    // Fragment aggregates stay around 1.5-2.5mm; the separate macro vertex term remains 8-18mm.
    float relief = clamp(loam_clump_relief * 0.14, 0.0015, 0.0025);
    return relief * (clod_field * 0.72 + (fine - 0.5) * 0.15 - pore_field * 0.14);
}

void vertex() {
    vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float relief_strength = clamp(loam_vertex_relief_strength, 0.0, 1.0);
    float h = loam_macro_height(wp.xz) * relief_strength;
    const float e = 0.018;
    float dx = (loam_macro_height(wp.xz + vec2(e, 0.0)) - loam_macro_height(wp.xz - vec2(e, 0.0))) / (2.0 * e) * relief_strength;
    float dz = (loam_macro_height(wp.xz + vec2(0.0, e)) - loam_macro_height(wp.xz - vec2(0.0, e))) / (2.0 * e) * relief_strength;
    VERTEX.y += h;
    NORMAL = normalize(NORMAL - vec3(dx, 0.0, dz));
    loam_world_pos = wp + vec3(0.0, h, 0.0);
}

void fragment() {
    vec2 field_uv = (loam_world_pos.xz - field_rect.xy) / max(field_rect.zw, vec2(0.001));
    float field_moisture = texture(field_tex, clamp(field_uv, vec2(0.001), vec2(0.999))).r;
    float damp = clamp(mix(clamp(loam_dampness, 0.0, 1.0), field_moisture, 0.28), 0.0, 1.0);

    vec2 tex_uv = loam_world_pos.xz / max(loam_texture_tile_metres, 0.2);
    // Keep most of the real scan in each pixel; a small second sample softens visible tile repetition.
    vec2 warp = (vec2(loam_noise(tex_uv * 0.27 + 7.3), loam_noise(tex_uv * 0.27 + 21.7)) - 0.5) * 0.18;
    vec2 uv_a = tex_uv + warp;
    vec2 uv_b = vec2(tex_uv.x * 0.91 - tex_uv.y * 0.41, tex_uv.x * 0.41 + tex_uv.y * 0.91) + vec2(0.37, 0.19) - warp * 0.55;
    float breakup = smoothstep(0.25, 0.75, loam_noise(tex_uv * 0.19 + 3.1));
    vec3 scan = mix(texture(soil_col, uv_a).rgb, texture(soil_col, uv_b).rgb, breakup * 0.15 + 0.04);
    vec3 scan_n = unpack_normal(texture(soil_nrm, uv_a).rgb);
    float scan_roughness = mix(texture(soil_rgh, uv_a).r, texture(soil_rgh, uv_b).r, breakup * 0.15 + 0.04);

    // Ground048 is already photographed chocolate-brown soil. Preserve its measured micro-luminance
    // and chroma; apply only a restrained cooler balance to remove the rusty cast of the previous tint.
    vec3 loam = scan * vec3(0.65, 0.78, 0.90);

    float clod_height = loam_meso_height(loam_world_pos.xz);
    float wet_darkening = mix(1.0, 0.80, damp * 0.58);
    float humus_patches = mix(0.78, 1.08, smoothstep(0.18, 0.82, loam_noise(loam_world_pos.xz * 5.7 + loam_seed)));
    ALBEDO = clamp(loam * wet_darkening * humus_patches, vec3(0.018), vec3(0.18));
    ROUGHNESS = clamp(mix(clamp(loam_roughness, 0.55, 0.99), scan_roughness, 0.18) - damp * 0.035, 0.60, 0.99);
    METALLIC = 0.0;
    SPECULAR = 0.22;

    // Derivative-based relief normal: the fragment-scale clods remain legible between terrain vertices.
    vec3 base_world_normal = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 px = dFdx(loam_world_pos), py = dFdy(loam_world_pos);
    float hx = dFdx(clod_height), hy = dFdy(clod_height);
    float det = px.x * py.z - px.z * py.x;
    float safe_det = abs(det) < 1e-10 ? (det < 0.0 ? -1e-10 : 1e-10) : det;
    float gradient_x = (hx * py.z - hy * px.z) / safe_det;
    float gradient_z = (hy * px.x - hx * py.x) / safe_det;
    vec3 relief_normal = normalize(base_world_normal - vec3(gradient_x, 0.0, gradient_z));
    vec3 scan_world_normal = cotangent_normal(relief_normal, loam_world_pos, uv_a, scan_n);
    vec3 view_normal = normalize((VIEW_MATRIX * vec4(mix(relief_normal, scan_world_normal, 0.22), 0.0)).xyz);
    NORMAL = view_normal;
}
