shader_type spatial;

// Authored monumental sculpture. The mesh supplies the continuous silhouette
// and carved planes; this shader gives those planes the same faded, world-space
// stone tooth as the voxel world without drawing a one-metre grid over them.
render_mode cull_back, diffuse_burley, specular_disabled;

uniform vec4 base_colour : source_color = vec4(0.86, 0.84, 0.92, 1.0);
uniform float weathering : hint_range(0.0, 1.0) = 0.62;
uniform float moss_mix : hint_range(0.0, 1.0) = 0.0;
uniform vec4 moss_colour : source_color = vec4(0.66, 0.71, 0.54, 1.0);
uniform vec3 warm_tint : source_color = vec3(1.02, 1.00, 0.985);
uniform vec3 cool_tint : source_color = vec3(0.945, 0.958, 1.06);
global uniform vec3 pf_sun_dir;

varying vec3 v_world;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1, 0)), f.x),
	           mix(hash21(i + vec2(0, 1)), hash21(i + vec2(1, 1)), f.x), f.y);
}

float fbm(vec2 p) {
	float sum = 0.0, amp = 0.5, norm = 0.0;
	mat2 rot = mat2(vec2(0.86, 0.51), vec2(-0.51, 0.86));
	for (int i = 0; i < 4; i++) {
		sum += vnoise(p) * amp;
		norm += amp;
		p = rot * p * 2.07 + 17.3;
		amp *= 0.5;
	}
	return sum / norm;
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 wn = normalize((INV_VIEW_MATRIX * vec4(normalize(NORMAL), 0.0)).xyz);
	vec3 an = abs(wn);
	vec2 uv = an.y > max(an.x, an.z) ? v_world.xz
		: (an.x > an.z ? vec2(v_world.z, v_world.y) : vec2(v_world.x, v_world.y));

	vec2 warped = uv * 0.78 + vec2(
		vnoise(uv * 0.23 + 13.1) - 0.5,
		vnoise(uv * 0.23 - 29.7) - 0.5) * 0.9;
	float broad = fbm(warped * 0.43);
	float blotch = fbm(warped * 1.28);
	float tooth = fbm(warped * 4.6);
	float stain = clamp((broad - 0.50) * 0.24 + (blotch - 0.5) * 0.30
		+ (tooth - 0.5) * 0.08, -0.13, 0.14) * weathering;

	vec3 base = base_colour.rgb;
	float moss_field = smoothstep(0.53, 0.76,
		broad * 0.68 + blotch * 0.32 + (1.0 - clamp(wn.y, 0.0, 1.0)) * 0.08);
	base = mix(base, moss_colour.rgb, moss_field * moss_mix);
	base *= 1.0 - stain;

	// Keep the reference's readable top/side separation on non-axis-aligned
	// planes. It is intentionally gentler than the terrain ramp: the sculpture's
	// bevels and facial planes need to turn continuously rather than posterise.
	float face_shade = mix(0.88, 1.0, clamp(wn.y * 0.62 + 0.50, 0.0, 1.0));
	face_shade *= mix(0.94, 1.0, abs(wn.x));
	base *= face_shade;

	float facing = dot(wn, normalize(pf_sun_dir));
	vec3 tint = mix(cool_tint, warm_tint, smoothstep(-0.6, 0.9, facing));
	ALBEDO = base * mix(vec3(1.0), tint, 0.38);
	ROUGHNESS = 0.94;
	SPECULAR = 0.0;
	METALLIC = 0.0;
}
