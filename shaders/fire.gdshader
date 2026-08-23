shader_type spatial;

// A small volumetric suggestion made from two crossed vertical planes. The
// planes never billboard, so the flame remains anchored in the world when the
// camera rotates. Soft analytic coverage keeps their rectangular bounds
// invisible and depth_draw_never prevents the transparent plume from cutting a
// hole in later transparent rendering.
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, specular_disabled;

uniform vec3 outer_color : source_color = vec3(1.00, 0.20, 0.055);
uniform vec3 middle_color : source_color = vec3(1.00, 0.52, 0.10);
uniform vec3 core_color : source_color = vec3(1.00, 0.91, 0.52);
uniform float emission_strength : hint_range(0.0, 8.0) = 3.6;
uniform float distortion_pixels : hint_range(0.0, 5.0) = 1.65;
uniform float haze_opacity : hint_range(0.0, 0.5) = 0.16;

// One scene lookup supplies the shimmer. No mipmaps: this offset is only a
// pixel or two, and selecting a lower mip would blur a conspicuous patch around
// the fire instead of bending the background.
uniform sampler2D screen_tex : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D depth_tex : hint_depth_texture, repeat_disable, filter_nearest;

varying float v_plane;

void vertex() {
	// The two quads carry different seeds in COLOR.r. Their silhouettes move
	// independently, avoiding an obvious bright plus sign where they cross.
	v_plane = COLOR.r;
}

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
	           mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0)), f.x), f.y);
}

// Three octaves are enough at this physical size. The rotated basis prevents
// the value-noise lattice from appearing as stacked square cells in the flame.
float fbm(vec2 p) {
	float sum = 0.0;
	float amp = 0.55;
	float norm = 0.0;
	mat2 turn = mat2(vec2(0.82, 0.57), vec2(-0.57, 0.82));
	for (int i = 0; i < 3; i++) {
		sum += noise2(p) * amp;
		norm += amp;
		p = turn * p * 2.03 + 13.7;
		amp *= 0.48;
	}
	return sum / norm;
}

// Linear view-space distance for the opaque object sampled by the heat haze.
// A displaced lookup that lands on geometry in front of the flame is rejected,
// otherwise silhouettes get pulled sideways across their own near edge.
// Stage built-ins are passed explicitly: Godot compiles helper functions in a
// stage-neutral scope, where INV_PROJECTION_MATRIX is not directly visible.
float view_depth(vec2 uv, mat4 inverse_projection) {
	float raw = texture(depth_tex, uv).r;
	vec3 ndc = vec3(uv * 2.0 - 1.0, raw);
	vec4 view = inverse_projection * vec4(ndc, 1.0);
	return -(view.z / view.w);
}

void fragment() {
	// Mesh UV runs bottom-to-top after this inversion. x is centred on the
	// hearth, while y continues above the visible flame to hold the heat plume.
	float y = 1.0 - UV.y;
	float x = UV.x * 2.0 - 1.0;
	float seed = v_plane * 19.73;
	float time = TIME;

	vec2 domain = vec2(x * 1.55 + seed, y * 3.35 - time * 1.34);
	float broad = fbm(domain + vec2(time * 0.17, -time * 0.08));
	float fine = noise2(domain * 2.15 + vec2(-time * 0.61, time * 0.28) + 7.1);

	// The lower flame is planted. Sway grows toward the tip, with noise breaking
	// the motion so it does not read as one sinusoidal cardboard sheet.
	float sway = sin(time * 2.25 + y * 4.1 + seed) * 0.055 * y;
	sway += (broad - 0.5) * 0.19 * y;
	float dx = abs(x - sway);

	float top = 0.72 + sin(time * 4.15 + seed) * 0.035 + (broad - 0.5) * 0.055;
	float fy = clamp(y / max(top, 0.1), 0.0, 1.25);
	float width = mix(0.70, 0.025, pow(clamp(fy, 0.0, 1.0), 0.82));
	width *= 0.86 + broad * 0.25 + (fine - 0.5) * 0.09;

	float aa = max(fwidth(dx) * 1.25, 0.008);
	float flame = 1.0 - smoothstep(width - aa, width + aa * 1.8, dx);
	flame *= smoothstep(0.0, 0.055, y);
	flame *= 1.0 - smoothstep(top - 0.085, top + 0.025, y);

	// A smaller differently warped body gives the flame a hot, bright interior
	// without a hard nested cutout. It naturally disappears before the outer tip.
	float core_width = width * mix(0.52, 0.27, clamp(fy, 0.0, 1.0));
	float core = 1.0 - smoothstep(core_width - aa, core_width + aa * 2.0, dx);
	core *= flame * (1.0 - smoothstep(0.52, 0.88, fy));
	float hot = core * (1.0 - smoothstep(0.10, 0.64, fy));

	vec3 vertical_color = mix(middle_color, outer_color,
		smoothstep(0.28, 1.0, fy));
	vec3 flame_color = mix(vertical_color, core_color,
		clamp(core * 0.72 + hot * 0.48, 0.0, 1.0));
	float flame_alpha = flame * (0.46 + core * 0.30 + hot * 0.12);

	// Heat lives mostly above the bright body, with a restrained skirt around its
	// upper edge. It changes the lookup, not the tint, so background objects
	// shimmer while staying fully readable.
	float plume_width = mix(0.48, 0.32, smoothstep(0.45, 1.0, y));
	plume_width *= 0.90 + broad * 0.20;
	float plume = 1.0 - smoothstep(plume_width, plume_width + 0.18,
		abs(x - sway * 0.55));
	float above = smoothstep(0.43, 0.70, y) * (1.0 - smoothstep(0.91, 1.0, y));
	float around = flame * (1.0 - core) * smoothstep(0.30, 0.62, y) * 0.30;
	float haze = clamp(plume * above + around, 0.0, 1.0);
	float haze_alpha = haze * haze_opacity * (1.0 - flame_alpha * 0.72);

	vec2 pixels = 1.0 / vec2(textureSize(screen_tex, 0));
	vec2 heat_flow = vec2(
		noise2(vec2(x * 4.2 + seed, y * 5.8 - time * 2.1)),
		noise2(vec2(x * 3.7 - seed, y * 6.4 - time * 1.65) + 31.4)) - 0.5;
	heat_flow += vec2(sin(time * 3.0 + y * 7.0 + seed),
		cos(time * 2.35 + x * 4.0)) * 0.12;
	vec2 warped_uv = clamp(SCREEN_UV + heat_flow * pixels * distortion_pixels * haze,
		pixels, vec2(1.0) - pixels);

	float fragment_depth = max(-VERTEX.z, 0.0);
	float behind = view_depth(warped_uv, INV_PROJECTION_MATRIX) - fragment_depth;
	float valid_warp = smoothstep(-0.025, 0.12, behind);
	warped_uv = mix(SCREEN_UV, warped_uv, valid_warp);
	vec3 refracted = texture(screen_tex, warped_uv).rgb;

	// Combine the refracted layer and flame into one conventional unpremultiplied
	// source colour. This keeps blend_mix edges clean while avoiding a second draw
	// for the heat haze.
	float alpha = flame_alpha + haze_alpha * (1.0 - flame_alpha);
	float safe_alpha = max(alpha, 0.0001);
	ALBEDO = (flame_color * flame_alpha
		+ refracted * haze_alpha * (1.0 - flame_alpha)) / safe_alpha;
	EMISSION = flame_color * emission_strength * flame_alpha
		* (0.62 + core * 0.58 + hot * 0.52) / safe_alpha;
	ALPHA = alpha;
}
