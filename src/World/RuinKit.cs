using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Slice 1 of the ruins direction (docs/ROADMAP.md §3): the twelve-part
/// architectural kit of docs/RUINS.md §3, built in voxels at the scale table of
/// §2. Nothing composed — just the vocabulary, placeable and inspectable.
///
/// The scale table is the acceptance criterion. Columns 15–30 blocks, arches
/// 12–20 tall over an 8–14 span, pylons 15–25, grand stairs 8–14 wide. The
/// previous build's tallest element was a seven-block chimney; every part here
/// exists to put something on the horizon.
///
/// The KIT YARD lays every part out on flat ground so the whole vocabulary can
/// be walked through and captured in one place. It is a review fixture, not
/// content: built on every boot during this phase, marked on the world map so
/// it can be found in seconds, and removed when the composition grammar
/// (slice 4) turns these builders into parts of real authored sites.
///
/// Every part grounds itself — each solid column is footed down to the terrain
/// under it — because the kit has to be judged standing on real ground, and a
/// floating plinth would say nothing about how these meet a hillside.
/// </summary>
public static class RuinKit
{
	public static bool Built { get; private set; }
	public static int LastBlockCount;
	public static int YardX, YardZ;

	/// <summary>
	/// Is a column inside the yard? The vegetation pass keeps out of it: the
	/// yard exists to judge parts against the reference scale table, and the
	/// first capture landed it in a sakura grove that swallowed every sightline
	/// the shots needed. Real sites keep the normal reclamation apron — this
	/// exemption is the review fixture's, not the kit's.
	/// </summary>
	public static bool Contains(int x, int z) =>
		Built && x >= YardX - YardW / 2 - 4 && x <= YardX + YardW / 2 + 4 &&
		z >= YardZ - YardD / 2 - 4 && z <= YardZ + YardD / 2 + 4;

	/// <summary>
	/// Named viewpoints inside the yard, for the capture rig. The height is the
	/// point the camera should LOOK AT: framing a 20-block pylon at its feet
	/// crops the top half out of the shot, which is how the first arch capture
	/// came back as two disconnected piers.
	/// </summary>
	private static readonly Dictionary<string, (int x, int z, int h)> _anchors = new();

	/// <summary>Where a named exhibit stands, or null if the yard was not built.</summary>
	public static Vector3? Anchor(Terrain terrain, string name)
	{
		if (!Built || !_anchors.TryGetValue(name, out var a)) return null;
		int y = terrain.Level[a.z * terrain.Size + a.x];
		return new Vector3(a.x + 0.5f, y + a.h, a.z + 0.5f);
	}

	/* ================================================================
	 * The yard
	 * ================================================================ */

	private const int YardW = 150, YardD = 100;

	public static void BuildYard(Terrain terrain, int seed)
	{
		Built = false;
		LastBlockCount = 0;
		_anchors.Clear();

		if (!FindSite(terrain, out int x0, out int z0)) return;
		YardX = x0 + YardW / 2;
		YardZ = z0 + YardD / 2;

		var rng = new Rng(seed ^ 0x2477);
		// Fray and decay fields with a wavelength, shared across the yard. Never
		// a per-block hash — the standing rule; see AGENTS.md.
		var fray = new Noise2D(seed ^ 0x51CE);

		int X(int rx) => x0 + rx;
		int Z(int rz) => z0 + rz;
		void A(string name, int rx, int rz, int h) => _anchors[name] = (X(rx), Z(rz), h);

		// Row A — the vertical vocabulary.
		Column(terrain, X(10), Z(8), height: 19, broken: 0);
		Column(terrain, X(24), Z(8), height: 19, broken: 5);
		FallenColumn(terrain, X(38), Z(8), length: 13);
		A("columns", 30, 8, 8);

		Arch(terrain, X(64), Z(8), span: 8, fray);
		A("arch", 71, 9, 8);

		Pylon(terrain, X(92), Z(8), height: 20);
		ToppledPylon(terrain, X(102), Z(8), length: 12);
		A("pylon", 100, 10, 9);

		CorniceFragment(terrain, X(124), Z(8), length: 10, fray);
		A("cornice", 129, 8, 4);

		// Row B — mass, connection and enclosure.
		GrandStair(terrain, X(8), Z(38), width: 10, risers: 8, fray);
		A("stair", 13, 46, 5);

		Colonnade(terrain, X(30), Z(40), bays: 6, fray);
		A("colonnade", 44, 43, 5);

		PrecinctWall(terrain, X(68), Z(42), length: 36, fray);
		A("wall", 86, 42, 2);

		Basin(terrain, X(114), Z(38), w: 10, d: 8);
		A("basin", 119, 42, 0);

		// Row C — the ground plane.
		PavedCourt(terrain, X(8), Z(74), w: 20, d: 16, fray);
		A("court", 18, 82, 0);

		Emblem(terrain, X(40), Z(82), fray);
		A("emblem", 40, 82, 0);

		RubbleField(terrain, X(52), Z(74), w: 16, d: 16, fray);
		A("rubble", 60, 82, 0);

		Revetment(terrain, X(78), Z(78), length: 24, fray);
		A("revetment", 90, 81, 3);

		A("yard", YardW / 2, YardD / 2, 4);

		// The land takes the yard back at one age, so every part is read in the
		// same state of reclamation — states are chosen by age, not per instance.
		// 0.35, down from 0.55: the first pass turned whole columns khaki, and
		// the references keep masonry pale with moss as an ACCENT at the feet
		// and on the shaded side.
		Reclaim.Overgrow(terrain, x0, z0, YardW, YardD,
			terrain.Level[z0 * terrain.Size + x0] - 4,
			terrain.Level[z0 * terrain.Size + x0] + 24, 0.35f,
			unchecked((uint)(seed ^ 0x2477)));

		Built = true;
	}

