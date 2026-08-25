using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Sub-voxel ground detail — grass tufts, flowers, pebbles, reeds, lichen,
/// fallen petals.
///
/// The reference images are covered in tiny handcrafted marks. None of it is
/// legible as an object from the play camera; all of it is what makes a grass
/// shelf read as *made* rather than as fill. A voxel is a metre here, so this
/// layer cannot be blocks: it is one merged mesh per chunk of cross-quads and
/// sub-voxel boxes.
///
/// Four decisions carry the whole look, and getting any of them wrong turns the
/// meadow into something else:
///
///   * A tuft is a CROSSED PAIR of blades, not a single quad and not a fan.
///   * Blade normals point straight UP, not out of the quad. Lit like the
///     ground they grow from, blades stay inside the high-key band; lit as
///     vertical surfaces they turn into dark slivers and a shelf reads as
///     gravel.
///   * A blade tapers only to 0.62 of its base width. Taper it to a point and
///     every tuft reads as a tiny conifer.
///   * Detail arrives in sparse CLUMPS — a few percent of columns, one to three
///     blades each — never as an even dusting over every column. An even
///     dusting is a lawn; clumps are a meadow.
///
/// Wind lives in the vertex shader, driven by a per-vertex sway weight (0 at
/// the root, 1 at the tip) and a per-clump phase, so a whole meadow ripples
/// rather than pulsing in lockstep.
/// </summary>
public static class GroundDetail
{
	/// <summary>World seed, so the scatter fields agree with the terrain's.</summary>
	public static int Seed;

	private static Noise2D _meadow, _flowers;

	/// <summary>Face brightness ramp for sub-voxel boxes, matching the voxel shader's.</summary>
	private const float ShadeTop = 1.0f, ShadeSide = 0.88f, ShadeSideZ = 0.82f, ShadeBottom = 0.7f;

	/// <summary>
	/// A deterministic per-column draw sequence. Seeded from the column itself
	/// rather than shared, so any chunk can be built in any order and a shelf
	/// scatters identically every time it streams back in.
	/// </summary>
	private struct Draw
	{
		private uint _s;

