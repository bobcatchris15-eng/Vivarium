// Water Table Shader: calm, crystal-clear standing pond water.
// High optical clarity reveals the pebbles, submerged rocks, and aquatic life beneath.
// Smooth ambient surface ripples provide Fresnel sky reflection and crisp specular sun glints.
// Vertical cut faces at the island boundary give a clear cross-section through aquarium glass.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 absorption = vec3(0.45, 0.18, 0.08);   // per metre: clean fresh water
uniform vec3 deep_tint : source_color = vec3(0.06, 0.22, 0.26);
uniform float refraction = 0.0025;
uniform float ripple_scale = 8.0;
uniform float underwater = 0.0;

float water_height(vec2 uv, float t) {
    vec2 q = uv * ripple_scale;
    float warp = 0.18 * sin(dot(q, vec2(0.31, 0.17)) + t * 0.22)
               + 0.12 * sin(dot(q, vec2(-0.23, 0.29)) - t * 0.15);
    float a = sin(dot(q, vec2(0.92, 0.38)) + warp - t * 0.75);
    float b = sin(dot(q, vec2(-0.45, 0.89)) * 1.35 - warp * 0.6 + t * 0.55);
    float c = sin(dot(q, vec2(0.68, -0.73)) * 2.15 + warp * 0.4 - t * 1.15);
    return 0.5 + 0.24 * a + 0.16 * b + 0.08 * c;
}

varying float depthf;
varying float cutface;

void vertex() {
    depthf = COLOR.a;
    cutface = step(abs(NORMAL.y), 0.5);
}

float linear_depth(vec2 uv, mat4 inv_proj) {
    float d = texture(depth_tex, uv).r;
    vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
    return -v.z / v.w;
}

void fragment() {
    float h = 0.0, hx = 0.0, hz = 0.0;
    if (cutface < 0.5) {
        float t = TIME;
        float e = 0.018;
        h = water_height(UV, t);
        hx = water_height(UV + vec2(e, 0.0), t);
        hz = water_height(UV + vec2(0.0, e), t);
        vec2 slope = vec2(h - hx, h - hz) / e * 0.038;
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

    // Optical depth and Beer-Lambert transmission: full clarity without artificial mip blur
    float view_cos = max(abs(dot(normalize(NORMAL), normalize(-VERTEX))), 0.25);
    float optical_depth = depthf * 0.30 / view_cos;
    if (cutface > 0.5 || underwater > 0.5) optical_depth = 0.0;
    vec3 behind = textureLod(screen_tex, ruv, 0.0).rgb;
    vec3 transmit = exp(-absorption * optical_depth);
    vec3 scene = mix(deep_tint, behind, transmit);

    if (cutface > 0.5 || underwater > 0.5) scene = behind;
    if (cutface < 0.5 && underwater < 0.5) {
        vec3 view_dir = normalize(-VERTEX);
        float ndotv = clamp(dot(normalize(NORMAL), view_dir), 0.0, 1.0);
        float fresnel = 0.02 + 0.98 * pow(1.0 - ndotv, 5.0);
        vec3 reflected = (INV_VIEW_MATRIX * vec4(reflect(-view_dir, normalize(NORMAL)), 0.0)).xyz;
        vec3 sky = mix(vec3(0.50, 0.65, 0.76), vec3(0.85, 0.92, 0.98), smoothstep(-0.1, 0.8, reflected.y));
        scene = mix(scene, sky, fresnel * 0.82);
    }

    if (cutface > 0.5) {
        float meniscus = 1.0 - smoothstep(0.0, 0.005, UV.y);
        scene = mix(scene, vec3(0.88, 0.96, 1.0), meniscus * 0.55);
    }

    ALBEDO = vec3(0.0);
    EMISSION = scene;
    ALPHA = cutface > 0.5 ? 1.0 : smoothstep(0.0, 0.025, base_thickness);
    ROUGHNESS = cutface > 0.5 ? 0.0 : 0.035;
    METALLIC = 0.0;
    SPECULAR = cutface > 0.5 ? 0.15 : 0.6;
}
