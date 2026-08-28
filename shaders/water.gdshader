shader_type spatial;

// Water.
//
// Blue-violet rather than the blue-green of real water: the whole basin is lilac
// stone and rose blossom, and a cyan river reads as a foreign object cutting
// through it. This hue lets the water belong to the same world while being by
// some distance the deepest, most saturated thing on the ground plane.
//
// ONE RULE governs everything below the colour stops, and every bad revision of
// this shader broke it: nothing periodic, and nothing broad, may pass through a
// high power. A sum of sines beats, and a high exponent turns that beat into a
// visible lattice; a broad noise field tilting the normal comes back as patches
// of lighter water sliding across the lake. Structure at block scale or finer,
// or no structure at all.
//
// A COROLLARY, learned the same way: every additive term here is a fixed amount
// of light added to a surface that now sits around a tenth in linear terms. The
// sun sheen at two tenths did not sheen the lake, it tripled it. When the body
// colour is deepened, every one of these has to come down with it.
//
// UNSHADED, and lit entirely by hand. As an ordinary lit surface the water
// received the directional shadow map like any other ground, so every cliff and
// canopy laid a grey shadow across the lake — which is not something water does.
// A surface is dark where it reflects something dark, not where something stands
// between it and the sun. Everything is therefore computed here: the bed seen
// through the water, the reflection off it, the fresnel between them, one broad
// sun sheen, and the glints. The reflection comes from a second render of the
// world — see PlanarReflection.cs.
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
uniform vec3 moon_colour : source_color = vec3(0.76, 0.82, 1.0);
uniform vec3 sun_dir = vec3(-0.55, 0.78, -0.32);
uniform vec3 moon_dir = vec3(0.55, -0.78, 0.32);
uniform float sun_visibility : hint_range(0.0, 1.0) = 1.0;
uniform float moon_visibility : hint_range(0.0, 1.0) = 0.0;
uniform float cloud_cover : hint_range(0.0, 1.0) = 0.0;
global uniform float pf_night;
// World height of the surface plane. Needed to reconstruct the fragment's
// position analytically — see the note in fragment().
uniform float plane_y = 24.35;
// Production atlas windows can contain ocean, rivers and high lakes in one
// mesh. Their small quads interpolate accurately, so those windows reconstruct
// the surface from the real mesh position instead of one global plane.
uniform bool surface_from_mesh = false;
varying vec3 mesh_world;

