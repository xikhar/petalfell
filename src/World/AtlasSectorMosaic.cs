using System;
using System.Collections.Generic;

namespace Petalfell.World;

/// <summary>
/// Joins ordinary compiled sector artifacts into a disposable square review
/// window. The source sectors remain the unit of compilation and persistence;
/// this class exists only so one authored district can be judged across seams.
/// </summary>
public static class AtlasSectorMosaic
{
	public static AtlasSectorData Compose(WorldAtlasDefinition atlas,
		int minSectorX, int minSectorZ, int maxSectorX, int maxSectorZ,
		Func<int, int, AtlasSectorData> load)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		if (load == null) throw new ArgumentNullException(nameof(load));
		if (minSectorX < 0 || minSectorZ < 0 || maxSectorX < minSectorX || maxSectorZ < minSectorZ)
			throw new ArgumentOutOfRangeException(nameof(minSectorX), "invalid sector mosaic bounds");
		int sectorColumns = atlas.Width / atlas.SectorSize;
		int sectorRows = atlas.Depth / atlas.SectorSize;
		if (maxSectorX >= sectorColumns || maxSectorZ >= sectorRows)
			throw new ArgumentOutOfRangeException(nameof(maxSectorX),
				$"sector mosaic {minSectorX},{minSectorZ}..{maxSectorX},{maxSectorZ} leaves atlas grid {sectorColumns}x{sectorRows}");

		int spanX = maxSectorX - minSectorX + 1;
		int spanZ = maxSectorZ - minSectorZ + 1;
		int apron = AtlasSectorCompiler.DefaultApron;
		if (spanX != spanZ)
			throw new InvalidOperationException(
				$"the current VoxelGrid review window must be square; requested {spanX}x{spanZ} sectors");

		var sources = new Dictionary<(int x, int z), AtlasSectorData>();
		string fingerprint = null;
		for (int sz = minSectorZ; sz <= maxSectorZ; sz++)
		for (int sx = minSectorX; sx <= maxSectorX; sx++)
		{
			AtlasSectorData source = load(sx, sz) ??
				throw new InvalidOperationException($"sector loader returned null for {sx},{sz}");
			if (source.SectorX != sx || source.SectorZ != sz)
				throw new InvalidOperationException(
					$"sector loader returned {source.SectorX},{source.SectorZ} for {sx},{sz}");
			if (source.CoreSize != atlas.SectorSize || source.Apron != apron)
				throw new InvalidOperationException(
					$"sector {sx},{sz} uses core/apron {source.CoreSize}/{source.Apron}, expected {atlas.SectorSize}/{apron}");
			if (fingerprint != null && source.SourceFingerprint != fingerprint)
				throw new InvalidOperationException("domain mosaic cannot mix sector artifacts from different atlas sources");
			fingerprint ??= source.SourceFingerprint;
			sources[(sx, sz)] = source;
		}

		int core = spanX * atlas.SectorSize;
		int width = core + apron * 2;
		var result = new AtlasSectorData(minSectorX, minSectorZ,
			minSectorX * atlas.SectorSize - apron,
			minSectorZ * atlas.SectorSize - apron,
			core, apron, width, width, atlas.Height, atlas.SeaLevel,
			$"{fingerprint}:review-mosaic:{minSectorX},{minSectorZ}-{maxSectorX},{maxSectorZ}");

		for (int z = 0; z < result.Depth; z++)
		{
			int globalZ = result.OriginZ + z;
			int ownerZ = Math.Clamp(globalZ / atlas.SectorSize, minSectorZ, maxSectorZ);
			for (int x = 0; x < result.Width; x++)
			{
				int globalX = result.OriginX + x;
				int ownerX = Math.Clamp(globalX / atlas.SectorSize, minSectorX, maxSectorX);
				AtlasSectorData source = sources[(ownerX, ownerZ)];
				int sourceX = globalX - source.OriginX;
				int sourceZ = globalZ - source.OriginZ;
				int si = sourceZ * source.Width + sourceX;
				int di = z * result.Width + x;
				result.Height[di] = source.Height[si];
				result.WaterSurface[di] = source.WaterSurface[si];
				result.Land[di] = source.Land[si];
				result.Water[di] = source.Water[si];
				result.Hydrology[di] = source.Hydrology[si];
				result.Profile[di] = source.Profile[si];
				result.SecondaryProfile[di] = source.SecondaryProfile[si];
				result.ProfileBlend[di] = source.ProfileBlend[si];
				result.Surface[di] = source.Surface[si];
				result.Slope[di] = source.Slope[si];
				result.Aspect[di] = source.Aspect[si];
				result.Curvature[di] = source.Curvature[si];
				result.Wetness[di] = source.Wetness[si];
			}
		}
		result.Validate(atlas.BiomeCatalog.Profiles.Count);
		return result;
	}
}
