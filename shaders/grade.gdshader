shader_type canvas_item;

// The grade. Display space, taste only.
//
// This runs as a fullscreen canvas pass over the 3D viewport, which means it
// sees the frame *after* tone mapping and glow. That ordering is deliberate:
// the reference applies its lift/gamma/gain, split tone and milk on display
// values, and the pale end of the palette only behaves if nothing downstream
// can turn it back into an emitter.
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

uniform vec3 lift = vec3(0.006, 0.004, 0.014);
uniform vec3 gamma_ = vec3(1.00, 1.005, 0.99);
uniform vec3 gain = vec3(1.03, 1.02, 1.03);
uniform float saturation = 1.19;
uniform float contrast = 1.10;
uniform float vignette = 0.06;
uniform float grain = 0.006;
uniform float aberration = 0.0008;
// A flat mix toward a bright lilac, weighted toward the dark end so the
// deepest values lift the most and nothing in frame approaches black.
uniform float milk = 0.010;
uniform vec3 milk_color : source_color = vec3(0.855, 0.845, 0.925);
// ACES already shoulders, but pastels sit high enough that a second, gentler
// knee is what stops blossom tops clipping to white.
uniform float knee = 0.80;

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 d = uv - 0.5;
	float r2 = dot(d, d);

	float ca = aberration * r2 * 2.4;
	vec3 col = texture(screen_tex, uv).rgb;
	if (aberration > 0.00001) {
		col.r = texture(screen_tex, uv - d * ca).r;
		col.b = texture(screen_tex, uv + d * ca).b;
	}

	col = col * gain + lift;
	col = pow(max(col, vec3(0.0)), 1.0 / gamma_);

	float l = dot(col, vec3(0.2126, 0.7152, 0.0722));
	col = mix(vec3(l), col, saturation);

	// Highlights toward warm cream, shadows toward lavender. The shadow leg
	// matters more than it looks: it is what makes cast shadows read as
	// coloured air rather than as an absence of light.
	col = mix(col, col * vec3(1.020, 1.000, 0.978), smoothstep(0.55, 1.0, l));
	col = mix(col, col * vec3(0.968, 0.976, 1.045), 1.0 - smoothstep(0.0, 0.50, l));

	float dark_bias = 1.0 - smoothstep(0.05, 0.72, l);
	col = mix(col, milk_color, milk * (0.42 + 0.58 * dark_bias));

	// The reference keeps its palette light but does not flatten it: warm lit
	// planes and cool shadow planes stay clearly separated.
	col = max((col - 0.5) * contrast + 0.5, vec3(0.0));

	vec3 over = max(col - vec3(knee), vec3(0.0));
	vec3 rolled = vec3(knee) + (1.0 - knee) * (1.0 - exp(-over / max(1.0 - knee, 0.001)));
	col = mix(col, rolled, step(vec3(knee), col));

	// Tinted rather than neutral; a grey vignette on a lilac frame reads as
	// dirt on the lens.
	float vig = vignette * smoothstep(0.10, 1.0, r2 * 1.75);
	col = mix(col, col * vec3(0.955, 0.955, 1.0), vig);
	col *= 1.0 - vig * 0.28;

	float g = fract(sin(dot(uv * vec2(1234.5, 6789.1) + TIME, vec2(12.9898, 78.233))) * 43758.5453);
	col += (g - 0.5) * grain;

	COLOR = vec4(col, 1.0);
}
