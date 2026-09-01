using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

public enum Biome { Meadow, Forest, Plains, Sakura, Highland, SnowyHills, Shore, Wetland }

public sealed class Region
{
	public int Id;
	public float Cx, Cz;
	public float Elevation, Temperature, Moisture, Magic;
	/// <summary>
	/// How long ago people gave this ground up. 0 is still held, 1 is gone for
	/// generations. See Planner.SampleAbandonment.
	/// </summary>
	public float Abandonment;
	public Biome Biome;
	public uint Seed;
	public float Importance;
	public List<int> Neighbours = new();
}

public sealed class Channel
{
	public int Order;
	public int Source;
	public bool Outflow, Tributary;
	public List<(float x, float z)> Points = new();
}

public sealed class LakePlan
{
	public int Region;
	public float Cx, Cz, Radius;
}

/// <summary>
/// Deterministic macro planner for a content-authored map.
///
/// This stage decides places before terrain writes blocks. The map definition
/// owns fixed geography and biome intent; region sampling and noise provide
/// stable natural infill between those authored controls. The Three.js planner
/// remains a visual reference, not a coordinate target.
/// </summary>
public sealed class Planner
{
	public const int Cell = 8;
	public const float RegionSpacing = 30f;
	public const float IslandFraction = 0.427f;

	private const float FeatureElevation = 210f;
	private const float FeatureTemperature = 320f;
	private const float FeatureMoisture = 300f;
	private const float FeatureMagic = 250f;

	public readonly int Size;
	public readonly MapDefinition Definition;
	public readonly ProductionTerrainGuide AtlasGuide;
	public readonly int CellW;
	public readonly List<Region> Regions = new();
	public readonly int[] CellRegion;
	public readonly byte[] CellBiome;
	public readonly float[] CellElevation;
	public readonly float[] CellAbandonment;
	public readonly List<Channel> Rivers = new();
	public readonly List<LakePlan> Lakes = new();
	public Region LakeRegion;
	public float LakeRadius;
	public readonly float IslandR;

	private readonly List<Region>[] _buckets;
	private readonly int _bucketW;
	private readonly Noise2D _nEdge;
	private readonly Noise2D _nWander;
	private readonly Noise2D _nRim;
	private readonly Noise2D _nAtlasAbandonment;
	private readonly Region[] _atlasBiomeRegions;

	private float GlobalX(float localX) => AtlasGuide?.GlobalX(localX) ?? localX;
	private float GlobalZ(float localZ) => AtlasGuide?.GlobalZ(localZ) ?? localZ;

	public Planner(int seed, int size, MapDefinition definition, ProductionTerrainGuide atlasGuide = null)
	{
		Size = size;
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		AtlasGuide = atlasGuide;
		CellW = (int)MathF.Ceiling(size / (float)Cell);
		CellRegion = new int[CellW * CellW];
		CellBiome = new byte[CellW * CellW];
		CellElevation = new float[CellW * CellW];
		CellAbandonment = new float[CellW * CellW];
		IslandR = size * MathF.Min(Definition.Boundary.RadiusX, Definition.Boundary.RadiusZ);

		var rng = new Rng(unchecked(seed ^ (int)0x9e3779b9u));
		var nTemp = new Noise2D(seed + 41);
		var nMoist = new Noise2D(seed + 42);
		var nMagic = new Noise2D(seed + 43);
		_nEdge = new Noise2D(seed + 44);
		_nWander = new Noise2D(seed + 45);
		_nRim = new Noise2D(seed + 7);
		_nAtlasAbandonment = new Noise2D(seed + 61);
		_atlasBiomeRegions = new Region[Enum.GetValues<Biome>().Length];
		for (int i = 0; i < _atlasBiomeRegions.Length; i++)
			_atlasBiomeRegions[i] = new Region
			{
				Id = i,
				Biome = (Biome)i,
				Elevation = .5f,
				Abandonment = .7f,
				Seed = unchecked((uint)(seed ^ (i * unchecked((int)0x85ebca6bu)))),
			};

		PlaceRegions(rng);
		SampleFields(seed, nTemp, nMoist, nMagic);

		_bucketW = Math.Max(1, (int)MathF.Ceiling(size / RegionSpacing));
		_buckets = new List<Region>[_bucketW * _bucketW];
		for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new List<Region>();
		foreach (var r in Regions)
		{
			int bx = Rng.ClampI((int)(r.Cx / RegionSpacing), 0, _bucketW - 1);
			int bz = Rng.ClampI((int)(r.Cz / RegionSpacing), 0, _bucketW - 1);
			_buckets[bz * _bucketW + bx].Add(r);
		}

		SampleAbandonment(seed);
		BuildCellMap();
		BuildCellElevation();
		BuildCellAbandonment();
		RouteWater(rng);
	}

