// Directional thin-tissue illumination used by production foliage paths.
// This replaces the ineffective low-energy BACKLIGHT approximation with an
// explicitly controllable diffuse reflection / transmission / cuticle model.
// BACKLIGHT is an *energy term* produced by leaf_through, not emission.
uniform float tissue_direct_transmission = 1.0;
uniform int tissue_material_diagnostic = 0;
uniform int tissue_light_diagnostic = 0; // 0 composite, 1 reflected, 2 transmitted, 3 specular

void light() {
    vec3 n = normalize(NORMAL);
    vec3 l = normalize(LIGHT);
    vec3 v = normalize(VIEW);
    float nl = dot(n, l);
    float reflected = max(nl, 0.0);
    float transmitted = max(-nl, 0.0);
    // DIFFUSE_LIGHT accumulates irradiance; Godot multiplies diffuse by
    // ALBEDO in the final surface response. BACKLIGHT is outgoing tissue
    // radiance, so express it as an albedo-relative light transport factor.
    vec3 surface = vec3(reflected);
    vec3 transport = clamp(BACKLIGHT / max(ALBEDO, vec3(.014)),
        vec3(0.0), vec3(2.0)) * (transmitted * tissue_direct_transmission);
    if (tissue_light_diagnostic == 1) transport = vec3(0.0);
    if (tissue_light_diagnostic == 2) surface = vec3(0.0);
    if (tissue_light_diagnostic == 3) { surface = vec3(0.0); transport = vec3(0.0); }
    DIFFUSE_LIGHT += (surface + transport) * LIGHT_COLOR * ATTENUATION * (1.0 / PI);

    if (tissue_light_diagnostic != 2 && reflected > 0.0) {
    vec3 h = normalize(l + v);
    float nh = max(dot(n, h), 0.0);
    float nv = max(dot(n, v), 0.04);
    float vh = max(dot(v, h), 0.0);
    float a = max(0.045, ROUGHNESS * ROUGHNESS);
    float a2 = a * a;
    float den = nh * nh * (a2 - 1.0) + 1.0;
    float distribution = a2 / max(PI * den * den, 0.0001);
    float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) * 0.125;
    float geometry = (nv / (nv * (1.0 - k) + k)) *
                     (reflected / (reflected * (1.0 - k) + k));
    float f0 = mix(0.018, 0.055, clamp(SPECULAR_AMOUNT, 0.0, 1.0));
    float fresnel = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
    float specular = distribution * geometry * fresnel / max(4.0 * nv, 0.01);
    if (tissue_light_diagnostic == 1) specular = 0.0;
    SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * specular * SPECULAR_AMOUNT;
    }
}
