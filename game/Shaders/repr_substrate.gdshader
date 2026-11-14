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
	// Prevent directional shadow-map texel self-shadowing (shadow acne) on macro ground & 3D litter views
	// while preserving full shadow casting onto the specimen base plane and from rock outcrops/stones.
	if (PROJECTION_MATRIX[3][3] > 0.5 && (kind < 0.5 || kind > 3.5)) {
		VERTEX.y -= 0.018;
	}
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
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), u.x),
			mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), u.x), u.y);
}
float vnoise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
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

vec2 set_nrm(sampler2D t, vec2 uv, float m) {
	vec2 na = texture(t, uv).xy * 2.0 - 1.0;
	vec2 uv2 = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * uv * 0.43 + vec2(0.37, 0.19);
	vec2 nb = texture(t, uv2).xy * 2.0 - 1.0;
	nb = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * nb;
	return mix(na, nb, m);
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
		nxy += sw.x * set_nrm(soil_nrm, u, m);
	}
	if (sw.y > 0.03) {
		vec2 u = uv * set_scale.y;
		ratio += sw.y * set_col(gravel_col, u, m) / gravel_mean;
		nxy += sw.y * set_nrm(gravel_nrm, u, m);
	}
	if (sw.z > 0.03) {
		vec2 u = uv * set_scale.z;
		ratio += sw.z * set_col(rock_col, u, m) / rock_mean;
		nxy += sw.z * set_nrm(rock_nrm, u, m);
	}
	if (sw.w > 0.03) {
		vec2 u = uv * set_scale.w;
		vec3 lcol = set_col(litter_col, u, m) / litter_mean;
		lcol = mix(vec3(luma(lcol)), lcol, 0.30);
		lcol = mix(vec3(1.0), lcol, 0.32);
		ratio += sw.w * lcol;
		nxy += sw.w * set_nrm(litter_nrm, u, m) * 0.75;
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
	float wd = (0.00026 + 0.00032 * hash12(vec2(id, seg)));
	float cover = clamp(wd / max(fp, 1e-5) * 1.3, 0.0, 1.0);
	float soft = max(fp, wd * 0.65);
	float prof = clamp(1.0 - (f * f) / max(wd * wd, 1e-9), 0.0, 1.0);
	return (1.0 - smoothstep(wd, wd + soft, abs(f))) * (0.55 + 0.45 * sqrt(prof)) * present * cover;
}

