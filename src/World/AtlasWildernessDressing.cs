using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Ordinary production-atlas wilderness. Candidates live on globally anchored
/// lattices and have independent seeds, so sector build order and unrelated
/// random draws cannot move a tree or boulder. Authored-site reclamation is a
/// separate, explicitly permitted pass.
/// </summary>
public static class AtlasWildernessDressing
{
	// Vegetation.Tree grows attached lobes rather than a fixed crown. Eighteen is
	// the conservative bound around every generated canopy, including an unlikely
	// chain of outward lobes; keeping that whole square outside a reference mask
	// prevents the old trunk-only check from leaking crowns into authored courts.
	private const int TreeFootprintRadius = 18;
	private const int TreeTerrainRadius = 3;
	public const int RequiredWindowHalo = TreeFootprintRadius + 2;

	public static AtlasWildernessDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, int worldSeed) =>
		ApplyCore(window, atlas, AtlasWildernessExclusion.None, worldSeed);

	public static AtlasWildernessDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, DomainPlanDefinition plan, int worldSeed) =>
		ApplyCore(window, atlas, new DomainPlanExclusion(plan), worldSeed);

	public static AtlasWildernessDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, ReferenceSiteDefinition site, int worldSeed) =>
		ApplyCore(window, atlas, new ReferenceSiteExclusion(site), worldSeed);

	public static AtlasWildernessDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, IReadOnlyList<ReferenceSiteDefinition> sites,
		int worldSeed) =>
		ApplyCore(window, atlas, sites == null || sites.Count == 0
			? AtlasWildernessExclusion.None
			: new ReferenceSitesExclusion(sites), worldSeed);

	private static AtlasWildernessDressingStatistics ApplyCore(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, AtlasWildernessExclusion exclusion, int worldSeed)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (atlas?.BiomeCatalog == null) throw new ArgumentNullException(nameof(atlas));
		var candidates = new List<Candidate>();
		CollectTrees(candidates, window, atlas, worldSeed);
		CollectBoulders(candidates, window, atlas, worldSeed);
		candidates.Sort(CandidateComparer.Instance);

		int trees = 0, boulders = 0, excluded = 0, unsuitable = 0, occupied = 0;
		ulong manifest = 1469598103934665603ul;
		foreach (Candidate candidate in candidates)
		{
			int x = candidate.GlobalX - window.Data.OriginX;
			int z = candidate.GlobalZ - window.Data.OriginZ;
			int footprint = candidate.Kind == CandidateKind.Tree
				? TreeFootprintRadius : candidate.Boulder.RadiusMax + 2;
			if (exclusion.Intersects(candidate.GlobalX - footprint,
				    candidate.GlobalZ - footprint, candidate.GlobalX + footprint,
				    candidate.GlobalZ + footprint))
			{
				excluded++;
				continue;
			}

			int radius = candidate.Kind == CandidateKind.Tree
				? TreeTerrainRadius : candidate.Boulder.RadiusMax + 1;
			int maxSlope = candidate.Kind == CandidateKind.Tree
				? candidate.Vegetation.MaxSlope : candidate.Boulder.MaxSlope;
			int minWetness = candidate.Kind == CandidateKind.Tree
				? candidate.Vegetation.MinWetness : candidate.Boulder.MinWetness;
			int maxWetness = candidate.Kind == CandidateKind.Tree
				? candidate.Vegetation.MaxWetness : candidate.Boulder.MaxWetness;
			Suitability result = Suitable(window, x, z, radius, maxSlope,
				minWetness, maxWetness, candidate.Kind == CandidateKind.Tree);
			if (result == Suitability.Occupied)
			{
				occupied++;
				continue;
			}
			if (result != Suitability.Accepted)
			{
				unsuitable++;
				continue;
			}

			int before = window.Grid.PlacedCount;
			if (candidate.Kind == CandidateKind.Tree)
				PlaceTree(window, candidate, x, z);
			else
				PlaceBoulder(window, candidate, x, z);
			if (window.Grid.PlacedCount == before)
			{
				unsuitable++;
				continue;
			}

			if (candidate.Kind == CandidateKind.Tree) trees++;
			else boulders++;
			manifest = Hash(manifest, (int)candidate.Kind);
			manifest = Hash(manifest, candidate.GlobalX);
			manifest = Hash(manifest, candidate.GlobalZ);
			manifest = Hash(manifest, candidate.ShapeSeed);
			manifest = Hash(manifest, Rng.StableHash(candidate.SetId));
		}

		return new AtlasWildernessDressingStatistics(candidates.Count, trees, boulders,
			excluded, unsuitable, occupied, manifest);
	}

	private static void CollectTrees(List<Candidate> target, AtlasSectorWindow window,
		WorldAtlasDefinition atlas, int worldSeed)
	{
		foreach (AtlasVegetationSet set in atlas.BiomeCatalog.VegetationSets)
		{
			var grove = new Noise2D(unchecked(worldSeed ^
				Rng.StableHash($"atlas:wilderness-grove:{set.Id}")));
			EnumerateCells(window.Data, set.CandidateSpacing, (cellX, cellZ) =>
			{
				int positionSeed = CandidateSeed(worldSeed,
					$"atlas:tree-position:{set.Id}", cellX, cellZ);
				var position = new Rng(positionSeed);
				int globalX = cellX * set.CandidateSpacing +
					position.RangeInt(2, set.CandidateSpacing - 3);
				int globalZ = cellZ * set.CandidateSpacing +
					position.RangeInt(2, set.CandidateSpacing - 3);
				if (!InsideSafeWindow(window.Data, globalX, globalZ)) return;
				int x = globalX - window.Data.OriginX, z = globalZ - window.Data.OriginZ;
				BiomeBuildProfile profile = window.BuildProfileAt(x, z);
				if (!string.Equals(profile.VegetationSetId, set.Id, StringComparison.Ordinal)) return;

				float field = grove.Fbm01(globalX / set.GroveWavelength,
					globalZ / set.GroveWavelength, 3);
				float chance = set.Density * (.18f + .82f * Rng.Smoothstep(.24f, .76f, field));
				var accept = new Rng(CandidateSeed(worldSeed,
					$"atlas:tree-accept:{set.Id}", cellX, cellZ));
				if (!accept.Chance(chance)) return;
				target.Add(Candidate.Tree(set, globalX, globalZ, cellX, cellZ,
					CandidateSeed(worldSeed, $"atlas:tree-shape:{set.Id}", cellX, cellZ)));
			});
		}
	}

	private static void CollectBoulders(List<Candidate> target, AtlasSectorWindow window,
		WorldAtlasDefinition atlas, int worldSeed)
	{
		foreach (AtlasBoulderSet set in atlas.BiomeCatalog.BoulderSets)
		{
			var clusters = new Noise2D(unchecked(worldSeed ^
				Rng.StableHash($"atlas:boulder-cluster:{set.Id}")));
			EnumerateCells(window.Data, set.CandidateSpacing, (cellX, cellZ) =>
			{
				int positionSeed = CandidateSeed(worldSeed,
					$"atlas:boulder-position:{set.Id}", cellX, cellZ);
				var position = new Rng(positionSeed);
				int globalX = cellX * set.CandidateSpacing +
					position.RangeInt(2, set.CandidateSpacing - 3);
				int globalZ = cellZ * set.CandidateSpacing +
					position.RangeInt(2, set.CandidateSpacing - 3);
				if (!InsideSafeWindow(window.Data, globalX, globalZ)) return;
				int x = globalX - window.Data.OriginX, z = globalZ - window.Data.OriginZ;
				BiomeBuildProfile profile = window.BuildProfileAt(x, z);
				if (!string.Equals(profile.BoulderSetId, set.Id, StringComparison.Ordinal)) return;

				float field = clusters.Fbm01(globalX / set.ClusterWavelength,
					globalZ / set.ClusterWavelength, 3);
				float chance = set.Density * (.15f + .85f * Rng.Smoothstep(.22f, .78f, field));
				var accept = new Rng(CandidateSeed(worldSeed,
					$"atlas:boulder-accept:{set.Id}", cellX, cellZ));
				if (!accept.Chance(chance)) return;
				target.Add(Candidate.BoulderPlan(set, globalX, globalZ, cellX, cellZ,
					CandidateSeed(worldSeed, $"atlas:boulder-shape:{set.Id}", cellX, cellZ)));
			});
		}
	}

	private static void EnumerateCells(AtlasSectorData data, int spacing,
		Action<int, int> visit)
	{
		int firstX = FloorDiv(data.OriginX - spacing, spacing);
		int firstZ = FloorDiv(data.OriginZ - spacing, spacing);
		int lastX = FloorDiv(data.OriginX + data.Width - 1 + spacing, spacing);
		int lastZ = FloorDiv(data.OriginZ + data.Depth - 1 + spacing, spacing);
		for (int cellZ = firstZ; cellZ <= lastZ; cellZ++)
		for (int cellX = firstX; cellX <= lastX; cellX++)
			visit(cellX, cellZ);
	}

	private static bool InsideSafeWindow(AtlasSectorData data, int globalX, int globalZ)
	{
		int x = globalX - data.OriginX, z = globalZ - data.OriginZ;
		// A candidate whose anchor is in this artifact is generated even when its
		// crown clips the disposable outer edge. Rejecting anchors by the full
		// canopy radius erased legitimate neighbours across the canonical core seam.
		// The verifier trims only influence from anchors outside both windows.
		return x >= 0 && z >= 0 && x < data.Width && z < data.Depth;
	}

	private static Suitability Suitable(AtlasSectorWindow window, int x, int z,
		int radius, int maxSlope, int minWetness, int maxWetness, bool tree)
	{
		AtlasSectorData data = window.Data;
		VoxelGrid grid = window.Grid;
		int centreIndex = z * data.Width + x;
		if (data.Land[centreIndex] == 0 || data.WaterSurface[centreIndex] > 0 ||
		    data.Surface[centreIndex] != (byte)AtlasTerrainSurface.Cap ||
		    data.Slope[centreIndex] > maxSlope || data.Wetness[centreIndex] < minWetness ||
		    data.Wetness[centreIndex] > maxWetness)
			return Suitability.Terrain;
		int centreHeight = data.Height[centreIndex];
		for (int dz = -radius; dz <= radius; dz++)
		for (int dx = -radius; dx <= radius; dx++)
		{
			int xx = x + dx, zz = z + dz;
			if (xx < 0 || zz < 0 || xx >= data.Width || zz >= data.Depth) continue;
			int index = zz * data.Width + xx;
			if (grid.Heights[index] != grid.Top[index]) return Suitability.Occupied;
			if (data.Land[index] == 0 || data.WaterSurface[index] > 0 ||
			    data.Surface[index] != (byte)AtlasTerrainSurface.Cap ||
			    Math.Abs(data.Height[index] - centreHeight) > (tree ? 2 : 3))
				return Suitability.Terrain;
		}
		int ground = grid.Top[centreIndex];
		if (ground < 1 || ground + 18 >= grid.Height) return Suitability.Terrain;
		byte cap = grid.At(x, ground - 1, z);
		if (tree && !TreeSurface(cap)) return Suitability.Terrain;
		if (!tree && cap is Palette.PAVING or Palette.PATH) return Suitability.Terrain;
		return Suitability.Accepted;
	}

	private static bool TreeSurface(byte surface) => Palette.IsGrassSurface(surface) ||
		surface is Palette.MOSS or Palette.BLOSSOM_DRIFT or Palette.SNOW;

	private static void PlaceTree(AtlasSectorWindow window, Candidate candidate, int x, int z)
	{
		var rng = new Rng(candidate.ShapeSeed);
		float scale = rng.Range(candidate.Vegetation.ScaleMin, candidate.Vegetation.ScaleMax);
		var hue = new Noise2D(unchecked(candidate.ShapeSeed ^ Rng.StableHash("atlas:tree-hue")));
		float tone = hue.Fbm01(candidate.GlobalX / 46f, candidate.GlobalZ / 46f, 2);
		int paletteIndex = Math.Min(candidate.Vegetation.CanopyPalette.Count - 1,
			(int)(tone * candidate.Vegetation.CanopyPalette.Count));
		byte leaf = AtlasWildernessPalette.ResolveCanopy(
			candidate.Vegetation.CanopyPalette[paletteIndex]);
		int ground = window.Grid.Top[z * window.Data.Width + x];
		Vegetation.Tree(window.Grid, rng, x, ground, z, scale, leaf);
	}

	private static void PlaceBoulder(AtlasSectorWindow window, Candidate candidate, int x, int z)
	{
		AtlasBoulderSet set = candidate.Boulder;
		var rng = new Rng(candidate.ShapeSeed);
		int radiusX = rng.RangeInt(set.RadiusMin, set.RadiusMax);
		int radiusZ = Math.Max(1, radiusX - rng.RangeInt(0, 1));
		int height = rng.RangeInt(set.HeightMin, set.HeightMax);
		byte stone = AtlasWildernessPalette.ResolveStone(
			set.StonePalette[rng.RangeInt(0, set.StonePalette.Count - 1)]);
		float skewX = rng.Range(-.35f, .35f), skewZ = rng.Range(-.35f, .35f);
		for (int dz = -radiusZ; dz <= radiusZ; dz++)
		for (int dx = -radiusX; dx <= radiusX; dx++)
		{
			float nx = (dx + skewX) / (radiusX + .3f);
			float nz = (dz + skewZ) / (radiusZ + .3f);
			float distance = nx * nx + nz * nz;
			if (distance > 1f) continue;
			var cellRng = new Rng(CandidateSeed(candidate.ShapeSeed,
				"atlas:boulder-cell", dx, dz));
			int rise = Math.Max(1, (int)MathF.Round(height * (1f - distance * .64f) +
				cellRng.Range(-.35f, .35f)));
			int xx = x + dx, zz = z + dz;
			if (xx < 0 || zz < 0 || xx >= window.Data.Width || zz >= window.Data.Depth)
				continue;
			int ground = window.Grid.Top[zz * window.Data.Width + xx];
			window.Grid.Column(xx, zz, ground, ground + rise, stone);
		}
	}

	private static int CandidateSeed(int worldSeed, string salt, int cellX, int cellZ) =>
		unchecked(worldSeed ^ Rng.StableHash(salt) ^ cellX * 73856093 ^ cellZ * 19349663);

	private static ulong Hash(ulong value, int component)
	{
		unchecked
		{
			value ^= (uint)component;
			return value * 1099511628211ul;
		}
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
	}

	private enum CandidateKind { Tree, Boulder }
	private enum Suitability { Accepted, Terrain, Occupied }

	private sealed class Candidate
	{
		public CandidateKind Kind { get; private init; }
		public string SetId { get; private init; }
		public int GlobalX { get; private init; }
		public int GlobalZ { get; private init; }
		public int CellX { get; private init; }
		public int CellZ { get; private init; }
		public int ShapeSeed { get; private init; }
		public AtlasVegetationSet Vegetation { get; private init; }
		public AtlasBoulderSet Boulder { get; private init; }

		public static Candidate Tree(AtlasVegetationSet set, int globalX, int globalZ,
			int cellX, int cellZ, int shapeSeed) => new()
		{
			Kind = CandidateKind.Tree, SetId = set.Id, GlobalX = globalX, GlobalZ = globalZ,
			CellX = cellX, CellZ = cellZ, ShapeSeed = shapeSeed, Vegetation = set,
		};

		public static Candidate BoulderPlan(AtlasBoulderSet set, int globalX, int globalZ,
			int cellX, int cellZ, int shapeSeed) => new()
		{
			Kind = CandidateKind.Boulder, SetId = set.Id, GlobalX = globalX, GlobalZ = globalZ,
			CellX = cellX, CellZ = cellZ, ShapeSeed = shapeSeed, Boulder = set,
		};
	}

	private sealed class CandidateComparer : IComparer<Candidate>
	{
		public static readonly CandidateComparer Instance = new();
		public int Compare(Candidate a, Candidate b)
		{
			int result = a.Kind.CompareTo(b.Kind);
			if (result != 0) return result;
			result = StringComparer.Ordinal.Compare(a.SetId, b.SetId);
			if (result != 0) return result;
			result = a.CellZ.CompareTo(b.CellZ);
			return result != 0 ? result : a.CellX.CompareTo(b.CellX);
		}
	}

	private abstract class AtlasWildernessExclusion
	{
		public static readonly AtlasWildernessExclusion None = new EmptyExclusion();
		protected abstract bool Contains(int globalX, int globalZ);

		public bool Intersects(int minX, int minZ, int maxX, int maxZ)
		{
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
				if (Contains(x, z)) return true;
			return false;
		}
	}

	private sealed class EmptyExclusion : AtlasWildernessExclusion
	{
		protected override bool Contains(int globalX, int globalZ) => false;
	}

	private sealed class ReferenceSiteExclusion : AtlasWildernessExclusion
	{
		private readonly ReferenceSiteDefinition _site;
		public ReferenceSiteExclusion(ReferenceSiteDefinition site) =>
			_site = site ?? throw new ArgumentNullException(nameof(site));
		protected override bool Contains(int globalX, int globalZ) =>
			_site.ContainsGlobal(globalX, globalZ);
	}

	private sealed class ReferenceSitesExclusion : AtlasWildernessExclusion
	{
		private readonly IReadOnlyList<ReferenceSiteDefinition> _sites;
		public ReferenceSitesExclusion(IReadOnlyList<ReferenceSiteDefinition> sites) =>
			_sites = sites;
		protected override bool Contains(int globalX, int globalZ)
		{
			foreach (ReferenceSiteDefinition site in _sites)
				if (site.ContainsGlobal(globalX, globalZ)) return true;
			return false;
		}
	}

	private sealed class DomainPlanExclusion : AtlasWildernessExclusion
	{
		private readonly DomainPlanDefinition _plan;
		private readonly float _cos;
		private readonly float _sin;

		public DomainPlanExclusion(DomainPlanDefinition plan)
		{
			_plan = plan ?? throw new ArgumentNullException(nameof(plan));
			float radians = plan.AxisDegrees * MathF.PI / 180f;
			_cos = MathF.Cos(radians);
			_sin = MathF.Sin(radians);
		}

		protected override bool Contains(int globalX, int globalZ)
		{
			float dx = globalX - _plan.Origin.X, dz = globalZ - _plan.Origin.Z;
			float x = dx * _cos - dz * _sin;
			float z = dx * _sin + dz * _cos;
			foreach (PlanPlatform platform in _plan.Platforms)
				if (Inside(x, z, platform.Polygon)) return true;
			foreach (PlanStair stair in _plan.Stairs)
				if (DistanceSquared(x, z, stair.From, stair.To) <=
				    MathF.Pow(stair.Width * .5f + 3f, 2f)) return true;
			foreach (PlanWall wall in _plan.Walls)
			for (int i = 1; i < wall.Points.Count; i++)
				if (DistanceSquared(x, z, wall.Points[i - 1], wall.Points[i]) <= 16f)
					return true;
			foreach (PlanLandmark landmark in _plan.Landmarks)
			{
				float radius = Math.Max(4f, Math.Max(landmark.Span, landmark.Count * 3) * .5f + 2f);
				float lx = x - landmark.Point.X, lz = z - landmark.Point.Z;
				if (lx * lx + lz * lz <= radius * radius) return true;
			}
			return false;
		}

		private static float DistanceSquared(float x, float z, PlanPoint a, PlanPoint b)
		{
			float vx = b.X - a.X, vz = b.Z - a.Z;
			float length = vx * vx + vz * vz;
			float t = length <= .001f ? 0f :
				Math.Clamp(((x - a.X) * vx + (z - a.Z) * vz) / length, 0f, 1f);
			float dx = x - (a.X + vx * t), dz = z - (a.Z + vz * t);
			return dx * dx + dz * dz;
		}

		private static bool Inside(float x, float z, IReadOnlyList<PlanPoint> polygon)
		{
			bool inside = false;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				PlanPoint a = polygon[i], b = polygon[j];
				bool crosses = (a.Z > z) != (b.Z > z) &&
				               x < (b.X - a.X) * (z - a.Z) / (float)(b.Z - a.Z) + a.X;
				if (crosses) inside = !inside;
			}
			return inside;
		}
	}
}

