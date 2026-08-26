using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>What the land has put back on a ruin. plan.md §11a.4 is the order.</summary>
public enum Growth : byte
{
	/// <summary>Cushions on damp shaded stone. First to arrive, last to leave.</summary>
	Moss,
	/// <summary>Hanging from a broken wall head. Needs a wall that stood long enough.</summary>
	Vine,
	/// <summary>In the shelter of a wall, in the corners where litter collects.</summary>
	Fern,
	/// <summary>Out in the open floor, once the roof went and the light got in.</summary>
	Thicket,
	/// <summary>Standing in what used to be a room. The end of the succession.</summary>
	Sapling,
	/// <summary>The courses a wall lost, lying where they landed.</summary>
	Rubble,
}

/// <summary>
/// One sub-voxel growth instance. Not a block: these are emitted into the
/// existing ground-detail mesh, so they arrive with the meadow's material and
/// the meadow's wind.
/// </summary>
public struct Sprig
{
	public float X, Y, Z;
	public Growth Kind;
	/// <summary>The wall face this clings to. (0,0) for anything growing off the floor.</summary>
	public sbyte Fx, Fz;
	public float Size, Phase, Tone;
}

/// <summary>
/// Reclamation — plan.md §11a.3.
///
/// Moss is not a block type that a ruin sometimes uses instead of stone. The
/// first build treated it as one (`decay > 0.6f ? MOSS : PAVING`) and it read as
/// exactly what it was: a coin flip on a threshold. Growth is convincing only
/// because of WHERE it is, and where it is comes out of four fields evaluated
/// per block face — damp, shelter, aspect and age.
///
/// The aspect term is the one that does the most work for the least code. A face
/// turned away from the sun keeps its moisture, so one side of a ruin comes out
/// visibly greener than the other, and that asymmetry is what stops a structure
/// reading as uniformly "aged" by a slider.
///
/// Two outputs. Blocks get walked down a material chain — PLASTER spalls to
/// RUBBLE, RUBBLE goes under MOSS_STONE — so the player can read how long a wall
/// has stood from what it is made of. And SPRIGS are emitted into a per-chunk
/// bucket for the detail layer to render at sub-voxel scale, because a metre
/// cube of "vine" is not a vine.
///
/// The bucket is written once during world construction and never touched again,
/// which is what makes it safe for the mesher's worker threads to read from
/// without a lock.
/// </summary>
public static class Reclaim
{
	private static readonly Dictionary<int, List<Sprig>> ByChunk = new();
	private static int _chunkW;

	/// <summary>Sprig count from the last run, for the boot diagnostics.</summary>
	public static int LastSprigCount;
	/// <summary>Blocks walked down the decay chain, for the boot diagnostics.</summary>
	public static int LastWeatheredCount;

	/// <summary>
	/// The patch field.
	///
	/// Moss arrives in PATCHES, and this is what makes them patches. The first
	/// build drew the blotch term from the same per-block hash as everything
	/// else, so neighbouring blocks got independent answers and a weathered wall
	/// came out as a checkerboard of four materials — the confetti failure this
	/// project has now hit three times, in wall decay, in roof loss, and here.
	/// Anything that is supposed to read as a REGION has to be sampled from a
	/// field with a wavelength, never from a hash.
	/// </summary>
	private static Noise2D _patch;

	public static void Reset(int worldSize, int seed)
	{
		ByChunk.Clear();
		_chunkW = worldSize / ChunkMesher.ChunkSize + 1;
		_patch = new Noise2D(seed ^ 0x3055);
		LastSprigCount = 0;
		LastWeatheredCount = 0;
	}

	/// <summary>
	/// A smooth 0..1 blotch value at a block, coherent with its neighbours on all
	/// three axes. Two 2D samples rather than a 3D noise: the horizontal one
	/// carries the patch across a wall, the vertical one lets it climb or stop.
	/// </summary>
	private static float Patch(int x, int y, int z)
	{
		float plan = _patch.Fbm01(x * 0.15f, z * 0.15f, 2);
		float rise = _patch.Fbm01(y * 0.26f + 71.3f, (x + z) * 0.045f, 2);
		return plan * 0.66f + rise * 0.34f;
	}

