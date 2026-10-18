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
 float depth=clamp(thickness+clamp(vein,0.,1.)*.92,.25,2.7);
 vec3 transmittance=exp(-depth*vec3(1.35,.42,2.40));
 vec3 scatter=vec3(reflectance.g*.67, reflectance.g*1.14, reflectance.g*.25);
 return mix(reflectance,scatter,.70)*transmittance
    *clamp(transparency,0.,.55)*mix(1.24,1.02,clamp(underside,0.,1.));
}
// Chloroplast domains, not random bright-green pixels. Individual blades
// use the same seed for broad and fine pigment variation.
float leaf_mesophyll_pattern(vec2 uv, float identity) {
 vec2 offset=vec2(identity*17.13,identity*31.79);
 float broad=vnoise(uv*vec2(3.6,4.1)+offset);
 float fine=vnoise(uv*vec2(11.5,14.7)+offset*2.37);
 return mix(.84,1.15,clamp(broad*.76+fine*.24,0.,1.));
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
