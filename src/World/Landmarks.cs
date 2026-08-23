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
			LandmarkForm.Farmstead => age < 0.78f && Flatness(6) > 78 && NearWater(26),

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
	/// One house on its own, with a fence and a well. The loneliest object in the
	/// world model and the one that most directly says a person lived here.
	/// </summary>
	private static void Farmstead(Terrain terrain, Rng rng, Landmark m)
	{
		int w = rng.RangeInt(5, 7), d = rng.RangeInt(5, 6);
		int x0 = m.X - w / 2, z0 = m.Z - d / 2;
		int S = terrain.Size;

		for (int dz = -1; dz <= d; dz++)
		for (int dx = -1; dx <= w; dx++)
			if (!Clear(terrain, x0 + dx, z0 + dz, out _)) return;

		int floor = m.Level;
		float decay = Rng.Clamp(0.25f + m.Age * 0.75f, 0f, 1f);
		byte roof = rng.Chance(0.5f) ? Palette.ROOF_SLATE : Palette.ROOF_TILE;
		int wallH = 4;

		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			int x = x0 + dx, z = z0 + dz;
			terrain.Grid.Set(x, floor - 1, z, decay > 0.6f ? Palette.MOSS : Palette.PAVING);
			bool edge = dx == 0 || dz == 0 || dx == w - 1 || dz == d - 1;
			if (!edge) continue;
			bool corner = (dx == 0 || dx == w - 1) && (dz == 0 || dz == d - 1);

			int side = dz == 0 ? 0 : dz == d - 1 ? 1 : dx == 0 ? 2 : 3;
			float along = side < 2 ? dx : dz;
			float wave = 0.5f + 0.5f * MathF.Sin(along * 0.85f + side * 1.7f);
			int standing = (int)MathF.Round(wallH * (1f - decay * (0.4f + wave * 0.85f)));
			if (corner) standing = Math.Max(standing, (int)(wallH * (1f - decay * 0.5f)));

			for (int k = 0; k < Math.Max(0, standing); k++)
				Put(terrain, x, floor + k, z, corner || k == 0 ? Palette.BEAM : Palette.PLASTER);
		}

		// Roof, if there is enough of the house left to carry one.
		if (decay < 0.55f)
			for (int dz = -1; dz <= d; dz++)
			for (int dx = -1; dx <= w; dx++)
				if (!rng.Chance(decay * 1.2f))
					Put(terrain, x0 + dx, floor + wallH, z0 + dz, roof);

		// The chimney outlives the house, as it does everywhere else.
		Put(terrain, x0 + 1, floor, z0 + 1, Palette.STONE_WARM);
		for (int k = 1; k <= wallH + 1 - (int)(decay * 2f); k++)
			Put(terrain, x0 + 1, floor + k, z0 + 1, Palette.STONE_WARM);
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
