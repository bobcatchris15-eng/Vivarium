// Undercut mineral loam peds. UV still carries world XZ metres for legacy tools, but every channel
// is sampled triplanar in world space so the undercut shell and the crown share one scan.
// COLOR.a carries baked basal cavity occlusion: 0.25 at the undercut waist, 1.0 at the crown.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float soil_texture_tile_metres = 0.82;
uniform vec3 soil_tint = vec3(0.65, 0.78, 0.90);
uniform float soil_specular = 0.22;
// Contact parallax across the clod shell: 1-4 mm of relief, never more than 0.5% of a tile.
uniform float soil_relief_metres = 0.0035;
uniform float soil_parallax = 1.0;      // 0 on low quality tiers where the raymarch is not affordable
uniform vec3 sun_direction = vec3(0.35, 0.75, 0.45);

varying vec3 clod_world_pos;
varying vec3 clod_world_normal;

void vertex() {
    clod_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    clod_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    float tile = max(soil_texture_tile_metres, 0.2);
    vec3 wn = normalize(clod_world_normal);
    vec3 world_p = clod_world_pos / tile;
    vec3 to_eye = normalize(CAMERA_POSITION_WORLD - clod_world_pos);
    vec3 to_sun = normalize(sun_direction);

    // The dominant planar projection drives one parallax march. Its world-space result is then
    // shared by all three projections, so albedo, normal, roughness and AO stay one coherent surface.
    vec3 aw = abs(wn);
    vec3 tangent_w, bitangent_w, plane_w;
    vec2 base_uv;
    if (aw.x >= aw.y && aw.x >= aw.z) {
        tangent_w = vec3(0.0, 0.0, 1.0); bitangent_w = vec3(0.0, 1.0, 0.0); plane_w = vec3(sign(wn.x), 0.0, 0.0);
        base_uv = world_p.zy;
    } else if (aw.y >= aw.z) {
        tangent_w = vec3(1.0, 0.0, 0.0); bitangent_w = vec3(0.0, 0.0, 1.0); plane_w = vec3(0.0, sign(wn.y), 0.0);
        base_uv = world_p.xz;
    } else {
        tangent_w = vec3(1.0, 0.0, 0.0); bitangent_w = vec3(0.0, 1.0, 0.0); plane_w = vec3(0.0, 0.0, sign(wn.z));
        base_uv = world_p.xy;
    }

    vec3 view_ts = vec3(dot(to_eye, tangent_w), dot(to_eye, bitangent_w), dot(to_eye, plane_w));
    vec3 light_ts = vec3(dot(to_sun, tangent_w), dot(to_sun, bitangent_w), dot(to_sun, plane_w));
    float pom_shadow = 1.0;
    vec2 displaced_uv = base_uv;
    if (soil_parallax > 0.5) {
        float height_scale = clamp(soil_relief_metres, 0.0, 0.01) / tile;
        displaced_uv = pom_raymarch(soil_hgt, base_uv, view_ts, height_scale, 14, pom_shadow, light_ts);
    }
    vec2 parallax_uv = displaced_uv - base_uv;
    vec3 sample_p = world_p + (tangent_w * parallax_uv.x + bitangent_w * parallax_uv.y);

    vec3 scan_normal;
    vec4 scan = triplanar(soil_col, soil_nrm, soil_rgh, sample_p, wn, 5.0, scan_normal);

    // Three-way AO blend so the cavity map follows the same projection weights as the scan.
    vec3 tw = pow(aw, vec3(5.0));
    tw /= max(tw.x + tw.y + tw.z, 1e-4);
    float scan_ao = texture(soil_ao, sample_p.zy).r * tw.x
                  + texture(soil_ao, sample_p.xz).r * tw.y
                  + texture(soil_ao, sample_p.xy).r * tw.z;

    // Baked basal cavity occlusion from the ped's own vertex encoding.
    float cavity = clamp(COLOR.a, 0.05, 1.0);
    float occlusion = clamp(scan_ao * cavity * mix(1.0, pom_shadow, 0.45), 0.0, 1.0);

    ALBEDO = scan.rgb * soil_tint * COLOR.rgb;
    ROUGHNESS = clamp(scan.a * mix(1.0, 0.92, 1.0 - cavity), 0.55, 1.0);
    METALLIC = 0.0;
    SPECULAR = soil_specular;
    NORMAL = normalize((VIEW_MATRIX * vec4(scan_normal, 0.0)).xyz);
    AO = occlusion;
    AO_LIGHT_AFFECT = 0.65;
}
