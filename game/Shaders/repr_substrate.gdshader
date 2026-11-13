// REPR substrate material (S2). ONE shader for ground tops, cut faces, bedrock and stones; the mesher's vertex
// attributes drive everything (see src/Vivarium.Sim/Repr/Substrate/Mesh/SubstrateMesher.cs header):
//   COLOR.rgb dry albedo (linear), COLOR.a organic | UV2 = (Horizon 1..5, depth below surface m)
//   CUSTOM0 = (saturation, film 0..1 mm, porosity, aggregate cm) | CUSTOM1 = (coarse, clay, sand, root/2000)
// `kind` selects the surface class: 0 ground top, 1 cut face (and dig strips), 2 bedrock top, 3 stones.
// Ground and rock tops take saturation / film / standing water from a per-column float texture (water_tex)
// by world XZ; cut faces use the baked CUSTOM0 values.
//
// Texture taps per fragment (typical): water 1 + for each active set (soil/gravel/rock/litter, usually 1-2)
// 2 albedo (anti-tile pair) + 1 normal = 3  =>  4-7 taps. Worst case (4 sets all active, biplanar edge) 2 x 13 + 1.
// Procedural: Worley 2D (9 cells) x up to 3 octaves (crumb D, crumb 0.4D, desiccation 4 cm), value noise,
// 3D ridged fracture noise on rock, warped-stripe rootlets (cut faces only, gated by root density).
// All patterns are functions of WORLD position. Every octave is faded toward its mean as its wavelength
// approaches 2 pixels (fp = metres per pixel from screen derivatives of world position).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform float kind = 0.0;
uniform float exaggerate : hint_range(0.0, 1.0) = 0.0;
uniform float water_enabled = 0.0;
uniform sampler2D water_tex : filter_linear, repeat_disable, hint_default_black;
uniform vec4 water_rect = vec4(0.0, 0.0, 1.0, 1.0); // origin x, origin z, 1/size x, 1/size z
uniform vec3 bed_normal = vec3(0.0, 1.0, 0.0);
uniform float seed_offset = 0.0;
uniform float film_gain = 2.5;   // sim film is capped at 0.2 mm; this maps it onto 0..1 sheen coverage

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D litter_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D litter_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;

uniform vec4 set_scale = vec4(3.0, 4.0, 6.0, 5.5);   // tiles per metre: soil, gravel, rock, litter
uniform vec3 soil_mean = vec3(0.5);
uniform vec3 gravel_mean = vec3(0.5);
uniform vec3 rock_mean = vec3(0.5);
uniform vec3 litter_mean = vec3(0.5);
uniform float grain_strength = 0.85;
uniform float rock_gain = 0.5;
uniform float puddle_mm = 0.05;
uniform float debug_mode = 0.0;
uniform float normal_strength = 1.0;
uniform float nrm_flip_y = 1.0;

varying vec3 v_wpos;
varying vec4 v_c0;
varying vec4 v_c1;

void vertex() {
	v_wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_c0 = CUSTOM0;
	v_c1 = CUSTOM1;
}

// ------------------------------------------------------------------ hashing and noise
float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}
vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}
float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}
float vnoise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
			mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}
float vnoise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash13(i), hash13(i + vec3(1, 0, 0)), u.x), mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), u.x), u.y);
	float b = mix(mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), u.x), mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), u.x), u.y);
	return mix(a, b, u.z);
}
// Worley 2D: x = F1, y = F2 (cell units), z = id of nearest cell (0..1)
vec3 worley2(vec2 p) {
	vec2 ip = floor(p);
	vec2 fp = fract(p);
	float f1 = 8.0;
	float f2 = 8.0;
	float id = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 o = hash22(ip + g);
			vec2 r = g + 0.12 + 0.76 * o - fp;
			float d = dot(r, r);
			if (d < f1) {
				f2 = f1;
				f1 = d;
				id = hash12(ip + g + 41.0);
			} else if (d < f2) {
				f2 = d;
			}
		}
	}
	return vec3(sqrt(f1), sqrt(f2), id);
}

float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
// 1 while the wavelength is >= 4 px, 0 at <= 2 px.
float fade_oct(float lambda, float fp) { return smoothstep(2.0 * fp, 4.0 * fp, lambda); }

