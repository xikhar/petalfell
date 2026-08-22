shader_type spatial;

// Water.
//
// Lavender rather than the blue-green of real water: the whole basin is lilac
// stone and rose blossom, and a cyan river reads as a foreign object cutting
// through it. Violet lets the water belong to the same world while still being
// the coolest, most saturated thing on the ground plane.
//
// UNSHADED, and lit entirely by hand. As an ordinary lit surface the water
// received the directional shadow map like any other ground, so every cliff and
// canopy laid a grey shadow across the lake — which is not something water does.
// A surface is dark where it reflects something dark, not where something stands
// between it and the sun. Everything is therefore computed here: the bed seen
// through the water, the reflection off it, the fresnel between them, one broad
// sun sheen, and the glints.
//
// The bed is composited rather than alpha-blended. Mixing toward an opaque
// colour flattens a lake — the voxels underneath lose their own tone and the
// whole surface becomes one violet sheet. Instead the bed is sampled and
// attenuated by a per-channel absorption over the distance the view ray actually
// travelled through water, so the mosaic of blocks below stays legible and depth
// reads as a HUE SHIFT rather than a fade to grey. Blue survives longest, which
// is the entire reason deep water looks like a different colour instead of a
// darker one.
//
// Depth comes from the opaque depth buffer rather than a baked map, so the
// shoreline is always exactly where the terrain actually is.
render_mode blend_mix, depth_draw_never, cull_back, unshaded;

uniform vec3 shoal : source_color = vec3(0.812, 0.761, 0.902);
uniform vec3 shallow : source_color = vec3(0.608, 0.541, 0.831);
uniform vec3 deep : source_color = vec3(0.357, 0.310, 0.620);
uniform vec3 warm : source_color = vec3(0.890, 0.769, 0.788);
uniform vec3 sheen : source_color = vec3(0.910, 0.886, 0.961);
uniform vec3 sky_low : source_color = vec3(0.86, 0.84, 0.96);
uniform vec3 sky_high : source_color = vec3(0.72, 0.70, 0.91);
uniform vec3 sun_colour : source_color = vec3(1.0, 0.96, 0.94);
uniform vec3 sun_dir = vec3(-0.55, 0.78, -0.32);
// World height of the surface plane. Needed to reconstruct the fragment's
// position analytically — see the note in fragment().
uniform float plane_y = 24.35;

// Per-channel absorption per world unit travelled. The ratio matters far more
// than the magnitude: keep blue well below red or depth stops being a hue shift.
uniform vec3 extinction = vec3(0.30, 0.21, 0.095);
// Ceiling on how much of the bed the water may ever hide. Even a deep channel
// keeps a little of what lies under it — the bed is half the appeal of looking
// at this water at all.
uniform float max_occlusion : hint_range(0.0, 1.0) = 0.90;
uniform float reflect_strength : hint_range(0.0, 1.0) = 0.55;
uniform float saturation = 1.18;
uniform float caustic = 0.42;
uniform float cell_tone = 0.032;
uniform float refraction = 0.010;
uniform float sparkle = 0.55;
uniform float sun_sheen = 0.20;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
// No mipmaps. The lookup UV carries a noise-driven refraction offset, so its
// screen-space derivatives jump from pixel to pixel; a mipmapped sampler then
// picks wildly different levels across the surface and the bed arrives blurred
// in broad, hard-edged patches — which read as translucent facets over the lake.
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

float h21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1, 0)), f.x),
	           mix(h21(i + vec2(0, 1)), h21(i + vec2(1, 1)), f.x), f.y);
}

// Rotated, warped fractal noise. Plain value noise keeps its integer lattice
// visible as square structure, which on a still surface reads as a printed
// dither rather than as light on water.
float fbm(vec2 p) {
	float sum = 0.0, amp = 0.5, norm = 0.0;
	mat2 rot = mat2(vec2(0.86, 0.51), vec2(-0.51, 0.86));
	for (int i = 0; i < 4; i++) {
		sum += n2(p) * amp;
		norm += amp;
		p = rot * p * 2.07 + 17.3;
		amp *= 0.5;
	}
	return sum / norm;
}

vec2 warp(vec2 p, float amount) {
	return p + vec2(n2(p * 0.41), n2(p * 0.41 + 53.7)) * (amount * 2.0) - amount;
}

// Godot built-ins are not visible inside user functions, so the matrices are
// handed in rather than referenced.
float depth_at(vec2 uv, mat4 inv_projection) {
	float raw = texture(depth_tex, uv).r;
	vec3 ndc = vec3(uv * 2.0 - 1.0, raw);
	vec4 view = inv_projection * vec4(ndc, 1.0);
	return -(view.z / view.w);
}

