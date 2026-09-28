// Shallow wetland and running water shader.
// High optical clarity reveals the pebbles, submerged rocks, and aquatic life beneath.
// Dynamic waves provide Fresnel sky reflection and crisp specular sun glints.
// Fast currents read through animated ripples, travelling wave trains, and aerated foam.
// Vertical cut faces at the island edge remain clear, with a thin meniscus line at the top.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 absorption = vec3(0.45, 0.18, 0.08);   // per metre: clean fresh water
uniform vec3 deep_tint : source_color = vec3(0.06, 0.22, 0.26);
uniform float refraction = 0.0025;
uniform float flow_visual_scale = 4.0;
uniform float ripple_scale = 7.0;
uniform float underwater = 0.0;

float water_height(vec2 uv, vec2 f, float t) {
    float speed = length(f);
    vec2 fdir = speed > 0.002 ? f / speed : vec2(0.8, 0.6);
    vec2 sdir = vec2(-fdir.y, fdir.x);
    float flow_speed = max(speed * 3.5, 0.85);

    // Dynamic wave trains: flow-aligned when moving, serene ambient when still
    float u_along = dot(uv, fdir);
    float u_across = dot(uv, sdir);
    float moving_t = t * flow_speed;

    float stream_w = sin(u_along * 24.0 - moving_t * 5.0 + sin(u_across * 8.0) * 0.6);
    float cross_w = sin(u_across * 16.0 + sin(u_along * 10.0 - moving_t * 2.5));

    vec2 q = uv * ripple_scale;
    float ambient_w = sin(dot(q, vec2(0.92, 0.38)) - t * 0.75) * 0.6
                    + sin(dot(q, vec2(-0.45, 0.89)) * 1.3 + t * 0.55) * 0.4;

    float flow_weight = smoothstep(0.02, 0.20, speed);
    return 0.5 + mix(ambient_w * 0.35, (stream_w * 0.35 + cross_w * 0.20), flow_weight);
}

varying float depthf;
varying vec2 flow;
varying float cutface;
varying float wetbank;   // 1 on the rivulet wet-bank strip (COLOR.r == 1)
varying float thinfilm;  // 1 for sub-millimetre-to-few-mm films

void vertex() {
    depthf = COLOR.a;
    wetbank = step(0.95, COLOR.r);
    thinfilm = (1.0 - wetbank) * (1.0 - smoothstep(0.02, 0.05, COLOR.a));
    flow = UV2;
    cutface = step(abs(NORMAL.y), 0.5);
}

float linear_depth(vec2 uv, mat4 inv_proj) {
    float d = texture(depth_tex, uv).r;
    vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
    return -v.z / v.w;
}

