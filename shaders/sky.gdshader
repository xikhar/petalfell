shader_type sky;

// Bright periwinkle overhead falling to a pale lilac-white at the skyline,
// with the sun a soft warm bloom rather than a disc. Distance dissolves into
// this, so the horizon band and the fog colour have to agree or the seam shows
// as a ring around the world.

uniform vec3 zenith : source_color = vec3(0.773, 0.796, 0.953);
uniform vec3 horizon : source_color = vec3(0.933, 0.918, 0.980);
uniform vec3 ground : source_color = vec3(0.882, 0.851, 0.937);
uniform vec3 sun_tint : source_color = vec3(1.0, 0.941, 0.949);
uniform float sun_glow : hint_range(0.0, 4.0) = 1.1;
uniform vec3 sun_dir = vec3(-0.55, 0.78, -0.32);

void sky() {
	vec3 dir = EYEDIR;
	float up = dir.y;

	vec3 col = mix(horizon, zenith, smoothstep(0.0, 0.62, up));
	col = mix(ground, col, smoothstep(-0.18, 0.02, up));

	// Forward scatter around the sun. Wide and low-amplitude: it should read as
	// air, never as a light source in frame.
	float s = max(dot(dir, normalize(sun_dir)), 0.0);
	col += sun_tint * sun_glow * (pow(s, 8.0) * 0.10 + pow(s, 2.0) * 0.045);

	COLOR = col;
}
