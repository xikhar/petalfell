using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Terrain shaping operators.
///
/// The lesson learned the hard way in the reference project: you cannot blur a
/// heightfield into terraces. Blurring a step spreads it over ~2 sigma columns
/// and quantising that spread produces exactly the one-block staircase we are
/// trying to avoid. Broad shelves only happen if the field is already
/// piecewise-flat by construction; these operators keep it that way — clean up
/// slivers, straighten cliff lines, and then cut walkable stairs back into the
/// result on purpose.
/// </summary>
public static class TerrainShape
{
	/// <summary>
	/// Majority (mode) filter — the workhorse. On a quantised field this is a
	/// morphological open+close: it deletes slivers narrower than the kernel,
	/// rounds ragged contour edges into decisive lines, and grows the dominant
	/// shelf outward. Ties resolve toward the column's own value so stable
	/// regions never drift.
	/// </summary>
	public static short[] ModeFilter(short[] lev, int S, int radius, byte[] land, int iterations = 1)
	{
		var src = lev;
		for (int it = 0; it < iterations; it++)
		{
			var outp = (short[])src.Clone();
			var input = src;
			// Parallel over rows, with the tally kept per partition. Rows are
			// independent — every column reads the source and writes only its own
			// cell — and the result does not depend on ordering, so a seed still
			// gives one world.
			System.Threading.Tasks.Parallel.For(0, S,
				() => new Dictionary<short, int>(64),
				(z, _, counts) =>
				{
					for (int x = 0; x < S; x++)
					{
						int i = z * S + x;
						if (land[i] == 0) continue;
						counts.Clear();
						short self = input[i];
						for (int dz = -radius; dz <= radius; dz++)
						{
							int zz = Rng.ClampI(z + dz, 0, S - 1);
							for (int dx = -radius; dx <= radius; dx++)
							{
								int j = zz * S + Rng.ClampI(x + dx, 0, S - 1);
								if (land[j] == 0) continue;
								short v = input[j];
								counts.TryGetValue(v, out int c);
								counts[v] = c + 1;
							}
						}
						short best = self;
						int bestC = -1;
						foreach (var kv in counts)
						{
							if (kv.Value > bestC ||
							    (kv.Value == bestC && Math.Abs(kv.Key - self) < Math.Abs(best - self)))
							{
								best = kv.Key; bestC = kv.Value;
							}
						}
						outp[i] = best;
					}
					return counts;
				},
				_ => { });
			src = outp;
		}
		return src;
	}

