using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Petalfell.World.Sites;

namespace Petalfell.World;

/// <summary>
/// Materialises one bounded piece of the full production continent with the
/// proven Terrain generator. The accepted atlas is only its macro planner;
/// shelves, broken terrace edges, banks, submerged beds and column materials
/// remain the original low-level system. This is the canonical normal-play
/// terrain and site foundation, not a temporary review implementation.
/// </summary>
public static class ProductionTerrainWindow
{
	public static AtlasPreparedWindow Build(MapDefinition map, int worldSeed,
		AtlasMosaicBounds bounds, Action<string> warning = null)
	{
		if (map?.CanonicalAtlas == null)
			throw new ArgumentNullException(nameof(map), "production map has no atlas");
		WorldAtlasDefinition atlas = map.CanonicalAtlas;
		int span = bounds.Span;
		if (bounds.MaxSectorZ - bounds.MinSectorZ + 1 != span)
			throw new InvalidOperationException($"terrain window {bounds} is not square");
		int size = span * atlas.SectorSize;
		int originX = bounds.MinSectorX * atlas.SectorSize;
		int originZ = bounds.MinSectorZ * atlas.SectorSize;
		var watch = Stopwatch.StartNew();

		ProductionTerrainGuide guide = ProductionTerrainGuide.CreateAtOrigin(
			atlas, size, originX, originZ, worldSeed);
		var planner = new Planner(worldSeed, size, map, guide);
		long planMs = watch.ElapsedMilliseconds;
		var terrain = new Terrain(worldSeed, size, planner, terrainOnly: true);
		long terrainMs = watch.ElapsedMilliseconds - planMs;

		AtlasSectorData data = Describe(terrain, atlas, worldSeed, bounds);
		var window = new AtlasSectorWindow(data, atlas, worldSeed, terrain.Grid);
		var siteBuilds = new List<AtlasReferenceSiteBuild>();
		BuildRuntimeSites(terrain, window, atlas, siteBuilds, warning);
		long siteMs = watch.ElapsedMilliseconds - planMs - terrainMs;
		Vegetation.Populate(terrain, worldSeed);
		long floraMs = watch.ElapsedMilliseconds - planMs - terrainMs - siteMs;

		GD.Print($"[production-terrain] full-atlas window {bounds} " +
		         $"({originX},{originZ}+{size}) plan {planMs}ms terrain {terrainMs}ms " +
		         $"sites {siteMs}ms flora {floraMs}ms; {terrain.Timings}");
		GD.Print($"[production-terrain] block grammar: {Vegetation.LastTreeCount} trees, " +
		         $"{siteBuilds.Count} production site(s)");
		GD.Print($"[production-hydrology] derived river-side bank cells " +
		         $"+{terrain.ProductionRiverBankPositive}/-{terrain.ProductionRiverBankNegative}");
		return new AtlasPreparedWindow(window, bounds, default, siteBuilds,
			terrain.NaturalFormations);
	}

	private static AtlasSectorData Describe(Terrain terrain, WorldAtlasDefinition atlas,
		int worldSeed, AtlasMosaicBounds bounds)
	{
		int size = terrain.Size;
		var data = new AtlasSectorData(bounds.MinSectorX, bounds.MinSectorZ,
			terrain.Grid.OriginX, terrain.Grid.OriginZ, size, 0, size, size,
			terrain.Grid.Height, Terrain.Sea, $"production-terrain-{worldSeed}");
		int[] profileByBiome = BuildProfileLookup(atlas);

		for (int z = 0; z < size; z++)
		for (int x = 0; x < size; x++)
		{
			int i = z * size + x;
			int height = terrain.Level[i];
			bool water = terrain.Land[i] == 0;
			data.Height[i] = (ushort)Math.Clamp(height, 1, ushort.MaxValue);
			data.WaterSurface[i] = water ? (ushort)Terrain.Sea : (ushort)0;
			data.Land[i] = water ? (byte)0 : (byte)1;
			data.Water[i] = water ? (byte)255 : (byte)0;
			Biome biome = terrain.Plan.AtlasGuide.BiomeAt(x, z);
			byte profile = (byte)profileByBiome[(int)biome];
			data.Profile[i] = profile;
			data.SecondaryProfile[i] = profile;
			data.ProfileBlend[i] = 0;

			int left = terrain.Level[z * size + Math.Max(0, x - 1)];
			int right = terrain.Level[z * size + Math.Min(size - 1, x + 1)];
			int north = terrain.Level[Math.Max(0, z - 1) * size + x];
			int south = terrain.Level[Math.Min(size - 1, z + 1) * size + x];
			int maxDelta = Math.Max(Math.Max(Math.Abs(height - left), Math.Abs(height - right)),
				Math.Max(Math.Abs(height - north), Math.Abs(height - south)));
			data.Slope[i] = (byte)Math.Clamp(maxDelta, 0, 255);
			int dx = left - right;
			int dz = north - south;
			float angle = dx == 0 && dz == 0 ? 0f : MathF.Atan2(dz, dx);
			data.Aspect[i] = (byte)Math.Clamp(
				(int)MathF.Round((angle + MathF.PI) / MathF.Tau * 255f), 0, 255);
			data.Curvature[i] = (byte)Math.Clamp(
				128 + (left + right + north + south - height * 4) * 4, 0, 255);
			bool bank = !water && NeighboursWater(terrain, x, z);
			// Water is a distinct runtime ownership class. The direct terrain path
			// previously left these cells as generic wet ground; rendering happened to
			// use WaterSurface, but collision/map/runtime audits then observed mutually
			// inconsistent water records.
			data.Hydrology[i] = water ? (byte)3 : bank ? (byte)2 :
				terrain.Wet[i] != 0 ? (byte)1 : (byte)0;
			data.Wetness[i] = water ? (byte)255 : bank ? (byte)224 :
				terrain.Wet[i] != 0 ? (byte)160 : (byte)0;
			data.Surface[i] = water ? (byte)AtlasTerrainSurface.Underwater
				: maxDelta >= Terrain.Step * 2 ? (byte)AtlasTerrainSurface.Cliff
				: bank && height <= Terrain.Sea + Terrain.Step
					? (byte)AtlasTerrainSurface.Shore
					: (byte)AtlasTerrainSurface.Cap;
		}
		return data;
	}

