// Camera-independent canopy radiance shared by the sky cubemap and all water layers.
uniform vec3 zenith_canopy_color = vec3(.48, .58, .42);
uniform vec3 horizon_canopy_color = vec3(.28, .36, .25);
uniform vec3 ground_bounce_color = vec3(.065, .09, .045);
uniform vec3 light_direction = vec3(.4, .8, -.3);
uniform float sky_energy = 1.0;
float canopy_hash(vec2 p){
    // Integer corner keys stay identical when reached from either neighboring cell.
    // A large sine/fract hash can reassociate differently across those expressions.
    uvec2 q=uvec2(ivec2(p));
    uint n=(q.x*1597334677u)^(q.y*3812015801u);
    n=(n^(n>>16u))*2246822519u;
    n=n^(n>>13u);
    return float(n&16777215u)/16777215.0;
}
float canopy_noise(vec2 p){
    vec2 cell=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    return mix(mix(canopy_hash(cell),canopy_hash(cell+vec2(1.0,0.0)),f.x),
        mix(canopy_hash(cell+vec2(0.0,1.0)),canopy_hash(cell+vec2(1.0,1.0)),f.x),f.y);
}
vec3 forest_canopy_radiance(vec3 ray) {
    vec3 direction = normalize(ray);
    float y = direction.y;
    vec3 canopy = y >= 0.0
        ? mix(horizon_canopy_color, zenith_canopy_color, pow(y, .6))
        : mix(horizon_canopy_color, ground_bounce_color, pow(-y, .75));
    // Directional crown openings provide a reflected canopy, without drawing a sky
    // behind the ecosystem. The range preserves approximately the old mean radiance.
    vec2 angular=direction.xz/max(.22,.45+direction.y)*6.0;
    float crowns=canopy_noise(angular)*.65+canopy_noise(angular*2.17+7.3)*.25
        +canopy_noise(angular*4.31+19.7)*.10;
    float cover=smoothstep(.25,.72,crowns);
    canopy*=mix(.30,1.65,cover)*smoothstep(-.8,.05,y)+1.0-smoothstep(-.8,.05,y);
    float opening = pow(max(dot(direction, normalize(light_direction)), 0.0), 18.0);
    opening *= smoothstep(.05, .45, y);
    return mix(canopy, vec3(.90, .94, .84), opening * .8) * sky_energy;
}
