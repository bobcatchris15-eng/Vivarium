// Presentation-only impact history supplied by falling-water parcels.
// xyz = world impact, w = impact strength; times use the fluid optical clock.
uniform vec4 plunge_impacts[8];
uniform float plunge_times[8];
uniform int plunge_count = 0;
uniform float plunge_clock = -1.0;

float interface_hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float interface_cells(vec2 p) {
    vec2 cell = floor(p), f = fract(p);
    float nearest = 2.0;
    for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
        vec2 g = vec2(float(x), float(y));
        vec2 jitter = vec2(interface_hash(cell + g), interface_hash(cell + g + 19.7));
        nearest = min(nearest, length(g + jitter - f));
    }
    return nearest;
}
void water_interface(vec3 position, float clock, float gap, float speed,
        out vec2 slope, out float foam) {
    slope = vec2(0.0);
    float impact_foam = 0.0;
    for (int i = 0; i < 8; i++) {
        if (i >= plunge_count) break;
        float age = (plunge_clock >= 0.0 ? plunge_clock : clock) - plunge_times[i];
        if (age < 0.0 || age > 1.2) continue;
        vec2 offset = position.xz - plunge_impacts[i].xz;
        float r = length(offset);
        float decay = exp(-r / 0.18 - age * 3.0) * plunge_impacts[i].w;
        float phase = r * 95.0 - age * 24.0;
        slope += offset / max(r, 0.001) * cos(phase) * decay * 0.08;
        impact_foam = max(impact_foam, exp(-r * r / 0.0064) * exp(-age * 1.8) * plunge_impacts[i].w);
    }
    vec2 advected = position.xz + vec2(clock * 0.012, -clock * 0.008);
    float bubbles = smoothstep(0.10, 0.30, interface_cells(advected * 85.0))
        * smoothstep(0.08, 0.22, interface_cells(advected * 240.0));
    float turbulent = smoothstep(0.12, 0.5, speed) * 0.35;
    foam = clamp((impact_foam + turbulent) * bubbles, 0.0, 0.92);
    foam *= smoothstep(0.0005, 0.004, gap);
}
