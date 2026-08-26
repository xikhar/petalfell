using System;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// How a structure answers the slope it stands on — plan.md §11a.1.
///
/// The generator used to flatten a disc under every site and set boxes on it.
/// That is the exact inverse of what a real site does: a builder reads the fall
/// of the land and answers it, and the answer is the most characterful thing
/// about the building. Flattening throws away the only free source of variety
/// the world has, and it guarantees that every structure on the continent looks
/// placed rather than built.
///
/// So nothing gets flat ground. A structure gets a FOOTING: a floor level chosen
/// by minimising earth moved, a cut into the uphill side, a masonry plinth
/// carrying the downhill side, a bank of soil that time has piled against the
/// outside, and a guaranteed way in.
///
/// Filling is priced dearer than cutting on purpose. Both are legal answers to a
/// slope, but a building that sits INTO its hill reads as belonging to it, and a
/// building carried on a tall podium reads as imposed on it. The weight is the
/// whole difference between those two pictures.
/// </summary>
public sealed class Footing
{
	/// <summary>Footprint origin and extent, in world blocks.</summary>
	public int X0, Z0, W, D;

	/// <summary>Floor of the main terrace — the first solid block sits at Floor - 1.</summary>
	public int Floor;
	/// <summary>Floor of the second terrace. Equal to <see cref="Floor"/> when not split.</summary>
	public int FloorB;
	/// <summary>Did the fall of the land break the plan in two?</summary>
	public bool Split;
	/// <summary>Split axis: true when the two terraces divide across X, false across Z.</summary>
	public bool SplitAlongX;
	/// <summary>Footprint cell index at which terrace B begins, along the split axis.</summary>
	public int SplitAt;

	/// <summary>Terrain level under each footprint cell, before anything was moved.</summary>
	public int[] Ground;

	/// <summary>How deep the earth has banked against the outside of the walls.</summary>
	public int Silt;

	/// <summary>Which way the approach leaves the building. Always a cardinal.</summary>
	public int DoorX, DoorZ, DoorDx, DoorDz;

	/// <summary>Blocks cut out and filled in, for the boot diagnostics.</summary>
	public int Cut, Fill;

	/// <summary>Totals across the world build, so the boot log can show the pass working.</summary>
	public static int TotalCut, TotalFill, TotalSplit, TotalFitted, TotalRefused;

	public static void ResetCounters()
	{
		TotalCut = TotalFill = TotalSplit = TotalFitted = TotalRefused = 0;
	}

	/// <summary>Lowest and highest floor across the whole footing.</summary>
	public int FloorLo => Math.Min(Floor, FloorB);
	public int FloorHi => Math.Max(Floor, FloorB);

	/// <summary>Which terrace a footprint cell belongs to.</summary>
	public bool OnB(int dx, int dz) =>
		Split && (SplitAlongX ? dx >= SplitAt : dz >= SplitAt);

	/// <summary>The floor level a footprint cell stands at.</summary>
	public int FloorAt(int dx, int dz) => OnB(dx, dz) ? FloorB : Floor;

	/// <summary>
	/// The floor level nearest an arbitrary cell, footprint or not. The plinth
	/// ring and the talus live outside the footprint and still need to know what
	/// they are holding up.
	/// </summary>
	public int NearestFloor(int x, int z)
	{
		int dx = Rng.ClampI(x - X0, 0, W - 1);
		int dz = Rng.ClampI(z - Z0, 0, D - 1);
		return FloorAt(dx, dz);
	}

	/* ================================================================
	 * Fitting
	 * ================================================================ */

	/// <summary>
	/// Past this fall across the footprint the plan splits in two.
	///
	/// Two, which given <c>Terrain.Step == 2</c> means "the footprint straddles a
	/// terrace edge". At four it meant "straddles two", and the boot log said what
	/// that was worth: thirty-nine footings fitted and not one of them split. A
	/// split plan that never happens is a paragraph of design document.
	/// </summary>
	private const int SplitRelief = 2;
	/// <summary>Past this, no footing at all — the ground is too broken to build on.</summary>
	private const int MaxRelief = 9;
	/// <summary>
	/// The deepest earth can bank against a wall, however old the site.
	///
	/// Two, not three. A ruin's walls are frequently down to three or four
	/// courses, and burying three of them leaves a kerb rather than a building.
	/// </summary>
	private const int MaxSilt = 2;

