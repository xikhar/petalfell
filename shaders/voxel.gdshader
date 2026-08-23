shader_type spatial;

// Terrain and structures.
//
// Form comes from the silhouette, a constant face-direction ramp, baked vertex
// occlusion and the haze. On top of that sits one thin layer of surface
// pattern: irregular clods on earth, weathered blotches on rock, a fine tooth
// on turf, lengthwise grain on timber. Flat colour alone leaves a cliff reading
// as a painted card, but the variation has to stay ORGANIC — an earlier
// revision drew masonry coursing on the soil band and the terraces read as
// built brick walls instead of cut ground.
//
// Patterns are evaluated on WORLD-space coordinates projected onto the face
// plane, so they tile seamlessly across block seams and never betray where one
// voxel ends and the next begins.
render_mode cull_back, diffuse_burley, specular_disabled;

uniform vec3 warm_tint : source_color = vec3(1.02, 1.00, 0.985);
uniform vec3 cool_tint : source_color = vec3(0.945, 0.958, 1.06);
uniform float face_tint : hint_range(0.0, 1.0) = 0.38;
// Driven by DayCycle. Global rather than per-material: the character shader
// alone builds a material for every box of every figure in the world, and a
// clock that had to find them all would spend the frame chasing references.
global uniform vec3 pf_sun_dir;
global uniform float pf_night;

// How far the pattern is allowed to darken its own surface. Restrained on
// purpose: this is tonal variation within one material, not a drawn line.
uniform float pattern_strength : hint_range(0.0, 1.0) = 0.55;
// Patterns fade out with distance. Kept past the fog's own reach they turn the
// far field into a shimmering mesh.
uniform float pattern_fade_start = 40.0;
uniform float pattern_fade_end = 130.0;

varying vec3 v_world;
varying vec4 v_surface;

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

// Fractal value noise with a ROTATION between octaves.
//
// Plain value noise is sampled on an integer lattice, so a single octave — or
// several stacked on the same axes — keeps that lattice visible as square,
// axis-aligned structure. At terrain scale it stops reading as weathering and
// starts reading as a printed pattern. Rotating each octave and offsetting it
// destroys the alignment; warping the domain first destroys what is left.
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

// Push the sample point around with a lower-frequency field. Cheap, and it is
// what turns a regular blotch field into an irregular one.
vec2 warp(vec2 p, float amount) {
	return p + vec2(vnoise(p * 0.41), vnoise(p * 0.41 + 53.7)) * (amount * 2.0) - amount;
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_surface = CUSTOM0;
}