	/// <summary>
	/// The flattest large expanse of open land, found deterministically. The
	/// exhibits foot themselves so a terrace step or two inside the yard is
	/// fine; standing water and settlements are not.
	/// </summary>
	private static bool FindSite(Terrain terrain, out int x0, out int z0)
	{
		int S = terrain.Size;
		float best = -1f;
		x0 = z0 = 0;

		for (int cz = YardD; cz < S - YardD; cz += 24)
		for (int cx = YardW; cx < S - YardW; cx += 24)
		{
			int refLevel = terrain.Level[cz * S + cx];
			if (refLevel <= Terrain.Sea + 1) continue;

			int flat = 0, total = 0;
			bool wet = false;
			for (int dz = -YardD / 2; dz <= YardD / 2 && !wet; dz += 6)
			for (int dx = -YardW / 2; dx <= YardW / 2; dx += 6)
			{
				int x = cx + dx, z = cz + dz;
				int i = z * S + x;
				total++;
				if (terrain.Land[i] == 0 || terrain.Level[i] <= Terrain.Sea + 1)
				{ wet = true; break; }
				if (Math.Abs(terrain.Level[i] - refLevel) <= Terrain.Step) flat++;
			}
			if (wet) continue;

			bool nearSite = false;
			foreach (var s in terrain.Sites)
			{
				float dx = s.X - cx, dz = s.Z - cz;
				float keep = s.Radius + YardW * 0.75f;
				if (dx * dx + dz * dz < keep * keep) { nearSite = true; break; }
			}
			if (nearSite) continue;

			// Flatness first; among equally flat candidates prefer the middle of
			// the map so the yard is cheap to reach from any spawn.
			float centre = 1f - (MathF.Abs(cx - S * 0.5f) + MathF.Abs(cz - S * 0.5f)) / S;
			float score = flat / (float)total + centre * 0.05f;
			if (score > best) { best = score; x0 = cx - YardW / 2; z0 = cz - YardD / 2; }
		}
		return best > 0.8f;
	}

	/* ================================================================
	 * Shared masonry
	 * ================================================================ */