void vertex() {
	mesh_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Per-channel absorption per world unit travelled. The ratio matters far more
// than the magnitude: keep blue well below red or depth stops being a hue shift.
// Red and green are pulled down hard here — that separation is the whole reason
// a metre of water reads as a richer BLUE rather than as a darker lilac.
uniform vec3 extinction = vec3(0.52, 0.36, 0.115);
// Ceiling on how much of the bed the water may ever hide. Even a deep channel
// keeps a little of what lies under it — the bed is half the appeal of looking
// at this water at all.
uniform float max_occlusion : hint_range(0.0, 1.0) = 0.93;
// How fast the body colour walks from shoal to deep, per world unit of column.
// The lake is only a handful of blocks deep, so a gentle ramp never reaches the
// deep stop at all and the whole basin comes out the pale shoal tone.
uniform float depth_rate = 0.42;
uniform float reflect_strength : hint_range(0.0, 1.0) = 0.85;
// Physically the reflectance at this camera pitch is a couple of percent, which
// is invisible. The reference lake plainly stands its banks up in itself, so the
// curve is lifted and flattened: still weakest looking straight down, strongest
// at a grazing angle, but present everywhere.
uniform float fresnel_floor : hint_range(0.0, 1.0) = 0.22;
uniform float saturation = 1.30;
uniform float caustic = 0.22;
uniform float cell_tone = 0.032;
// Screen-space displacement of the bed lookup, in UV. The flow field peaks
// around a quarter, so this is roughly the fraction of the screen the deepest
// water may bend what lies under it.
uniform float refraction = 0.075;
// How far the bed is smeared along the flow at full depth, and how far the
// reflection is smeared. The reflection gets more: a mirror image off a moving
// surface is never sharp, while the bed is still something you want to read.
uniform float haze = 0.0022;
uniform float reflect_haze = 0.010;
uniform float sparkle = 0.55;
// Small, and it has to stay small. The lobe is wide, so across the near half of
// the frame it is effectively a CONSTANT addition — and deep water sits around a
// tenth in linear light, so a sheen of two tenths does not sheen it, it triples
// it. At 0.20 the whole lake came back a pale lilac wash with the depth gradient
// buried underneath.
uniform float sun_sheen = 0.05;
uniform float moon_sheen = 0.038;
uniform float shore_foam : hint_range(0.0, 1.0) = 0.09;

// The mirrored render of the world. See PlanarReflection.cs — a point on the
// water plane lands at the same screen position in both views, so this is read
// at the fragment's own SCREEN_UV, horizontally flipped to undo the winding fix.
uniform sampler2D reflect_tex : hint_default_black, filter_linear;
uniform float reflect_mix : hint_range(0.0, 1.0) = 0.88;
uniform float reflect_wobble = 0.10;
// How far the mirrored frame is squeezed toward its top edge. 1.0 is the honest
// mirror, whose reflections are correct and barely visible; lower drags each
// bank's fringe of real reflection further out across the water.
uniform float reflect_reach : hint_range(0.2, 1.0) = 0.45;

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

// The reflection viewport hands back a finished, TONEMAPPED AND ENCODED frame —
// the same bytes that would have gone to the screen. Everything downstream of
// here works in linear light, so feeding those bytes straight in raises the
// whole image by a gamma and the lake comes back a flat near-white sheet.
vec3 decode(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec2 warp(vec2 p, float amount) {
	return p + vec2(n2(p * 0.41), n2(p * 0.41 + 53.7)) * (amount * 2.0) - amount;
}

// One travelling ripple: height in x, surface gradient in yz.
//
// A sine rather than another octave of noise, for two reasons. Its derivative is
// exact, so the normal costs nothing beyond the cosine already being computed —
// and it travels in a DIRECTION. Noise scrolled through itself boils in place;
// a stack of directional waves crossing at different angles and speeds is what
// makes a surface read as flowing rather than as a field being animated.
//
// Built-ins are not visible inside user functions, so TIME arrives as a parameter.
vec3 ripple(vec2 p, vec2 dir, float freq, float amp, float speed, float t) {
	float phase = dot(p, dir) * freq + t * speed;
	return vec3(sin(phase) * amp, dir * (cos(phase) * amp * freq));
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
	vec3 wpos = surface_from_mesh
		? mesh_world
		: cam + dir * ((plane_y - cam.y) / denom);
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
	vec3 calm_world = normalize(vec3(swell.x * 0.055, 1.0, swell.y * 0.055));
	vec3 calm_normal = normalize(mat3(VIEW_MATRIX) * calm_world);

	// Five crossing ripples, four world units down to two thirds of one.
	//
	// The frequencies are the whole design here, and getting them wrong is what
	// every bad revision of this shader had in common. A wave eleven units long is
	// not a ripple, it is a SWELL: broad structure, and every term downstream of
	// the normal raises it to a power, so broad structure comes back as broad
	// bands of lighter water sliding across the lake. The same failure as the
	// original drifting facets, arrived at from a different direction.
	//
	// Held to a block or two and under, the identical maths reads as fine glitter
	// and short travelling crest lines instead — which is what water does.
	vec2 p = wpos.xz;
	vec3 wave = ripple(p, vec2( 0.940,  0.341), 1.55, 0.030, 1.00, TIME)
	          + ripple(p, vec2(-0.416,  0.909), 2.41, 0.020, 1.25, TIME)
	          + ripple(p, vec2( 0.707, -0.707), 3.77, 0.013, 1.55, TIME)
	          + ripple(p, vec2(-0.860, -0.510), 5.83, 0.008, 1.90, TIME)
	          + ripple(p, vec2( 0.190,  0.982), 9.11, 0.005, 2.30, TIME);
	// For a height field the normal runs against the gradient.
	vec3 ripple_world = normalize(vec3(swell.x * 0.055 - wave.y, 1.0, swell.y * 0.055 - wave.z));
	vec3 ripple_normal = normalize(mat3(VIEW_MATRIX) * ripple_world);

	// THE WAVE FILTER.
	//
	// One screen-space displacement field, shared by everything the water shows
	// through or reflects off itself, so the bed and the reflection warp together
	// as one surface rather than each doing its own thing. Two scales: the ripple
	// gradient for the fast, fine crinkle, and the slow swell underneath it for a
	// lazy large-scale sway — a single scale reads as a filter effect, two read as
	// water.
	//
	// The Y component is negated because screen V runs down while world Z runs
	// away from the camera.
	vec2 flow = vec2(wave.y, -wave.z) + swell * 0.55;

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
	float column = max(wpos.y - bed_y, 0.0);

	// The colour lookup is displaced, and only where there is water in front of
	// the bed — offsetting at the shoreline drags the bank's colour out over the
	// water and paints a halo along every edge. The ramp starts a little way in
	// rather than at zero, which keeps the first few centimetres of shallows
	// undistorted and the waterline crisp.
	float wet = clamp((through - 0.35) * 0.7, 0.0, 1.0);
	vec2 offset = flow * refraction * wet;
	vec2 uv = clamp(SCREEN_UV + offset, vec2(0.0), vec2(1.0));

	// A displaced sample landing on something in FRONT of the water is not bed at
	// all — it is the bank, or the traveller standing on it. Faded rather than
	// switched: a hard test flips whole pixels between displaced and undisplaced
	// along every depth discontinuity, and the terrace edges come out chewed.
	float valid = smoothstep(0.0, 0.7, depth_at(uv, INV_PROJECTION_MATRIX) - surface);
	uv = mix(SCREEN_UV, uv, valid);

	// HAZE. Deep water is not a clean window: the further the light has travelled
	// through it the more it has been scattered sideways, so the bed goes soft
	// while the shallows stay sharp. Smeared ALONG the displacement direction
	// rather than in a symmetric disc, which is what makes it read as looking
	// through moving water instead of as a blurred photograph.
	vec2 smear = normalize(flow + vec2(0.0001)) * haze * wet * smoothstep(0.0, 3.5, column);
	vec3 bed = texture(screen_tex, uv).rgb * 0.34
	         + texture(screen_tex, clamp(uv + smear, vec2(0.0), vec2(1.0))).rgb * 0.22
	         + texture(screen_tex, clamp(uv - smear, vec2(0.0), vec2(1.0))).rgb * 0.22
	         + texture(screen_tex, clamp(uv + smear * 2.1, vec2(0.0), vec2(1.0))).rgb * 0.11
	         + texture(screen_tex, clamp(uv - smear * 2.1, vec2(0.0), vec2(1.0))).rgb * 0.11;

	// The VERTICAL column, not the length of the view ray through the water.
	// Path length is the physically honest measure, but it varies smoothly with
	// viewing angle across the frame, so a flat stretch of lakebed came out as a
	// soft gradient and every terrace edge as a long diagonal ramp. Measured per
	// column, a flat bed takes one flat tone and each step takes its own — the
	// crisp mosaic of tiles the reference actually shows.
	float depth_units = column;

	vec3 absorb = max(exp(-extinction * depth_units), vec3(1.0 - max_occlusion));
	float t = 1.0 - exp(-depth_units * depth_rate);
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

	// The mirrored render of the world, standing the banks up in the lake.
	//
	// A screen-space march lived here once. At this camera pitch the reflected ray
	// leaves the frame for almost every fragment, so it missed nearly everywhere
	// and the few hits formed hard-edged regions of a different tone — a march
	// that is mostly miss is all artifact and no reflection. It is now a real
	// second view of the world; see PlanarReflection.cs.
	//
	// Mirroring fixes the water plane, so this fragment's reflection sits at this
	// fragment's own screen position. The horizontal flip undoes the X negation
	// that keeps the mirrored camera's winding correct.
	// STRETCH. The mirrored pass is geometrically right and, left alone, almost
	// invisible: reflections reach about one and a half times an object's height
	// out from the shore and no further, so open water shows bare sky. The
	// reference art has them lying right across the lake.
	//
	// The frame is therefore squeezed toward its top edge. A downward-looking rig
	// puts the far shore near the top, so pulling that way spreads the band of
	// real reflection — the fringe hugging each bank — down over the water in
	// front of it. Everything sampled is genuine mirrored geometry standing in
	// roughly the right place; it is only reaching further than the angles allow.
	//
	// The direction matters and I had it inverted at first: squeezing toward the
	// BOTTOM samples ever more of the sky that was already the problem.
	vec2 ruv = vec2(1.0 - SCREEN_UV.x, SCREEN_UV.y * reflect_reach);

	// The same wave filter the bed gets, mirrored in X to match the flipped
	// lookup. This is where the reflection stops being a picture of the bank and
	// starts being water: it breaks up and swims. It also covers the stretch,
	// which a still mirror would show up immediately.
	ruv += vec2(-flow.x, flow.y) * reflect_wobble;
	// And smeared along the flow, harder than the bed is. A reflection off a
	// moving surface is the one thing that is never sharp.
	vec2 rsmear = normalize(vec2(-flow.x, flow.y) + vec2(0.0001)) * reflect_haze;
	vec3 mirrored = decode(
		  texture(reflect_tex, clamp(ruv, vec2(0.001), vec2(0.999))).rgb * 0.4
		+ texture(reflect_tex, clamp(ruv + rsmear, vec2(0.001), vec2(0.999))).rgb * 0.2
		+ texture(reflect_tex, clamp(ruv - rsmear, vec2(0.001), vec2(0.999))).rgb * 0.2
		+ texture(reflect_tex, clamp(ruv + rsmear * 2.2, vec2(0.001), vec2(0.999))).rgb * 0.1
		+ texture(reflect_tex, clamp(ruv - rsmear * 2.2, vec2(0.001), vec2(0.999))).rgb * 0.1);

	vec3 incident = normalize(mat3(VIEW_MATRIX) * dir);
	vec3 ray = reflect(incident, calm_normal);
	vec3 ray_world = normalize((INV_VIEW_MATRIX * vec4(ray, 0.0)).xyz);
	// Held a little toward the sky gradient. The mirrored pass sees under the
	// world's shell where nothing is meshed, and leaning on the sky there keeps
	// those places the colour of the sky they are showing.
	vec3 sky = mix(sky_low, sky_high, clamp(ray_world.y * 1.6, 0.0, 1.0));
	vec3 reflected = mix(sky, mirrored, reflect_mix);

	// Schlick, lifted. Water is nearly clear looking straight down and nearly a
	// mirror at a grazing angle, and that transition is most of what says
	// "surface" rather than "tinted volume" — but the honest curve leaves two
	// percent reflection at this pitch, which is no reflection at all.
	//
	// The exponent is lower than Schlick's five as well, which has the useful side
	// effect of making this term far less sensitive to the normal.
	float facing = clamp(dot(-incident, calm_normal), 0.0, 1.0);
	float fresnel = fresnel_floor + (1.0 - fresnel_floor) * pow(1.0 - facing, 3.5);

	vec3 col = mix(refracted, reflected, clamp(fresnel * reflect_strength, 0.0, 1.0));

	// One broad, soft sun sheen off the calm surface, and a fine glitter off the
	// ripples. The wide lobe is the light on the lake; the tight one is the light
	// on the individual wavelets, and only the second is allowed to move quickly.
	vec3 sun_view = normalize(mat3(VIEW_MATRIX) * normalize(sun_dir));
	vec3 moon_view = normalize(mat3(VIEW_MATRIX) * normalize(moon_dir));
	vec3 sun_half = normalize(sun_view - incident);
	vec3 moon_half = normalize(moon_view - incident);
	float sun_spec = pow(clamp(dot(calm_normal, sun_half), 0.0, 1.0), 14.0);
	float moon_spec = pow(clamp(dot(calm_normal, moon_half), 0.0, 1.0), 18.0);
	float sun_transmission = mix(1.0, 0.60, cloud_cover);
	float moon_transmission = mix(1.0, 0.28, cloud_cover);
	col += sun_colour * sun_spec * sun_sheen * sun_visibility * sun_transmission;
	col += moon_colour * moon_spec * moon_sheen * moon_visibility * moon_transmission;

	// NO tight specular lobe off the ripple normal.
	//
	// A pow-90 highlight on the wave normal is the single most tempting thing to
	// put here and the reason three revisions of this shader came back covered in
	// a regular lattice of pale dashes. The wave stack is a SUM OF SINES: it beats,
	// and a high exponent turns that beat into visible periodic structure no matter
	// how fine the wavelengths are. The rule this shader keeps relearning is that
	// nothing periodic may pass through a high power. Sparkle comes from noise
	// below; the ripples earn their keep in the reflection's wobble.

	// No lightening of the crests themselves.
	//
	// Tried, and it is wrong twice over. A sum of sines has a beat pattern, so
	// thresholding its height paints a REGULAR LATTICE of pale dashes over the
	// lake — the eye reads the periodicity instantly and the water turns into
	// wallpaper. And the reference lake has no visible crests at all: it is
	// glassy, and what moves on it is sparkle, not ridges. The wave stack earns
	// its keep through the normal — glints and the wobble in the reflection —
	// and never by tinting the surface directly.

	// Where the water runs out. A rim of brighter, busier surface against the
	// bank — the column is measured per bed block, so the rim follows the terraces
	// the way the shallows in the reference do, rather than as a smooth airbrushed
	// band ignoring the blocks underneath.
	float shore = 1.0 - smoothstep(0.0, 1.35, column);
	float lap = 0.5 + 0.5 * sin(dot(wpos.xz, vec2(0.61, 0.79)) * 1.7 - TIME * 0.9 + wave.x * 14.0);
	col = mix(col, sheen, shore * shore * shore_foam * (0.35 + 0.65 * lap));

	// Drifting sheets of brighter surface.
	//
	// The domain warp is sixteen world units wide, so thresholding this field
	// draws long smooth filaments rather than patches. Against the old pale water
	// a six percent lift was invisible; against water this dark the same six
	// percent painted pale worms crawling over the lake. Every additive term in
	// this shader was tuned when the surface was three times lighter, and every
	// one of them had to come down.
	float sheet = fbm(warp(wpos.xz * 0.075, 1.2) + vec2(TIME * 0.010, TIME * 0.006));
	col = mix(col, sheen, smoothstep(0.66, 0.98, sheet) * 0.018);

	// A one-block lattice of lightness, so the surface belongs to the same blocky
	// world rather than being a smooth sheet laid over it.
	col *= 1.0 + (h21(floor(wpos.xz)) - 0.5) * cell_tone;

	// Glints, warped before thresholding. A threshold applied straight to lattice
	// noise puts one in almost every cell, and an even speckle across the whole
	// surface is the most obviously artificial thing water can do. Pushed past
	// 1.0 so the environment's glow catches them.
	float glint = fbm(warp(wpos.xz * 3.4, 2.2) + vec2(TIME * 0.09, -TIME * 0.07));
	// Not weighted by the wave height: that is periodic, and periodic structure
	// scaling a threshold puts the sparkle on a grid.
	// Glints survive the night and gain a little: moonlight on water is mostly
	// specular, so it is the one thing that should NOT go dim after dusk.
	col += smoothstep(0.82, 0.98, glint) * sparkle * (1.0 + pf_night * 0.5)
	     * mix(1.0, 0.42, cloud_cover)
	     * vec3(1.05, 0.98, 1.02);

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
