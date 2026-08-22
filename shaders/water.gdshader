shader_type spatial;

// Water.
//
// Lavender rather than the blue-green of real water: the whole basin is lilac
// stone and rose blossom, and a cyan river reads as a foreign object cutting
// through it. Violet lets the water belong to the same world while still being
// the coolest, most saturated thing on the ground plane.
//
// Depth comes from the opaque depth buffer rather than a baked map, so the
// shoreline is always exactly where the terrain actually is.
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 shoal : source_color = vec3(0.812, 0.761, 0.902);
uniform vec3 shallow : source_color = vec3(0.608, 0.541, 0.831);
uniform vec3 deep : source_color = vec3(0.357, 0.310, 0.620);
uniform vec3 warm : source_color = vec3(0.890, 0.769, 0.788);
uniform vec3 sheen : source_color = vec3(0.910, 0.886, 0.961);
uniform float opacity : hint_range(0.0, 1.0) = 0.88;
uniform float density = 0.23;
uniform float saturation = 1.20;
uniform float caustic = 0.60;
uniform float cell_tone = 0.055;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

float h21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x),
	           mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}

void fragment() {
	// How much water the view ray travelled through, from the opaque depth.
	float raw = texture(depth_tex, SCREEN_UV).r;
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw);
	vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	float bed = -(view.z / view.w);
	float surface = -VERTEX.z;
	float through = max(bed - surface, 0.0);

	vec3 wpos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;

	// Colour saturates hard with depth; coverage rises far more slowly, because
	// the riverbed is half the appeal of looking at this water at all.
	float t = 1.0 - exp(-through * 0.085);
	vec3 col = mix(shoal, shallow, smoothstep(0.0, 0.45, t));
	col = mix(col, deep, smoothstep(0.35, 1.0, t));
	// Sunlit ground throwing warmth back up through the first half-metre.
	col = mix(warm, col, smoothstep(0.0, 0.22, t));

	// Drifting sheets of brighter surface, and a caustic web in the shallows.
	float sheet = n2(wpos.xz * 0.022 + vec2(TIME * 0.010, TIME * 0.006));
	col = mix(col, sheen, smoothstep(0.55, 0.95, sheet) * 0.22);
	float caus = n2(wpos.xz * 0.55 + vec2(TIME * 0.07, -TIME * 0.05))
	           * n2(wpos.xz * 0.42 - vec2(TIME * 0.05, TIME * 0.06));
	col += caus * caustic * 0.10 * (1.0 - smoothstep(0.0, 0.5, t));

	// A one-block lattice of lightness, so the surface reads as part of the
	// same blocky world rather than a smooth sheet laid over it.
	float cell = h21(floor(wpos.xz));
	col *= 1.0 + (cell - 0.5) * cell_tone;

	// Averaging two colour stops always loses chroma toward the middle; this
	// puts the midrange back without touching either end.
	float lum = dot(col, vec3(0.2126, 0.7152, 0.0722));
	col = mix(vec3(lum), col, saturation);

	ALBEDO = col;
	ALPHA = min(opacity, 1.0 - exp(-through * density)) ;
	ROUGHNESS = 0.12;
	SPECULAR = 0.5;
	METALLIC = 0.0;

	// Gentle ripple normals — visible but restrained.
	float e = 0.6;
	float h0 = n2(wpos.xz * 0.22 + vec2(TIME * 0.05, TIME * 0.035));
	float hx = n2((wpos.xz + vec2(e, 0.0)) * 0.22 + vec2(TIME * 0.05, TIME * 0.035));
	float hz = n2((wpos.xz + vec2(0.0, e)) * 0.22 + vec2(TIME * 0.05, TIME * 0.035));
	NORMAL = normalize(mat3(VIEW_MATRIX) * normalize(vec3((h0 - hx) * 0.35, 1.0, (h0 - hz) * 0.35)));
}