	internal static int Ground(Terrain t, int x, int z)
	{
		int S = t.Size;
		if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) return -1;
		if (t.Land[z * S + x] == 0) return -1;
		return t.Level[z * S + x];
	}

	internal static void Put(Terrain t, int x, int y, int z, byte id)
	{
		var grid = t.Grid;
		if (!grid.InBounds(x, y, z)) return;
		grid.Set(x, y, z, id);
		LastBlockCount++;
		if (!Palette.IsSolid(id)) return;
		int i = z * t.Size + x;
		if (y + 1 > grid.Heights[i]) grid.Heights[i] = (short)(y + 1);
	}

	/// <summary>
	/// The floor a part stands on: the highest ground under its footprint. The
	/// foundation then fills every column up from its own ground, so the part
	/// sits ON the land rather than hovering over the low side of it.
	/// </summary>
	internal static int FloorAt(Terrain t, int x0, int z0, int w, int d)
	{
		int floor = -1;
		for (int z = z0; z < z0 + d; z++)
		for (int x = x0; x < x0 + w; x++)
			floor = Math.Max(floor, Ground(t, x, z));
		return floor;
	}

	internal static void Foot(Terrain t, int x, int z, int floor)
	{
		int g = Ground(t, x, z);
		if (g < 0) return;
		for (int y = g; y < floor; y++) Put(t, x, y, z, Palette.STONE);
	}

	/* ================================================================
	 * 1–3. Column: standing / stump / fallen full-length
	 * ================================================================ */

	/// <summary>
	/// A grand column. The shaft is a 3×3 with the corners left out — the
	/// one-block flutes are what give the ink vertical lines to draw, and
	/// without them a column reads as a square chimney. Two capital courses
	/// step back out to full width so the head reads against the sky.
	/// </summary>
	internal static void Column(Terrain t, int x, int z, int height, int broken)
	{
		int floor = FloorAt(t, x - 1, z - 1, 3, 3);
		if (floor < 0) return;
		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			Foot(t, x + dx, z + dz, floor);
			Put(t, x + dx, floor, z + dz, Palette.STONE);
		}

		int top = broken > 0 ? broken : height;
		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			if (dx != 0 && dz != 0) continue; // the flutes
			// A broken shaft tears, it does not slice: each rib of the stump
			// carries its own ragged height.
			int ribTop = broken > 0
				? Math.Max(2, top + (int)MathF.Round(MathF.Sin(dx * 2.1f + dz * 3.3f + x) * 1.4f))
				: top - 2;
			for (int k = 1; k <= ribTop; k++)
				Put(t, x + dx, floor + k, z + dz, k % 5 == 0 ? Palette.STONE : Palette.STONE_PALE);
		}

		if (broken > 0) return;
		// Capital: an abacus course and a warm head with the arms stepped out.
		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
			Put(t, x + dx, floor + height - 1, z + dz, Palette.STONE_PALE);
		for (int dz = -2; dz <= 2; dz++)
		for (int dx = -2; dx <= 2; dx++)
		{
			if (Math.Abs(dx) == 2 && Math.Abs(dz) == 2) continue;
			if (Math.Abs(dx) == 2 && dz != 0) continue;
			if (Math.Abs(dz) == 2 && dx != 0) continue;
			Put(t, x + dx, floor + height, z + dz, Palette.STONE_WARM);
		}
	}

	/// <summary>
	/// A 2×2 column — the middle weight of the family. The references never
	/// repeat one column type: a range mixes 1×1 posts, 2×2 shafts and 3×3
	/// fluted grand columns at different heights, and it is exactly that
	/// variety that keeps a colonnade from reading as a fence.
	/// </summary>
	internal static void Column2(Terrain t, int x, int z, int height, int broken)
	{
		int floor = FloorAt(t, x, z, 2, 2);
		if (floor < 0) return;
		for (int dz = -1; dz <= 2; dz++)
		for (int dx = -1; dx <= 2; dx++)
		{
			Foot(t, x + dx, z + dz, floor);
			Put(t, x + dx, floor, z + dz, Palette.STONE);
		}

		int top = broken > 0 ? broken : height;
		for (int dz = 0; dz <= 1; dz++)
		for (int dx = 0; dx <= 1; dx++)
		{
			int ribTop = broken > 0
				? Math.Max(1, top + (int)MathF.Round(MathF.Sin(dx * 2.7f + dz * 1.9f + z) * 1.2f))
				: top - 1;
			for (int k = 1; k <= ribTop; k++)
				Put(t, x + dx, floor + k, z + dz,
					k % 5 == 0 ? Palette.STONE : Palette.STONE_PALE);
		}
		if (broken > 0) return;
		// A one-course capital, stepped out on the axes only.
		for (int dz = -1; dz <= 2; dz++)
		for (int dx = -1; dx <= 2; dx++)
		{
			if ((dx == -1 || dx == 2) && (dz == -1 || dz == 2)) continue;
			Put(t, x + dx, floor + height, z + dz, Palette.STONE_WARM);
		}
	}

	/// <summary>A 1×1 post — balustrade weight, the smallest vertical.</summary>
	internal static void Post(Terrain t, int x, int z, int height)
	{
		int g = Ground(t, x, z);
		if (g < 0) return;
		for (int k = 0; k < height; k++)
			Put(t, x, g + k, z, k == height - 1 ? Palette.STONE_WARM : Palette.STONE_PALE);
	}

	/// <summary>
	/// A column lying full length — reference-8, and the single highest-value
	/// missing piece: a twelve-block horizontal immediately reads as something
	/// that FELL, which no amount of standing rubble does.
	/// </summary>
	internal static void FallenColumn(Terrain t, int x, int z, int length)
	{
		// The plinth and one drum stayed put.
		int floor = FloorAt(t, x - 1, z - 1, 3, 3);
		if (floor < 0) return;
		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			Foot(t, x + dx, z + dz, floor);
			Put(t, x + dx, floor, z + dz, Palette.STONE);
			if (dx == 0 || dz == 0)
				Put(t, x + dx, floor + 1, z + dz, Palette.STONE_PALE);
		}

		// The shaft lies where it landed, in drums. The breaks are gaps, and the
		// far drums drift off the line — a shaft that fell dead straight reads
		// as placed, not fallen.
		for (int l = 2; l < 2 + length; l++)
		{
			if (l == 6 || l == 11) continue; // drum breaks
			int lx = x + l;
			int shift = l >= 11 ? 1 : 0;
			int g = Math.Max(Ground(t, lx, z + shift), Ground(t, lx, z + shift + 1));
			if (g < 0) continue;
			for (int dz = 0; dz <= 1; dz++)
			for (int k = 0; k <= 1; k++)
				Put(t, lx, g + k, z + shift + dz,
					k == 1 ? Palette.STONE_PALE : Palette.STONE);
		}

		// The capital, upside down past the end of the shaft.
		int cx = x + 2 + length + 1;
		int cg = Ground(t, cx, z);
		if (cg >= 0)
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++)
				Put(t, cx + dx, cg + (dx == 0 && dz == 0 ? 1 : 0), z + dz, Palette.STONE_WARM);
	}

	/* ================================================================
	 * 4. Freestanding arch
	 * ================================================================ */

	/// <summary>
	/// A voussoired arch: two piers, a stepped soffit closing the span, a flat
	/// band across the top. The stepped underside is exactly what the reference
	/// arches are — single blocks corbelling in until they meet.
	/// </summary>
	internal static void Arch(Terrain t, int x0, int z, int span, Noise2D fray)
	{
		const int pierW = 3, depth = 2;
		int spring = 9;                      // pier height to the first voussoir
		int width = pierW * 2 + span;        // 14 across for an 8 span
		int top = spring + span / 2 + 1;     // crown height ≈ 14

		int floor = FloorAt(t, x0, z, width, depth);
		if (floor < 0) return;

		// Piers, on a one-course plinth that spreads a block each way.
		foreach (int px in new[] { x0, x0 + pierW + span })
		{
			for (int dz = -1; dz <= depth; dz++)
			for (int dx = -1; dx <= pierW; dx++)
			{
				Foot(t, px + dx, z + dz, floor);
				Put(t, px + dx, floor, z + dz, Palette.STONE);
			}
			for (int dz = 0; dz < depth; dz++)
			for (int dx = 0; dx < pierW; dx++)
			for (int k = 1; k <= spring; k++)
				Put(t, px + dx, floor + k, z + dz,
					k % 4 == 0 ? Palette.STONE : Palette.STONE_PALE);
		}

		// The soffit steps up toward the centre; everything above it to the
		// crown is filled, so the arch reads as one mass with a stepped hole.
		for (int gx = 0; gx < span; gx++)
		{
			int d = Math.Min(gx + 1, span - gx);      // distance in from the pier
			int soffit = spring + Math.Min(d, span / 2);
			for (int dz = 0; dz < depth; dz++)
			for (int y = soffit; y <= top; y++)
				Put(t, x0 + pierW + gx, floor + y, z + dz, Palette.STONE_PALE);
		}

		// The band across the top, weathering away from one end. Fray with a
		// wavelength, never per block.
		for (int dx = 0; dx < width; dx++)
		{
			if (fray.Fbm01((x0 + dx) * 0.13f, z * 0.13f, 2) < 0.34f) continue;
			for (int dz = 0; dz < depth; dz++)
				Put(t, x0 + dx, floor + top + 1, z + dz, Palette.STONE_WARM);
		}
	}

	/* ================================================================
	 * 5. Pylon / stele, standing and toppled, inscribed
	 * ================================================================ */

	/// <summary>
	/// The meander — the civilisation's signature (docs/RUINS.md §9), picked
	/// out one block deep in the face, exactly as the references do it. The
	/// recesses are what give the ink something to draw on a flat face.
	/// </summary>
	internal static bool Meander(int u, int v) => (v % 4) switch
	{
		1 => u != 2,
		3 => u != 0,
		_ => false,
	};

	/// <summary>
	/// Walking-distance stele face: a 5-wide running meander.
	/// Two-row bars filled the slab into one dark panel; one-gap-per-row read as
	/// stairs. Alternate centre-gap and end-gap bars, with single-cell posts
	/// on the in-between rows, so the key turns (reference-8) instead of
	/// hanging as two sparse stripes.
	/// </summary>
	internal static bool SteleInscription(int u, int v, int width, int band)
	{
		if (width < 3 || band < 4) return Meander(u, v);
		if ((uint)u >= (uint)width || (uint)v >= (uint)band) return false;
		return (v % 4) switch
		{
			0 => u == 0 || u == width - 1,
			1 => u != width / 2,
			2 => u == width / 2,
			3 => u != 0 && u != width - 1,
			_ => false,
		};
	}

	internal static void Pylon(Terrain t, int x, int z, int height)
	{
		const int w = 3, d = 2;
		int floor = FloorAt(t, x, z, w, d);
		if (floor < 0) return;

		for (int dz = -1; dz <= d; dz++)
		for (int dx = -1; dx <= w; dx++)
		{
			Foot(t, x + dx, z + dz, floor);
			Put(t, x + dx, floor, z + dz, Palette.STONE);
		}

		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		for (int k = 1; k <= height - 2; k++)
			Put(t, x + dx, floor + k, z + dz, Palette.STONE_PALE);

		// The glyph band, carved into the broad south face.
		for (int k = 4; k <= height - 5; k++)
		for (int dx = 0; dx < w; dx++)
			if (Meander(dx, k))
				Put(t, x + dx, floor + k, z, Palette.AIR);

		// The cap: one course proud, one course crown.
		for (int dz = -1; dz <= d; dz++)
		for (int dx = -1; dx <= w; dx++)
			Put(t, x + dx, floor + height - 1, z + dz, Palette.STONE_WARM);
		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
			Put(t, x + dx, floor + height, z + dz, Palette.STONE_PALE);
	}

	internal static void ToppledPylon(Terrain t, int x, int z, int length)
	{
		// The base broke off at the second course and stayed.
		int floor = FloorAt(t, x, z, 3, 2);
		if (floor < 0) return;
		for (int dz = 0; dz < 2; dz++)
		for (int dx = 0; dx < 3; dx++)
		{
			Foot(t, x + dx, z + dz, floor);
			Put(t, x + dx, floor, z + dz, Palette.STONE);
			Put(t, x + dx, floor + 1, z + dz, Palette.STONE_PALE);
		}

		// The shaft lies along the ground, glyph face up — reference-8's
		// toppled stele, legible as geometry rather than as a building.
		for (int l = 0; l < length; l++)
		{
			int lz = z + 3 + l;
			for (int dx = 0; dx < 3; dx++)
			{
				int g = Ground(t, x + dx, lz);
				if (g < 0) continue;
				Put(t, x + dx, g, lz, Palette.STONE);
				if (!Meander(dx, l + 1))
					Put(t, x + dx, g + 1, lz, Palette.STONE_PALE);
			}
		}
	}

	/* ================================================================
	 * 6. Grand stair (with its platform — a stair to nowhere floats)
	 * ================================================================ */

	private static void GrandStair(Terrain t, int x0, int z0, int width, int risers, Noise2D fray)
	{
		int floor = FloorAt(t, x0 - 1, z0, width + 2, risers + 14);
		if (floor < 0) return;

		// The flight, one block rise per tread, paving on top of a stone core.
		for (int s = 0; s < risers; s++)
		{
			int z = z0 + s;
			for (int dx = 0; dx < width; dx++)
			{
				int x = x0 + dx;
				int g = Ground(t, x, z);
				if (g < 0) continue;
				for (int y = g; y <= floor + s; y++)
					Put(t, x, y, z, y == floor + s ? Palette.PAVING : Palette.STONE);
			}
			// Cheek walls flanking the flight, one course proud of the treads,
			// losing their tops in lengths.
			foreach (int cx in new[] { x0 - 1, x0 + width })
			{
				int g = Ground(t, cx, z);
				if (g < 0) continue;
				int h = fray.Fbm01(cx * 0.09f, z * 0.09f, 2) > 0.42f ? 1 : 0;
				for (int y = g; y <= floor + s + h; y++)
					Put(t, cx, y, z, Palette.STONE_PALE);
			}
		}

		// The platform the stair earns: a solid terrace at the head, its faces
		// the same stone as the stair — terrain and architecture as one system.
		int py = floor + risers;
		for (int dz = 0; dz < 12; dz++)
		for (int dx = -1; dx <= width; dx++)
		{
			int x = x0 + dx, z = z0 + risers + dz;
			int g = Ground(t, x, z);
			if (g < 0) continue;
			bool edge = dx == -1 || dx == width || dz == 11;
			for (int y = g; y <= py; y++)
				Put(t, x, y, z, y == py && !edge ? Palette.PAVING
					: edge && y == py ? Palette.STONE_PALE : Palette.STONE);
		}
	}

	/* ================================================================
	 * 7. Terrace revetment
	 * ================================================================ */

	/// <summary>
	/// A masonry face holding up a grass terrace — the reference-2/11 fact that
	/// terrain and architecture are the same stone doing the same job. One
	/// section has failed, and the earth behind it slumps through the gap.
	/// </summary>
	private static void Revetment(Terrain t, int x0, int z0, int length, Noise2D fray)
	{
		const int height = 5, depth = 6;
		int floor = FloorAt(t, x0, z0, length, depth + 1);
		if (floor < 0) return;

		for (int dx = 0; dx < length; dx++)
		{
			int x = x0 + dx;
			// The failed length, chosen by the field: one coherent slump, not
			// scattered holes.
			bool fallen = fray.Fbm01(x * 0.055f, z0 * 0.055f, 2) < 0.36f;
			int wallTop = fallen ? floor + 2 : floor + height;

			int g = Ground(t, x, z0);
			if (g < 0) continue;
			// Coursed: a pale band every fourth course, so the face reads as
			// laid masonry rather than a blank slab — the same convention as
			// the precinct's conform fill.
			for (int y = g; y <= wallTop; y++)
				Put(t, x, y, z0, y == wallTop || (y & 3) == 3
					? Palette.STONE_PALE : Palette.STONE);
			if (fallen)
			{
				// The spill at the foot of the break.
				int fg = Ground(t, x, z0 - 1);
				if (fg >= 0) Put(t, x, fg, z0 - 1, Palette.RUBBLE);
			}

			// The earth the wall retains, capped with living grass. It STEPS DOWN
			// toward the back to meet the land, the way a natural terrace does —
			// the first pass held full height for the whole depth and the fill
			// read as a red soil box with a lid, the single worst object in the
			// capture. Behind a revetment is a hillside, not a bunker.
			for (int dz = 1; dz <= depth; dz++)
			{
				int z = z0 + dz;
				int eg = Ground(t, x, z);
				if (eg < 0) continue;
				int drop = Math.Max(0, dz - 2) * Terrain.Step / 2;
				int terrace = (fallen ? wallTop + Math.Min(dz, 2) - 1 : floor + height) - drop;
				terrace = Math.Min(terrace, floor + height);
				if (terrace <= eg) continue;
				for (int y = eg; y <= terrace; y++)
					Put(t, x, y, z, y == terrace ? Palette.GRASS : Palette.SOIL);
			}
		}
	}

	/* ================================================================
	 * 8. Long precinct wall
	 * ================================================================ */

	/// <summary>
	/// The connective tissue — reference-9's lesson that long walls between
	/// monuments are what turn a scatter into a place. A gate at the middle,
	/// stelae posted along the run, and breaches that come in lengths.
	/// </summary>
	private static void PrecinctWall(Terrain t, int x0, int z, int length, Noise2D fray)
	{
		int gate = x0 + length / 2;

		for (int dx = 0; dx < length; dx++)
		{
			int x = x0 + dx;
			if (Math.Abs(x - gate) <= 1)
			{
				// The gate jambs, standing taller than the wall they interrupt,
				// carrying a lintel.
				if (Math.Abs(x - gate) == 1)
				{
					int jg = Ground(t, x, z);
					if (jg < 0) continue;
					for (int k = 0; k < 6; k++)
						Put(t, x, jg + k, z, k == 5 ? Palette.STONE_WARM : Palette.STONE_PALE);
				}
				else
				{
					int lg = Ground(t, x, z);
					if (lg >= 0) Put(t, x, lg + 5, z, Palette.STONE_WARM);
				}
				continue;
			}

			int g = Ground(t, x, z);
			if (g < 0) continue;
			// Sections fail in lengths; the slow term carries the shape.
			float wave = 0.5f + 0.35f * MathF.Sin(dx * 0.22f)
			                  + 0.15f * MathF.Sin(dx * 0.71f);
			int standing = (int)MathF.Round(3.4f * (1f - 0.55f * wave));
			for (int k = 0; k < standing; k++)
				Put(t, x, g + k, z, k == 0 ? Palette.STONE : Palette.STONE_PALE);
			if (standing == 0 && fray.Fbm01(x * 0.2f, z * 0.2f, 2) > 0.5f)
				Put(t, x, g, z, Palette.RUBBLE);
		}

		// Stelae posted along the wall, the reference-9 rhythm.
		foreach (int sx in new[] { x0 + 5, x0 + length - 6 })
		{
			int g = Ground(t, sx, z + 2);
			if (g < 0) continue;
			for (int k = 0; k < 4; k++)
				Put(t, sx, g + k, z + 2, k == 3 ? Palette.STONE_WARM : Palette.STONE_PALE);
		}
	}

	/* ================================================================
	 * 9. Paved court with a ragged edge
	 * ================================================================ */

	/// <summary>
	/// Paving that survives as a STAIN — holes inside, frayed at the edge, set
	/// into the ground rather than laid on it. Paving that stops in a clean
	/// rectangle reads as a floor tile; paving that frays reads as time.
	/// </summary>
	internal static void PavedCourt(Terrain t, int x0, int z0, int w, int d, Noise2D fray)
	{
		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			int x = x0 + dx, z = z0 + dz;
			int g = Ground(t, x, z);
			if (g < 0) continue;

			int edge = Math.Min(Math.Min(dx, w - 1 - dx), Math.Min(dz, d - 1 - dz));
			float f = fray.Fbm01(x * 0.11f, z * 0.11f, 2);
			// Interior holes where the field dips; the perimeter needs more of
			// the field to survive, which is what makes the edge ragged.
			float need = edge >= 4 ? 0.22f : 0.55f - edge * 0.09f;
			if (f < need) continue;

			byte paver = fray.Fbm01(x * 0.07f + 40f, z * 0.07f, 2) > 0.72f
				? Palette.STONE_WARM : Palette.PAVING;
			t.Grid.Set(x, g - 1, z, paver);
			LastBlockCount++;
		}
	}

	/* ================================================================
	 * 10. Sunken basin
	 * ================================================================ */

	private static void Basin(Terrain t, int x0, int z0, int w, int d)
	{
		int floor = FloorAt(t, x0, z0, w, d);
		if (floor < 0) return;

		for (int dz = 0; dz < d; dz++)
		for (int dx = 0; dx < w; dx++)
		{
			int x = x0 + dx, z = z0 + dz;
			bool rim = dx == 0 || dz == 0 || dx == w - 1 || dz == d - 1;
			if (rim)
			{
				// The coping, one course proud of the ground.
				int g = Ground(t, x, z);
				if (g >= 0) Put(t, x, g, z, Palette.STONE_PALE);
				continue;
			}
			bool lining = dx == 1 || dz == 1 || dx == w - 2 || dz == d - 2;
			if (lining)
			{
				Put(t, x, floor - 1, z, Palette.STONE);
				Put(t, x, floor - 2, z, Palette.STONE);
				t.Grid.Set(x, floor - 3, z, Palette.STONE);
			}
			else
			{
				// The open pit — carved AIR is a real edit and survives lookup.
				Put(t, x, floor - 1, z, Palette.AIR);
				Put(t, x, floor - 2, z, Palette.AIR);
				t.Grid.Set(x, floor - 3, z, Palette.PAVING);
			}
		}

		// Steps down at one end, so the pit is somewhere you go, not a trap.
		Put(t, x0 + 2, floor - 2, z0 + d / 2, Palette.STONE_PALE);
		Put(t, x0 + 3, floor - 2, z0 + d / 2, Palette.AIR);
		t.Grid.Set(x0 + 3, floor - 3, z0 + d / 2, Palette.STONE_PALE);
	}

	/* ================================================================
	 * 11. Corbelled capital and cornice
	 * ================================================================ */

	/// <summary>
	/// The cornice as a wall fragment: the top two courses step out, which is
	/// the whole trick — two or three stepped courses are what the reference
	/// capitals and cornices actually are.
	/// </summary>
	private static void CorniceFragment(Terrain t, int x0, int z, int length, Noise2D fray)
	{
		int floor = FloorAt(t, x0, z, length, 1);
		if (floor < 0) return;

		for (int dx = 0; dx < length; dx++)
		{
			int x = x0 + dx;
			Foot(t, x, z, floor);
			// One end has come down; the break is a length, not a sieve.
			int h = fray.Fbm01(x * 0.08f, z * 0.08f, 2) > 0.38f ? 5 : 3;
			for (int k = 0; k <= h; k++)
				Put(t, x, floor + k, z, k == 0 ? Palette.STONE : Palette.STONE_PALE);
			if (h < 5) continue;
			// The corbel courses, stepping the wall head out to three thick.
			Put(t, x, floor + 6, z - 1, Palette.STONE_PALE);
			Put(t, x, floor + 6, z, Palette.STONE_PALE);
			Put(t, x, floor + 6, z + 1, Palette.STONE_PALE);
			Put(t, x, floor + 7, z, Palette.STONE_WARM);
		}
	}

	/* ================================================================
	 * 12. Colonnade range on a stylobate
	 * ================================================================ */

	internal static void Colonnade(Terrain t, int x0, int z0, int bays, Noise2D fray)
	{
		const int spacing = 4, colH = 9;
		int length = bays * spacing + 2;
		int floor = FloorAt(t, x0 - 1, z0 - 1, length + 2, 6);
		if (floor < 0) return;

		// The stylobate: a two-course platform with a one-course apron, so the
		// range stands on architecture rather than on lawn.
		for (int dz = -1; dz <= 4; dz++)
		for (int dx = -1; dx <= length; dx++)
		{
			int x = x0 + dx, z = z0 + dz;
			int g = Ground(t, x, z);
			if (g < 0) continue;
			bool apron = dx == -1 || dx == length || dz == -1 || dz == 4;
			for (int y = g; y <= (apron ? floor : floor + 1); y++)
				Put(t, x, y, z, y == floor + 1 ? Palette.PAVING : Palette.STONE);
		}

		// The columns, in states the same age chose: most stand, one is a
		// stump, and one lies across the stylobate where it fell.
		var standing = new bool[bays + 1];
		for (int c = 0; c <= bays; c++)
		{
			int cx = x0 + c * spacing;
			if (c == 2)
			{
				// The stump.
				for (int k = 2; k <= 4; k++)
				for (int dz = 0; dz <= 1; dz++)
				for (int dx = 0; dx <= 1; dx++)
					Put(t, cx + dx, floor + k, z0 + 1 + dz, Palette.STONE_PALE);
				continue;
			}
			if (c == 4)
			{
				// The fallen one, lying across the platform and off its edge.
				for (int l = 0; l < 7; l++)
				{
					if (l == 3) continue;
					int z = z0 + 1 + l;
					int rest = l <= 3 ? floor + 2 : Ground(t, cx, z);
					if (rest < 0) continue;
					for (int dx = 0; dx <= 1; dx++)
					for (int k = 0; k <= 1; k++)
						Put(t, cx + dx, rest + k, z, k == 1 ? Palette.STONE_PALE : Palette.STONE);
				}
				continue;
			}
			standing[c] = true;
			for (int k = 2; k <= colH; k++)
			for (int dz = 0; dz <= 1; dz++)
			for (int dx = 0; dx <= 1; dx++)
				Put(t, cx + dx, floor + k, z0 + 1 + dz,
					k == colH ? Palette.STONE_WARM : Palette.STONE_PALE);
		}

		// Architrave beams survive only between neighbours that both stand.
		for (int c = 0; c < bays; c++)
		{
			if (!standing[c] || !standing[c + 1]) continue;
			if (fray.Fbm01(c * 1.7f, z0 * 0.1f, 2) < 0.3f) continue;
			for (int dx = 0; dx < spacing + 2; dx++)
			for (int dz = 0; dz <= 1; dz++)
				Put(t, x0 + c * spacing + dx, floor + colH + 1, z0 + 1 + dz, Palette.STONE_PALE);
		}
	}

	/* ================================================================
	 * 13. Circular floor emblem
	 * ================================================================ */

	internal static void Emblem(Terrain t, int cx, int cz, Noise2D fray)
	{
		for (int dz = -6; dz <= 6; dz++)
		for (int dx = -6; dx <= 6; dx++)
		{
			float r = MathF.Sqrt(dx * dx + dz * dz);
			if (r > 5.6f) continue;
			int x = cx + dx, z = cz + dz;
			int g = Ground(t, x, z);
			if (g < 0) continue;
			// The outermost ring frays into the grass; the centre survives.
			if (r > 4.4f && fray.Fbm01(x * 0.14f, z * 0.14f, 2) < 0.42f) continue;

			// All stone, no moss ring: the author called the mossy pass off —
			// the reference masonry is bare pale grey, and reclamation returns
			// later as its own judged layer, not baked into parts.
			byte ring = r <= 1.6f ? Palette.STONE_WARM
				: r <= 2.8f ? Palette.PAVING
				: r <= 3.8f ? Palette.STONE_PALE
				: r <= 4.8f ? Palette.PAVING : Palette.STONE;
			t.Grid.Set(x, g - 1, z, ring);
			LastBlockCount++;
		}
	}

	/* ================================================================
	 * 14. Rubble field
	 * ================================================================ */

	/// <summary>
	/// Collapse as a region, not confetti: heaps where the field is high,
	/// single dressed blocks at its skirts, nothing where it is low.
	/// </summary>
	internal static void RubbleField(Terrain t, int x0, int z0, int w, int d, Noise2D fray)
	{
		for (int dz = 0; dz < d; dz += 2)
		for (int dx = 0; dx < w; dx += 2)
		{
			// Jittered off the sampling lattice: the first pass placed every
			// block on the even grid and the "collapse" came back as a polka
			// dot pattern readable from the overview shot.
			int x = x0 + dx + (int)(fray.Fbm01(dx * 1.3f, dz * 0.7f + 9f, 2) * 3f) - 1;
			int z = z0 + dz + (int)(fray.Fbm01(dx * 0.7f + 5f, dz * 1.3f, 2) * 3f) - 1;
			float f = fray.Fbm01(x * 0.09f + 80f, z * 0.09f, 2);
			if (f < 0.55f) continue;
			int g = Ground(t, x, z);
			if (g < 0) continue;

			if (f > 0.72f)
			{
				// A heap, with its spill.
				Put(t, x, g, z, Palette.RUBBLE);
				Put(t, x, g + 1, z, Palette.RUBBLE);
				Put(t, x + 1, g, z, Palette.RUBBLE);
				Put(t, x, g, z + 1, Palette.STONE);
			}
			else if (f > 0.64f)
			{
				// A dressed block that landed whole.
				Put(t, x, g, z, Palette.STONE_PALE);
			}
			else
			{
				Put(t, x, g, z, Palette.RUBBLE);
			}
		}
	}
}
