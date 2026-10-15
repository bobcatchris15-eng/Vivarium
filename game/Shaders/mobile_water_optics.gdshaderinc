// Shared look for mobile films, falling sheets and droplets. The standing pond
// keeps its own material. The thin-film tint floor is an art-direction control:
// millimetre water would otherwise inherit almost all of the brown ground color.
uniform vec3 mobile_water_tint : source_color = vec3(.10,.46,.50);
uniform float mobile_tint_strength : hint_range(0.0,0.6) = .13;
uniform float mobile_reflection_strength : hint_range(0.0,0.3) = .035;
uniform float mobile_roughness : hint_range(0.02,0.5) = .20;

vec3 mobile_transmission(vec3 background,float thickness,float visibility){
    vec3 absorbed=background*exp(-vec3(3.8,.7,.5)*thickness*visibility);
    float tint=mobile_tint_strength+.08*(1.0-exp(-thickness*20.0));
    return mix(absorbed,mobile_water_tint,clamp(tint*visibility,0.0,.65));
}

vec3 mobile_reflection(vec3 reflected){
    vec3 enclosure=mix(vec3(.035,.065,.072),vec3(.14,.23,.25),smoothstep(-.1,.8,reflected.y));
    float panel=exp(-pow(reflected.z*2.8,2.0))*smoothstep(.30,.85,reflected.y);
    return enclosure+vec3(.24,.31,.32)*panel;
}

vec3 mobile_surface_color(vec3 transmission,vec3 reflected,float fresnel,float visibility){
    float reflectance=fresnel+mobile_reflection_strength*visibility*(1.0-fresnel);
    return mix(transmission,mobile_reflection(reflected),reflectance);
}
