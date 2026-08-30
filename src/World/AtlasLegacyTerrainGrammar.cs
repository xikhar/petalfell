using System;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// The successful local shelf grammar from the sunset terrain, expressed as a
/// pure atlas-space field. The production map still owns the continent-scale
/// elevation; this field only adds the old warped, piecewise-flat rooms and
/// hollows on top of that guide. Candidate cells and every noise lookup are in
/// absolute coordinates so independently compiled sectors see the same shapes.
/// </summary>
internal sealed class AtlasLegacyTerrainGrammar
{
	public const int EdgeGrid = 6;
	private const float LegacyScale = 256f / 192f;
	private const int MidGrid = 38;
	private const int GladeGrid = 148;
	private const int CrownGrid = 181;
	private static readonly float[][] CrownRadii =
	{
		new[] { 30f, 24f, 18f, 13f, 8f },
		new[] { 26f, 19f, 13f },
		new[] { 22f, 16f, 11f, 7f },
	};

	private readonly int _seed;
	private readonly Noise2D _edgeWarp;
	private readonly Noise2D _macroWarpX;
	private readonly Noise2D _macroWarpZ;
	private readonly Noise2D _ledge;

	private readonly record struct Disc(float X, float Z, float Radius, float Offset,
		float AxisX, float AxisZ, float WarpAmplitude, float Sign);

	public AtlasLegacyTerrainGrammar(int seed)
	{
		_seed = seed;
		_edgeWarp = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:disc-warp")));
		_macroWarpX = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:macro-warp-x")));
		_macroWarpZ = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:macro-warp-z")));
		_ledge = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:toe-ledge")));
	}

	/// <summary>
	/// The atlas source is a guide, not a visible eight-block stencil. A small,
	/// low-frequency coordinate wander moves its contours and coast by at most a
	/// few legacy rooms without changing the continent's macro composition.
	/// Callers reject samples that cross incompatible land/water ownership.
	/// </summary>
	public Vector2 GuideWarpAt(int globalX, int globalZ)
	{
		const float wavelength = 384f;
		const float amplitude = 30f;
		return new Vector2(
			_macroWarpX.Fbm(globalX / wavelength, globalZ / wavelength, 3) * amplitude,
			_macroWarpZ.Fbm((globalX + 113f) / wavelength, (globalZ - 79f) / wavelength, 3) * amplitude);
	}

