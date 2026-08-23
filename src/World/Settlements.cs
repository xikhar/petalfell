using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>How big the place was when people still lived in it.</summary>
public enum SettlementKind : byte { Hamlet, Village, Town }

/// <summary>
/// How far gone it is now.
///
/// Kept as a SECOND axis rather than replacing size, which is a deviation from
/// the first sketch in ARCHITECTURE.md §4a and a better one: a hamlet that is
/// merely shuttered and a town reduced to its foundations are entirely
/// different places, and collapsing the two axes throws that away. Four sizes
/// against four states gives sixteen kinds of location out of one generator.
/// </summary>
public enum RemnantState : byte
{
	/// <summary>Two or three people still live here. Rare, and always an event.</summary>
	Holdout,
	/// <summary>Nobody home. Shuttered, roofs on, doors closed, valuables gone.</summary>
	Remnant,
	/// <summary>Roofs open, walls partial, the land coming back in.</summary>
	Ruin,
	/// <summary>Only what was cut from stone is left standing.</summary>
	Monument,
}

public sealed class SettlementSite
{
	public string Id = "";
	public int X, Z;
	/// <summary>Terrain level of the chosen centre column.</summary>
	public int Level;
	public float Radius;
	public SettlementKind Kind;
	public RemnantState State;
	public Biome Biome;
	public bool Authored;
	public uint Seed;
	public float Score;
	public readonly List<(int x, int z, int w, int d)> Buildings = new();
}

/// <summary>
/// Where people live, and what they built.
///
/// Split in two on purpose. SITES are chosen before roads exist, because roads
/// are the thing that connects settlements and cannot be routed until it knows
/// where they are. BUILDINGS go up after the voxels are filled, because a house
/// needs to know what the ground actually turned out to be.
///
/// Site choice is a straight weighted search rather than anything clever, and
/// the weights encode why anyone would settle somewhere: flat ground to build
/// on, fresh water within walking distance, a province that will grow something,
/// and enough distance from the next place along that the two do not merge into
/// one continuous ribbon of houses. The last one matters more than it sounds —
/// without a separation floor the scoring piles every settlement onto the single
/// best meadow, because the terms that make ground good are all smooth fields
/// and their maxima are broad.
/// </summary>
public static class Settlements
{
	/// <summary>Shortest signed distance between two bearings, in radians.</summary>
	private static float AngleDelta(float from, float to)
	{
		float d = (to - from) % MathF.Tau;
		if (d > MathF.PI) d -= MathF.Tau;
		if (d < -MathF.PI) d += MathF.Tau;
		return d;
	}

	private static float BiomeWeight(Biome b) => b switch
	{
		Biome.Meadow => 1.00f,
		Biome.Plains => 0.94f,
		Biome.Shore => 0.86f,
		Biome.Forest => 0.78f,
		Biome.Sakura => 0.70f,
		Biome.Highland => 0.42f,
		Biome.Wetland => 0.30f,
		Biome.SnowyHills => 0.24f,
		_ => 0.5f,
	};