	private static bool NeighboursWater(Terrain terrain, int x, int z)
	{
		int size = terrain.Size;
		if (x > 0 && terrain.Land[z * size + x - 1] == 0) return true;
		if (x + 1 < size && terrain.Land[z * size + x + 1] == 0) return true;
		if (z > 0 && terrain.Land[(z - 1) * size + x] == 0) return true;
		return z + 1 < size && terrain.Land[(z + 1) * size + x] == 0;
	}

	private static int[] BuildProfileLookup(WorldAtlasDefinition atlas)
	{
		var result = new int[Enum.GetValues<Biome>().Length];
		for (int biome = 0; biome < result.Length; biome++)
		{
			string name = ((Biome)biome).ToString();
			int index = atlas.BiomeCatalog.Profiles.FindIndex(profile =>
				profile.RuntimeBiomes.Contains(name, StringComparer.Ordinal));
			result[biome] = Math.Max(0, index);
		}
		return result;
	}

	private static void BuildRuntimeSites(Terrain terrain, AtlasSectorWindow window,
		WorldAtlasDefinition atlas, List<AtlasReferenceSiteBuild> builds,
		Action<string> warning)
	{
		if (atlas.Topology == null) return;
		foreach (CanonicalSite canonical in atlas.Topology.Sites)
		{
			ReferenceSiteDefinition site = canonical.ReferencePlan;
			if (!canonical.RunsInProduction || site == null ||
			    !FootprintIntersects(site, window.Data)) continue;
			if (!FootprintFits(site, window.Data))
			{
				warning?.Invoke($"production site '{site.SiteId}' crosses terrain-window {window.Data.OriginX}," +
				                $"{window.Data.OriginZ}+{window.Data.CoreSize}; it remains reserved but unbuilt");
				continue;
			}

			int localX = site.Origin.X - window.Data.OriginX;
			int localZ = site.Origin.Z - window.Data.OriginZ;
			int naturalTop = terrain.Grid.Top[localZ * terrain.Size + localX];
			ReferenceSiteGroundPlan groundPlan = ReferenceSiteGroundPlan.Load(site);
			ReferenceGroundPlanTerrain datum = groundPlan.Terrain.FirstOrDefault(shape =>
				shape.WriteMode == "preserve-atlas" && shape.SurfaceY.HasValue);
			if (datum?.SurfaceY == null)
				throw new InvalidOperationException($"site '{site.SiteId}' has no natural terrain datum");
			int verticalOffset = naturalTop - datum.SurfaceY.Value;
			ReferenceSiteStatistics statistics = ReferenceSiteBuilder.Build(window, site,
				verticalOffset);
			terrain.SyncAuthoredTerrain();
			builds.Add(new AtlasReferenceSiteBuild(site.SiteId, statistics));
			GD.Print($"[production-site] {site.SiteId} offsetY {verticalOffset} " +
			         $"surface {statistics.SurfaceCells} voxels {statistics.Voxels}");
		}
	}

	private static bool FootprintIntersects(ReferenceSiteDefinition site,
		AtlasSectorData data)
	{
		(int minX, int minZ, int maxX, int maxZ) = Footprint(site);
		return maxX >= data.OriginX && maxZ >= data.OriginZ &&
		       minX < data.OriginX + data.CoreSize && minZ < data.OriginZ + data.CoreSize;
	}

	private static bool FootprintFits(ReferenceSiteDefinition site, AtlasSectorData data)
	{
		(int minX, int minZ, int maxX, int maxZ) = Footprint(site);
		return minX >= data.OriginX && minZ >= data.OriginZ &&
		       maxX < data.OriginX + data.CoreSize && maxZ < data.OriginZ + data.CoreSize;
	}

	private static (int minX, int minZ, int maxX, int maxZ) Footprint(
		ReferenceSiteDefinition site)
	{
		PlanPoint min = site.RuntimeFootprintMin;
		PlanPoint max = site.RuntimeFootprintMax;
		BlockPoint[] corners =
		{
			site.ToGlobalRuntime(new PlanPoint { X = min.X, Z = min.Z }),
			site.ToGlobalRuntime(new PlanPoint { X = max.X, Z = min.Z }),
			site.ToGlobalRuntime(new PlanPoint { X = min.X, Z = max.Z }),
			site.ToGlobalRuntime(new PlanPoint { X = max.X, Z = max.Z }),
		};
		return (corners.Min(point => point.X), corners.Min(point => point.Z),
			corners.Max(point => point.X), corners.Max(point => point.Z));
	}
}
