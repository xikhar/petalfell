using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

public enum SettlementKind : byte { Hamlet, Village, Town }

public sealed class SettlementSite
{
	public string Id = "";
	public int X, Z;
	/// <summary>Terrain level of the chosen centre column.</summary>
	public int Level;
	public float Radius;
	public SettlementKind Kind;
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

		float area = (S / 256f) * (S / 256f);
		int want = sites.Count + Math.Max(5, (int)MathF.Round(1.7f * area));
		float spacing = 74f;

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
				SettlementKind.Town => 17f,
				SettlementKind.Village => 13f,
				_ => 9f,
			};
		}
		return sites;
	}

	/* ================================================================
	 * Construction
	 * ================================================================ */

	/// <summary>Building count from the last run, for the boot diagnostics.</summary>
	public static int LastBuildingCount;

	public static void Build(Terrain terrain, List<SettlementSite> sites, int seed)
	{
		LastBuildingCount = 0;
		if (sites == null) return;
		foreach (var site in sites) BuildOne(terrain, site);
	}

	/// <summary>Radius of the paved square at the middle of anywhere bigger than a hamlet.</summary>
	private static float SquareRadius(SettlementSite site) => site.Kind switch
	{
		SettlementKind.Town => 6.5f,
		SettlementKind.Village => 5.5f,
		_ => 0f,
	};

	private static void BuildOne(Terrain terrain, SettlementSite site)
	{
		var rng = new Rng(unchecked((int)site.Seed));
		int houses = site.Kind switch
		{
			SettlementKind.Town => rng.RangeInt(9, 14),
			SettlementKind.Village => rng.RangeInt(5, 8),
			_ => rng.RangeInt(3, 5),
		};

		// The square goes down first. It is the thing everything else fronts on to,
		// and it has to exist before a house can be told to face it.
		Square(terrain, rng, site);

		// Houses are tried on a jittered ring rather than a grid. A grid of
		// buildings reads as a barracks; rings around a centre read as a place
		// that grew outward from something.
		int placed = 0;
		for (int attempt = 0; attempt < houses * 16 && placed < houses; attempt++)
		{
			// Ringed on the square, not scattered over the whole site.
			//
			// Sampling the disc uniformly threw the houses to the outside — area
			// grows with the square of the radius, so almost every sample lands
			// near the rim — and the middle of the village came out as a paved
			// space with a well in it and the nearest building thirty blocks away.
			// The reference villages are tight: the houses ARE the edge of the
			// square.
			float ang = rng.Next() * MathF.Tau;
			float inner = SquareRadius(site) + 3f;
			float rad = inner + MathF.Max(0f, site.Radius - inner) * rng.Next();
			int w = rng.RangeInt(6, 9), d = rng.RangeInt(5, 7);
			// Half the houses stand end-on to the centre, so a street is not a row
			// of identical gables all facing the same way.
			if (rng.Chance(0.5f)) (w, d) = (d, w);
			int x = (int)(site.X + MathF.Cos(ang) * rad) - w / 2;
			int z = (int)(site.Z + MathF.Sin(ang) * rad) - d / 2;
			if (!Fits(terrain, site, x, z, w, d)) continue;
			Cottage(terrain, rng, site, x, z, w, d);
			site.Buildings.Add((x, z, w, d));
			placed++;
			LastBuildingCount++;
		}

		if (site.Kind != SettlementKind.Hamlet) Well(terrain, rng, site);
		Signposts(terrain, site);
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
			if (x < b.x + b.w + 3 && x + w + 3 > b.x && z < b.z + b.d + 3 && z + d + 3 > b.z)
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
		int x0, int z0, int w, int d)
	{
		int S = terrain.Size;
		int floor = terrain.Level[(z0 + d / 2) * S + x0 + w / 2];
		int wallH = rng.RangeInt(4, 5);
		int wallTop = floor + wallH;

		byte roof = rng.Chance(0.45f) ? Palette.ROOF_SLATE : Palette.ROOF_TILE;
		byte frame = rng.Chance(0.35f) ? Palette.TRUNK_ROSE : Palette.BEAM;

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

			// Windows, on the panels between the posts and clear of the door.
			if (!corner && !band && y == floor + 2)
			{
				bool longSide = (dz == 0 || dz == d - 1) ? w >= d : d > w;
				int along = (dz == 0 || dz == d - 1) ? dx : dz;
				if (longSide && along % 2 == 1 && along > 0 && along < (dz == 0 || dz == d - 1 ? w - 1 : d - 1))
					id = Palette.WINDOW;
			}
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
		// Three courses, hard cap.
		//
		// A one-in-one pitch carried to a true ridge needs five on a seven-deep
		// cottage, and in voxels every course shows its own top face — so the roof
		// arrives as a flight of five separate ledges standing taller than the
		// walls beneath it. Three steps and a wide capped ridge keeps the gable
		// ends and the eaves, which is what actually says "roof" at this distance,
		// without the building turning into a staircase.
		int rings = Math.Min(3, (span + 2 + 1) / 2);

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
			for (int y = floor; y <= wallTop + rings; y++)
				Put(terrain, cx, y, cz, Palette.STONE_WARM);
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
			terrain.Grid.Set(x, terrain.Level[i] - 1, z, Palette.PAVING);
			// The square IS the road here. Leaving the mask set would keep every
			// house out of the middle of its own village, and would draw the
			// square on the world map as a swollen road junction.
			if (terrain.Roads != null) terrain.Roads.Mask[i] = 0;
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
			Lamp(terrain, x, terrain.Level[i], z);
		}
	}

	private static void Lamp(Terrain terrain, int x, int y, int z)
	{
		Put(terrain, x, y, z, Palette.BEAM);
		Put(terrain, x, y + 1, z, Palette.BEAM);
		Put(terrain, x, y + 2, z, Palette.LANTERN);
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
