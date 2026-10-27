shader_type sky;
render_mode disable_fog;

#include "res://Shaders/forest_canopy_radiance.gdshaderinc"

uniform sampler2D canopy_day : filter_linear_mipmap, repeat_enable;
uniform sampler2D canopy_dusk : filter_linear_mipmap, repeat_enable;
uniform sampler2D canopy_night : filter_linear_mipmap, repeat_enable;
uniform float enclosure_daylight = 1.0;

void sky() {
    if (AT_CUBEMAP_PASS) {
        vec2 uv = vec2(atan(EYEDIR.x, EYEDIR.z) / (2.0 * PI) + 0.5, acos(clamp(EYEDIR.y, -1.0, 1.0)) / PI);

        // Smooth continuous daylight transition between authored 4K EXR sky maps:
        // canopy_night -> canopy_dusk -> canopy_day
        vec3 authored = mix(texture(canopy_night, uv).rgb, texture(canopy_dusk, uv).rgb, smoothstep(0.0, 0.28, enclosure_daylight));
        authored = mix(authored, texture(canopy_day, uv).rgb, smoothstep(0.28, 1.0, enclosure_daylight));

        // Ground ambient bounce below horizon from calibrated day/night keyframes
        float ground_t = pow(clamp(-EYEDIR.y, 0.0, 1.0), 0.75);
        vec3 ground = mix(ground_horizon_color, ground_bottom_color, ground_t) * sky_energy;

        // Add true high-dynamic-range directional solar opening and crisp canopy sun dapples (10.0 - 27.0+ linear)
        // without diluting or washing out the authored 4K conservatory diffuser lighting
        vec3 solar = canopy_solar_radiance(EYEDIR);
        vec3 upper_radiance = authored + solar;

        // Smooth transition across horizon into terrain bounce
        float horizon = smoothstep(-0.04, 0.04, EYEDIR.y);
        COLOR = mix(ground, upper_radiance, horizon);
    } else {
        // Camera rays render out-of-focus smoked conservatory enclosure backdrop
        COLOR = enclosure_backdrop(EYEDIR);
    }
}
