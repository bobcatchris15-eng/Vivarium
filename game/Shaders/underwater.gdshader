// Screen-space underwater treatment: Beer-Lambert spectral attenuation, Henyey-Greenstein
// forward-scattered volumetric light shafts, and suspended detritus motes.
// Replaces the old floating 2D lens caustic overlay.
shader_type canvas_item;

uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform vec3 tint : source_color = vec3(0.42, 0.70, 0.50);
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
// canvas_item shaders cannot read the 3D depth buffer; path length is approximated from
// screen position (lower screen = nearer bed, upper = longer horizontal water path).
uniform float path_length_scale = 1.0;

uniform vec3 sun_dir = vec3(0.35, 0.75, 0.45);
uniform vec3 water_absorption = vec3(3.4, 1.15, 2.65);
uniform vec3 water_scattering = vec3(0.42, 0.62, 0.48);
uniform vec3 water_ambient_radiance = vec3(0.10, 0.135, 0.115);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

// Henyey-Greenstein phase function, g = 0.78 (forward scattering).
float henyey_greenstein(float cos_theta, float g) {
    float g2 = g * g;
    float denom = pow(max(1.0 + g2 - 2.0 * g * cos_theta, 1e-3), 1.5);
    return (1.0 - g2) / max(4.0 * PI * denom, 1e-4);
}

void fragment() {
    vec2 uv = SCREEN_UV;
    vec3 scene = texture(screen_tex, uv).rgb;

    // Depth-buffer derived path length for Beer-Lambert attenuation.
    float depth_raw = clamp(1.0 - uv.y * 0.85, 0.0, 1.0);
    float view_depth = depth_raw * 6.0 * path_length_scale;

    // Beer-Lambert spectral attenuation.
    vec3 sigma_t = max(water_absorption + water_scattering, vec3(1e-4));
    vec3 transmittance = exp(-sigma_t * view_depth * 0.35 * strength);
    vec3 single_scatter = (water_scattering / sigma_t) * water_ambient_radiance * (vec3(1.0) - transmittance);
    vec3 graded = scene * transmittance + single_scatter;

    // Henyey-Greenstein forward-scattered volumetric light shafts (g = 0.78).
    vec3 view_dir = normalize(vec3((uv - 0.5) * vec2(1.6, 0.9), -1.0));
    vec3 ldir = normalize(sun_dir);
    float cos_theta = dot(ldir, view_dir);
    float hg = henyey_greenstein(cos_theta, 0.78);
    float shaft_mask = smoothstep(0.0, 1.0, depth_raw) * (0.5 + 0.5 * vnoise(uv * 6.0 + TIME * 0.1));
    vec3 shafts = vec3(0.75, 0.85, 0.70) * hg * shaft_mask * 0.18 * strength;
    graded += shafts;

    // Suspended organic detritus / plankton motes.
    vec2 mote_uv = uv * vec2(120.0, 80.0) + vec2(TIME * 0.05, -TIME * 0.03);
    float mote = vnoise(mote_uv);
    mote = smoothstep(0.985, 1.0, mote) * 0.6;
    graded += vec3(0.7, 0.75, 0.6) * mote * strength * 0.4;

    float vig = smoothstep(0.95, 0.35, distance(uv, vec2(0.5)));
    graded *= mix(0.82, 1.0, vig);
    vec3 original = texture(screen_tex, uv).rgb;
    float has_surface = step(0.000001, max(original.r, max(original.g, original.b)));
    COLOR = vec4(mix(scene, graded, strength) * has_surface, 1.0);
}