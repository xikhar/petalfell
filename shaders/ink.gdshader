shader_type spatial;

// The ink.
//
// Each run arrives as a degenerate quad: VERTEX carries only the corner
// (x = 0 at the start endpoint, 1 at the end; y = which side), and the run's
// real data rides in the four custom channels. The vertex stage expands that
// into a screen-space-width strip, so a stroke is the same thickness in
// framebuffer pixels at every camera distance and zoom — which is the whole
// requirement.
//
// The pale/dark choice is made HERE, per fragment, not baked into the mesh.
// The rule is a statement about the camera: an edge is pale only where both of
// its owning faces are turned toward the viewer and both belong to the pale
// palette. Bake it at mesh time and a shelf lip keeps a bright stroke after you
// orbit behind it, which is exactly the tell that the outline is painted on
// rather than lit.
//
// Depth is tested by hand against the opaque depth buffer rather than by the
// fixed-function test. A stroke sits exactly on the surface it outlines, so a
// hardware test either z-fights or eats the line at grazing angles.
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled,
	cull_disabled, shadows_disabled, fog_disabled, specular_disabled;

// Dark is a soft plum-grey, never black: the only genuinely dark values in this
// world are tree trunks, and ink that goes to black reads as a cartoon stroke
// laid over a pastel painting.
uniform vec3 ink_dark : source_color = vec3(0.47, 0.42, 0.52);
uniform vec3 ink_light : source_color = vec3(0.62, 0.61, 0.65);
// How far the pale ink falls once it is fully night, and in how many jumps.
//
// STEPPED rather than continuous, on purpose. A pale edge is a drawing
// convention, not a lit surface, and sliding it smoothly down with the sun makes
// it behave like one — the line quietly follows the light and the trick of it
// shows. Moving in a few discrete jumps reads instead like the scene being
// re-inked a handful of times between dusk and dark, which is what a
// hand-drawn frame would do and what this world's whole look is imitating.
uniform float ink_night_scale : hint_range(0.1, 1.0) = 0.40;
uniform float ink_night_steps : hint_range(1.0, 8.0) = 4.0;
global uniform float pf_night;
uniform float core_width = 3.2;
// Pass 0 draws only pale fragments, pass 1 only the dark silhouette fragments
// of that same eligible geometry, and pass 2 draws permanently-dark runs.
// Splitting rather than overdrawing also keeps the antialias fringe single-width.
uniform int ink_pass = 0;
uniform float water_level = 24.35;
// How much of a stroke survives below the waterline. The transition is a hard
// stop with only the softness needed for clean antialiasing.
uniform float submerged_ink : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

varying flat vec2 v_screen_start;
varying flat vec2 v_screen_end;
varying flat float v_depth_start;
varying flat float v_depth_end;
varying flat vec3 v_ws_start;
varying flat vec3 v_ws_end;
varying flat vec2 v_endpoint_pale_count;
varying flat vec3 v_normal_a;
varying flat vec3 v_normal_b;
varying flat float v_pale_eligible;

vec3 face_direction(int direction) {
	if (direction == 0) return vec3(1.0, 0.0, 0.0);
	if (direction == 1) return vec3(-1.0, 0.0, 0.0);
	if (direction == 2) return vec3(0.0, 1.0, 0.0);
	if (direction == 3) return vec3(0.0, -1.0, 0.0);
	if (direction == 4) return vec3(0.0, 0.0, 1.0);
	return vec3(0.0, 0.0, -1.0);
}

ivec2 decode_face_pair(int code) {
	int pair = code - 1;
	if (pair < 5) return ivec2(0, pair + 1);
	pair -= 5;
	if (pair < 4) return ivec2(1, pair + 2);
	pair -= 4;
	if (pair < 3) return ivec2(2, pair + 3);
	pair -= 3;
	if (pair < 2) return ivec2(3, pair + 4);
	return ivec2(4, 5);
}

float pale_incident_count(float packed_value, vec3 to_camera, mat3 model_basis) {
	int packed = int(packed_value + 0.5);
	float count = 0.0;
	for (int edge = 0; edge < 6; edge++) {
		int code = packed % 16;
		packed /= 16;
		if (code == 0) break;
		ivec2 pair = decode_face_pair(code);
		vec3 normal_a = normalize(model_basis * face_direction(pair.x));
		vec3 normal_b = normalize(model_basis * face_direction(pair.y));
		count += step(0.02, min(dot(normal_a, to_camera), dot(normal_b, to_camera)));
	}
	return count;
}

