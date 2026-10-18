// Subpixel bryophyte micro-canopy: broad normal distribution + deep filament cavities.
// Uses stochastic stable plant-scale grain, never emissive or additive green.
void moss_aggregate(vec3 source, vec3 substrate_normal, vec3 camera_dir,
    vec2 position_m, float maturity, float gain,
    out vec3 pigment, out vec3 micro_normal, out float occlusion)
{
    float g = max(gain, 0.0);
    float clump = vnoise(position_m * 37.0 + vec2(7.23, 1.81));
    float needle = vnoise(position_m * 113.0 + vec2(1.52, 8.73));
    float cavity = smoothstep(.31, .74, mix(clump, needle, .40));
    float head = smoothstep(.07, .86, maturity);
    float grazing = pow(1.0 - clamp(abs(dot(normalize(substrate_normal),
        normalize(camera_dir))), 0.0, 1.0), 1.5);
    float scattering = .70 + cavity * .34 + (1.0 - grazing) * .13;
    float exposed = mix(1.0, scattering, clamp(g, 0.0, 1.0));
    float core_shade = mix(.75, 1.0, head);
    pigment = source * mix(1.0, exposed * core_shade, clamp(g, 0.0, 1.0));
    vec2 curl = vec2(vnoise(position_m * 66.0 + 5.0) - .5,
        vnoise(position_m * 64.0 - 3.8) - .5);
    micro_normal = normalize(substrate_normal +
        vec3(curl.x, .05, curl.y) * min(g, 3.0) * .62);
    occlusion = mix(1.0, mix(.57, 1.0, cavity) * mix(.78, 1.0, head),
        clamp(g, 0.0, 1.0));
}
