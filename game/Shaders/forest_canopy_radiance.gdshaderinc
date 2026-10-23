// Continuous high-dynamic-range canopy radiance shared by the sky cubemap and all water layers.
uniform vec3 sun_color = vec3(1.0, 0.95, 0.87);
uniform vec3 sky_top_color = vec3(0.30, 0.44, 0.57);
uniform vec3 sky_horizon_color = vec3(0.65, 0.73, 0.76);
uniform vec3 ground_bottom_color = vec3(0.06, 0.08, 0.05);
uniform vec3 ground_horizon_color = vec3(0.14, 0.18, 0.12);
uniform float sun_energy = 1.3;
uniform vec3 light_direction = vec3(0.4, 0.8, -0.3);
uniform float sky_energy = 1.0;

float canopy_hash(vec2 p) {
    uvec2 q = uvec2(ivec2(p));
    uint n = (q.x * 1597334677u) ^ (q.y * 3812015801u);
    n = (n ^ (n >> 16u)) * 2246822519u;
    n = n ^ (n >> 13u);
    return float(n & 16777215u) / 16777215.0;
}

vec2 canopy_hash2(vec2 p) {
    uvec2 q = uvec2(ivec2(p));
    uint n1 = (q.x * 1597334677u) ^ (q.y * 3812015801u);
    n1 = (n1 ^ (n1 >> 16u)) * 2246822519u;
    n1 = n1 ^ (n1 >> 13u);
    uint n2 = (q.x * 3812015801u) ^ (q.y * 1597334677u);
    n2 = (n2 ^ (n2 >> 16u)) * 2246822519u;
    n2 = n2 ^ (n2 >> 13u);
    return vec2(float(n1 & 16777215u), float(n2 & 16777215u)) / 16777215.0;
}

float canopy_noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(canopy_hash(cell), canopy_hash(cell + vec2(1.0, 0.0)), f.x),
               mix(canopy_hash(cell + vec2(0.0, 1.0)), canopy_hash(cell + vec2(1.0, 1.0)), f.x), f.y);
}

float canopy_voronoi(vec2 p) {
    vec2 n = floor(p);
    vec2 f = fract(p);
    float md = 8.0;
    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            vec2 g = vec2(float(i), float(j));
            vec2 o = canopy_hash2(n + g);
            vec2 r = g + o - f;
            float d = dot(r, r);
            if (d < md) {
                md = d;
            }
        }
    }
    return sqrt(md);
}

float canopy_gap_mask(vec3 dir) {
    vec2 angular = dir.xz / max(0.18, 0.42 + dir.y) * 4.8;
    float v1 = canopy_voronoi(angular * 0.85);
    float v2 = canopy_voronoi(angular * 2.3 + vec2(4.7, 1.9));
    float n = canopy_noise(angular * 3.6 + vec2(7.3, 3.1));
    float gaps = smoothstep(0.24, 0.70, v1 * 0.55 + v2 * 0.30 + n * 0.15);
    float flecks = smoothstep(0.62, 0.86, canopy_noise(angular * 8.5 + vec2(2.1, 5.7)));
    return clamp(gaps + flecks * 0.45, 0.06, 1.0);
}

vec3 forest_canopy_radiance(vec3 ray) {
    vec3 direction = normalize(ray);
    float y = direction.y;

    // Horizon transition below 0: smooth blend into ground_bottom_color and ground_horizon_color
    if (y < 0.0) {
        float ground_t = pow(clamp(-y, 0.0, 1.0), 0.75);
        vec3 ground = mix(ground_horizon_color, ground_bottom_color, ground_t);
        return ground * sky_energy;
    }

    // Horizon transition above 0: smooth blend into sky_horizon_color
    float elevation = clamp(y, 0.0, 1.0);
    vec3 base_sky = mix(sky_horizon_color, sky_top_color, pow(elevation, 0.65));

    // Overhead soft-box conservatory sky opening: smoothstep(0.55, 0.92, y) * 2.2 * sky_top_color
    vec3 conservatory_opening = smoothstep(0.55, 0.92, y) * 2.2 * sky_top_color;

    // Rich multi-layered green foliage transmittance (vec3(0.18, 0.34, 0.11)) at mid-elevations
    float mid_elevation = smoothstep(0.04, 0.28, y) * smoothstep(0.92, 0.48, y);
    vec2 angular = direction.xz / max(0.18, 0.42 + y) * 5.0;
    float foliage_texture = canopy_noise(angular) * 0.65 + canopy_noise(angular * 2.3 + vec2(3.7, 8.1)) * 0.35;
    vec3 foliage_transmittance = vec3(0.18, 0.34, 0.11);
    vec3 foliage_color = foliage_transmittance * mix(0.75, 1.30, foliage_texture);

    // Multi-scale canopy gap noise
    float gap_mask = canopy_gap_mask(direction);

    // Mid-elevation canopy blend: foliage transmittance where gap is small, open sky where gap is large
    vec3 canopy = mix(foliage_color, base_sky + conservatory_opening, gap_mask);
    vec3 radiance = mix(base_sky, canopy, mid_elevation);
    radiance += conservatory_opening * smoothstep(0.65, 0.95, y);

    // Sharp solar disk + circumsolar Mie aureole:
    // pow(max(dot(direction, normalize(light_direction)), 0.0), 512.0) * 18.0 + pow(max(dot(direction, normalize(light_direction)), 0.0), 24.0) * 2.8
    vec3 L = normalize(light_direction);
    float NdotL = max(dot(direction, L), 0.0);
    float solar_disk = pow(NdotL, 512.0) * 18.0;
    float solar_aureole = pow(NdotL, 24.0) * 2.8;
    float solar_lobe = solar_disk + solar_aureole;

    // Masked by multi-scale canopy gap noise (canopy_gap_mask), so specular reflections on water
    // and waxy leaves catch crisp, high-energy (10.0-25.0 linear) sun dapples through overhead leaves
    vec3 solar_radiance = sun_color * sun_energy * solar_lobe * gap_mask;
    radiance += solar_radiance;

    return radiance * sky_energy;
}

vec3 enclosure_backdrop(vec3 dir) {
    vec3 direction = normalize(dir);
    // Dark, out-of-focus smoked-slate conservatory enclosure backdrop
    // Luminance ~ 0.018 - 0.035 linear
    vec3 base_slate = vec3(0.019, 0.022, 0.025);
    vec3 top_slate = vec3(0.026, 0.030, 0.034);

    // Subtle vertical glass-frame gradient
    float vertical = clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    vec3 color = mix(base_slate, top_slate, vertical);

    // Subtle out-of-focus vertical glass-frame gradient
    float azimuth = atan(direction.x, direction.z);
    float frame = sin(azimuth * 12.0) * 0.5 + 0.5;
    frame = smoothstep(0.15, 0.85, frame);
    color += vec3(0.003, 0.0035, 0.004) * (1.0 - frame * 0.4);

    // Soft directional light falloff
    vec3 L = normalize(light_direction);
    float NdotL = max(dot(direction, L), 0.0);
    float soft_falloff = pow(NdotL, 2.0) * 0.008 * sun_energy;
    color += sun_color * soft_falloff;

    return color;
}
