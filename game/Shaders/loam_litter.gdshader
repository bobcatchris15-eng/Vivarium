shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D leaf_col : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_nrm : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_rgh : filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_opacity : filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_trans : source_color, filter_linear_mipmap_anisotropic;

uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : filter_linear_mipmap_anisotropic, repeat_enable;

uniform bool twig_mode = false;
uniform bool duff_mode = false;
uniform float decay_strength : hint_range(0.0, 1.0) = 0.0;

varying vec3 vertex_tint;
varying vec2 v_uv2;

vec3 litter_srgb_to_linear(vec3 c) {
    c = max(c, vec3(0.0));
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

void vertex() {
    vertex_tint = litter_srgb_to_linear(COLOR.rgb);
    v_uv2 = UV2;
}

void fragment() {
    if (!FRONT_FACING) NORMAL = -NORMAL;

    if (twig_mode || v_uv2.x >= 0.5) {
        if (v_uv2.x >= 1.5) {
            // Wood end-grain on severed twig tips
            vec3 end_wood = texture(bark_col, UV * 2.0).rgb * vec3(1.15, 0.96, 0.78) * vertex_tint;
            ALBEDO = end_wood;
            ROUGHNESS = 0.84;
            SPECULAR = 0.14;
        } else {
            // 8-sided bark-mapped twig cylinder
            vec3 bark = texture(bark_col, UV).rgb * vertex_tint;
            ALBEDO = bark;
            NORMAL_MAP = texture(bark_nrm, UV).rgb;
            NORMAL_MAP_DEPTH = 0.35;
            ROUGHNESS = clamp(texture(bark_rgh, UV).r, 0.70, 0.98);
            SPECULAR = 0.16;
        }
    } else {
        if (texture(leaf_opacity, UV).r < 0.5) discard;

        vec3 scanned_leaf = texture(leaf_col, UV).rgb;
        float rgh = texture(leaf_rgh, UV).r;
        vec3 nrm = texture(leaf_nrm, UV).rgb;
        vec3 trans = texture(leaf_trans, UV).rgb;

        if (duff_mode) {
            float decay = clamp(decay_strength, 0.0, 1.0);
            vec3 soil_stain = vec3(0.115, 0.073, 0.034);
            ALBEDO = mix(scanned_leaf, soil_stain, decay * 0.45) * vertex_tint;
            ROUGHNESS = clamp(rgh + 0.15 * decay, 0.80, 1.0);
            NORMAL_MAP = nrm;
            NORMAL_MAP_DEPTH = 0.15;
            SPECULAR = 0.10;
            BACKLIGHT = trans * 0.22;
        } else {
            ALBEDO = scanned_leaf * vertex_tint;
            ROUGHNESS = clamp(rgh, 0.68, 0.96);
            NORMAL_MAP = nrm;
            NORMAL_MAP_DEPTH = 0.30;
            SPECULAR = 0.18;
            BACKLIGHT = trans * 0.55;
        }

        // Moisture-driven wetting: clearcoat and internal reflection darkening
        float wet = clamp(COLOR.a, 0.0, 1.0);
        CLEARCOAT = 0.75 * smoothstep(0.40, 0.92, wet);
        CLEARCOAT_ROUGHNESS = 0.08;
        ALBEDO = mix(ALBEDO, ALBEDO * 0.48, smoothstep(0.35, 0.95, wet));
    }
}
