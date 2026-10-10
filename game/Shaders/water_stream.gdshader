// Flowing Water / Spring Stream Shader.
// Dynamically animated along the authoritative hydrology flow vector (UV2).
// Physically lit material with refraction, Beer-Lambert depth absorption,
// capillary ripples, and grow-light glints that survive steep camera views.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
// Low base albedo with subtle natural freshwater tint
uniform vec3 water_albedo : source_color = vec3(0.018, 0.035, 0.025);
// Vivarium pond water: clear in the shallows, tannin/green-stained in depth (absorbs red+blue first).
uniform vec3 absorption = vec3(1.5, 0.7, 1.9);     // per unit depth
uniform vec3 deep_tint : source_color = vec3(0.045, 0.075, 0.040);
// Reflection of the dark enclosure backdrop (no sky in a vivarium): darkens grazing water instead of milking it.
uniform vec3 backdrop_low : source_color = vec3(0.020, 0.022, 0.018);
uniform vec3 backdrop_high : source_color = vec3(0.070, 0.075, 0.065);
uniform vec3 grow_light_color : source_color = vec3(1.0, 0.98, 0.92);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 0.98);
uniform float refraction = 0.003;
uniform float flow_speed_mult = 3.5;
uniform float underwater = 0.0;
uniform float meniscus_threshold = 0.008;

varying float depthf;
varying vec2 flow;
varying float cutface;

void vertex() {
    depthf = COLOR.a;
    flow = UV2;
    cutface = step(abs(NORMAL.y), 0.5);
}

float linear_depth(vec2 uv, mat4 inv_proj) {
    float d = texture(depth_tex, uv).r;
    vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
    return -v.z / v.w;
}

float stream_height(vec2 uv, vec2 fdir, vec2 sdir, float t) {
    float u_along = dot(uv, fdir);
    float u_across = dot(uv, sdir);
    // Travelling waves along the current plus cross-stream capillary chop
    float w1 = sin(u_along * 24.0 - t * 5.5 + sin(u_across * 8.0) * 0.7);
    float w2 = sin(u_along * 48.0 - t * 11.0 + u_across * 16.0);
    float w3 = sin(u_across * 18.0 + sin(u_along * 12.0 - t * 3.0));
    return 0.5 + 0.35 * w1 + 0.20 * w2 + 0.12 * w3;
}

