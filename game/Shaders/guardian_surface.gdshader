// Guardian surfaces: 0 keratin, 1 fur, 2 bristles, 3 hide, 4 corneal eye.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform int mode = 0;
uniform vec3 base_color : source_color = vec3(0.8, 0.65, 0.5);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_albedo_texture = false;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_normal_texture = false;
uniform sampler2D roughness_texture : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_roughness_texture = false;
uniform float roughness_mul = 1.0;
uniform float metallic = 0.0;
uniform float specular = 0.5;
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform float water_level = -10000.0;
uniform float day_night : hint_range(0.0, 1.0) = 1.0;
uniform vec3 contact_sphere = vec3(0.0);
uniform float contact_radius = 0.0;
uniform float contact_max_displacement = 0.0015;
uniform float strand_density = 280.0;
varying vec3 world_pos;
varying vec3 vertex_color;
varying vec3 fiber_tangent;
varying float surface_wetness;

void vertex() {
    // Breathing remains skeletal so imported skin, eyes and contact samplers agree.
    // Only the local coat is displaced, never armor, eyes or nasal skin.
    if ((mode == 1 || mode == 2) && contact_radius > 0.001) {
        vec3 p = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        vec3 d = p - contact_sphere;
        float dist = length(d);
        if (dist < contact_radius && dist > 0.0001) {
            float push = min((contact_radius - dist) * 0.15, contact_max_displacement);
            VERTEX += (inverse(MODEL_MATRIX) * vec4(d / dist * push, 0.0)).xyz;
        }
    }
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vertex_color = COLOR.rgb;
}

float strand_height(vec2 uv) {
    return 0.5 + 0.5 * sin(uv.x * strand_density * 6.2831853 + sin(uv.y * 47.0) * 0.8);
}

