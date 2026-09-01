using System;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// ONE site, built to the author's reference image — the summit sanctum — and
/// the first site built with the Massif process (see Massif.cs, and the
/// corrections that led to it in docs/ROADMAP.md §3). Nothing here digs: a
/// natural summit is chosen and the whole site is STACKED ON TOP of it as
/// slabs of stone — three tiers rising base → mid → crown, small satellite
/// slabs shed around the skirt, sheer warped faces everywhere, the land
/// beyond the slabs untouched.
///
/// The monument follows the reference's plan exactly: the arched apse with
/// the glowing emblem at its foot on the crown, two broad axis flights down
/// through the slab fronts, the unequal column cluster and the torn round
/// tower on the west mid tier, the glyph pylons on the east, dressed slabs
/// and a fallen column on the base court, small stairs leaving every tier at
/// their own widths and angles.
///
/// Bare stone, no moss: reclamation returns later as its own reviewed layer.
/// </summary>
public static class Sanctum
{
	public static bool Built { get; private set; }
	public static int SiteX, SiteZ;

	/// <summary>Worked half-extent. Slabs reach ~60 out plus warp; the box is
	/// generous because a Massif never touches what its slabs don't cover.</summary>
	private const int Ext = 90;

	/// <summary>Tier rises above the natural summit, and the apse's height.
	/// Together they must clear the world ceiling — see FindSummit. The first
	/// Massif build used 6/12 and the whole site read FLAT: the reference's
	/// tier faces are ~8 tall and its axis flights are long cascades, so the
	/// rises are sized to give two flights of eight steps each.</summary>
	private const int MidRise = 8, CrownRise = 16, ApseTop = 21;

	private static readonly System.Collections.Generic.Dictionary<string, (int x, int z, int h)>
		_anchors = new();

	public static Vector3? Anchor(Terrain terrain, string name)
	{
		if (!Built || !_anchors.TryGetValue(name, out var a)) return null;
		int y = terrain.Level[a.z * terrain.Size + a.x];
		return new Vector3(a.x + 0.5f, y + a.h, a.z + 0.5f);
	}

