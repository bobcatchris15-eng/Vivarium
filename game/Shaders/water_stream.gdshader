// Flowing Water / Spring Stream Shader.
// Dynamically animated along the authoritative hydrology flow vector (UV2).
// Crystal-clear shallow running water with flow-aligned capillary ripples,
// speed-dependent agitation, and frothy aeration on fast currents or cascades.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
// Vivarium pond water: clear in the shallows, tannin/green-stained in depth (absorbs red+blue first).
uniform vec3 absorption = vec3(1.5, 0.7, 1.9);     // per unit normalized depth
uniform vec3 deep_tint : source_color = vec3(0.045, 0.075, 0.040);
// Reflection of the dark enclosure backdrop (no sky in a vivarium): darkens grazing water instead of milking it.
uniform vec3 backdrop_low : source_color = vec3(0.020, 0.022, 0.018);
uniform vec3 backdrop_high : source_color = vec3(0.070, 0.075, 0.065);
uniform vec3 foam_color : source_color = vec3(0.92, 0.96, 0.98);
uniform float refraction = 0.003;
uniform float flow_speed_mult = 3.5;
uniform float underwater = 0.0;

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

    if (cutface < 0.5) {
        h = stream_height(UV, fdir, sdir, t);
        hx = stream_height(UV + vec2(e, 0.0), fdir, sdir, t);
        hz = stream_height(UV + vec2(0.0, e), fdir, sdir, t);

        float amp = (0.10 + 0.30 * clamp(speed, 0.0, 1.0)) * mix(0.12, 1.0, depthf);
        vec2 slope = vec2(h - hx, h - hz) / e * amp * 0.045;
        vec3 wn = normalize(vec3(slope.x, 1.0, slope.y));
        if (underwater > 0.5) wn = -wn;
        NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
    }

    float surf_depth = -VERTEX.z;
    float base_depth = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
    float base_thickness = max(base_depth - surf_depth, 0.0);
    float refraction_weight = smoothstep(0.005, 0.04, base_thickness);
    vec2 offset = vec2(h - hx, h - hz) / 0.02 * refraction * refraction_weight * (1.0 - cutface);
    vec2 ruv = SCREEN_UV + offset;
    float candidate_depth = linear_depth(ruv, INV_PROJECTION_MATRIX);
    float edge_weight = 1.0 - smoothstep(0.02, 0.08, abs(candidate_depth - base_depth));
    if (candidate_depth < surf_depth) edge_weight = 0.0;
    ruv = SCREEN_UV + offset * edge_weight;

    // Crisp bed visibility through shallow running water
    float optical_depth = depthf * 0.15;
    float tannin_depth = smoothstep(0.0, 1.0, depthf) * 0.45 + min(base_thickness, 0.6) * 0.2;
    vec3 behind = textureLod(screen_tex, ruv, 0.0).rgb;
    vec3 transmit = exp(-absorption * tannin_depth);
    if (cutface > 0.5 || underwater > 0.5) transmit = vec3(1.0);
    vec3 scene = mix(deep_tint, behind, transmit);

    // Surface foam and aeration on fast-flowing sections
    float u_along = dot(UV, fdir);
    float u_across = dot(UV, sdir);
    float fast_flow = smoothstep(0.35, 1.4, speed) * smoothstep(0.008, 0.04, optical_depth);
    float foam_noise = sin(u_along * 38.0 - t * 7.5) * cos(u_across * 24.0 + sin(u_along * 14.0));
    float foam_mask = smoothstep(0.35, 0.80, foam_noise * 0.5 + 0.5 + (h - 0.5) * 0.4) * fast_flow;
    scene = mix(scene, foam_color, foam_mask * 0.60);

    if (cutface < 0.5 && underwater < 0.5) {
        vec3 view_dir = normalize(-VERTEX);
        float ndotv = clamp(dot(normalize(NORMAL), view_dir), 0.0, 1.0);
        float fresnel = 0.02 + 0.98 * pow(1.0 - ndotv, 5.0);
        vec3 reflected = (INV_VIEW_MATRIX * vec4(reflect(-view_dir, normalize(NORMAL)), 0.0)).xyz;
        vec3 sky = mix(backdrop_low, backdrop_high, smoothstep(-0.1, 0.8, reflected.y));
        scene = mix(scene, sky, fresnel * 0.55);
    }

    ALBEDO = vec3(0.0);
    EMISSION = scene;
    ALPHA = cutface > 0.5 ? 1.0 : smoothstep(0.0, 0.015, base_thickness);
    // Glints come from the real grow light via the lit specular term (ALBEDO is black, so only spec is lit).
    ROUGHNESS = cutface > 0.5 ? 0.0 : mix(0.09, 0.25, foam_mask);
    METALLIC = 0.0;
    SPECULAR = cutface > 0.5 ? 0.15 : 0.75;
}
