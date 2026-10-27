shader_type spatial;
render_mode cull_disabled, blend_mix, depth_draw_opaque;
void fragment() {
    float rim = pow(1.0 - max(dot(normalize(NORMAL), normalize(VIEW)), 0.0), 3.0);
    ALBEDO = vec3(0.65, 0.78, 0.73);
    ROUGHNESS = 0.045;
    SPECULAR = 0.8;
    CLEARCOAT = 1.0;
    CLEARCOAT_ROUGHNESS = 0.02;
    ALPHA = 0.06 + rim * 0.65;
}