		public Draw(int x, int z, int salt)
		{
			unchecked
			{
				uint h = (uint)(x * 374761393) + (uint)(z * 668265263) + (uint)(salt * 1442695040);
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
		public int Int(int a, int b) => a + (int)(Next() * (b - a + 1));
		public bool Chance(float p) => Next() < p;
		public T Pick<T>(T[] items) => items[Math.Min(items.Length - 1, (int)(Next() * items.Length))];
	}

	private sealed class Field
	{
		public readonly List<Vector3> Pos = new(4096);
		public readonly List<Vector3> Nrm = new(4096);
		public readonly List<Color> Col = new(4096);
		public readonly List<float> Det = new(8192);   // (sway, phase) per vertex
		public readonly List<int> Idx = new(6144);

		public bool Empty => Pos.Count == 0;

		/// <summary>One quad. `sway` applies to the last two corners — the top edge.</summary>
		public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n,
			Color bottom, Color top, float sway, float phase)
		{
			int i = Pos.Count;
			Pos.Add(a); Pos.Add(b); Pos.Add(c); Pos.Add(d);
			for (int k = 0; k < 4; k++) Nrm.Add(n);
			Col.Add(bottom); Col.Add(bottom); Col.Add(top); Col.Add(top);
			Det.Add(0f); Det.Add(phase);
			Det.Add(0f); Det.Add(phase);
			Det.Add(sway); Det.Add(phase);
			Det.Add(sway); Det.Add(phase);
			Idx.Add(i); Idx.Add(i + 1); Idx.Add(i + 2);
			Idx.Add(i); Idx.Add(i + 2); Idx.Add(i + 3);
		}

		/// <summary>A crossed pair of vertical blades — the workhorse.</summary>
		public void Tuft(float x, float y, float z, float w, float h,
			Color bottom, Color top, float phase, float leanX = 0f, float leanZ = 0f, float sway = 1f)
		{
			float hw = w * 0.5f;
			for (int k = 0; k < 2; k++)
			{
				float ax = k == 0 ? hw : 0f;
				float az = k == 0 ? 0f : hw;
				float tx = x + leanX, tz = z + leanZ;
				Quad(
					new Vector3(x - ax, y, z - az),
					new Vector3(x + ax, y, z + az),
					new Vector3(tx + ax * 0.62f, y + h, tz + az * 0.62f),
					new Vector3(tx - ax * 0.62f, y + h, tz - az * 0.62f),
					Vector3.Up, bottom, top, sway, phase);
			}
		}

		/// <summary>A sub-voxel box: pebbles, flower heads, lichen, tiny fungus caps.</summary>
		public void Box(float cx, float y, float cz, float sx, float sy, float sz,
			Color color, float sway = 0f, float phase = 0f)
		{
			float x0 = cx - sx * 0.5f, x1 = cx + sx * 0.5f;
			float y0 = y, y1 = y + sy;
			float z0 = cz - sz * 0.5f, z1 = cz + sz * 0.5f;

			Color Shade(float m) => new(color.R * m, color.G * m, color.B * m);
			var top = Shade(ShadeTop);
			var bot = Shade(ShadeBottom);
			var sxc = Shade(ShadeSide);
			var szc = Shade(ShadeSideZ);

			Quad(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), Vector3.Up, top, top, sway, phase);
			Quad(new(x0, y0, z1), new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), Vector3.Down, bot, bot, 0f, phase);
			Quad(new(x1, y0, z0), new(x1, y0, z1), new(x1, y1, z1), new(x1, y1, z0), Vector3.Right, sxc, sxc, sway, phase);
			Quad(new(x0, y0, z1), new(x0, y0, z0), new(x0, y1, z0), new(x0, y1, z1), Vector3.Left, sxc, sxc, sway, phase);
			Quad(new(x1, y0, z1), new(x0, y0, z1), new(x0, y1, z1), new(x1, y1, z1), Vector3.Back, szc, szc, sway, phase);
			Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), Vector3.Forward, szc, szc, 0f, phase);
		}

		/// <summary>
		/// A strand hanging DOWN a wall face — the vine primitive.
		///
		/// Everything about it is the tuft inverted. The moving end is the bottom,
		/// so the corner order puts the hanging tip in the last pair, which is
		/// where <see cref="Quad"/> applies sway. The plane holds the wall's
		/// tangent and up, so the strand lies flat against the masonry rather than
		/// standing off it.
		///
		/// The normal is mostly UP with a lean out of the wall. Straight out, and
		/// a curtain of vine is lit as a vertical surface and goes to dark slivers
		/// for the same reason blades do; straight up, and it stops reading as
		/// attached to anything.
		/// </summary>
		public void Drape(float x, float y, float z, float fx, float fz,
			float length, float width, Color attach, Color tip, float phase)
		{
			float tx = -fz, tz = fx;             // along the wall
			float hw = width * 0.5f;
			var n = new Vector3(fx * 0.45f, 0.89f, fz * 0.45f).Normalized();
			float bottom = y - length;
			// A vine hangs, so it drifts away from the wall as it falls.
			float swing = 0.10f + length * 0.06f;

			Quad(
				new Vector3(x - tx * hw, y, z - tz * hw),
				new Vector3(x + tx * hw, y, z + tz * hw),
				new Vector3(x + tx * hw * 0.7f + fx * swing, bottom, z + tz * hw * 0.7f + fz * swing),
				new Vector3(x - tx * hw * 0.7f + fx * swing, bottom, z - tz * hw * 0.7f + fz * swing),
				n, attach, tip, 1f, phase);
		}

		/// <summary>A tiny leaf or petal lying flat on the ground, rotated in plan.</summary>
		public void Fleck(float x, float y, float z, float length, float width, float rot, Color color)
		{
			var along = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * (length * 0.5f);
			var across = new Vector2(-along.Y, along.X).Normalized() * (width * 0.5f);
			Quad(
				new(x - along.X, y, z - along.Y),
				new(x + across.X, y, z + across.Y),
				new(x + along.X, y, z + along.Y),
				new(x - across.X, y, z - across.Y),
				Vector3.Up, color, color, 0f, 0f);
		}

		public ArrayMesh Build()
		{
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = Pos.ToArray();
			arrays[(int)Mesh.ArrayType.Normal] = Nrm.ToArray();
			arrays[(int)Mesh.ArrayType.Color] = Col.ToArray();
			arrays[(int)Mesh.ArrayType.Custom0] = Det.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = Idx.ToArray();

			// Two floats per vertex in CUSTOM0: sway weight and clump phase.
			ulong fmt = (ulong)Mesh.ArrayCustomFormat.RgFloat << 13;

			var mesh = new ArrayMesh();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
				(Mesh.ArrayFormat)fmt);
			return mesh;
		}
	}

	private static Color Srgb(uint hex) => new Color(
		((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f).SrgbToLinear();

	private static readonly Color[] FlowerTops =
	{
		Srgb(0xf6c3d4), Srgb(0xf2dcc0), Srgb(0xe2cdf3), Srgb(0xfaeaf0), Srgb(0xf3b9c6),
	};
	private static readonly Color[] PebbleTops =
	{
		Srgb(0xd6d0e2), Srgb(0xcfc7db), Srgb(0xded7e6),
	};
	private static readonly Color[] PadColors =
	{
		Srgb(0x93ad72), Srgb(0xa3b97f), Srgb(0x87a267),
	};
	private static readonly Color TuftBase = Srgb(0x8f9e66);
	private static readonly Color ReedBase = Srgb(0x9aa877);
	private static readonly Color ReedTip = Srgb(0xc3cf9c);
	private static readonly Color Lichen = Srgb(0xa8b782);

	// Reclamation greens (plan.md §11a.4). Deliberately deeper and less pastel
	// than the meadow's: growth taking a building back is the one green in this
	// world allowed to look vigorous, and against pale masonry it has to carry
	// enough weight to read as mass rather than as a stain.
	// Deeper than the meadow, but only just.
	//
	// The first pass used a genuine forest green (0x6b855a) on the reasoning that
	// growth taking a building back should look vigorous. Against pastel masonry
	// it rendered as black tar streaked down the walls: everything else in this
	// world sits between 0.6 and 0.9 linear and that green sits at 0.15. In a
	// palette this high-key, "darker than the darkest thing present" is not
	// contrast, it is a hole in the picture.
	private static readonly Color VineAttach = Srgb(0x87a067);
	private static readonly Color VineTip = Srgb(0xbccf96);
	private static readonly Color[] ThicketBase =
	{
		Srgb(0x8aa26a), Srgb(0x93a973), Srgb(0x829a63),
	};
	private static readonly Color[] ThicketTip =
	{
		Srgb(0xbdcf9a), Srgb(0xc6d6a6), Srgb(0xb2c68d),
	};
	private static readonly Color MossCushionSide = Srgb(0x86a05f);
	private static readonly Color MossCushionTop = Srgb(0x9db572);
	private static readonly Color[] RubbleTones =
	{
		Srgb(0xb0a8bb), Srgb(0xa39aae), Srgb(0xbcb4c6),
	};
	private static readonly Color SaplingTrunk = Srgb(0x7d5a68);

	/// <summary>
	/// What floats on the water: fallen blossom and the occasional lily pad.
	///
	/// Built as its own mesh because it needs a material that writes depth — see
	/// waterdetail.gdshader for why — and because it is scattered over water
	/// columns, which the ground pass skips outright.
	/// </summary>
	public static ArrayMesh BuildWater(Terrain terrain, int ci, int ck)
	{
		int cs = ChunkMesher.ChunkSize;
		int S = terrain.Size;
		int x0 = ci * cs, z0 = ck * cs;
		int x1 = Math.Min(S, x0 + cs), z1 = Math.Min(S, z0 + cs);
		float surface = Palette.WaterLevel;

		var f = new Field();

		for (int z = z0; z < z1; z++)
		for (int x = x0; x < x1; x++)
		{
			int i = z * S + x;
			int bed = terrain.Level[i];
			if (bed > Terrain.Sea) continue;                  // dry land
			// Nothing floats where the water is a film over the bed; that band
			// belongs to the shoreline and reads as debris stranded on mud.
			if (surface - bed < 0.8f) continue;

			var rng = new Draw(x, z, 0x0FA7);
			float fx = x + 0.5f, fz = z + 0.5f;

			// Pads gather; single petals drift. A pad carries a flower often
			// enough to be a surprise and rarely enough to stay one.
			if (rng.Chance(0.005f))
			{
				var pad = rng.Pick(PadColors);
				int n = rng.Int(1, 3);
				for (int k = 0; k < n; k++)
				{
					float ox = rng.Range(-0.34f, 0.34f), oz = rng.Range(-0.34f, 0.34f);
					f.Fleck(fx + ox, surface + 0.03f, fz + oz,
						rng.Range(0.46f, 0.72f), rng.Range(0.42f, 0.66f), rng.Next() * 1.57f, pad);
					if (k == 0 && rng.Chance(0.30f))
					{
						f.Box(fx + ox, surface + 0.05f, fz + oz, 0.20f, 0.14f, 0.20f,
							rng.Pick(FlowerTops));
					}
				}
				continue;
			}

			// Punctuation, not a carpet. A percent or so of columns puts a couple
			// of dozen petals across a whole lake, which is what the reference
			// actually holds; anything denser reads as debris.
			if (rng.Chance(0.011f))
			{
				int n = rng.Int(1, 2);
				for (int k = 0; k < n; k++)
				{
					f.Fleck(fx + rng.Range(-0.38f, 0.38f), surface + 0.02f,
						fz + rng.Range(-0.38f, 0.38f),
						rng.Range(0.26f, 0.42f), rng.Range(0.22f, 0.36f),
						rng.Next() * 1.57f, rng.Pick(Palette.PetalColors));
				}
			}
		}

		return f.Empty ? null : f.Build();
	}

	/// <summary>
	/// Everything the land has put back on a ruin in this chunk.
	///
	/// The sprigs were decided once, during world construction, by
	/// <see cref="Reclaim.Overgrow"/> — this only turns them into geometry. They
	/// go into the SAME field as the meadow's tufts on purpose: one mesh, one
	/// material, one wind shader, and not a line of new plumbing in the streamer.
	/// </summary>
	private static void Sprigs(Field f, int ci, int ck)
	{
		var list = Reclaim.In(ci, ck);
		if (list == null) return;

		foreach (var s in list)
		{
			// Tone is a per-instance draw, so neighbouring growth of the same kind
			// never comes out the same colour. Without it a wall of vine reads as
			// one flat green shape.
			float t = s.Tone;

			switch (s.Kind)
			{
				case Growth.Vine:
				{
					// Two or three strands off one attachment, at slightly
					// different lengths. A single quad reads as a hanging rag.
					// Narrow strands at unequal lengths. Wide ones read as a hanging
					// rag whatever colour they are.
					int strands = t > 0.45f ? 3 : 2;
					for (int k = 0; k < strands; k++)
					{
						float off = (k - (strands - 1) * 0.5f) * 0.26f;
						float len = s.Size * (0.58f + ((k * 37 + (int)(t * 91)) % 10) * 0.048f);
						f.Drape(
							s.X + -s.Fz * off, s.Y, s.Z + s.Fx * off,
							s.Fx, s.Fz, len, 0.12f + t * 0.09f,
							VineAttach, VineTip, s.Phase + k * 0.7f);
					}
					break;
				}

				case Growth.Fern:
				{
					// Fronds arch OUT of the wall they shelter under, which is the
					// only thing distinguishing a fern from a tuft at this size.
					var b = ThicketBase[(int)(t * 2.99f)];
					var tip = ThicketTip[(int)(t * 2.99f)];
					int n = 3 + (int)(t * 2f);
					for (int k = 0; k < n; k++)
					{
						float spread = (k / MathF.Max(1f, n - 1f) - 0.5f) * 0.5f;
						f.Tuft(
							s.X + -s.Fz * spread, s.Y - 0.04f, s.Z + s.Fx * spread,
							0.13f + t * 0.06f, s.Size * (0.7f + k % 3 * 0.15f),
							b, tip, s.Phase + k * 0.9f,
							s.Fx * (0.16f + t * 0.12f), s.Fz * (0.16f + t * 0.12f));
					}
					break;
				}

				case Growth.Thicket:
				{
					var b = ThicketBase[(int)(t * 2.99f)];
					var tip = ThicketTip[(int)(t * 2.99f)];
					// A woody heart with blades over it. The boxes are what stop a
					// thicket reading as tall grass — scrub has mass.
					f.Box(s.X, s.Y - 0.04f, s.Z, s.Size * 0.55f, s.Size * 0.34f, s.Size * 0.5f, b);
					int n = 4 + (int)(t * 3f);
					for (int k = 0; k < n; k++)
					{
						float a = (k / (float)n) * 6.283f + t * 3f;
						float r = s.Size * (0.14f + (k % 3) * 0.10f);
						f.Tuft(
							s.X + MathF.Cos(a) * r, s.Y - 0.05f, s.Z + MathF.Sin(a) * r,
							0.15f + t * 0.09f, s.Size * (0.55f + (k % 4) * 0.14f),
							b, tip, s.Phase + k * 0.8f,
							MathF.Cos(a) * 0.14f, MathF.Sin(a) * 0.14f);
					}
					break;
				}

				case Growth.Sapling:
				{
					f.Box(s.X, s.Y - 0.05f, s.Z, 0.13f, s.Size, 0.13f, SaplingTrunk, 0.35f, s.Phase);
					var tip = ThicketTip[(int)(t * 2.99f)];
					var b = ThicketBase[(int)(t * 2.99f)];
					for (int k = 0; k < 4; k++)
					{
						float a = k * 1.571f + t;
						f.Tuft(
							s.X + MathF.Cos(a) * 0.12f, s.Y + s.Size * 0.55f, s.Z + MathF.Sin(a) * 0.12f,
							0.22f, s.Size * 0.55f, b, tip, s.Phase + k,
							MathF.Cos(a) * 0.20f, MathF.Sin(a) * 0.20f);
					}
					break;
				}

				case Growth.Moss:
				{
					f.Box(s.X, s.Y - 0.06f, s.Z, s.Size, 0.10f + t * 0.09f, s.Size * 0.9f, MossCushionTop);
					if (t > 0.6f)
						f.Tuft(s.X, s.Y + 0.02f, s.Z, 0.10f, 0.10f + t * 0.10f,
							MossCushionSide, MossCushionTop, s.Phase, 0f, 0f, 0.5f);
					break;
				}

				case Growth.Rubble:
				{
					f.Box(s.X, s.Y - 0.05f, s.Z, s.Size, s.Size * (0.34f + t * 0.3f), s.Size * 0.86f,
						RubbleTones[(int)(t * 2.99f)]);
					break;
				}
			}
		}
	}

	public static ArrayMesh Build(Terrain terrain, int ci, int ck)
	{
		_meadow ??= new Noise2D(Seed + 71);
		_flowers ??= new Noise2D(Seed + 72);

		int cs = ChunkMesher.ChunkSize;
		int S = terrain.Size;
		int x0 = ci * cs, z0 = ck * cs;
		int x1 = Math.Min(S, x0 + cs), z1 = Math.Min(S, z0 + cs);

		var f = new Field();

		for (int z = z0; z < z1; z++)
		for (int x = x0; x < x1; x++)
		{
			int i = z * S + x;
			if (terrain.Land[i] == 0) continue;
			int h = terrain.Level[i];
			byte cap = terrain.Grid.At(x, h - 1, z);
			// Nothing sprouts under a canopy or inside a trunk.
			if (terrain.Grid.At(x, h, z) != Palette.AIR) continue;

			float y = h;
			float fx = x + 0.5f, fz = z + 0.5f;
			var rng = new Draw(x, z, 0x5EED);

			bool grassy = Palette.IsGrassSurface(cap) || cap is Palette.MOSS or Palette.BLOSSOM_DRIFT;
			bool muddy = cap == Palette.MUD;
			// Snow and scree carry their own marks, not grass ones.
			bool snowy = cap == Palette.SNOW;
			bool scree = cap == Palette.SCREE;
			bool sandy = cap == Palette.SAND;
			bool stony = cap is Palette.STONE or Palette.STONE_PALE or Palette.STONE_WARM;

			// Rushes stand in the SHALLOWS, so the bed has to be near the surface
			// — not merely below it. Without the lower bound the lake basin
			// qualifies all the way to its deepest point and the open water fills
			// with reeds standing ten blocks under.
			if ((sandy || grassy || muddy) && terrain.Wet[i] == 1 &&
			    h <= Terrain.Sea + 2 && h >= Terrain.Sea - 2)
			{
				if (rng.Chance(0.15f))
				{
					int n = rng.Int(2, 4);
					for (int k = 0; k < n; k++)
					{
						f.Tuft(fx + rng.Range(-0.30f, 0.30f), y - 0.03f, fz + rng.Range(-0.30f, 0.30f),
							rng.Range(0.10f, 0.17f), rng.Range(0.38f, 0.76f),
							ReedBase, ReedTip, rng.Next() * 6.28f,
							rng.Range(-0.18f, 0.18f), rng.Range(-0.18f, 0.18f));
					}
				}
				continue;
			}

			if (grassy)
			{
				// The base is a deeper sage than any shelf, the tip a little
					// lighter than the one it grows from. Taken straight off the
					// block's own top colour the whole blade sits inside the ground
					// tone and the scatter disappears — the marks only read because
					// the root end is decisively darker than what surrounds it.
					var block = Palette.Get(cap);
					var dark = TuftBase;
					var lite = new Color(block.Top.R * 1.08f, block.Top.G * 1.08f, block.Top.B * 1.08f);

				// Clumps, not a dusting: a few percent of columns, each carrying
				// one to three blades.
				float lush = _meadow.Fbm01(x * 0.045f, z * 0.045f, 3);
				if (rng.Chance(0.018f + lush * 0.055f))
				{
					int n = rng.Int(1, 3);
					for (int k = 0; k < n; k++)
					{
						f.Tuft(fx + rng.Range(-0.32f, 0.32f), y - 0.03f, fz + rng.Range(-0.32f, 0.32f),
							rng.Range(0.16f, 0.30f), rng.Range(0.26f, 0.52f),
							dark, lite, rng.Next() * 6.28f,
							rng.Range(-0.14f, 0.14f), rng.Range(-0.14f, 0.14f));
					}
				}

				// Flowers gather into drifts rather than dusting evenly.
				float ff = _flowers.Fbm01(x * 0.028f, z * 0.028f, 3);
				if (ff > 0.66f && rng.Chance((ff - 0.66f) * 0.30f))
				{
					var col = rng.Pick(FlowerTops);
					int n = rng.Int(1, 3);
					for (int k = 0; k < n; k++)
					{
						float ox = rng.Range(-0.30f, 0.30f), oz = rng.Range(-0.30f, 0.30f);
						float sh = rng.Range(0.24f, 0.42f);
						// A bare stem plus a head: the head is the note of colour,
						// the stem exists only so it is not floating.
						f.Tuft(fx + ox, y - 0.03f, fz + oz, 0.07f, sh, dark, dark, 0f, 0f, 0f, 0.6f);
						f.Box(fx + ox, y + sh - 0.04f, fz + oz, 0.20f, 0.16f, 0.20f,
							col, 0.9f, rng.Next() * 6.28f);
					}
				}

				// Pebbles and clods. Never animated — they are the still notes.
				if (rng.Chance(0.0055f))
				{
					f.Box(fx + rng.Range(-0.25f, 0.25f), y - 0.05f, fz + rng.Range(-0.25f, 0.25f),
						rng.Range(0.24f, 0.46f), rng.Range(0.14f, 0.28f), rng.Range(0.24f, 0.46f),
						rng.Pick(PebbleTops));
				}

				// Ground flecks are tiny and scarce. Most are leaves; flower petals are
				// a rarer biome note rather than a uniform confetti scatter.
				var biome = terrain.Plan.RegionAt(fx, fz).Biome;
				float leafChance = biome switch
				{
					Biome.Forest => 0.008f,
					Biome.Sakura => 0.007f,
					Biome.Meadow => 0.005f,
					Biome.Plains => 0.003f,
					_ => 0.002f,
				};
				if (rng.Chance(leafChance))
				{
					f.Fleck(fx + rng.Range(-0.36f, 0.36f), y + 0.02f,
						fz + rng.Range(-0.36f, 0.36f), rng.Range(0.10f, 0.18f),
						rng.Range(0.045f, 0.075f), rng.Next() * Mathf.Pi,
						rng.Pick(Palette.FallenLeafColors));
				}

				float petalChance = biome switch
				{
					Biome.Sakura => 0.006f,
					Biome.Meadow => 0.0025f,
					Biome.Forest => 0.0015f,
					_ => 0f,
				};
				if (rng.Chance(petalChance))
				{
					f.Fleck(fx + rng.Range(-0.36f, 0.36f), y + 0.022f,
						fz + rng.Range(-0.36f, 0.36f), rng.Range(0.08f, 0.14f),
						rng.Range(0.04f, 0.07f), rng.Next() * Mathf.Pi,
						rng.Pick(Palette.PetalColors));
				}
				continue;
			}

			// Reeds and clods on open mud, wherever it is not already flooded.
			if (muddy)
			{
				if (rng.Chance(0.06f))
				{
					int n = rng.Int(1, 3);
					for (int k = 0; k < n; k++)
					{
						f.Tuft(fx + rng.Range(-0.30f, 0.30f), y - 0.03f, fz + rng.Range(-0.30f, 0.30f),
							rng.Range(0.09f, 0.15f), rng.Range(0.30f, 0.62f),
							ReedBase, ReedTip, rng.Next() * 6.28f,
							rng.Range(-0.20f, 0.20f), rng.Range(-0.20f, 0.20f));
					}
				}
				continue;
			}

			// Wind-scoured snow keeps only the odd exposed stone.
			if (snowy)
			{
				if (rng.Chance(0.004f))
				{
					f.Box(fx + rng.Range(-0.25f, 0.25f), y - 0.05f, fz + rng.Range(-0.25f, 0.25f),
						rng.Range(0.22f, 0.40f), rng.Range(0.12f, 0.24f), rng.Range(0.22f, 0.40f),
						rng.Pick(PebbleTops));
				}
				continue;
			}

			// Lichen flecks on bare stone and scree.
			if ((stony || scree) && rng.Chance(0.012f))
			{
				f.Box(fx + rng.Range(-0.25f, 0.25f), y - 0.06f, fz + rng.Range(-0.25f, 0.25f),
					rng.Range(0.22f, 0.40f), rng.Range(0.10f, 0.20f), rng.Range(0.22f, 0.40f),
					Lichen);
			}
		}

		// Reclamation last, and outside the column walk: a vine hangs at an
		// arbitrary height on a wall face, which the per-column pass above has no
		// way to reach — it only ever looks at the top of each column.
		Sprigs(f, ci, ck);

		return f.Empty ? null : f.Build();
	}
}
