// Bulk optics shared by the water table, the dynamic fluid surface and the falling
// parcels. Only what happens below the interface is described here: the lit water
// surfaces leave reflection to Godot's Forward+ pipeline (shadowed GGX sun glints,
// sky / reflection-probe IBL and screen-space reflections), so no canopy colour and
// no milky reflection floor is composited by hand any more.
#include "res://Shaders/forest_canopy_radiance.gdshaderinc"

// Legacy parcel tint, still used by the airborne films. Water surfaces do not tint
// their transmission with it: the bed shows through the absorption spectrum below.
uniform vec3 mobile_water_tint : source_color = vec3(.10,.20,.13);
// The airborne films still composite their own canopy reflection. Lit water leaves
// this at zero so the fake milky floor cannot come back.
uniform float mobile_reflection_strength : hint_range(0.0,0.3) = 0.0;
uniform int water_debug = 0;
uniform float mobile_roughness : hint_range(0.02,0.5) = .065;

// Clear, lightly humic vivarium water. Absorption is per metre of optical path; red
// is removed roughly three times faster than green, which grades a pond from a
// colour-neutral shallow bed to a saturated teal as the column deepens.
uniform vec3 water_absorption = vec3(3.4, 1.15, 2.65);
uniform vec3 water_scattering = vec3(0.42, 0.62, 0.48);
// Ambient radiance that fills the body through single scattering (linear).
uniform vec3 water_ambient_radiance = vec3(0.10, 0.135, 0.115);
// Chromatic separation of the refracted screen sample, in screen-UV units.
uniform float water_dispersion : hint_range(0.0, 0.02) = 0.0032;
// Wind forcing for the gravity-capillary cascade shared by both water surfaces.
uniform float water_wind_speed : hint_range(0.0, 8.0) = 0.6;
uniform vec2 water_wind_direction = vec2(0.94, 0.34);
uniform float water_wave_steepness : hint_range(0.0, 2.0) = 1.0;

// Normal-incidence Fresnel reflectance of water, n = 1.333:
// F0 = ((n - 1) / (n + 1))^2 = 0.0204.
const float WATER_F0 = 0.0204;
// Godot's SPECULAR output scales F0 by 0.08, so 0.0204 / 0.08 = 0.255.
const float WATER_SPECULAR = 0.255;
const float WATER_GRAVITY = 9.81;       // m/s^2
const float WATER_CAPILLARY = 7.28e-5;  // gamma / rho at 20 C, m^3/s^2
const float WATER_TAU = 6.28318530718;

float water_fresnel(float cosine) {
    return WATER_F0 + (1.0 - WATER_F0) * pow(1.0 - clamp(cosine, 0.0, 1.0), 5.0);
}

// omega^2 = (g k + gamma/rho k^3) tanh(k h)   (Lamb, Hydrodynamics)
float water_dispersion_omega(float k, float depth) {
    float k3 = k * k * k;
    return sqrt(max(0.0, (WATER_GRAVITY * k + WATER_CAPILLARY * k3) * tanh(clamp(k * depth, 0.0, 20.0))));
}

// Eight-direction dispersion-correct gravity-capillary cascade. Returns the surface
// gradient (dh/dx, dh/dz) and the time-averaged slope variance that drives the GGX
// roughness. Every octave runs at its own frequency, so an overhead sun breaks into
// separate specular glints on the short capillaries instead of one stretched streak.
vec2 water_wave_slope(vec2 p, float clock, float depth, float wind_speed, vec2 wind_dir, out float slope_variance) {
    slope_variance = 0.0;
    vec2 slope = vec2(0.0);
    vec2 base_dir = length(wind_dir) > 1e-5 ? normalize(wind_dir) : vec2(1.0, 0.0);
    float base_angle = atan(base_dir.y, base_dir.x);
    // Calm air still leaves capillary texture; gusting air adds the full slope.
    float wind_gain = mix(0.30, 1.0, smoothstep(0.0, 1.6, wind_speed)) * water_wave_steepness;
    float wavelength = 0.34;
    for (int i = 0; i < 8; i++) {
        float k = WATER_TAU / wavelength;
        // Deep-water limit of the dispersion relation drives the period; the
        // finite-depth omega = omega_inf * sqrt(tanh(k h)) drives the phase.
        // Gravity dominates the 34 cm swell, surface tension the 5 cm capillaries.
        float omega_deep = sqrt(WATER_GRAVITY * k + WATER_CAPILLARY * k * k * k);
        float omega = water_dispersion_omega(k, depth);
        // The period holds a whole number of cycles of this octave, so the bounded
        // clock wraps with an exact 2*pi step: no phase pop, no float drift after
        // hours of runtime, even though shallow water slows the wave down.
        float cycles = max(1.0, floor(omega_deep * 2.4 / WATER_TAU));
        float period = WATER_TAU * cycles / max(omega, 1e-3);
        // Slope amplitude 1.5-4.5 %, ramped from gravity to capillary scales.
        float steepness = mix(0.045, 0.015, float(i) / 7.0) * wind_gain;
        // Shoreline damping: a wave dies where the column is thinner than roughly a
        // fifth of its wavelength, so the wet/dry front stays glassy.
        steepness *= smoothstep(0.0, 0.2 * wavelength, depth);
        float angle = base_angle + float(i) * 0.7853981634 + sin(float(i) * 2.3999632) * 0.16;
        vec2 dir = vec2(cos(angle), sin(angle));
        float phase = k * dot(dir, p) - omega * mod(clock, period) + float(i) * 1.7;
        slope += dir * (steepness * cos(phase));
        slope_variance += 0.5 * steepness * steepness;
        wavelength *= 0.72;
    }
    return slope;
}

// Refracted radiance after an un-clamped bulk path, plus the single-scattered
// ambient fill that keeps a deep pond from reading as black plastic. At the
// interface this must be weighted by (1 - F) and handed to EMISSION: it is already
// lit radiance and must not be multiplied by the surface's own lighting again.
vec3 mobile_transmission(vec3 background, float thickness, float visibility) {
    vec3 sigma_t = max(water_absorption + water_scattering, vec3(1e-4));
    vec3 tau = sigma_t * max(thickness, 0.0) * clamp(visibility, 0.0, 1.0);
    vec3 transmittance = exp(-tau);
    vec3 single_scatter = (water_scattering / sigma_t) * water_ambient_radiance * sky_energy;
    return background * transmittance + single_scatter * (vec3(1.0) - transmittance);
}

// Screen-space refraction of a deep column is defocused by scattering; blur the
// sample instead of tinting it. Airborne films and shallow films stay sharp.
float water_refraction_mip(float optical_path, float roughness) {
    return clamp(optical_path * 2.4 * (0.35 + roughness * 6.0), 0.0, 3.0);
}

// Red, green and blue do not bend by the same angle. The offset is zero at the
// surface and grows with the column, so only deep refraction shows colour fringing.
float water_dispersion_amount(float optical_path, float roughness) {
    return water_dispersion * smoothstep(0.02, 0.5, optical_path) * (0.6 + roughness * 6.0);
}

vec3 mobile_reflection(vec3 reflected) { return forest_canopy_radiance(reflected); }

vec3 mobile_surface_color(vec3 transmission, vec3 reflected, float fresnel, float visibility) {
    float reflectance = clamp(fresnel + mobile_reflection_strength * visibility * (1.0 - fresnel), 0.0, 1.0);
    return mix(transmission, mobile_reflection(reflected), reflectance);
}
