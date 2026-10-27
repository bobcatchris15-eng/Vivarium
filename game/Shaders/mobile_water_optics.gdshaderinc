// One freshwater optical response for the table, mobile films and falling parcels.
#include "res://Shaders/forest_canopy_radiance.gdshaderinc"
uniform vec3 mobile_water_tint : source_color = vec3(.10,.20,.13);
uniform float mobile_tint_strength : hint_range(0.0,0.6) = .018;
uniform float mobile_reflection_strength : hint_range(0.0,0.3) = .055;
uniform int water_debug = 0;
uniform float mobile_roughness : hint_range(0.02,0.5) = .065;
vec3 mobile_transmission(vec3 background,float thickness,float visibility){
    vec3 absorb=exp(-vec3(2.2,.8,1.65)*thickness*visibility);
    vec3 deep=vec3(.025,.055,.033);
    vec3 transmitted=background*absorb+deep*(vec3(1.0)-absorb);
    float tint=mobile_tint_strength*(1.0-exp(-thickness*12.0));
    return mix(transmitted,mobile_water_tint,clamp(tint*visibility,0.0,.15));
}
vec3 mobile_reflection(vec3 reflected){return forest_canopy_radiance(reflected);}
vec3 mobile_surface_color(vec3 transmission,vec3 reflected,float fresnel,float visibility){
    float reflectance=fresnel+mobile_reflection_strength*visibility*(1.0-fresnel);
    return mix(transmission,mobile_reflection(reflected),reflectance);
}