void fragment() {
    surface_wetness = wetness * (1.0 - smoothstep(water_level, water_level + 0.04, world_pos.y));
    vec3 n = normalize(NORMAL);
    // Imported meshes need not have UV tangents. Construct an orthogonal strand
    // frame from their local longitudinal axis, with a pole-safe fallback.
    vec3 axis = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(0.0, 0.0, 1.0, 0.0)).xyz);
    vec3 t = axis - n * dot(axis, n);
    if (dot(t, t) < 0.001) {
        axis = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
        t = axis - n * dot(axis, n);
    }
    t = normalize(t);
    vec3 b = normalize(cross(n, t));
    vec2 uv = UV;
    if (mode == 1 || mode == 2) {
        // Three bounded depth steps suggest overlapping strands on the existing
        // skin; no fiber geometry or silhouette expansion is introduced.
        vec2 view_slope = vec2(dot(VIEW, b), dot(VIEW, t)) / max(dot(VIEW, n), 0.3);
        for (int i = 0; i < 3; i++) {
            uv -= view_slope * (1.0 - strand_height(uv)) * 0.00035;
        }
        float frequency = strand_density * 6.2831853;
        float antialias = 1.0 - smoothstep(0.4, 1.6, fwidth(uv.x) * strand_density);
        float ripple = cos(uv.x * frequency + sin(uv.y * 47.0) * 0.8) * antialias;
        n = normalize(n + b * ripple * mix(0.12, 0.035, surface_wetness));
    }
    if (has_normal_texture) {
        vec3 map_normal = texture(normal_texture, uv).rgb * 2.0 - 1.0;
        n = normalize(b * map_normal.x + t * map_normal.y + n * map_normal.z);
    }
    fiber_tangent = normalize(t - n * dot(t, n));
    NORMAL = n;
    vec3 col = base_color * vertex_color;
    if (has_albedo_texture) col *= texture(albedo_texture, uv).rgb;
    float rgh = roughness_mul;
    if (has_roughness_texture) rgh *= texture(roughness_texture, uv).r;
    ALBEDO = col;
    ROUGHNESS = clamp(rgh, 0.06, 1.0);
    METALLIC = metallic;
    SPECULAR = specular;
    SSS_STRENGTH = mode == 0 ? 0.65 : (mode == 3 ? 0.15 : 0.20);
    BACKLIGHT = col * (mode == 0 ? vec3(0.45, 0.24, 0.06) : vec3(0.10));
    CLEARCOAT = mode == 0 ? 0.28 : 0.10;
    CLEARCOAT_ROUGHNESS = 0.30;
    if (mode == 0) {
        float ridge = sin(uv.x * 380.0 + sin(uv.y * 30.0));
        float aa = 1.0 - smoothstep(0.4, 1.6, fwidth(uv.x) * 60.0);
        NORMAL = normalize(n + b * ridge * 0.045 * aa);
        AO = mix(0.78, 1.0, smoothstep(-0.8, 0.2, ridge));
    }
    if (mode == 4) {
        // Snell refraction through a convex tear-film into a recessed iris.
        // This refracts the iris lookup, not the scene behind an opaque eyeball.
        vec2 centered = UV - vec2(0.5);
        vec3 cornea_n = normalize(vec3(centered * 0.55, 1.0));
        vec3 incident = normalize(vec3(-dot(VIEW, b), -dot(VIEW, t), -max(dot(VIEW, n), 0.05)));
        vec3 ray = refract(incident, cornea_n, 1.0 / 1.376);
        float iris_depth = 0.045 + 0.025 * (1.0 - clamp(length(centered) * 2.0, 0.0, 1.0));
        vec2 iris_uv = centered + ray.xy * iris_depth / max(abs(ray.z), 0.15);
        float r = length(iris_uv);
        float pupil_radius = mix(0.22, 0.095, clamp(day_night, 0.0, 1.0));
        float theta = atan(iris_uv.y, iris_uv.x);
        float fibers = 0.80 + 0.12 * sin(theta * 93.0 + r * 130.0) + 0.08 * sin(theta * 157.0);
        vec3 iris = mix(vec3(0.16, 0.085, 0.025), col * 1.8, 0.45) * fibers;
        iris *= mix(1.0, 0.22, smoothstep(0.35, 0.46, r));
        ALBEDO = mix(vec3(0.003, 0.004, 0.006), iris, smoothstep(pupil_radius, pupil_radius + 0.012, r));
        ROUGHNESS = 0.04;
        SPECULAR = 0.85;
        METALLIC = 0.0;
        CLEARCOAT = 1.0;
        CLEARCOAT_ROUGHNESS = 0.01;
        SSS_STRENGTH = 0.0;
        BACKLIGHT = vec3(0.0);
    } else {
        ALBEDO *= mix(1.0, 0.55, surface_wetness);
        ROUGHNESS = mix(ROUGHNESS, 0.07, surface_wetness * 0.90);
        CLEARCOAT = max(CLEARCOAT, surface_wetness * 0.90);
        CLEARCOAT_ROUGHNESS = mix(CLEARCOAT_ROUGHNESS, 0.04, surface_wetness);
    }
}

float longitudinal_lobe(float sin_l, float sin_v, float shift, float width) {
    float theta_l = asin(clamp(sin_l, -0.999, 0.999));
    float theta_v = asin(clamp(sin_v, -0.999, 0.999));
    float d = (theta_l + theta_v) * 0.5 - shift;
    return exp(-0.5 * d * d / (width * width));
}

