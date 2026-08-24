shader_type spatial;

// A firefly is an emissive point, not a tiny lamp mesh. A literal sub-pixel
// sphere disappears during glow downsampling, so this material draws a sharp
// point inside a wider, very low-energy radial source. The point stays tiny;
// the surrounding pixels merely give the bloom chain something to sample.
render_mode unshaded, blend_add, depth_draw_never, cull_disabled,
	shadows_disabled, fog_disabled, specular_disabled;

uniform float emission_strength : hint_range(0.0, 8.0) = 2.2;

void fragment() {
	float distance_from_core = length(UV * 2.0 - 1.0);
	float core = 1.0 - smoothstep(0.10, 0.25, distance_from_core);
	float halo = pow(max(1.0 - distance_from_core, 0.0), 2.4);
	float coverage = max(core, halo * 0.30);
	vec3 glow = COLOR.rgb;

	ALBEDO = glow * 0.12;
	EMISSION = glow * emission_strength * (core + halo * 0.42);
	ALPHA = COLOR.a * coverage;
}
