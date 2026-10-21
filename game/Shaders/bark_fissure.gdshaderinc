// Living bark is layered cork, not merely a photograph on a cylinder.
// Grooves track the scan's existing low-albedo cracks, warped slowly along
// the trunk. Matching albedo/AO/normal response prevents painted-on cracks.
void bark_fissure(vec3 scanned, vec3 scan_normal, vec2 cyl_uv,
    float intensity, out vec3 bark_color, out vec3 normal_detail,
    out float crevice_ao)
{
    float g = clamp(intensity, 0.0, 4.0);
    float warp = (vnoise(vec2(cyl_uv.x * 2.0, cyl_uv.y * .55)) - .5) * .45;
    float axial = vnoise(vec2(cyl_uv.x * 17.0 + warp, cyl_uv.y * 1.35));
    float secondary = vnoise(vec2(cyl_uv.x * 43.0 - warp * 1.2,
         cyl_uv.y * 3.0));
    float scan_gray = dot(scanned, vec3(.2126, .7152, .0722));
    float fissure = (1.0 - smoothstep(.22, .50, axial)) * (.54 + .46 * secondary);
    float old_dark = 1.0 - smoothstep(.025, .21, scan_gray);
    float groove = clamp(fissure * .72 + old_dark * .28, 0.0, 1.0);
    float strength = clamp(g, 0.0, 1.0);

    // Cross-slope into fissure creates 3D bevels that catch grazing light
    float e = 0.015;
    float a_l = vnoise(vec2((cyl_uv.x - e) * 17.0 + warp, cyl_uv.y * 1.35));
    float a_r = vnoise(vec2((cyl_uv.x + e) * 17.0 + warp, cyl_uv.y * 1.35));
    float f_l = (1.0 - smoothstep(.22, .50, a_l));
    float f_r = (1.0 - smoothstep(.22, .50, a_r));
    float groove_slope_x = (f_r - f_l) * 2.5;

    // Darkening is concentrated in actual valleys; raised cork ridges keep
    // natural species scan color and variation.
    crevice_ao = mix(1.0, 1.0 - .58 * groove, strength);
    bark_color = scanned * mix(1.0, mix(.57, 1.09, 1.0 - groove), strength);

    // Enhanced tactile normal relief: scanned normal bump + physical fissure bevels
    vec2 n_xy = scan_normal.xy * (2.6 + g * 0.8 + groove * g * 0.5);
    n_xy.x += groove_slope_x * strength * 0.45;
    normal_detail = normalize(vec3(n_xy, scan_normal.z));
}

void bark_fissure_pbr(vec3 scanned, vec3 scan_normal, float scan_roughness, vec2 cyl_uv,
    float intensity, float weathering, float exposure,
    out vec3 bark_color, out vec3 normal_detail, out float bark_roughness,
    out float crevice_ao, out float groove_out)
{
    float g = clamp(intensity, 0.0, 4.0);
    float warp = (vnoise(vec2(cyl_uv.x * 2.0, cyl_uv.y * .55)) - .5) * .45;
    float axial = vnoise(vec2(cyl_uv.x * 17.0 + warp, cyl_uv.y * 1.35));
    float secondary = vnoise(vec2(cyl_uv.x * 43.0 - warp * 1.2,
         cyl_uv.y * 3.0));
    float scan_gray = dot(scanned, vec3(.2126, .7152, .0722));
    float fissure = (1.0 - smoothstep(.22, .50, axial)) * (.54 + .46 * secondary);
    float old_dark = 1.0 - smoothstep(.025, .21, scan_gray);
    float groove = clamp(fissure * .72 + old_dark * .28, 0.0, 1.0);
    groove_out = groove;
    float strength = clamp(g, 0.0, 1.0);

    float e = 0.015;
    float a_l = vnoise(vec2((cyl_uv.x - e) * 17.0 + warp, cyl_uv.y * 1.35));
    float a_r = vnoise(vec2((cyl_uv.x + e) * 17.0 + warp, cyl_uv.y * 1.35));
    float f_l = (1.0 - smoothstep(.22, .50, a_l));
    float f_r = (1.0 - smoothstep(.22, .50, a_r));
    float groove_slope_x = (f_r - f_l) * 2.5;

    crevice_ao = mix(1.0, 1.0 - .58 * groove, strength);
    bark_color = scanned * mix(1.0, mix(.57, 1.09, 1.0 - groove), strength);

    vec2 n_xy = scan_normal.xy * (2.6 + g * 0.8 + groove * g * 0.5);
    n_xy.x += groove_slope_x * strength * 0.45;
    normal_detail = normalize(vec3(n_xy, scan_normal.z));

    // Dynamic roughness mapping:
    // Raised cork plates have smooth satiny sheen (~0.32-0.52),
    // while deep crevices become powdery matte (~0.88-0.96)
    float r_plate = smoothstep(0.18, 0.88, scan_roughness);
    float r_base = mix(0.32, 0.84, r_plate);
    float r_fissure = mix(r_base, 0.96, groove * 0.80);
    bark_roughness = clamp(r_fissure + weathering * exposure * 0.06, 0.28, 0.98);
}