	public static void Build(Terrain terrain, int seed)
	{
		Built = false;
		_anchors.Clear();
		if (!FindSummit(terrain, out int cx, out int cz)) return;
		SiteX = cx;
		SiteZ = cz;

		int S = terrain.Size;
		var warp = new Noise2D(seed ^ 0x5A4C);
		var fray = new Noise2D(seed ^ 0x71D3);

		// The base tier caps the natural summit: four courses above the
		// highest ground under it, so even where the hill crests the first
		// slab face is unmistakably BUILT — at +2 the base skimmed the summit
		// and the site read as sitting flat on the ground.
		int peak = 0;
		for (int dz = -48; dz <= 48 && peak >= 0; dz += 6)
		for (int dx = -48; dx <= 48; dx += 6)
			peak = Math.Max(peak, terrain.Level[(cz + dz) * S + cx + dx]);
		int B = Math.Min(peak + 4, Terrain.Height - 2 - ApseTop - CrownRise);
		int M = B + MidRise, C = B + CrownRise;

		/* ---- the earthworks: slabs, decks, stairs, in that order ---- */

		var massif = new Massif(terrain, cx, cz, Ext, warp);

		// Base tier: broad overlapped lobes, the south ones a course or two
		// lower so the approach side already steps before the stairs begin.
		massif.Slab(0, 2, 46, 42, B);
		massif.Slab(-28, -20, 34, 30, B - 1);
		massif.Slab(26, -16, 30, 28, B - 1);
		massif.Slab(-2, -42, 34, 24, B - 2);
		massif.Slab(8, 40, 30, 22, B - 1);
		massif.Slab(-38, 18, 22, 20, B - 2);
		massif.Slab(36, 24, 20, 18, B - 2);

		// Satellite slabs shed around the skirt — the reference's detached
		// blocks standing off the mass, half swallowed by the ground.
		massif.Slab(54, -8, 10, 8, B - 4);
		massif.Slab(-52, -4, 9, 9, B - 5);
		massif.Slab(18, -54, 12, 9, B - 4);
		massif.Slab(-26, -52, 10, 8, B - 3);
		massif.Slab(48, 34, 8, 7, B - 5);

		// Mid tier: the working level — column platform west, pylon ledge
		// east, a north shoulder, all gashed against the central mass.
		massif.Slab(0, 6, 30, 30, M);
		massif.Slab(-24, 6, 20, 22, M);
		massif.Slab(22, 4, 16, 20, M - 1);
		massif.Slab(0, 30, 22, 16, M - 1);

		// Crown tier: the sanctum stands here, flanked by two lesser slabs.
		massif.Slab(0, 16, 18, 18, C);
		massif.Slab(15, 20, 10, 10, C - 1);
		massif.Slab(-15, 18, 10, 10, C - 1);

		// The masonry decks the monument actually stands on, one course proud.
		massif.Deck(-14, 14, 8, 30, C + 1, fray);        // sanctum platform
		massif.Deck(-30, -16, -4, 16, M + 1, fray);      // west: columns + tower
		massif.Deck(16, 28, -6, 12, M, fray);            // east: pylons

		// The axis pair, notched through the tier fronts: crown → mid, then
		// mid → base, the lower flight wider, landings of different lengths.
		massif.Stair(x0: -5, w: 10, sz: 7, ddx: 0, ddz: -1, from: C + 1, to: M, landing: 3);
		massif.Stair(x0: -6, w: 12, sz: -17, ddx: 0, ddz: -1, from: M, to: B, landing: 3);

		// The side ways off each tier, every one its own width and direction.
		massif.Stair(x0: 2, w: 5, sz: -30, ddx: -1, ddz: 0, from: M + 1, to: B - 1, landing: 2);  // west, down west
		massif.Stair(x0: -2, w: 4, sz: 28, ddx: 1, ddz: 0, from: M, to: B - 1, landing: 2);       // east, down east
		massif.Stair(x0: -10, w: 6, sz: -44, ddx: 0, ddz: -1, from: B - 2, to: B - 6, landing: 3); // south skirt
		massif.Stair(x0: 6, w: 4, sz: 44, ddx: 0, ddz: 1, from: B - 1, to: B - 5, landing: 2);     // north skirt

		massif.Apply();

		/* ---- the monument, placed as the reference places it ---- */

		int Xw(int rx) => cx + rx;
		int Zw(int rz) => cz + rz;

		// The apse at the head, the glowing emblem DIRECTLY at its foot on the
		// same platform — one composition, not an apse plus a distant medallion.
		Apse(terrain, cx, Zw(17), C + 1, fray);
		Dais(terrain, cx, Zw(11), C + 1, fray);

		// Paving stains on the crown and the base court; the worn axis line.
		RuinKit.PavedCourt(terrain, Xw(-12), Zw(-14), 24, 18, fray);
		RuinKit.PavedCourt(terrain, Xw(-12), Zw(-38), 26, 14, fray);
		for (int rz = -26; rz <= -18; rz++)
			terrain.Grid.Set(cx, terrain.Level[Zw(rz) * S + cx] - 1, Zw(rz), Palette.PATH);

		// The column cluster west of the stair head on its own deck — the
		// reference's forest of unequal verticals, tallest at the back.
		RuinKit.Column(terrain, Xw(-21), Zw(11), height: 19, broken: 0);
		RuinKit.Column(terrain, Xw(-21), Zw(2), height: 16, broken: 0);
		RuinKit.Column(terrain, Xw(-22), Zw(-3), height: 14, broken: 6);
		RuinKit.Column2(terrain, Xw(-27), Zw(13), height: 11, broken: 0);
		RuinKit.Column2(terrain, Xw(-28), Zw(0), height: 9, broken: 4);

		// East of the apse: one tall whole column and a broken partner, the
		// two glyph pylons on the east deck below — unequal, as drawn.
		RuinKit.Column(terrain, Xw(15), Zw(21), height: 17, broken: 0);
		RuinKit.Column2(terrain, Xw(16), Zw(-2), height: 8, broken: 4);
		RuinKit.Pylon(terrain, Xw(21), Zw(7), height: 15);
		RuinKit.Pylon(terrain, Xw(24), Zw(-3), height: 11);

		// The ruined round tower at the west deck's far end.
		Tower(terrain, Xw(-33), Zw(6), radius: 5, height: 15, fray);

		// Small posts flanking the mid landing.
		RuinKit.Post(terrain, Xw(-8), Zw(-2), 4);
		RuinKit.Post(terrain, Xw(7), Zw(-3), 5);

		// The base court: a fallen column and dressed slabs lying where they
		// were dropped — the big foreground stones of the image.
		RuinKit.FallenColumn(terrain, Xw(-12), Zw(-30), length: 10);
		foreach (var (sx, sz, w, d, h) in new[]
		{ (5, -31, 3, 2, 2), (10, -26, 2, 3, 1), (-4, -38, 2, 2, 1), (27, -12, 2, 2, 1), (-34, -20, 3, 2, 1) })
		{
			int g = RuinKit.FloorAt(terrain, Xw(sx), Zw(sz), w, d);
			if (g < 0) continue;
			for (int bz = 0; bz < d; bz++)
			for (int bx = 0; bx < w; bx++)
			for (int by = 0; by < h; by++)
				RuinKit.Put(terrain, Xw(sx) + bx, g + by, Zw(sz) + bz,
					by == h - 1 ? Palette.STONE_PALE : Palette.STONE);
		}

		// Shed stone where the collapse went.
		RuinKit.RubbleField(terrain, Xw(-8), Zw(-40), 14, 8, fray);
		RuinKit.RubbleField(terrain, Xw(18), Zw(-18), 10, 8, fray);
		RuinKit.RubbleField(terrain, Xw(-38), Zw(-26), 10, 10, fray);

		void A(string name, int rx, int rz, int h) => _anchors[name] = (Xw(rx), Zw(rz), h);
		A("site", 0, 6, 12);
		A("high", 0, 6, 16);
		A("axis", 0, -8, 8);
		A("apse", 0, 17, 12);
		A("emblem", 0, 11, 4);
		A("stair", 0, -10, 5);
		A("court", 0, -30, 4);
		A("tower", -33, 6, 8);
		Built = true;
	}

