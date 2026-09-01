using System;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Production local shelf grammar expressed as a pure atlas-space field. The
/// accepted map owns continent-scale elevation; this field adds warped,
/// piecewise-flat rooms and hollows without changing that macro composition.
/// Candidate cells and every noise lookup use absolute coordinates so moving
/// windows see the same shapes.
/// </summary>
internal sealed class ProductionTerrainGrammar
{
	public const int EdgeGrid = 6;
	private const float ReferenceScale = 256f / 192f;
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
	private readonly Noise2D _mountainWarp;
	private readonly Noise2D _mountainPrimary;
	private readonly Noise2D _mountainSecondary;
	private readonly Noise2D _mountainShoulder;

	private readonly record struct Disc(float X, float Z, float Radius, float Offset,
		float AxisX, float AxisZ, float WarpAmplitude, float Sign);

	public ProductionTerrainGrammar(int seed)
	{
		_seed = seed;
		// These stable salt strings predate the production name. Changing their text
		// would change the accepted terrain, so keep them byte-for-byte.
		_edgeWarp = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:disc-warp")));
		_macroWarpX = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:macro-warp-x")));
		_macroWarpZ = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:macro-warp-z")));
		_ledge = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:toe-ledge")));
		_mountainWarp = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:mountain-warp")));
		_mountainPrimary = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:mountain-primary")));
		_mountainSecondary = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:mountain-secondary")));
		_mountainShoulder = new Noise2D(unchecked(seed ^ Rng.StableHash("atlas:legacy:mountain-shoulder")));
	}

	/// <summary>
	/// The atlas source is a guide, not a visible eight-block stencil. A small,
	/// low-frequency coordinate wander moves its contours and coast by at most a
	/// few terrain rooms without changing the continent's macro composition.
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
	/// Evaluates the accepted shelf composition without a whole-continent
	/// catalogue: one raised or sunken warped ellipse per 38-block cell, sparse
	/// glades, and nested crowns only on high authored ground. This deliberately
	/// excludes radial world boundaries and every generated architectural
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
				Rng.Lerp(8.5f, 17f, Hash01(cx, cz, 13)) * ReferenceScale,
				971f * (cx * 7f + cz + 3f),
				Rng.Lerp(0.70f, 1.35f, Hash01(cx, cz, 17)),
				Rng.Lerp(0.70f, 1.35f, Hash01(cx, cz, 19)),
				Rng.Lerp(4.5f, 8.5f, Hash01(cx, cz, 23)) * ReferenceScale,
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
				Rng.Lerp(9f, 13f, Hash01(cx, cz, 41)) * ReferenceScale,
				4441f + cx * 37f + cz * 101f,
				1f, 1f,
				Rng.Lerp(4f, 6f, Hash01(cx, cz, 43)) * ReferenceScale,
				-1f);
			sum += DiscAt(x, z, disc);
		}

		// A global crown-candidate lattice supplies stable density while the accepted
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
					centreX += Bell(cx, cz, 61 + ring * 4) * 5f * ReferenceScale;
					centreZ += Bell(cx, cz, 63 + ring * 4) * 5f * ReferenceScale;
					var disc = new Disc(centreX, centreZ, radii[ring] * ReferenceScale,
						137f * (cx * 11f + cz * 17f + ring + 1f), 1f, 1f,
						12f * ReferenceScale * (1f - ring * 0.09f), 1f);
					sum += DiscAt(x, z, disc) * crownMask;
				}
			}
		}

		return sum;
	}

	/// <summary>
	/// Cut the accepted northern high mass into connected spines, saddles and
	/// shoulders. The elevation map decides where a mountain exists; this field
	/// only gives that mass the large internal relief the source image cannot
	/// encode at block scale. It is continuous and global, so it creates neither
	/// seed-chosen landmarks nor window seams.
	/// </summary>
	public float MountainReliefAt(int globalX, int globalZ, float authoredElevation)
	{
		float high = Rng.Smoothstep(.68f, .91f, authoredElevation);
		if (high <= .001f) return 0f;

		var point = new Vector2(globalX, globalZ);
		float warp = _mountainWarp.Fbm(globalX / 460f, globalZ / 460f, 3) * 42f;
		point += new Vector2(warp, -warp * .63f);
		float primary = Anisotropic(_mountainPrimary, point,
			Mathf.DegToRad(-24f), 620f, 112f);
		float secondary = Anisotropic(_mountainSecondary,
			point + new Vector2(83f, -47f), Mathf.DegToRad(51f), 470f, 138f);
		float primaryRidge = MathF.Pow(1f - MathF.Abs(primary), 4f);
		float secondaryRidge = MathF.Pow(1f - MathF.Abs(secondary), 4f) * .82f;
		float spine = Rng.Smoothstep(.16f, .66f,
			Math.Max(primaryRidge, secondaryRidge));
		float shoulder = _mountainShoulder.Fbm(globalX / 280f,
			globalZ / 280f, 3) * 5f;
		return Rng.Clamp(((spine - .48f) * 38f + shoulder) * high, -19f, 21f);
	}

	private static float Anisotropic(Noise2D noise, Vector2 point,
		float angle, float alongWave, float acrossWave)
	{
		float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
		float along = (point.X * cos + point.Y * sin) / alongWave;
		float across = (-point.X * sin + point.Y * cos) / acrossWave;
		return noise.Fbm(along, across, 3);
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
