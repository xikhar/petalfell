using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

public enum LandmarkForm : byte
{
	/// <summary>A stone shaft on high ground. Seen from far off, sees far off.</summary>
	Watchtower,
	/// <summary>A ring of cut monoliths. Says nothing and explains nothing.</summary>
	StandingStones,
	/// <summary>A small roadside or waterside shrine, still lit.</summary>
	Shrine,
	/// <summary>One house, alone. Somebody stayed out here by themselves.</summary>
	Farmstead,
	/// <summary>A heap of stones beside a route. The cheapest orientation there is.</summary>
	Cairn,
}

public sealed class Landmark
{
	public LandmarkForm Form;
	public int X, Z, Level;
	public uint Seed;
	/// <summary>How long this ground has been empty, sampled once at placement.</summary>
	public float Age;
	/// <summary>Worth routing a trail to. Cairns are not; a watchtower is.</summary>
	public bool Significant => Form != LandmarkForm.Cairn;
}

/// <summary>
/// The things worth walking to.
///
/// With no villages and no towns, landmarks carry the entire load of orientation,
/// pacing and reward — plan.md §13 promotes them from set dressing to the primary
/// content layer, and that promotion is the single largest gap between the plan
/// and the build. Four authored markers existed in the map package and nothing
/// consumed them.
///
/// Everything here is generated. Authored anchors go back in later on top of it;
/// what matters first is that a continent this size has enough on it that walking
/// in any direction finds something, because that is the whole experience.
///
/// Split into PLAN and BUILD like settlements are, and for the same reason: the
/// road router needs to know where these are before it can run trails to them,
/// and the builders need finished ground before they can put a block on it.
///
/// Every form here has to answer at least one of §13's three questions — tell me
/// where I am, tell me what happened here, or give me a reason to have walked
/// over. A shape that answers none of them is scenery, and scenery is already
/// handled by the vegetation pass.
/// </summary>
public static class Landmarks
{
	public static int LastBlockCount;

	/* ================================================================
	 * Planning
	 * ================================================================ */

	/// <summary>
	/// The landmarks worth routing a trail to. Planned BEFORE the roads, because
	/// the road network takes them as anchors — that is what turns a trail into a
	/// route to somewhere rather than a decorative squiggle.
	/// </summary>
	public static List<Landmark> PlanSignificant(Terrain terrain, List<SettlementSite> sites, int seed)
	{
		int S = terrain.Size;
		var rng = new Rng(seed ^ 0x1A9D3);
		var found = new List<Landmark>();
		float area = (S / 256f) * (S / 256f);

		// Counts scale with area, like every other feature count in this project.
		Scatter(terrain, sites, found, rng, LandmarkForm.Watchtower, (int)(0.10f * area));
		Scatter(terrain, sites, found, rng, LandmarkForm.StandingStones, (int)(0.13f * area));
		Scatter(terrain, sites, found, rng, LandmarkForm.Shrine, (int)(0.20f * area));
		Scatter(terrain, sites, found, rng, LandmarkForm.Farmstead, (int)(0.34f * area));
		return found;
	}

	/// <summary>
	/// Cairns, which have to come after the roads because their entire purpose is
	/// to sit beside one. They dominate the count by an order of magnitude: they
	/// are three blocks each and their job is to be frequent enough to navigate
	/// by on a continent with nothing else to steer on.
	/// </summary>
	public static void PlanCairns(Terrain terrain, List<SettlementSite> sites,
		List<Landmark> found, int seed)
	{
		float area = (terrain.Size / 256f) * (terrain.Size / 256f);
		Scatter(terrain, sites, found, new Rng(seed ^ 0x0CA12), LandmarkForm.Cairn,
			(int)(1.20f * area));
	}

