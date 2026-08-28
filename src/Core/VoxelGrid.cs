using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Petalfell.Core;

/// <summary>
/// The world's blocks — DERIVED, not stored.
///
/// This used to be one dense byte per block for the whole map, and that single
/// array was the only thing deciding how big Petalfell could be. At 76 blocks
/// tall it is 80 MB at 1024 across, 800 MB at ten times the area, and 8 GB at
/// ten times the width. Nothing else in the project came close; the array was
/// 78% of the world's memory all by itself.
///
/// It was also almost entirely redundant. Look at what the terrain pass
/// actually wrote into it: a cap block on top, one block of substrate under
/// that, and deep stone all the way down. Three values, of which two are per
/// COLUMN and the third is a noise field. So a column needs two bytes and its
/// height, and every block in it can be computed on demand — a 38-fold saving
/// against storing the answer.
///
/// What cannot be derived is everything PLACED: bridge decks, house walls, tree
/// canopies, lanterns. Those go in a sparse overlay, which is the right shape
/// for them — they are a vanishing fraction of the volume, and they are exactly
/// the things a future authored-asset layer will want to own.
///
/// The public surface is unchanged. Callers still read At() and write Set(),
/// and the chunk mesher never learns that the array underneath it is gone.
/// </summary>
public sealed class VoxelGrid
{
	public readonly int Size;
	public readonly int Height;

	/// <summary>Top solid block +1 per column, INCLUDING anything placed on it.</summary>
	public readonly short[] Heights;

	/// <summary>First empty y of the bare terrain, before anything was built on it.</summary>
	public readonly short[] Top;
	/// <summary>The block capping each terrain column, and the one directly under it.</summary>
	public readonly byte[] Cap, Sub;
	/// <summary>
	/// Optional authored deep/cliff material per column. Zero preserves the
	/// legacy repeating stone field; production atlas profiles set it explicitly.
	/// </summary>
	public readonly byte[] Deep;
	/// <summary>Global atlas origin of this local window, used by stable material fields.</summary>
	public readonly int OriginX, OriginZ;

	/// <summary>
	/// Everything placed on the terrain, keyed by absolute block index.
	///
	/// AIR is stored explicitly rather than removed — clearing a block that the
	/// terrain would otherwise derive as solid (a house pad cut into a slope) is
	/// a real edit and has to survive the lookup.
	/// </summary>
	private readonly Dictionary<long, byte> _edits = new();
	/// <summary>
	/// The same edits, bucketed by tile.
	///
	/// The flat dictionary answers "what is at this block", which is what a point
	/// lookup needs. Filling a chunk window needs the opposite question — "what
	/// has been placed anywhere near here" — and answering that by walking the
	/// whole dictionary is a million iterations per chunk on a finished map.
	/// Two views of one set: the cost is a list per populated tile, and almost
	/// no tile is populated.
	/// </summary>
	private readonly Dictionary<int, List<long>> _tileEdits = new();
	/// <summary>
	/// Which coarse tiles hold any edit at all.
	///
	/// Almost none of them do, and this is read once per block face by the
	/// mesher — several hundred million times over a session. Checking a bool
	/// before touching the dictionary keeps the common case to one array read.
	/// </summary>
	private readonly bool[] _touched;
	private readonly int _tileW;
	private const int TileShift = 5;

	/// <summary>
	/// Deep stone tone, tabulated rather than evaluated.
	///
	/// The terrain pass sampled a two-octave fbm per block, which is fine when it
	/// runs once over the map and writes the answer down. Deriving on demand puts
	/// that call in the mesher's inner loop, six times per block for the
	/// neighbour tests — hundreds of millions of fbm evaluations to prime the
	/// chunks around the player. A tile of the SAME field, indexed by the low
	/// bits of the coordinate, is one array read and locally identical; deep
	/// stone is only ever visible on the face of a tall cut, where a repeat every
	/// 64 blocks is not something anyone can see.
	/// </summary>
	private readonly byte[] _deep;
	private const int DeepW = 64, DeepH = 32;

