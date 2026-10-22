// Directional thin-tissue illumination used by production foliage paths.
// This replaces the ineffective low-energy BACKLIGHT approximation with an
// explicitly controllable diffuse reflection / transmission / cuticle model.
// BACKLIGHT is an *energy term* produced by leaf_through, not emission.
uniform float tissue_direct_transmission = 1.9;
// Diagnostic gain; default leaves all existing direct cuticle optics intact.
uniform float tissue_specular_stress = 1.0;
// M02: retain established directional-light reflectance independently of light
// intensity. The legacy switch remains available for controlled comparisons.
uniform bool tissue_use_explicit_material_f0 = true;
uniform float tissue_material_f0 = 0.055;
// Experimental controlled scattering modes. Defaults preserve last accepted rendering.
uniform float tissue_diffuse_wrap = 0.0;
uniform float tissue_phase_scatter = 1.6;
uniform float tissue_fiber_anisotropy = 0.72;
// UV-x-aligned fiber axis. Supplied by specialized leaf material.
varying vec3 tissue_fiber_view;
varying float tissue_fiber_mask;
// Mask forward scatter to actual membranes; other organisms use normal BRDF.
varying float tissue_phase_mask;
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
    // Wrap-lambert mimics finite-width mesophyll scattering without emission.
    float reflected_scatter = reflected;
    if (tissue_diffuse_wrap > 0.001) {
        float wrap = max(tissue_diffuse_wrap, 0.0);
        float broad_reflection = max(nl + wrap, 0.0) / (1.0 + wrap);
        reflected_scatter = mix(reflected, broad_reflection, clamp(wrap * 1.4, 0.0, 1.0));
    }
    vec3 surface = vec3(reflected_scatter);
    // Forward scattering phase response: brightens backlit tissue specifically when
    // the light direction and viewing direction oppose one another.
    float phase = pow(max(-dot(l, v), 0.0), 8.0) * (1.0 - abs(nl)) * tissue_phase_mask;
    float transmission_cosine = transmitted + phase * max(tissue_phase_scatter, 0.0) * 0.24;
    vec3 transport = clamp(BACKLIGHT / max(ALBEDO, vec3(.014)),
        vec3(0.0), vec3(2.0)) * (transmission_cosine * tissue_direct_transmission);
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
    // Elliptical GGX: adaxial wax follows cellulose fibers along blade UV-x.
    // This is only enabled on specimens with a valid, derivative-based fiber axis.
    float anis = clamp(tissue_fiber_anisotropy * tissue_fiber_mask, 0.0, 0.96);
    if (anis > 0.001) {
        vec3 t = normalize(tissue_fiber_view - n * dot(tissue_fiber_view,n));
        vec3 b = normalize(cross(n,t));
        float aspect = sqrt(max(0.055,1.0 - 0.85 * anis));
        float at = max(0.045, a / aspect);
        float ab = max(0.045, a * aspect);
        float ellipsoid = pow(dot(h,t)/at, 2.0) +
            pow(dot(h,b)/ab, 2.0) + nh*nh;
        float elliptical_d = 1.0 / max(PI * at * ab * ellipsoid * ellipsoid, 0.0001);
        distribution = mix(distribution, elliptical_d, anis);
    }
    float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) * 0.125;
    float geometry = (nv / (nv * (1.0 - k) + k)) *
                     (reflected / (reflected * (1.0 - k) + k));
    float light_f0_legacy = mix(0.018, 0.055, clamp(SPECULAR_AMOUNT, 0.0, 1.0));
    float f0 = tissue_use_explicit_material_f0
        ? clamp(tissue_material_f0, 0.0, 0.16)
        : light_f0_legacy;
    float fresnel = f0 + (1.0 - f0) * pow(1.0 - vh, 5.0);
    float specular = distribution * geometry * fresnel / max(4.0 * nv, 0.01);
    if (tissue_light_diagnostic == 1) specular = 0.0;
    SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * specular * SPECULAR_AMOUNT * tissue_specular_stress;
    }
}