void vertex() {
	vec3 ws_start = (MODEL_MATRIX * vec4(CUSTOM0.xyz, 1.0)).xyz;
	vec3 ws_end = (MODEL_MATRIX * vec4(CUSTOM1.xyz, 1.0)).xyz;

	mat3 model_basis = mat3(MODEL_MATRIX);
	v_normal_a = normalize(model_basis * CUSTOM2.xyz);
	v_normal_b = normalize(model_basis * CUSTOM3.xyz);
	v_endpoint_pale_count = vec2(
		pale_incident_count(CUSTOM0.w, normalize(CAMERA_POSITION_WORLD - ws_start), model_basis),
		pale_incident_count(CUSTOM1.w, normalize(CAMERA_POSITION_WORLD - ws_end), model_basis));
	// Baked half of the rule: both owning faces pale, manifold, not a concave
	// fold. The camera half is decided in the fragment stage.
	v_pale_eligible = CUSTOM2.w;

	vec4 vs = VIEW_MATRIX * vec4(ws_start, 1.0);
	vec4 ve = VIEW_MATRIX * vec4(ws_end, 1.0);

	// Trim against the near plane in view space. Projecting a point behind the
	// camera wraps it to the far side of the frame and drags the whole strip
	// across the screen with it.
	float near_z = -0.08;
	bool s_behind = vs.z > near_z;
	bool e_behind = ve.z > near_z;
	if (s_behind && e_behind) {
		POSITION = vec4(2.0, 2.0, 2.0, 1.0);   // off-screen, cheaply culled
		v_screen_start = vec2(0.0);
		v_screen_end = vec2(0.0);
		v_depth_start = 1.0;
		v_depth_end = 1.0;
		v_ws_start = ws_start;
		v_ws_end = ws_end;
	} else {
		if (s_behind) {
			float t = (near_z - ve.z) / (vs.z - ve.z);
			vs = mix(ve, vs, t);
			ws_start = mix(ws_end, ws_start, t);
		} else if (e_behind) {
			float t = (near_z - vs.z) / (ve.z - vs.z);
			ve = mix(vs, ve, t);
			ws_end = mix(ws_start, ws_end, t);
		}

		vec4 cs = PROJECTION_MATRIX * vs;
		vec4 ce = PROJECTION_MATRIX * ve;

		// Screen position derived from clip exactly the way the rasteriser
		// derives FRAGCOORD, so the fragment stage can compare the two directly
		// without caring which way the platform's Y points.
		vec2 ss = (cs.xy / cs.w * 0.5 + 0.5) * VIEWPORT_SIZE;
		vec2 se = (ce.xy / ce.w * 0.5 + 0.5) * VIEWPORT_SIZE;

		v_screen_start = ss;
		v_screen_end = se;
		v_depth_start = -vs.z;
		v_depth_end = -ve.z;
		v_ws_start = ws_start;
		v_ws_end = ws_end;

		vec2 dir = se - ss;
		float len = length(dir);
		dir = len > 0.0001 ? dir / len : vec2(1.0, 0.0);
		vec2 perp = vec2(-dir.y, dir.x);

		// The geometry runs a pixel wider than the visible stroke so the fragment
		// stage has room to resolve analytic coverage, and half a stroke longer
		// at any end that owns its round cap.
		float half_w = core_width * 0.5 + 1.0;
		bool at_end = VERTEX.x > 0.5;
		vec4 pick = at_end ? ce : cs;
		vec2 base = at_end ? se : ss;
		float extend = at_end ? half_w : -half_w;

		vec2 screen = base + perp * (VERTEX.y * half_w) + dir * extend;
		vec2 ndc = (screen / VIEWPORT_SIZE) * 2.0 - 1.0;
		POSITION = vec4(ndc * pick.w, pick.z, pick.w);
	}
}

float scene_depth_at(vec2 uv, mat4 inverse_projection) {
	uv = clamp(uv, vec2(0.0), vec2(1.0));
	float raw = texture(depth_tex, uv).r;
	vec3 ndc = vec3(uv * 2.0 - 1.0, raw);
	vec4 view = inverse_projection * vec4(ndc, 1.0);
	return -(view.z / view.w);
}

