shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D leaf_col : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_nrm : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_rgh : filter_linear_mipmap_anisotropic;
uniform sampler2D leaf_opacity : filter_linear_mipmap_anisotropic;
uniform bool twig_mode = false;
uniform bool duff_mode = false;
uniform float decay_strength : hint_range(0.0, 1.0) = 0.0;

varying vec3 vertex_tint;

vec3 litter_srgb_to_linear(vec3 c) {
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

float litter_hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float litter_noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(litter_hash(i), litter_hash(i + vec2(1.0, 0.0)), f.x),
        mix(litter_hash(i + vec2(0.0, 1.0)), litter_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void vertex() {
    vertex_tint = litter_srgb_to_linear(COLOR.rgb);
}

void fragment() {
    if (!FRONT_FACING) NORMAL = -NORMAL;

    if (twig_mode) {
        ALBEDO = vertex_tint;
        ROUGHNESS = 0.94;
        SPECULAR = 0.12;
    } else {
    vec3 scanned_leaf = texture(leaf_col, UV).rgb;
    float mask_luma = dot(scanned_leaf, vec3(0.2126, 0.7152, 0.0722));
    // The source atlas is a CC0 color scan on a near-black JPG background. Discard its gaps so
    // neither the ground nor the shadow pass receives a rectangular card silhouette.
    if (texture(leaf_opacity, UV).r < 0.5) discard;

    if (duff_mode) {
        float luma = dot(scanned_leaf, vec3(0.2126, 0.7152, 0.0722));
        vec3 subdued_leaf = mix(vec3(luma), scanned_leaf, 0.40);
        float mottling = litter_noise(UV * vec2(210.0, 170.0));
        float torn_patch = litter_noise(UV * vec2(510.0, 390.0) + vec2(8.4, 2.7));
        float decay = clamp(decay_strength, 0.0, 1.0);
        vec3 soil_stain = vec3(0.115, 0.073, 0.034) * (0.72 + mottling * 0.56);
        float patch_factor = mix(0.72, 1.08, torn_patch);
        ALBEDO = mix(subdued_leaf, soil_stain, decay * 0.58) * vertex_tint * patch_factor;
        ROUGHNESS = clamp(texture(leaf_rgh, UV).r + 0.18 * decay, 0.82, 1.0);
        NORMAL_MAP = texture(leaf_nrm, UV).rgb;
        NORMAL_MAP_DEPTH = 0.10;
        SPECULAR = 0.08;
        BACKLIGHT = ALBEDO * vec3(0.64, 0.43, 0.22) * 0.012;
        AO = 0.90;
        AO_LIGHT_AFFECT = 0.04;
    } else {
        float leaf_luma = dot(scanned_leaf, vec3(0.2126, 0.7152, 0.0722));
        ALBEDO = mix(vec3(leaf_luma), scanned_leaf, 0.68) * vertex_tint;
        ROUGHNESS = clamp(texture(leaf_rgh, UV).r, 0.76, 0.99);
        NORMAL_MAP = texture(leaf_nrm, UV).rgb;
        NORMAL_MAP_DEPTH = 0.18;
        SPECULAR = 0.17;
        BACKLIGHT = ALBEDO * vec3(0.76, 0.54, 0.30) * 0.035;
        AO = 0.96;
        AO_LIGHT_AFFECT = 0.08;
    }
    }
}
