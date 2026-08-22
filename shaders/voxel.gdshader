shader_type spatial;

// Terrain and structures.
//
// Faces are flat, uninterrupted colour: form comes from the silhouette, the
// face direction, cast light and soft occlusion, never from per-voxel seams or
// surface hatching. Ambient occlusion is baked per vertex by the mesher and
// arrives in COLOR.rgb already multiplied in — it is the main reason the
// geometry reads, so nothing here fights it.
render_mode cull_back, diffuse_burley, specular_disabled;

// A narrow peach glint on lit upper contours and a soft plum on the opposite
// side, so block faces feel lightly bevelled without any line being drawn.
uniform vec3 warm_tint : source_color = vec3(1.03, 1.00, 0.975);
uniform vec3 cool_tint : source_color = vec3(0.965, 0.972, 1.04);
uniform float face_tint : hint_range(0.0, 1.0) = 0.35;
uniform vec3 sun_dir = vec3(-0.55, 0.78, -0.32);

void fragment() {
	vec3 base = COLOR.rgb;

	// Face-direction shading, in world space so a wall reads the same wherever
	// the camera stands.
	vec3 n = normalize(NORMAL);
	vec3 wn = normalize((INV_VIEW_MATRIX * vec4(n, 0.0)).xyz);
	// The source applies a constant axis ramp before Lambert lighting. It is
	// deliberately independent of the sun: the same terrace remains legible
	// through camera or light changes, with Z sides a little deeper than X.
	float face_shade = wn.y > 0.5 ? 1.0
		: wn.y < -0.5 ? 0.72
		: abs(wn.x) > 0.5 ? 0.94 : 0.88;
	base *= face_shade;
	float facing = dot(wn, normalize(sun_dir));
	vec3 tint = mix(cool_tint, warm_tint, smoothstep(-0.6, 0.9, facing));
	base *= mix(vec3(1.0), tint, face_tint);

	ALBEDO = base;
	ROUGHNESS = 0.92;
	SPECULAR = 0.0;
	METALLIC = 0.0;
	// The mesher parks per-block emissive in the vertex alpha; lanterns and
	// cascades are the only things in the world allowed to glow.
	EMISSION = base * COLOR.a * 2.2;
}
