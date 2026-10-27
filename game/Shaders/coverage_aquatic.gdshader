shader_type spatial;
render_mode diffuse_burley, cull_disabled;
uniform float water_table = 0.0;
varying float tissue;
varying vec3 world_pos;
void vertex() {
    tissue = UV2.x;
    if (tissue < 1.5 || tissue > 3.5) VERTEX.y = water_table + UV2.y;
    if ((tissue > 0.5 && tissue < 2.5) || tissue > 3.5) {
        float flex = tissue > 3.5 ? 1.0 : 0.25;
        vec2 current = UV;
        float speed = min(length(current), 1.0);
        vec2 direction = speed > 0.00001 ? current / speed : vec2(0.7, 0.3);
        // Coverage is stationary; only fauna and water have ambient motion.
        float phase = -VERTEX.x * 60.0 - VERTEX.z * 40.0;
        VERTEX.xz += direction * sin(phase) * (0.0003 + speed * 0.0007) * flex;
        // All floating filament nodes move within their original submergence band.
        NORMAL = normalize(NORMAL + vec3(direction.x, 0.0, direction.y) * cos(phase) * 0.08 * flex);
    }
    world_pos = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz;
}
void fragment() {
    if (!FRONT_FACING) NORMAL = -NORMAL;
    ALBEDO = COLOR.rgb;
    ROUGHNESS = 0.22;
    SPECULAR = 0.50;
    BACKLIGHT = COLOR.rgb * 0.65;
    SSS_STRENGTH = 0.35;
    if (tissue > 0.5 && tissue < 2.5) {
        ROUGHNESS = tissue > 1.5 ? 0.72 : 0.48;
        float velvet = 0.90 + 0.10 * sin(world_pos.x * 2300.0) * sin(world_pos.z * 1900.0);
        ALBEDO *= velvet;
    } else if (tissue > 2.5 && tissue < 3.5) {
        ALBEDO = vec3(0.35,0.48,0.38);
        METALLIC = 0.0;
        ROUGHNESS = 0.025;
        SPECULAR = 0.95;
        CLEARCOAT = 1.0;
        CLEARCOAT_ROUGHNESS = 0.02;
        BACKLIGHT = vec3(0.15);
    } else if (tissue > 3.5) {
        // Thin translucent tissue keeps opaque depth writes to avoid water alpha sorting.
        ALBEDO *= vec3(0.70,0.94,0.62);
        ROUGHNESS = 0.30;
        BACKLIGHT = vec3(0.22,0.52,0.08);
        SSS_STRENGTH = 0.75;
        SSS_TRANSMITTANCE_COLOR = vec4(0.25,0.72,0.12,1.0);
        SSS_TRANSMITTANCE_DEPTH = 0.002;
    }
}
