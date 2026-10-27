// Rocks and gravel pebbles: Voronoi-cleaved stratified boulders and water-worn river gravel.
// Stochastic triplanar-ish PBR with vertex cavity AO / ridge wear, quartz-mica glints, capillary wet belt.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/water_caustics.gdshaderinc"

uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_hgt : hint_default_black, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform sampler2D sub_tex : hint_default_black, filter_linear, repeat_disable;
uniform vec4 field_rect = vec4(0.0, 0.0, 1.0, 1.0);
uniform float water_table = 0.0;
uniform float tile_metres = 0.9;
uniform float lichen = 1.0;
uniform float dust = 0.4;
uniform float pebble_mode = 0.0;

varying vec3 world_pos;
varying vec3 obj_pos;
varying vec3 inst_origin;
varying vec3 v_ts;
varying vec3 l_ts;

varying float material_specular;
void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    obj_pos = VERTEX;
    inst_origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    vec3 view_dir = normalize((inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz - VERTEX);
    v_ts = vec3(dot(view_dir, TANGENT), dot(view_dir, BINORMAL), dot(view_dir, NORMAL));
    vec3 sun_world = normalize(vec3(0.35, 0.85, 0.25));
    vec3 light_view = normalize((inverse(MODEL_MATRIX) * vec4(sun_world, 0.0)).xyz);
    l_ts = vec3(dot(light_view, TANGENT), dot(light_view, BINORMAL), dot(light_view, NORMAL));
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 to_eye_view = normalize(-VERTEX);

    float height_scale = 0.02;
    vec2 uv = world_pos.xz / max(tile_metres, 0.001);
    vec2 displaced_uv = uv;
    float pom_shadow = 1.0;
    if (height_scale > 0.0) {
        displaced_uv = pom_raymarch(rock_hgt, uv, v_ts, height_scale, 16, pom_shadow, l_ts);
    }

    vec3 n_ts;
    float h_val, ao_tex;
    vec4 pbr = sample_stochastic_pbr(rock_col, rock_nrm, rock_rgh, rock_hgt, rock_ao, displaced_uv, n_ts, h_val, ao_tex);

    vec3 perturbed = cotangent_normal(wn, world_pos, displaced_uv, n_ts);

    float avg = max(lum(textureLod(rock_col, vec2(0.5), 10.0).rgb), 0.02);

    vec3 tint;
    if (pebble_mode > 0.5) {
        tint = srgb_to_linear(COLOR.rgb);
    } else {
        tint = srgb_to_linear(vec3(0.56, 0.54, 0.52));
        vec2 cell = inst_origin.xz * 97.0;
        float j = vnoise(cell + 2.1), k = vnoise(cell * 1.7 + 8.7);
        tint *= mix(vec3(1.04, 1.0, 0.94), vec3(0.95, 0.97, 1.0), j) * (0.85 + 0.3 * k);
    }

    vec3 scan = mix(vec3(lum(pbr.rgb)), pbr.rgb, 0.3) / avg;
    vec3 col = tint * scan * 0.9;

    float cavity_ao = (pebble_mode > 0.5) ? 1.0 : COLOR.r;
    float ridge = (pebble_mode > 0.5) ? 0.0 : COLOR.g;

    col *= mix(0.55, 1.0, cavity_ao);
    col += ridge * 0.04;

    float up = smoothstep(0.30, 0.88, wn.y);
    float broad = vnoise(obj_pos.xz * 2.35 + obj_pos.y * 0.8 + 3.2);
    float broken = vnoise(obj_pos.zx * 4.91 + obj_pos.y * 1.7 + 11.6);
    float crust = smoothstep(0.58, 0.76, broad * 0.72 + broken * 0.28) * up * lichen;
    vec3 lichenCol = mix(vec3(0.43, 0.46, 0.34), vec3(0.56, 0.52, 0.32), vnoise(obj_pos.xz * 6.7 + 5.0));
    col = mix(col, lichenCol * (0.92 + 0.14 * vnoise(world_pos.xz * 24.0)), crust * 0.58);

    float foot = 1.0 - smoothstep(-0.30, 0.08, obj_pos.y);
    float stainNoise = 0.78 + 0.22 * vnoise(obj_pos.xz * 5.2 + 17.0);
    vec3 soilStain = vec3(0.24, 0.19, 0.14) * stainNoise;
    col = mix(col, soilStain, foot * dust * 0.34);

    float rough = mix(clamp(pbr.a, 0.48, 1.0), 0.92, crust);
    rough = mix(rough, rough * 0.7, ridge);

    float moisture = 0.0;
    if (field_rect.z > 0.0) {
        vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
        moisture = texture(field_tex, clamp(fuv, vec2(0.001), vec2(0.999))).r;
    }
    float wet = 0.0;
    if (world_pos.y < water_table + 0.04) wet = smoothstep(water_table + 0.04, water_table, world_pos.y);
    wet = max(wet, moisture * 0.5);
    if (wet > 0.0) {
        col *= mix(1.0, 0.72, wet);
        rough = mix(rough, 0.25, wet);
    }

    vec3 sun_view = (VIEW_MATRIX * vec4(normalize(vec3(0.35, 0.85, 0.25)), 0.0)).xyz;
    vec3 facet_n_view = normalize((VIEW_MATRIX * vec4(perturbed, 0.0)).xyz);
    float glint = pow(max(dot(reflect(-to_eye_view, facet_n_view), normalize(sun_view)), 0.0), 96.0);
    col += vec3(0.9, 0.95, 1.0) * glint * 0.25;

    ALBEDO = col;
    ROUGHNESS = rough;
    METALLIC = 0.0;
    SPECULAR = 0.3;
    NORMAL = normalize((VIEW_MATRIX * vec4(perturbed, 0.0)).xyz);
    AO = cavity_ao * ao_tex * pom_shadow;
    AO_LIGHT_AFFECT = 0.65;

    if (wet > 0.0) {
        CLEARCOAT = 0.85 * wet;
        CLEARCOAT_ROUGHNESS = 0.1;
    }
    material_specular = SPECULAR;
}

