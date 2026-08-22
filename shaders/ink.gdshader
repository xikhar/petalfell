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
uniform vec3 ink_light : source_color = vec3(0.97, 0.95, 0.97);
uniform float dark_opacity : hint_range(0.0, 1.0) = 0.55;
uniform float light_opacity : hint_range(0.0, 1.0) = 0.22;

uniform float core_width = 3.2;
uniform float water_level = 24.35;
// How much of a stroke survives below the waterline. The transition is a hard
// stop with only the softness needed for clean antialiasing.
uniform float submerged_ink : hint_range(0.0, 1.0) = 0.0;
uniform float fade_start = 150.0;
uniform float fade_end = 420.0;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

varying flat vec2 v_screen_start;
varying flat vec2 v_screen_end;
varying flat float v_depth_start;
varying flat float v_depth_end;
varying flat vec3 v_ws_start;
varying flat vec3 v_ws_end;
varying flat vec2 v_caps;
varying flat vec3 v_normal_a;
varying flat vec3 v_normal_b;
varying flat float v_pale_eligible;

void vertex() {
	vec3 ws_start = (MODEL_MATRIX * vec4(CUSTOM0.xyz, 1.0)).xyz;
	vec3 ws_end = (MODEL_MATRIX * vec4(CUSTOM1.xyz, 1.0)).xyz;

	v_normal_a = normalize(mat3(MODEL_MATRIX) * CUSTOM2.xyz);
	v_normal_b = normalize(mat3(MODEL_MATRIX) * CUSTOM3.xyz);
	v_caps = vec2(CUSTOM0.w, CUSTOM1.w);
	v_ws_start = ws_start;
	v_ws_end = ws_end;
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
	} else {
		if (s_behind) {
			float t = (near_z - ve.z) / (vs.z - ve.z);
			vs = mix(ve, vs, t);
		} else if (e_behind) {
			float t = (near_z - vs.z) / (ve.z - vs.z);
			ve = mix(vs, ve, t);
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
		float cap = at_end ? CUSTOM1.w : CUSTOM0.w;
		float extend = at_end ? half_w * cap : -half_w * cap;

		vec2 screen = base + perp * (VERTEX.y * half_w) + dir * extend;
		vec2 ndc = (screen / VIEWPORT_SIZE) * 2.0 - 1.0;
		POSITION = vec4(ndc * pick.w, pick.z, pick.w);
	}
}

void fragment() {
	vec2 pa = FRAGCOORD.xy - v_screen_start;
	vec2 ba = v_screen_end - v_screen_start;
	float bb = dot(ba, ba);
	float t = bb > 0.0001 ? dot(pa, ba) / bb : 0.0;
	float tc = clamp(t, 0.0, 1.0);

	// A run that does not own its cap stops dead at the vertex plane, so a dark
	// stroke cannot protrude half a width into the pale run it meets.
	if (t < 0.0 && v_caps.x < 0.5) discard;
	if (t > 1.0 && v_caps.y < 0.5) discard;

	float dist = length(pa - ba * tc);
	float coverage = 1.0 - smoothstep(core_width * 0.5 - 0.5, core_width * 0.5 + 0.5, dist);
	if (coverage <= 0.001) discard;

	float edge_depth = mix(v_depth_start, v_depth_end, tc);
	vec3 point = mix(v_ws_start, v_ws_end, tc);

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
	float both = smoothstep(0.0, 0.16, min(face_a, face_b));
	float pale = v_pale_eligible * both;

	vec3 col = mix(ink_dark, ink_light, pale);
	float op = mix(dark_opacity, light_opacity, pale);

	// The waterline is a hard visual stop, not a fade.
	float submerged = smoothstep(water_level - 0.06, water_level + 0.06, point.y);
	float wet = mix(submerged_ink, 1.0, submerged);

	// Far ink crawls with aliasing and adds nothing once the air has eaten the
	// contrast anyway.
	float fade = 1.0 - smoothstep(fade_start, fade_end, edge_depth);

	// Tolerance only has to cover depth-buffer precision: a stroke sits exactly
	// on the surface it outlines. Anything looser and edges hidden a block or
	// two behind — a trunk inside its own canopy, the far side of a cube — punch
	// straight through the thing in front of them.
	float raw = texture(depth_tex, SCREEN_UV).r;
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, raw);
	vec4 view = INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	float scene_depth = -(view.z / view.w);
	float tol = max(0.05, edge_depth * 0.004);
	if (scene_depth > 0.0 && edge_depth > scene_depth + tol) discard;

	ALBEDO = col;
	ALPHA = coverage * op * wet * fade;
}