int dom_axis(vec3 n) {
	vec3 a = abs(n);
	if (a.x >= a.y && a.x >= a.z) return 0;
	if (a.y >= a.z) return 1;
	return 2;
}
vec2 plane_uv(vec3 p, int a) {
	if (a == 0) return p.zy;
	if (a == 1) return p.xz;
	return p.xy;
}
// Tangent (u) and bitangent (v) world vectors of a plane projection.
void plane_frame(int a, out vec3 t, out vec3 b) {
	if (a == 0) { t = vec3(0.0, 0.0, 1.0); b = vec3(0.0, 1.0, 0.0); }
	else if (a == 1) { t = vec3(1.0, 0.0, 0.0); b = vec3(0.0, 0.0, 1.0); }
	else { t = vec3(1.0, 0.0, 0.0); b = vec3(0.0, 1.0, 0.0); }
}

vec3 set_col(sampler2D t, vec2 uv, float m) {
	vec3 a = texture(t, uv).rgb;
	vec2 uv2 = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * uv * 0.43 + vec2(0.37, 0.19);
	vec3 b = texture(t, uv2).rgb;
	return mix(a, b, m);
}

// Fine-grain colour ratio (per channel, ~1 on average) and world-space normal delta for one projection plane.
vec3 grain_plane(int a, vec3 p, vec4 sw, out vec3 nd) {
	vec2 uv = plane_uv(p, a);
	float m = smoothstep(0.3, 0.7, vnoise2(uv * 0.9 + 3.1));
	vec3 ratio = vec3(0.0);
	vec2 nxy = vec2(0.0);
	if (sw.x > 0.03) {
		vec2 u = uv * set_scale.x;
		ratio += sw.x * set_col(soil_col, u, m) / soil_mean;
		nxy += sw.x * (texture(soil_nrm, u).xy * 2.0 - 1.0);
	}
	if (sw.y > 0.03) {
		vec2 u = uv * set_scale.y;
		ratio += sw.y * set_col(gravel_col, u, m) / gravel_mean;
		nxy += sw.y * (texture(gravel_nrm, u).xy * 2.0 - 1.0);
	}
	if (sw.z > 0.03) {
		vec2 u = uv * set_scale.z;
		ratio += sw.z * set_col(rock_col, u, m) / rock_mean;
		nxy += sw.z * (texture(rock_nrm, u).xy * 2.0 - 1.0);
	}
	if (sw.w > 0.03) {
		vec2 u = uv * set_scale.w;
		ratio += sw.w * set_col(litter_col, u, m) / litter_mean;
		nxy += sw.w * (texture(litter_nrm, u).xy * 2.0 - 1.0);
	}
	vec3 t;
	vec3 b;
	plane_frame(a, t, b);
	nd = t * nxy.x + b * (nxy.y * nrm_flip_y);
	return ratio;
}

// Derivative bump: tilts n by the screen-space gradient of height h (metres).
vec3 bump_normal(vec3 n, vec3 p, float h) {
	vec3 px = dFdx(p);
	vec3 py = dFdy(p);
	vec3 tx = px - n * dot(n, px);
	vec3 ty = py - n * dot(n, py);
	float a = dot(tx, tx);
	float b = dot(tx, ty);
	float c = dot(ty, ty);
	float det = a * c - b * b;
	float hx = dFdx(h);
	float hy = dFdy(h);
	float ga = 0.0;
	float gb = 0.0;
	if (abs(det) > 1e-14) {
		ga = (hx * c - hy * b) / det;
		gb = (hy * a - hx * b) / det;
	}
	return normalize(n - (tx * ga + ty * gb));
}

// One layer of warped-stripe rootlets; 0..1 coverage. dens is the probability a stripe segment is present.
float root_layer(vec2 p, float ang, float spacing, float seed, float dens, float fp) {
	vec2 dir = vec2(cos(ang), sin(ang));
	vec2 nrm = vec2(-dir.y, dir.x);
	vec2 w = vec2(vnoise2(p * 11.0 + seed), vnoise2(p * 11.0 + seed + 17.3)) - 0.5;
	vec2 w2 = vec2(vnoise2(p * 47.0 + seed * 3.0), vnoise2(p * 47.0 + seed + 5.3)) - 0.5;
	vec2 q = p + w * 0.05 + w2 * 0.008;
	float s = dot(q, nrm) / spacing + seed * 3.7;
	float id = floor(s);
	float f = (fract(s) - 0.5) * spacing;
	float along = dot(q, dir);
	float seg = floor(along / 0.035 + hash12(vec2(id, seed)) * 9.0);
	float present = step(hash12(vec2(id * 1.7 + seed, seg)), dens);
	float wd = (0.00022 + 0.00028 * hash12(vec2(id, seg)));
	float cover = clamp(wd / max(fp, 1e-5) * 1.2, 0.0, 1.0);
	float soft = max(fp, wd * 0.6);
	return (1.0 - smoothstep(wd, wd + soft, abs(f))) * present * cover;
}

