using System;
using System.Collections.Generic;
using Godot;

namespace Petalfell.Core;

/// <summary>
/// ART BIBLE — the single source of truth for the look of Petalfell.
///
/// Every module reads colour from here. Do not hardcode a hex anywhere else.
/// Ported from the reference project's core/palette.js; the values are the
/// accumulated result of the whole art-direction pass and are not to be
/// "improved" casually.
///
/// Direction: storybook pastel with ink. Shelf tops are a pale sage green,
/// cliff sides run terracotta over lilac stone, canopies are pink / blush /
/// cream / lavender cubes, water is dusty violet, the sky bright periwinkle.
/// The only dark values in the world are tree trunks and the ink itself, and
/// both are a soft plum — never black.
/// </summary>
public static class Palette
{
	/// <summary>
	/// Programmatic shader values and procedural vertex attributes are uploaded
	/// as raw components in this renderer path. Decode authored sRGB once at the
	/// palette boundary, just as Three.Color does in the reference renderer.
	/// </summary>
	private static Color C(uint hex) => Srgb(hex).SrgbToLinear();

	private static Color Srgb(uint hex) => new Color(
		((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f);

	/* ---------------- atmosphere ---------------- */
	public static readonly Color SkyZenith = C(0xb9b4e8);
	public static readonly Color SkyHorizon = C(0xdcd6f4);
	public static readonly Color SkyGround = C(0xd2cbef);
	public static readonly Color SkyHaze = C(0xcdc6ef);
	public static readonly Color SunTint = C(0xfff0f2);

	public static readonly Color SunColor = C(0xfff0ee);
	public const float SunIntensity = 2.2f;
	/// <summary>Sun low-ish and behind-left so cliffs throw long readable shadows.</summary>
	public static readonly Vector3 SunDir = new Vector3(-0.55f, 0.78f, -0.32f).Normalized();
	public static readonly Color LightSky = C(0xd8d5f0);
	public static readonly Color LightGround = C(0xebd8dc);
	public static readonly Color FillColor = C(0xd8cdf2);

	/* ---------------- water ---------------- */
	public static readonly Color WaterShoal = C(0xb9bcf2);
	public static readonly Color WaterShallow = C(0x7076e2);
	public static readonly Color WaterDeep = C(0x3a3ea6);
	public static readonly Color WaterWarm = C(0xe3c4c9);
	public static readonly Color WaterSheen = C(0xe8e2f5);
	public static readonly Color WaterEdge = C(0x5c5378);
	public const float WaterLevel = 24.35f;

	/* ---------------- ink ---------------- */
	/// <summary>
	/// Intrinsic sRGB luminance at which a face belongs to the pale palette.
	///
	/// Keep this identical to the Three.js edge graph. The threshold deliberately
	/// includes the high-key terrain family; camera-facing and concavity tests
	/// still decide whether an eligible edge is actually rendered pale.
	/// </summary>
	public const float LightFaceLuma = 0.61f;
	/// <summary>
	/// The two inks. Dark is a soft plum-grey, deliberately well short of
	/// black: the only genuinely dark values in this world are tree trunks, and
	/// an outline that reaches black reads as a cartoon stroke laid over a
	/// pastel painting rather than as part of it. Light is a restrained warm
	/// white: a pale edge is a hint that a surface turned, not an emitted
	/// highlight. Their restrained authored colours keep that softness while the
	/// stroke cores remain opaque and overlap-safe.
	/// </summary>
	// Shader `source_color` uniforms perform the sRGB-to-linear conversion.
	// Supplying an already-linear value here would convert it twice and make this
	// intended mid-grey ink render almost black.
	public static readonly Color InkDark = new Color(0.30f, 0.28f, 0.33f);
	// Dimmed. The pale ink was authored against a much flatter render; once the
	// grade gained real contrast it stopped reading as "this surface turned" and
	// started reading as a drawn white highlight sitting on top of the picture.
	public static readonly Color InkLight = new Color(0.62f, 0.61f, 0.65f);
	/// <summary>
	/// Stroke width in framebuffer pixels. Kept here so the renderer and the
	/// developer control always start from the same authored value.
	/// </summary>
	public const float InkWidth = 1.30f;

	/* ---------------- grade ----------------
	 *
	 * These values are pushed into grade.gdshader from Main and OVERRIDE the
	 * shader's own defaults, which is worth knowing because for a long time they
	 * were quietly making the picture worse than the shader would have on its
	 * own: the shader defaults to 1.19 saturation and 1.10 contrast, and this
	 * table was sending 1.02 and 0.94. A contrast below one does not merely fail
	 * to add punch, it actively takes it out — half the flatness in the frame was
	 * being applied deliberately, in the last pass, after everything else had got
	 * it right.
	 *
	 * Pastel is a HUE choice, not a contrast choice. Soft colours with real
	 * separation between light and shade read as storybook; soft colours with no
	 * separation read as fog. */
	public const float GradeExposure = 1.12f;
	public static readonly Vector3 GradeLift = new(0.002f, 0.000f, 0.010f);
	public static readonly Vector3 GradeGamma = new(1.00f, 1.005f, 0.99f);
	public static readonly Vector3 GradeGain = new(1.015f, 1.02f, 1.05f);
	public const float GradeSaturation = 1.26f;
	public const float GradeContrast = 1.16f;
	public const float GradeVignette = 0.06f;

	/* ---------------- time of day ----------------
	 *
	 * One keyframe per hour that matters, interpolated around the clock. Every
	 * value the atmosphere owns lives here rather than in Atmosphere.cs, for the
	 * same reason every block colour does: this file is the art bible, and a
	 * lighting rig that carries its own private colours is a second bible that
	 * will disagree with this one.
	 *
	 * Night is NOT simply day scaled down. A dark scene lit by a dimmed version
	 * of a daylight rig reads as underexposed footage; a night that reads as
	 * night has its own hues — deep blue ambient, a cold key from the wrong side
	 * of the sky, and warm points of light doing all the work. That last part is
	 * what makes this worth having in a world about abandonment: after dusk the
	 * only warm light on the continent is coming from the handful of windows
	 * somebody still lives behind.
	 *
	 * Night is also kept LIT. The first pass took it down to a moon of 0.10 and
	 * an ambient of 0.16, which is roughly physically honest and produced a black
	 * screen with a few electric-blue facets — the world stopped existing between
	 * the lanterns. This is a game about looking at a landscape, so the moon here
	 * is a stage moon: bright enough to read every terrace and keep the palette's
	 * hues alive, dim and cold enough that a lit window still reads as the
	 * warmest thing for miles.
	 *
	 * ONE RULE for the twilight keys: the SKY may be as hot as it likes, the KEY
	 * LIGHT may not. A saturated orange key does not tint a scene, it REPLACES
	 * every albedo in it with its own hue — and with the grade running at 1.26
	 * saturation the first dusk came back a uniform neon crimson with no material
	 * distinguishable from any other. Sunset colour belongs in the sky, the fog
	 * and the bloom; what actually lands on surfaces stays close to white. */
	public readonly struct SkyState
	{
		public readonly float At;
		public readonly Color Zenith, Horizon, Ground;
		public readonly Color Sun; public readonly float SunEnergy;
		public readonly Color Ambient; public readonly float AmbientEnergy;
		public readonly Color Fog;
		public readonly float ShadowOpacity;
		public readonly float Night;
		public readonly float GlowThreshold;
		/// <summary>
		/// How much of the ambient is taken from the SKY rather than from the
		/// authored ambient colour.
		///
		/// Time-varying, and it has to be. A blue midday sky makes an excellent
		/// cool fill and should supply nearly all of it. A sunset sky is a hot
		/// orange, and letting THAT supply the fill means the key and the fill are
		/// both warm and there is nothing cool left anywhere in the frame — the
		/// first dusk came back a uniform crimson for exactly this reason, and
		/// neutralising the key alone did not fix it, because the sky was still
		/// pouring orange into the shadows.
		/// </summary>
		public readonly float SkyMix;

		public SkyState(float at, uint zenith, uint horizon, uint ground,
			uint sun, float sunEnergy, uint ambient, float ambientEnergy,
			uint fog, float shadow, float night, float glow, float skyMix)
		{
			At = at;
			Zenith = C(zenith); Horizon = C(horizon); Ground = C(ground);
			Sun = C(sun); SunEnergy = sunEnergy;
			Ambient = Srgb(ambient); AmbientEnergy = ambientEnergy;
			Fog = C(fog);
			ShadowOpacity = shadow; Night = night; GlowThreshold = glow; SkyMix = skyMix;
		}
	}

	/// <summary>
	/// The day, keyed. Times are fractions of a full cycle: 0 midnight, 0.25
	/// sunrise, 0.5 noon, 0.75 sunset.
	/// </summary>
	public static readonly SkyState[] Day =
	{
		//              t     zenith    horizon   ground    sun       energy ambient   energy fog       shad night glow
		new SkyState(0.00f, 0x232c5e, 0x3b4478, 0x272a4c, 0xc3d0ff, 0.34f, 0xaeb6e2, 0.40f, 0x3a3f6a, 0.46f, 1.00f, 0.40f, 0.62f),
		new SkyState(0.21f, 0x33356a, 0x64507f, 0x36335a, 0xc7aae4, 0.38f, 0xb0a8dc, 0.42f, 0x54497a, 0.48f, 0.90f, 0.44f, 0.52f),
		// The sun on the horizon: the long, low, orange half hour.
		new SkyState(0.27f, 0x6f74c0, 0xe8a290, 0x8b7196, 0xffd2b4, 0.60f, 0xbcb4e0, 0.38f, 0xd6b4b0, 0.66f, 0.42f, 0.70f, 0.26f),
		new SkyState(0.33f, 0x9aa0e0, 0xf0cdc4, 0xc0aec4, 0xffe6d6, 0.92f, 0xd6d4f2, 0.40f, 0xe6cfda, 0.72f, 0.12f, 0.92f, 0.56f),
		new SkyState(0.50f, 0xb9b4e8, 0xdcd6f4, 0xd2cbef, 0xfff0ee, 0.98f, 0xebeeff, 0.42f, 0xcdc6ef, 0.74f, 0.00f, 1.05f, 0.78f),
		new SkyState(0.68f, 0xb0aae6, 0xe4d2e2, 0xcdc2ea, 0xffeee2, 0.94f, 0xe4e2f8, 0.42f, 0xd6c8ea, 0.74f, 0.02f, 1.00f, 0.68f),
		// Dusk. Warmer and deeper than dawn, because the day has to end
		// differently from how it began or the cycle reads as a loop.
		new SkyState(0.76f, 0x6a63b4, 0xe8ab96, 0x8f6f92, 0xffc8a4, 0.56f, 0xb4aeda, 0.38f, 0xd8b2ae, 0.64f, 0.46f, 0.66f, 0.24f),
		new SkyState(0.83f, 0x35326a, 0x6b4a7c, 0x38335c, 0xbb9edd, 0.38f, 0xaea6da, 0.42f, 0x584878, 0.48f, 0.92f, 0.44f, 0.52f),
	};

	/* ---------------- petals ---------------- */
	public static readonly Color[] PetalColors =
	{
		C(0xf7b9cc), C(0xf3a8c0), C(0xe6c0ef), C(0xd7b3ee), C(0xfbd3dc), C(0xf8e2c8),
	};
	public static readonly Color[] FallenLeafColors =
	{
		C(0xaab581), C(0x929f70), C(0xc4b989), C(0xb59b82),
	};

	// Particle material colors stay in authored sRGB: Godot's material and
	// particle color properties perform their own source-color conversion.
	public static readonly Color[] AirLeafColors =
	{
		Srgb(0xb5bf8d), Srgb(0x99a879), Srgb(0xc9bc91), Srgb(0xb89c87),
	};
	public static readonly Color[] AirPetalColors =
	{
		Srgb(0xf1b8ca), Srgb(0xe6c2eb), Srgb(0xf5d4dc), Srgb(0xd7b8e7),
	};
	public static readonly Color[] AirAlpineColors =
	{
		Srgb(0xaeb5c5), Srgb(0xb7b2c6), Srgb(0x9fabb6), Srgb(0xb3a9bb),
	};
	public static readonly Color[] AirReedColors =
	{
		Srgb(0xc3bd92), Srgb(0xaeb287), Srgb(0xd1c7a5),
	};
	/// <summary>
	/// Motes. Deliberately above 1.0 so they clear the glow threshold and bloom
	/// into soft points of light instead of sitting flat against the haze.
	/// </summary>
	public static readonly Color[] MoteColors =
	{
		new(1.35f, 1.28f, 1.20f), new(1.30f, 1.14f, 1.26f),
		new(1.18f, 1.16f, 1.38f), new(1.38f, 1.22f, 1.14f),
	};
	/// <summary>
	/// Fireflies are deliberately HDR: their geometry stays pin-small while the
	/// environment glow supplies the soft halo around it.
	/// </summary>
	public static readonly Color[] FireflyColors =
	{
		new(2.10f, 1.92f, 0.62f), new(1.72f, 2.02f, 0.66f),
		new(2.24f, 1.62f, 0.48f),
	};

	public static readonly Color[] FootPuffColors =
	{
		Srgb(0x918e94), Srgb(0xa39da5), Srgb(0x7f8389), Srgb(0x98919b),
	};

	/* ================= block library ================= */

	public const byte AIR = 0;
	public const byte GRASS = 1;
	public const byte GRASS_LIGHT = 2;
	public const byte GRASS_DEEP = 3;
	public const byte SOIL = 4;
	public const byte SAND = 5;
	public const byte GRASS_STONE = 6;
	public const byte GRASS_LIGHT_STONE = 7;
	public const byte GRASS_DEEP_STONE = 8;
	public const byte SNOW = 14;
	public const byte MUD = 15;
	public const byte MOSS = 16;
	/// <summary>Fallen blossom lying thick on the ground under a sakura stand.</summary>
	public const byte BLOSSOM_DRIFT = 17;
	public const byte SCREE = 18;
	public const byte STONE = 10;
	public const byte STONE_PALE = 11;
	public const byte STONE_WARM = 12;
	public const byte PATH = 13;
	/// <summary>Flagstone paving for a village square. Laid, not trodden.</summary>
	public const byte PAVING = 19;
	public const byte PLANK = 20;
	public const byte PLANK_PALE = 21;
	public const byte BEAM = 22;
	public const byte TRUNK = 23;
	public const byte TRUNK_PALE = 24;
	public const byte TRUNK_ROSE = 25;
	// Building materials. Cottages are plaster panels in a timber frame under a
	// steep tiled roof, which is the one place in this world a MADE seam is
	// allowed to show — everything natural is forbidden coursing and grids.
	public const byte PLASTER = 26;
	public const byte ROOF_SLATE = 27;
	public const byte ROOF_TILE = 28;
	public const byte LEAF_PINK = 30;
	public const byte LEAF_BLUSH = 31;
	public const byte LEAF_LILAC = 32;
	public const byte LEAF_CREAM = 33;
	public const byte LEAF_MINT = 34;
	public const byte LEAF_ROSE = 35;
	public const byte LANTERN = 40;
	public const byte CRYSTAL = 41;
	/// <summary>A lit window. The reason a village reads as inhabited at dusk.</summary>
	public const byte WINDOW = 42;
	public const byte CASCADE = 50;

	/// <summary>
	/// Surface patterns, consumed by the voxel fragment shader.
	///
	/// Flat colour alone leaves a cliff reading as a painted card, so every
	/// material carries a thin layer of variation. It stays ORGANIC: clods,
	/// blotches and tooth, never coursing or a grid. Terrain the player is meant
	/// to read as cut ground must not look like a built brick wall, and timber is
	/// the only material here allowed to show a made seam.
	/// </summary>
	public const float PatternNone = 0f;
	public const float PatternEarth = 1f;
	public const float PatternRock = 2f;
	public const float PatternPlank = 3f;
	public const float PatternBark = 4f;
	public const float PatternGrass = 5f;
	public const float PatternTrodden = 6f;
	/// <summary>
	/// Per-block tonal jitter, no lines.
	///
	/// A blossom crown is an assembly of visibly distinct cuboids, and the ink
	/// draws only its outer contour — never the seams between neighbouring
	/// leaf blocks. Without a per-block tone shift the whole crown collapses
	/// into one flat silhouette and stops reading as stacked cubes at all.
	/// </summary>
	public const float PatternLeaf = 7f;

	public struct BlockDef
	{
		public Color Top, Side, Bottom;
		/// <summary>
		/// The colour turf takes when it spills over the lip of the block below.
		///
		/// Its own field rather than a reuse of Side: in the reference the drip is
		/// visibly greener and more saturated than the shelf it grows from, and
		/// that contrast is what makes the lip read as living turf tearing over an
		/// edge instead of a second flat band of the same paint.
		/// </summary>
		public Color Fringe;
		public float Pattern;
		public bool Solid;
		/// <summary>Grass promotes its convex perimeter to the pale ink regardless of luminance.</summary>
		public bool LightEdge;
		public float Emissive;
		public bool TopLight, SideLight, BottomLight;
	}

	private static readonly BlockDef[] Table = new BlockDef[256];

	public static BlockDef Get(byte id) => Table[id];
	public static bool IsSolid(byte id) => Table[id].Solid;
	public static bool IsGrassSurface(byte id) => id is GRASS or GRASS_LIGHT or GRASS_DEEP
		or GRASS_STONE or GRASS_LIGHT_STONE or GRASS_DEEP_STONE;
	public static bool HasStoneSubstrate(byte id) => id is GRASS_STONE
		or GRASS_LIGHT_STONE or GRASS_DEEP_STONE;

	private static void Def(byte id, uint top, uint side, uint bottom,
		bool lightEdge = false, float emissive = 0f, float pattern = PatternNone,
		uint fringe = 0u, bool forceLightFaces = false)
	{
		var d = new BlockDef
		{
			Top = C(top), Side = C(side), Bottom = C(bottom), Pattern = pattern,
			Fringe = C(fringe == 0u ? side : fringe),
			Solid = true, LightEdge = lightEdge, Emissive = emissive,
		};
		// The pale/dark split is judged on the authored sRGB value, not on the
		// linear one. Luminance in linear space is a different number entirely,
		// and the 0.61 threshold was tuned against the hex.
		d.TopLight = forceLightFaces || Luma(Srgb(top)) >= LightFaceLuma;
		d.SideLight = forceLightFaces || Luma(Srgb(side)) >= LightFaceLuma;
		d.BottomLight = forceLightFaces || Luma(Srgb(bottom)) >= LightFaceLuma;
		Table[id] = d;
	}

	private static float Luma(Color c) => c.R * 0.2126f + c.G * 0.7152f + c.B * 0.0722f;

	static Palette()
	{
		Table[AIR] = new BlockDef { Solid = false };

		// Shelf tops are soft sage green, and the whole first ledge stays green.
		// Warm soil or cool stone begins on the block *below*, which keeps the
		// material boundary aligned to the voxel topology instead of leaving a
		// brown strip immediately under the grass.
		Def(GRASS, 0xc6cd8b, 0xbcc98f, 0xa8b57f, lightEdge: true, pattern: PatternGrass, fringe: 0xa2bb5f);
		Def(GRASS_LIGHT, 0xd2d79a, 0xc9d49f, 0xb5c18e, lightEdge: true, pattern: PatternGrass, fringe: 0xb0c76c);
		Def(GRASS_DEEP, 0xb2bd78, 0xaaba7e, 0x97a86f, lightEdge: true, pattern: PatternGrass, fringe: 0x8fac52);
		Def(GRASS_STONE, 0xc6cd8b, 0xbcc98f, 0xa8b57f, lightEdge: true, pattern: PatternGrass, fringe: 0xa2bb5f);
		Def(GRASS_LIGHT_STONE, 0xd2d79a, 0xc9d49f, 0xb5c18e, lightEdge: true, pattern: PatternGrass, fringe: 0xb0c76c);
		Def(GRASS_DEEP_STONE, 0xb2bd78, 0xaaba7e, 0x97a86f, lightEdge: true, pattern: PatternGrass, fringe: 0x8fac52);

		Def(SOIL, 0xc2836f, 0xc4826d, 0xa96d5c, pattern: PatternEarth,
			forceLightFaces: true);
		Def(SAND, 0xefe3cb, 0xe5d6bc, 0xd2c1a6, pattern: PatternGrass);

		// Biome surfaces. Each province has to be recognisable from its ground
		// alone — the plan's whole point in having provinces is that you can
		// tell where you are by looking, before any tree or building appears.
		Def(SNOW, 0xf4f2fa, 0xe9e6f3, 0xd6d2e6, lightEdge: true, pattern: PatternGrass);
		Def(MOSS, 0xa6b87c, 0x9fb384, 0x8ba070, lightEdge: true, pattern: PatternGrass, fringe: 0x86a355);
		Def(MUD, 0xa48a75, 0x99806c, 0x847060, pattern: PatternEarth);
		Def(BLOSSOM_DRIFT, 0xf0dce2, 0xe9d2da, 0xd6bec7, lightEdge: true, pattern: PatternGrass);
		Def(SCREE, 0xcfc8d9, 0xc2bbcf, 0xaea6bd, pattern: PatternRock);

		// The cool end of the run. Stone is where the world is allowed to go
		// bluish, which is what keeps the mauve soil above it reading as warm.
		Def(STONE, 0xc9c3db, 0xbcb5cf, 0xa79fbc, pattern: PatternRock);
		Def(STONE_PALE, 0xddd9eb, 0xd0cae1, 0xbbb4d0, pattern: PatternRock);
		Def(STONE_WARM, 0xd5c5d2, 0xc8b7c6, 0xb19fad, pattern: PatternRock);
		Def(PATH, 0xe4dce7, 0xd5cddd, 0xc2bacd, pattern: PatternTrodden);

		Def(PAVING, 0xdcd6e8, 0xcfc8de, 0xbbb3cc, pattern: PatternRock);

		// Cottage fabric. Cream plaster against a rose-brown frame, under roofs
		// that are the darkest built value in the world — a village has to hold
		// its silhouette against pale blossom and paler sky, and only the roofs
		// have the weight to do it.
		Def(PLASTER, 0xf7efe2, 0xefe5d5, 0xdbd1c2, pattern: PatternPlank,
			forceLightFaces: true);
		// Muted, not brick. These are the darkest built values in the world and
		// they still have to belong to a pastel one: the first pass was a
		// saturated terracotta that read as a different painting laid on top.
		Def(ROOF_SLATE, 0x817ba0, 0x746e94, 0x635d82, pattern: PatternPlank);
		Def(ROOF_TILE, 0xa9807c, 0x9c7570, 0x876460, pattern: PatternPlank);

		Def(PLANK, 0xe7bbb0, 0xdfafa4, 0xcc9a90, pattern: PatternPlank);
		Def(PLANK_PALE, 0xf0cfc6, 0xe8c2ba, 0xd5aca4, pattern: PatternPlank);
		Def(BEAM, 0xcc958e, 0xc08a84, 0xac7871, pattern: PatternBark);
		// Trunks are the darkest value in the world by a wide margin, and that
		// is deliberate: they carry the only real contrast, which is what lets
		// pale blossom read as solid objects against a pale sky.
		Def(TRUNK, 0x8a6270, 0x785462, 0x664654, pattern: PatternBark);
		Def(TRUNK_PALE, 0x9a7080, 0x886073, 0x745164, pattern: PatternBark);
		Def(TRUNK_ROSE, 0x9c7783, 0x8d6875, 0x7b5866, pattern: PatternBark);

		Def(LEAF_PINK, 0xf8ccda, 0xf3c1d0, 0xe5b0bf, pattern: PatternLeaf);
		Def(LEAF_BLUSH, 0xfcdee4, 0xf8d2da, 0xebbfc8, pattern: PatternLeaf);
		Def(LEAF_LILAC, 0xdccef1, 0xd2c2eb, 0xc0b0da, pattern: PatternLeaf);
		Def(LEAF_CREAM, 0xfae6de, 0xf5dcd3, 0xe4cac2, pattern: PatternLeaf);
		Def(LEAF_MINT, 0xd9dff3, 0xccd3ec, 0xb9c0da, pattern: PatternLeaf);
		Def(LEAF_ROSE, 0xf3c0cd, 0xedb5c3, 0xdca2b1, pattern: PatternLeaf);

		Def(LANTERN, 0xffdcb8, 0xffd2a8, 0xf6c79e, emissive: 0.75f);
		Def(CRYSTAL, 0xdfd0f7, 0xd2c0f1, 0xc2aee5, emissive: 0.42f);
		Def(WINDOW, 0xffe6bc, 0xffdcac, 0xf3cb9c, emissive: 0.62f);
		// Falling water is voxels, not a second transparent pass: a pale,
		// faintly luminous column that catches the bloom and reads as spray.
		Def(CASCADE, 0xeaf1ff, 0xdde8fb, 0xccdaf2, emissive: 0.14f);
	}

	public static readonly byte[] LeafBlocks =
	{
		LEAF_PINK, LEAF_BLUSH, LEAF_LILAC, LEAF_CREAM, LEAF_MINT, LEAF_ROSE,
	};
}