public readonly record struct AtlasWildernessDressingStatistics(int Candidates,
	int Trees, int Boulders, int Excluded, int Unsuitable, int Occupied, ulong ManifestHash);

/// <summary>Stable authored names used by biome JSON, resolved only at runtime.</summary>
public static class AtlasWildernessPalette
{
	public static bool IsCanopy(string id) => id is "leaf-pink" or "leaf-blush" or
		"leaf-lilac" or "leaf-cream" or "leaf-mint" or "leaf-rose";

	public static bool IsStone(string id) => id is "stone" or "stone-pale" or
		"stone-warm" or "moss-stone" or "scree";

	public static byte ResolveCanopy(string id) => id switch
	{
		"leaf-pink" => Palette.LEAF_PINK,
		"leaf-blush" => Palette.LEAF_BLUSH,
		"leaf-lilac" => Palette.LEAF_LILAC,
		"leaf-cream" => Palette.LEAF_CREAM,
		"leaf-mint" => Palette.LEAF_MINT,
		"leaf-rose" => Palette.LEAF_ROSE,
		_ => throw new InvalidOperationException($"unknown wilderness canopy palette '{id}'"),
	};

	public static byte ResolveStone(string id) => id switch
	{
		"stone" => Palette.STONE,
		"stone-pale" => Palette.STONE_PALE,
		"stone-warm" => Palette.STONE_WARM,
		"moss-stone" => Palette.MOSS_STONE,
		"scree" => Palette.SCREE,
		_ => throw new InvalidOperationException($"unknown wilderness stone palette '{id}'"),
	};
}
