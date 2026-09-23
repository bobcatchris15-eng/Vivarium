// Flora instances: baked vertex colours per species, INSTANCE_CUSTOM = (tint variation, health, wobble, seed).
// Sway and poke wobble are purely cosmetic.
shader_type spatial;
render_mode diffuse_burley, cull_disabled;

uniform float sway = 1.0;
uniform float stiffness = 1.0;
varying vec4 inst;

void vertex() {
    inst = INSTANCE_CUSTOM;
    float h = max(VERTEX.y, 0.0);
    float phase = TIME * 1.3 + inst.w * 6.2831;
    float amount = (0.015 * sway + inst.z * 0.15) / stiffness;
    VERTEX.x += sin(phase) * h * h * amount * 3.0 + sin(TIME * 18.0 + inst.w * 40.0) * inst.z * h * 0.2;
    VERTEX.z += cos(phase * 0.8) * h * h * amount * 2.0;
}

void fragment() {
    vec3 c = COLOR.rgb * (1.0 + (inst.x - 0.5) * 0.3);
    float health = clamp(inst.y, 0.0, 1.0);
    vec3 faded = mix(vec3(dot(c, vec3(0.33))), vec3(0.62, 0.55, 0.32), 0.5);
    ALBEDO = mix(faded, c, 0.35 + 0.65 * health);
    ROUGHNESS = 0.75;
    SPECULAR = 0.25;
    BACKLIGHT = c * 0.25;
}
