// Shared living-tissue optics, using linear-light reflectance rather than emission.
float leaf_hash(float x) { return fract(sin(x * 59.171 + 11.79) * 43758.5453); }
vec3 leaf_pigment(vec3 vertex_tissue, vec3 species_pigment, float maturity, float identity) {
 float age=smoothstep(.18,.91,clamp(maturity,0.,1.));
 vec3 juvenile=species_pigment*vec3(.94,1.08,.98);
 vec3 adult=species_pigment*vec3(1.04,.98,.89);
 return max(vec3(0.), mix(vertex_tissue,mix(juvenile,adult,age),.62)
           * mix(.92,1.08,leaf_hash(identity)));
}
// Directional illumination is handled by Godot's BACKLIGHT. Here we model the
// spectral absorbance and tissue thickness (thicker veins block more light).
vec3 leaf_through(vec3 reflectance,float transparency,float vein,float thickness,float underside) {
 float depth=clamp(thickness*.65+clamp(vein,0.,1.)*1.15,.20,2.9);
 // Beer-Lambert attenuation, normalized to the green-band response.
 // The artist's transmission parameter controls an actual fraction of
 // incoming direct irradiance, rather than multiplying already-dark tissue.
 vec3 absorption=exp(-depth*vec3(1.75,.58,2.85));
 vec3 spectral=absorption/max(absorption.g,.015);
 float openness=clamp(transparency,0.,.55)*2.60;
 float vein_occlusion=exp(-clamp(vein,0.,1.)*1.20);
 return max(reflectance,vec3(0.)) * spectral * openness *
        vein_occlusion * mix(1.12,.98,clamp(underside,0.,1.));
}
// Chloroplast domains, not random bright-green pixels. Individual blades
// use the same seed for broad and fine pigment variation.
float leaf_mesophyll_pattern(vec2 uv, float identity) {
 vec2 offset=vec2(identity*17.13,identity*31.79);
 float broad=vnoise(uv*vec2(3.6,4.1)+offset);
 float fine=vnoise(uv*vec2(11.5,14.7)+offset*2.37);
 return mix(.94,1.06,clamp(broad*.76+fine*.24,0.,1.));
}
// Abaxial (underside) lamina optics: paler, less saturated mesophyll behind a
// thinner, softer wax layer. Shared so every foliage path agrees on which side
// of FRONT_FACING is the botanical underside.
vec3 leaf_abaxial_tint(vec3 reflectance, float underside) {
 float u=clamp(underside,0.,1.);
 float l=dot(reflectance,vec3(.2126,.7152,.0722));
 vec3 pale=mix(vec3(l)*1.12,reflectance,.58)*vec3(1.02,1.05,.96);
 return mix(reflectance,pale,u*.38);
}
// Hydraulically thicker veins transmit less light than the lamina between
// them: the vein mask becomes optical thickness, so backlit blades show dark
// vascular silhouettes surrounded by warm chlorophyll glow.
float leaf_vein_tau(float base_tau,float vein_mask,float underside) {
 float t=mix(base_tau,base_tau*3.4,clamp(vein_mask,0.,1.));
 return mix(t,t*.82,clamp(underside,0.,1.));
}
// Epidermal micro-noise is only resolvable close to camera. Fading it to its
// neutral value cuts fragment-side noise evaluations on distant foliage.
float leaf_micro_fade(vec3 camera_position,vec3 world_position) {
 return 1.-smoothstep(2.2,5.,length(camera_position-world_position));
}
// Screen derivatives of height must be transformed to UV before using a
// tangent-space normal; otherwise texture relief varies with camera distance.
vec2 leaf_uv_gradient(float height,vec2 uv) {
 vec2 a=dFdx(uv),b=dFdy(uv);
 float jac=a.x*b.y-b.x*a.y;
 if(abs(jac)<1e-7) return vec2(0.);
 vec2 grad=vec2(dFdx(height)*b.y-dFdy(height)*a.y,
                dFdy(height)*a.x-dFdx(height)*b.x)/jac;
 return clamp(grad,vec2(-4.),vec2(4.));
}
// Henyey-Greenstein phase function for forward tissue subsurface scattering
float henyey_greenstein(float cos_theta, float g) {
 float g2 = g * g;
 float denom = 1.0 + g2 - 2.0 * g * cos_theta;
 return (1.0 - g2) / (4.0 * 3.14159265359 * pow(max(denom, 0.001), 1.5));
}
// Thick succulent water-parenchyma volumetric scattering: core remains optically dense while
// thinner tips, margins, and epidermal windows scatter milky desaturated green-amber light.
vec3 leaf_through_thick(vec3 reflectance, float transparency, float thickness_mm, float edge_factor, float turgor, vec3 trans_color) {
 float eff_mm = clamp(thickness_mm, 0.4, 12.0) * mix(1.0, 0.28, clamp(edge_factor, 0.0, 1.0)) * mix(0.75, 1.05, clamp(turgor, 0.0, 1.0));
 float depth = clamp(0.45 + eff_mm * 0.24, 0.35, 3.6);
 vec3 absorption = exp(-depth * vec3(1.15, 0.72, 1.55));
 vec3 milky = mix(trans_color, vec3(dot(reflectance, vec3(0.2126, 0.7152, 0.0722)) * 1.1 + 0.12), 0.38);
 float openness = clamp(transparency, 0.0, 0.65) * mix(0.65, 2.15, clamp(edge_factor, 0.0, 1.0));
 return max(reflectance * 0.35 + milky * 0.65, vec3(0.0)) * absorption * openness;
}
// Epicuticular farina powder bloom: settles strongest across protected basal/medial planes,
// lifts value toward chalky pale blue-grey, raises surface roughness, and suppresses mirror specular.
void leaf_farina(vec2 uv, float along, float abs_across, float farina_strength, float seed, float micro_fade,
  inout vec3 color, inout float roughness, inout float wax_top) {
 float f = clamp(farina_strength, 0.0, 1.0);
 if (f <= 0.001) return;
 float grain = 0.5;
 if (micro_fade > 0.01) {
  grain = mix(0.5, vnoise(uv * vec2(18.0, 22.0) + seed * 19.3) * 0.65 + vnoise(uv * vec2(52.0, 61.0) + seed * 37.1) * 0.35, micro_fade);
 }
 // Wax bloom weathers off the exposed apex and sharp margin slightly.
 float spatial = mix(1.0, 0.52, smoothstep(0.62, 0.98, along)) * mix(1.0, 0.62, smoothstep(0.75, 0.98, abs_across));
 float mask = clamp(f * spatial * (0.72 + 0.52 * grain), 0.0, 0.95);
 float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
 vec3 powder = vec3(clamp(luma * 1.35 + 0.14, 0.10, 0.44)) * vec3(0.92, 0.99, 1.08);
 color = mix(color, powder, mask * 0.68);
 roughness = mix(roughness, clamp(0.86 + 0.10 * grain, 0.80, 0.97), mask * 0.85);
 wax_top *= (1.0 - clamp(mask * 1.15, 0.0, 0.95));
}
// Unfurling and turgor curl offset in normalized blade length fractions: (0, y_frac, z_frac) before transform.
vec2 leaf_unfurl_and_turgor_offset(vec2 uv, float blade_len, float maturity, float turgor, vec3 curl_params) {
 float along = clamp(uv.x, 0.0, 1.0);
 float root_pin = smoothstep(0.0, 0.14, along);
 float unfurl = 1.0 - smoothstep(0.18, 0.95, clamp(maturity, 0.0, 1.0));
 float thirst = 1.0 - clamp(turgor, 0.0, 1.0);
 float across = (uv.y - 0.5) * 2.0;
 float cup = curl_params.y * (across * across) * along * (0.12 * unfurl + 0.05 * thirst);
 float tip_curve = along * along;
 float y_frac = (cup + 0.11 * unfurl * tip_curve - curl_params.z * (0.04 + 0.12 * thirst) * tip_curve) * root_pin;
 float z_frac = -(0.09 * unfurl + 0.03 * thirst) * tip_curve * root_pin;
 return clamp(vec2(y_frac, z_frac), vec2(-0.18), vec2(0.18));
}