	/// <summary>
	/// Read the land under a footprint and decide how the building answers it.
	/// Returns null when the ground refuses — water, the map edge, something
	/// already standing there, or a fall too steep for any sane structure.
	/// </summary>
	public static Footing Fit(Terrain terrain, int x0, int z0, int w, int d, float age)
	{
		int S = terrain.Size;
		if (x0 < 3 || z0 < 3 || x0 + w >= S - 3 || z0 + d >= S - 3) { TotalRefused++; return null; }

		var ground = new int[w * d];
		int lo = int.MaxValue, hi = int.MinValue;

		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			int i = (z0 + dz) * S + x0 + dx;
			if (terrain.Land[i] == 0) { TotalRefused++; return null; }
			// Something is already standing here — a bridge deck, another
			// building, a boulder. The footing may move earth but it may not
			// demolish.
			if (terrain.Grid.Heights[i] > terrain.Level[i]) { TotalRefused++; return null; }
			int g = terrain.Level[i];
			ground[dz * w + dx] = g;
			if (g < lo) lo = g;
			if (g > hi) hi = g;
		}

		if (hi - lo > MaxRelief || lo <= Terrain.Sea + 1) { TotalRefused++; return null; }
		TotalFitted++;

		var f = new Footing
		{
			X0 = x0, Z0 = z0, W = w, D = d, Ground = ground,
			Silt = Math.Min(MaxSilt, (int)MathF.Round(age * MaxSilt + 0.15f)),
		};

		if (hi - lo < SplitRelief)
		{
			f.Floor = f.FloorB = Best(ground, 0, ground.Length, lo, hi, out _);
			return f;
		}

		// Steep enough to split. The plan divides ACROSS the fall, so the step
		// between the two terraces runs along the contour — which is where a real
		// builder would put it, and what makes the break read as deliberate.
		f.Split = true;
		f.SplitAlongX = Fall(ground, w, d, true) >= Fall(ground, w, d, false);
		int n = f.SplitAlongX ? w : d;

		// WHERE it divides is searched, not assumed.
		//
		// Splitting down the middle only helps when the ground happens to break
		// in the middle, and it usually does not — so both halves still needed
		// cutting and the split bought nothing. Trying every reasonable division
		// and keeping the cheapest puts the internal step on the terrace edge,
		// which is the only place it means anything.
		var aCells = new int[ground.Length];
		var bCells = new int[ground.Length];
		float bestTotal = float.MaxValue;
		f.SplitAt = n / 2;

		for (int at = Math.Max(2, n / 4); at <= Math.Min(n - 2, n - n / 4); at++)
		{
			int aLo = int.MaxValue, aHi = int.MinValue, bLo = int.MaxValue, bHi = int.MinValue;
			int aCount = 0, bCount = 0;

			for (int dz = 0; dz < d; dz++)
			for (int dx = 0; dx < w; dx++)
			{
				int g = ground[dz * w + dx];
				if (f.SplitAlongX ? dx >= at : dz >= at)
				{
					bCells[bCount++] = g;
					if (g < bLo) bLo = g;
					if (g > bHi) bHi = g;
				}
				else
				{
					aCells[aCount++] = g;
					if (g < aLo) aLo = g;
					if (g > aHi) aHi = g;
				}
			}

			int fa = Best(aCells, 0, aCount, aLo, aHi, out float ca);
			int fb = Best(bCells, 0, bCount, bLo, bHi, out float cb);
			if (ca + cb >= bestTotal) continue;
			bestTotal = ca + cb;
			f.SplitAt = at;
			f.Floor = fa;
			f.FloorB = fb;
		}