void fragment() {
    float h = 0.0, hx = 0.0, hz = 0.0;
    float speed = length(flow);
    vec2 fdir = speed > 0.002 ? flow / speed : vec2(0.8, 0.6);
    vec2 sdir = vec2(-fdir.y, fdir.x);

    float flow_speed = max(speed * flow_speed_mult, 0.75);
    float t = TIME * flow_speed;
    float e = 0.015;

    float amp = (0.10 + 0.30 * clamp(speed, 0.0, 1.0)) * mix(0.12, 1.0, depthf);
    vec2 slope = vec2(0.0);

    if (cutface < 0.5) {
        h = stream_height(UV, fdir, sdir, t);
        hx = stream_height(UV + vec2(e, 0.0), fdir, sdir, t);
        hz = stream_height(UV + vec2(0.0, e), fdir, sdir, t);

        slope = vec2(h - hx, h - hz) / e * amp * 0.045;
        vec3 wn = normalize(vec3(slope.x, 1.0, slope.y));
        if (underwater > 0.5) wn = -wn;
        NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
    }

    float surf_depth = -VERTEX.z;
    float scene_depth_straight = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
    float thickness_straight = max(scene_depth_straight - surf_depth, 0.0);

    // Clamped screen-space refraction with shoreline fade
    vec2 max_refr_offset = vec2(0.012);
    float shore_weight = smoothstep(0.003, 0.03, thickness_straight);
    vec2 refr_offset = clamp(slope * refraction * 35.0 * shore_weight * (1.0 - cutface), -max_refr_offset, max_refr_offset);
    vec2 ruv = clamp(SCREEN_UV + refr_offset, vec2(0.002), vec2(0.998));

    // Validate depth at refracted coordinate: avoid foreground sampling or depth tearing
    float candidate_depth = linear_depth(ruv, INV_PROJECTION_MATRIX);
    float candidate_water_depth = candidate_depth - surf_depth;
    if (candidate_water_depth < 0.0 || candidate_depth > surf_depth + 20.0) {
        ruv = SCREEN_UV;
        candidate_depth = scene_depth_straight;
        candidate_water_depth = thickness_straight;
    } else {
        float depth_disc = smoothstep(0.03, 0.12, abs(candidate_depth - scene_depth_straight));
        ruv = mix(ruv, SCREEN_UV, depth_disc);
        candidate_depth = mix(candidate_depth, scene_depth_straight, depth_disc);
        candidate_water_depth = max(candidate_depth - surf_depth, 0.0);
    }

    // Optical depth in water and Beer-Lambert exponential absorption
    float view_cos = max(abs(dot(normalize(NORMAL), normalize(-VERTEX))), 0.25);
    float optical_depth = candidate_water_depth / view_cos;
    if (scene_depth_straight < surf_depth || scene_depth_straight > surf_depth + 20.0) {
        optical_depth = (depthf <= 0.005) ? 0.0 : max(depthf * 0.25, 0.05);
    }

    vec3 transmit = exp(-absorption * optical_depth);
    if (cutface > 0.5 || underwater > 0.5) {
        transmit = vec3(1.0);
    }

    vec3 behind = textureLod(screen_tex, ruv, 0.0).rgb;
    vec3 transmitted = behind * transmit;

    // Surface foam and aeration on fast currents
    float u_along = dot(UV, fdir);
    float u_across = dot(UV, sdir);
    float fast_flow = smoothstep(0.35, 1.4, speed) * smoothstep(0.008, 0.04, optical_depth);
    float foam_noise = sin(u_along * 38.0 - t * 7.5) * cos(u_across * 24.0 + sin(u_along * 14.0));
    float foam_mask = smoothstep(0.35, 0.80, foam_noise * 0.5 + 0.5 + (h - 0.5) * 0.4) * fast_flow;

    // Thin meniscus / contact highlight line where water intersects scene geometry (stems, rocks, terrain)
    float contact_depth = scene_depth_straight - surf_depth;
    float contact_optical_depth = contact_depth / view_cos;
    float meniscus_line = 0.0;
    if (cutface < 0.5 && underwater < 0.5 && contact_depth >= 0.001 && contact_depth < 10.0 && depthf > 0.005) {
        meniscus_line = 1.0 - smoothstep(0.001, meniscus_threshold, contact_optical_depth);
    }
    float total_foam = (depthf <= 0.005 || contact_depth < 0.001) ? 0.0 : clamp(foam_mask + meniscus_line * 0.85, 0.0, 1.0);

    // View vectors and world-space reflection for backdrop and overhead grow light
    vec3 view_dir = normalize(-VERTEX);
    float ndotv = clamp(dot(normalize(NORMAL), view_dir), 0.0, 1.0);
    float fresnel = 0.02 + 0.98 * pow(1.0 - ndotv, 5.0);

    vec3 world_view = (INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz;
    vec3 world_normal = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
    vec3 world_refl = reflect(-world_view, world_normal);

    // Ripple slope variation for roughness/specular modulation and micro-facets
    float slope_mag = length(slope);
    float slope_var = clamp(slope_mag * 12.0, 0.0, 1.0);

    // Overhead LED grow-light panel reflection and facet sparkle glints
    float bar1 = exp(-pow(world_refl.z * 2.2, 2.0)) * smoothstep(0.40, 0.85, world_refl.y);
    float bar2 = exp(-pow((world_refl.z - 0.28) * 2.8, 2.0)) * smoothstep(0.50, 0.90, world_refl.y);
    float panel_spec = max(bar1, bar2 * 0.7);

    float sparkle_wave = sin(dot(UV, fdir) * 85.0 - t * 12.0 + sin(dot(UV, sdir) * 45.0) * 2.0);
    float sparkle_crest = clamp((h - 0.50) * 4.0, 0.0, 1.0) * clamp(slope_var * 1.6, 0.0, 1.0);
    float sparkle = pow(clamp(sparkle_wave * 0.5 + 0.5, 0.0, 1.0), 16.0) * sparkle_crest * smoothstep(0.35, 0.85, world_refl.y);

    // Glints survive steep angles (camera looking down at 30-50 deg in water_margin and cutaway_pond)
    float glint_angle = mix(0.20, 1.0, fresnel);
    float bank_edge_fade = smoothstep(0.005, 0.02, depthf) * smoothstep(0.0, 0.005, contact_depth);
    vec3 glints = (grow_light_color * panel_spec * 1.1 + grow_light_color * sparkle * 2.4) * glint_angle * (1.0 - cutface) * (1.0 - underwater) * bank_edge_fade;

    // 1. Real ALBEDO: small base albedo with subtle tint, receiving real scene diffuse lighting, with foam and meniscus
    vec3 surf_albedo = mix(water_albedo, foam_color, total_foam * 0.75);
    ALBEDO = (cutface > 0.5 || underwater > 0.5) ? vec3(0.0) : surf_albedo;

    // 2. Modulate ROUGHNESS and SPECULAR from ripple slope/normal variation for Godot PBR highlights
    float base_roughness = mix(0.035, 0.14, slope_var);
    ROUGHNESS = cutface > 0.5 ? 0.0 : mix(base_roughness, 0.25, total_foam);
    float base_specular = mix(0.50, 0.85, slope_var);
    SPECULAR = cutface > 0.5 ? 0.20 : mix(base_specular, 0.95, meniscus_line);
    METALLIC = 0.0;

    // 3, 4, 5. Shading and emission: transmitted bed + subtle backdrop + grow-light glints + contact highlight
    if (cutface > 0.5) {
        float meniscus = (depthf <= 0.005) ? 0.0 : (1.0 - smoothstep(0.0, 0.006, UV.y));
        EMISSION = mix(behind, vec3(0.90, 0.96, 1.0), meniscus * 0.5);
        ALPHA = 1.0;
    } else if (underwater > 0.5) {
        EMISSION = behind;
        ALPHA = 1.0;
    } else {
        vec3 backdrop = mix(backdrop_low, backdrop_high, smoothstep(-0.1, 0.7, world_refl.y));
        vec3 surface_refl = backdrop * fresnel * 0.4;
        vec3 contact_highlight = (contact_depth < 0.001 || depthf <= 0.005) ? vec3(0.0) : (foam_color * meniscus_line * 0.65);
        EMISSION = transmitted * (1.0 - fresnel * 0.65) + surface_refl + glints + contact_highlight;
        ALPHA = max(smoothstep(0.0, 0.015, thickness_straight), meniscus_line * 0.95);
    }
}
