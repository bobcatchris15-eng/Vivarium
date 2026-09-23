// Specimen cut faces. Layer colours come from content (vertex COLOR per band); this shader adds grain,
// banding and a crisp boundary line so the section reads as a deliberate cut, not missing geometry.
// UV = (distance along perimeter, depth below surface); UV2 = (layer index, depth within layer).
shader_type spatial;
render_mode diffuse_burley;

uniform float grain_strength = 0.35;
uniform float banding_strength = 0.3;
uniform vec3 rim_color : source_color = vec3(0.22, 0.30, 0.12);

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void fragment() {
    float along = UV.x, depth = UV.y, layer = UV2.x, inLayer = UV2.y;
    vec3 col = COLOR.rgb;
    // speckle / pebbles scale with layer index (coarser deeper down)
    float g = hash(floor(vec2(along, depth) * (90.0 - layer * 15.0)));
    float pebble = step(0.93 - layer * 0.03, vnoise(vec2(along, depth) * (14.0 + layer * 6.0)));
    col *= 0.88 + grain_strength * (g - 0.5) * 0.6;
    col = mix(col, col * 1.35 + 0.08, pebble * 0.55 * min(layer, 2.0) * 0.5);
    // horizontal sedimentary banding, gently wavy
    float band = sin(depth * 38.0 + vnoise(vec2(along * 0.8, depth * 3.0)) * 3.0);
    col *= 1.0 + banding_strength * 0.12 * band * min(layer + 0.5, 2.0);
    // darker line at each layer's top edge
    col *= mix(0.72, 1.0, smoothstep(0.0, 0.025, depth - 0.0) * smoothstep(0.0, 0.03, inLayer + step(0.5, layer) * 0.0 + (layer < 0.5 ? 1.0 : 0.0) * 1.0) );
    // living root mat right under the surface
    col = mix(rim_color, col, smoothstep(0.0, 0.05, depth));
    // bottom face (normal pointing down) shades darker
    ALBEDO = col;
    ROUGHNESS = 0.95;
    SPECULAR = 0.2;
}