	/* ----------------------------------------------------------------
	 * Region centres — the reference's bounded dart throwing.
	 * ---------------------------------------------------------------- */
	private void PlaceRegions(Rng rng)
	{
		float radius = RegionSpacing;
		float radius2 = radius * radius;
		int gw = Math.Max(1, (int)MathF.Ceiling(Size / radius));
		var grid = new int[gw * gw];
		Array.Fill(grid, -1);
		int budget = (int)MathF.Ceiling(Size * Size / (radius * radius)) * 30;

		for (int attempt = 0; attempt < budget; attempt++)
		{
			float x = rng.Range(0f, Size), z = rng.Range(0f, Size);
			int gx = Math.Min(gw - 1, (int)(x / radius));
			int gz = Math.Min(gw - 1, (int)(z / radius));
			bool ok = true;

			for (int dz = -2; dz <= 2 && ok; dz++)
			for (int dx = -2; dx <= 2 && ok; dx++)
			{
				int ix = gx + dx, iz = gz + dz;
				if (ix < 0 || iz < 0 || ix >= gw || iz >= gw) continue;
				int other = grid[iz * gw + ix];
				if (other < 0) continue;
				var p = Regions[other];
				float px = p.Cx - x, pz = p.Cz - z;
				if (px * px + pz * pz < radius2) ok = false;
			}

			int cell = gz * gw + gx;
			if (!ok || grid[cell] >= 0) continue;
			grid[cell] = Regions.Count;
			Regions.Add(new Region { Id = Regions.Count, Cx = x, Cz = z });
		}
	}