		// A step nobody can climb is not a floor plan, it is two buildings. Two
		// blocks is the tallest riser the controller's auto-step will take without
		// a jump, and three is the tallest a stair inside one room should ever be.
		int step = f.FloorB - f.Floor;
		if (Math.Abs(step) > 3) f.FloorB = f.Floor + Math.Sign(step) * 3;
		if (f.Floor == f.FloorB) f.Split = false;
		return f;
	}

	/// <summary>Mean fall across one axis, used to orient the split.</summary>
	private static float Fall(int[] ground, int w, int d, bool alongX)
	{
		float first = 0f, last = 0f;
		int n = alongX ? w : d;
		int across = alongX ? d : w;
		for (int k = 0; k < across; k++)
		{
			first += alongX ? ground[k * w] : ground[k];
			last += alongX ? ground[k * w + n - 1] : ground[(n - 1) * w + k];
		}
		return MathF.Abs(last - first) / Math.Max(1, across);
	}

	/// <summary>
	/// The floor level that moves the least earth.
	///
	/// Fill is priced at 1.4 against cut at 1.0, so on an even slope the answer
	/// leans HIGH — into the hill, with the downhill side carried on masonry. See
	/// the class remark: this weight is the difference between a building that
	/// belongs to its hill and one standing on a podium.
	/// </summary>
	private static int Best(int[] cells, int from, int count, int lo, int hi, out float bestCost)
	{
		const float CutCost = 1.0f, FillCost = 1.4f;
		int best = lo;
		bestCost = float.MaxValue;

		for (int floor = lo; floor <= hi; floor++)
		{
			float cost = 0f;
			for (int k = from; k < from + count; k++)
			{
				int g = cells[k];
				cost += g > floor ? (g - floor) * CutCost : (floor - g) * FillCost;
			}
			// `<=` rather than `<`: on a tie the HIGHER floor wins, because a
			// building would rather stand a course proud than sink into a hollow.
			if (cost <= bestCost) { bestCost = cost; best = floor; }
		}
		return best;
	}

	/* ================================================================
	 * Application
	 * ================================================================ */

	/// <summary>
	/// Move the earth. Cuts the uphill side out, carries the downhill side on
	/// masonry, banks silt against the outside, and cuts a way in.
	///
	/// Three things have to stay in step here or downstream passes misbehave
	/// silently: the grid's blocks, the grid's per-column Heights (which the
	/// mesher, the vegetation apron and Landmarks.Clear all read), and the 2D
	/// Level field that ground detail, fauna and navigation read. A cut LOWERS
	/// Heights, which no other pass in this project does.
	/// </summary>
	public static void Apply(Terrain terrain, Footing f, Rng rng, byte plinth)
	{
		// The footprint plus one ring: the ring is the plinth face and the ledge
		// the talus banks against, and it is the part the player actually sees.
		for (int dz = -1; dz <= f.D; dz++)
		for (int dx = -1; dx <= f.W; dx++)
			Level(terrain, f, f.X0 + dx, f.Z0 + dz, f.NearestFloor(f.X0 + dx, f.Z0 + dz), plinth);

		Riser(terrain, f);
		Talus(terrain, f, rng);
		Ramp(terrain, f);

		TotalCut += f.Cut;
		TotalFill += f.Fill;
		if (f.Split) TotalSplit++;
	}

	/// <summary>
	/// A tread where the two terraces meet.
	///
	/// A split plan is only an improvement on a flat pad if the player can get
	/// between the halves. Three blocks is inside the fitter's clamp but outside
	/// what the controller will step up, so the boundary column takes an
	/// intermediate level and no riser is ever taller than two.
	/// </summary>
	private static void Riser(Terrain terrain, Footing f)
	{
		if (!f.Split) return;
		int diff = f.FloorB - f.Floor;
		if (Math.Abs(diff) < 2) return;

		int mid = f.Floor + diff / 2;
		int n = f.SplitAlongX ? f.D : f.W;
		for (int k = 0; k < n; k++)
		{
			int x = f.SplitAlongX ? f.X0 + f.SplitAt : f.X0 + k;
			int z = f.SplitAlongX ? f.Z0 + k : f.Z0 + f.SplitAt;
			Level(terrain, f, x, z, mid, Palette.STONE);
		}
	}

	/// <summary>Cut or fill one column to a target floor.</summary>
	private static void Level(Terrain terrain, Footing f, int x, int z, int floor, byte plinth)
	{
		int S = terrain.Size;
		if (x < 0 || z < 0 || x >= S || z >= S) return;
		int i = z * S + x;
		if (terrain.Land[i] == 0) return;

		var grid = terrain.Grid;
		int g = terrain.Level[i];
		if (g == floor) return;

		// The turf that was on top before anything moved. A cut bank that has been
		// open for generations is grown over, not raw earth — leaving the
		// derivation to supply the new surface exposes deep stone and the terrace
		// reads as a quarry.
		byte cap = grid.At(x, g - 1, z);

		if (g > floor)
		{
			// Cut. AIR is written explicitly rather than left to the derivation —
			// the sparse overlay stores it for exactly this case.
			for (int y = floor; y < g; y++) grid.Set(x, y, z, Palette.AIR);
			grid.Set(x, floor - 1, z, cap);
			f.Cut += g - floor;
		}
		else
		{
			// Fill, and the fill is MASONRY, not soil. This is the retaining wall
			// that carries the downhill side, and plan.md §11a.1 is emphatic that
			// it is architecture rather than a repair: it is the part of the
			// building seen first from below, and its shape is dictated entirely
			// by the slope, so it varies for free.
			for (int y = g; y < floor; y++) grid.Set(x, y, z, plinth);
			f.Fill += floor - g;
		}

		terrain.Level[i] = (short)floor;
		grid.Heights[i] = (short)floor;
	}

	/// <summary>
	/// The earth time has banked against the walls — plan.md §11a.2.
	///
	/// Deepest at the masonry, gone within a few blocks. It buries the lowest
	/// courses from OUTSIDE only: the interior floor is never touched, so the
	/// building gains age without becoming unwalkable. Cheapest age signal in the
	/// project by a distance.
	/// </summary>
	private static void Talus(Terrain terrain, Footing f, Rng rng)
	{
		if (f.Silt <= 0) return;
		int S = terrain.Size;
		int reach = f.Silt + 2;
		var grid = terrain.Grid;

		for (int dz = -reach; dz < f.D + reach; dz++)
		for (int dx = -reach; dx < f.W + reach; dx++)
		{
			// The interior is off limits — the floor stays walkable. The ring
			// immediately outside is NOT: that is the wall face, and it is where
			// the bank has to be deepest. The first version skipped it too, so the
			// deepest earth sat two blocks clear of the masonry and touched
			// nothing, which is the one place a bank cannot be.
			if (dx >= 0 && dz >= 0 && dx < f.W && dz < f.D) continue;

			int x = f.X0 + dx, z = f.Z0 + dz;
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			int i = z * S + x;
			if (terrain.Land[i] == 0) continue;
			if (grid.Heights[i] > terrain.Level[i]) continue;

			// Distance out from the wall face, in Chebyshev blocks.
			int out2 = Math.Max(
				Math.Max(-dx, dx - f.W + 1),
				Math.Max(-dz, dz - f.D + 1));
			if (out2 < 1 || out2 > reach) continue;

			// A hard taper reads as a moulding. The noise term is what makes the
			// bank look like something that drifted rather than something laid.
			float t = 1f - (out2 - 1) / (float)reach;
			int rise = (int)MathF.Round(f.Silt * t * t + rng.Bell() * 0.4f);
			if (rise <= 0) continue;

			int g = terrain.Level[i];
			int top = Math.Min(f.FloorHi + f.Silt, g + rise);
			if (top <= g) continue;

			for (int y = g; y < top; y++) grid.Set(x, y, z, Palette.SOIL);
			// The bank is grown over, not bare earth — it has been settling for
			// generations, not since last winter.
			grid.Set(x, top - 1, z, grid.At(x, g - 1, z));
			terrain.Level[i] = (short)top;
			grid.Heights[i] = (short)top;
		}
	}

	/// <summary>
	/// The way in — plan.md §11a.1, "nothing is ever sealed".
	///
	/// Wherever the floor stands above the land outside, the footing owes the
	/// player a way up. Without this the cut-and-fill above turns every ruin into
	/// a box with a wall around it, which is strictly worse than the flat pads it
	/// replaced.
	/// </summary>
	private static void Ramp(Terrain terrain, Footing f)
	{
		int S = terrain.Size;
		var grid = terrain.Grid;

		// The approach leaves by whichever side has the gentlest fall away from
		// the building — the side somebody would actually have walked up.
		int bestDx = 0, bestDz = -1;
		float bestDrop = float.MaxValue;
		int bestX = f.X0 + f.W / 2, bestZ = f.Z0;

		Span<int> dirs = stackalloc int[] { 0, -1, 0, 1, -1, 0, 1, 0 };
		for (int k = 0; k < 4; k++)
		{
			int dx = dirs[k * 2], dz = dirs[k * 2 + 1];
			int cx = f.X0 + f.W / 2 + (dx > 0 ? f.W - 1 : dx < 0 ? 0 : 0);
			int cz = f.Z0 + f.D / 2 + (dz > 0 ? f.D - 1 : dz < 0 ? 0 : 0);
			if (dx != 0) { cx = dx > 0 ? f.X0 + f.W - 1 : f.X0; cz = f.Z0 + f.D / 2; }
			else { cx = f.X0 + f.W / 2; cz = dz > 0 ? f.Z0 + f.D - 1 : f.Z0; }

			int floor = f.NearestFloor(cx, cz);
			// How far the land has fallen four blocks out.
			int ox = cx + dx * 4, oz = cz + dz * 4;
			if (ox < 1 || oz < 1 || ox >= S - 1 || oz >= S - 1) continue;
			int oi = oz * S + ox;
			if (terrain.Land[oi] == 0) continue;
			float drop = MathF.Abs(terrain.Level[oi] - floor);
			if (drop < bestDrop) { bestDrop = drop; bestDx = dx; bestDz = dz; bestX = cx; bestZ = cz; }
		}

		f.DoorX = bestX; f.DoorZ = bestZ; f.DoorDx = bestDx; f.DoorDz = bestDz;

		// Walk outward from the doorway, stepping the ground down one block at a
		// time until it meets the land. A run of at most one block per course is
		// what makes it climbable rather than merely present.
		int fromFloor = f.NearestFloor(bestX, bestZ);
		int width = 1;
		int px = -bestDz, pz = bestDx;   // perpendicular, for the tread's width

		int cur = fromFloor;
		for (int step = 1; step <= 10; step++)
		{
			int x = bestX + bestDx * step, z = bestZ + bestDz * step;
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) return;
			int i = z * S + x;
			if (terrain.Land[i] == 0) return;

			int natural = terrain.Level[i];
			if (natural == cur) return;                     // met the land, done
			cur += Math.Sign(natural - cur);                // one course per tread

			for (int side = -width; side <= width; side++)
			{
				int tx = x + px * side, tz = z + pz * side;
				if (tx < 1 || tz < 1 || tx >= S - 1 || tz >= S - 1) continue;
				int ti = tz * S + tx;
				if (terrain.Land[ti] == 0) continue;

				int g = terrain.Level[ti];
				if (g > cur)
					for (int y = cur; y < g; y++) grid.Set(tx, y, tz, Palette.AIR);
				else
					for (int y = g; y < cur; y++) grid.Set(tx, y, tz, Palette.PATH);

				grid.Set(tx, cur - 1, tz, Palette.PATH);
				terrain.Level[ti] = (short)cur;
				grid.Heights[ti] = (short)cur;
			}
		}
	}
}
