using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

public enum Biome { Meadow, Sakura, Highland, Shore, Wetland }

public sealed class Region
{
	public int Id;
	public float Cx, Cz;
	public float Elevation, Temperature, Moisture, Magic;
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
/// The source world plan, transliterated from planner.js.
///
/// This stage decides places before terrain writes blocks: dart-thrown region
/// centres, authored macro fields, a warped region map, local elevation, lake
/// basins and routed channels. Keeping these equations and their RNG order in
/// sync with the reference matters more than using an engine-native noise or
/// navigation helper; changing the plan changes the world.
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
	public readonly int CellW;
	public readonly List<Region> Regions = new();
	public readonly int[] CellRegion;
	public readonly byte[] CellBiome;
	public readonly float[] CellElevation;
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

	public Planner(int seed, int size)
	{
		Size = size;
		CellW = (int)MathF.Ceiling(size / (float)Cell);
		CellRegion = new int[CellW * CellW];
		CellBiome = new byte[CellW * CellW];
		CellElevation = new float[CellW * CellW];
		IslandR = size * IslandFraction;

		var rng = new Rng(unchecked(seed ^ (int)0x9e3779b9u));
		var nTemp = new Noise2D(seed + 41);
		var nMoist = new Noise2D(seed + 42);
		var nMagic = new Noise2D(seed + 43);
		_nEdge = new Noise2D(seed + 44);
		_nWander = new Noise2D(seed + 45);
		_nRim = new Noise2D(seed + 7);

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

		BuildCellMap();
		BuildCellElevation();
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
		float half = Size * 0.5f;
		foreach (var r in Regions)
		{
			float rim = MathF.Sqrt((r.Cx - half) * (r.Cx - half) + (r.Cz - half) * (r.Cz - half)) / half;
			r.Elevation = Rng.Clamp(0.94f - rim * 0.70f +
				_nEdge.Fbm(r.Cx / FeatureElevation, r.Cz / FeatureElevation, 3) * 0.42f, 0f, 1f);
			r.Temperature = Rng.Clamp(0.5f +
				nTemp.Fbm(r.Cx / FeatureTemperature, r.Cz / FeatureTemperature, 3), 0f, 1f);
			r.Moisture = Rng.Clamp(0.5f +
				nMoist.Fbm(r.Cx / FeatureMoisture, r.Cz / FeatureMoisture, 3), 0f, 1f);
			r.Magic = Rng.Clamp(0.5f +
				nMagic.Fbm(r.Cx / FeatureMagic, r.Cz / FeatureMagic, 2), 0f, 1f);

			if (r.Elevation < 0.15f) r.Biome = Biome.Shore;
			else if (r.Elevation > 0.82f) r.Biome = Biome.Highland;
			else if (r.Moisture > 0.55f && r.Magic > 0.44f) r.Biome = Biome.Sakura;
			else if (r.Moisture > 0.62f) r.Biome = Biome.Wetland;
			else r.Biome = Biome.Meadow;

			r.Seed = unchecked((uint)(seed ^ (r.Id * unchecked((int)0x85ebca6bu))));
		}
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
			float qx = wx + _nEdge.Fbm(wx * 0.006f, wz * 0.006f, 3) * warp;
			float qz = wz + _nEdge.Fbm(wx * 0.006f + 19f, wz * 0.006f + 7f, 3) * warp;
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

		// Kept byte-for-byte in behaviour with the source: horizontal cell
		// boundaries are the adjacency skeleton used by the elevation blend.
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
		int cx = Rng.ClampI((int)MathF.Floor(x / Cell), 0, CellW - 1);
		int cz = Rng.ClampI((int)MathF.Floor(z / Cell), 0, CellW - 1);
		return Regions[CellRegion[cz * CellW + cx]];
	}

	public float RimRadius(float x, float z) => IslandR + _nRim.Fbm(x * 0.011f, z * 0.011f, 3) * 20f;

	/* ----------------------------------------------------------------
	 * Routed water — same ordering and RNG consumption as planner.js.
	 * ---------------------------------------------------------------- */
	private List<(float x, float z)> TraceRiver(float sx, float sz,
		List<(float x, float z)> join = null, float joinRadius = 7f, bool spring = true)
	{
		const float step = 3f;
		float joinR2 = joinRadius * joinRadius;
		var points = new List<(float, float)> { (sx, sz) };
		float x = sx, z = sz;
		float half = Size * 0.5f;
		float hx = x - half, hz = z - half;
		float h0 = MathF.Sqrt(hx * hx + hz * hz);
		if (h0 == 0f) h0 = 1f;
		hx /= h0; hz /= h0;

		for (int iteration = 0; iteration < 600; iteration++)
		{
			if (MathF.Sqrt((x - half) * (x - half) + (z - half) * (z - half)) > IslandR * 0.99f) break;
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
				        + _nWander.Fbm(nx * 0.020f, nz * 0.020f, 2) * 0.030f
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
		float half = Size * 0.5f;
		var inland = Regions.FindAll(r =>
		{
			float dx = r.Cx - half, dz = r.Cz - half;
			return MathF.Sqrt(dx * dx + dz * dz) < IslandR * 0.74f;
		});
		var pool = inland.Count > 0 ? inland : Regions;
		var peaks = new List<Region>(pool);
		peaks.Sort((a, b) => b.Elevation.CompareTo(a.Elevation));

		float areaScale = (Size / 256f) * (Size / 256f);
		int trunkCount = Rng.ClampI((int)MathF.Floor(1.5f * MathF.Sqrt(areaScale) + 0.5f), 1, 20);
		int lakeCount = Rng.ClampI((int)MathF.Floor(areaScale + 0.5f), 1, 48);

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

		LakePlan largest = Lakes[0];
		foreach (var lake in Lakes) if (lake.Radius > largest.Radius) largest = lake;
		LakeRegion = Regions[largest.Region];
		LakeRadius = largest.Radius;

		foreach (var peak in peaks)
		{
			if (Rivers.Count >= trunkCount) break;
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
		foreach (var peak in peaks)
		{
			if (Rivers.Count >= trunkCount * 3) break;
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
}