void fragment() {
    float h = 0.0, hx = 0.0, hz = 0.0;
    vec2 f = flow * flow_visual_scale;
    float speed = length(flow);

    if (cutface < 0.5) {
        float t = TIME;
        float e = 0.018;
        h = water_height(UV, f, t);
        hx = water_height(UV + vec2(e, 0.0), f, t);
        hz = water_height(UV + vec2(0.0, e), f, t);

        float amp = 0.22 + 0.55 * clamp(speed * 3.0, 0.0, 1.0);
        vec2 slope = vec2(h - hx, h - hz) / e * amp * 0.045;
        vec3 wn = normalize(vec3(slope.x, 1.0, slope.y));
        if (underwater > 0.5) wn = -wn;
        NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
    }

    float surf_depth = -VERTEX.z;
    float base_depth = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
    float base_thickness = max(base_depth - surf_depth, 0.0);
    float refraction_weight = smoothstep(0.01, 0.06, base_thickness);
    vec2 offset = vec2(h - hx, h - hz) / 0.02 * refraction * refraction_weight * (1.0 - cutface);
    vec2 ruv = SCREEN_UV + offset;
    float candidate_depth = linear_depth(ruv, INV_PROJECTION_MATRIX);
    float edge_weight = 1.0 - smoothstep(0.03, 0.10, abs(candidate_depth - base_depth));
    if (candidate_depth < surf_depth) edge_weight = 0.0;
    ruv = SCREEN_UV + offset * edge_weight;

    // Optical depth and Beer-Lambert transmission: crisp resolution without mipmap blur
    float view_cos = max(abs(dot(normalize(NORMAL), normalize(-VERTEX))), 0.25);
    float optical_depth = depthf * 0.28 / view_cos;
    if (cutface > 0.5 || underwater > 0.5) optical_depth = 0.0;
    vec3 behind = textureLod(screen_tex, ruv, 0.0).rgb;
    vec3 transmit = exp(-absorption * optical_depth);
    vec3 scene = mix(deep_tint, behind, transmit);

    // Surface foam on fast-flowing sections
    float fast_flow = smoothstep(0.10, 0.45, speed);
    if (fast_flow > 0.0 && cutface < 0.5) {
        vec2 fdir = speed > 0.002 ? flow / speed : vec2(0.8, 0.6);
        vec2 sdir = vec2(-fdir.y, fdir.x);
        float u_along = dot(UV, fdir);
        float u_across = dot(UV, sdir);
        float foam_noise = sin(u_along * 36.0 - TIME * 7.0) * cos(u_across * 22.0);
        float foam_mask = smoothstep(0.35, 0.80, foam_noise * 0.5 + 0.5) * fast_flow;
        scene = mix(scene, vec3(0.92, 0.96, 0.98), foam_mask * 0.55);
    }

    if (cutface > 0.5 || underwater > 0.5) scene = behind;
    if (cutface < 0.5 && underwater < 0.5) {
        vec3 view_dir = normalize(-VERTEX);
        float ndotv = clamp(dot(normalize(NORMAL), view_dir), 0.0, 1.0);
        float fresnel = 0.02 + 0.98 * pow(1.0 - ndotv, 5.0);
        vec3 reflected = (INV_VIEW_MATRIX * vec4(reflect(-view_dir, normalize(NORMAL)), 0.0)).xyz;
        vec3 sky = mix(vec3(0.50, 0.65, 0.76), vec3(0.85, 0.92, 0.98), smoothstep(-0.1, 0.8, reflected.y));
        scene = mix(scene, sky, fresnel * 0.80);
    }

    if (cutface > 0.5) {
        float meniscus = 1.0 - smoothstep(0.0, 0.005, UV.y);
        scene = mix(scene, vec3(0.88, 0.96, 1.0), meniscus * 0.55);
    }

    ALBEDO = vec3(0.0);
    EMISSION = scene;
    ALPHA = cutface > 0.5 ? 1.0 : smoothstep(0.0, 0.025, base_thickness);
    float flow_rough = smoothstep(0.10, 0.50, speed);
    ROUGHNESS = cutface > 0.5 ? 0.0 : mix(0.035, 0.12, flow_rough);
    METALLIC = 0.0;
    SPECULAR = cutface > 0.5 ? 0.15 : 0.55;

    // Thin moving sheet: the ground shows straight through, the film reads as a glossy highlight.
    if (thinfilm > 0.0 && cutface < 0.5 && underwater < 0.5) {
        vec3 wet_ground = behind * vec3(0.62, 0.64, 0.66);
        EMISSION = mix(EMISSION, mix(wet_ground, scene, 0.35), thinfilm);
        ALPHA = mix(ALPHA, 0.92, thinfilm);
        ROUGHNESS = mix(ROUGHNESS, mix(0.015, 0.06, flow_rough), thinfilm);
        SPECULAR = mix(SPECULAR, 0.85, thinfilm);
    }
    // Wet bank strip: darkened, slightly glossy soil beside the rivulet, no water body.
    if (wetbank > 0.0 && underwater < 0.5) {
        EMISSION = mix(EMISSION, behind * vec3(0.58, 0.60, 0.62), wetbank);
        ALPHA = mix(ALPHA, 0.85, wetbank);
        ROUGHNESS = mix(ROUGHNESS, 0.30, wetbank);
        SPECULAR = mix(SPECULAR, 0.45, wetbank);
    }
}