	/// <summary>
	/// Label walk-connected regions. Two neighbouring land columns belong to
	/// the same region when the player can step between them freely, so a shelf
	/// plus its own access stair count as one region.
	/// </summary>
	public static (int[] label, List<int> sizes) LabelWalk(short[] lev, int S, byte[] land, int maxStep)
	{
		var label = new int[S * S];
		Array.Fill(label, -1);
		var sizes = new List<int>();
		var stack = new int[S * S];
		int next = 0;

		for (int start = 0; start < S * S; start++)
		{
			if (land[start] == 0 || label[start] >= 0) continue;
			int id = next++;
			int sp = 0, size = 0;
			stack[sp++] = start;
			label[start] = id;
			while (sp > 0)
			{
				int i = stack[--sp];
				size++;
				int x = i % S, z = i / S;
				int h = lev[i];
				for (int d = 0; d < 4; d++)
				{
					int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
					int nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= S || nz >= S) continue;
					int j = nz * S + nx;
					if (land[j] == 0 || label[j] >= 0) continue;
					if (Math.Abs(lev[j] - h) > maxStep) continue;
					label[j] = id;
					stack[sp++] = j;
				}
			}
			sizes.Add(size);
		}
		return (label, sizes);
	}

	/// <summary>
	/// Delete regions smaller than minArea by flooding them to the most common
	/// height on their border. Kills the single-column pimples and one-block
	/// moats that survive the mode filter, both of which read as noise.
	/// </summary>
	public static short[] Despeckle(short[] lev, int S, byte[] land, int minArea = 14)
	{
		var outp = (short[])lev.Clone();
		var (label, sizes) = LabelWalk(lev, S, land, 0);

		// Bucket every cell by its region in ONE pass.
		//
		// This used to sweep the entire map once per small region looking for its
		// members — O(regions x area), and a large map has tens of thousands of
		// specks, so the cost went up with the SQUARE of the area. It was the
		// second thing standing between this project and a big world.
		var head = new int[sizes.Count];
		Array.Fill(head, -1);
		var next = new int[S * S];
		for (int i = S * S - 1; i >= 0; i--)
		{
			int id = label[i];
			if (id < 0) continue;
			next[i] = head[id];
			head[id] = i;
		}

		var border = new Dictionary<short, int>(32);
		for (int id = 0; id < sizes.Count; id++)
		{
			if (sizes[id] >= minArea) continue;
			border.Clear();
			for (int i = head[id]; i >= 0; i = next[i])
			{
				int x = i % S, z = i / S;
				for (int d = 0; d < 4; d++)
				{
					int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
					int nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= S || nz >= S) continue;
					int j = nz * S + nx;
					if (land[j] == 0 || label[j] == id) continue;
					border.TryGetValue(lev[j], out int c);
					border[lev[j]] = c + 1;
				}
			}
			short best = 0;
			int bestC = 0;
			foreach (var kv in border) if (kv.Value > bestC) { best = kv.Key; bestC = kv.Value; }
			if (bestC == 0) continue;
			for (int i = head[id]; i >= 0; i = next[i]) outp[i] = best;
		}
		return outp;
	}

	/// <summary>
	/// Cut walkable stairs into cliffs until the land is one connected region.
	///
	/// Standard terrace steps are two blocks, so every shelf boundary is a wall the
	/// player cannot climb. Rather than softening the cliffs — which is how you
	/// get mush — leave them sharp and carve a small number of deliberate
	/// notched staircases, the same move the reference images make.
	/// </summary>
	/// <summary>
	/// Cut stairs until every worthwhile shelf is reachable from the main one.
	///
	/// Connectivity is a global property, so the only honest way to know what is
	/// still stranded is to re-label the whole map — and that is expensive. The
	/// first version paid it PER STAIR: label the map, scan it for the single
	/// cheapest boundary anywhere, cut one stair, repeat ninety times. At a
	/// thousand blocks across that was already the most expensive thing in world
	/// generation, and it scales with area times the number of stairs, so a map
	/// ten times the area needs ten times as many cuts over ten times as much
	/// ground — a hundredfold. It would have taken half a minute on its own.
	///
	/// Cutting is now batched by ROUND. One labelling, then the cheapest boundary
	/// for EVERY stranded region at once, then all of those cuts together. Five
	/// or six rounds settle a map that previously took ninety passes, because the
	/// regions are largely independent — connecting one rarely changes where
	/// another one's cheapest way out is.
	/// </summary>
	public static byte[] CarveStairs(short[] lev, int S, byte[] land,
		int minArea = 30, int tread = 2, int width = 3, int maxStairs = 90,
		byte[] skip = null)
	{
		var mask = new byte[S * S];
		// A flat array rather than a HashSet: this is tested once per cell per
		// direction per round, and at a large world size the hashing alone was
		// measurable. Same semantics, including persisting across rounds.
		var dead = new bool[1024];
		bool Dead(int id) => id >= 0 && id < dead.Length && dead[id];
		void Kill(int id)
		{
			if (id < 0) return;
			if (id >= dead.Length) Array.Resize(ref dead, Math.Max(id + 1, dead.Length * 2));
			dead[id] = true;
		}

		int cuts = 0;
		const int MaxRounds = 24;

		for (int round = 0; round < MaxRounds && cuts < maxStairs; round++)
		{
			var (label, sizes) = LabelWalk(lev, S, land, 1);
			if (sizes.Count <= 1) break;

			int mainId = 0;
			for (int id = 1; id < sizes.Count; id++) if (sizes[id] > sizes[mainId]) mainId = id;

			// Compact the regions still worth connecting into a dense slot range,
			// so the per-thread bests are a handful of entries rather than one per
			// label on the map.
			var slot = new int[sizes.Count];
			Array.Fill(slot, -1);
			int pending = 0;
			for (int id = 0; id < sizes.Count; id++)
				if (id != mainId && sizes[id] >= minArea && !Dead(id)) slot[id] = pending++;
			if (pending == 0) break;

			// Ties break on the LOWEST CELL INDEX, not on whoever got there first.
			// The sequential scan resolved ties by scan order, which is the same
			// thing; saying it explicitly is what makes the search safe to run
			// across threads, so a seed still produces exactly one world.
			var best = new (int cost, int at, int x, int z, int nx, int nz)[pending];
			for (int k = 0; k < pending; k++) best[k] = (int.MaxValue, int.MaxValue, -1, -1, 0, 0);
			var gate = new object();

			System.Threading.Tasks.Parallel.For(1, S - 1,
				() =>
				{
					var local = new (int cost, int at, int x, int z, int nx, int nz)[pending];
					for (int k = 0; k < pending; k++) local[k] = (int.MaxValue, int.MaxValue, -1, -1, 0, 0);
					return local;
				},
				(z, _, local) =>
				{
					for (int x = 1; x < S - 1; x++)
					{
						int i = z * S + x;
						if (land[i] == 0) continue;
						int a = label[i];
						if (a < 0 || slot[a] < 0) continue;
						int k = slot[a];
						for (int d = 0; d < 4; d++)
						{
							int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
							int nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
							int j = nz * S + nx;
							if (land[j] == 0 || label[j] == a) continue;
							if (skip != null && (skip[i] != 0 || skip[j] != 0)) continue;
							int dh = Math.Abs(lev[j] - lev[i]);
							if (dh < 2) continue;
							int cost = dh * 4 + ((label[j] == mainId || a == mainId) ? 0 : 6);
							if (cost < local[k].cost || (cost == local[k].cost && i < local[k].at))
								local[k] = (cost, i, x, z, nx, nz);
						}
					}
					return local;
				},
				local =>
				{
					lock (gate)
					{
						for (int k = 0; k < pending; k++)
						{
							if (local[k].x < 0) continue;
							if (local[k].cost > best[k].cost ||
								(local[k].cost == best[k].cost && local[k].at >= best[k].at)) continue;
							best[k] = local[k];
						}
					}
				});

			// Cut in slot order, which is label order, which is scan order — so a
			// round is as deterministic as a single cut was.
			int cutThisRound = 0;
			for (int id = 0; id < sizes.Count && cuts < maxStairs; id++)
			{
				int k = slot[id];
				if (k < 0) continue;
				if (best[k].x < 0) { Kill(id); continue; }

				int bx = best[k].x, bz = best[k].z, bnx = best[k].nx, bnz = best[k].nz;
				bool firstIsHigh = lev[bz * S + bx] > lev[bnz * S + bnx];
				int hx = firstIsHigh ? bx : bnx, hz = firstIsHigh ? bz : bnz;
				int lx = firstIsHigh ? bnx : bx, lz = firstIsHigh ? bnz : bz;
				CutStair(lev, S, land, mask, lx, lz, hx - lx, hz - lz, tread, width, skip);
				cuts++;
				cutThisRound++;
			}
			if (cutThisRound == 0) break;
		}
		return mask;
	}

	/// <summary>One staircase: from the low column, biting up into the high ground.</summary>
	private static void CutStair(short[] lev, int S, byte[] land, byte[] mask,
		int lx, int lz, int ux, int uz, int tread, int width, byte[] skip)
	{
		int lo = lev[lz * S + lx];
		int px = uz, pz = ux;                  // perpendicular, for the stair width
		int half = (width - 1) / 2;
		for (int s = 1; s < 96; s++)
		{
			int x = lx + ux * s, z = lz + uz * s;
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) break;
			int i = z * S + x;
			if (land[i] == 0 || (skip != null && skip[i] != 0)) break;
			int target = lo + (int)MathF.Ceiling(s / (float)tread);
			if (lev[i] <= target) break;       // reached the upper shelf
			for (int w = -half; w <= half; w++)
			{
				int wx = x + px * w, wz = z + pz * w;
				if (wx < 0 || wz < 0 || wx >= S || wz >= S) continue;
				int j = wz * S + wx;
				if (land[j] == 0 || (skip != null && skip[j] != 0)) continue;
				if (lev[j] > target) { lev[j] = (short)target; mask[j] = 1; }
				else if (w == 0) mask[j] = 1;
			}
		}
	}

	/// <summary>
	/// Limit how far a column may stand above its lowest neighbour, so the land
	/// steps down one terrace at a time instead of dropping several at once.
	///
	/// This is what makes a riser show exactly one green turf block over one
	/// block of substrate. Without it two shelves that happen to differ by two
	/// or three terraces meet in a single tall face, the stone under the soil
	/// band comes into view, and every edge reads as a stack of stripes rather
	/// than as a clean lip. Broad shelves stepping down one at a time is the
	/// shape the reference images actually have.
	///
	/// Iterated, because lowering one column can put its own neighbour over the
	/// limit; it converges quickly since every pass only ever lowers ground.
	/// </summary>
	public static void LimitDrop(short[] lev, int S, byte[] land, int maxDrop, int iterations = 8)
	{
		for (int it = 0; it < iterations; it++)
		{
			bool changed = false;
			for (int z = 0; z < S; z++)
			for (int x = 0; x < S; x++)
			{
				int i = z * S + x;
				if (land[i] == 0) continue;
				int lowest = int.MaxValue;
				for (int d = 0; d < 4; d++)
				{
					int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
					int nz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
					if (nx < 0 || nz < 0 || nx >= S || nz >= S) continue;
					lowest = Math.Min(lowest, lev[nz * S + nx]);
				}
				if (lowest == int.MaxValue) continue;
				if (lev[i] - lowest > maxDrop)
				{
					lev[i] = (short)(lowest + maxDrop);
					changed = true;
				}
			}
			if (!changed) break;
		}
	}

	/// <summary>How far this column stands above its lowest 4-neighbour: cliff height.</summary>
	public static int DropBelow(short[] lev, int S, int x, int z)
	{
		int At(int xx, int zz) => lev[Rng.ClampI(zz, 0, S - 1) * S + Rng.ClampI(xx, 0, S - 1)];
		int c = At(x, z);
		return c - Math.Min(Math.Min(At(x + 1, z), At(x - 1, z)),
		                    Math.Min(At(x, z + 1), At(x, z - 1)));
	}

	/// <summary>Height difference to the tallest 4-neighbour: at the foot of a cliff.</summary>
	public static int RiseAbove(short[] lev, int S, int x, int z)
	{
		int At(int xx, int zz) => lev[Rng.ClampI(zz, 0, S - 1) * S + Rng.ClampI(xx, 0, S - 1)];
		int c = At(x, z);
		return Math.Max(Math.Max(At(x + 1, z), At(x - 1, z)),
		                Math.Max(At(x, z + 1), At(x, z - 1))) - c;
	}
}
