// Dedicated Physarum polycephalum plasmodium shader (docs/overhaul/growth_models.md §6).
// Renders an authentic living slime mold: anastomosing tubular vein cords with open bays,
// advancing lobate pseudopodial foraging fan apron, rhythmic shuttle-streaming peristaltic
// protoplasmic pulse, rounded cylindrical cord normal maps, and glistening amoebozoan translucency.
shader_type spatial;
render_mode diffuse_burley, cull_disabled;

#include "res://Shaders/surface.gdshaderinc"

uniform vec3 vein_core_color = vec3(0.92, 0.58, 0.06);    // Deep rich warm golden amber
uniform vec3 vein_sheath_color = vec3(0.98, 0.84, 0.16);  // Bright luminous canary yellow
uniform vec3 front_fan_color = vec3(0.99, 0.88, 0.22);    // Active exploratory front yellow
uniform float pulse_speed = 2.4;                          // Peristaltic shuttle streaming frequency
uniform float vein_scale = 15.0;                          // Spatial frequency of vein loops (~6.5cm bays)

varying vec3 world_pos;
varying vec3 vert_normal;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vert_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

// 2D cellular Voronoi returning vec2(F1, F2 - F1)
// F2 - F1 is distance to vein centerline; 0 at the boundary, forming an anastomosing network.
vec2 physarum_network(vec2 p) {
    // Multi-octave domain warping creates natural sinuous curving veins rather than straight lines
    vec2 warp = vec2(
        fbm(p * 4.0 + vec2(1.7, 9.2)),
        fbm(p * 4.0 + vec2(8.3, 2.8))
    );
    vec2 wp = p + (warp - 0.5) * 0.55;

    vec2 ip = floor(wp);
    vec2 fp = fract(wp);

    float d1 = 8.0;
    float d2 = 8.0;

    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            vec2 g = vec2(float(i), float(j));
            vec2 o = vec2(hash21(ip + g), hash21(ip + g + 19.3));
            vec2 r = g + o * 0.72 - fp;
            float d = dot(r, r);
            if (d < d1) {
                d2 = d1;
                d1 = d;
            } else if (d < d2) {
                d2 = d;
            }
        }
    }
    return vec2(sqrt(d1), sqrt(d2) - sqrt(d1));
}

void fragment() {
    vec2 wp = world_pos.xz;
    vec3 wn = normalize(vert_normal);

    // UV2.x = continuous tube width W (0..1), UV2.y = front factor (0 = interior cords, 1 = front fan)
    float vein_w = clamp(UV2.x, 0.0, 1.0);
    float front_fac = clamp(UV2.y, 0.0, 1.0);

    // 1. Edge feathering for the outer perimeter of the colony
    float edge_noise = fbm(wp * 16.0) * 0.35 + fbm(wp * 54.0) * 0.40 + fbm(wp * 130.0) * 0.25;
    float alpha_threshold = clamp(0.50 + (edge_noise - 0.5) * 2.0, 0.06, 0.94);
    if (COLOR.a < alpha_threshold) {
        discard;
    }

    // 2. Evaluate anastomosing vein network
    vec2 net = physarum_network(wp * vein_scale);
    float dist_to_cord = net.y; // 0 on cord center, increases away from cord

    // Major trunk corridors (coarse spatial flow channels)
    float trunk_boost = smoothstep(0.40, 0.85, fbm(wp * 3.2 + vec2(3.1, 7.4)));

    // Cord half-width scales with tube width W and trunk hierarchy
    float base_cord_w = mix(0.09, 0.26, clamp(vein_w + trunk_boost * 0.22, 0.0, 1.0));

    // Advancing front fan expands cords into broad lobate pseudopodial fingers
    float fan_lobes = smoothstep(0.20, 0.80, fbm(wp * 24.0));
    float effective_cord_w = mix(base_cord_w, 0.38 + 0.16 * fan_lobes, front_fac);

    // Inside cord or in open bay
    float in_cord = smoothstep(effective_cord_w, effective_cord_w * 0.35, dist_to_cord);

    // In open bays between cords and between fan lobes, discard to reveal the soil!
    if (in_cord < 0.12) {
        discard;
    }

    // 3. Peristaltic shuttle streaming: rhythmic traveling wave of cytoplasmic flow
    float wave_phase = TIME * pulse_speed - (wp.x * 6.5 + wp.y * 5.2) + vnoise(wp * 14.0) * 2.0;
    float stream_pulse = 0.5 + 0.5 * sin(wave_phase);

    // Fine protoplasmic micro-granules streaming inside the cytoplasm
    float micro_stream = vnoise(wp * 75.0 + vec2(TIME * 0.8, -TIME * 0.6));

    // 4. Color synthesis
    vec3 species_tint = srgb_to_linear(COLOR.rgb);
    vec3 c;
    float rough;
    float spec;

    if (front_fac > 0.60) {
        // Advancing foraging fan lobes: luminous canary yellow with active streaming
        c = mix(vein_sheath_color, front_fan_color, front_fac);
        c *= 0.94 + 0.16 * stream_pulse + 0.08 * (micro_stream - 0.5);
        c = mix(c, species_tint, 0.25);
        rough = 0.22;
        spec = 0.68;
    } else {
        // Raised protoplasmic cord: warm amber-gold core with brighter canary-yellow sheath
        vec3 cord_col = mix(vein_sheath_color, vein_core_color, in_cord);
        // Surge of cytoplasm brightens the vein during the contraction pulse
        cord_col *= 0.90 + 0.25 * stream_pulse;
        cord_col = mix(cord_col, species_tint, 0.20);
        c = cord_col;
        rough = mix(0.16, 0.26, 1.0 - in_cord);
        spec = 0.80;
    }

    // 5. Surface normal perturbation: rounded tubular cross-section for the cords
    float eps = 0.0015;
    float dX = physarum_network((wp + vec2(eps, 0.0)) * vein_scale).y - dist_to_cord;
    float dZ = physarum_network((wp + vec2(0.0, eps)) * vein_scale).y - dist_to_cord;
    vec3 cord_grad = vec3(-dX, 0.0, -dZ) / eps * (in_cord * 0.40);
    vec3 perturbed_n = normalize(wn + cord_grad);

    // Fresnel sheen for moist organic membrane
    float fresnel = pow(1.0 - max(0.0, dot(normalize((VIEW_MATRIX * vec4(perturbed_n, 0.0)).xyz), vec3(0.0, 0.0, 1.0))), 3.5);
    c = mix(c, vec3(1.0, 0.96, 0.65), fresnel * 0.32 * in_cord);

    ALBEDO = c;
    ROUGHNESS = rough;
    SPECULAR = spec;
    BACKLIGHT = c * vec3(1.25, 0.92, 0.30) * 0.50; // Subsurface amber glow when backlit
    NORMAL = normalize((VIEW_MATRIX * vec4(perturbed_n, 0.0)).xyz);
}