	/// <summary>
	/// Recreates the old BuildDiscs/DiscAt composition without a whole-continent
	/// catalogue: one raised or sunken warped ellipse per 38-block cell, sparse
	/// glades, and nested crowns only on high authored ground. This deliberately
	/// excludes the old radial island rim and every generated architectural
	/// landmark.
	/// </summary>
	public float TerraceOffsetAt(int globalX, int globalZ, float authoredElevation)
	{
		int x = FloorDiv(globalX, EdgeGrid) * EdgeGrid + EdgeGrid / 2;
		int z = FloorDiv(globalZ, EdgeGrid) * EdgeGrid + EdgeGrid / 2;
		float sum = 0f;

		int midX = FloorDiv(x, MidGrid);
		int midZ = FloorDiv(z, MidGrid);
		for (int cz = midZ - 2; cz <= midZ + 2; cz++)
		for (int cx = midX - 2; cx <= midX + 2; cx++)
		{
			float centreX = (cx + 0.5f) * MidGrid + Bell(cx, cz, 1) * MidGrid * 0.075f;
			float centreZ = (cz + 0.5f) * MidGrid + Bell(cx, cz, 5) * MidGrid * 0.075f;
			float sign = Hash01(cx, cz, 9) < 0.34f ? -1f : 1f;
			var disc = new Disc(
				centreX, centreZ,
				Rng.Lerp(8.5f, 17f, Hash01(cx, cz, 13)) * LegacyScale,
				971f * (cx * 7f + cz + 3f),
				Rng.Lerp(0.70f, 1.35f, Hash01(cx, cz, 17)),
				Rng.Lerp(0.70f, 1.35f, Hash01(cx, cz, 19)),
				Rng.Lerp(4.5f, 8.5f, Hash01(cx, cz, 23)) * LegacyScale,
				sign);
			sum += DiscAt(x, z, disc);
		}

		int gladeX = FloorDiv(x, GladeGrid);
		int gladeZ = FloorDiv(z, GladeGrid);
		for (int cz = gladeZ - 1; cz <= gladeZ + 1; cz++)
		for (int cx = gladeX - 1; cx <= gladeX + 1; cx++)
		{
			var disc = new Disc(
				(cx + Hash01(cx, cz, 31)) * GladeGrid,
				(cz + Hash01(cx, cz, 37)) * GladeGrid,
				Rng.Lerp(9f, 13f, Hash01(cx, cz, 41)) * LegacyScale,
				4441f + cx * 37f + cz * 101f,
				1f, 1f,
				Rng.Lerp(4f, 6f, Hash01(cx, cz, 43)) * LegacyScale,
				-1f);
			sum += DiscAt(x, z, disc);
		}

		// The old crowns were stacks centred on the highest planner regions. A
		// global candidate lattice supplies the same density while the accepted
		// elevation map, rather than a seed-selected region list, decides where a
		// crown is allowed to exist.
		float crownMask = Rng.Smoothstep(0.50f, 0.68f, authoredElevation);
		if (crownMask > 0f)
		{
			int crownX = FloorDiv(x, CrownGrid);
			int crownZ = FloorDiv(z, CrownGrid);
			for (int cz = crownZ - 1; cz <= crownZ + 1; cz++)
			for (int cx = crownX - 1; cx <= crownX + 1; cx++)
			{
				float centreX = (cx + 0.5f) * CrownGrid + Bell(cx, cz, 47) * CrownGrid * 0.22f;
				float centreZ = (cz + 0.5f) * CrownGrid + Bell(cx, cz, 53) * CrownGrid * 0.22f;
				float candidateDx = x - centreX;
				float candidateDz = z - centreZ;
				if (candidateDx * candidateDx + candidateDz * candidateDz > 72f * 72f) continue;
				int family = (int)(Hash01(cx, cz, 59) * 3f);
				float[] radii = CrownRadii[Math.Clamp(family, 0, CrownRadii.Length - 1)];
				for (int ring = 0; ring < radii.Length; ring++)
				{
					centreX += Bell(cx, cz, 61 + ring * 4) * 5f * LegacyScale;
					centreZ += Bell(cx, cz, 63 + ring * 4) * 5f * LegacyScale;
					var disc = new Disc(centreX, centreZ, radii[ring] * LegacyScale,
						137f * (cx * 11f + cz * 17f + ring + 1f), 1f, 1f,
						12f * LegacyScale * (1f - ring * 0.09f), 1f);
					sum += DiscAt(x, z, disc) * crownMask;
				}
			}
		}

		return sum;
	}

	public bool KeepToeLedge(int globalX, int globalZ)
	{
		float x = FloorDiv(globalX, EdgeGrid) * EdgeGrid + EdgeGrid / 2f;
		float z = FloorDiv(globalZ, EdgeGrid) * EdgeGrid + EdgeGrid / 2f;
		return _ledge.Fbm(x * 0.035f, z * 0.035f, 2) >= 0.10f;
	}

	private float DiscAt(int x, int z, in Disc disc)
	{
		float dx = x - disc.X;
		float dz = z - disc.Z;
		float reach = disc.Radius * MathF.Max(disc.AxisX, disc.AxisZ) +
			disc.WarpAmplitude + EdgeGrid * 2f;
		if (dx * dx + dz * dz > reach * reach) return 0f;
		float warp = _edgeWarp.Fbm01((x + disc.Offset) * 0.016f,
			(z + disc.Offset) * 0.016f, 3) * 2f - 1f;
		float distance = MathF.Sqrt(
			(dx / disc.AxisX) * (dx / disc.AxisX) +
			(dz / disc.AxisZ) * (dz / disc.AxisZ)) + warp * disc.WarpAmplitude;
		return (1f - Rng.Smoothstep(disc.Radius - 1f, disc.Radius + 1f, distance)) * disc.Sign;
	}

	private float Bell(int x, int z, int salt) =>
		(Hash01(x, z, salt) + Hash01(x, z, salt + 1) + Hash01(x, z, salt + 2)) / 1.5f - 1f;

	private float Hash01(int x, int z, int salt)
	{
		unchecked
		{
			uint h = (uint)(x * 374761393 + z * 668265263 +
				salt * 1442695040 + _seed * unchecked((int)0x9e3779b1));
			h = (h ^ (h >> 13)) * 1274126177u;
			return (h ^ (h >> 16)) / 4294967296f;
		}
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
	}
}