	private void SampleFields(int seed, Noise2D nTemp, Noise2D nMoist, Noise2D nMagic)
	{
		foreach (var r in Regions)
		{
			float nx = r.Cx / Size, nz = r.Cz / Size;
			if (AtlasGuide != null)
			{
				// The production map owns the macro field. Planner retains the accepted
				// low-level terrain grammar below that guide.
				r.Elevation = AtlasGuide.ElevationAt(r.Cx, r.Cz);
				float gx = GlobalX(r.Cx), gz = GlobalZ(r.Cz);
				r.Temperature = Rng.Clamp(0.5f +
					nTemp.Fbm(gx / FeatureTemperature, gz / FeatureTemperature, 3), 0f, 1f);
				r.Moisture = Rng.Clamp(0.5f +
					nMoist.Fbm(gx / FeatureMoisture, gz / FeatureMoisture, 3), 0f, 1f);
				r.Magic = Rng.Clamp(0.5f +
					nMagic.Fbm(gx / FeatureMagic, gz / FeatureMagic, 2), 0f, 1f);
				r.Biome = AtlasGuide.BiomeAt(r.Cx, r.Cz);
				r.Seed = unchecked((uint)(seed ^ (r.Id * unchecked((int)0x85ebca6bu))));
				continue;
			}
			float rim = Definition.BoundaryDistance(nx, nz);
			r.Elevation = Rng.Clamp(0.94f - rim * 0.70f +
				_nEdge.Fbm(r.Cx / FeatureElevation, r.Cz / FeatureElevation, 3) * 0.42f, 0f, 1f);
			r.Temperature = Rng.Clamp(0.5f +
				nTemp.Fbm(r.Cx / FeatureTemperature, r.Cz / FeatureTemperature, 3), 0f, 1f);
			r.Moisture = Rng.Clamp(0.5f +
				nMoist.Fbm(r.Cx / FeatureMoisture, r.Cz / FeatureMoisture, 3), 0f, 1f);
			r.Magic = Rng.Clamp(0.5f +
				nMagic.Fbm(r.Cx / FeatureMagic, r.Cz / FeatureMagic, 2), 0f, 1f);
			// Elevation zones establish the chapter's plateaus, valleys and
			// basins. Noise survives inside each zone, but it cannot move a major
			// destination into a different part of the map.
			foreach (var zone in Definition.ElevationZones)
			{
				float influence = MapDefinition.Influence(zone.Centre, zone.RadiusX,
					zone.RadiusZ, zone.Inner, nx, nz) * Rng.Clamp(zone.Strength, 0f, 1f);
				r.Elevation = Rng.Lerp(r.Elevation, Rng.Clamp(zone.Target, 0f, 1f), influence);
			}

			// Shore is a biome about standing at the water's edge, not merely
			// about being low. Claiming everything under 0.15 gave it 28% of the
			// map, which put pale sand where the chapter wants lush terraces —
			// and the band between here and the old threshold has somewhere far
			// better to go, since the moisture rules below produce wetland,
			// meadow or plains from exactly that ground.
			if (r.Elevation < 0.09f) r.Biome = Biome.Shore;
			else if (r.Elevation > 0.86f && r.Temperature < 0.58f) r.Biome = Biome.SnowyHills;
			else if (r.Elevation > 0.78f) r.Biome = Biome.Highland;
			else if (r.Moisture > 0.64f) r.Biome = Biome.Wetland;
			else if (r.Moisture > 0.54f && r.Magic > 0.44f) r.Biome = Biome.Sakura;
			else if (r.Moisture > 0.48f) r.Biome = Biome.Forest;
			else if (r.Moisture < 0.38f) r.Biome = Biome.Plains;
			else r.Biome = Biome.Meadow;

			// Biome zones are soft ownership fields. Below the threshold the
			// climate result wins, which creates a real transition rather than a
			// hard circular paint mask.
			float strongest = 0f;
			Biome authored = r.Biome;
			foreach (var zone in Definition.BiomeZones)
			{
				float influence = MapDefinition.Influence(zone.Centre, zone.RadiusX,
					zone.RadiusZ, zone.Inner, nx, nz) * Rng.Clamp(zone.Strength, 0f, 1f);
				if (influence <= strongest) continue;
				strongest = influence;
				authored = zone.Biome;
			}
			if (strongest >= 0.28f) r.Biome = authored;

			r.Seed = unchecked((uint)(seed ^ (r.Id * unchecked((int)0x85ebca6bu))));
		}
	}

	/// <summary>
	/// How long ago each region was given up.
	///
	/// This is the field the whole post-population world hangs off, and it exists
	/// as ONE authority on purpose. Remnant decay, road reclamation, how far
	/// vegetation has taken a building back, which landmarks appear and later how
	/// densely the wilds hold a place all have to answer "how gone is this?" — and
	/// if each of them derives its own answer they disagree, visibly, because the
	/// player reads all of them in the same glance.
	///
	/// The shape of it is the retreat described in plan.md §2.1. People fell back
	/// toward the coast and the surviving roads, so abandonment grows INLAND and
	/// UPWARD: the shore was held longest, the high country went first. Province
	/// matters too — nobody fought to keep a bog or a snowfield, and the good
	/// meadows were the last to be let go.
	///
	/// The noise is deliberately coarse and warped rather than smooth, so the
	/// frontier of the retreat is ragged. A tidy radial gradient would read as a
	/// difficulty ring painted on the map, which is exactly what §8.1 says this
	/// must not be.
	/// </summary>
	private void SampleAbandonment(int seed)
	{
		var nAge = new Noise2D(seed + 61);
		foreach (var r in Regions)
		{
			float nx = r.Cx / Size, nz = r.Cz / Size;
			// Distance from the rim, 0 at the coast and 1 deep inland.
			float inland = 1f - Rng.Clamp(Definition.BoundaryDistance(nx, nz), 0f, 1f);

			// The constant is doing real work. Regions are spread over a disc, so
			// area — and therefore most of the map — sits well away from the rim;
			// without a floor the mean landed at 0.23 and half the continent came
			// out "still held", which is the opposite of the premise. The country
			// that was kept is a THIN fringe, and everything behind it is gone.
			float age = 0.30f + inland * 0.62f + Rng.Clamp(r.Elevation, 0f, 1f) * 0.30f;

			// What the ground was worth staying for.
			age += r.Biome switch
			{
				Biome.Meadow => -0.16f,
				Biome.Plains => -0.12f,
				Biome.Shore => -0.18f,
				Biome.Forest => -0.02f,
				Biome.Sakura => 0.02f,
				Biome.Highland => 0.12f,
				Biome.Wetland => 0.16f,
				Biome.SnowyHills => 0.20f,
				_ => 0f,
			};

			age += nAge.Fbm(GlobalX(r.Cx) / 260f, GlobalZ(r.Cz) / 260f, 3) * 0.26f;
			r.Abandonment = Rng.Clamp(age, 0f, 1f);
		}
	}