void fragment() {
	vec2 pa = FRAGCOORD.xy - v_screen_start;
	vec2 ba = v_screen_end - v_screen_start;
	float bb = dot(ba, ba);
	float t = bb > 0.0001 ? dot(pa, ba) / bb : 0.0;
	float tc = clamp(t, 0.0, 1.0);

	float dist = length(pa - ba * tc);
	float coverage = 1.0 - smoothstep(core_width * 0.5 - 0.5, core_width * 0.5 + 0.5, dist);
	if (coverage <= 0.001) discard;

	// `tc` is a screen-space parameter. View depth and world position are not
	// linear in it under perspective, so ordinary mix() bends the inferred edge
	// behind its owning face on long receding runs. Interpolate them with 1/w,
	// as the rasteriser does for regular vertex attributes.
	float inv_start_depth = 1.0 / max(v_depth_start, 0.0001);
	float inv_end_depth = 1.0 / max(v_depth_end, 0.0001);
	float inv_depth = mix(inv_start_depth, inv_end_depth, tc);
	float edge_depth = 1.0 / inv_depth;
	vec3 point = mix(v_ws_start * inv_start_depth,
	                 v_ws_end * inv_end_depth, tc) / inv_depth;

	// The camera half of the light/dark rule, evaluated at this point along the
	// run rather than at its midpoint, so a long stroke can change class as it
	// crosses a silhouette.
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - point);
	float face_a = dot(v_normal_a, to_cam);
	float face_b = dot(v_normal_b, to_cam);

	// Neither owning face is turned toward the viewer: this edge is on the far
	// side of the solid and has no business being drawn at all.
	if (face_a <= 0.0 && face_b <= 0.0) discard;

	// Pale only where BOTH faces front the camera. Where one has turned away the
	// edge is a silhouette, and a silhouette is always dark — that contrast is
	// what separates a shape from whatever is behind it.
	float both = step(0.02, min(face_a, face_b));
	float pale = v_pale_eligible * both;
	if (ink_pass == 0 && pale < 0.5) discard;
	if (ink_pass == 1 && pale > 0.5) discard;


	// All strokes keep their full round reach. Dark wins normally, including at
	// one-pale/one-dark and one-pale/many-dark vertices. Only a vertex with two
	// or more camera-visible pale incident edges masks dark ink in the shared
	// cap region, revealing the already-connected pale lines underneath.
	if (pale < 0.5) {
		float joint_radius = core_width * 0.5 + 0.5;
		if (v_endpoint_pale_count.x >= 2.0 &&
			length(FRAGCOORD.xy - v_screen_start) <= joint_radius) discard;
		if (v_endpoint_pale_count.y >= 2.0 &&
			length(FRAGCOORD.xy - v_screen_end) <= joint_radius) discard;
	}

	// Quantise the night factor before it touches the ink. Rounding to the
	// nearest level rather than flooring puts each jump in the middle of its
	// band, so the first step happens partway into dusk instead of the instant
	// the sun clears the horizon — and the same on the way back up at dawn.
	float night = clamp(pf_night, 0.0, 1.0);
	float stepped = floor(night * ink_night_steps + 0.5) / ink_night_steps;
	vec3 pale_ink = ink_light * mix(1.0, ink_night_scale, stepped);

	vec3 col = mix(ink_dark, pale_ink, pale);

	// The waterline is a hard visual stop, not a fade.
	float submerged = smoothstep(water_level - 0.06, water_level + 0.06, point.y);
	float wet = mix(submerged_ink, 1.0, submerged);

	// Test visibility at the mathematical centreline, not at each pixel of the
	// expanded strip. Sampling a sloping owner face at the strip's outside edge
	// clips half the stroke at normal viewing angles. The two small side samples
	// let either owning face preserve the line, while a genuinely nearer solid
	// still wins across the full stroke.
	float segment_length = sqrt(max(bb, 0.0001));
	vec2 line_direction = ba / segment_length;
	vec2 line_normal = vec2(-line_direction.y, line_direction.x);
	vec2 nearest = v_screen_start + ba * tc;
	float scene_depth = scene_depth_at(nearest / VIEWPORT_SIZE, INV_PROJECTION_MATRIX);
	scene_depth = max(scene_depth, scene_depth_at(
		(nearest + line_normal * 0.75) / VIEWPORT_SIZE, INV_PROJECTION_MATRIX));
	scene_depth = max(scene_depth, scene_depth_at(
		(nearest - line_normal * 0.75) / VIEWPORT_SIZE, INV_PROJECTION_MATRIX));

	// This covers reconstruction precision only. A distance-scaled world-space
	// bias eventually approaches a whole voxel and exposes perpendicular edges
	// that are actually behind the visible face.
	float tol = 0.04;
	if (scene_depth > 0.0 && edge_depth > scene_depth + tol) discard;

	ALBEDO = col;
	// The stroke body is genuinely opaque. Alpha exists only for the one-pixel
	// analytic fringe (and the waterline cut), so intersecting same-colour lines
	// are idempotent instead of becoming darker at every vertex.
	ALPHA = coverage * wet;
}