void light() {
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 wl = normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz);
    vec3 vv = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);

    float ndotl = max(dot(wn, wl), 0.0);
    DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndotl * (1.0 / PI);

    if (ndotl > 0.0) {
        vec3 h = normalize(wl + vv);
        float ndoth = max(dot(wn, h), 0.0);
        float ndotv = max(dot(wn, vv), 1e-4);
        float a = max(ROUGHNESS * ROUGHNESS, 0.001);
        float a2 = a * a;
        float denom = ndoth * ndoth * (a2 - 1.0) + 1.0;
        float D = a2 / max(PI * denom * denom, 1e-4);
        float F0 = material_specular * 0.08;
        float vh = max(dot(vv, h), 0.0);
        float F = F0 + (1.0 - F0) * pow(1.0 - vh, 5.0);
        float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) * 0.125;
        float G = (ndotv / max(ndotv * (1.0 - k) + k, 1e-4)) *
                 (ndotl / max(ndotl * (1.0 - k) + k, 1e-4));
        float spec = D * G * F / max(4.0 * ndotv * ndotl, 1e-4);
        SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * spec * ndotl;
    }

    vec2 field_uv = (world_pos.xz - field_rect.xy) / max(field_rect.zw, vec2(0.001));
    float stream_depth = texture(sub_tex, clamp(field_uv, vec2(0.001), vec2(0.999))).b;
    float water_surface_y = water_table + max(0.0, stream_depth * 2.55 - 0.01);
    vec3 caustic = evaluate_submerged_caustics(world_pos, wn, water_surface_y, wl, TIME);
    DIFFUSE_LIGHT += caustic * ATTENUATION * LIGHT_COLOR;
}