	private void BuildCellAbandonment()
	{
		var scratch = new List<Region>(96);
		for (int cz = 0; cz < CellW; cz++)
		for (int cx = 0; cx < CellW; cx++)
		{
			float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
			float num = 0f, den = 0f;
			foreach (var r in Nearby(wx, wz, 2, scratch))
			{
				float dx = r.Cx - wx, dz = r.Cz - wz;
				float d2 = MathF.Max(dx * dx + dz * dz, 1f);
				float w = 1f / (d2 * d2);
				num += r.Abandonment * w;
				den += w;
			}
			CellAbandonment[cz * CellW + cx] = den > 0f ? num / den : 0.5f;
		}
	}

	/// <summary>How long ago this ground was given up, 0 still held to 1 long gone.</summary>
	public float AbandonmentAt(float x, float z)
	{
		if (AtlasGuide != null)
		{
			float elevation = AtlasGuide.ElevationAt(x, z);
			Biome biome = AtlasGuide.BiomeAt(x, z);
			float age = .42f + elevation * .34f + biome switch
			{
				Biome.Meadow => -.16f,
				Biome.Plains => -.12f,
				Biome.Shore => -.18f,
				Biome.Forest => -.02f,
				Biome.Sakura => .02f,
				Biome.Highland => .12f,
				Biome.Wetland => .16f,
				Biome.SnowyHills => .20f,
				_ => 0f,
			};
			age += _nAtlasAbandonment.Fbm(GlobalX(x) / 260f,
				GlobalZ(z) / 260f, 3) * .26f;
			return Rng.Clamp(age, 0f, 1f);
		}
		float fx = Rng.Clamp(x / Cell - 0.5f, 0f, CellW - 1.001f);
		float fz = Rng.Clamp(z / Cell - 0.5f, 0f, CellW - 1.001f);
		int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
		float tx = fx - x0, tz = fz - z0;
		float a = CellAbandonment[z0 * CellW + x0];
		float b = CellAbandonment[z0 * CellW + x0 + 1];
		float c = CellAbandonment[(z0 + 1) * CellW + x0];
		float d = CellAbandonment[(z0 + 1) * CellW + x0 + 1];
		return Rng.Lerp(Rng.Lerp(a, b, tx), Rng.Lerp(c, d, tx), tz);
	}

	private List<Region> Nearby(float x, float z, int rings, List<Region> output)
	{
		output.Clear();
		int bx = Rng.ClampI((int)(x / RegionSpacing), 0, _bucketW - 1);
		int bz = Rng.ClampI((int)(z / RegionSpacing), 0, _bucketW - 1);
		for (int r = rings; r <= rings + 6 && output.Count == 0; r++)
		for (int dz = -r; dz <= r; dz++)
		for (int dx = -r; dx <= r; dx++)
		{
			int ix = bx + dx, iz = bz + dz;
			if (ix < 0 || iz < 0 || ix >= _bucketW || iz >= _bucketW) continue;
			output.AddRange(_buckets[iz * _bucketW + ix]);
		}
		return output;
	}

