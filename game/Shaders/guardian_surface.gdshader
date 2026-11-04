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
varying float surface_specular;

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
    vec3 geom_n = normalize(NORMAL);
    vec3 n = geom_n;
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
        // skin; keep texture UVs unshifted so atlas seams never bleed across cards.
        vec2 strand_uv = uv;
        vec2 view_slope = vec2(dot(VIEW, b), dot(VIEW, t)) / max(dot(VIEW, n), 0.3);
        for (int i = 0; i < 3; i++) {
            strand_uv -= view_slope * (1.0 - strand_height(strand_uv)) * 0.00035;
        }
        float frequency = strand_density * 6.2831853;
        float antialias = 1.0 - smoothstep(0.4, 1.6, fwidth(uv.x) * strand_density);
        float ripple = cos(strand_uv.x * frequency + sin(strand_uv.y * 47.0) * 0.8) * antialias;
        n = normalize(n + b * ripple * mix(0.075, 0.025, surface_wetness));
    }
    if (has_normal_texture) {
        // Godot RGTC normal textures store X and Y in .rg with .b = 0.0.
        // Reconstruct positive Z from XY so the surface normal never inverts.
        vec2 map_xy = texture(normal_texture, uv).rg * 2.0 - 1.0;
        vec3 map_normal = vec3(map_xy * 0.72, sqrt(max(0.0, 1.0 - dot(map_xy, map_xy))));
        vec3 dpx = dFdx(VERTEX);
        vec3 dpy = dFdy(VERTEX);
        vec2 duvx = dFdx(uv);
        vec2 duvy = dFdy(uv);
        vec3 r1 = cross(dpy, n);
        vec3 r2 = cross(n, dpx);
        vec3 uv_t = r1 * duvx.x + r2 * duvy.x;
        vec3 uv_b = r1 * duvx.y + r2 * duvy.y;
        float uv_scale = max(dot(uv_t, uv_t), dot(uv_b, uv_b));
        if (uv_scale > 1e-10 && uv_scale < 1e10) {
            float inv_max = inversesqrt(uv_scale);
            n = normalize(n * map_normal.z + (uv_t * map_normal.x - uv_b * map_normal.y) * inv_max);
        } else {
            n = normalize(b * map_normal.x + t * map_normal.y + n * map_normal.z);
        }
    }
    if (mode == 0) {
        float ridge = sin(uv.x * 380.0 + sin(uv.y * 30.0));
        float aa = 1.0 - smoothstep(0.4, 1.6, fwidth(uv.x) * 60.0);
        n = normalize(n + b * ridge * 0.035 * aa);
        AO = mix(0.82, 1.0, smoothstep(-0.8, 0.2, ridge));
    }
    // Keep perturbed normals in the valid geometric/view hemisphere so grazing
    // normal-map slopes cannot trigger 100% Schlick Fresnel mirror halos.
    if (dot(n, geom_n) < 0.25) {
        n = normalize(mix(geom_n, n, 0.45));
    }
    fiber_tangent = normalize(t - n * dot(t, n));
    NORMAL = n;
    vec3 col = base_color * vertex_color;
    if (has_albedo_texture) col *= texture(albedo_texture, uv).rgb;
    float rgh = roughness_mul;
    if (has_roughness_texture) rgh *= texture(roughness_texture, uv).r;
    // Geometric / normal-map variance roughness filtering (Toksvig) to prevent specular aliasing.
    vec3 dnx = dFdx(n);
    vec3 dny = dFdy(n);
    float normal_var = dot(dnx, dnx) + dot(dny, dny);
    rgh = sqrt(rgh * rgh + min(normal_var * 0.45, 0.16));
    float min_rgh = mode == 0 ? 0.24 : (mode == 3 ? 0.38 : 0.54);
    ALBEDO = col;
    ROUGHNESS = clamp(rgh, min_rgh, 1.0);
    METALLIC = 0.0;
    SPECULAR = specular * (mode == 0 ? 0.80 : (mode == 3 ? 0.52 : 0.32));
    SSS_STRENGTH = mode == 0 ? 0.55 : (mode == 3 ? 0.22 : 0.16);
    BACKLIGHT = col * (mode == 0 ? vec3(0.38, 0.20, 0.05) : (mode == 3 ? vec3(0.18, 0.08, 0.05) : vec3(0.08)));
    CLEARCOAT = mode == 0 ? 0.12 : 0.0;
    CLEARCOAT_ROUGHNESS = 0.28;
    if (mode == 4) {
        // Snell refraction through a convex tear-film into a recessed iris.
        // Supports textured eyes (RedIris), dedicated dark pupil meshes, polar UV caps, and planar UVs.
        float base_luma = dot(base_color, vec3(0.2126, 0.7152, 0.0722));
        float vtx_luma = dot(vertex_color, vec3(0.2126, 0.7152, 0.0722));
        if (has_albedo_texture) {
            vec2 centered = UV - vec2(0.5);
            vec3 cornea_n = normalize(vec3(centered * 0.55, 1.0));
            vec3 incident = normalize(vec3(-dot(VIEW, b), -dot(VIEW, t), -max(dot(VIEW, n), 0.05)));
            vec3 ray = refract(incident, cornea_n, 1.0 / 1.376);
            vec2 refr_uv = clamp(UV + ray.xy * 0.03, vec2(0.002), vec2(0.998));
            ALBEDO = base_color * vertex_color * texture(albedo_texture, refr_uv).rgb;
        } else if (base_luma < 0.025 && vtx_luma > 0.95) {
            // Dedicated pupil surface (e.g., BoaPupil, Eye pupil, GlossEye): keep deep obsidian absorption.
            ALBEDO = vec3(0.003, 0.004, 0.006);
        } else if (vtx_luma < 0.92) {
            // Vertex-painted eye mesh (DarkEye, ThylacineEye, EyeCornea, AmberIris):
            // pupil and iris gradient are authored in vertex_color in 3D space.
            vec2 centered = UV - vec2(0.5);
            float theta = atan(centered.y, centered.x);
            float r = length(centered);
            float fibers = 0.86 + 0.09 * sin(theta * 93.0 + r * 130.0) + 0.05 * sin(theta * 157.0);
            vec3 iris_tone = max(base_color * 2.5, vec3(0.58, 0.34, 0.12));
            vec3 iris = iris_tone * vertex_color * fibers;
            float pupil_mask = smoothstep(0.025, 0.085, vtx_luma);
            ALBEDO = mix(vec3(0.003, 0.004, 0.006), iris, pupil_mask);
        } else {
            // Polar UV cornea caps encode angle in UV.x and radius from pole in UV.y (0..0.33).
            bool polar_uv = UV.y < 0.34 && abs(UV.x - 0.5) > 0.18;
            vec2 centered = polar_uv
                ? vec2(cos(UV.x * 6.2831853), sin(UV.x * 6.2831853)) * (UV.y * 1.42)
                : (UV - vec2(0.5));
            vec3 cornea_n = normalize(vec3(centered * 0.55, 1.0));
            vec3 incident = normalize(vec3(-dot(VIEW, b), -dot(VIEW, t), -max(dot(VIEW, n), 0.05)));
            vec3 ray = refract(incident, cornea_n, 1.0 / 1.376);
            float iris_depth = 0.045 + 0.025 * (1.0 - clamp(length(centered) * 2.0, 0.0, 1.0));
            vec2 iris_uv = centered + ray.xy * iris_depth / max(abs(ray.z), 0.15);
            float r = length(iris_uv);
            float pupil_radius = mix(0.20, 0.105, clamp(day_night, 0.0, 1.0));
            float theta = atan(iris_uv.y, iris_uv.x);
            float fibers = 0.84 + 0.10 * sin(theta * 93.0 + r * 130.0) + 0.06 * sin(theta * 157.0);
            vec3 pigment = max(col * 1.65, vertex_color * vec3(0.34, 0.19, 0.065));
            vec3 iris = mix(vec3(0.18, 0.095, 0.03), pigment, 0.65) * fibers;
            iris *= mix(1.0, 0.20, smoothstep(0.34, 0.46, r));
            ALBEDO = mix(vec3(0.003, 0.004, 0.006), iris, smoothstep(pupil_radius, pupil_radius + 0.015, r));
        }
        ROUGHNESS = 0.04;
        SPECULAR = 0.45;
        METALLIC = 0.0;
        CLEARCOAT = 0.35;
        CLEARCOAT_ROUGHNESS = 0.02;
        SSS_STRENGTH = 0.0;
        BACKLIGHT = vec3(0.0);
    } else {
        ALBEDO *= mix(1.0, 0.58, surface_wetness);
        ROUGHNESS = mix(ROUGHNESS, 0.08, surface_wetness * 0.88);
        CLEARCOAT = max(CLEARCOAT, surface_wetness * 0.85);
        CLEARCOAT_ROUGHNESS = mix(CLEARCOAT_ROUGHNESS, 0.04, surface_wetness);
    }
    surface_specular = SPECULAR;
}

