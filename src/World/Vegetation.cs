using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Trees and undergrowth.
///
/// Biome drives *species*, not just density. It used to drive density alone, so
/// a sakura province was a meadow with more trees in it and you could not tell
/// one from the other by looking — which defeats the point of having provinces.
///
/// Crowns are compact asymmetric stacks of solid cuboids. Never rings, donuts,
/// noisy voxel clouds, or repeated grid-like layouts: the silhouette has to
/// look sculpted, and it is the only thing describing the form once the faces
/// are flat colour.
/// </summary>
public static class Vegetation
{
	private struct Flora
	{
		public byte[] Canopy;
		public float ScaleLo, ScaleHi;
		public float Density;
	}

	private static Flora For(Biome b) => b switch
	{
		Biome.Sakura => new Flora
		{
			Canopy = new[] { Palette.LEAF_PINK, Palette.LEAF_BLUSH, Palette.LEAF_ROSE, Palette.LEAF_PINK },
			ScaleLo = 0.42f, ScaleHi = 1.0f, Density = 1.65f,
		},
		Biome.Meadow => new Flora
		{
			Canopy = new[] { Palette.LEAF_CREAM, Palette.LEAF_BLUSH, Palette.LEAF_LILAC },
			ScaleLo = 0.20f, ScaleHi = 0.92f, Density = 0.55f,
		},
		Biome.Forest => new Flora
		{
			Canopy = new[] { Palette.LEAF_LILAC, Palette.LEAF_MINT, Palette.LEAF_CREAM, Palette.LEAF_LILAC },
			ScaleLo = 0.48f, ScaleHi = 1.0f, Density = 1.22f,
		},
		Biome.Plains => new Flora
		{
			Canopy = new[] { Palette.LEAF_CREAM, Palette.LEAF_BLUSH },
			ScaleLo = 0.18f, ScaleHi = 0.68f, Density = 0.20f,
		},
		Biome.Wetland => new Flora
		{
			Canopy = new[] { Palette.LEAF_MINT, Palette.LEAF_CREAM, Palette.LEAF_MINT },
			ScaleLo = 0.26f, ScaleHi = 0.78f, Density = 0.75f,
		},
		Biome.Highland => new Flora
		{
			Canopy = new[] { Palette.LEAF_LILAC, Palette.LEAF_MINT },
			ScaleLo = 0.14f, ScaleHi = 0.46f, Density = 0.28f,
		},
		Biome.SnowyHills => new Flora
		{
			Canopy = new[] { Palette.LEAF_MINT, Palette.LEAF_LILAC },
			ScaleLo = 0.16f, ScaleHi = 0.44f, Density = 0.10f,
		},
		_ => new Flora
		{
			Canopy = new[] { Palette.LEAF_CREAM, Palette.LEAF_BLUSH },
			ScaleLo = 0.16f, ScaleHi = 0.60f, Density = 0.22f,
		},
	};

	/// <summary>Tree count from the last run, for the boot diagnostics.</summary>
	public static int LastTreeCount;

