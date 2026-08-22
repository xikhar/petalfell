shader_type spatial;

// Sub-voxel ground detail: grass tufts, flower heads, pebbles, reeds, petals.
//
// One merged mesh per chunk. Colour is per vertex — each blade runs from a
// deeper base to a lighter tip — and CUSTOM0 carries (sway weight, clump
// phase): the weight is 0 at a root and 1 at a tip, the phase is per clump, so
// a meadow ripples instead of pulsing in lockstep.
//
// Depth is not written. Thousands of blades writing depth put a depth edge
// under every lawn, and anything that keys off depth discontinuities — the
// ink's own occlusion test included — then sees grass where it should see
// ground.
render_mode cull_disabled, depth_draw_never, diffuse_burley, specular_disabled;

uniform float wind_amp = 0.10;
uniform float wind_speed = 1.0;

void vertex() {
	float sway = CUSTOM0.x;
	float phase = CUSTOM0.y;
	float t = TIME * wind_speed;
	// Two frequencies, so a gust travels rather than the whole field breathing.
	VERTEX.x += (sin(t + phase) * 0.7 + sin(t * 1.7 + phase * 1.3) * 0.3) * sway * wind_amp;
	VERTEX.z += (cos(t * 0.83 + phase * 1.1) * 0.7) * sway * wind_amp * 0.6;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.95;
	SPECULAR = 0.0;
}
