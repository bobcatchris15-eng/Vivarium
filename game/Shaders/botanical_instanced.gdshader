shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec4 anatomical_color : source_color = vec4(0.36, 0.58, 0.29, 1.0);
uniform float waxiness : hint_range(0.0, 1.0) = 0.45;
uniform float transmitted_light : hint_range(0.0, 1.0) = 0.17;
uniform bool clip_to_hex = false;
uniform float hex_apothem = 6.92820323;
varying vec3 world_position;
varying float organ_pigment;
varying float tissue_margin;

// COLOR + INSTANCE_CUSTOM are both individualized stable fingerprint channels.
// All input GLBs remain Blender-authored source meshes; this only makes one-shot
// physical deformations inexpensive enough to perform per-instance.
void vertex() {
    float u = clamp(UV.x, 0.0, 1.0);
    float v = clamp(UV.y * 2.0 - 1.0, -1.0, 1.0);
    float mid = u * u * (3.0 - 2.0 * u);
    float lobe = pow(sin(3.14159265 * u), 2.0);
    float elongate = mix(0.925, 1.075, COLOR.r);
    float left_expand = mix(0.90, 1.10, COLOR.g);
    float right_expand = mix(0.90, 1.10, COLOR.b);
    float curl = mix(-0.06, 0.06, COLOR.a);
    float roll = mix(-0.16, 0.16, INSTANCE_CUSTOM.r);
    float apex_shift = mix(-0.045, 0.045, INSTANCE_CUSTOM.g);
    float petiole_lean = mix(-0.13, 0.13, INSTANCE_CUSTOM.b);

    // Authored lengths are in metres. The local position X is the anatomical
    // longitudinal axis; Z is the lamina breadth. Scattering is not noise.
    float span = max(abs(VERTEX.x), 0.01);
    float width = max(abs(VERTEX.z), 0.002);
    float lateral = VERTEX.z < 0.0 ? left_expand : right_expand;
    VERTEX.x = VERTEX.x * elongate + apex_shift * mid * span;
    VERTEX.z = VERTEX.z * lateral + curl * lobe * span + v * lobe * width * 0.018;
    VERTEX.y += roll * lobe * (VERTEX.z * VERTEX.z) / width
              + petiole_lean * u * span * 0.13;
    organ_pigment = INSTANCE_CUSTOM.a;
    tissue_margin = abs(v);
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    if (clip_to_hex) {
        float d = max(abs(world_position.z),
            0.8660254037844386 * abs(world_position.x) + .5 * abs(world_position.z))
            - hex_apothem;
        if (d > .001) discard;
    }
    float pigment = mix(0.84, 1.16, organ_pigment);
    float rim = smoothstep(0.70, 1.0, tissue_margin);
    vec3 tissue = anatomical_color.rgb * pigment;
    tissue *= mix(1.03, 0.86, rim * (0.20 + waxiness * 0.35));
    ALBEDO = tissue;
    ROUGHNESS = mix(0.85, 0.35, waxiness);
    SPECULAR = mix(0.24, 0.47, waxiness);
    // Very low diffuse backlit contribution; never turn leaves into emissive cards.
    BACKLIGHT = tissue * transmitted_light;
}