// World position of whatever the depth buffer holds at this pixel.
vec3 world_at(vec2 uv, mat4 inv_projection, mat4 inv_view) {
	float raw = texture(depth_tex, uv).r;
	vec3 ndc = vec3(uv * 2.0 - 1.0, raw);
	vec4 view = inv_projection * vec4(ndc, 1.0);
	return (inv_view * vec4(view.xyz / view.w, 1.0)).xyz;
}

void fragment() {
	// Reconstruct where this fragment sits on the water, analytically.
	//
	// VERTEX is the rasteriser's interpolation of view-space position, and the
	// water is one plane a thousand units across drawn as TWO triangles. Across
	// spans that large the interpolated value carries visible error, and since
	// the surface depth feeds the absorption, that error came out as broad
	// triangular facets of slightly different tone spread over the whole lake —
	// the "low poly lines". Intersecting the view ray with the known plane
	// height is exact at every pixel and independent of how the mesh happens to
	// be tessellated.
	vec4 ndc_far = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 0.5, 1.0);
	vec3 ray_view = ndc_far.xyz / ndc_far.w;
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(ray_view, 0.0)).xyz);
	vec3 cam = CAMERA_POSITION_WORLD;
	// Guard the degenerate case: a ray parallel to the surface never meets it.
	float denom = abs(dir.y) < 0.0005 ? 0.0005 * sign(dir.y + 0.0001) : dir.y;
	vec3 wpos = cam + dir * ((plane_y - cam.y) / denom);
	float surface = -(VIEW_MATRIX * vec4(wpos, 1.0)).z;

	// A slow, shallow swell. The reference water is nearly still — glassy enough
	// to hold a reflection — so this only breaks the surface up, never reads as
	// chop.
	vec2 swell = vec2(
		fbm(wpos.xz * 0.14 + vec2(TIME * 0.035, TIME * 0.022)) - 0.5,
		fbm(wpos.xz * 0.14 + vec2(TIME * 0.026 + 11.0, TIME * 0.031)) - 0.5);
	// The perturbation stays TINY.
	//
	// Fresnel raises its term to the fifth power and the sun sheen to the
	// twenty-eighth, so both are violently sensitive to the surface normal. At a
	// quarter-unit tilt — about seven degrees — the swell swung them far enough
	// to paint broad soft patches of lighter water that crawled across the lake
	// as it animated: the drifting facets. The reference water is glassy; the
	// swell only has to break the sheen up, not to visibly tilt the surface.
	vec3 world_normal = normalize(vec3(swell.x * 0.055, 1.0, swell.y * 0.055));
	vec3 view_normal = normalize(mat3(VIEW_MATRIX) * world_normal);

	/* ---- what is under the water ---------------------------------- */

	// DEPTH is measured straight down the view ray, never through the refracted
	// lookup.
	//
	// This is the whole cause of the drifting facets. Refraction offsets the
	// sample by a few pixels, and near a bed step those few pixels land on a
	// block a whole terrace higher — so the measured column jumped by two units
	// along every edge of the terraced lakebed, drawing hard straight lines at
	// exactly the isometric angles of the blocks. Because the offset is driven
	// by the animated swell, those lines then crawled across the surface.
	//
	// Refraction bends what you SEE. It must not bend what the water measures
	// itself to be: the column at this pixel is a property of the pixel.
	float bed_depth = depth_at(SCREEN_UV, INV_PROJECTION_MATRIX);
	float through = max(bed_depth - surface, 0.0);
	float bed_y = world_at(SCREEN_UV, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX).y;
	float column = max(plane_y - bed_y, 0.0);

	// The colour lookup may still be displaced, and only where there is water in
	// front of the bed — offsetting at the shoreline drags the bank's colour out
	// over the water and paints a halo along every edge.
	vec2 offset = swell * refraction * clamp(through * 0.5, 0.0, 1.0);
	vec2 uv = clamp(SCREEN_UV + offset, vec2(0.0), vec2(1.0));
	// A displaced sample landing on something in FRONT of the water is not bed at
	// all — it is the bank, or the traveller standing on it.
	if (depth_at(uv, INV_PROJECTION_MATRIX) < surface) uv = SCREEN_UV;

	vec3 bed = texture(screen_tex, uv).rgb;

	// The VERTICAL column, not the length of the view ray through the water.
	// Path length is the physically honest measure, but it varies smoothly with
	// viewing angle across the frame, so a flat stretch of lakebed came out as a
	// soft gradient and every terrace edge as a long diagonal ramp. Measured per
	// column, a flat bed takes one flat tone and each step takes its own — the
	// crisp mosaic of tiles the reference actually shows.
	float depth_units = column;

	vec3 absorb = max(exp(-extinction * depth_units), vec3(1.0 - max_occlusion));
	float t = 1.0 - exp(-depth_units * 0.085);
	vec3 body = mix(shoal, shallow, smoothstep(0.0, 0.45, t));
	body = mix(body, deep, smoothstep(0.35, 1.0, t));
	// Sunlit ground throwing warmth back up through the first half-metre.
	body = mix(warm, body, smoothstep(0.0, 0.22, t));

	vec3 refracted = bed * absorb + body * (1.0 - absorb);

	// A caustic web, only where the light still reaches the bed.
	float caus = fbm(warp(wpos.xz * 0.48, 1.1) + vec2(TIME * 0.07, -TIME * 0.05))
	           * fbm(warp(wpos.xz * 0.38, 1.4) - vec2(TIME * 0.05, TIME * 0.06));
	refracted += caus * caustic * 0.12 * (1.0 - smoothstep(0.0, 0.5, t));

	/* ---- what is on the water ------------------------------------- */

	// Sky reflection off the swell.
	//
	// A screen-space march lived here and has been removed. At this camera pitch
	// the reflected ray points up and off-screen for almost every fragment, so it
	// missed nearly everywhere and contributed almost nothing — while the handful
	// of fragments that DID hit formed hard-edged regions of a different tone,
	// which read as broad translucent facets laid over the lake. A march that is
	// mostly miss is all artifact and no reflection.
	//
	// Reflecting the sky gradient off the perturbed normal keeps what that was
	// actually buying: the surface brightens toward the horizon and ripples where
	// the swell tilts it, with no discontinuities anywhere.
	vec3 incident = normalize(mat3(VIEW_MATRIX) * dir);
	vec3 ray = reflect(incident, view_normal);
	vec3 ray_world = normalize((INV_VIEW_MATRIX * vec4(ray, 0.0)).xyz);
	vec3 reflected = mix(sky_low, sky_high, clamp(ray_world.y * 1.6, 0.0, 1.0));

	// Schlick. Water is nearly clear looking straight down and nearly a mirror
	// at a grazing angle, and that transition is most of what says "surface"
	// rather than "tinted volume".
	float facing = clamp(dot(-incident, view_normal), 0.0, 1.0);
	float fresnel = 0.02 + 0.98 * pow(1.0 - facing, 5.0);

	vec3 col = mix(refracted, reflected, clamp(fresnel * reflect_strength, 0.0, 1.0));

	// One broad, soft sun sheen. Deliberately wide: a tight highlight on water
	// this still is a photographic hotspot, and this palette never does those.
	vec3 sun_view = normalize(mat3(VIEW_MATRIX) * normalize(sun_dir));
	vec3 half_vec = normalize(sun_view - incident);
	// A wide lobe: broad sheen, not a hotspot that slides around with the swell.
	float spec = pow(clamp(dot(view_normal, half_vec), 0.0, 1.0), 14.0);
	col += sun_colour * spec * sun_sheen;

	// Drifting sheets of brighter surface.
	float sheet = fbm(warp(wpos.xz * 0.075, 1.2) + vec2(TIME * 0.010, TIME * 0.006));
	col = mix(col, sheen, smoothstep(0.66, 0.98, sheet) * 0.06);

	// A one-block lattice of lightness, so the surface belongs to the same blocky
	// world rather than being a smooth sheet laid over it.
	col *= 1.0 + (h21(floor(wpos.xz)) - 0.5) * cell_tone;

	// Glints, warped before thresholding. A threshold applied straight to lattice
	// noise puts one in almost every cell, and an even speckle across the whole
	// surface is the most obviously artificial thing water can do. Pushed past
	// 1.0 so the environment's glow catches them.
	float glint = fbm(warp(wpos.xz * 3.4, 2.2) + vec2(TIME * 0.09, -TIME * 0.07));
	col += smoothstep(0.82, 0.98, glint) * sparkle * vec3(1.05, 0.98, 1.02);

	// Averaging two colour stops always loses chroma toward the middle; this puts
	// the midrange back without touching either end.
	float lum = dot(col, vec3(0.2126, 0.7152, 0.0722));
	col = mix(vec3(lum), col, saturation);

	ALBEDO = col;
	// The bed is already composited in, so the surface itself is opaque. Alpha
	// blending on top would apply the attenuation twice and wash the shallows out
	// to nothing.
	ALPHA = 1.0;
}