	public VoxelGrid(int size, int height, int seed = 0, int originX = 0, int originZ = 0)
	{
		Size = size;
		Height = height;
		OriginX = originX;
		OriginZ = originZ;
		Heights = new short[size * size];
		Top = new short[size * size];
		Cap = new byte[size * size];
		Sub = new byte[size * size];
		Deep = new byte[size * size];

		_tileW = (size >> TileShift) + 1;
		_touched = new bool[_tileW * _tileW];

		_deep = new byte[DeepW * DeepW * DeepH];
		var noise = new Noise2D(seed + 4);
		for (int y = 0; y < DeepH; y++)
		for (int z = 0; z < DeepW; z++)
		for (int x = 0; x < DeepW; x++)
		{
			float n = noise.Fbm(x * 0.035f, (z + y * 5.3f) * 0.035f, 2);
			_deep[(y * DeepW + z) * DeepW + x] = n > 0.24f ? Palette.STONE_WARM
				: n < -0.28f ? Palette.STONE_PALE : Palette.STONE;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Index(int x, int y, int z) => (y * Size + z) * Size + x;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool InBounds(int x, int y, int z) =>
		x >= 0 && z >= 0 && y >= 0 && x < Size && z < Size && y < Height;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int Tile(int x, int z) => (z >> TileShift) * _tileW + (x >> TileShift);

	/// <summary>Record the bare terrain of one column. Called once, by the terrain pass.</summary>
	public void Describe(int x, int z, int top, byte cap, byte sub)
		=> Describe(x, z, top, cap, sub, Palette.AIR);

	/// <summary>Record a column whose profile owns the exposed deep/cliff material.</summary>
	public void Describe(int x, int z, int top, byte cap, byte sub, byte deep)
	{
		int i = z * Size + x;
		Top[i] = (short)top;
		Cap[i] = cap;
		Sub[i] = sub;
		Deep[i] = deep;
		if (top > Heights[i]) Heights[i] = (short)top;
	}

	/// <summary>
	/// Replace a bare-terrain column before any sparse placed geometry exists in
	/// its coarse tile. Authored stairs use this after a platform pass: raising
	/// only would leave the upper platform underneath the lower stair treads and
	/// turn a six-block procession into one abrupt ledge.
	/// </summary>
	public void RedescribeUnedited(int x, int z, int top, byte cap, byte sub, byte deep)
	{
		if (x < 0 || z < 0 || x >= Size || z >= Size) return;
		if (_touched[Tile(x, z)])
			throw new InvalidOperationException(
				"bare terrain cannot be replaced after placed geometry touched its tile");
		int i = z * Size + x;
		Top[i] = (short)top;
		Cap[i] = cap;
		Sub[i] = sub;
		Deep[i] = deep;
		Heights[i] = (short)top;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte Terrain(int x, int y, int z)
	{
		int i = z * Size + x;
		int h = Top[i];
		if (y >= h) return Palette.AIR;
		if (y == h - 1) return Cap[i];
		// The substrate is one block, and never the bottom of the world: a column
		// two blocks tall is a cap over stone, not a cap over soil over nothing.
		if (y >= h - 2 && y >= 1) return Sub[i];
		if (Deep[i] != Palette.AIR) return Deep[i];
		int gz = z + OriginZ, gx = x + OriginX;
		return _deep[((y & (DeepH - 1)) * DeepW + (gz & (DeepW - 1))) * DeepW + (gx & (DeepW - 1))];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public byte At(int x, int y, int z)
	{
		if (!InBounds(x, y, z)) return Palette.AIR;
		if (_touched[Tile(x, z)] && _edits.TryGetValue(Index(x, y, z), out byte placed))
			return placed;
		return Terrain(x, y, z);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool SolidAt(int x, int y, int z) => Palette.IsSolid(At(x, y, z));

	public void Set(int x, int y, int z, byte id)
	{
		if (!InBounds(x, y, z)) return;
		int tile = Tile(x, z);
		_touched[tile] = true;
		long key = Index(x, y, z);
		// Only index a key the first time it is written; overwriting a block must
		// not add it to the tile list twice.
		if (!_edits.ContainsKey(key))
		{
			if (!_tileEdits.TryGetValue(tile, out var list))
				_tileEdits[tile] = list = new List<long>();
			list.Add(key);
		}
		_edits[key] = id;
	}

	/// <summary>Ground height (first empty y) at a column, clamped into the map.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int HeightAt(int x, int z)
	{
		if (x < 0 || z < 0 || x >= Size || z >= Size) return 0;
		return Heights[z * Size + x];
	}

	/// <summary>Fill a column from y0 (inclusive) to y1 (exclusive), updating Heights.</summary>
	public void Column(int x, int z, int y0, int y1, byte id)
	{
		if (x < 0 || z < 0 || x >= Size || z >= Size) return;
		y0 = Math.Max(0, y0);
		y1 = Math.Min(Height, y1);
		for (int y = y0; y < y1; y++) Set(x, y, z, id);
		if (Palette.IsSolid(id) && y1 > Heights[z * Size + x]) Heights[z * Size + x] = (short)y1;
	}

	/// <summary>
	/// Copy a cuboid of blocks into a caller-owned buffer.
	///
	/// The chunk mesher asks for roughly two hundred thousand blocks per chunk —
	/// once for the block itself and six more times for its neighbours — and
	/// every one of those goes through a bounds test, a tile test, a possible
	/// dictionary probe and a derivation. That was affordable when the answer was
	/// a single array index; it is not now the blocks are computed.
	///
	/// Resolving the whole neighbourhood ONCE and letting the mesher read a flat
	/// local array turns the per-block work into a single indexed load, and takes
	/// the derivation count down by a factor of seven.
	/// </summary>
	public void FillWindow(byte[] dst, int x0, int z0, int w, int yTop)
	{
		Array.Clear(dst, 0, Math.Min(dst.Length, w * w * yTop));
		for (int y = 0; y < yTop; y++)
		{
			int plane = y * w * w;
			for (int lz = 0; lz < w; lz++)
			{
				int z = z0 + lz;
				if (z < 0 || z >= Size) continue;
				int row = plane + lz * w;
				for (int lx = 0; lx < w; lx++)
				{
					int x = x0 + lx;
					if (x < 0 || x >= Size) continue;
					int i = z * Size + x;
					int h = Top[i];
					byte id;
					if (y >= h) id = Palette.AIR;
					else if (y == h - 1) id = Cap[i];
					else if (y >= h - 2 && y >= 1) id = Sub[i];
					else if (Deep[i] != Palette.AIR) id = Deep[i];
					else
					{
						int gz = z + OriginZ, gx = x + OriginX;
						id = _deep[((y & (DeepH - 1)) * DeepW + (gz & (DeepW - 1))) * DeepW + (gx & (DeepW - 1))];
					}
					dst[row + lx] = id;
				}
			}
		}

		// Placed blocks last, so an edit always wins over the terrain under it —
		// including an edit that carved AIR out of solid ground for a house pad.
		// Only the tiles this window actually touches are visited.
		if (_edits.Count == 0) return;
		int tz0 = Math.Max(0, z0) >> TileShift, tz1 = Math.Min(Size - 1, z0 + w - 1) >> TileShift;
		int tx0 = Math.Max(0, x0) >> TileShift, tx1 = Math.Min(Size - 1, x0 + w - 1) >> TileShift;

		for (int tz = tz0; tz <= tz1; tz++)
		for (int tx = tx0; tx <= tx1; tx++)
		{
			if (!_tileEdits.TryGetValue(tz * _tileW + tx, out var list)) continue;
			foreach (long key in list)
			{
				int y = (int)(key / (Size * (long)Size));
				if (y < 0 || y >= yTop) continue;
				int rem = (int)(key - (long)y * Size * Size);
				int lx = rem % Size - x0, lz = rem / Size - z0;
				if (lx < 0 || lz < 0 || lx >= w || lz >= w) continue;
				dst[(y * w + lz) * w + lx] = _edits[key];
			}
		}
	}

	/// <summary>How many blocks have been placed on top of the bare terrain.</summary>
	public int PlacedCount => _edits.Count;

	/// <summary>Every placed block, for diagnostics. Not ordered.</summary>
	public IEnumerable<byte> Placed => _edits.Values;
}