	private void BuildCellMap()
	{
		float warp = RegionSpacing * 0.5f;
		var scratch = new List<Region>(64);
		for (int cz = 0; cz < CellW; cz++)
		for (int cx = 0; cx < CellW; cx++)
		{
			float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
			float gx = GlobalX(wx), gz = GlobalZ(wz);
			float qx = wx + _nEdge.Fbm(gx * 0.006f, gz * 0.006f, 3) * warp;
			float qz = wz + _nEdge.Fbm(gx * 0.006f + 19f, gz * 0.006f + 7f, 3) * warp;
			var candidates = Nearby(qx, qz, 1, scratch);
			Region best = candidates[0];
			float bestD = float.MaxValue;
			foreach (var r in candidates)
			{
				float dx = r.Cx - qx, dz = r.Cz - qz;
				float d = dx * dx + dz * dz;
				if (d < bestD) { bestD = d; best = r; }
			}
			int i = cz * CellW + cx;
			CellRegion[i] = best.Id;
			CellBiome[i] = (byte)best.Biome;
		}

			// Region boundaries form the adjacency skeleton used by later roads,
			// waterways and settlement ownership.
		for (int cz = 0; cz < CellW; cz++)
		for (int cx = 0; cx < CellW - 1; cx++)
		{
			int a = CellRegion[cz * CellW + cx];
			int b = CellRegion[cz * CellW + cx + 1];
			if (a == b) continue;
			if (!Regions[a].Neighbours.Contains(b)) Regions[a].Neighbours.Add(b);
			if (!Regions[b].Neighbours.Contains(a)) Regions[b].Neighbours.Add(a);
		}
	}

	private void BuildCellElevation()
	{
		var scratch = new List<Region>(96);
		for (int cz = 0; cz < CellW; cz++)
		for (int cx = 0; cx < CellW; cx++)
		{
			float wx = (cx + 0.5f) * Cell, wz = (cz + 0.5f) * Cell;
			float num = 0f, den = 0f;
			foreach (var r in Nearby(wx, wz, 2, scratch))
			{
				float dx = r.Cx - wx, dz = r.Cz - wz;
				float d2 = MathF.Max(dx * dx + dz * dz, 1f);
				float w = 1f / (d2 * d2);
				num += r.Elevation * w;
				den += w;
			}
			CellElevation[cz * CellW + cx] = den > 0f ? num / den : 0.34f;
		}
	}

	public float ElevationAt(float x, float z)
	{
		if (AtlasGuide != null) return AtlasGuide.ElevationAt(x, z);
		float fx = Rng.Clamp(x / Cell - 0.5f, 0f, CellW - 1.001f);
		float fz = Rng.Clamp(z / Cell - 0.5f, 0f, CellW - 1.001f);
		int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
		float tx = fx - x0, tz = fz - z0;
		float a = CellElevation[z0 * CellW + x0];
		float b = CellElevation[z0 * CellW + x0 + 1];
		float c = CellElevation[(z0 + 1) * CellW + x0];
		float d = CellElevation[(z0 + 1) * CellW + x0 + 1];
		return Rng.Lerp(Rng.Lerp(a, b, tx), Rng.Lerp(c, d, tx), tz);
	}

	public Region RegionAt(float x, float z)
	{
		if (AtlasGuide != null)
		{
			Biome biome = AtlasGuide.BiomeAt(x, z);
			return _atlasBiomeRegions[(int)biome];
		}
		int cx = Rng.ClampI((int)MathF.Floor(x / Cell), 0, CellW - 1);
		int cz = Rng.ClampI((int)MathF.Floor(z / Cell), 0, CellW - 1);
		return Regions[CellRegion[cz * CellW + cx]];
	}

	/// <summary>Directional distance from the authored centre to the map boundary.</summary>
	public float RimRadius(float x, float z)
	{
		float cx = Definition.Boundary.Centre.X * Size;
		float cz = Definition.Boundary.Centre.Z * Size;
		float dx = x - cx, dz = z - cz;
		float len = MathF.Sqrt(dx * dx + dz * dz);
		if (len < 0.0001f) return IslandR;
		float ux = dx / len, uz = dz / len;
		float rx = Definition.Boundary.RadiusX * Size;
		float rz = Definition.Boundary.RadiusZ * Size;
		float radius = 1f / MathF.Sqrt(ux * ux / (rx * rx) + uz * uz / (rz * rz));
		return radius + _nRim.Fbm(x * 0.011f, z * 0.011f, 3)
			* Definition.Boundary.Noise * Size;
	}

