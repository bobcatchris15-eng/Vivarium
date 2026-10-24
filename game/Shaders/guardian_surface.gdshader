// Guardian unified surface shader (WP-24).
// Modes: 0=KeratinScale, 1=MarschnerFur, 2=BristlePlumage, 3=LeatheryHide, 4=RefractiveEye.
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
uniform float wetness = 0.0;
uniform float breathing_phase = 0.0;
uniform float water_level = 0.0;
uniform float day_night = 1.0;
uniform vec3 contact_sphere = vec3(0.0, 0.0, 0.0);
uniform float contact_radius = 0.0;

varying vec3 world_pos;
varying vec3 vertex_color;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vertex_color = COLOR.rgb;
    if (mode == 1 || mode == 2 || mode == 3) {
        float pulse = sin(breathing_phase) * 0.003;
        VERTEX += NORMAL * pulse;
    }
    if (contact_radius > 0.001) {
        vec3 wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        vec3 d = wpos - contact_sphere;
        float dist = length(d);
        if (dist < contact_radius && dist > 0.0001) {
            float push = (contact_radius - dist) * 0.5;
            VERTEX += (inverse(MODEL_MATRIX) * vec4(normalize(d) * push, 0.0)).xyz;
        }
    }
}

void fragment() {
    vec3 col = base_color * vertex_color;
    if (has_albedo_texture) {
        col *= texture(albedo_texture, UV).rgb;
    }
    float rgh = roughness_mul;
    if (has_roughness_texture) {
        rgh *= texture(roughness_texture, UV).r;
    }
    rgh = clamp(rgh, 0.02, 1.0);

    vec3 nrm = NORMAL;
    if (has_normal_texture) {
        vec3 t = texture(normal_texture, UV).rgb * 2.0 - 1.0;
        nrm = normalize(TANGENT * t.x + BINORMAL * t.y + NORMAL * t.z);
    }

    ALBEDO = col;
    ROUGHNESS = rgh;
    METALLIC = metallic;
    SPECULAR = specular;
    NORMAL = normalize(nrm);

    if (mode == 0) {
        SSS_STRENGTH = 0.65;
        BACKLIGHT = col * vec3(0.45, 0.24, 0.06);
        CLEARCOAT = 0.28;
        CLEARCOAT_ROUGHNESS = 0.30;
        RIM = 0.35;
        RIM_TINT = 0.68;
    } else if (mode == 1) {
        SSS_STRENGTH = 0.25;
        ANISOTROPY = 0.75;
        CLEARCOAT = 0.12;
        CLEARCOAT_ROUGHNESS = 0.35;
        RIM = 0.14;
        RIM_TINT = 0.65;
    } else if (mode == 2) {
        SSS_STRENGTH = 0.20;
        ANISOTROPY = 0.60;
        CLEARCOAT = 0.18;
        CLEARCOAT_ROUGHNESS = 0.22;
        RIM = 0.28;
        RIM_TINT = 0.70;
    } else if (mode == 3) {
        SSS_STRENGTH = 0.15;
        CLEARCOAT = 0.10;
        CLEARCOAT_ROUGHNESS = 0.40;
        RIM = 0.10;
        RIM_TINT = 0.50;
    } else {
        ALBEDO = col * 0.15;
        ROUGHNESS = 0.04;
        SPECULAR = 0.85;
        METALLIC = 0.0;
        CLEARCOAT = 1.0;
        CLEARCOAT_ROUGHNESS = 0.01;
        SSS_STRENGTH = 0.0;
    }

    if (mode == 4) {
        float pupil = mix(0.35, 0.12, day_night);
        vec2 c = UV - vec2(0.5);
        float r = length(c);
        if (r < pupil) {
            ALBEDO = vec3(0.01, 0.01, 0.02);
        }
        float limbal = smoothstep(0.38, 0.46, r) * (1.0 - smoothstep(0.46, 0.52, r));
        ALBEDO *= (1.0 - limbal * 0.6);
    }

    if (wetness > 0.01) {
        ALBEDO *= mix(vec3(1.0), vec3(0.55), wetness);
        ROUGHNESS = mix(ROUGHNESS, 0.04, wetness * 0.9);
        CLEARCOAT = max(CLEARCOAT, wetness * 0.9);
        CLEARCOAT_ROUGHNESS = min(CLEARCOAT_ROUGHNESS, mix(CLEARCOAT_ROUGHNESS, 0.04, wetness));
        if (mode == 1 || mode == 2) {
            NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), wetness * 0.3));
        }
    }
}
