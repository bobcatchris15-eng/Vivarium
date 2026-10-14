shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_burley, specular_schlick_ggx;

// Thin sheet of running water reconstructed from the spring droplet stream. The mesh carries local water
// thickness in UV2.x and lies directly on the terrain, so this only has to sell "clear liquid": screen
// refraction of what is behind it, a fresnel rim, depth tint, and a moving ripple on the normal.

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float refraction_strength : hint_range(0.0, 0.5) = 0.035;
uniform float fresnel_power : hint_range(1.0, 8.0) = 3.0;
uniform float fresnel_gain : hint_range(0.0, 2.0) = 0.55;
uniform vec3 shallow_tint : source_color = vec3(0.55, 0.72, 0.70);
uniform vec3 deep_tint : source_color = vec3(0.10, 0.33, 0.34);
uniform float film_alpha : hint_range(0.0, 1.0) = 0.22;
uniform float pool_alpha : hint_range(0.0, 1.0) = 0.72;
uniform float glint_gain : hint_range(0.0, 2.0) = 0.30;
uniform float ripple_strength : hint_range(0.0, 1.0) = 0.22;
uniform float ripple_scale : hint_range(0.1, 40.0) = 9.0;
uniform float ripple_speed : hint_range(0.0, 10.0) = 1.6;

void vertex() {
	// UV2.x carries the water thickness for this corner.
}

void fragment() {
	float depth = clamp(UV2.x, 0.0, 1.0);

	// Moving ripple on the normal. The mesh is geometrically flat, so without this a sheet reflects like a
	// glass plate rather than water.
	float t = TIME * ripple_speed;
	vec2 w = UV;
	float a = sin(w.x * ripple_scale + t) * cos(w.y * ripple_scale * 0.87 - t * 0.6);
	float b = sin((w.x * 0.6 + w.y) * ripple_scale * 1.7 - t * 1.3);
	vec3 nrm = normalize(vec3(-(a * 0.6 + b * 0.35) * ripple_strength, 1.0, -(b * 0.6 - a * 0.25) * ripple_strength));
	NORMAL = normalize((VIEW_MATRIX * vec4(nrm, 0.0)).xyz);

	vec3 tint = mix(shallow_tint, deep_tint, depth);
	float fres = pow(1.0 - clamp(dot(NORMAL, normalize(VIEW)), 0.0, 1.0), fresnel_power);

	// No screen-texture read and no hand-rolled specular here on purpose. (a) hint_screen_texture inside a
	// material being composited into the same target is fragile, and (b) there is no global LIGHT identifier in a
	// Godot 4 spatial shader -- referencing one fails compilation and Godot silently renders the material opaque
	// white. A non-unshaded spatial shader gets correct PBR lighting and a specular glint from ROUGHNESS/
	// METALLIC/SPECULAR for free, so this only has to supply the tinted translucency and the fresnel rim.
	vec3 col = mix(vec3(1.0), tint, 0.62 + 0.25 * depth);
	col = mix(col, vec3(1.0), fres * fresnel_gain * 0.30);

	ALBEDO = col;
	ALPHA = clamp(mix(film_alpha, pool_alpha, depth) + fres * fresnel_gain * 0.30, 0.0, 1.0);
	ROUGHNESS = mix(0.08, 0.26, depth);
	METALLIC = 0.0;
	SPECULAR = 0.85;
}