	private static void Scatter(Terrain terrain, List<SettlementSite> sites,
		List<Landmark> found, Rng rng, LandmarkForm form, int want)
	{
		int S = terrain.Size;
		float spacing = form switch
		{
			LandmarkForm.Cairn => 26f,
			LandmarkForm.Farmstead => 70f,
			_ => 90f,
		};

		for (int attempt = 0; attempt < want * 40 && want > 0; attempt++)
		{
			int x = rng.RangeInt(10, S - 11);
			int z = rng.RangeInt(10, S - 11);
			int i = z * S + x;

			if (terrain.Land[i] == 0) continue;
			int level = terrain.Level[i];
			if (level <= Terrain.Sea + 1) continue;
			if (terrain.StairMask[i] != 0 || terrain.RockMask[i] != 0) continue;
			if (terrain.Plan.Definition.BoundaryDistance(x / (float)S, z / (float)S) > 0.93f) continue;

			float age = terrain.Plan.AbandonmentAt(x, z);
			if (!Suits(terrain, form, x, z, level, age)) continue;

			// Never inside a remnant — these are the things BETWEEN places.
			bool near = false;
			foreach (var s in sites)
			{
				float dx = s.X - x, dz = s.Z - z;
				float keep = s.Radius + 22f;
				if (dx * dx + dz * dz < keep * keep) { near = true; break; }
			}
			if (near) continue;

			foreach (var l in found)
			{
				float dx = l.X - x, dz = l.Z - z;
				float keep = l.Form == form ? spacing : 18f;
				if (dx * dx + dz * dz < keep * keep) { near = true; break; }
			}
			if (near) continue;

			found.Add(new Landmark
			{
				Form = form, X = x, Z = z, Level = level, Age = age,
				Seed = unchecked((uint)(rng.Next() * int.MaxValue)),
			});
			if (--want <= 0) return;
		}
	}

	/// <summary>Is this the right kind of ground for this kind of thing?</summary>
	private static bool Suits(Terrain terrain, LandmarkForm form, int x, int z, int level, float age)
	{
		int S = terrain.Size;

		int Flatness(int r)
		{
			int flat = 0, total = 0;
			for (int dz = -r; dz <= r; dz += 2)
			for (int dx = -r; dx <= r; dx += 2)
			{
				int xx = x + dx, zz = z + dz;
				if (xx < 0 || zz < 0 || xx >= S || zz >= S) continue;
				total++;
				int j = zz * S + xx;
				if (terrain.Land[j] != 0 && Math.Abs(terrain.Level[j] - level) <= Terrain.Step) flat++;
			}
			return total == 0 ? 0 : flat * 100 / total;
		}

		bool NearWater(int r)
		{
			for (int a = 0; a < 10; a++)
			{
				int xx = (int)(x + MathF.Cos(a / 10f * MathF.Tau) * r);
				int zz = (int)(z + MathF.Sin(a / 10f * MathF.Tau) * r);
				if (xx < 0 || zz < 0 || xx >= S || zz >= S) continue;
				if (terrain.Land[zz * S + xx] == 0) return true;
			}
			return false;
		}

		return form switch
		{
			// High ground, and genuinely high — a tower on a shelf sees nothing
			// and is seen from nowhere, which defeats both of its jobs.
			LandmarkForm.Watchtower => level >= Terrain.Base + Terrain.Step * 5 && Flatness(4) > 60,

			// Open ground, and old. Whoever raised these was gone long before
			// anyone alive; putting them in recently-worked country reads wrong.
			LandmarkForm.StandingStones => age > 0.55f && Flatness(7) > 82,

			// Where somebody would stop: a waterside, or a place with a view.
			LandmarkForm.Shrine => Flatness(3) > 70 &&
				(NearWater(9) || level >= Terrain.Base + Terrain.Step * 4),

			// Good ground somebody worked alone, and not the oldest country —
			// a farm that has entirely vanished is a monument, not a farmstead.
			//
			// The flatness bar came down from 78 because the builder changed under
			// it. That gate existed to protect a pass that could only set boxes on
			// level ground; the footing now reads the slope and answers it, so
			// insisting on level ground throws away every site where it would have
			// anything interesting to say. Sloped sites are the good ones now.
			LandmarkForm.Farmstead => age < 0.78f && Flatness(6) > 52 && NearWater(26),

			// Beside a route, on anything walkable.
			LandmarkForm.Cairn => terrain.Roads != null && NearRoad(terrain, x, z, 5),
			_ => false,
		};
	}

	private static bool NearRoad(Terrain terrain, int x, int z, int r)
	{
		int S = terrain.Size;
		for (int dz = -r; dz <= r; dz++)
		for (int dx = -r; dx <= r; dx++)
		{
			int xx = x + dx, zz = z + dz;
			if (xx < 0 || zz < 0 || xx >= S || zz >= S) continue;
			if (terrain.Roads.Mask[zz * S + xx] != 0) return true;
		}
		return false;
	}