	/* ================================================================
	 * The parts only this site has
	 * ================================================================ */

	/// <summary>
	/// The apse: a seventeen-wide mass with a tall corbel-arched niche carved
	/// through its south face, meander glyphs cut into the back wall, stepped
	/// buttress wings, and a frayed arch-ring crown. The one huge gesture the
	/// whole composition defers to. Built at absolute floor height H — the
	/// deck under it is already conformed.
	/// </summary>
	private static void Apse(Terrain t, int cx, int z0, int H, Noise2D fray)
	{
		const int halfW = 8, depth = 7, top = 21;

		for (int dx = -halfW; dx <= halfW; dx++)
		for (int dz = 0; dz <= depth; dz++)
		{
			int x = cx + dx, z = z0 + dz;
			// The crown fails in lengths, and the shoulders step down toward
			// the wings — the reference crown is a ragged arch RING, not a
			// flat-topped box.
			int shoulder = Math.Max(0, Math.Abs(dx) - 5);
			int myTop = top - shoulder * 2
				- (fray.Fbm01(x * 0.11f, z * 0.17f, 2) < 0.52f ? 3 : 0)
				+ (dx == 0 ? 1 : 0);
			for (int y = 0; y <= myTop; y++)
			{
				byte id = y == myTop ? Palette.STONE_WARM
					: (y % 5) == 4 ? Palette.STONE : Palette.STONE_PALE;
				RuinKit.Put(t, x, H + y, z, id);
			}
		}

		// The niche: carved, not built — a stepped soffit rising to a crown
		// at +18, floor two courses up (the dais delivers you to it).
		for (int dx = -4; dx <= 4; dx++)
		{
			int soffit = 14 + (4 - Math.Abs(dx));
			for (int dz = 0; dz <= 4; dz++)
			for (int y = 2; y < soffit; y++)
				RuinKit.Put(t, cx + dx, H + y, z0 + dz, Palette.AIR);
		}

		// The niche floor: pale, with a crystal seam across the threshold —
		// the reference apse glows from inside.
		for (int dx = -4; dx <= 4; dx++)
		for (int dz = 0; dz <= 4; dz++)
			RuinKit.Put(t, cx + dx, H + 1, z0 + dz,
				dz == 1 && Math.Abs(dx) <= 3 ? Palette.CRYSTAL : Palette.STONE_PALE);

		// Meander glyphs, carved one deep into the back wall of the niche.
		for (int y = 4; y <= 12; y++)
		for (int dx = -3; dx <= 3; dx++)
			if (RuinKit.Meander(dx + 3, y))
				RuinKit.Put(t, cx + dx, H + y, z0 + 4, Palette.AIR);

		// Buttress wings, stepping down and out from the mass.
		foreach (int sideX in new[] { -1, 1 })
			for (int step = 0; step < 3; step++)
			{
				int x = cx + sideX * (halfW + 1 + step);
				int h = 12 - step * 3;
				for (int dz = 0; dz <= depth - 1; dz++)
				for (int y = 0; y < h; y++)
					RuinKit.Put(t, x, H + y, z0 + dz,
						y == h - 1 ? Palette.STONE_WARM
						: (y % 5) == 4 ? Palette.STONE : Palette.STONE_PALE);
			}
	}