void light() {
    float nl = max(dot(NORMAL, LIGHT), 0.0);
    vec3 half_sum = LIGHT + VIEW;
    vec3 h = half_sum / max(length(half_sum), 0.0001);
    // Explicit tear/water/keratin film lobe: custom light() owns direct lighting.
    float film = mode == 4 ? 1.0 : max(mode == 0 ? 0.28 : 0.10, surface_wetness * 0.90);
    float film_roughness = mode == 4 ? 0.04 : mix(0.30, 0.07, surface_wetness);
    float film_a2 = pow(film_roughness, 4.0);
    float film_nh = max(dot(NORMAL, h), 0.0);
    float film_denom = film_nh * film_nh * (film_a2 - 1.0) + 1.0;
    float film_D = film_a2 / max(3.14159265 * film_denom * film_denom, 0.00000001);
    float film_nv = max(dot(NORMAL, VIEW), 0.001);
    float film_fresnel = 0.025 + 0.975 * pow(1.0 - max(dot(VIEW, h), 0.0), 5.0);
    float film_k = pow(film_roughness + 1.0, 2.0) / 8.0;
    float film_G = (nl / max(nl * (1.0 - film_k) + film_k, 0.001))
        * (film_nv / (film_nv * (1.0 - film_k) + film_k));
    SPECULAR_LIGHT += film * film_D * film_fresnel * film_G * LIGHT_COLOR * ATTENUATION
        / max(4.0 * film_nv, 0.001);
    if (mode == 1 || mode == 2) {
        vec3 tangent = normalize(fiber_tangent);
        float sl = dot(tangent, LIGHT);
        float sv = dot(tangent, VIEW);
        // Marschner-inspired longitudinal R / TT / TRT lobes, with energy-bounded
        // weights; this is a practical fiber approximation, not a spectral solver.
        float width = mix(0.15, 0.095, surface_wetness);
        float R = longitudinal_lobe(sl, sv, -0.10472, width);
        float TT = longitudinal_lobe(sl, sv, 0.05236, width * 1.5);
        float TRT = longitudinal_lobe(sl, sv, 0.15708, width * 0.85);
        vec3 lp = LIGHT - tangent * sl;
        vec3 vp = VIEW - tangent * sv;
        float azimuth = dot(lp, vp) / max(length(lp) * length(vp), 0.0001);
        float reflect_side = 0.5 + 0.5 * azimuth;
        float transmit_side = 1.0 - reflect_side;
        vec3 fiber_spec = vec3(0.16) * R * reflect_side
            + ALBEDO * 0.18 * TT * transmit_side
            + sqrt(max(ALBEDO, vec3(0.0))) * 0.10 * TRT * reflect_side;
        float cylinder_diffuse = sqrt(max(0.0, 1.0 - sl * sl));
        DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * cylinder_diffuse * (0.4 + 0.3 * nl) / 3.14159265;
        SPECULAR_LIGHT += fiber_spec * LIGHT_COLOR * ATTENUATION * (0.35 + 0.65 * nl);
    } else {
        // GGX for armor, skin and cornea; Godot LIGHT_COLOR already contains pi.
        float nv = max(dot(NORMAL, VIEW), 0.001);
        float nh = max(dot(NORMAL, h), 0.0);
        float vh = max(dot(VIEW, h), 0.0);
        float a = max(ROUGHNESS * ROUGHNESS, 0.0036);
        float a2 = a * a;
        float denom = nh * nh * (a2 - 1.0) + 1.0;
        float D = a2 / max(3.14159265 * denom * denom, 0.000001);
        float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) / 8.0;
        float G = (nl / max(nl * (1.0 - k) + k, 0.001)) * (nv / (nv * (1.0 - k) + k));
        vec3 f0 = mix(vec3(0.08 * specular), ALBEDO, METALLIC);
        vec3 F = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
        DIFFUSE_LIGHT += (1.0 - F) * (1.0 - METALLIC) * nl * LIGHT_COLOR * ATTENUATION / 3.14159265;
        if (mode != 4) SPECULAR_LIGHT += D * G * F * LIGHT_COLOR * ATTENUATION / max(4.0 * nv, 0.001);
        if (mode == 0 || mode == 3) {
            DIFFUSE_LIGHT += (mode == 0 ? vec3(0.12, 0.065, 0.02) : vec3(0.035))
                * max(-dot(NORMAL, LIGHT), 0.0) * LIGHT_COLOR * ATTENUATION / 3.14159265;
        }
    }
}
