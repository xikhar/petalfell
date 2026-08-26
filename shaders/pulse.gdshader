shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_front,
	shadows_disabled, fog_disabled, specular_disabled;

// The click marker. A soft white shell, brightest at its rim, gone in half a
// second — confirmation, not a waypoint flag.
uniform float fade : hint_range(0.0, 1.0) = 1.0;
uniform vec3 tint : source_color = vec3(1.0, 0.99, 1.0);

void fragment() {
	float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.6);
	ALBEDO = tint;
	ALPHA = fade * fade * (0.10 + rim * 0.42);
}