	/* ================================================================
	 * Construction
	 * ================================================================ */

	public static void Build(Terrain terrain, List<Landmark> marks)
	{
		LastBlockCount = 0;
		if (marks == null) return;
		foreach (var m in marks)
		{
			var rng = new Rng(unchecked((int)m.Seed));
			switch (m.Form)
			{
				case LandmarkForm.Watchtower: Watchtower(terrain, rng, m); break;
				case LandmarkForm.StandingStones: StandingStones(terrain, rng, m); break;
				case LandmarkForm.Shrine: Shrine(terrain, rng, m); break;
				case LandmarkForm.Farmstead: Farmstead(terrain, rng, m); break;
				case LandmarkForm.Cairn: Cairn(terrain, rng, m); break;
			}

			// The farmstead runs its own pass — it is the only form that knows its
			// footprint, and its yard is much wider than its building.
			if (m.Form == LandmarkForm.Farmstead) continue;

			(int r, int h) = m.Form switch
			{
				LandmarkForm.Watchtower => (4, 21),
				LandmarkForm.StandingStones => (11, 8),
				LandmarkForm.Shrine => (3, 5),
				_ => (2, 5),
			};
			Reclaim.Overgrow(terrain, m.X - r, m.Z - r, r * 2 + 1, r * 2 + 1,
				m.Level - 1, m.Level + h, m.Age, m.Seed);
		}
	}

