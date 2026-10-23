// Scanned loam surface for low, partly buried geometry. UV carries world XZ metres.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float soil_texture_tile_metres = 0.82;
uniform vec3 soil_tint = vec3(0.65, 0.78, 0.90);
uniform float soil_specular = 0.22;

void fragment() {
    vec2 uv = UV / max(soil_texture_tile_metres, 0.2);
    vec3 scan = texture(soil_col, uv).rgb;
    float scan_roughness = texture(soil_rgh, uv).r;
    ALBEDO = clamp(scan * soil_tint * COLOR.rgb, vec3(0.018), vec3(0.18));
    ROUGHNESS = clamp(mix(0.84, scan_roughness, 0.18), 0.60, 0.99);
    METALLIC = 0.0;
    SPECULAR = soil_specular;
}
