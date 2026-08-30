using System;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Headless mechanical verification for derived wilderness. Terrain's verifier
/// owns compiled field seams; this one independently materialises placed blocks
/// and compares them by canonical coordinate, never dictionary enumeration.
/// </summary>
public static class AtlasWildernessAuthoring
{
	public static AtlasWildernessVerification Verify(WorldAtlasDefinition atlas,
		AtlasSectorCompiler compiler, int worldSeed, int sectorX, int sectorZ, int apron)
	{
		if (apron < AtlasWildernessDressing.RequiredWindowHalo)
			throw new InvalidOperationException(
				$"wilderness verification needs an apron of at least {AtlasWildernessDressing.RequiredWindowHalo}, got {apron}");
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		if (sectorX < 0 || sectorZ < 0 || sectorX >= columns || sectorZ >= rows)
			throw new InvalidOperationException($"sector {sectorX},{sectorZ} lies outside {columns}x{rows} atlas");

		Build first = Materialise(atlas, compiler, worldSeed, sectorX, sectorZ, apron);
		Build repeat = Materialise(atlas, compiler, worldSeed, sectorX, sectorZ, apron);
		if (first.Statistics != repeat.Statistics)
			throw new InvalidOperationException(
				$"wilderness repeat statistics differ: {first.Statistics} versus {repeat.Statistics}");
		Comparison repeated = Compare(first.Window, repeat.Window, "repeat", trimOuterHalo: false);

		Comparison east = default;
		if (sectorX + 1 < columns)
		{
			Build neighbour = Materialise(atlas, compiler, worldSeed, sectorX + 1, sectorZ, apron);
			east = Compare(first.Window, neighbour.Window, "east", trimOuterHalo: true);
		}
		Comparison south = default;
		if (sectorZ + 1 < rows)
		{
			Build neighbour = Materialise(atlas, compiler, worldSeed, sectorX, sectorZ + 1, apron);
			south = Compare(first.Window, neighbour.Window, "south", trimOuterHalo: true);
		}

		return new AtlasWildernessVerification(first.Statistics, repeated.Columns,
			repeated.Voxels, east.Columns, east.Voxels, south.Columns, south.Voxels);
	}

	private static Build Materialise(WorldAtlasDefinition atlas, AtlasSectorCompiler compiler,
		int worldSeed, int sectorX, int sectorZ, int apron)
	{
		AtlasSectorData data = compiler.Compile(sectorX, sectorZ, apron);
		var window = new AtlasSectorWindow(data, atlas, worldSeed);
		AtlasWildernessDressingStatistics statistics =
			AtlasWildernessDressing.Apply(window, atlas, worldSeed);
		return new Build(window, statistics);
	}

	private static Comparison Compare(AtlasSectorWindow a, AtlasSectorWindow b,
		string label, bool trimOuterHalo)
	{
		AtlasSectorData ad = a.Data, bd = b.Data;
		int minX = Math.Max(ad.OriginX, bd.OriginX);
		int minZ = Math.Max(ad.OriginZ, bd.OriginZ);
		int maxX = Math.Min(ad.OriginX + ad.Width, bd.OriginX + bd.Width);
		int maxZ = Math.Min(ad.OriginZ + ad.Depth, bd.OriginZ + bd.Depth);
		if (trimOuterHalo)
		{
			// Candidates need terrain under their anchor before a shape can be
			// validated. The disposable outer halo is deliberately not dressed from
			// unseen data; trim it while retaining the canonical core seam at the
			// centre of the overlap.
			int halo = AtlasWildernessDressing.RequiredWindowHalo;
			if (minX == ad.OriginX || minX == bd.OriginX) minX += halo;
			if (maxX == ad.OriginX + ad.Width || maxX == bd.OriginX + bd.Width) maxX -= halo;
			if (minZ == ad.OriginZ || minZ == bd.OriginZ) minZ += halo;
			if (maxZ == ad.OriginZ + ad.Depth || maxZ == bd.OriginZ + bd.Depth) maxZ -= halo;
		}
		if (minX >= maxX || minZ >= maxZ)
			throw new InvalidOperationException($"{label} wilderness overlap is empty after its safety halo");

		long voxels = 0;
		for (int globalZ = minZ; globalZ < maxZ; globalZ++)
		for (int globalX = minX; globalX < maxX; globalX++)
		{
			int ax = globalX - ad.OriginX, az = globalZ - ad.OriginZ;
			int bx = globalX - bd.OriginX, bz = globalZ - bd.OriginZ;
			int ai = az * ad.Width + ax, bi = bz * bd.Width + bx;
			if (a.Grid.Top[ai] != b.Grid.Top[bi] || a.Grid.Cap[ai] != b.Grid.Cap[bi] ||
			    a.Grid.Sub[ai] != b.Grid.Sub[bi] || a.Grid.Deep[ai] != b.Grid.Deep[bi])
				throw new InvalidOperationException(
					$"{label} wilderness base differs at global {globalX},{globalZ}");
			int y0 = Math.Max(0, Math.Min(a.Grid.Top[ai], b.Grid.Top[bi]) - 1);
			int y1 = Math.Min(a.Grid.Height, Math.Max(a.Grid.Heights[ai], b.Grid.Heights[bi]));
			for (int y = y0; y < y1; y++)
			{
				byte av = a.Grid.At(ax, y, az), bv = b.Grid.At(bx, y, bz);
				if (av != bv)
					throw new InvalidOperationException(
						$"{label} wilderness voxel differs at global {globalX},{y},{globalZ}: {av} versus {bv}");
				voxels++;
			}
			if (a.Grid.Heights[ai] != b.Grid.Heights[bi])
				throw new InvalidOperationException(
					$"{label} wilderness height differs at global {globalX},{globalZ}: " +
					$"{a.Grid.Heights[ai]} versus {b.Grid.Heights[bi]}");
		}
		return new Comparison((maxX - minX) * (maxZ - minZ), voxels);
	}

	private readonly record struct Build(AtlasSectorWindow Window,
		AtlasWildernessDressingStatistics Statistics);
	private readonly record struct Comparison(int Columns, long Voxels);
}

public readonly record struct AtlasWildernessVerification(
	AtlasWildernessDressingStatistics Statistics, int RepeatColumns, long RepeatVoxels,
	int EastColumns, long EastVoxels, int SouthColumns, long SouthVoxels);