	/* ----------------------------------------------------------------
	 * Routed water — derived from the reference's flow language, but owned by
	 * this map plan rather than constrained to its exact coordinates.
	 * ---------------------------------------------------------------- */
	private List<(float x, float z)> TraceRiver(float sx, float sz,
		List<(float x, float z)> join = null, float joinRadius = 7f, bool spring = true)
	{
		const float step = 3f;
		float joinR2 = joinRadius * joinRadius;
		var points = new List<(float, float)> { (sx, sz) };
		float x = sx, z = sz;
		float centreX = Definition.Boundary.Centre.X * Size;
		float centreZ = Definition.Boundary.Centre.Z * Size;
		float hx = x - centreX, hz = z - centreZ;
		float h0 = MathF.Sqrt(hx * hx + hz * hz);
		if (h0 == 0f) h0 = 1f;
		hx /= h0; hz /= h0;

		for (int iteration = 0; iteration < 600; iteration++)
		{
			if (Definition.BoundaryDistance(x / Size, z / Size) > 0.99f) break;
			if (x < 3f || z < 3f || x > Size - 4f || z > Size - 4f) break;

			if (join != null)
			{
				foreach (var p in join)
				{
					float dx = p.x - x, dz = p.z - z;
					if (dx * dx + dz * dz >= joinR2) continue;
					points.Add(p);
					return TrimSpring(points, spring);
				}
			}

			float baseAngle = MathF.Atan2(hz, hx);
			float bx = hx, bz = hz, best = float.MaxValue;
			for (int k = -4; k <= 4; k++)
			{
				float angle = baseAngle + k * 0.22f;
				float dx = MathF.Cos(angle), dz = MathF.Sin(angle);
				float nx = x + dx * step, nz = z + dz * step;
				float e = ElevationAt(nx, nz)
				        + _nWander.Fbm(GlobalX(nx) * 0.020f, GlobalZ(nz) * 0.020f, 2) * 0.030f
				        + Math.Abs(k) * 0.004f;
				if (e < best) { best = e; bx = dx; bz = dz; }
			}
			hx = hx * 0.55f + bx * 0.45f;
			hz = hz * 0.55f + bz * 0.45f;
			float length = MathF.Sqrt(hx * hx + hz * hz);
			if (length == 0f) length = 1f;
			hx /= length; hz /= length;
			x += hx * step; z += hz * step;
			points.Add((x, z));
		}
		return TrimSpring(points, spring);
	}

	private static List<(float x, float z)> TrimSpring(List<(float x, float z)> points, bool spring)
	{
		int skip = spring ? (int)MathF.Floor(points.Count * 0.14f + 0.5f) : 0;
		if (points.Count - skip < 8 || skip == 0) return points;
		return points.GetRange(skip, points.Count - skip);
	}

