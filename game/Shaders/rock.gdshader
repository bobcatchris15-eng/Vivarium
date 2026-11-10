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
varying float surface_wet;
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
    float cam_dist = distance(CAMERA_POSITION_WORLD, world_pos);
    bool pebble = pebble_mode > 0.5;
    // Geometric curvature from the smooth facet normal: high on broken ridge lines
    // and on the concave seams where two cleavage planes meet.
    float edge_curv = clamp(length(dFdx(wn)) + length(dFdy(wn)), 0.0, 1.0);

    float height_scale = 0.02;
    vec2 uv = world_pos.xz / max(tile_metres, 0.001);
    vec2 displaced_uv = uv;
    float pom_shadow = 1.0;
    float pom_fade = 1.0 - smoothstep(3.0, 5.0, cam_dist);

    // Field moisture feeds both the soil stain and the capillary damp belt.
    float moisture = 0.0;
    if (field_rect.z > 0.0) {
        vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
        moisture = texture(field_tex, clamp(fuv, vec2(0.001), vec2(0.999))).r;
    }

    vec3 n_ts;
    vec4 pbr = vec4(1.0);
    float h_val = 0.5;
    float ao_tex = 1.0;
    vec3 pebble_col = vec3(0.5);

    if (pebble) {
        // Pebble fast path: one albedo/roughness/normal tap and no POM. The
        // 15-tap stochastic bomb and the 28-tap march are invisible at gravel
        // size and dominate the draw cost of every scattered stone.
        vec3 pc = texture(rock_col, uv).rgb;
        float pr = texture(rock_rgh, uv).r;
        n_ts = unpack_normal(texture(rock_nrm, uv).rgb);
        pbr = vec4(pc, pr);

        // Deterministic per-stone mineral family with horizontal sediment
        // banding, so neighbouring pebbles read as varied river/scree stone.
        float id = hash21(inst_origin.xz * 41.7 + inst_origin.y * 7.3 + 0.5);
        float gas = vnoise(obj_pos.xz * 170.0 + obj_pos.y * 95.0);
        float band = 0.5 + 0.5 * sin(obj_pos.y * 34.0 + id * 6.2831853
            + (vnoise(obj_pos.xz * 24.0) - 0.5) * 1.5);
        vec3 warm = mix(vec3(0.46, 0.40, 0.32), vec3(0.58, 0.50, 0.39), fract(id * 5.71));
        vec3 cool = mix(vec3(0.29, 0.31, 0.34), vec3(0.41, 0.43, 0.46), fract(id * 11.3));
        vec3 mineral = mix(cool, warm, smoothstep(0.22, 0.82, fract(id * 3.17)));
        mineral *= mix(vec3(0.88, 0.93, 1.02), vec3(1.08, 1.02, 0.93), band);
        mineral *= mix(0.80, 1.15, fract(id * 17.9));
        float pc_lum = max(lum(pc), 0.02);
        pebble_col = mineral * (mix(vec3(pc_lum), pc, 0.55) / pc_lum);

        // Fine grain micro-normal from two cheap hashes: crisp dry-stone face.
        n_ts = finite_surface_normal(n_ts + vec3((gas - 0.5) * 0.5,
            (vnoise(obj_pos.zy * 170.0 + 7.7) - 0.5) * 0.5, 0.0), vec3(0.0, 0.0, 1.0));
    } else {
        if (pom_fade > 0.001) {
            float near_shadow = 1.0;
            vec2 near_uv = pom_raymarch(rock_hgt, uv, v_ts, height_scale, 16, near_shadow, l_ts);
            displaced_uv = mix(uv, near_uv, pom_fade);
            pom_shadow = mix(1.0, near_shadow, pom_fade);
        }
        pbr = sample_stochastic_pbr(rock_col, rock_nrm, rock_rgh, rock_hgt, rock_ao, displaced_uv, n_ts, h_val, ao_tex);
    }

    vec3 perturbed = cotangent_normal(wn, world_pos, displaced_uv, n_ts);

    vec3 col;
    if (pebble) {
        // Instance tint still carries authored variation; blend it with the
        // deterministic mineral tone so neither source crushes the other.
        vec3 instance_tint = srgb_to_linear(COLOR.rgb);
        col = mix(instance_tint, pebble_col, 0.65) * 0.9;
    } else {
        float avg = max(lum(textureLod(rock_col, vec2(0.5), 10.0).rgb), 0.02);
        vec3 tint = srgb_to_linear(vec3(0.56, 0.54, 0.52));
        vec2 cell = inst_origin.xz * 97.0;
        float j = vnoise(cell + 2.1), k = vnoise(cell * 1.7 + 8.7);
        tint *= mix(vec3(1.04, 1.0, 0.94), vec3(0.95, 0.97, 1.0), j) * (0.85 + 0.3 * k);
        vec3 scan = mix(vec3(lum(pbr.rgb)), pbr.rgb, 0.72) / avg;
        col = tint * scan * 0.9;
    }

    if (!pebble) {
        // Damp creek stone, not chalk: deepen the value and break the flat facet
        // read with mineral stratification aligned to object elevation rather than
        // to the projected texture, so stacked beds run continuously up a boulder.
        col *= 0.84;
        float strata = 0.88 + 0.22 * vnoise(vec2(obj_pos.y * 14.0 + obj_pos.x * 2.5, obj_pos.z * 2.5));
        col *= strata;
    }

    // Basal cavity: pebbles darken where the stone meets the soil; boulders keep
    // their baked vertex cavity, strengthened by the contact stain below.
    float basal = pebble ? smoothstep(-0.35, 0.25, obj_pos.y) : 1.0;
    float cavity_ao = pebble ? mix(0.40, 1.0, basal) * mix(0.86, 1.0, COLOR.r) : COLOR.r;
    float ridge = pebble ? 0.0 : COLOR.g;
    col *= mix(0.50, 1.0, cavity_ao);
    col += ridge * 0.04;
    if (pebble) col *= mix(0.78, 1.0, basal);

    float up = smoothstep(0.30, 0.88, wn.y);
    float broad = vnoise(obj_pos.xz * 2.35 + obj_pos.y * 0.8 + 3.2);
    float broken = vnoise(obj_pos.zx * 4.91 + obj_pos.y * 1.7 + 11.6);
    float crust = smoothstep(0.58, 0.76, broad * 0.72 + broken * 0.28) * up * lichen;
    vec3 lichenCol = mix(vec3(0.43, 0.46, 0.34), vec3(0.56, 0.52, 0.32), vnoise(obj_pos.xz * 6.7 + 5.0));
    col = mix(col, lichenCol * (0.92 + 0.14 * vnoise(world_pos.xz * 24.0)), crust * 0.58);

    if (!pebble) {
        // Facet weathering: upward crests keep a drier, lighter patina while
        // recessed seams hold damp grime, so faceted steps stop reading as plaster.
        float facet = smoothstep(0.02, 0.16, edge_curv);
        float crest = smoothstep(0.45, 0.90, wn.y);
        col *= mix(1.0, 0.86, facet * (1.0 - crest));
        col *= 1.0 + 0.09 * facet * crest;
        // Rain-washed top faces carry a thin mineral patina and micro-lichen film.
        float top_patina = smoothstep(0.55, 0.95, wn.y);
        col *= mix(1.0, 0.90, top_patina * 0.7);
        col = mix(col, col * vec3(0.92, 0.97, 0.88), top_patina * 0.5);
    }

    float foot = 1.0 - smoothstep(-0.30, 0.08, obj_pos.y);
    float stainNoise = 0.78 + 0.22 * vnoise(obj_pos.xz * 5.2 + 17.0);
    vec3 soilStain = vec3(0.24, 0.19, 0.14) * stainNoise;
    col = mix(col, soilStain, foot * dust * 0.34);

    float rough = mix(clamp(pbr.a, 0.48, 1.0), 0.92, crust);
    rough = mix(rough, rough * 0.7, ridge);

    float wet = 0.0;
    if (world_pos.y < water_table + 0.04) wet = smoothstep(water_table + 0.04, water_table, world_pos.y);
    wet = max(wet, moisture * 0.5);
    // Capillary dampness wicks out of the soil and up the boulder's foot.
    if (!pebble) wet = max(wet, foot * (0.25 + 0.55 * moisture));
    if (wet > 0.0) {
        col *= mix(1.0, 0.72, wet);
        // A water film adds a smooth coat, but porous stone keeps its underlying roughness.
        rough = mix(rough, min(rough, max(0.42, rough * 0.72)), wet);
    }

    ALBEDO = col;
    SPECULAR = 0.3;
    METALLIC = 0.0;
    NORMAL = normalize((VIEW_MATRIX * vec4(perturbed, 0.0)).xyz);

    // Specular anti-aliasing: widen roughness where the normal changes faster
    // than a pixel, so stochastic mineral normals do not sparkle in motion.
    float n_var = clamp(dot(dFdx(NORMAL), dFdx(NORMAL)) + dot(dFdy(NORMAL), dFdy(NORMAL)), 0.0, 0.25);
    ROUGHNESS = clamp(sqrt(rough * rough + n_var * 0.5), 0.04, 1.0);

    AO = cavity_ao * ao_tex * pom_shadow;
    AO_LIGHT_AFFECT = 0.65;

    if (wet > 0.0) {
        CLEARCOAT = 0.85 * wet;
        CLEARCOAT_ROUGHNESS = 0.1;
    }
    material_specular = SPECULAR;
    surface_wet = wet;
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
        // Quartz-mica micro-glints gated by actual shadowed light rather than baked into unlit albedo.
        float glint = pow(ndoth, 96.0) * 0.18;
        float coat = 0.0;
        if (surface_wet > 0.01) {
            float ca2 = 0.01 * 0.01;
            float cdenom = ndoth * ndoth * (ca2 - 1.0) + 1.0;
            float cD = ca2 / max(PI * cdenom * cdenom, 1e-4);
            float cF = 0.04 + 0.96 * pow(1.0 - vh, 5.0);
            coat = surface_wet * 0.85 * cD * cF / max(4.0 * ndotv * ndotl, 1e-4);
        }
        SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * (spec + glint + coat) * ndotl;
    }

    vec2 field_uv = (world_pos.xz - field_rect.xy) / max(field_rect.zw, vec2(0.001));
    float stream_depth = texture(sub_tex, clamp(field_uv, vec2(0.001), vec2(0.999))).b;
    float water_surface_y = water_table + max(0.0, stream_depth * 2.55 - 0.01);
    vec3 caustic = evaluate_submerged_caustics(world_pos, wn, water_surface_y, wl, TIME);
    DIFFUSE_LIGHT += caustic * ATTENUATION * LIGHT_COLOR;
}
