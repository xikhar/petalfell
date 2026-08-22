shader_type spatial;

// Characters and props built from boxes rather than from voxels. Same face
// tinting and the same flat, uninterrupted surfaces as the terrain, so a
// figure standing on a shelf belongs to it.
render_mode cull_back, diffuse_burley, specular_disabled;

uniform vec4 albedo : source_color = vec4(0.9, 0.9, 0.9, 1.0);
uniform vec3 warm_tint : source_color = vec3(1.03, 1.00, 0.975);
uniform vec3 cool_tint : source_color = vec3(0.965, 0.972, 1.04);
uniform float face_tint : hint_range(0.0, 1.0) = 0.55;
uniform vec3 sun_dir = vec3(-0.55, 0.78, -0.32);
uniform float emission_amount = 0.0;

void fragment() {
	vec3 wn = normalize((INV_VIEW_MATRIX * vec4(normalize(NORMAL), 0.0)).xyz);
	float facing = dot(wn, normalize(sun_dir));
	vec3 tint = mix(cool_tint, warm_tint, smoothstep(-0.6, 0.9, facing));

	ALBEDO = albedo.rgb * mix(vec3(1.0), tint, face_tint);
	ROUGHNESS = 0.92;
	SPECULAR = 0.0;
	METALLIC = 0.0;
	EMISSION = ALBEDO * emission_amount;
}
