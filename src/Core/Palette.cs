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
	public static readonly Color SkyZenith = C(0xc5cbf3);
	public static readonly Color SkyHorizon = C(0xeeeafa);
	public static readonly Color SkyGround = C(0xe1d9ef);
	public static readonly Color SkyHaze = C(0xdadbf5);
	public static readonly Color SunTint = C(0xfff0f2);

	public static readonly Color SunColor = C(0xfff0ee);
	public const float SunIntensity = 2.2f;
	/// <summary>Sun low-ish and behind-left so cliffs throw long readable shadows.</summary>
	public static readonly Vector3 SunDir = new Vector3(-0.55f, 0.78f, -0.32f).Normalized();
	public static readonly Color LightSky = C(0xd8d5f0);
	public static readonly Color LightGround = C(0xebd8dc);
	public static readonly Color FillColor = C(0xdfd2ef);

	/* ---------------- water ---------------- */
	public static readonly Color WaterShoal = C(0xcfc2e6);
	public static readonly Color WaterShallow = C(0x9b8ad4);
	public static readonly Color WaterDeep = C(0x5b4f9e);
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
	/// white, and its opacity is low on purpose — a pale edge is a hint that a
	/// surface turned, not a highlight.
	/// </summary>
	public static readonly Color InkDark = new Color(0.39f, 0.32f, 0.43f).SrgbToLinear();
	public static readonly Color InkLight = new Color(0.97f, 0.95f, 0.97f).SrgbToLinear();
	public const float InkDarkOpacity = 0.62f;
	public const float InkLightOpacity = 0.30f;
	/// <summary>
	/// Stroke width in framebuffer pixels. The reference ships 1.85 as its
	/// authored default; the settings slider reaches 3.2 and that is what the
	/// plan calls the visual starting point. 1.85 is canon until told otherwise.
	/// </summary>
	public const float InkWidth = 1.85f;

	/* ---------------- grade ---------------- */
	public const float GradeExposure = 1.12f;
	public static readonly Vector3 GradeLift = new(0.006f, 0.004f, 0.014f);
	public static readonly Vector3 GradeGamma = new(1.00f, 1.005f, 0.99f);
	public static readonly Vector3 GradeGain = new(1.03f, 1.02f, 1.03f);
	public const float GradeSaturation = 1.19f;
	public const float GradeContrast = 1.10f;
	public const float GradeVignette = 0.06f;

	/* ---------------- petals ---------------- */
	public static readonly Color[] PetalColors =
	{
		C(0xf7b9cc), C(0xf3a8c0), C(0xe6c0ef), C(0xd7b3ee), C(0xfbd3dc), C(0xf8e2c8),
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
	public const byte PLANK = 20;
	public const byte PLANK_PALE = 21;
	public const byte BEAM = 22;
	public const byte TRUNK = 23;
	public const byte TRUNK_PALE = 24;
	public const byte TRUNK_ROSE = 25;
	public const byte LEAF_PINK = 30;
	public const byte LEAF_BLUSH = 31;
	public const byte LEAF_LILAC = 32;
	public const byte LEAF_CREAM = 33;
	public const byte LEAF_MINT = 34;
	public const byte LEAF_ROSE = 35;
	public const byte LANTERN = 40;
	public const byte CRYSTAL = 41;
	public const byte CASCADE = 50;

	public struct BlockDef
	{
		public Color Top, Side, Bottom;
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
		bool lightEdge = false, float emissive = 0f)
	{
		var d = new BlockDef
		{
			Top = C(top), Side = C(side), Bottom = C(bottom),
			Solid = true, LightEdge = lightEdge, Emissive = emissive,
		};
		// The pale/dark split is judged on the authored sRGB value, not on the
		// linear one. Luminance in linear space is a different number entirely,
		// and the 0.61 threshold was tuned against the hex.
		d.TopLight = Luma(Srgb(top)) >= LightFaceLuma;
		d.SideLight = Luma(Srgb(side)) >= LightFaceLuma;
		d.BottomLight = Luma(Srgb(bottom)) >= LightFaceLuma;
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
		Def(GRASS, 0xbcc98f, 0xbcc98f, 0xa8b57f, lightEdge: true);
		Def(GRASS_LIGHT, 0xc9d49f, 0xc9d49f, 0xb5c18e, lightEdge: true);
		Def(GRASS_DEEP, 0xaaba7e, 0xaaba7e, 0x97a86f, lightEdge: true);
		Def(GRASS_STONE, 0xbcc98f, 0xbcc98f, 0xa8b57f, lightEdge: true);
		Def(GRASS_LIGHT_STONE, 0xc9d49f, 0xc9d49f, 0xb5c18e, lightEdge: true);
		Def(GRASS_DEEP_STONE, 0xaaba7e, 0xaaba7e, 0x97a86f, lightEdge: true);

		Def(SOIL, 0xc09082, 0xc59484, 0xb08173);
		Def(SAND, 0xefe3cb, 0xe5d6bc, 0xd2c1a6);

		// Biome surfaces. Each province has to be recognisable from its ground
		// alone — the plan's whole point in having provinces is that you can
		// tell where you are by looking, before any tree or building appears.
		Def(SNOW, 0xf4f2fa, 0xe9e6f3, 0xd6d2e6, lightEdge: true);
		Def(MOSS, 0x9fb384, 0x9fb384, 0x8ba070, lightEdge: true);
		Def(MUD, 0xa2907f, 0x978575, 0x827265);
		Def(BLOSSOM_DRIFT, 0xf0dce2, 0xe9d2da, 0xd6bec7, lightEdge: true);
		Def(SCREE, 0xcfc8d9, 0xc2bbcf, 0xaea6bd);

		// The cool end of the run. Stone is where the world is allowed to go
		// bluish, which is what keeps the mauve soil above it reading as warm.
		Def(STONE, 0xc9c3db, 0xbcb5cf, 0xa79fbc);
		Def(STONE_PALE, 0xddd9eb, 0xd0cae1, 0xbbb4d0);
		Def(STONE_WARM, 0xd5c5d2, 0xc8b7c6, 0xb19fad);
		Def(PATH, 0xe4dce7, 0xd5cddd, 0xc2bacd);

		Def(PLANK, 0xe7bbb0, 0xdfafa4, 0xcc9a90);
		Def(PLANK_PALE, 0xf0cfc6, 0xe8c2ba, 0xd5aca4);
		Def(BEAM, 0xcc958e, 0xc08a84, 0xac7871);
		// Trunks are the darkest value in the world by a wide margin, and that
		// is deliberate: they carry the only real contrast, which is what lets
		// pale blossom read as solid objects against a pale sky.
		Def(TRUNK, 0x8a6270, 0x785462, 0x664654);
		Def(TRUNK_PALE, 0x9a7080, 0x886073, 0x745164);
		Def(TRUNK_ROSE, 0x9c7783, 0x8d6875, 0x7b5866);

		Def(LEAF_PINK, 0xf8ccda, 0xf3c1d0, 0xe5b0bf);
		Def(LEAF_BLUSH, 0xfcdee4, 0xf8d2da, 0xebbfc8);
		Def(LEAF_LILAC, 0xdccef1, 0xd2c2eb, 0xc0b0da);
		Def(LEAF_CREAM, 0xfae6de, 0xf5dcd3, 0xe4cac2);
		Def(LEAF_MINT, 0xd9dff3, 0xccd3ec, 0xb9c0da);
		Def(LEAF_ROSE, 0xf3c0cd, 0xedb5c3, 0xdca2b1);

		Def(LANTERN, 0xffdcb8, 0xffd2a8, 0xf6c79e, emissive: 0.75f);
		Def(CRYSTAL, 0xdfd0f7, 0xd2c0f1, 0xc2aee5, emissive: 0.42f);
		// Falling water is voxels, not a second transparent pass: a pale,
		// faintly luminous column that catches the bloom and reads as spray.
		Def(CASCADE, 0xeaf1ff, 0xdde8fb, 0xccdaf2, emissive: 0.14f);
	}

	public static readonly byte[] LeafBlocks =
	{
		LEAF_PINK, LEAF_BLUSH, LEAF_LILAC, LEAF_CREAM, LEAF_MINT, LEAF_ROSE,
	};
}
