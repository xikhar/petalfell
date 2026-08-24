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
uniform vec3 moon_dir = vec3(0.55, -0.78, 0.32);
/// 0 at noon, 1 in the small hours. Drives the stars and the moon.
uniform float night : hint_range(0.0, 1.0) = 0.0;
uniform vec3 moon_tint : source_color = vec3(0.88, 0.90, 1.0);
// Clouds are intentionally not drawn. Their coverage still attenuates the two
// celestial highlights so the non-visual weather and the light rig agree.
uniform float cloud_cover : hint_range(0.0, 1.0) = 0.0;

float h21(vec2 p) {
	p = fract(p * vec2(443.897, 441.423));
	p += dot(p, p + 19.19);
	return fract(p.x * p.y);
}

void sky() {
	vec3 dir = EYEDIR;
	float up = dir.y;

	vec3 col = mix(horizon, zenith, smoothstep(0.0, 0.62, up));
	col = mix(ground, col, smoothstep(-0.18, 0.02, up));

	// Stars.
	//
	// Sampled on a coarse cell rather than by thresholding smooth noise: a star
	// is a POINT, and noise thresholded low enough to give points gives them in
	// clumps. One candidate per cell, kept or dropped by a second hash, lets the
	// field be sparse and even at the same time. Faded out near the horizon
	// where the haze would swallow them anyway.
	if (night > 0.01) {
		vec2 cell = dir.xz / max(abs(dir.y) + 0.28, 0.001) * 34.0;
		vec2 id = floor(cell);
		vec2 f = fract(cell) - 0.5;
		float pick = h21(id);
		if (pick > 0.955) {
			vec2 jitter = vec2(h21(id + 11.7), h21(id + 23.3)) - 0.5;
			float d = length(f - jitter * 0.6);
			float star = smoothstep(0.16, 0.0, d) * (0.35 + 0.65 * h21(id + 5.1));
			// A little colour: not every star is white, and a field of identical
			// points reads as dirt on the lens.
			vec3 tint = mix(vec3(0.82, 0.88, 1.0), vec3(1.0, 0.94, 0.86), h21(id + 3.3));
			col += tint * star * night * smoothstep(0.02, 0.30, up) * 0.9;
		}
	}

	// The moon: a real disc with a soft halo, unlike the sun, which is
	// deliberately only a bloom. A visible moon gives the night sky something to
	// be about and tells the player which way the light is coming from.
	float m = max(dot(dir, normalize(moon_dir)), 0.0);
	float disc = smoothstep(0.9975, 0.9990, m);
	float moon_transmission = mix(1.0, 0.28, cloud_cover);
	col += moon_tint * night * moon_transmission
	     * (disc * 1.6 + pow(m, 220.0) * 0.5 + pow(m, 12.0) * 0.05);

	// Forward scatter around the sun. Wide and low-amplitude: it should read as
	// air, never as a light source in frame.
	float s = max(dot(dir, normalize(sun_dir)), 0.0);
	float sun_transmission = mix(1.0, 0.60, cloud_cover);
	col += sun_tint * sun_glow * sun_transmission
	     * (pow(s, 8.0) * 0.10 + pow(s, 2.0) * 0.045);

	COLOR = col;
}