	public static List<SettlementSite> PlanSites(Terrain terrain, int seed)
	{
		int S = terrain.Size;
		var sites = new List<SettlementSite>();
		var def = terrain.Plan.Definition;

		// Authored settlements are chapter geography and are not negotiable. They
		// go in before the search so procedural sites keep their distance.
		foreach (var marker in def.Settlements)
		{
			int x = Rng.ClampI((int)(marker.Centre.X * S), 6, S - 7);
			int z = Rng.ClampI((int)(marker.Centre.Z * S), 6, S - 7);
			sites.Add(new SettlementSite
			{
				Id = marker.Id,
				X = x, Z = z,
				Level = terrain.Level[z * S + x],
				Radius = MathF.Max(marker.Radius * S, 14f),
				Kind = SettlementKind.Town,
				Biome = terrain.Plan.RegionAt(x, z).Biome,
				Authored = true,
				Seed = unchecked((uint)(seed ^ Rng.StableHash(marker.Id))),
			});
		}

		// FEWER settlements than the area would suggest, and much further apart.
		//
		// Scaling the count linearly with area keeps the density of the old small
		// map, so a world ten times the size is just the same village-every-two-
		// minutes experience for ten times as long. Growing the count far more
		// slowly than the area is what buys wilderness: each settlement ends up
		// with several times as much country around it, which is where landmarks,
		// ruins and everything else worth walking to have to live.
		float area = (S / 256f) * (S / 256f);
		int want = sites.Count + Math.Max(5, (int)MathF.Round(0.36f * area));
		float spacing = MathF.Max(74f, S * 0.055f);

		var candidates = new List<SettlementSite>();
		const int Step = 6;
		for (int z = 12; z < S - 12; z += Step)
		for (int x = 12; x < S - 12; x += Step)
		{
			int i = z * S + x;
			if (terrain.Land[i] == 0) continue;
			int level = terrain.Level[i];
			if (level <= Terrain.Sea + 1) continue;
			if (terrain.StairMask[i] != 0 || terrain.RockMask[i] != 0) continue;
			if (def.BoundaryDistance(x / (float)S, z / (float)S) > 0.86f) continue;

			// Flatness. A settlement wants a shelf, and the cheapest honest test
			// for one is how much of the neighbourhood shares this column's level.
			int flat = 0, total = 0;
			const int R = 7;
			for (int dz = -R; dz <= R; dz += 2)
			for (int dx = -R; dx <= R; dx += 2)
			{
				int xx = x + dx, zz = z + dz;
				if (xx < 0 || zz < 0 || xx >= S || zz >= S) continue;
				int j = zz * S + xx;
				total++;
				if (terrain.Land[j] != 0 && Math.Abs(terrain.Level[j] - level) <= Terrain.Step) flat++;
			}
			if (total == 0) continue;
			float flatness = flat / (float)total;
			if (flatness < 0.72f) continue;

			// Fresh water in walking distance, without being in it.
			float water = 1f;
			for (int r = 6; r <= 42; r += 6)
			{
				bool found = false;
				for (int a = 0; a < 12 && !found; a++)
				{
					float ang = a / 12f * MathF.Tau;
					int xx = (int)(x + MathF.Cos(ang) * r), zz = (int)(z + MathF.Sin(ang) * r);
					if (xx < 0 || zz < 0 || xx >= S || zz >= S) continue;
					if (terrain.Land[zz * S + xx] == 0) found = true;
				}
				if (!found) continue;
				water = 1f - Rng.Smoothstep(6f, 42f, r);
				break;
			}

			var biome = terrain.Plan.RegionAt(x, z).Biome;
			candidates.Add(new SettlementSite
			{
				X = x, Z = z, Level = level, Biome = biome,
				Score = flatness * 1.15f + water * 0.55f + BiomeWeight(biome),
			});
		}

		candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

		// Quotas by province, rather than one global ranking.
		//
		// Ranking the whole map at once put every settlement in the same place —
		// not literally, but every one of them landed in the meadow-and-plains
		// belt, because the terms that make ground good to build on are smooth
		// fields whose maxima all sit in the same kind of country. The result was
		// a chapter with a dozen villages in one biome and none at all in the
		// forest, the blossom or the snow, which throws away the whole point of
		// having provinces: a settlement is supposed to be one of the ways you
		// TELL which one you are in.
		//
		// So each province gets a share of the settlements proportional to how
		// much habitable ground it actually has, weighted by how willingly people
		// would live there. A forest gets fewer than a meadow of the same size,
		// and the snowy hills get very few — but they do not get none.
		var byBiome = new Dictionary<Biome, List<SettlementSite>>();
		foreach (var c in candidates)
		{
			if (!byBiome.TryGetValue(c.Biome, out var list))
				byBiome[c.Biome] = list = new List<SettlementSite>();
			list.Add(c);
		}

		float totalShare = 0f;
		var shares = new Dictionary<Biome, float>();
		foreach (var (biome, list) in byBiome)
		{
			float share = list.Count * BiomeWeight(biome);
			shares[biome] = share;
			totalShare += share;
		}

		int budget = want - sites.Count;
		var quota = new Dictionary<Biome, int>();
		foreach (var (biome, share) in shares)
			quota[biome] = totalShare > 0f
				? Math.Max(1, (int)MathF.Round(budget * share / totalShare))
				: 0;

		bool Crowded(SettlementSite c)
		{
			foreach (var s in sites)
			{
				float dx = s.X - c.X, dz = s.Z - c.Z;
				if (dx * dx + dz * dz < spacing * spacing) return true;
			}
			return false;
		}

		void Take(SettlementSite c)
		{
			c.Id = $"settlement_{sites.Count:00}";
			c.Seed = unchecked((uint)(seed * 31 + sites.Count * 2654435761u));
			sites.Add(c);
		}

		// Filled in a FIXED province order, never in dictionary order.
		//
		// Belt and braces rather than a fix: the run-to-run variation that sent me
		// looking here turned out to be string.GetHashCode() upstream, not this.
		// But the order settlements are taken in decides the seed each one is
		// given, so leaving that resting on a hash table's enumeration would be
		// leaving a second way for the same bug to come back.
		foreach (Biome biome in Enum.GetValues<Biome>())
		{
			if (!byBiome.TryGetValue(biome, out var list)) continue;
			int taken = 0;
			foreach (var c in list)
			{
				if (taken >= quota[biome] || sites.Count >= want) break;
				if (Crowded(c)) continue;
				Take(c);
				taken++;
			}
		}

		// Whatever the rounding left over goes to the best remaining ground
		// anywhere, so a map never comes up short because of quota arithmetic.
		foreach (var c in candidates)
		{
			if (sites.Count >= want) break;
			if (sites.Contains(c) || Crowded(c)) continue;
			Take(c);
		}

		// STATE follows the retreat, size follows standing. The two are sampled
		// independently on purpose — where a place was worth building has nothing
		// to do with how long ago it was given up, and letting them correlate
		// would put every ruin in bad country and every holdout in good, which
		// reads as a rule rather than as a history.
		//
		// Holdouts are capped hard. plan.md §2.2: meeting a person has to stay an
		// event, and the cheapest way to guarantee that is to refuse to generate
		// more than a handful however inviting the coast looks.
		// Assigned by RANK, not by absolute threshold.
		//
		// Fixed cut-offs looked right against the field's distribution across the
		// whole map and were badly wrong against the sites: settlement siting
		// rejects the outer rim, so every site sits inland of the coastal fringe
		// and skews old. On the first run that produced one holdout and two
		// remnants out of sixty-eight — the entire "still standing" half of the
		// world's vocabulary, missing. Ranking is immune to the field shifting
		// under it, which it will do again the moment the retreat is retuned.
		var byAge = new List<SettlementSite>(sites);
		byAge.Sort((a, b) => terrain.Plan.AbandonmentAt(a.X, a.Z)
			.CompareTo(terrain.Plan.AbandonmentAt(b.X, b.Z)));
		for (int i = 0; i < byAge.Count; i++)
		{
			float q = (i + 0.5f) / byAge.Count;
			byAge[i].State = q < 0.06f ? RemnantState.Holdout
				: q < 0.26f ? RemnantState.Remnant
				: q < 0.70f ? RemnantState.Ruin
				: RemnantState.Monument;
		}

		// Size follows standing: the best-scoring places grew into towns.
		var ranked = new List<SettlementSite>(sites);
		ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
		for (int i = 0; i < ranked.Count; i++)
		{
			if (ranked[i].Authored) continue;
			ranked[i].Kind = i < ranked.Count * 0.18f ? SettlementKind.Town
				: i < ranked.Count * 0.60f ? SettlementKind.Village
				: SettlementKind.Hamlet;
			ranked[i].Radius = ranked[i].Kind switch
			{
				// Big enough to HOLD a plan.
				//
				// The lot band runs from just outside the square to just inside
				// the wall, and at the old radii that band was empty or one deep —
				// a village asked for twelve houses and the geometry had room for
				// none, which is why a map of sixty-eight settlements produced a
				// hundred buildings between them.
				SettlementKind.Town => 36f,
				SettlementKind.Village => 26f,
				_ => 14f,
			};
		}
		return sites;
	}

	/* ================================================================
	 * Construction
	 * ================================================================ */

	/// <summary>
	/// Cut each settlement a level platform to stand on, before roads are routed.
	///
	/// This is the difference between a town and a scatter of buildings. Left on
	/// raw terrain every cottage levelled its own little pad, so a village was a
	/// dozen unrelated plinths at a dozen different heights with the ground
	/// rippling between them — there was no ground the town SAT on, and nothing
	/// for a square, a street or a wall to be flat against.
	///
	/// Deliberately placed here, between site choice and road routing, because
	/// the router costs climbing heavily: flatten first and the roads arrive onto
	/// the platform and run along it, instead of the town being dropped onto
	/// whatever the roads had already decided to cross.
	///
	/// Only ground within one terrace of the target is taken. A settlement that
	/// happens to back onto a cliff keeps its cliff — the platform stops at the
	/// foot of it rather than eating the landscape to make itself round.
	/// </summary>
	public static void TerraceSites(Terrain terrain)
	{
		int S = terrain.Size;
		foreach (var site in terrain.Sites)
		{
			int target = terrain.Level[site.Z * S + site.X];
			float r = site.Radius + 5f;
			int ri = (int)MathF.Ceiling(r);

			for (int dz = -ri; dz <= ri; dz++)
			for (int dx = -ri; dx <= ri; dx++)
			{
				int x = site.X + dx, z = site.Z + dz;
				if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) continue;
				if (dx * dx + dz * dz > r * r) continue;
				int i = z * S + x;
				if (terrain.Land[i] == 0) continue;
				if (Math.Abs(terrain.Level[i] - target) > Terrain.Step * 2) continue;
				terrain.Level[i] = (short)target;
			}
			site.Level = target;
		}