float longitudinal_lobe(float sin_l, float sin_v, float shift, float width) {
    float theta_l = asin(clamp(sin_l, -0.999, 0.999));
    float theta_v = asin(clamp(sin_v, -0.999, 0.999));
    float d = (theta_l + theta_v) * 0.5 - shift;
    return exp(-0.5 * d * d / (width * width));
}

void light() {
    float raw_nl = dot(NORMAL, LIGHT);
    float nl = max(raw_nl, 0.0);
    vec3 half_sum = LIGHT + VIEW;
    vec3 h = half_sum / max(length(half_sum), 0.0001);
    float nv = max(dot(NORMAL, VIEW), 0.01);
    float vh = max(dot(VIEW, h), 0.0);
    // Explicit tear/water/keratin film lobe: active on corneal eyes, wet coats, and lightly on keratin.
    float dry_film = mode == 4 ? 1.0 : (mode == 0 ? 0.12 : 0.0);
    float film = max(dry_film, surface_wetness * 0.85);
    float film_fresnel = 0.04 + 0.96 * pow(1.0 - vh, 5.0);
    if (film > 0.001 && nl > 0.0) {
        float film_roughness = mode == 4 ? 0.035 : mix(0.28, 0.06, surface_wetness);
        float film_a = max(film_roughness * film_roughness, 0.0012);
        float film_a2 = film_a * film_a;
        float film_nh = max(dot(NORMAL, h), 0.0);
        float film_denom = film_nh * film_nh * (film_a2 - 1.0) + 1.0;
        float film_D = film_a2 / max(3.14159265 * film_denom * film_denom, 0.00001);
        float film_k = (film_roughness + 1.0) * (film_roughness + 1.0) / 8.0;
        float film_G = (nl / max(nl * (1.0 - film_k) + film_k, 0.001))
            * (nv / (nv * (1.0 - film_k) + film_k));
        SPECULAR_LIGHT += film * film_D * film_fresnel * film_G * nl * LIGHT_COLOR * ATTENUATION
            / max(4.0 * nv * nl, 0.001);
    }
    float base_transmission = 1.0 - film * film_fresnel;
    if (mode == 1 || mode == 2) {
        vec3 tangent = normalize(fiber_tangent);
        float sl = dot(tangent, LIGHT);
        float sv = dot(tangent, VIEW);
        // Marschner-inspired longitudinal R / TRT lobes scaled by surface roughness and normalized by PI.
        float width = mix(0.28, 0.46, ROUGHNESS) * mix(1.0, 0.65, surface_wetness);
        float R = longitudinal_lobe(sl, sv, -0.08727, width);
        float TRT = longitudinal_lobe(sl, sv, 0.1309, width * 1.15);
        vec3 lp = LIGHT - tangent * sl;
        vec3 vp = VIEW - tangent * sv;
        float azimuth = dot(lp, vp) / max(length(lp) * length(vp), 0.0001);
        float reflect_side = 0.5 + 0.5 * azimuth;
        float sheen_atten = (1.0 - 0.62 * ROUGHNESS) * base_transmission;
        vec3 fiber_spec = (vec3(0.055) * R * reflect_side
            + mix(vec3(0.025), ALBEDO * 0.04, 0.5) * TRT * reflect_side) * sheen_atten;
        float cylinder_diffuse = sqrt(max(0.0, 1.0 - sl * sl));
        float wrap_nl = clamp((raw_nl + 0.22) / 1.22, 0.0, 1.0);
        float coat_diffuse = mix(nl, cylinder_diffuse * wrap_nl, 0.42);
        DIFFUSE_LIGHT += base_transmission * LIGHT_COLOR * ATTENUATION * coat_diffuse / 3.14159265;
        SPECULAR_LIGHT += fiber_spec * LIGHT_COLOR * ATTENUATION * nl / 3.14159265;
    } else {
        // GGX for armor, skin and cornea; Godot LIGHT_COLOR already contains pi.
        float nh = max(dot(NORMAL, h), 0.0);
        float a = max(ROUGHNESS * ROUGHNESS, 0.0036);
        float a2 = a * a;
        float denom = nh * nh * (a2 - 1.0) + 1.0;
        float D = a2 / max(3.14159265 * denom * denom, 0.00001);
        float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) / 8.0;
        float G = (nl / max(nl * (1.0 - k) + k, 0.001)) * (nv / (nv * (1.0 - k) + k));
        vec3 f0 = vec3(0.08 * surface_specular);
        vec3 F = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
        DIFFUSE_LIGHT += base_transmission * (1.0 - F) * nl * LIGHT_COLOR * ATTENUATION / 3.14159265;
        if (mode != 4 && nl > 0.0) {
            SPECULAR_LIGHT += base_transmission * D * G * F * nl * LIGHT_COLOR * ATTENUATION / max(4.0 * nv * nl, 0.001);
        }
        if (mode == 0 || mode == 3) {
            float sss_wrap = max(0.0, (raw_nl + 0.35) / 1.35) * (1.0 - nl);
            vec3 sss_tint = mode == 0 ? vec3(0.14, 0.075, 0.025) : vec3(0.12, 0.045, 0.03);
            DIFFUSE_LIGHT += base_transmission * sss_tint
                * (sss_wrap + 0.65 * max(-raw_nl, 0.0)) * LIGHT_COLOR * ATTENUATION / 3.14159265;
        }
    }
}