	public static void Populate(Terrain terrain, int seed)
	{
		LastTreeCount = 0;
		var grid = terrain.Grid;
		int S = terrain.Size;
		var rng = new Rng(seed ^ 0x7EE5);
		var nGrove = new Noise2D(seed + 21);
		var nHue = new Noise2D(seed + 22);

		// Scatter on a jittered lattice rather than by rejection sampling: it
		// costs one pass and cannot clump.
		const int Cell = 4;
		for (int cz = 0; cz < S / Cell; cz++)
		for (int cx = 0; cx < S / Cell; cx++)
		{
			int x = cx * Cell + (int)(rng.Next() * Cell);
			int z = cz * Cell + (int)(rng.Next() * Cell);
			if (x < 4 || z < 4 || x >= S - 4 || z >= S - 4) continue;

			int i = z * S + x;
			if (terrain.Land[i] == 0) continue;
			if (terrain.Plan.Definition.ReservesNaturalDetail(x / (float)S, z / (float)S, 5f / S)) continue;
			// Roads and their verges. A canopy closing over a route turns it into
			// a tunnel and hides the one thing the player navigates by.
			if (terrain.Roads != null && terrain.Roads.Clear[i] != 0) continue;
			int h = terrain.Level[i];
			if (h <= Terrain.Sea + 1) continue;
			if (terrain.StairMask[i] == 1) continue;

			// Structures and paths own their composition before scatter does. A
			// raised column marks bridge decks, rails, lanterns and later authored
			// props; reserve a small apron around all of them so a tree never grows
			// through a crossing or turns its approach into a tunnel.
			bool reserved = false;
			// Six blocks also accounts for the footprint of a large canopy whose
			// trunk stands outside the immediate prop buffer.
			for (int rz = -6; rz <= 6 && !reserved; rz++)
			for (int rx = -6; rx <= 6; rx++)
			{
				int xx = x + rx, zz = z + rz;
				if (xx < 0 || zz < 0 || xx >= S || zz >= S) { reserved = true; break; }
				int ri = zz * S + xx;
				if (grid.Heights[ri] > terrain.Level[ri]) { reserved = true; break; }
			}
			if (reserved) continue;

			byte surface = grid.At(x, h - 1, z);
			bool plantable = Palette.IsGrassSurface(surface) || surface == Palette.MOSS ||
				surface == Palette.BLOSSOM_DRIFT;
			if (!plantable) continue;
			// Never on a lip: a tree hanging over a cliff edge reads as an
			// accident, and it breaks the clean terrace silhouette.
			if (TerrainShape.DropBelow(terrain.Level, S, x, z) > 1) continue;
			if (TerrainShape.RiseAbove(terrain.Level, S, x, z) > 1) continue;

			var region = terrain.Plan.RegionAt(x, z);
			var flora = For(region.Biome);

			// The grove field already exceeds 1 in its cores, where the chance
			// test saturates — so a 1.65x multiplier added no trees at all and
			// the biomes arrived with a 1.3x spread instead of the 3x they
			// specify. Clamping first is what makes the plan visible on the
			// ground.
			float dens = nGrove.Fbm01(x * 0.035f, z * 0.035f, 3);
			dens = MathF.Min(dens, 0.55f) * flora.Density;
			if (!rng.Chance(dens * 0.36f)) continue;

			float scale = rng.Range(flora.ScaleLo, flora.ScaleHi);
			// Hue varies only *within* the biome's palette, so neighbouring
			// stands differ without ever crossing into another province.
			float hue = nHue.Fbm01(x * 0.09f, z * 0.09f, 2);
			byte leaf = flora.Canopy[Math.Min(flora.Canopy.Length - 1,
				(int)(hue * flora.Canopy.Length))];

			Tree(grid, rng, x, h, z, scale, leaf);
			LastTreeCount++;
		}
	}

	private static byte PaleLeaf(byte leaf) => leaf switch
	{
		Palette.LEAF_PINK => Palette.LEAF_BLUSH,
		Palette.LEAF_ROSE => Palette.LEAF_PINK,
		Palette.LEAF_LILAC => Palette.LEAF_BLUSH,
		Palette.LEAF_CREAM => Palette.LEAF_BLUSH,
		Palette.LEAF_BLUSH => Palette.LEAF_CREAM,
		_ => leaf,
	};