	/// <summary>Everything growing in one chunk, or null. Read-only after world build.</summary>
	public static List<Sprig> In(int ci, int ck)
	{
		if (_chunkW == 0) return null;
		return ByChunk.TryGetValue(ck * _chunkW + ci, out var list) ? list : null;
	}

	private static void Add(in Sprig s)
	{
		int key = ((int)s.Z / ChunkMesher.ChunkSize) * _chunkW + (int)s.X / ChunkMesher.ChunkSize;
		if (!ByChunk.TryGetValue(key, out var list)) ByChunk[key] = list = new List<Sprig>(32);
		list.Add(s);
		LastSprigCount++;
	}

	/* ================================================================
	 * The material chain
	 * ================================================================ */

	/// <summary>Materials a structure is made of, as opposed to ground it stands on.</summary>
	private static bool IsMasonry(byte id) => id is Palette.STONE or Palette.STONE_PALE
		or Palette.STONE_WARM or Palette.PAVING or Palette.PLASTER or Palette.RUBBLE
		or Palette.MOSS_STONE;

	private static bool IsTimber(byte id) => id is Palette.PLANK or Palette.PLANK_PALE
		or Palette.BEAM or Palette.TRUNK;

	/// <summary>
	/// One step down the chain. Plaster fails first and exposes the core; moss
	/// takes the core last. Timber does not go mossy — it goes, and the builder
	/// has already decided how much of it is left.
	/// </summary>
	private static byte Weather(byte id, float moss) => id switch
	{
		Palette.PLASTER => moss > 0.46f ? Palette.MOSS_STONE
			: moss > 0.26f ? Palette.RUBBLE : id,
		Palette.RUBBLE or Palette.STONE or Palette.STONE_PALE or Palette.STONE_WARM
			or Palette.PAVING => moss > 0.40f ? Palette.MOSS_STONE : id,
		_ => id,
	};

	/* ================================================================
	 * The pass
	 * ================================================================ */

	private static readonly int[] Nx = { 1, -1, 0, 0 };
	private static readonly int[] Nz = { 0, 0, 1, -1 };

	/// <summary>
	/// Where the sun sits, flattened. A wall face whose normal points along this
	/// is the dry side; the opposite face is the green one.
	/// </summary>
	private static readonly float SunX, SunZ;

	static Reclaim()
	{
		float sx = Palette.SunDir.X, sz = Palette.SunDir.Z;
		float len = MathF.Sqrt(sx * sx + sz * sz);
		SunX = sx / len;
		SunZ = sz / len;
	}