		for (int i = 0; i < S * S; i++)
			terrain.Land[i] = (byte)(terrain.Level[i] > Terrain.Sea ? 1 : 0);
	}

	/// <summary>Building count from the last run, for the boot diagnostics.</summary>
	public static int LastBuildingCount;

	public static void Build(Terrain terrain, List<SettlementSite> sites, int seed)
	{
		LastBuildingCount = 0;
		if (sites == null) return;
		foreach (var site in sites) BuildOne(terrain, site);
	}

	/// <summary>Radius of the paved square at the middle of anywhere bigger than a hamlet.</summary>
	private static float SquareRadius(SettlementSite site)
	{
		if (site.State == RemnantState.Monument) return 0f;
		// Smaller than the first pass. A square wants to be enclosed by the
		// buildings around it; at eight blocks the ring of cottages was too far
		// out to read as edges of a space and the middle was simply a paved field.
		return site.Kind switch
		{
			SettlementKind.Town => 6f,
			SettlementKind.Village => 5f,
			_ => 0f,
		};
	}

	/// <summary>0 for a maintained place, 1 for one the land has entirely taken.</summary>
	private static float Decay(SettlementSite site) => site.State switch
	{
		RemnantState.Holdout => 0f,
		RemnantState.Remnant => 0.25f,
		RemnantState.Ruin => 0.65f,
		_ => 1f,
	};

	/// <summary>
	/// Lay out one settlement, in the order a real one grew.
	///
	/// Ground, then the square, then the streets that leave it, then the lots
	/// those streets front, then the wall around the lot. Every stage constrains
	/// the next, which is the whole point: the earlier version chose a random
	/// angle and radius for each house and asked whether anything was in the way,
	/// so the result had no streets, no frontage and no centre — just buildings
	/// that had successfully avoided each other.
	/// </summary>
	private static void BuildOne(Terrain terrain, SettlementSite site)
	{
		var rng = new Rng(unchecked((int)site.Seed));
		int S = terrain.Size;

		float squareR = SquareRadius(site);
		float wallR = site.Radius;
		// A holdout pulled back inside its own defences, so its wall encloses less
		// than the place once covered. The GATES have to be found at the radius
		// the wall is actually built at, not at the original one — otherwise the
		// openings are cut in the wrong places and the palisade seals across the
		// roads leading in.
		float palisadeR = site.State == RemnantState.Holdout ? wallR * 0.62f : wallR;

		// Where the roads actually arrive. These become the gates, and the
		// streets are run out to meet them, so the approach a player walks in on
		// continues into the town instead of stopping at its edge.
		var gates = FindGates(terrain, site, palisadeR);

		// A monument has lost everything that was made of wood. What is left is
		// the platform it stood on and the stone that was cut for it, which is
		// generated as a footprint rather than as buildings.
		if (site.State == RemnantState.Monument)
		{
			Foundations(terrain, rng, site);
			Signposts(terrain, site);
			return;
		}

		Square(terrain, rng, site);

		// Radial streets: one to every gate, plus fill so a town always has at
		// least four ways out of its own square.
		var spokes = new List<float>(gates);
		int minSpokes = site.Kind == SettlementKind.Town ? 5 : 4;
		for (int k = 0; spokes.Count < minSpokes && k < 16; k++)
		{
			float a = MathF.Tau * k / minSpokes + rng.Next() * 0.2f;
			bool clash = false;
			foreach (float g in spokes)
				if (MathF.Abs(AngleDelta(g, a)) < 0.55f) { clash = true; break; }
			if (!clash) spokes.Add(a);
		}

		foreach (float a in spokes)
			Street(terrain, site, a, squareR, wallR + 4f, site.Kind == SettlementKind.Town ? 2 : 1);

		// A ring street partway out, so the town has blocks rather than a fan.
		if (site.Kind != SettlementKind.Hamlet)
			RingStreet(terrain, site, (squareR + wallR) * 0.55f, 1);

		// A market means commerce means people. Only a holdout gets stalls with
		// cloth still on them; a remnant keeps the bare frames, which is a more
		// eloquent object than either a working market or an empty space; and
		// beyond that there is nothing left to stand up.
		if (site.State <= RemnantState.Remnant && site.Kind != SettlementKind.Hamlet && spokes.Count > 0)
			Market(terrain, rng, site, spokes[rng.RangeInt(0, spokes.Count - 1)], squareR);

		// Lots, placed ALONG the streets rather than anywhere that happens to fit.
		// Nearest the square first, so the maintained handful are the ones at the
		// middle of the place rather than scattered through the derelict edge.
		int keptBuildings = rng.RangeInt(2, 4);
		int placed = 0;
		int wanted = site.Kind switch
		{
			SettlementKind.Town => 22,
			SettlementKind.Village => 12,
			_ => 5,
		};

		foreach (float a in spokes)
		{
			// Both sides of the street, walking outward from the square.
			for (float d = squareR + 4f; d < wallR - 4f && placed < wanted; d += 10f)
			for (int side = -1; side <= 1 && placed < wanted; side += 2)
			{
				float offset = (site.Kind == SettlementKind.Town ? 5.5f : 4.5f);
				float px = site.X + MathF.Cos(a) * d - MathF.Sin(a) * offset * side;
				float pz = site.Z + MathF.Sin(a) * d + MathF.Cos(a) * offset * side;

				// Long axis parallel to the street, so the gable end and the door
				// address the road the way a terrace of cottages does.
				bool alongX = MathF.Abs(MathF.Cos(a)) > MathF.Abs(MathF.Sin(a));
				int w = alongX ? rng.RangeInt(6, 8) : rng.RangeInt(5, 6);
				int dd = alongX ? rng.RangeInt(5, 6) : rng.RangeInt(6, 8);

				int x = (int)px - w / 2, z = (int)pz - dd / 2;
				if (!Fits(terrain, site, x, z, w, dd)) continue;

				// A holdout keeps only the few buildings its people can actually
				// use. Everything else on the site is as dead as anywhere else.
				//
				// This is the whole image from plan.md §11.1 and the first version
				// missed it entirely: making a holdout simply "intact" rebuilt a
				// working village, which is the one thing this world does not have.
				// What it should look like is the last lit room in a large dark
				// house — and that only works if the dark house is there.
				bool kept = site.State != RemnantState.Holdout || placed < keptBuildings;
				Cottage(terrain, rng, site, x, z, w, dd, kept);
				site.Buildings.Add((x, z, w, dd));
				placed++;
				LastBuildingCount++;
			}
		}

		// A palisade, with the gates left open. Villages get one too; hamlets are
		// too small to be worth walling and look absurd inside one.
		if (site.Kind != SettlementKind.Hamlet)
			Palisade(terrain, rng, site, palisadeR, gates);

		if (site.Kind != SettlementKind.Hamlet) Well(terrain, rng, site);
		Signposts(terrain, site);
	}

	/// <summary>
	/// What a place looks like once only the stonework is left.
	///
	/// Deliberately not "a ruin with more blocks removed". Timber-framed
	/// buildings leave nothing after long enough; what survives generations is
	/// what was cut from stone and what was moved as earth — footings, thresholds,
	/// the line of a wall, the platform itself. The player should be able to walk
	/// the plan of a place whose buildings are entirely gone.
	/// </summary>
	private static void Foundations(Terrain terrain, Rng rng, SettlementSite site)
	{
		int S = terrain.Size;
		int lines = site.Kind switch
		{
			SettlementKind.Town => 9,
			SettlementKind.Village => 6,
			_ => 3,
		};

		for (int n = 0; n < lines; n++)
		{
			float a = rng.Next() * MathF.Tau;
			float d = site.Radius * MathF.Sqrt(rng.Range(0.05f, 0.9f));
			int cx = (int)(site.X + MathF.Cos(a) * d);
			int cz = (int)(site.Z + MathF.Sin(a) * d);
			int w = rng.RangeInt(5, 9), h = rng.RangeInt(5, 8);
			bool alongX = rng.Chance(0.5f);
			if (!alongX) (w, h) = (h, w);

			for (int dz = 0; dz < h; dz++)
			for (int dx = 0; dx < w; dx++)
			{
				bool edge = dx == 0 || dz == 0 || dx == w - 1 || dz == h - 1;
				if (!edge) continue;
				// Even the footings are broken; a continuous rectangle reads as a
				// foundation slab rather than as something that has been here for
				// generations.
				if (rng.Chance(0.28f)) continue;
				int x = cx + dx, z = cz + dz;
				if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) continue;
				int i = z * S + x;
				if (terrain.Land[i] == 0) continue;
				if (terrain.Grid.Heights[i] > terrain.Level[i]) continue;
				int y = terrain.Level[i];
				Put(terrain, x, y, z, rng.Chance(0.3f) ? Palette.STONE_PALE : Palette.STONE);
				if (rng.Chance(0.16f)) Put(terrain, x, y + 1, z, Palette.STONE);
			}
		}
	}

	/// <summary>Bearings at which a road crosses the settlement's boundary.</summary>
	private static List<float> FindGates(Terrain terrain, SettlementSite site, float wallR)
	{
		var gates = new List<float>();
		if (terrain.Roads == null) return gates;
		int S = terrain.Size;

		for (int k = 0; k < 72; k++)
		{
			float a = MathF.Tau * k / 72f;
			int x = site.X + (int)(MathF.Cos(a) * wallR);
			int z = site.Z + (int)(MathF.Sin(a) * wallR);
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			if (terrain.Roads.Mask[z * S + x] == 0) continue;

			bool near = false;
			foreach (float g in gates)
				if (MathF.Abs(AngleDelta(g, a)) < 0.45f) { near = true; break; }
			if (!near) gates.Add(a);
		}
		return gates;
	}

	/// <summary>Pave a straight street outward from the square along one bearing.</summary>
	private static void Street(Terrain terrain, SettlementSite site, float angle,
		float from, float to, int half)
	{
		int S = terrain.Size;
		for (float d = from; d <= to; d += 0.5f)
		for (int o = -half; o <= half; o++)
		{
			int x = (int)(site.X + MathF.Cos(angle) * d - MathF.Sin(angle) * o);
			int z = (int)(site.Z + MathF.Sin(angle) * d + MathF.Cos(angle) * o);
			Pave(terrain, site, x, z);
		}
	}

	private static void RingStreet(Terrain terrain, SettlementSite site, float radius, int half)
	{
		float step = 1f / MathF.Max(radius, 1f);
		for (float a = 0f; a < MathF.Tau; a += step)
		for (int o = -half; o <= half; o++)
		{
			int x = (int)(site.X + MathF.Cos(a) * (radius + o));
			int z = (int)(site.Z + MathF.Sin(a) * (radius + o));
			Pave(terrain, site, x, z);
		}
	}

	private static void Pave(Terrain terrain, SettlementSite site, int x, int z)
	{
		int S = terrain.Size;
		if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) return;
		int i = z * S + x;
		if (terrain.Land[i] == 0) return;
		if (terrain.Grid.Heights[i] > terrain.Level[i]) return;
		if (terrain.Roads != null) terrain.Roads.Mask[i] = 0;

		// A street nobody sweeps closes from the edges and fills between the
		// stones. Sampled as coherent patches rather than per block: paving comes
		// up in slabs, and a per-block sprinkle reads as dither on a floor.
		float lift = Decay(site) * 1.2f;
		if (lift > 0.02f)
		{
			float patch = 0.5f + 0.5f * MathF.Sin(x * 0.27f + z * 0.11f) * MathF.Cos(z * 0.31f - x * 0.09f);
			if (patch < lift)
			{
				// What comes back depends on how long it has had. Moss first, then
				// the ground simply wins and there is nothing left to say a street
				// was ever here.
				if (lift > 0.9f) return;
				terrain.Grid.Set(x, terrain.Level[i] - 1, z,
					lift > 0.55f ? Palette.MOSS : Palette.BLOSSOM_DRIFT);
				return;
			}
		}
		terrain.Grid.Set(x, terrain.Level[i] - 1, z, Palette.PAVING);
	}

	/// <summary>
	/// A ring of upright timbers with the gateways left open.
	///
	/// Two courses of plank on a beam post every third block: solid enough to
	/// read as a defence from the air, open enough that it does not become a
	/// featureless band around every town.
	/// </summary>
	private static void Palisade(Terrain terrain, Rng rng, SettlementSite site,
		float radius, List<float> gates)
	{
		int S = terrain.Size;
		float decay = Decay(site);
		float step = 0.6f / MathF.Max(radius, 1f);
		int post = 0;

		for (float a = 0f; a < MathF.Tau; a += step, post++)
		{
			bool atGate = false;
			foreach (float g in gates)
				if (MathF.Abs(AngleDelta(g, a)) < 0.16f) { atGate = true; break; }

			int x = (int)(site.X + MathF.Cos(a) * radius);
			int z = (int)(site.Z + MathF.Sin(a) * radius);
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			int i = z * S + x;
			if (terrain.Land[i] == 0) continue;
			if (terrain.Grid.Heights[i] > terrain.Level[i]) continue;

			int y = terrain.Level[i];
			if (atGate)
			{
				// Gate towers flanking the opening, and nothing across it.
				continue;
			}

			// Taller and darker than the first pass, which read as a pale kerb
			// rather than a defence. TRUNK is the darkest value the world allows
			// and is what gives the wall a silhouette against pale ground.
			//
			// Decay eats it from the top and in runs — a palisade fails post by
			// post, leaving stubs and gaps, not a uniformly shorter fence.
			float gap = decay * 1.1f * (0.5f + 0.5f * MathF.Abs(MathF.Sin(post * 0.7f)));
			if (rng.Next() < gap) continue;
			int height = 4 - (int)(decay * 2.6f + rng.Next() * 0.8f);
			if (height < 1) continue;

			Put(terrain, x, y, z, Palette.TRUNK);
			for (int k = 1; k < height; k++)
				Put(terrain, x, y + k, z, post % 3 == 0 ? Palette.TRUNK : Palette.PLANK);
		}

		// Gateposts: a pair of tall timbers either side of every opening.
		foreach (float g in gates)
		foreach (int side in new[] { -1, 1 })
		{
			float a = g + side * 0.17f;
			int x = (int)(site.X + MathF.Cos(a) * radius);
			int z = (int)(site.Z + MathF.Sin(a) * radius);
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			int i = z * S + x;
			if (terrain.Land[i] == 0) continue;
			int y = terrain.Level[i];
			int tall = 6 - (int)(decay * 3.5f);
			for (int k = 0; k < tall; k++) Put(terrain, x, y + k, z, Palette.TRUNK);
			if (decay < 0.5f) Put(terrain, x, y + tall, z, Palette.ROOF_TILE);
		}
	}

	/// <summary>
	/// Stalls: a low counter under a bright awning on four posts.
	///
	/// Colour does the work here. A market has to read as a different KIND of
	/// place from the houses around it at a glance, and the one thing this world
	/// has in abundance that a roof does not use is blossom — so the awnings are
	/// canopy colours, which makes the market the brightest patch in any town.
	/// </summary>
	private static void Market(Terrain terrain, Rng rng, SettlementSite site,
		float angle, float squareR)
	{
		int S = terrain.Size;
		byte[] awnings = { Palette.LEAF_PINK, Palette.LEAF_BLUSH, Palette.LEAF_LILAC, Palette.LEAF_CREAM };

		for (int n = 0; n < 6; n++)
		{
			float d = squareR + 2.5f + (n / 2) * 5f;
			float o = (n % 2 == 0 ? -1f : 1f) * 4.5f;
			int cx = (int)(site.X + MathF.Cos(angle) * d - MathF.Sin(angle) * o);
			int cz = (int)(site.Z + MathF.Sin(angle) * d + MathF.Cos(angle) * o);
			if (cx < 3 || cz < 3 || cx >= S - 3 || cz >= S - 3) continue;

			int i = cz * S + cx;
			if (terrain.Land[i] == 0) continue;
			int y = terrain.Level[i];

			bool clear = true;
			for (int dz = -1; dz <= 1 && clear; dz++)
			for (int dx = -1; dx <= 1; dx++)
			{
				int j = (cz + dz) * S + cx + dx;
				if (terrain.Land[j] == 0 || terrain.Level[j] != y ||
					terrain.Grid.Heights[j] > terrain.Level[j]) { clear = false; break; }
			}
			if (!clear) continue;

			bool cloth = site.State == RemnantState.Holdout;
			byte awning = rng.Pick(awnings);
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++)
			{
				bool corner = dx != 0 && dz != 0;
				if (corner) for (int k = 0; k < 3; k++) Put(terrain, cx + dx, y + k, cz + dz, Palette.TRUNK);
				if (cloth) Put(terrain, cx + dx, y + 3, cz + dz, awning);
				else if (corner) Put(terrain, cx + dx, y + 3, cz + dz, Palette.TRUNK);
			}
			// The counter itself, on the side facing the square.
			Put(terrain, cx, y, cz - 1, Palette.PLANK_PALE);
			Put(terrain, cx, y, cz + 1, Palette.PLANK_PALE);
		}
	}

	private static bool Fits(Terrain terrain, SettlementSite site, int x, int z, int w, int d)
	{
		int S = terrain.Size;
		// One clear block all round for the roof's eaves, which overhang the walls.
		if (x < 3 || z < 3 || x + w >= S - 3 || z + d >= S - 3) return false;

		float square = SquareRadius(site);
		int level = terrain.Level[(z + d / 2) * S + x + w / 2];
		for (int dz = -2; dz < d + 2; dz++)
		for (int dx = -2; dx < w + 2; dx++)
		{
			int xx = x + dx, zz = z + dz;
			int i = zz * S + xx;
			if (terrain.Land[i] == 0) return false;
			if (terrain.StairMask[i] != 0) return false;
			// The road test applies to the FOOTPRINT only, not to the two-block
			// margin around it.
			//
			// Testing the margin is why the first villages had a paved square in
			// the middle with the nearest building thirty blocks away: every route
			// into a settlement converges on its centre, so the mask there is a
			// wide blob, and demanding two clear blocks beyond the walls as well
			// pushed every house outside it. A cottage is supposed to stand ON the
			// street — that is what a street is.
			bool inFootprint = dx >= 0 && dz >= 0 && dx < w && dz < d;
			if (inFootprint && terrain.Roads != null && terrain.Roads.Mask[i] != 0) return false;
			float sx = xx - site.X, sz = zz - site.Z;
			if (square > 0f && sx * sx + sz * sz < square * square) return false;
			if (Math.Abs(terrain.Level[i] - level) > Terrain.Step) return false;
			// Anything already standing here — another house, a bridge rail — is
			// visible as a column taller than the ground.
			if (terrain.Grid.Heights[i] > terrain.Level[i]) return false;
		}

		foreach (var b in site.Buildings)
			if (x < b.x + b.w + 2 && x + w + 2 > b.x && z < b.z + b.d + 2 && z + d + 2 > b.z)
				return false;
		return true;
	}

	/// <summary>Write a block and keep the column height in step with it.</summary>
	private static void Put(Terrain terrain, int x, int y, int z, byte id)
	{
		var grid = terrain.Grid;
		if (!grid.InBounds(x, y, z)) return;
		grid.Set(x, y, z, id);
		if (!Palette.IsSolid(id)) return;
		int i = z * terrain.Size + x;
		if (y + 1 > grid.Heights[i]) grid.Heights[i] = (short)(y + 1);
	}

	/* ----------------------------------------------------------------
	 * A cottage
	 * ---------------------------------------------------------------- */

	/// <summary>
	/// Plaster panels in a timber frame under a steep tiled roof.
	///
	/// The roof is the whole silhouette and it is built as a real GABLE: a ridge
	/// along the long axis, two planes falling away to eaves that overhang the
	/// walls, and a triangle of plaster closing each end. An earlier revision
	/// stacked concentric slabs instead, which is much less code and reads
	/// unmistakably as a pagoda — the eye finds the horizontal tiers immediately
	/// and no amount of colour work argues it out of them.
	///
	/// Everything else here exists to stop the box reading as a box: a sill and a
	/// head band so the wall has a frame rather than four corner posts, windows
	/// that are lit so the place looks lived in, a chimney to break the ridge, and
	/// a door with a step facing whatever the house was built to face.
	/// </summary>
	private static void Cottage(Terrain terrain, Rng rng, SettlementSite site,
		int x0, int z0, int w, int d, bool kept = true)
	{
		int S = terrain.Size;
		int floor = terrain.Level[(z0 + d / 2) * S + x0 + w / 2];
		int wallH = rng.RangeInt(4, 5);
		int wallTop = floor + wallH;

		byte roof = rng.Chance(0.45f) ? Palette.ROOF_SLATE : Palette.ROOF_TILE;
		byte frame = rng.Chance(0.35f) ? Palette.TRUNK_ROSE : Palette.BEAM;

		// How far gone THIS building is. Rolled per building around the site's
		// state rather than taken from it directly: a ruined hamlet where every
		// cottage has lost exactly the same amount reads as a filter applied to a
		// village, not as a place that fell apart one roof at a time.
		float baseDecay = kept ? Decay(site) : MathF.Max(Decay(site), 0.62f);
		float decay = Rng.Clamp(baseDecay + rng.Bell() * 0.18f, 0f, 1f);
		bool lived = kept && site.State == RemnantState.Holdout && decay < 0.2f;

		// One bias per wall, so the four sides of a building fail by different
		// amounts. Without it every cottage loses its walls symmetrically and a
		// ruined street reads as a repeated stamp.
		var sideBias = new float[4];
		for (int k = 0; k < 4; k++) sideBias[k] = rng.Next();

		// Level the pad. A house standing on a one-block ripple is on stilts at one
		// corner, and the terrain filter guarantees ripples inside any footprint
		// this size.
		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			int x = x0 + dx, z = z0 + dz, i = z * S + x;
			for (int y = terrain.Level[i]; y < floor; y++) Put(terrain, x, y, z, Palette.STONE_PALE);
			for (int y = floor; y < terrain.Level[i]; y++) terrain.Grid.Set(x, y, z, Palette.AIR);
			terrain.Level[i] = (short)floor;
			terrain.Grid.Heights[i] = (short)floor;
			terrain.Grid.Set(x, floor - 1, z, Palette.PAVING);
		}

		// Which way the door faces: toward the middle of the settlement, so a
		// street of houses all turn to look at the same place.
		float toCx = site.X - (x0 + w * 0.5f), toCz = site.Z - (z0 + d * 0.5f);
		int doorSide = MathF.Abs(toCx) > MathF.Abs(toCz)
			? (toCx > 0 ? 3 : 2)
			: (toCz > 0 ? 1 : 0);
		int doorX = x0 + w / 2, doorZ = z0 + d / 2;

		bool IsDoor(int x, int z, int y)
		{
			if (y >= floor + 2) return false;
			return doorSide switch
			{
				0 => z == z0 && x == doorX,
				1 => z == z0 + d - 1 && x == doorX,
				2 => x == x0 && z == doorZ,
				_ => x == x0 + w - 1 && z == doorZ,
			};
		}

		// Walls. Plaster panels, with the frame reading at the corners, the sill
		// and the head — the three places real timber framing actually shows.
		for (int y = floor; y < wallTop; y++)
		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			bool edge = dx == 0 || dz == 0 || dx == w - 1 || dz == d - 1;
			if (!edge) continue;
			int x = x0 + dx, z = z0 + dz;
			if (IsDoor(x, z, y)) continue;

			bool corner = (dx == 0 || dx == w - 1) && (dz == 0 || dz == d - 1);
			bool band = y == floor || y == wallTop - 1;
			byte id = corner || band ? frame : Palette.PLASTER;

			// Windows.
			//
			// The single most useful signal in the whole world model. A lit window
			// means somebody is in there; the emissive block does the work at any
			// distance and needs no marker, no icon and no interface. So only a
			// LIVED-IN building gets one. A shuttered remnant boards them over, and
			// a ruin has holes where they were.
			if (!corner && !band && y == floor + 2)
			{
				bool longSide = (dz == 0 || dz == d - 1) ? w >= d : d > w;
				int along = (dz == 0 || dz == d - 1) ? dx : dz;
				if (longSide && along % 2 == 1 && along > 0 && along < (dz == 0 || dz == d - 1 ? w - 1 : d - 1))
					id = lived ? Palette.WINDOW
						: decay < 0.45f ? frame
						: Palette.AIR;
			}

			// Walls fail in SECTIONS, never block by block.
			//
			// Per-block random loss was the first attempt and it turns a cottage
			// into confetti: what survives is a cloud of unrelated cubes, and by
			// the rule in plan.md §11.3 a ruin is only a ruin if you can still
			// tell what it was. Buildings do not come apart that way. A wall goes
			// as a run, one gable stands to full height while the wall beside it
			// is down to a course, and the corner posts — the frame the whole
			// thing was built around — outlast every panel.
			if (id != Palette.AIR && decay > 0.05f)
			{
				int side = dz == 0 ? 0 : dz == d - 1 ? 1 : dx == 0 ? 2 : 3;
				float along = side < 2 ? dx : dz;
				// One slow wave per wall, so the surviving height rises and falls
				// along it instead of jittering.
				float wave = 0.5f + 0.5f * MathF.Sin(along * 0.85f + sideBias[side] * 6.28f);
				float loss = decay * (0.35f + sideBias[side] * 0.85f) * (0.4f + wave * 0.9f);
				int standing = (int)MathF.Round(wallH * (1f - loss));
				// The frame is the last thing left.
				if (corner) standing = Math.Max(standing, (int)(wallH * (1f - decay * 0.55f)));
				if (band && y == floor) standing = Math.Max(standing, 1);
				if (y - floor >= Math.Max(0, standing)) continue;
			}
			if (id == Palette.AIR) continue;
			Put(terrain, x, y, z, id);
		}

		// A lintel over the door, and a step down to the ground.
		{
			int lx = doorSide == 2 ? x0 : doorSide == 3 ? x0 + w - 1 : doorX;
			int lz = doorSide == 0 ? z0 : doorSide == 1 ? z0 + d - 1 : doorZ;
			Put(terrain, lx, floor + 2, lz, frame);
			int sx = lx + (doorSide == 2 ? -1 : doorSide == 3 ? 1 : 0);
			int sz = lz + (doorSide == 0 ? -1 : doorSide == 1 ? 1 : 0);
			Put(terrain, sx, floor - 1, sz, Palette.PAVING);
		}

		/* ---- the roof ---- */

		bool ridgeAlongX = w >= d;
		int span = ridgeAlongX ? d : w;
		// TWO courses: an eave and a ridge.
		//
		// In voxels every course shows its own top face, so three read as three
		// separate ledges and the building came out looking like a flight of
		// steps. Two shows two faces, which the eye resolves as one pitch. The
		// gable ends and the overhanging eaves are what actually say "roof" at
		// this distance; the number of treads between them is not.
		int rings = Math.Min(2, (span + 2 + 1) / 2);

		// A ruin has lost most of its roof, and the roof goes before the walls.
		float roofLoss = decay * 1.35f;

		for (int r = 0; r < rings; r++)
		{
			int y = wallTop + r;
			int lo = (ridgeAlongX ? z0 : x0) - 1 + r;
			int hi = (ridgeAlongX ? z0 + d : x0 + w) - r;
			int endLo = (ridgeAlongX ? x0 : z0) - 1;
			int endHi = (ridgeAlongX ? x0 + w : z0 + d);
			if (lo > hi) break;

			for (int a = endLo; a <= endHi; a++)
			{
				// Roofs fail in PATCHES, not per block: a hole in a roof is a
				// section of missing rafters, so the loss is sampled coarsely
				// along the ridge and whole runs go together.
				bool gone = roofLoss > 0.02f &&
					rng.Next() < roofLoss * (0.55f + 0.45f * MathF.Abs(MathF.Sin(a * 0.9f)));
				if (gone) continue;

				if (r == rings - 1)
				{
					// The ridge: whatever is left of the span, closed over.
					for (int b = lo; b <= hi; b++)
						Put(terrain, ridgeAlongX ? a : b, y, ridgeAlongX ? b : a, roof);
				}
				else
				{
					Put(terrain, ridgeAlongX ? a : lo, y, ridgeAlongX ? lo : a, roof);
					Put(terrain, ridgeAlongX ? a : hi, y, ridgeAlongX ? hi : a, roof);
				}
			}

			// The gable: a triangle of plaster closing each end of the ridge, set
			// in one block from the verge so the roof edge stands proud of it.
			if (r >= rings - 1) continue;
			for (int b = lo + 1; b < hi; b++)
			foreach (int end in new[] { ridgeAlongX ? x0 : z0, ridgeAlongX ? x0 + w - 1 : z0 + d - 1 })
				Put(terrain, ridgeAlongX ? end : b, y, ridgeAlongX ? b : end, Palette.PLASTER);
		}

		// A chimney, off-centre and clear of the door, rising past the ridge.
		{
			int cx = ridgeAlongX ? x0 + (rng.Chance(0.5f) ? 1 : w - 2) : x0 + w / 2;
			int cz = ridgeAlongX ? z0 + d / 2 : z0 + (rng.Chance(0.5f) ? 1 : d - 2);
			// The chimney is stone and outlives everything else, which is exactly
			// what real abandoned houses look like from a distance: a stack
			// standing in a stand of trees.
			int top = wallTop + rings - (int)(decay * rings);
			for (int y = floor; y <= top; y++)
				Put(terrain, cx, y, cz, Palette.STONE_WARM);
		}

		// The floor goes back to the land as the roof lets the weather in.
		if (decay > 0.4f)
		{
			byte floorCover = decay > 0.8f ? Palette.MOSS : Palette.BLOSSOM_DRIFT;
			for (int dz = 1; dz < d - 1; dz++)
			for (int dx = 1; dx < w - 1; dx++)
				if (rng.Next() < (decay - 0.4f) * 1.7f)
					terrain.Grid.Set(x0 + dx, floor - 1, z0 + dz, floorCover);
		}

		// A lamp by the door of a house somebody comes home to.
		if (lived)
		{
			int lx = doorSide == 2 ? x0 - 1 : doorSide == 3 ? x0 + w : doorX;
			int lz = doorSide == 0 ? z0 - 1 : doorSide == 1 ? z0 + d : doorZ;
			Put(terrain, lx, floor, lz, Palette.BEAM);
			Put(terrain, lx, floor + 1, lz, Palette.BEAM);
			Put(terrain, lx, floor + 2, lz, Palette.LANTERN);
		}

		Garden(terrain, rng, site, x0, z0, w, d, doorSide);
	}

	/// <summary>
	/// A fenced plot beside the house, on the side away from its door.
	///
	/// Fences are what turn a scatter of buildings into property. Posted at
	/// intervals rather than run solid: a continuous rail at this scale reads as a
	/// garden wall, and the reference's plots are clearly open timber.
	/// </summary>
	private static void Garden(Terrain terrain, Rng rng, SettlementSite site,
		int x0, int z0, int w, int d, int doorSide)
	{
		if (rng.Chance(0.35f)) return;
		int S = terrain.Size;
		int depth = rng.RangeInt(3, 5);

		// Opposite the door, so the plot is behind the house rather than across
		// its own doorstep.
		int gx0 = x0, gz0 = z0, gw = w, gd = depth;
		switch (doorSide)
		{
			case 0: gz0 = z0 + d; break;
			case 1: gz0 = z0 - depth; break;
			case 2: gx0 = x0 + w; gw = depth; gd = d; break;
			default: gx0 = x0 - depth; gw = depth; gd = d; break;
		}

		int level = terrain.Level[(z0 + d / 2) * S + x0 + w / 2];
		for (int dz = 0; dz < gd; dz++)
		for (int dx = 0; dx < gw; dx++)
		{
			int x = gx0 + dx, z = gz0 + dz;
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) return;
			int i = z * S + x;
			if (terrain.Land[i] == 0 || terrain.Level[i] != level) return;
			if (terrain.Grid.Heights[i] > terrain.Level[i]) return;
			if (terrain.Roads != null && terrain.Roads.Mask[i] != 0) return;
		}

		for (int dz = 0; dz < gd; dz++)
		for (int dx = 0; dx < gw; dx++)
		{
			bool edge = dx == 0 || dz == 0 || dx == gw - 1 || dz == gd - 1;
			if (!edge) continue;
			int x = gx0 + dx, z = gz0 + dz;
			bool post = (dx + dz) % 2 == 0;
			Put(terrain, x, level, z, Palette.BEAM);
			if (post) Put(terrain, x, level + 1, z, Palette.BEAM);
		}
	}

	/* ----------------------------------------------------------------
	 * The square
	 * ---------------------------------------------------------------- */

	/// <summary>
	/// Flagstone at the middle of the settlement, with lamps around its edge.
	///
	/// Without it the centre of a village is just the place where several roads
	/// happen to overlap, which is exactly what it looked like: a wide pale blob
	/// with houses scattered round the outside. A deliberate paved space gives
	/// the houses something to face and the player somewhere to arrive.
	/// </summary>
	private static void Square(Terrain terrain, Rng rng, SettlementSite site)
	{
		float radius = SquareRadius(site);
		if (radius <= 0f) return;

		int S = terrain.Size;
		int level = terrain.Level[site.Z * S + site.X];
		int r = (int)MathF.Ceiling(radius) + 1;

		for (int dz = -r; dz <= r; dz++)
		for (int dx = -r; dx <= r; dx++)
		{
			int x = site.X + dx, z = site.Z + dz;
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			int i = z * S + x;
			// A rounded square rather than a disc: paving is laid in courses and a
			// circular edge of it reads as a stain rather than as work.
			float shape = MathF.Max(MathF.Abs(dx), MathF.Abs(dz)) * 0.62f
			            + MathF.Sqrt(dx * dx + dz * dz) * 0.38f;
			if (shape > radius) continue;
			if (terrain.Land[i] == 0) continue;
			if (Math.Abs(terrain.Level[i] - level) > Terrain.Step) continue;
			if (terrain.Grid.Heights[i] > terrain.Level[i]) continue;
			// One reclamation rule, shared with the streets. Two rules would drift
			// apart and a square would stop matching the roads running out of it.
			//
			// Pave also clears the road mask: the square IS the road here. Leaving
			// it set would keep every house out of the middle of its own village,
			// and would draw the square on the world map as a swollen junction.
			Pave(terrain, site, x, z);
		}

		// Lamps at the corners of the square.
		for (int k = 0; k < 4; k++)
		{
			float ang = MathF.Tau * (k / 4f) + 0.785f;
			int x = site.X + (int)(MathF.Cos(ang) * (radius - 1f));
			int z = site.Z + (int)(MathF.Sin(ang) * (radius - 1f));
			if (x < 1 || z < 1 || x >= S - 1 || z >= S - 1) continue;
			int i = z * S + x;
			if (terrain.Land[i] == 0 || terrain.Grid.Heights[i] > terrain.Level[i]) continue;
			Lamp(terrain, x, terrain.Level[i], z, site.State == RemnantState.Holdout);
		}
	}

	private static void Lamp(Terrain terrain, int x, int y, int z, bool lit)
	{
		Put(terrain, x, y, z, Palette.TRUNK);
		Put(terrain, x, y + 1, z, Palette.TRUNK);
		// A lantern is a thing somebody fills. An empty place has the post and
		// the bracket and no light in it, which says more than removing the lamp.
		Put(terrain, x, y + 2, z, lit ? Palette.LANTERN : Palette.TRUNK);
		Put(terrain, x, y + 3, z, Palette.ROOF_SLATE);
	}

	/// <summary>A well with a roof over it, which is what a well actually has.</summary>
	private static void Well(Terrain terrain, Rng rng, SettlementSite site)
	{
		int S = terrain.Size;
		int x = site.X, z = site.Z;
		int i = z * S + x;
		if (terrain.Land[i] == 0) return;
		int floor = terrain.Level[i];

		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			if (dx == 0 && dz == 0) continue;
			Put(terrain, x + dx, floor, z + dz, Palette.STONE);
		}
		// Two posts and a little roof. A bare post with a lantern on it was
		// indistinguishable from a lamp.
		foreach (int side in new[] { -1, 1 })
		for (int y = floor + 1; y <= floor + 3; y++)
			Put(terrain, x + side, y, z, Palette.BEAM);
		// A small gable over it, three by three and one course tall. Anything
		// bigger and the roof is the well: the first version was five blocks
		// across and two deep, which from the rig's height read as a shed that
		// had swallowed the thing it was sheltering.
		for (int dx = -1; dx <= 1; dx++)
		{
			Put(terrain, x + dx, floor + 4, z - 1, Palette.ROOF_TILE);
			Put(terrain, x + dx, floor + 4, z + 1, Palette.ROOF_TILE);
			Put(terrain, x + dx, floor + 5, z, Palette.ROOF_TILE);
		}
		Put(terrain, x, floor + 3, z, Palette.LANTERN);
	}

	/// <summary>
	/// A marker where a road reaches the settlement.
	///
	/// Cheap, and it does a job nothing else does: it tells the player at a
	/// distance that the track they are on leads somewhere, which is most of what
	/// makes a road worth following.
	/// </summary>
	private static void Signposts(Terrain terrain, SettlementSite site)
	{
		if (terrain.Roads == null) return;
		int S = terrain.Size;
		float reach = site.Radius + 4f;
		int placed = 0;

		for (int a = 0; a < 16 && placed < 2; a++)
		{
			float ang = MathF.Tau * a / 16f;
			int x = site.X + (int)(MathF.Cos(ang) * reach);
			int z = site.Z + (int)(MathF.Sin(ang) * reach);
			if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) continue;
			if (terrain.Roads.Mask[z * S + x] == 0) continue;

			// Beside the road, never on it.
			for (int side = -2; side <= 2; side += 4)
			{
				int px = x + (int)(MathF.Cos(ang + MathF.PI * 0.5f) * side);
				int pz = z + (int)(MathF.Sin(ang + MathF.PI * 0.5f) * side);
				if (px < 1 || pz < 1 || px >= S - 1 || pz >= S - 1) continue;
				int i = pz * S + px;
				if (terrain.Land[i] == 0 || terrain.Roads.Mask[i] != 0) continue;
				if (terrain.Grid.Heights[i] > terrain.Level[i]) continue;
				int y = terrain.Level[i];
				Put(terrain, px, y, pz, Palette.BEAM);
				Put(terrain, px, y + 1, pz, Palette.BEAM);
				Put(terrain, px, y + 2, pz, Palette.PLANK_PALE);
				placed++;
				break;
			}
		}
	}
}