	/// <summary>
	/// The glowing emblem at the apse's foot, inlaid nearly flush — in the
	/// reference it is a disc IN the platform, not a podium standing on it.
	/// One shallow coping step, then the rings.
	/// </summary>
	private static void Dais(Terrain t, int cx, int cz, int H, Noise2D fray)
	{
		for (int dz = -7; dz <= 7; dz++)
		for (int dx = -7; dx <= 7; dx++)
		{
			float r = MathF.Sqrt(dx * dx + dz * dz);
			if (r > 6.8f) continue;
			int x = cx + dx, z = cz + dz;

			int h = r <= 5.2f ? 1 : 0;
			if (h > 0) RuinKit.Put(t, x, H, z, Palette.STONE_PALE);

			byte inlay = r <= 1.8f ? Palette.CRYSTAL
				: r <= 3.2f ? Palette.STONE_WARM
				: r <= 4.2f ? Palette.CRYSTAL
				: r <= 5.2f ? Palette.PAVING
				: fray.Fbm01(x * 0.2f, z * 0.2f, 2) > 0.45f ? Palette.PAVING : Palette.STONE_PALE;
			RuinKit.Put(t, x, H + h - 1, z, inlay);
		}
	}

	/// <summary>
	/// The ruined round tower: hollow, its top torn once around from crest to
	/// breach, a doorway facing the crown. Radius 5 — below an 11-block
	/// diameter the eye refuses to read a voxel drum as round.
	/// </summary>
	private static void Tower(Terrain t, int cx, int cz, int radius, int height, Noise2D fray)
	{
		int g = RuinKit.FloorAt(t, cx - radius, cz - radius, radius * 2 + 1, radius * 2 + 1);
		if (g < 0) return;

		for (int dz = -radius; dz <= radius; dz++)
		for (int dx = -radius; dx <= radius; dx++)
		{
			float r = MathF.Sqrt(dx * dx + dz * dz);
			if (r > radius + 0.4f) continue;
			int x = cx + dx, z = cz + dz;
			bool wall = r > radius - 1.2f;

			if (!wall)
			{
				// The floor inside, one course of paving.
				RuinKit.Foot(t, x, z, g);
				RuinKit.Put(t, x, g, z, Palette.PAVING);
				continue;
			}

			// The wall height tears ONCE around the drum, from a full-height
			// crest down to a low breach — one continuous spiral, because two
			// sine lobes made the first version read as stacked slabs rather
			// than a torn cylinder.
			float angle = MathF.Atan2(dz, dx);                    // -π..π
			// Phased so the BREACH faces south-west — the review shot and the
			// walk both approach from there, and a tear that faces away shows
			// the camera an intact drum.
			float around = ((angle + MathF.PI) / (2f * MathF.PI) + 0.4f) % 1f;
			int myTop = (int)(height * (0.20f + 0.80f * around));
			// The doorway, facing east toward the crown.
			bool door = dx > 0 && Math.Abs(dz) <= 1 && r > radius - 1.6f;
			RuinKit.Foot(t, x, z, g);
			for (int y = 0; y <= myTop; y++)
			{
				if (door && y >= 1 && y <= 4) continue;
				RuinKit.Put(t, x, g + y, z,
					(y % 5) == 4 ? Palette.STONE : Palette.STONE_PALE);
			}
		}

		// The stone the tear shed, lying at the foot of the low side.
		for (int k = 0; k < 5; k++)
		{
			int x = cx - radius - 2 - k % 3, z = cz - 2 + k;
			int rg = RuinKit.Ground(t, x, z);
			if (rg >= 0) RuinKit.Put(t, x, rg, z, k % 2 == 0 ? Palette.RUBBLE : Palette.STONE_PALE);
		}
	}