	/// <summary>
	/// Let the land back into a finished structure.
	///
	/// Runs over the structure's bounding volume AFTER it is built, because every
	/// field it evaluates depends on the final shape — which faces ended up
	/// exposed, what survived to cover what, where the wall broke.
	/// </summary>
	public static void Overgrow(Terrain terrain, int x0, int z0, int w, int d,
		int yLo, int yHi, float age, uint seed)
	{
		var grid = terrain.Grid;
		int S = terrain.Size;
		var region = terrain.Plan.RegionAt(x0 + w * 0.5f, z0 + d * 0.5f);
		float moisture = region.Moisture;
		float span = Math.Max(1, yHi - yLo);

		// One margin block out, so growth spills off the structure onto the ground
		// beside it rather than stopping dead at the wall — plan.md §11a.5, the
		// contact line is what betrays a stamped-in building.
		for (int z = z0 - 1; z <= z0 + d; z++)
		for (int x = x0 - 1; x <= x0 + w; x++)
		{
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			int i = z * S + x;
			float damp0 = 0.10f + (terrain.Wet[i] != 0 ? 0.30f : 0f) + moisture * 0.40f;

			for (int y = yLo; y <= yHi; y++)
			{
				byte id = grid.At(x, y, z);
				if (id == Palette.AIR) continue;
				bool masonry = IsMasonry(id);
				bool timber = IsTimber(id);
				if (!masonry && !timber && !Palette.IsSolid(id)) continue;

				var rng = new Hash(x, y, z, seed);

				// Damp: standing water nearby, the province, and — the term that
				// matters most — height above the base. The bottom of a wall is
				// always wetter than the top, and growth that ignores that reads
				// as a texture rather than as something alive.
				float low = 1f - Rng.Clamp((y - yLo) / span, 0f, 1f);
				float damp = Rng.Clamp(damp0 + low * 0.50f, 0f, 1f);

				// Shelter: how boxed in this block is, and whether anything still
				// covers it. A crevice holds growth; a parapet does not.
				int neighbours = 0;
				for (int dz = -1; dz <= 1; dz++)
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dz == 0) continue;
					if (grid.SolidAt(x + dx, y, z + dz)) neighbours++;
				}
				bool covered = false;
				for (int k = 1; k <= 4 && !covered; k++)
					covered = grid.SolidAt(x, y + k, z);
				float shelter = Rng.Clamp(0.25f + neighbours / 8f * 0.55f + (covered ? 0.35f : 0f), 0f, 1f);

				// Blotches, never an even wash — and never per block. See Patch().
				float blot = 0.48f + Patch(x, y, z) * 1.10f;
				// Age raises the CEILING; it does not scale the answer.
				float vigour = 0.34f + 0.66f * age;

				bool openAbove = !grid.SolidAt(x, y + 1, z);

				// --- faces -------------------------------------------------
				// Each exposed vertical face is judged on its own, because
				// aspect is a per-face quantity and it is the whole reason one
				// side of a ruin comes out greener than the other.
				float bestFace = 0f;
				for (int k = 0; k < 4; k++)
				{
					int fx = Nx[k], fz = Nz[k];
					if (grid.SolidAt(x + fx, y, z + fz)) continue;

					// A weighted SUM of the three site terms, not a product.
					//
					// The first version multiplied damp x shelter x aspect x age,
					// four independent terms each averaging about a half, and the
					// answer averaged 0.06 — a continent of a hundred and ninety
					// structures came out with a thousand mossy blocks on it in
					// total, which is nothing. Stacked sub-unit multiplicands
					// collapse; the fix is to let the terms trade against each
					// other and keep age as a ceiling on the result.
					float facing = fx * SunX + fz * SunZ;
					float aspect = 0.5f - 0.5f * facing;   // 1 turned away from the sun
					float site = 0.32f * damp + 0.30f * shelter + 0.38f * aspect;
					float moss = vigour * site * blot;
					if (moss > bestFace) bestFace = moss;

					// Vines hang from a wall HEAD — a broken top course or the
					// lip of an opening. They need a wall that survived to be
					// climbed, so a heavily vined ruin is telling the player its
					// walls stood a long time.
					// The patch term is in the test as well as in the strength, so
					// vines arrive in HANGS. Emitting on a flat probability put one
					// on every second head block all the way round a building, and
					// an evenly spaced curtain is a fringe, not growth.
					if (masonry && openAbove && moss > 0.34f && rng.Chance(0.25f + blot * 0.30f))
					{
						int drop = 0;
						while (drop < 4 && grid.SolidAt(x, y - drop - 1, z)) drop++;
						if (drop >= 1)
						{
							Add(new Sprig
							{
								X = x + 0.5f + fx * 0.52f, Y = y + 0.96f, Z = z + 0.5f + fz * 0.52f,
								Kind = Growth.Vine, Fx = (sbyte)fx, Fz = (sbyte)fz,
								// Length is drawn per instance as well as fielded, so
								// a hang has a ragged bottom edge rather than a
								// hemmed one.
								Size = MathF.Min(drop + 0.4f, (0.5f + moss * 1.9f) * rng.Range(0.6f, 1.4f)),
								Phase = rng.Next() * 6.28f, Tone = rng.Next(),
							});
						}
					}

					// Ferns take the sheltered foot of a wall, on the outside as
					// readily as the inside.
					if (openAbove && moss > 0.34f && low > 0.5f && rng.Chance(0.30f))
					{
						Add(new Sprig
						{
							X = x + 0.5f + fx * 0.55f, Y = y + 1f, Z = z + 0.5f + fz * 0.55f,
							Kind = Growth.Fern, Fx = (sbyte)fx, Fz = (sbyte)fz,
							Size = 0.34f + moss * 0.42f,
							Phase = rng.Next() * 6.28f, Tone = rng.Next(),
						});
					}
				}

				// --- the block itself --------------------------------------
				if (masonry)
				{
					byte after = Weather(id, bestFace);
					if (after != id)
					{
						grid.Set(x, y, z, after);
						LastWeatheredCount++;
					}
				}

				if (!openAbove) continue;

				// Moss cushions on the exposed top. Small, and only where the
				// field is already strong enough to have swapped the material —
				// cushions on clean stone read as scattered litter.
				if (masonry && bestFace > 0.42f && rng.Chance(0.45f))
				{
					int n = 1 + (int)(rng.Next() * 2.99f);
					for (int c = 0; c < n; c++)
						Add(new Sprig
						{
							X = x + rng.Range(0.18f, 0.82f), Y = y + 1f, Z = z + rng.Range(0.18f, 0.82f),
							Kind = Growth.Moss, Size = rng.Range(0.22f, 0.44f),
							Phase = 0f, Tone = rng.Next(),
						});
				}

				// Rubble at the foot of a broken wall — plan.md §11a.5. A wall
				// that lost its top courses put them on the floor, and the clean
				// seam where masonry meets ground is the single strongest signal
				// that a building was stamped in rather than built.
				if (bestFace > 0f && low > 0.72f && WallAbove(grid, x, y, z) == 0 && NearWall(grid, x, y, z))
				{
					if (rng.Chance(0.30f))
					{
						int n = 1 + (int)(rng.Next() * 2.99f);
						for (int c = 0; c < n; c++)
							Add(new Sprig
							{
								X = x + rng.Range(0.15f, 0.85f), Y = y + 1f, Z = z + rng.Range(0.15f, 0.85f),
								Kind = Growth.Rubble, Size = rng.Range(0.22f, 0.48f),
								Phase = 0f, Tone = rng.Next(),
							});
					}
				}

				// Thickets and saplings want a FLOOR with the sky over it. A room
				// that kept its roof stays clear; the one that lost it fills. That
				// contrast is worth more than either state on its own.
				bool floorLike = masonry && !covered;
				if (!floorLike) continue;

				float open = vigour * (0.40f + moisture * 0.60f) * blot;
				if (open > 0.40f && rng.Chance(0.18f))
				{
					Add(new Sprig
					{
						X = x + rng.Range(0.25f, 0.75f), Y = y + 1f, Z = z + rng.Range(0.25f, 0.75f),
						Kind = Growth.Thicket, Size = 0.42f + open * 0.55f,
						Phase = rng.Next() * 6.28f, Tone = rng.Next(),
					});
				}
				// Last in the succession, and rare enough to stay an event.
				else if (age > 0.68f && open > 0.50f && rng.Chance(0.020f))
				{
					Add(new Sprig
					{
						X = x + 0.5f, Y = y + 1f, Z = z + 0.5f,
						Kind = Growth.Sapling, Size = rng.Range(0.9f, 1.7f),
						Phase = rng.Next() * 6.28f, Tone = rng.Next(),
					});
				}
			}
		}
	}

	/// <summary>How much wall still stands over this block.</summary>
	private static int WallAbove(VoxelGrid grid, int x, int y, int z)
	{
		int n = 0;
		while (n < 3 && grid.SolidAt(x, y + 1 + n, z)) n++;
		return n;
	}

	/// <summary>Is there standing masonry beside this block? Rubble collects at a foot.</summary>
	private static bool NearWall(VoxelGrid grid, int x, int y, int z)
	{
		for (int k = 0; k < 4; k++)
			if (grid.SolidAt(x + Nx[k], y + 1, z + Nz[k])) return true;
		return false;
	}

	/// <summary>
	/// A deterministic per-block draw. Seeded from the block itself rather than
	/// carried along, so the pass gives the same answer whatever order it walks
	/// the volume in.
	/// </summary>
	private struct Hash
	{
		private uint _s;

		public Hash(int x, int y, int z, uint salt)
		{
			unchecked
			{
				uint h = (uint)(x * 374761393) + (uint)(y * 668265263) + (uint)(z * 2246822519)
					+ salt * 1442695040u;
				h = (h ^ (h >> 13)) * 1274126177u;
				_s = h ^ (h >> 16);
			}
		}

		public float Next()
		{
			unchecked
			{
				_s += 0x6D2B79F5u;
				uint t = _s;
				t = (uint)((t ^ (t >> 15)) * (t | 1u));
				t ^= t + (uint)((t ^ (t >> 7)) * (t | 61u));
				return ((t ^ (t >> 14)) & 0xFFFFFF) / 16777216f;
			}
		}

		public float Range(float a, float b) => a + Next() * (b - a);
		public bool Chance(float p) => Next() < p;
	}
}
