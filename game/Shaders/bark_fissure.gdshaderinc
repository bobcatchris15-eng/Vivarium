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
    // Darkening is concentrated in actual valleys; raised cork ridges keep
    // natural species scan color and variation.
    crevice_ao = mix(1.0, 1.0 - .58 * groove, strength);
    bark_color = scanned * mix(1.0, mix(.57, 1.09, 1.0 - groove), strength);
    // Scanned normal orientation follows stem UV; strengthen at deep cracks.
    normal_detail = normalize(vec3(scan_normal.xy *
        (1.0 + g * .25 + groove * g * .28), scan_normal.z));
}