	/* ================================================================
	 * Site selection
	 * ================================================================ */

	/// <summary>
	/// A prominent dry summit with vertical HEADROOM: the site stacks
	/// CrownRise + ApseTop on top of the peak, so summits too near the world
	/// ceiling are skipped — building the monument down into them would be
	/// exactly the excavation this process forbids. Among the candidates that
	/// fit, the highest and most prominent wins.
	/// </summary>
	private static bool FindSummit(Terrain terrain, out int cx, out int cz)
	{
		int S = terrain.Size;
		float best = float.MinValue;
		cx = cz = 0;
		int headroom = Terrain.Height - 4 - ApseTop - CrownRise;

		for (int z = Ext + 4; z < S - Ext - 4; z += 16)
		for (int x = Ext + 4; x < S - Ext - 4; x += 16)
		{
			int core = 0, coreN = 0, coreMax = 0;
			bool bad = false;
			for (int dz = -48; dz <= 48 && !bad; dz += 12)
			for (int dx = -48; dx <= 48; dx += 12)
			{
				int i = (z + dz) * S + x + dx;
				if (terrain.Land[i] == 0 || terrain.Level[i] <= Terrain.Sea + 3)
				{ bad = true; break; }
				core += terrain.Level[i]; coreN++;
				coreMax = Math.Max(coreMax, terrain.Level[i]);
			}
			if (bad || coreMax > headroom) continue;

			int ring = 0, ringN = 0;
			for (int dz = -144; dz <= 144 && !bad; dz += 36)
			for (int dx = -144; dx <= 144; dx += 36)
			{
				if (Math.Abs(dx) < 96 && Math.Abs(dz) < 96) continue;
				int i = (z + dz) * S + x + dx;
				if (terrain.Land[i] == 0) { bad = true; break; }
				ring += terrain.Level[i]; ringN++;
			}
			if (bad || ringN == 0) continue;

			bool near = false;
			foreach (var s in terrain.Sites)
			{
				float dx = s.X - x, dz = s.Z - z;
				float keep = s.Radius + Ext + 20;
				if (dx * dx + dz * dz < keep * keep) { near = true; break; }
			}
			if (near) continue;

			float coreMean = core / (float)coreN;
			float prominence = coreMean - ring / (float)ringN;
			float score = coreMean * 2f + prominence * 2f;
			if (score > best) { best = score; cx = x; cz = z; }
		}
		return best > float.MinValue;
	}
}
