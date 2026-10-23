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
// Optical thickness tau and trichome rim fuzz
varying float tissue_rim;
varying float tissue_tau;
uniform int tissue_material_diagnostic = 0;
uniform int tissue_light_diagnostic = 0; // 0 composite, 1 reflected, 2 transmitted, 3 specular

void process_leaf_direct_light(
    vec3 n, vec3 l, vec3 v,
    vec3 albedo, float roughness, float specular_amount,
    vec3 light_color, float attenuation,
    inout vec3 diffuse_light, inout vec3 specular_light) {

    float nl = dot(n, l);
    float reflected = max(nl, 0.0);
    float transmitted = max(-nl, 0.0);

    // DIFFUSE_LIGHT accumulates irradiance; Godot multiplies diffuse by
    // ALBEDO in the final surface response.
    float reflected_scatter = reflected;
    if (tissue_diffuse_wrap > 0.001) {
        float wrap = max(tissue_diffuse_wrap, 0.0);
        float broad_reflection = max(nl + wrap, 0.0) / (1.0 + wrap);
        reflected_scatter = mix(reflected, broad_reflection, clamp(wrap * 1.4, 0.0, 1.0));
    }
    vec3 surface = vec3(reflected_scatter);

    // Spectrally Selective Beer-Lambert Subsurface Transmittance using baked vein thickness tau(u,v) in tissue_physical.b
    float tau = tissue_tau > 0.001 ? tissue_tau : 0.65;
    float hg = henyey_greenstein(-dot(v, l), 0.68);
    vec3 T_leaf = light_color * exp(-tau * vec3(4.8, 1.15, 6.4)) * hg;

    // Decouple thin-leaf back-transmittance from front-face NdotL self-shadowing while retaining cast-shadow ATTENUATION
    float back_cosine = max(-nl + 0.15, 0.0) / 1.15;
    float phase = pow(max(-dot(l, v), 0.0), 8.0) * (1.0 - abs(nl));
    float transmission_factor = (back_cosine + phase * 0.35) * tissue_direct_transmission * tissue_phase_mask;

    vec3 sss_transport = (T_leaf / max(albedo, vec3(0.014))) * transmission_factor * 12.5;

    // Retain BACKLIGHT fallback if tau is unset
    if (tissue_tau <= 0.001) {
        float transmission_cosine = transmitted + phase * max(tissue_phase_scatter, 0.0) * 0.24;
        sss_transport = clamp(BACKLIGHT / max(albedo, vec3(.014)),
            vec3(0.0), vec3(2.0)) * (transmission_cosine * tissue_direct_transmission);
    }

    // Trichome / Margin Fuzz (RIM): grazing-angle asperity scattering
    float rim_factor = tissue_rim > 0.0 ? tissue_rim : 0.0;
    float asperity = pow(1.0 - max(dot(n, v), 0.0), 3.5) * max(dot(n, l) + 0.35, 0.0) * rim_factor;
    vec3 fuzz = vec3(asperity * 1.5);

    if (tissue_light_diagnostic == 1) sss_transport = vec3(0.0);
    if (tissue_light_diagnostic == 2) { surface = vec3(0.0); fuzz = vec3(0.0); }
    if (tissue_light_diagnostic == 3) { surface = vec3(0.0); sss_transport = vec3(0.0); fuzz = vec3(0.0); }

    diffuse_light += (surface + sss_transport + fuzz) * light_color * attenuation * (1.0 / PI);

    // Dual-Lobe Specular BRDF
    if (tissue_light_diagnostic != 2 && reflected > 0.0) {
        vec3 h = normalize(l + v);
        float nh = max(dot(n, h), 0.0);
        float nv = max(dot(n, v), 0.04);
        float vh = max(dot(v, h), 0.0);
        float nl_clamped = max(nl, 0.001);

        // Lobe 1: Epicuticular wax lobe (sharp highlight)
        float r_wax = clamp(roughness * 0.42, 0.06, 0.28);
        float a_wax = r_wax * r_wax;
        float a2_wax = a_wax * a_wax;
        float den_wax = nh * nh * (a2_wax - 1.0) + 1.0;
        float D_wax = a2_wax / max(PI * den_wax * den_wax, 0.0001);
        float k_wax = (r_wax + 1.0) * (r_wax + 1.0) * 0.125;
        float G_wax = (nv / (nv * (1.0 - k_wax) + k_wax)) *
                      (nl_clamped / (nl_clamped * (1.0 - k_wax) + k_wax));
        float f0_wax = 0.028;
        float F_wax = f0_wax + (1.0 - f0_wax) * pow(1.0 - vh, 5.0);
        float spec_wax = D_wax * G_wax * F_wax / max(4.0 * nv, 0.01);

        // Lobe 2: Broader epidermal lobe
        float a_epi = max(0.045, roughness * roughness);
        float a2_epi = a_epi * a_epi;
        float den_epi = nh * nh * (a2_epi - 1.0) + 1.0;
        float D_epi = a2_epi / max(PI * den_epi * den_epi, 0.0001);
        float anis = clamp(tissue_fiber_anisotropy * tissue_fiber_mask, 0.0, 0.96);
        if (anis > 0.001) {
            vec3 t = normalize(tissue_fiber_view - n * dot(tissue_fiber_view, n));
            vec3 b = normalize(cross(n, t));
            float aspect = sqrt(max(0.055, 1.0 - 0.85 * anis));
            float at = max(0.045, a_epi / aspect);
            float ab = max(0.045, a_epi * aspect);
            float ellipsoid = pow(dot(h, t) / at, 2.0) +
                pow(dot(h, b) / ab, 2.0) + nh * nh;
            float elliptical_d = 1.0 / max(PI * at * ab * ellipsoid * ellipsoid, 0.0001);
            D_epi = mix(D_epi, elliptical_d, anis);
        }
        float k_epi = (roughness + 1.0) * (roughness + 1.0) * 0.125;
        float G_epi = (nv / (nv * (1.0 - k_epi) + k_epi)) *
                      (nl_clamped / (nl_clamped * (1.0 - k_epi) + k_epi));
        float f0_epi = tissue_use_explicit_material_f0
            ? clamp(tissue_material_f0, 0.0, 0.16)
            : mix(0.018, 0.055, clamp(specular_amount, 0.0, 1.0));
        float F_epi = f0_epi + (1.0 - f0_epi) * pow(1.0 - vh, 5.0);
        float spec_epi = D_epi * G_epi * F_epi / max(4.0 * nv, 0.01);

        float combined_spec = spec_wax * 0.75 + spec_epi * 0.65;
        if (tissue_light_diagnostic == 1) combined_spec = 0.0;
        specular_light += light_color * attenuation * combined_spec * specular_amount * tissue_specular_stress;
    }
}

void light() {
    process_leaf_direct_light(
        normalize(NORMAL), normalize(LIGHT), normalize(VIEW),
        ALBEDO, ROUGHNESS, SPECULAR_AMOUNT,
        LIGHT_COLOR, ATTENUATION,
        DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
