shader_type spatial;

// Petals and lily pads lying on the water.
//
// Separate from the ground detail shader for one reason: this one WRITES DEPTH.
// The water composites the bed by measuring how far the view ray travelled
// through it, read from the opaque depth buffer. A petal that does not write
// depth is invisible to that measurement, so the water attenuates it as though
// it were lying on the lakebed several blocks down and every petal comes out
// drowned in violet. Writing depth puts the petal at the surface, where the
// travelled distance is zero and it keeps its own colour.
render_mode cull_disabled, diffuse_burley, specular_disabled;

uniform float sway = 0.035;
uniform float sway_speed = 0.6;

void vertex() {
	// Pads and petals ride the same slow swell as the surface they sit on,
	// rather than being pinned to a plane that moves under them.
	vec3 origin = MODEL_MATRIX[3].xyz;
	float phase = VERTEX.x * 0.6 + VERTEX.z * 0.8 + origin.x + origin.z;
	VERTEX.y += sin(TIME * sway_speed + phase) * sway;
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 0.85;
	SPECULAR = 0.1;
}
