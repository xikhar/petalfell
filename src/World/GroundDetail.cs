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
	private static readonly Color TuftBase = Srgb(0x8f9e66);
	private static readonly Color ReedBase = Srgb(0x9aa877);
	private static readonly Color ReedTip = Srgb(0xc3cf9c);
	private static readonly Color Lichen = Srgb(0xa8b782);

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

		return f.Empty ? null : f.Build();
	}
}