void fragment() {
	vec3 N = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 P = v_wpos;
	float fp = max(max(length(dFdx(P)), length(dFdy(P))), 1e-5);
	float X = 1.0 + 2.0 * exaggerate;
	bool is_ground = kind < 0.5;
	bool is_wall = kind > 0.5 && kind < 1.5;
	bool is_rock = kind > 1.5 && kind < 3.5;
	bool is_stone = kind > 2.5 && kind < 3.5;
	bool is_litter3d = kind > 3.5;

	vec3 A = COLOR.rgb;
	float org = COLOR.a;
	float hor = UV2.x;
	float depth_m = UV2.y;
	float sat = v_c0.x;
	float film = v_c0.y;
	float standing = 0.0;
	float aggCm = v_c0.w;
	float cf = v_c1.x;
	float clay = v_c1.y;
	float sand = v_c1.z;
	float root = v_c1.w;
	if (water_enabled > 0.5 && !is_wall) {
		// Domain-warp the 20x20 column water texture lookup so column boundaries never show as 5 cm grid blocks
		vec2 wwarp = (vec2(vnoise2(P.xz * 19.0 + 1.7), vnoise2(P.xz * 19.0 + 9.4)) - 0.5) * 0.036
		           + (vec2(vnoise2(P.xz * 53.0 + 4.2), vnoise2(P.xz * 53.0 + 13.8)) - 0.5) * 0.014;
		vec2 wuv = clamp(((P.xz + wwarp) - water_rect.xy) * water_rect.zw, vec2(0.01), vec2(0.99));
		vec3 wt = texture(water_tex, wuv).rgb;
		if (is_stone) {
			float surfW = 1.0 - smoothstep(0.0, 0.035, depth_m);
			sat = mix(sat, wt.r, surfW);
			film = wt.g * surfW;
		} else {
			sat = wt.r;
			film = wt.g;
			standing = is_ground ? wt.b : 0.0;
		}
	}
	film *= film_gain;

	// ---- 3D attached litter geometry (curled leaves UV2.x==6 and woody twigs UV2.x==7)
	if (is_litter3d) {
		bool is_twig = hor > 6.5;
		vec2 luv = UV;
		float edgeDist = clamp(depth_m, 0.0, 1.0);
		float sE = clamp(sat, 0.0, 1.0);
		float fl = clamp(film * X + smoothstep(0.82, 1.0, sE) * 0.35, 0.0, 1.0);
		vec3 alb = A;
		float roughL = 0.82;
		float aoL = 1.0;

		vec2 texU = P.xz * 8.5 + luv * 0.06;
		vec3 gTex = texture(soil_col, texU).rgb / max(soil_mean, vec3(0.05));
		gTex = mix(vec3(luma(gTex)), gTex, 0.25);
		gTex = clamp(vec3(1.0) + (gTex - vec3(1.0)) * 0.42, vec3(0.68), vec3(1.30));
		vec2 nxyL = (texture(soil_nrm, texU).xy * 2.0 - 1.0) * 0.55;

		if (!is_twig) {
			// Curled fallen leaf blade: central midrib + organic leaf-tissue mottle + weathered margin darkening
			float midrib = exp(-abs(luv.x) * 10.0);
			float mottle = (vnoise2(P.xz * 95.0 + 1.7) - 0.5) * 0.18 + (vnoise2(P.xz * 210.0 + 6.3) - 0.5) * 0.10;
			float spot = smoothstep(0.56, 0.80, vnoise2(P.xz * 52.0 + 3.1));
			alb *= gTex * (1.0 + 0.10 * midrib + mottle - 0.20 * spot);
			alb *= mix(1.0, 0.72, smoothstep(0.55, 1.0, edgeDist));
			nxyL.x += -sign(luv.x) * midrib * 0.22;
			roughL = mix(0.82, 0.22, clamp(sE * 0.65 + fl * 0.85, 0.0, 1.0));
			aoL = mix(0.64, 1.0, smoothstep(0.0, 0.50, edgeDist));
		} else {
			// 3D woody twig / bark sliver: longitudinal bark grain
			alb *= gTex;
			roughL = mix(0.88, 0.28, clamp(sE * 0.60 + fl * 0.80, 0.0, 1.0));
			aoL = mix(0.58, 1.0, 1.0 - edgeDist * 0.45);
		}

		alb *= 1.0 - 0.56 * sE;
		vec3 NbL = normalize(N + vec3(nxyL.x, 0.0, nxyL.y) * (1.0 - 0.5 * fl));
		ALBEDO = clamp(alb, vec3(0.0), vec3(1.0));
		ROUGHNESS = clamp(roughL, 0.08, 1.0);
		SPECULAR = mix(0.26, 0.52, max(sE * 0.6, fl));
		METALLIC = 0.0;
		AO = aoL;
		AO_LIGHT_AFFECT = 0.68;
		NORMAL = normalize((VIEW_MATRIX * vec4(NbL, 0.0)).xyz);
	} else {
		int ax = dom_axis(N);
		vec2 pp = plane_uv(P, ax) + seed_offset;

		// ---- horizon transition: irregular, 4-12 mm organic fingers inside the gradient band
		float hf = fract(hor);
		float band = 4.0 * hf * (1.0 - hf);
		float nbig = vnoise2(pp / 0.018) * 0.55 + vnoise2(pp / 0.0055) * 0.30 + vnoise2(pp / 0.0020) * 0.15;
		float nfade = fade_oct(0.004, fp);
		nbig = mix(0.5, nbig, nfade);
		float hf2 = clamp(hf + (nbig - 0.5) * 1.15 * band, 0.0, 1.0);
		float heff = hor;
		if (hf >= 0.0001) heff = floor(hor) + smoothstep(0.32, 0.68, hf2);
		else if (is_wall) {
			// Perturb horizon index slightly with world-space organic noise so cut-face transitions finger naturally
			heff = clamp(hor + (nbig - 0.5) * 0.42 * fade_oct(0.012, fp), 1.0, 5.0);
		}

		// ---- material set weights (soil/gravel/rock PBR lead; 2D litter_col kept subtle under 3D litter)
		float rockW = is_rock ? 1.0 : smoothstep(4.35, 4.95, heff);
		float litW = is_ground ? (1.0 - rockW) * (1.0 - smoothstep(1.15, 1.95, heff)) * smoothstep(0.30, 0.65, org) * 0.45 : 0.0;
		float gravW = (1.0 - rockW) * (1.0 - litW) * max(smoothstep(0.12, 0.55, cf), smoothstep(3.4, 4.4, heff) * 0.50);
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
		grain = mix(vec3(luma(grain)), grain, 0.30);
		grain = clamp(vec3(1.0) + (grain - vec3(1.0)) * 0.68, vec3(0.52), vec3(1.42));

		// ---- rounded soil aggregates, pores, dry-only shrinkage fissures, sand
		float D = max(aggCm * 0.01, 0.0012);
		float sandy = smoothstep(0.22, 0.55, sand) * (1.0 - smoothstep(0.2, 0.6, org)) * (1.0 - rockW);
		float aggStr = (1.0 - rockW) * clamp(org * 1.15 + clay * 1.1, 0.0, 1.0) * (1.0 - sandy);
		float crumb = 0.55;
		float edge = 0.16;
		float cid = 0.5;
		if (aggStr > 0.01 && !is_rock) {
			vec2 pr1 = mat2(vec2(0.80, -0.60), vec2(0.60, 0.80)) * pp;
			vec2 pw = pr1 + (vec2(vnoise2(pr1 / (D * 1.9)), vnoise2(pr1 / (D * 1.9) + 9.1)) - 0.5) * D * 1.15;
			vec3 wa = worley2(pw / D);
			float fa = fade_oct(D, fp);
			vec3 wb = worley2(pw / (D * 0.45) + 11.3);
			float fb = fade_oct(D * 0.45, fp);
			// Rounded convex soil granules (from F1 distance wa.x, NEVER connected Voronoi cell-border lines wa.y-wa.x)
			float c1h = 1.0 - smoothstep(0.10, 0.62, wa.x);
			float c2h = 1.0 - smoothstep(0.10, 0.60, wb.x);
			crumb = 0.62 * mix(0.55, c1h, fa) + 0.38 * mix(0.55, c2h, fb);
			// Discrete inter-crumb pore pockets at triple-junction interstices (where both F1 distances are large)
			float p1 = smoothstep(0.42, 0.72, wa.x) * smoothstep(0.32, 0.68, wb.x);
			float p2 = smoothstep(0.45, 0.74, wb.x) * smoothstep(0.40, 0.75, vnoise2(pr1 / (D * 0.55) + 4.7));
			edge = 0.65 * mix(0.16, p1, fa) + 0.35 * mix(0.16, p2, fb);
			cid = mix(0.5, wa.z, fa);
		}
		float sparkle = 0.5;
		float sparkleHi = 0.0;
		if (sandy > 0.01) {
			vec2 sp = pp / 0.0009;
			float fs = fade_oct(0.0018, fp);
			float hs = hash12(floor(sp));
			sparkle = mix(0.5, hs, fs);
			sparkleHi = step(0.988, hs) * fs;
		}
		float crack = 0.0;
		if (is_ground && org < 0.16) {
			float clayish = clay / max(1.0 - org - cf, 0.1);
			// Desiccation cracks open ONLY in bare mineral clay sub-soil (org < 0.16) when dry (sat < 0.25)
			float crackStr = smoothstep(0.18, 0.35, clayish) * (1.0 - smoothstep(0.08, 0.16, org)) * (1.0 - smoothstep(0.14, 0.25, sat)) * (1.0 - rockW);
			crackStr = clamp(crackStr * X, 0.0, 1.0);
			if (crackStr > 0.01) {
				vec2 pc = pp + (vec2(vnoise2(pp / 0.028), vnoise2(pp / 0.028 + 4.0)) - 0.5) * 0.022;
				vec3 wc = worley2(pc / 0.042 + 5.7);
				float fc = fade_oct(0.042, fp);
				float patchMask = smoothstep(0.42, 0.72, vnoise2(pp / 0.085 + 2.3));
				float soft = max(0.025, fp / 0.042);
				crack = (1.0 - smoothstep(0.015, 0.015 + soft, wc.y - wc.x)) * patchMask * crackStr * fc;
			}
		}

		// ---- bedrock & stones: natural strata bedding and subtle joint crevices (no closed vnoise3 contour loops)
		float lamMod = 1.0;
		float fracture = 0.0;
		if (rockW > 0.01) {
			float bt = dot(P, bed_normal);
			float warp = (vnoise3(P * 18.0) - 0.5) * 0.6 + (vnoise3(P * 42.0 + 5.2) - 0.5) * 0.4;
			float lf = sin(6.2831853 * bt / 0.0065 + 2.2 * warp);
			float lc = sin(6.2831853 * bt / 0.036 + 1.4 * warp);
			float ln = vnoise2(vec2(bt / 0.008, dot(P.xz, vec2(0.7, -0.7)) * 4.0)) - 0.5;
			lamMod = 1.0 + rockW * (0.04 * lf * fade_oct(0.0065, fp) * X + 0.06 * lc * fade_oct(0.036, fp) * X + 0.08 * ln * fade_oct(0.008, fp));

			// Subtle bedding-parting and conjugate joint crevices
			vec3 jDir1 = normalize(cross(bed_normal, vec3(0.62, 0.0, 0.78)));
			float j1 = abs(fract(dot(P, jDir1) / 0.075 + warp * 0.28) - 0.5);
			float bPart = abs(fract(bt / 0.032 + warp * 0.18) - 0.5);
			float mask1 = smoothstep(0.48, 0.78, vnoise3(P * 14.0 + 1.9));
			float w1j = max(0.022, fp / 0.075 * 1.5);
			float fJoint1 = (1.0 - smoothstep(0.0, w1j, j1)) * mask1 * 0.55 * fade_oct(0.075, fp);
			float fBed = (1.0 - smoothstep(0.0, max(0.025, fp / 0.032 * 1.5), bPart)) * mask1 * 0.65 * fade_oct(0.032, fp);
			fracture = clamp(max(fJoint1, fBed) * rockW, 0.0, 1.0);
		}

		// ---- base colour assembly (dry)
		if (is_rock && !is_stone) {
			// Continuous world-space rock palette across the entire Rock mesh (zero sawtooth interpolation at contact)
			float bt = dot(P, bed_normal);
			float rBand = 0.90 + 0.10 * sin(6.2831853 * bt / 0.038);
			float rPatch = 0.92 + 0.16 * vnoise3(P * 9.0);
			A = vec3(0.33, 0.30, 0.27) * rBand * rPatch;
		}
		vec3 alb = A * mix(vec3(1.0), grain, grain_strength);
		float tone = 1.0 + aggStr * 0.38 * X * ((crumb - 0.55) * 0.65 + (cid - 0.5) * 0.25);
		alb *= max(tone, 0.35);
		alb *= 1.0 + sandy * (sparkle - 0.5) * 0.26;
		alb += vec3(sparkleHi * 0.045);
		alb *= 1.0 - 0.58 * crack;
		alb *= lamMod * (1.0 - 0.16 * fracture);
		alb *= mix(1.0, rock_gain * 1.25, rockW);
		alb *= 1.0 + band * (nbig - 0.5) * 0.45;

		// Damp soil-contact collar on embedded stones
		if (is_stone) {
			float collar = 1.0 - smoothstep(-0.002, 0.012, -depth_m);
			alb *= 1.0 - 0.28 * collar;
		}

		// ---- wetness
		float sE = clamp(sat, 0.0, 1.0);
		float darkAmt = is_rock ? 0.32 : mix(0.64, 0.46, smoothstep(0.40, 0.75, org));
		darkAmt += 0.10 * sandy;
		darkAmt = clamp(darkAmt * X, 0.0, 0.78);
		float poreFill = clamp(sE * 0.55 + film * 0.8, 0.0, 1.0);
		float poreDark = 1.0 - 0.42 * edge * aggStr * X * (1.0 - 0.75 * poreFill);
		alb *= max(poreDark, 0.24);
		alb *= 1.0 - darkAmt * sE;
		alb *= mix(1.08, 1.0, sE);
		float lw = luma(alb);
		alb = mix(vec3(lw), alb, 1.0 + 0.25 * sE * min(X, 2.0));

		float rough = mix(0.92, 0.76, rockW);
		rough += 0.06 * edge * aggStr * (1.0 - poreFill);
		rough *= 1.0 + sandy * (sparkle - 0.5) * 0.20;
		float wetRough = mix(0.42, 0.28, rockW);
		rough = mix(rough, wetRough, clamp(sE * X * (is_rock ? 0.65 : 1.0), 0.0, 1.0));
		rough = clamp(rough, 0.05, 1.0);

		// ---- hollow-first film: driven by rotated multi-scale micro-depressions, NEVER sharp Voronoi cell edges
		float fl = clamp(film * X, 0.0, 1.0);
		fl = max(fl, smoothstep(0.84, 1.0, sE) * 0.42);
		vec2 pRotA = mat2(vec2(0.80, -0.60), vec2(0.60, 0.80)) * pp;
		vec2 pRotB = mat2(vec2(0.60, 0.80), vec2(-0.80, 0.60)) * pp;
		float microDepr = 1.0 - (0.52 * vnoise2(pRotA / 0.055 + 1.3) + 0.32 * vnoise2(pRotB / 0.021 + 6.7) + 0.16 * vnoise2(pp / 0.007 + 11.2));
		float hollow = clamp(microDepr * 1.15 - 0.08, 0.0, 1.0);
		float thr = 1.0 - fl * 1.25;
		float cov = smoothstep(thr - 0.08, thr + 0.18, hollow) * smoothstep(0.0002, 0.0025, fl);
		alb *= 1.0 - 0.15 * cov;
		rough = mix(rough, 0.09, cov);

		// ---- roots on cut faces (with 3D cylindrical normal relief and contact AO)
		float rootMask = 0.0;
		if (is_wall && root > 0.03 && heff > 1.4 && heff < 4.6) {
			float dens = clamp(root * 4.2, 0.0, 1.0) * 0.92;
			float r1 = root_layer(pp, 1.45, 0.032, 1.0, dens, fp);
			float r2 = root_layer(pp, 1.75, 0.045, 2.0, dens, fp);
			float r3 = root_layer(pp, 1.22, 0.022, 3.3, dens * 0.75, fp) * 0.8;
			rootMask = max(max(r1, r2), r3);
		}
		vec3 rootCol = vec3(0.46, 0.35, 0.23) * (1.0 - 0.42 * sE);
		alb = mix(alb, rootCol, rootMask * 0.92);
		rough = mix(rough, 0.65, rootMask);

		// ---- normals: smooth anisotropic PBR normal grain + rotated clod slope (no axis-aligned or 2x2 quad striping)
		float flatten = 1.0 - clamp(0.75 * cov + 0.35 * poreFill, 0.0, 1.0);
		vec3 tFrame;
		vec3 bFrame;
		plane_frame(ax, tFrame, bFrame);
		float clodGx = (vnoise2((pRotA + vec2(0.0035, 0.0)) / 0.022) - vnoise2((pRotA - vec2(0.0035, 0.0)) / 0.022)) * 0.16 * aggStr * fade_oct(0.022, fp);
		float clodGy = (vnoise2((pRotB + vec2(0.0, 0.0035)) / 0.025) - vnoise2((pRotB - vec2(0.0, 0.0035)) / 0.025)) * 0.16 * aggStr * fade_oct(0.025, fp);
		vec3 clodN = (tFrame * clodGx + bFrame * clodGy) * flatten;
		vec3 Nb = normalize(N + (nd * normal_strength * 0.85 * (1.0 - 0.20 * rockW) + clodN) * flatten);
		Nb = normalize(mix(Nb, N, cov * 0.85));

		// ---- standing water (smooth organic depressions, no hard step threshold)
		float pud = smoothstep(puddle_mm * 0.20, puddle_mm * 1.25, standing * (0.50 + 0.85 * hollow));
		vec3 pudCol = alb * 0.45 + vec3(0.010, 0.014, 0.012);
		alb = mix(alb, pudCol, pud);
		rough = mix(rough, 0.04, pud);
		Nb = normalize(mix(Nb, vec3(0.0, 1.0, 0.0), pud * 0.92));

		float ao = 1.0 - 0.42 * edge * aggStr * X * (1.0 - 0.65 * poreFill) - 0.42 * crack - 0.22 * fracture - 0.32 * rootMask;
		ao = clamp(ao, 0.20, 1.0);

		if (debug_mode > 0.5) alb = vec3(sE, clamp(film, 0.0, 1.0), clamp(standing * 10.0, 0.0, 1.0));
		ALBEDO = clamp(alb, vec3(0.0), vec3(1.0));
		ROUGHNESS = rough;
		SPECULAR = mix(0.28, 0.52, max(max(sE * 0.65, cov), pud));
		METALLIC = 0.0;
		AO = ao;
		AO_LIGHT_AFFECT = 0.6;
		NORMAL = normalize((VIEW_MATRIX * vec4(Nb, 0.0)).xyz);
	}
}