	/// <summary>
	/// The canopy grammar: a compact assembly of cuboids, grown rather than
	/// stamped.
	///
	/// Two rules govern it.
	///
	/// **Every lobe is placed against the mass that already exists.** The offset
	/// range is derived from the anchor's extent so the two boxes are guaranteed
	/// to share at least one block on all three axes. The previous grammar was a
	/// fixed list of literal offsets, and its topmost lobe sat a whole level
	/// above anything else in the tree — so every large canopy carried one cube
	/// hanging unsupported in the air.
	///
	/// **Nothing about the arrangement is fixed.** The old grammar had exactly
	/// three shapes, mirrored four ways, which is why a grove read as the same
	/// tree stamped over and over. Core size, lobe count, lobe sizes, offsets
	/// and which lobes take the paler leaf are all drawn per tree.
	/// </summary>
	private static void Tree(VoxelGrid grid, Rng rng, int x, int y, int z,
		float scale, byte leaf)
	{
		int tw = scale >= 0.84f ? 2 : 1;
		int bare = Math.Max(2, (int)MathF.Round(2.2f + scale * 4.4f + rng.Bell() * 0.9f));
		int canopyY = y + bare;
		if (canopyY + 8 >= grid.Height) return;

		byte trunk = rng.Chance(0.55f) ? Palette.TRUNK
			: rng.Chance(0.5f) ? Palette.TRUNK_PALE : Palette.TRUNK_ROSE;
		byte light = PaleLeaf(leaf);

		int trunkOffset = -(tw - 1) / 2;
		for (int dz = 0; dz < tw; dz++)
		for (int dx = 0; dx < tw; dx++)
			grid.Column(x + trunkOffset + dx, z + trunkOffset + dz, y, canopyY + 1, trunk);

		void Fill(int ox, int oy, int oz, int sx, int sy, int sz, byte id)
		{
			int px = x + ox - sx / 2;
			int py = canopyY + oy;
			int pz = z + oz - sz / 2;
			for (int by = 0; by < sy; by++)
			for (int bz = 0; bz < sz; bz++)
			for (int bx = 0; bx < sx; bx++)
			{
				int xx = px + bx, yy = py + by, zz = pz + bz;
				if (!grid.InBounds(xx, yy, zz)) continue;
				grid.Set(xx, yy, zz, id);
				int col = zz * grid.Size + xx;
				if (yy + 1 > grid.Heights[col]) grid.Heights[col] = (short)(yy + 1);
			}
		}

		// A box occupies [o - s/2, o - s/2 + s - 1] on each axis. Overlapping the
		// anchor on an axis means picking the new centre inside this window; the
		// arithmetic is the containment test rearranged for the unknown.
		(int lo, int hi) Window(int anchorOffset, int anchorSize, int size) => (
			anchorOffset - anchorSize / 2 + size / 2 - size + 1,
			anchorOffset + anchorSize - 1 - anchorSize / 2 + size / 2);

		var boxes = new List<(int ox, int oy, int oz, int sx, int sy, int sz)>(8);

		// A modest heart with many small lobes around it, not one broad slab with
		// a couple of bumps. The reference crowns are a dozen visible cuboids
		// with notches bitten between them; a large core swallows the lobes and
		// the tree reads as a lollipop.
		int coreW = Math.Max(2, (int)MathF.Round(2.2f + scale * 2.2f));
		int coreD = Math.Max(2, coreW - rng.RangeInt(0, 1));
		int coreH = scale >= 0.5f ? 2 : 1;
		boxes.Add((0, 0, 0, coreW, coreH, coreD));
		Fill(0, 0, 0, coreW, coreH, coreD, leaf);

		int lobes = (scale >= 0.6f ? 5 : 3) + rng.RangeInt(0, 3);
		for (int k = 0; k < lobes; k++)
		{
			// Anchoring on the most recent boxes as often as the core is what
			// makes a crown sprawl and bud rather than radiate from one centre.
			var anchor = boxes[rng.Chance(0.45f) ? 0 : rng.RangeInt(0, boxes.Count - 1)];
			int sx = 2 + rng.RangeInt(0, 1);
			int sz = 2 + rng.RangeInt(0, 1);
			int sy = rng.Chance(0.42f) ? 2 : 1;

			var wx = Window(anchor.ox, anchor.sx, sx);
			var wy = Window(anchor.oy, anchor.sy, sy);
			var wz = Window(anchor.oz, anchor.sz, sz);
			int ox = rng.RangeInt(wx.lo, wx.hi);
			int oz = rng.RangeInt(wz.lo, wz.hi);
			// Bias upward: a crown grows over its own shoulders, and lobes that
			// hang below the core read as a bush swallowing the trunk.
			int oy = rng.Chance(0.78f) ? Math.Max(wy.lo, Math.Min(wy.hi, anchor.oy + 1))
			                           : rng.RangeInt(wy.lo, wy.hi);
			if (oy < 0) oy = 0;

			boxes.Add((ox, oy, oz, sx, sy, sz));
			Fill(ox, oy, oz, sx, sy, sz, rng.Chance(0.45f) ? light : leaf);
		}
	}
}