	private static bool Clear(Terrain terrain, int x, int z, out int y)
	{
		int S = terrain.Size;
		y = 0;
		if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) return false;
		int i = z * S + x;
		if (terrain.Land[i] == 0) return false;
		if (terrain.Grid.Heights[i] > terrain.Level[i]) return false;
		y = terrain.Level[i];
		return true;
	}

	private static void Put(Terrain terrain, int x, int y, int z, byte id)
	{
		var grid = terrain.Grid;
		if (!grid.InBounds(x, y, z)) return;
		grid.Set(x, y, z, id);
		LastBlockCount++;
		if (!Palette.IsSolid(id)) return;
		int i = z * terrain.Size + x;
		if (y + 1 > grid.Heights[i]) grid.Heights[i] = (short)(y + 1);
	}

	/// <summary>
	/// A stone shaft, broken off at a height that depends on how long it has
	/// stood untended. Tall enough to be a bearing from a long way off, which is
	/// the point of it — with no settlements on the horizon these are what the
	/// player navigates by.
	/// </summary>
	private static void Watchtower(Terrain terrain, Rng rng, Landmark m)
	{
		// Decay barely shortens it, and there is a hard floor.
		//
		// The obvious formula compounded into the exact opposite of the point.
		// Towers go on the highest ground; the highest ground is the oldest
		// abandonment; so age scaled the height down hardest precisely where the
		// towers are — and the first build produced three-block stumps. A
		// watchtower's entire job is to be seen from a long way off. It is allowed
		// to be broken; it is not allowed to be short.
		int full = rng.RangeInt(13, 18);
		int height = Math.Max(9, (int)(full * (1f - m.Age * 0.28f)));
		int r = 2;

		for (int dz = -r; dz <= r; dz++)
		for (int dx = -r; dx <= r; dx++)
		{
			bool edge = Math.Abs(dx) == r || Math.Abs(dz) == r;
			if (!edge) continue;
			int x = m.X + dx, z = m.Z + dz;
			if (!Clear(terrain, x, z, out int y)) continue;

			// The side facing the weather goes first, so a tower is never a clean
			// cylinder — one wall stands proud and the opposite one is a stump.
			float lean = 0.5f + 0.5f * MathF.Sin((dx * 0.9f + dz * 1.3f) + m.Seed % 7);
			int top = Math.Max(3, (int)(height * (1f - m.Age * 0.30f * lean)));
			for (int k = 0; k < top; k++)
			{
				// Timber courses banding the stone.
				//
				// Not decoration. Stone in this palette is pale lilac and so is
				// snow, scree and highland ground — the first towers were built
				// in exactly those provinces and came out the same value as the
				// hill they stood on, which is to say invisible. TRUNK is the
				// darkest thing the world allows, and a band of it every few
				// courses guarantees the silhouette reads against ANY ground.
				byte block = k % 5 == 4 ? Palette.TRUNK
					: k % 4 == 3 ? Palette.STONE_PALE : Palette.STONE;
				Put(terrain, x, y + k, z, block);
			}
		}

		// A timber cap, overhanging, only where it has not had long enough to rot
		// away. The overhang is what makes a shaft read as a tower rather than a
		// chimney.
		if (m.Age < 0.6f)
			for (int dz = -r - 1; dz <= r + 1; dz++)
			for (int dx = -r - 1; dx <= r + 1; dx++)
				if (Clear(terrain, m.X + dx, m.Z + dz, out int y))
					Put(terrain, m.X + dx, y + height, m.Z + dz,
						Math.Abs(dx) > r || Math.Abs(dz) > r ? Palette.TRUNK : Palette.PLANK);
	}

	/// <summary>
	/// A ring of monoliths. Deliberately the one form here that explains nothing:
	/// §13 asks each landmark to say where you are, what happened, or why you
	/// walked over, and "what happened" is allowed to remain a question.
	/// </summary>
	private static void StandingStones(Terrain terrain, Rng rng, Landmark m)
	{
		int count = rng.RangeInt(6, 10);
		float radius = rng.Range(5f, 9f);

		for (int k = 0; k < count; k++)
		{
			float a = MathF.Tau * k / count + rng.Bell() * 0.12f;
			int x = (int)(m.X + MathF.Cos(a) * radius);
			int z = (int)(m.Z + MathF.Sin(a) * radius);
			if (!Clear(terrain, x, z, out int y)) continue;

			// One in five has come down. A complete ring reads as decoration; a
			// ring with a gap in it reads as something that has been here a while.
			if (rng.Chance(0.2f))
			{
				Put(terrain, x, y, z, Palette.STONE_PALE);
				continue;
			}
			int h = rng.RangeInt(3, 6);
			for (int i = 0; i < h; i++)
				Put(terrain, x, y + i, z, i == h - 1 ? Palette.STONE_PALE : Palette.STONE);
		}
	}

	/// <summary>A small shrine, and one of the few lights left in the wilds.</summary>
	private static void Shrine(Terrain terrain, Rng rng, Landmark m)
	{
		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
			if (Clear(terrain, m.X + dx, m.Z + dz, out int y))
				terrain.Grid.Set(m.X + dx, y - 1, m.Z + dz, Palette.PAVING);

		if (!Clear(terrain, m.X, m.Z, out int cy)) return;
		Put(terrain, m.X, cy, m.Z, Palette.STONE);
		Put(terrain, m.X, cy + 1, m.Z, Palette.STONE_PALE);
		// Still burning. Somebody, at some point, is keeping these going — which
		// is a question the world can answer later, and a beacon in the meantime.
		Put(terrain, m.X, cy + 2, m.Z, m.Age > 0.85f ? Palette.STONE_PALE : Palette.CRYSTAL);

		foreach (int side in new[] { -2, 2 })
		{
			if (Clear(terrain, m.X + side, m.Z, out int y1))
				for (int k = 0; k < 2; k++) Put(terrain, m.X + side, y1 + k, m.Z, Palette.TRUNK);
		}
	}

	/// <summary>
	/// A hall standing in its own yard — the loneliest object in the world model
	/// and the one that most directly says a person lived here.
	///
	/// This is the form that carries plan.md §11a, and it is built as a SEQUENCE
	/// rather than as an object: a gate in a boundary wall, a worn path across a
	/// yard, a doorway, one room, a step, another room. Every one of those is
	/// somewhere the player physically goes, which is the whole difference
	/// between a ruin and a model of a ruin. A single box on a pad, however well
	/// weathered, is still something you walk around.
	///
	/// The ground decides the plan, not the other way round. The footing picks
	/// the floor and may split it across two terraces; the hall then goes in the
	/// corner FURTHEST from the gate, so arriving always means crossing the yard.
	/// </summary>
	private static void Farmstead(Terrain terrain, Rng rng, Landmark m)
	{
		// The footing fits the HALL, and nothing else.
		//
		// The first build fitted it to the whole yard, which levelled a thirteen
		// by eleven rectangle and produced exactly the stone podium this entire
		// section exists to abolish — the pancake, rebuilt in masonry. Only a
		// FLOOR needs to be level. A yard is a yard: it follows the land, its
		// wall steps down with the ground the way a dry-stone wall does, and the
		// path across it climbs. That contrast, between one deliberately level
		// thing and everything around it staying crooked, is what makes the level
		// thing read as built.
		int hw = rng.RangeInt(7, 9), hd = rng.RangeInt(6, 7);
		int hx = m.X - hw / 2, hz = m.Z - hd / 2;

		var f = Footing.Fit(terrain, hx, hz, hw, hd, m.Age);
		if (f == null) return;

		float decay = Rng.Clamp(0.25f + m.Age * 0.75f, 0f, 1f);
		Footing.Apply(terrain, f, rng, Palette.STONE);

		int wallH = rng.RangeInt(5, 6);
		byte roof = rng.Chance(0.5f) ? Palette.ROOF_SLATE : Palette.ROOF_TILE;

		// The footing already chose which way somebody walks in, and cut the ramp
		// for it. The door goes there; the yard opens that way.
		int doorX = f.DoorX, doorZ = f.DoorZ;

		// The yard reaches out on the approach side, so arriving means crossing
		// it rather than stepping straight through the gate into the wall.
		int run = rng.RangeInt(6, 9);
		Span(hx, hw, f.DoorDx, run, out int yx, out int yw);
		Span(hz, hd, f.DoorDz, run, out int yz, out int yd);

		int gateX = f.DoorDx > 0 ? yx + yw - 1 : f.DoorDx < 0 ? yx : doorX;
		int gateZ = f.DoorDz > 0 ? yz + yd - 1 : f.DoorDz < 0 ? yz : doorZ;

		Boundary(terrain, yx, yz, yw, yd, gateX, gateZ, decay);
		YardPath(terrain, gateX, gateZ, doorX, doorZ);
		Hall(terrain, rng, f, hx, hz, hw, hd, wallH, decay, roof, doorX, doorZ);

		// The chimney outlives the house, as it does everywhere else.
		int cx = hx + (f.DoorDx > 0 ? 1 : hw - 2);
		int cz = hz + (f.DoorDz > 0 ? 1 : hd - 2);
		int cf = f.NearestFloor(cx, cz);
		for (int k = 0; k <= wallH + 2 - (int)(decay * 2f); k++)
			Put(terrain, cx, cf + k, cz, Palette.STONE_WARM);

		// A trough in the yard. One object of obvious daily use is worth more
		// than any amount of broken wall for saying somebody LIVED here.
		int tx = gateX - f.DoorDx * 3, tz = gateZ - f.DoorDz * 3;
		if (tx < hx - 1 || tx > hx + hw || tz < hz - 1 || tz > hz + hd)
			for (int k = -1; k <= 1; k++)
			{
				int px = f.DoorDz != 0 ? tx + k : tx;
				int pz = f.DoorDz != 0 ? tz : tz + k;
				if (Clear(terrain, px, pz, out int ty))
					Put(terrain, px, ty, pz, k == 0 ? Palette.RUBBLE : Palette.STONE_PALE);
			}

		// And then the land takes it back. The volume is the yard, not the hall:
		// growth spilling off the building onto the ground beside it is the whole
		// point of plan.md §11a.5.
		int lo = int.MaxValue;
		foreach (int g in f.Ground) if (g < lo) lo = g;
		Reclaim.Overgrow(terrain, yx, yz, yw, yd, lo - 1, f.FloorHi + wallH + 3,
			m.Age, m.Seed);
	}

	/// <summary>
	/// One axis of the yard: a margin all round, and the rest of the run thrown
	/// out on whichever side the approach comes from.
	/// </summary>
	private static void Span(int origin, int size, int dir, int run,
		out int start, out int length)
	{
		const int Margin = 2;
		length = size + Margin * 2 + (dir == 0 ? 0 : run);
		start = origin - Margin - (dir < 0 ? run : 0);
	}

	/// <summary>
	/// The yard wall — low, breached, following the ground, and the thing that
	/// turns a building into a PLACE by giving it an inside and an outside.
	/// </summary>
	private static void Boundary(Terrain terrain, int x0, int z0, int w, int d,
		int gateX, int gateZ, float decay)
	{
		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			if (dx != 0 && dz != 0 && dx != w - 1 && dz != d - 1) continue;
			int x = x0 + dx, z = z0 + dz;

			// The gateway, three wide, so it reads as an opening rather than as
			// one more gap the wall has lost.
			if (Math.Abs(x - gateX) + Math.Abs(z - gateZ) <= 1) continue;
			// No levelling: the wall takes the ground as it finds it.
			if (!Clear(terrain, x, z, out int y)) continue;

			// Sections, never block by block.
			//
			// The rule was already written down and the first build broke it
			// anyway: at 0.62 radians per block the wave turned over every five
			// blocks, which at this scale IS block by block, and the wall came
			// out as confetti. A wall fails in LENGTHS. The slow term sets the
			// length; the fast one only roughens its ends.
			int side = dz == 0 ? 0 : dz == d - 1 ? 1 : dx == 0 ? 2 : 3;
			float along = side < 2 ? dx : dz;
			float wave = 0.5f + 0.35f * MathF.Sin(along * 0.24f + side * 2.1f)
			                  + 0.15f * MathF.Sin(along * 0.77f + side * 1.1f);
			int standing = (int)MathF.Round(3f * (1f - decay * (0.20f + wave * 0.85f)));
			for (int k = 0; k < standing; k++)
				Put(terrain, x, y + k, z, k == 0 ? Palette.STONE : Palette.STONE_PALE);
		}
	}

	/// <summary>The line the feet wore between the gate and the door.</summary>
	private static void YardPath(Terrain terrain, int fromX, int fromZ, int toX, int toZ)
	{
		int x = fromX, z = fromZ;
		for (int step = 0; step < 48 && (x != toX || z != toZ); step++)
		{
			if (x != toX) x += Math.Sign(toX - x);
			else z += Math.Sign(toZ - z);
			if (!Clear(terrain, x, z, out int y)) continue;
			terrain.Grid.Set(x, y - 1, z, Palette.PATH);
		}
	}

	/// <summary>
	/// The hall itself: two rooms where the ground gave enough width for two, a
	/// doorway that is always open, and windows that are holes.
	/// </summary>
	private static void Hall(Terrain terrain, Rng rng, Footing f, int hx, int hz,
		int hw, int hd, int wallH, float decay, byte roof, int doorX, int doorZ)
	{
		// A divider across the longer axis, so the second space is a room rather
		// than a corridor. It gets a gap, because a sealed room is a texture.
		bool divideX = hw >= hd;
		int divide = divideX ? hx + hw / 2 : hz + hd / 2;
		bool twoRooms = (divideX ? hw : hd) >= 7;
		int gap = divideX ? hz + hd / 2 : hx + hw / 2;

		// How much wall survived, kept so the roof can ask what is left to rest
		// on. Without it the roof pass puts slabs over open air.
		var standingAt = new int[hw * hd];

		for (int dz = 0; dz < hd; dz++)
		for (int dx = 0; dx < hw; dx++)
		{
			int x = hx + dx, z = hz + dz;
			int floor = f.NearestFloor(x, z);
			terrain.Grid.Set(x, floor - 1, z, Palette.PAVING);

			bool perimeter = dx == 0 || dz == 0 || dx == hw - 1 || dz == hd - 1;
			bool inner = twoRooms && (divideX ? x == divide : z == divide) && !perimeter;
			if (!perimeter && !inner) continue;
			// The inner doorway.
			if (inner && (divideX ? z : x) == gap) continue;

			bool corner = (dx == 0 || dx == hw - 1) && (dz == 0 || dz == hd - 1);

			int side = dz == 0 ? 0 : dz == hd - 1 ? 1 : dx == 0 ? 2 : 3;
			float along = side < 2 ? dx : dz;
			// Slow section, fast roughening — see Boundary for why the fast term
			// cannot be the one carrying the shape.
			float wave = 0.5f + 0.35f * MathF.Sin(along * 0.30f + side * 1.7f)
			                  + 0.15f * MathF.Sin(along * 0.95f + side * 2.6f);
			int standing = (int)MathF.Round(wallH * (1f - decay * (0.15f + wave * 0.70f)));
			// Corners are the last thing to go, everywhere in this project. A ruin
			// whose corners fell reads as demolished rather than abandoned.
			if (corner) standing = Math.Max(standing, (int)(wallH * (1f - decay * 0.35f)));
			if (inner) standing = Math.Min(standing, wallH - 1);
			standing = Math.Max(0, standing);
			standingAt[dz * hw + dx] = standing;

			for (int k = 0; k < standing; k++)
			{
				// The doorway, always two clear. Whatever the decay did to this
				// section, the way in survives it — plan.md §11a.1.
				if (perimeter && k < 2 && x == doorX && z == doorZ) continue;
				// Windows are holes at head height, on the long walls only.
				if (perimeter && !corner && k == 2 && along % 3 == 1 && side >= 2) continue;

				// A timber FRAME, not a timber stripe — and one that rots.
				//
				// Beam on the whole bottom course put a band of the most saturated
				// colour in the world around every hall at ankle height. Posts at
				// the corners and every fourth bay is what half-timbering actually
				// is, and it leaves the plaster panels reading as panels.
				//
				// On an old site the posts are simply GONE. plan.md §11a.4 puts
				// timber first in the order of things to fail, and a ruin whose
				// frame outlasted its masonry has the succession backwards.
				bool post = corner || (int)along % 4 == 0;
				byte block = inner ? Palette.PLANK
					: post ? (decay < 0.5f ? Palette.BEAM : Palette.STONE)
					: Palette.PLASTER;
				Put(terrain, x, floor + k, z, block);
			}
		}

		// Roof.
		//
		// Only over ground the walls can still carry. The first version tested
		// decay alone and then dropped blocks on a per-cell coin flip, which put
		// pale slabs floating in mid air over rooms whose walls had gone — the
		// single most conspicuous artefact in the first capture. A roof is held
		// UP by something; if the something is not there, neither is the roof.
		//
		// What it covers stays clear of thicket, which is what makes the unroofed
		// room read as further gone than the roofed one.
		if (decay >= 0.62f) return;
		// A ridge, so the roof is a ROOF. One flat course at wall height is a lid,
		// and a lid is the single thing most likely to make a generated building
		// read as a box with a box on it. Two courses is all this project has ever
		// needed — the cottage pass reached the same answer twice.
		int ridge = divideX ? hz + hd / 2 : hx + hw / 2;

		for (int dz = -1; dz <= hd; dz++)
		for (int dx = -1; dx <= hw; dx++)
		{
			int cx = Rng.ClampI(dx, 0, hw - 1), cz = Rng.ClampI(dz, 0, hd - 1);
			int bearing = 0;
			if (standingAt[cz * hw + 0] >= wallH - 1) bearing++;
			if (standingAt[cz * hw + hw - 1] >= wallH - 1) bearing++;
			if (standingAt[0 * hw + cx] >= wallH - 1) bearing++;
			if (standingAt[(hd - 1) * hw + cx] >= wallH - 1) bearing++;
			if (bearing < 2) continue;

			// Roofs fail in PATCHES, not per block — a hole in a roof is a hole,
			// not a sieve.
			float hole = 0.5f + 0.5f * MathF.Sin(dx * 0.55f + dz * 0.9f + hx * 0.13f);
			if (hole < decay * 1.15f) continue;

			int x = hx + dx, z = hz + dz;
			bool eave = dx < 0 || dz < 0 || dx >= hw || dz >= hd;
			int lift = !eave && Math.Abs((divideX ? z : x) - ridge) <= 1 ? 1 : 0;
			Put(terrain, x, f.NearestFloor(x, z) + wallH + lift, z, roof);
		}
	}

	/// <summary>Three stones on a route. Cheap, numerous, and how you know you are on one.</summary>
	private static void Cairn(Terrain terrain, Rng rng, Landmark m)
	{
		if (!Clear(terrain, m.X, m.Z, out int y)) return;
		int h = rng.RangeInt(2, 4);
		for (int k = 0; k < h; k++)
			Put(terrain, m.X, y + k, m.Z, k == h - 1 ? Palette.STONE_PALE : Palette.STONE);
		if (rng.Chance(0.5f) && Clear(terrain, m.X + 1, m.Z, out int y2))
			Put(terrain, m.X + 1, y2, m.Z, Palette.STONE);
	}
}
