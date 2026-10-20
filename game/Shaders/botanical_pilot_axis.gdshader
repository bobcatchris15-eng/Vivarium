shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// The old-growth cambial crown joins the authoritative terrain-aware pilot
// trunk/root, but is still clipped to the exact same hexagonal world boundary.
uniform vec4 bark_color : source_color = vec4(0.28, 0.24, 0.18, 1.0);
uniform float hex_apothem = 6.92820323;
varying vec3 world_pos;
varying vec3 local_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    local_pos = VERTEX;
}
void fragment() {
    float d = max(abs(world_pos.z),
        0.8660254037844386 * abs(world_pos.x) + .5 * abs(world_pos.z))
        - hex_apothem;
    if(d > .001) discard;
    float ridges = sin(local_pos.y * 33.0 + sin(local_pos.x * 14.0 +
        local_pos.z * 18.0) * .9);
    vec3 bark = bark_color.rgb * mix(.83, 1.13, ridges * .5 + .5);
    ALBEDO = bark;
    ROUGHNESS = .93;
    SPECULAR = .18;
}
