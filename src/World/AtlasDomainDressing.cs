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
	private const int Cell = 8;

	public static AtlasDomainDressingStatistics Apply(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, DomainPlanDefinition plan, int worldSeed) =>
		ApplyCore(window, atlas, worldSeed, plan);

	/// <summary>
	/// Sector review uses the same globally anchored grove grammar without an L3
	/// plan: trees follow biome density only, never authored courts.
	/// </summary>
	public static AtlasDomainDressingStatistics ApplyWilderness(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, int worldSeed) =>
		ApplyCore(window, atlas, worldSeed, plan: null);

	private static AtlasDomainDressingStatistics ApplyCore(AtlasSectorWindow window,
		WorldAtlasDefinition atlas, int worldSeed, DomainPlanDefinition plan)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (atlas?.BiomeCatalog == null) throw new ArgumentNullException(nameof(atlas));
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
			// One sample per 8-block cell left the 144/124 caps bald. Authored
			// high reclamation is an invitation to grove (reference-1/2).
			int attempts = 1;
			if (plan != null)
			{
				int cx = cellX * Cell + Cell / 2, cz = cellZ * Cell + Cell / 2;
				(float rec, _, _) = ReclamationAt(plan, cx, cz);
				if (rec >= 0.65f) attempts = 3;
			}
			for (int attempt = 0; attempt < attempts; attempt++)
			{
			int globalX = cellX * Cell + rng.RangeInt(2, Cell - 3);
			int globalZ = cellZ * Cell + rng.RangeInt(2, Cell - 3);
			int x = globalX - data.OriginX, z = globalZ - data.OriginZ;
			if (x < 4 || z < 4 || x >= data.Width - 4 || z >= data.Depth - 4) continue;
			candidates++;
			int index = z * data.Width + x;
			if (data.Land[index] == 0 || data.WaterSurface[index] > 0) continue;
			float reclamation = 0f;
			float localX = 0f, localZ = 0f;
			bool inDistrict = false;
			if (plan != null)
			{
				(reclamation, localX, localZ) = ReclamationAt(plan, globalX, globalZ);
				int gateZ = 48;
				foreach (PlanLandmark landmark in plan.Landmarks)
				{
					if (landmark.Id != "gate-hero-arch") continue;
					gateZ = landmark.Point.Z;
					break;
				}
				// Keep the drowned approach, the slot floor, and the masonry
				// cheeks clear. |X|<=9 on z>36 still planted on the 124/144
				// face and clipped the gate (reference-1's grove is beside
				// the cliff, not through the opening).
				bool onMassif = Math.Abs(localX) <= 18f &&
				                localZ >= gateZ - 28f && localZ < gateZ + 60f;
				// Trees on the 124 wing south lips read as planter bastions
				// flanking the stair (reference-1's grove is hillside beside
				// the cliff, not boxes on the terrace lip).
				bool onWingLip = Math.Abs(localX) > 22f && Math.Abs(localX) < 92f &&
				                 localZ >= gateZ - 8f && localZ < gateZ + 18f;
				// 114 Massif cheeks: trees on a 10-block shelf read as planter
				// boxes beside the stair. Grove stays on the hillside beyond.
				bool onCheek = localZ >= gateZ - 20f && localZ < gateZ + 10f &&
				               ((localX >= 10f && localX <= 50f) ||
				                (localX <= -8f && localX >= -42f));
				// Wide/far own the drowned pool. Grove on the east lobe hid
				// water+gate (reference-1/2/5).
				bool onDrownedLobe = localX <= -40f && localX >= -170f &&
				                     localZ <= -160f && localZ >= -480f;
				// East 114 bank: the cliff that joins the 124 to the shallows
				// (reference-1/2). Grove on that drop hid the masonry face.
				bool onEastBank = localX <= -52f && localX >= -180f &&
				                  localZ <= 136f && localZ >= -180f;
				float clear = localZ < gateZ - 52f ? 22f : 9f;
				if (onMassif || onWingLip || onCheek || onDrownedLobe || onEastBank ||
				    (Math.Abs(localX) <= clear && localZ > -520f && localZ < 180f))
					continue;
				// Made courts stay clear of orchard (reference-1/4). Terrain
				// courts and the natural hillsides left when the massif shrank
				// take blossom (reference-7/9).
				inDistrict = Math.Abs(localX) < 260f && localZ > -420f && localZ < 220f;
				if (inDistrict && reclamation > 0f && reclamation < 0.65f) continue;
			}
			// Any raised or cut column belongs to the authored L3 composition. A
			// named cutout may explicitly invite growth back; the wilderness pass is
			// not allowed to make that story decision on its own.
			if (grid.Top[index] != data.Height[index] && reclamation <= 0f) continue;
			int ground = grid.Top[index];
			byte surface = grid.At(x, ground - 1, z);
			bool hillsideGrove = inDistrict && reclamation <= 0f;
			bool plantable = Palette.IsGrassSurface(surface) || surface == Palette.MOSS ||
			                 surface == Palette.BLOSSOM_DRIFT || surface == Palette.MOSS_STONE;
			bool reclaimedStone = (reclamation > 0f || hillsideGrove) && surface is Palette.SAND or Palette.MUD or
				Palette.MOSS_STONE or Palette.STONE_WARM or Palette.STONE_PALE or Palette.STONE;
			if (!plantable && !reclaimedStone) continue;
			int nearRadius = reclamation >= 0.65f || hillsideGrove ? 3 : reclamation > 0.50f ? 3 : reclamation > 0.35f ? 4 : 5;
			int nearHeight = reclamation >= 0.65f || hillsideGrove ? 6 : reclamation > 0.50f ? 4 : 2;
			if (!LocallyLevel(grid, x, z, reclamation > 0.45f || hillsideGrove ? 7 : reclamation > 0.35f ? 4 : 2) ||
			    NearPlacedStructureOrRoad(grid, x, z, nearRadius,
				    allowRuinedStone: reclamation > 0f || hillsideGrove,
				    structureHeight: nearHeight)) continue;

			int profileIndex = data.Profile[index];
			if (data.ProfileBlend[index] > 0 &&
			    rng.Next() < data.ProfileBlend[index] / 255f)
				profileIndex = data.SecondaryProfile[index];
			BiomeBuildProfile profile = atlas.BiomeCatalog.Profiles[profileIndex];
			FloraSpec flora = For(profile.VegetationSetId);
			float density = grove.Fbm01(globalX / 168f, globalZ / 168f, 3);
			float groveFloor = flora.Density >= .55f ? .48f : flora.Density >= .20f ? .50f : .38f;
			float chance = Math.Max(0f, density - groveFloor) * flora.Density;
			if (reclamation >= 0.65f)
			{
				// Hinterland 124 plateaus: sparse blossom beside the cliff
				// (reference-1/2), not a roof wood. Lower made courts still
				// take the denser reclamation grove.
				chance = ground >= 120
					? Math.Clamp(0.08f + 0.12f * density, 0.06f, 0.22f)
					: Math.Clamp(0.42f + 0.28f * density, 0.40f, 0.75f);
			}
			else if (reclamation > 0f)
			{
				// Made ground may return a little growth (reference-2), not a wood
				// that swallows the monument. 0.10 still left a court orchard.
				chance = Math.Min(chance, reclamation * 0.045f * density);
			}
			else if (inDistrict)
			{
				// 0.16–0.42 was a wood that swallowed the water–gate landscape.
				// Reference-1/2: sparse blossom beside the cliff, not a canopy.
				chance = Math.Clamp(0.05f + 0.10f * density, 0.04f, 0.16f);
			}
			if (!rng.Chance(chance)) continue;

			float scale = rng.Range(flora.ScaleLo, flora.ScaleHi);
			if (reclamation > 0.60f || hillsideGrove)
				scale = rng.Range(0.62f, 1.08f);
			float tone = hue.Fbm01(globalX / 46f + 17f, globalZ / 46f - 31f, 2);
			byte leaf = flora.Leaves[Math.Min(flora.Leaves.Length - 1,
				(int)(tone * flora.Leaves.Length))];
			Vegetation.Tree(grid, rng, x, ground, z, scale, leaf);
			trees++;
			}
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
		"shallows-sparse-grove" => new(.10f, .42f, .82f,
			new[] { Palette.LEAF_PINK, Palette.LEAF_BLUSH, Palette.LEAF_ROSE }),
		_ => throw new InvalidOperationException($"atlas vegetation set '{id}' has no domain-review grammar"),
	};

	private static bool LocallyLevel(VoxelGrid grid, int x, int z, int slack = 2)
	{
		int centre = grid.Top[z * grid.Size + x];
		for (int dz = -3; dz <= 3; dz += 3)
		for (int dx = -3; dx <= 3; dx += 3)
			if (Math.Abs(grid.Top[(z + dz) * grid.Size + x + dx] - centre) > slack)
				return false;
		return true;
	}

	private static bool NearPlacedStructureOrRoad(VoxelGrid grid, int x, int z,
		int radius, bool allowRuinedStone, int structureHeight = 2)
	{
		int step = radius <= 0 ? 1 : 2;
		for (int dz = -radius; dz <= radius; dz += step)
		for (int dx = -radius; dx <= radius; dx += step)
		{
			int xx = x + dx, zz = z + dz;
			if (xx < 0 || zz < 0 || xx >= grid.Size || zz >= grid.Size) return true;
			int index = zz * grid.Size + xx;
			int stacked = grid.Heights[index] - grid.Top[index];
			if (stacked > structureHeight) return true;
			int y = grid.Heights[index] - 1;
			byte surface = grid.At(xx, y, zz);
			if (stacked > 0 && surface is Palette.RUBBLE or Palette.STONE_WARM
			    or Palette.STONE_PALE or Palette.SAND or Palette.MOSS_STONE)
				continue;
			if (surface is Palette.PAVING or Palette.PATH ||
			    !allowRuinedStone && surface == Palette.STONE_WARM) return true;
		}
		return false;
	}

	private static (float reclamation, float localX, float localZ) ReclamationAt(
		DomainPlanDefinition plan, int globalX, int globalZ)
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
		}
		return (result, localX, localZ);
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