	private void RouteWater(Rng rng)
	{
		var inland = Regions.FindAll(r =>
			Definition.BoundaryDistance(r.Cx / Size, r.Cz / Size) < 0.74f);
		var pool = inland.Count > 0 ? inland : Regions;
		var peaks = new List<Region>(pool);
		peaks.Sort((a, b) => b.Elevation.CompareTo(a.Elevation));

		int lakeCount = Definition.Lakes.Count
			+ Math.Max(0, Definition.NaturalInfill.AdditionalMajorLakes);

		// Fixed Chapter geography goes in first. Procedural water may enrich it,
		// but can never replace or relocate it.
		foreach (var marker in Definition.Lakes)
		{
			float x = marker.Centre.X * Size, z = marker.Centre.Z * Size;
			var region = NearestRegion(x, z);
			Lakes.Add(new LakePlan
			{
				Region = region.Id,
				Cx = x,
				Cz = z,
				Radius = marker.Radius * Size,
			});
		}

		foreach (var marker in Definition.Waterways)
		{
			var points = new List<(float x, float z)>(marker.Points.Count);
			foreach (var point in marker.Points) points.Add((point.X * Size, point.Z * Size));
			var source = NearestRegion(points[0].x, points[0].z);
			Rivers.Add(new Channel
			{
				Order = marker.Order,
				Source = source.Id,
				Outflow = marker.Outflow,
				Tributary = marker.Tributary,
				Points = points,
			});
		}
		int authoredTrunks = Rivers.FindAll(r => r.Order == 0).Count;
		int trunkCount = authoredTrunks
			+ Math.Max(0, Definition.NaturalInfill.AdditionalRiverTrunks);

		var basins = new List<Region>(pool);
		basins.Sort((a, b) => a.Elevation.CompareTo(b.Elevation));
		foreach (var basin in basins)
		{
			if (Lakes.Count >= lakeCount) break;
			float radius = RegionSpacing * rng.Range(0.85f, 1.5f);
			bool overlaps = false;
			foreach (var lake in Lakes)
			{
				float dx = lake.Cx - basin.Cx, dz = lake.Cz - basin.Cz;
				if (MathF.Sqrt(dx * dx + dz * dz) < (lake.Radius + radius) * 1.9f) { overlaps = true; break; }
			}
			if (overlaps) continue;
			Lakes.Add(new LakePlan { Region = basin.Id, Cx = basin.Cx, Cz = basin.Cz, Radius = radius });
		}

		if (Lakes.Count > 0)
		{
			LakePlan largest = Lakes[0];
			foreach (var lake in Lakes) if (lake.Radius > largest.Radius) largest = lake;
			LakeRegion = Regions[largest.Region];
			LakeRadius = largest.Radius;
		}
		else
		{
			LakeRegion = Regions[0];
			LakeRadius = 0f;
		}

		int currentTrunks = authoredTrunks;
		foreach (var peak in peaks)
		{
			if (currentTrunks >= trunkCount) break;
			bool close = false;
			foreach (var river in Rivers)
			{
				var p = river.Points[0];
				float dx = p.x - peak.Cx, dz = p.z - peak.Cz;
				if (MathF.Sqrt(dx * dx + dz * dz) < RegionSpacing * 5f) { close = true; break; }
			}
			if (close) continue;
			Rivers.Add(new Channel
			{
				Order = 0, Source = peak.Id,
				Points = TraceRiver(peak.Cx, peak.Cz),
			});
			currentTrunks++;
		}

		foreach (var lake in Lakes)
		{
			bool reached = false;
			foreach (var river in Rivers)
			{
				foreach (var p in river.Points)
				{
					float dx = p.x - lake.Cx, dz = p.z - lake.Cz;
					if (MathF.Sqrt(dx * dx + dz * dz) < lake.Radius * 1.3f) { reached = true; break; }
				}
				if (reached) break;
			}
			if (reached) continue;
			Rivers.Add(new Channel
			{
				Order = 1, Source = lake.Region, Outflow = true,
				Points = TraceRiver(lake.Cx, lake.Cz, spring: false),
			});
		}

		var trunks = Rivers.FindAll(r => r.Order == 0);
		int tributaryTarget = Rivers.Count
			+ Math.Max(0, Definition.NaturalInfill.AdditionalTributaries);
		foreach (var peak in peaks)
		{
			if (Rivers.Count >= tributaryTarget) break;
			if (Rivers.Exists(r => r.Source == peak.Id)) continue;
			if (trunks.Count == 0) break;
			var host = trunks[Rivers.Count % trunks.Count];
			var points = TraceRiver(peak.Cx, peak.Cz, host.Points);
			if (points.Count < 10) continue;
			Rivers.Add(new Channel
			{
				Order = 1, Source = peak.Id, Tributary = true, Points = points,
			});
		}
	}

	private Region NearestRegion(float x, float z)
	{
		Region nearest = Regions[0];
		float best = float.MaxValue;
		foreach (var region in Regions)
		{
			float dx = region.Cx - x, dz = region.Cz - z;
			float d = dx * dx + dz * dz;
			if (d >= best) continue;
			best = d;
			nearest = region;
		}
		return nearest;
	}
}
