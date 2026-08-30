using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Preliminary deterministic wilderness dressing for a production domain
/// review. Biome contracts choose the species family, a global wavelength field
/// chooses grove density, and a globally anchored lattice chooses individual
/// trees. The planned wilderness paint will later modulate this density without
/// changing the algorithm or moving authored structures.
/// </summary>
public static class AtlasDomainDressing
{
	private const int Cell = 16;

	public static AtlasDomainDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, DomainPlanDefinition plan, int worldSeed)
		=> ApplyCore(window, atlas, plan, null, worldSeed);

	public static AtlasDomainDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, ReferenceSiteDefinition site, int worldSeed)
		=> ApplyCore(window, atlas, null, site, worldSeed);

	private static AtlasDomainDressingStatistics ApplyCore(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, DomainPlanDefinition plan,
		ReferenceSiteDefinition site, int worldSeed)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (atlas?.BiomeCatalog == null) throw new ArgumentNullException(nameof(atlas));
		if (plan == null && site == null) throw new ArgumentNullException(nameof(plan));
		AtlasSectorData data = window.Data;
		VoxelGrid grid = window.Grid;
		var grove = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:wilderness-grove")));
		var hue = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:wilderness-hue")));
		int trees = 0, candidates = 0;

		int firstCellX = FloorDiv(data.OriginX + 4, Cell);
		int firstCellZ = FloorDiv(data.OriginZ + 4, Cell);
		int lastCellX = FloorDiv(data.OriginX + data.Width - 5, Cell);
		int lastCellZ = FloorDiv(data.OriginZ + data.Depth - 5, Cell);
		for (int cellZ = firstCellZ; cellZ <= lastCellZ; cellZ++)
		for (int cellX = firstCellX; cellX <= lastCellX; cellX++)
		{
			var rng = new Rng(unchecked(worldSeed ^ Rng.StableHash("atlas:tree") ^
				cellX * 73856093 ^ cellZ * 19349663));
			int globalX = cellX * Cell + rng.RangeInt(2, Cell - 3);
			int globalZ = cellZ * Cell + rng.RangeInt(2, Cell - 3);
			if (site?.ContainsGlobal(globalX, globalZ) == true) continue;
			int x = globalX - data.OriginX, z = globalZ - data.OriginZ;
			if (x < 4 || z < 4 || x >= data.Width - 4 || z >= data.Depth - 4) continue;
			candidates++;
			int index = z * data.Width + x;
			if (data.Land[index] == 0 || data.WaterSurface[index] > 0) continue;
			float reclamation = plan == null ? 0f : ReclamationAt(plan, globalX, globalZ);
			// Any raised or cut column belongs to the authored L3 composition. A
			// named cutout may explicitly invite growth back; the wilderness pass is
			// not allowed to make that story decision on its own.
			if (grid.Top[index] != data.Height[index] && reclamation <= 0f) continue;
			int ground = grid.Top[index];
			byte surface = grid.At(x, ground - 1, z);
			bool plantable = Palette.IsGrassSurface(surface) || surface == Palette.MOSS ||
			                 surface == Palette.BLOSSOM_DRIFT;
			bool reclaimedStone = reclamation > 0f && surface is Palette.SAND or Palette.MUD or
				Palette.MOSS_STONE or Palette.STONE_WARM;
			if (!plantable && !reclaimedStone) continue;
			if (!LocallyLevel(grid, x, z) ||
			    NearPlacedStructureOrRoad(grid, x, z, reclamation > 0f ? 2 : 5,
				    allowRuinedStone: reclamation > 0f)) continue;

			int profileIndex = data.Profile[index];
			if (data.ProfileBlend[index] > 0 &&
			    rng.Next() < data.ProfileBlend[index] / 255f)
				profileIndex = data.SecondaryProfile[index];
			BiomeBuildProfile profile = atlas.BiomeCatalog.Profiles[profileIndex];
			FloraSpec flora = For(profile.VegetationSetId);
			float density = grove.Fbm01(globalX / 168f, globalZ / 168f, 3);
			float chance = Math.Max(0f, density - .35f) * flora.Density;
			if (reclamation > 0f)
				chance = Math.Max(chance, reclamation * (.24f + density * .46f));
			if (!rng.Chance(chance)) continue;

			float scale = rng.Range(flora.ScaleLo, flora.ScaleHi);
			float tone = hue.Fbm01(globalX / 46f + 17f, globalZ / 46f - 31f, 2);
			byte leaf = flora.Leaves[Math.Min(flora.Leaves.Length - 1,
				(int)(tone * flora.Leaves.Length))];
			Vegetation.Tree(grid, rng, x, ground, z, scale, leaf);
			trees++;
		}
		return new AtlasDomainDressingStatistics(candidates, trees);
	}

	private readonly record struct FloraSpec(float Density, float ScaleLo, float ScaleHi,
		byte[] Leaves);

	private static FloraSpec For(string id) => id switch
	{
		"alpine-sparse" => new(.10f, .16f, .46f,
			new[] { Palette.LEAF_MINT, Palette.LEAF_LILAC }),
		"highland-thin" => new(.20f, .18f, .58f,
			new[] { Palette.LEAF_LILAC, Palette.LEAF_MINT }),
		"scarp-sparse" => new(.08f, .16f, .42f,
			new[] { Palette.LEAF_CREAM, Palette.LEAF_LILAC }),
		"meadow-river-mix" => new(.48f, .24f, .86f,
			new[] { Palette.LEAF_CREAM, Palette.LEAF_BLUSH, Palette.LEAF_LILAC }),
		"temperate-canopy" => new(.82f, .46f, 1f,
			new[] { Palette.LEAF_LILAC, Palette.LEAF_MINT, Palette.LEAF_CREAM }),
		"sakura-orchard" => new(.90f, .42f, 1f,
			new[] { Palette.LEAF_PINK, Palette.LEAF_BLUSH, Palette.LEAF_ROSE }),
		"fen-reed-alder" => new(.52f, .28f, .72f,
			new[] { Palette.LEAF_MINT, Palette.LEAF_CREAM }),
		"shallows-sparse-grove" => new(.34f, .22f, .76f,
			new[] { Palette.LEAF_CREAM, Palette.LEAF_BLUSH }),
		_ => throw new InvalidOperationException($"atlas vegetation set '{id}' has no domain-review grammar"),
	};

	private static bool LocallyLevel(VoxelGrid grid, int x, int z)
	{
		int centre = grid.Top[z * grid.Size + x];
		for (int dz = -3; dz <= 3; dz += 3)
		for (int dx = -3; dx <= 3; dx += 3)
			if (Math.Abs(grid.Top[(z + dz) * grid.Size + x + dx] - centre) > 2)
				return false;
		return true;
	}

	private static bool NearPlacedStructureOrRoad(VoxelGrid grid, int x, int z,
		int radius, bool allowRuinedStone)
	{
		for (int dz = -radius; dz <= radius; dz += 2)
		for (int dx = -radius; dx <= radius; dx += 2)
		{
			int xx = x + dx, zz = z + dz;
			if (xx < 0 || zz < 0 || xx >= grid.Size || zz >= grid.Size) return true;
			int index = zz * grid.Size + xx;
			if (grid.Heights[index] > grid.Top[index]) return true;
			int y = grid.Top[index] - 1;
			byte surface = grid.At(xx, y, zz);
			if (surface is Palette.PAVING or Palette.PATH ||
			    !allowRuinedStone && surface == Palette.STONE_WARM) return true;
		}
		return false;
	}

	private static float ReclamationAt(DomainPlanDefinition plan, int globalX, int globalZ)
	{
		float radians = plan.AxisDegrees * MathF.PI / 180f;
		float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
		float dx = globalX - plan.Origin.X, dz = globalZ - plan.Origin.Z;
		float localX = dx * cos - dz * sin;
		float localZ = dx * sin + dz * cos;
		float result = 0f;
		foreach (PlanPlatform platform in plan.Platforms)
		{
			if (platform.Reclamation > result && Inside(localX, localZ, platform.Polygon))
				result = platform.Reclamation;
		foreach (PlanPlatformCutout cutout in platform.Cutouts)
			if (cutout.Reclamation > result && Inside(localX, localZ, cutout.Polygon))
				result = cutout.Reclamation;
		foreach (PlanSurfacePatch patch in platform.SurfacePatches)
			if (Inside(localX, localZ, patch.Polygon))
			{
				float patchReclamation = patch.Role switch
				{
					PlanSurfacePatchRole.ReclaimedEarth => patch.Coverage,
					PlanSurfacePatchRole.RubbleField => patch.Coverage * .72f,
					_ => patch.Coverage * .35f,
				};
				if (patchReclamation > result) result = patchReclamation;
			}
		}
		return result;
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

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
	}
}

public readonly record struct AtlasDomainDressingStatistics(int Candidates, int Trees);