void fragment() {
	vec3 base = COLOR.rgb;

	vec3 n = normalize(NORMAL);
	vec3 wn = normalize((INV_VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// A constant axis ramp, applied before Lambert. Deliberately independent of
	// the sun: the same terrace stays legible through camera and light changes,
	// with Z sides a little deeper than X.
	// Widened. At 0.94 and 0.88 the two vertical faces of a cube were within six
	// percent of its top, so a block read as a flat hexagon rather than as a
	// solid — the ramp is the main thing describing form here and it was barely
	// doing anything.
	float face_shade = wn.y > 0.5 ? 1.0
		: wn.y < -0.5 ? 0.62
		: abs(wn.x) > 0.5 ? 0.88 : 0.78;
	base *= face_shade;

	// The face plane, in world units. A top face reads its XZ; a wall reads its
	// horizontal axis against world Y, so a bank's variation runs level.
	vec3 an = abs(wn);
	bool upright = an.y < 0.5;
	vec2 uv = an.y > 0.5 ? v_world.xz
		: (an.x > 0.5 ? vec2(v_world.z, v_world.y) : vec2(v_world.x, v_world.y));

	float pattern = floor(v_surface.x + 0.001);
	float drip = fract(v_surface.x + 0.001) / 0.9;

	float mark = 0.0;      // 0..1, how much this fragment is "in shadow" of its pattern
	float lift = 0.0;      // occasional brighter clods, so a face is not uniform

	if (pattern > 0.5 && pattern < 1.5) {
		// EARTH — soil and mud. Irregular clods and a broad tonal drift, with no
		// straight lines anywhere. An earlier revision drew masonry coursing
		// here; it read as a built brick wall rather than as a cut bank, which
		// is exactly wrong for terrain the player is meant to take as natural.
		vec2 wp = warp(uv * 0.9, 0.7);
		float broad = fbm(wp * 0.55);
		float clods = fbm(wp * 2.1);
		float grit = fbm(wp * 5.7);
		mark = (broad - 0.5) * 0.45 + (clods - 0.5) * 0.66 + (grit - 0.5) * 0.26;
		lift = smoothstep(0.70, 0.98, clods) * 0.10;
	} else if (pattern > 1.5 && pattern < 2.5) {
		// ROCK — weathered stone. Larger blotches than the earth, plus a faint
		// bedding drift so a tall face is not uniform top to bottom.
		vec2 wp = warp(uv * 0.8, 0.9);
		float bed = fbm(vec2(wp.x * 0.32, wp.y * 1.0));
		float blotch = fbm(wp * 1.35);
		float fine = fbm(wp * 4.4);
		mark = (bed - 0.5) * 0.34 + (blotch - 0.5) * 0.64 + (fine - 0.5) * 0.24;
		lift = smoothstep(0.74, 0.98, blotch) * 0.12;
	} else if (pattern > 2.5 && pattern < 3.5) {
		// PLANK — the one built material left. Boards are a made thing and read
		// as one, but the seams stay soft and lengthwise, never a grid.
		float board = uv.y * 2.6;
		float seam = abs(fract(board) - 0.5) * 2.0;
		mark = (1.0 - smoothstep(0.55, 0.95, seam)) * 0.40;
		mark += (fbm(vec2(uv.x * 2.6, floor(board) * 3.1)) - 0.5) * 0.32;
	} else if (pattern > 3.5 && pattern < 4.5) {
		// BARK — vertical striations, no cross-courses.
		float grain = fbm(vec2(uv.x * 6.2, uv.y * 0.6));
		mark = (grain - 0.5) * 0.75;
	} else if (pattern > 4.5 && pattern < 5.5) {
		// TURF / SNOW / DRIFT — a fine tooth and nothing more. The reference's
		// shelf tops are clean flat colour; this only has to stop a large shelf
		// reading as a painted card.
		vec2 wp = warp(uv * 1.2, 0.5);
		float speck = fbm(wp * 4.6) * 0.55 + fbm(wp * 11.0) * 0.45;
		mark = (speck - 0.5) * 0.55;
	} else if (pattern > 5.5 && pattern < 6.5) {
		// TRODDEN GROUND — paths worn through turf. Patchy wear, not pavers.
		vec2 wp = warp(uv, 0.8);
		float wear = fbm(wp * 1.0) * 0.6 + fbm(wp * 3.3) * 0.4;
		mark = (wear - 0.5) * 0.72;
		lift = smoothstep(0.76, 0.98, wear) * 0.10;
	}

	// LEAF — one tone per BLOCK.
	//
	// Handled apart from the mark/recess path above, which desaturates toward
	// grey: a crown wants its cubes to differ in *value* while staying the same
	// blossom, not to go progressively colourless. Sampled in three dimensions
	// so a cube's three visible faces share one value and its neighbour gets
	// another; sampled in two, a whole vertical column would match and the crown
	// would band instead of reading as stacked cubes.
	//
	// The ink draws only the crown's outer contour — never the seams between
	// neighbouring leaf blocks — so without this the whole mass collapses into
	// one flat silhouette.
	if (pattern > 6.5) {
		vec3 cell = floor(v_world - wn * 0.5 + 0.001);
		float tone = hash21(cell.xz * 1.0 + cell.y * 17.0);
		base *= mix(0.88, 1.09, tone);
	}

	float dist = length(v_world - CAMERA_POSITION_WORLD);
	float fade = 1.0 - smoothstep(pattern_fade_start, pattern_fade_end, dist);
	mark *= pattern_strength * fade;
	lift *= fade;

	// Darken toward a desaturated, slightly cooler version of the surface, never
	// toward black or a fixed grey. The mark has to read as the same material in
	// shadow, not as ink laid on top of it.
	float luma = dot(base, vec3(0.2126, 0.7152, 0.0722));
	vec3 recess = mix(base, vec3(luma) * vec3(0.94, 0.96, 1.06), 0.55) * 0.80;
	base = mix(base, recess, clamp(mark, 0.0, 1.0));
	base *= 1.0 + lift;

	// The grass drip: turf spilling raggedly over the lip and hanging a little
	// way down the face below. The ragged edge is a per-column hash rather than
	// smooth noise — it has to break at block-ish intervals to look like turf
	// tearing, not like an airbrushed gradient.
	if (upright && drip > 0.01) {
		float column = an.x > 0.5 ? v_world.z : v_world.x;
		float jag = hash21(vec2(floor(column * 2.0), floor(v_world.y)));
		float jag2 = hash21(vec2(floor(column * 5.0) + 31.0, floor(v_world.y)));
		float depth = drip * (0.16 + 0.30 * jag + 0.14 * jag2);
		float local = fract(v_world.y);
		float w = fwidth(local) * 1.2 + 0.002;
		float lip = smoothstep(1.0 - depth - w, 1.0 - depth + w, local);
		base = mix(base, v_surface.yzw * face_shade, lip);
	}

	float facing = dot(wn, normalize(pf_sun_dir));
	vec3 tint = mix(cool_tint, warm_tint, smoothstep(-0.6, 0.9, facing));
	base *= mix(vec3(1.0), tint, face_tint);

	ALBEDO = base;
	ROUGHNESS = 0.92;
	SPECULAR = 0.0;
	METALLIC = 0.0;
	// The mesher parks per-block emissive in the vertex alpha; lanterns, lit
	// windows and cascades are the only things in the world allowed to glow.
	//
	// After dark they are the only warm light on the continent, and they have to
	// grow to carry that — at daylight strength a lantern is invisible against a
	// night ambient, and the one signal that says somebody lives here would go
	// out exactly when it matters most.
	// Boosted after dark, but nowhere near enough to clip: at 7x the windows
	// came back as pure white rectangles, which reads as a hole in the wall
	// rather than as a warm room behind it. The COLOUR is the point.
	EMISSION = base * COLOR.a * (2.2 + pf_night * 1.9);
}
