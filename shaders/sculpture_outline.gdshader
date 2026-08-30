shader_type spatial;

// Arbitrary imported sculpture has no voxel edge-run metadata, so the world's
// screen-space ink shader cannot consume it. A restrained inverted hull gives
// the two authored Meshy silhouettes the same plum outline without retexturing
// or rebuilding their detailed triangle geometry.
render_mode unshaded, cull_front, depth_draw_never, shadows_disabled,
	fog_disabled, specular_disabled;

uniform vec3 ink_colour : source_color = vec3(0.47, 0.42, 0.52);
uniform float outline_width : hint_range(0.0, 0.08) = 0.009;
global uniform float pf_night;

void vertex() {
	VERTEX += NORMAL * outline_width;
}

void fragment() {
	ALBEDO = ink_colour * mix(1.0, 0.58, pf_night);
	ROUGHNESS = 1.0;
}
