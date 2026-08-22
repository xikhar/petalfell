using System;
using System.Runtime.CompilerServices;

namespace Petalfell.Core;

/// <summary>
/// The dense world grid: one byte per block for the whole map, plus the
/// heightfield that every other system reads instead of re-deriving its own
/// idea of where the ground is.
///
/// This is deliberately the same model as the reference project. It is what
/// caps world size — at Height 76 that is ~45 MB at 768 blocks square and
/// ~80 MB at 1024, which is the practical ceiling. Meshes and collision are
/// streamed per chunk; only this array is global.
/// </summary>
public sealed class VoxelGrid
{
	public readonly int Size;
	public readonly int Height;
	public readonly byte[] Blocks;
	/// <summary>Top solid block +1 per column. Kept in step with Blocks by every writer.</summary>
	public readonly short[] Heights;

	public VoxelGrid(int size, int height)
	{
		Size = size;
		Height = height;
		Blocks = new byte[size * size * height];
		Heights = new short[size * size];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Index(int x, int y, int z) => (y * Size + z) * Size + x;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool InBounds(int x, int y, int z) =>
		x >= 0 && z >= 0 && y >= 0 && x < Size && z < Size && y < Height;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte At(int x, int y, int z) =>
		InBounds(x, y, z) ? Blocks[Index(x, y, z)] : Palette.AIR;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool SolidAt(int x, int y, int z) => Palette.IsSolid(At(x, y, z));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Set(int x, int y, int z, byte id)
	{
		if (!InBounds(x, y, z)) return;
		Blocks[Index(x, y, z)] = id;
	}

	/// <summary>Ground height (first empty y) at a column, clamped into the map.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int HeightAt(int x, int z)
	{
		if (x < 0 || z < 0 || x >= Size || z >= Size) return 0;
		return Heights[z * Size + x];
	}

	/// <summary>Recompute the heightfield from the blocks. O(area x height); use sparingly.</summary>
	public void RebuildHeights()
	{
		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int top = 0;
			for (int y = Height - 1; y >= 0; y--)
			{
				if (Palette.IsSolid(Blocks[Index(x, y, z)])) { top = y + 1; break; }
			}
			Heights[z * Size + x] = (short)top;
		}
	}

	/// <summary>Fill a column from y0 (inclusive) to y1 (exclusive), updating Heights.</summary>
	public void Column(int x, int z, int y0, int y1, byte id)
	{
		if (x < 0 || z < 0 || x >= Size || z >= Size) return;
		y0 = Math.Max(0, y0);
		y1 = Math.Min(Height, y1);
		for (int y = y0; y < y1; y++) Blocks[Index(x, y, z)] = id;
		if (Palette.IsSolid(id) && y1 > Heights[z * Size + x]) Heights[z * Size + x] = (short)y1;
	}
}