void fragment() {
	vec3 N = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 P = v_wpos;
	float fp = max(max(length(dFdx(P)), length(dFdy(P))), 1e-5);
	float X = 1.0 + 2.0 * exaggerate;
	bool is_ground = kind < 0.5;
	bool is_wall = kind > 0.5 && kind < 1.5;
	bool is_rock = kind > 1.5;

	vec3 A = COLOR.rgb;
	float org = COLOR.a;
	float hor = UV2.x;
	float sat = v_c0.x;
	float film = v_c0.y;
	float standing = 0.0;
	float aggCm = v_c0.w;
	float cf = v_c1.x;
	float clay = v_c1.y;
	float sand = v_c1.z;
	float root = v_c1.w;
	if (water_enabled > 0.5 && !is_wall && kind < 2.5) {
		vec2 wuv = (P.xz - water_rect.xy) * water_rect.zw;
		vec3 wt = texture(water_tex, wuv).rgb;
		sat = wt.r;
		film = wt.g;
		standing = wt.b;
	}
	film *= film_gain;

	int ax = dom_axis(N);
	vec2 pp = plane_uv(P, ax) + seed_offset;

	// ---- horizon transition: irregular, 3-10 mm fingers inside the mesher's gradient band
	float hf = fract(hor);
	float band = 4.0 * hf * (1.0 - hf);
	float nbig = vnoise2(pp / 0.006) * 0.65 + vnoise2(pp / 0.0021) * 0.35;
	float nfade = fade_oct(0.004, fp);
	nbig = mix(0.5, nbig, nfade);
	float hf2 = clamp(hf + (nbig - 0.5) * 1.1 * band, 0.0, 1.0);
	float heff = hor;
	if (hf >= 0.0001) heff = floor(hor) + smoothstep(0.35, 0.65, hf2);

	// ---- material set weights
	float rockW = is_rock ? 1.0 : smoothstep(4.25, 4.9, heff);
	float litW = (1.0 - rockW) * (1.0 - smoothstep(1.15, 1.95, heff)) * smoothstep(0.25, 0.6, org);
	float gravW = (1.0 - rockW) * (1.0 - litW) * max(smoothstep(0.08, 0.5, cf), smoothstep(3.4, 4.3, heff) * 0.6);
	float soilW = max(0.0, 1.0 - rockW - litW - gravW);
	vec4 sw = vec4(soilW, gravW, rockW, litW);

	// ---- fine grain from the existing PBR sets (biplanar blend only near 45 degrees)
	vec3 aw = pow(abs(N), vec3(8.0));
	aw /= max(aw.x + aw.y + aw.z, 1e-5);
	int a1 = ax == 0 ? (aw.y >= aw.z ? 1 : 2) : (ax == 1 ? (aw.x >= aw.z ? 0 : 2) : (aw.x >= aw.y ? 0 : 1));
	vec3 nd0;
	vec3 grain = grain_plane(ax, P, sw, nd0);
	vec3 nd = nd0;
	float w1 = aw[a1];
	if (w1 > 0.1) {
		vec3 nd1;
		vec3 g1 = grain_plane(a1, P, sw, nd1);
		float k = w1 / (w1 + aw[ax]);
		grain = mix(grain, g1, k);
		nd = mix(nd0, nd1, k);
	}
	grain = mix(vec3(luma(grain)), grain, 0.25);
	grain = clamp(vec3(1.0) + (grain - vec3(1.0)) * 0.6, vec3(0.55), vec3(1.4));

	// ---- aggregates, pores, cracks, sand
	float D = max(aggCm * 0.01, 0.0006);
	float sandy = smoothstep(0.22, 0.55, sand) * (1.0 - smoothstep(0.2, 0.6, org)) * (1.0 - rockW);
	float aggStr = (1.0 - rockW) * clamp(org * 1.3 + clay * 1.2, 0.0, 1.0) * (1.0 - sandy);
	aggStr *= 1.0 - 0.75 * litW;
	float crumb = 0.55;
	float edge = 0.28;
	float cid = 0.5;
	if (aggStr > 0.01 && !is_rock) {
		vec2 pw = pp + (vec2(vnoise2(pp / (D * 2.3)), vnoise2(pp / (D * 2.3) + 9.1)) - 0.5) * D * 0.9;
		vec3 wa = worley2(pw / D);
		float fa = fade_oct(D, fp);
		float e1 = 1.0 - smoothstep(0.0, 0.14, wa.y - wa.x);
		float c1h = smoothstep(0.0, 0.5, wa.y - wa.x);
		vec3 wb = worley2(pw / (D * 0.4) + 11.3);
		float fb = fade_oct(D * 0.4, fp);
		float e2 = 1.0 - smoothstep(0.0, 0.25, wb.y - wb.x);
		float c2h = smoothstep(0.0, 0.5, wb.y - wb.x);
		crumb = 0.65 * mix(0.55, c1h, fa) + 0.35 * mix(0.55, c2h, fb);
		edge = max(mix(0.28, e1, fa), 0.6 * mix(0.28, e2, fb));
		cid = mix(0.5, wa.z, fa);
	}
	float sparkle = 0.5;
	float sparkleHi = 0.0;
	if (sandy > 0.01) {
		vec2 sp = pp / 0.0009;
		float fs = fade_oct(0.0018, fp);
		float hs = hash12(floor(sp));
		sparkle = mix(0.5, hs, fs);
		sparkleHi = step(0.985, hs) * fs;
	}
	float crack = 0.0;
	if (is_ground) {
		float clayish = clay / max(1.0 - org - cf, 0.1);
		float crackStr = smoothstep(0.12, 0.3, clayish) * (1.0 - smoothstep(0.3, 0.75, org)) * (1.0 - smoothstep(0.35, 0.85, sat)) * (1.0 - rockW);
		crackStr = clamp(crackStr * X, 0.0, 1.0);
		if (crackStr > 0.01) {
			vec2 pc = pp + (vec2(vnoise2(pp / 0.03), vnoise2(pp / 0.03 + 4.0)) - 0.5) * 0.02;
			vec3 wc = worley2(pc / 0.04 + 5.7);
			float fc = fade_oct(0.04, fp);
			float soft = max(0.03, fp / 0.04);
			crack = (1.0 - smoothstep(0.02, 0.02 + soft, wc.y - wc.x)) * crackStr * fc;
		}
	}

	// ---- bedrock: lamination and fractures
	float lamMod = 1.0;
	float fracture = 0.0;
	if (rockW > 0.01) {
		float bt = dot(P, bed_normal);
		float warp = vnoise3(P * 28.0) - 0.5;
		float lf = sin(6.2831853 * bt / 0.0045 + 3.0 * warp);
		float lc = sin(6.2831853 * bt / 0.032 + 1.5 * warp);
		float ln = vnoise2(vec2(bt / 0.0045, 0.0)) - 0.5; // per-lamina tone
		lamMod = 1.0 + rockW * (0.05 * lf * fade_oct(0.0045, fp) * X + 0.07 * lc * fade_oct(0.032, fp) * X + 0.10 * ln * fade_oct(0.0045, fp));
		float f1n = 9.0;
		float rn = vnoise3(P * f1n + 3.3);
		float ridge = 1.0 - abs(2.0 * rn - 1.0);
		float wdt = 0.012 + fp * f1n * 1.2;
		float fr1 = smoothstep(1.0 - wdt, 1.0, ridge) * (1.0 - smoothstep(0.1, 0.3, fp * f1n));
		float f2n = 24.0;
		float rn2 = vnoise3(P * f2n + 8.1);
		float ridge2 = 1.0 - abs(2.0 * rn2 - 1.0);
		float wdt2 = 0.010 + fp * f2n * 1.2;
		float fr2 = smoothstep(1.0 - wdt2, 1.0, ridge2) * (1.0 - smoothstep(0.1, 0.3, fp * f2n));
		fracture = clamp(max(fr1, 0.7 * fr2) * rockW, 0.0, 1.0);
	}

	// ---- base colour assembly (dry)
	vec3 alb = A * mix(vec3(1.0), grain, grain_strength);
	float tone = 1.0 + aggStr * 0.5 * X * ((crumb - 0.55) * 0.8 + (cid - 0.5) * 0.35);
	alb *= max(tone, 0.3);
	alb *= 1.0 + sandy * (sparkle - 0.5) * 0.3;
	alb += vec3(sparkleHi * 0.06);
	alb *= 1.0 - 0.65 * crack;
	alb *= lamMod * (1.0 - 0.4 * fracture);
	alb *= mix(1.0, rock_gain, rockW);
	alb *= 1.0 + band * (nbig - 0.5) * 0.55;

	// ---- wetness
	float sE = clamp(sat, 0.0, 1.0);
	float darkAmt = is_rock ? 0.12 : mix(0.82, 0.55, smoothstep(0.45, 0.7, org)) + 0.25 * litW;
	darkAmt += 0.12 * sandy;
	darkAmt *= (1.0 - 0.5 * rockW);
	darkAmt = clamp(darkAmt * X, 0.0, 0.92);
	float poreFill = clamp(sE * 0.55 + film * 0.8, 0.0, 1.0);
	float poreDark = 1.0 - 0.6 * edge * aggStr * X * (1.0 - 0.8 * poreFill);
	alb *= max(poreDark, 0.15);
	alb *= 1.0 - darkAmt * sE;
	alb *= mix(1.0, 0.62, litW);
	alb *= mix(1.0, 0.72, gravW);
	alb *= mix(1.0, 0.62, smoothstep(3.2, 4.4, heff) * (1.0 - rockW));
	alb *= mix(1.1, 1.0, sE);
	float lw = luma(alb);
	alb = mix(vec3(lw), alb, 1.0 + 0.3 * sE * min(X, 2.0));

	float rough = mix(0.95, 0.78, rockW);
	rough += 0.08 * edge * aggStr * (1.0 - poreFill);
	rough *= 1.0 + sandy * (sparkle - 0.5) * 0.25;
	float wetRough = mix(0.60, 0.40, rockW);
	rough = mix(rough, wetRough, clamp(sE * X * (is_rock ? 0.5 : 1.0), 0.0, 1.0));
	rough = clamp(rough, 0.05, 1.0);

	// hollow-first film
	float fl = clamp(film * X, 0.0, 1.0);
	fl = max(fl, smoothstep(0.88, 1.0, sE) * 0.4);
	float hollow = is_rock ? (1.0 - vnoise2(pp / 0.012)) : clamp(0.55 * edge * min(aggStr * 4.0, 1.0) + 0.45 * (1.0 - crumb) + 0.0, 0.0, 1.0);
	hollow = clamp(hollow * 0.8 + 0.2 * vnoise2(pp / 0.03), 0.0, 1.0);
	float thr = 1.0 - fl * 1.3;
	float cov = smoothstep(thr - 0.02, thr + 0.2, hollow) * step(0.0005, fl);
	alb *= 1.0 - 0.18 * cov;
	rough = mix(rough, 0.08, cov);

	// ---- roots on cut faces
	float rootMask = 0.0;
	if (is_wall && root > 0.03 && heff > 1.6 && heff < 4.6) {
		float dens = clamp(root * 4.0, 0.0, 1.0) * 0.9;
		float r1 = root_layer(pp, 1.45, 0.034, 1.0, dens, fp);
		float r2 = root_layer(pp, 1.75, 0.047, 2.0, dens, fp);
		rootMask = max(r1, r2);
	}
	vec3 rootCol = vec3(0.50, 0.40, 0.27) * (1.0 - 0.45 * sE);
	alb = mix(alb, rootCol, rootMask * 0.9);
	rough = mix(rough, 0.7, rootMask);

	// ---- normals: texture grain, derivative bump of crumb relief, flattened as water fills pores
	float flatten = 1.0 - clamp(0.85 * fl + 0.5 * poreFill, 0.0, 1.0);
	float relief = ((crumb - 0.55) * 0.3 - edge * 0.18 * min(aggStr * 4.0, 1.0)) * D * aggStr * X - 0.0006 * crack - 0.0012 * fracture;
	vec3 Nb = normalize(N + nd * normal_strength * 0.55 * flatten * (1.0 - 0.3 * rockW));
	Nb = bump_normal(Nb, P, relief * 3.0 * flatten);
	Nb = normalize(mix(N, Nb, flatten));
	Nb = normalize(mix(Nb, N, cov));

	// ---- standing water
	float pud = smoothstep(puddle_mm * 0.25, puddle_mm, standing * (0.6 + 0.8 * hollow)) * step(0.004, standing);
	vec3 pudCol = alb * 0.12 + vec3(0.012, 0.018, 0.016);
	alb = mix(alb, pudCol, pud);
	rough = mix(rough, 0.03, pud);
	Nb = normalize(mix(Nb, vec3(0.0, 1.0, 0.0), pud));

	float ao = 1.0 - 0.55 * edge * aggStr * X * (1.0 - 0.7 * poreFill) - 0.4 * crack - 0.5 * fracture * 0.6 - 0.35 * rootMask;
	ao = clamp(ao, 0.15, 1.0);

	if (debug_mode > 0.5) alb = vec3(sE, clamp(film, 0.0, 1.0), clamp(standing * 10.0, 0.0, 1.0));
	ALBEDO = clamp(alb, vec3(0.0), vec3(1.0));
	ROUGHNESS = rough;
	SPECULAR = mix(0.28, 0.5, max(max(sE * 0.6, cov), pud));
	METALLIC = 0.0;
	AO = ao;
	AO_LIGHT_AFFECT = 0.6;
	NORMAL = normalize((VIEW_MATRIX * vec4(Nb, 0.0)).xyz);
}
