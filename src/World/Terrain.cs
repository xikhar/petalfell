using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>Ordered channel sample consumed by bridges, cascades and later stamps.</summary>
public readonly struct RiverNode
{
	public readonly float X, Z, Width, T, Nx, Nz;
	public readonly int Order;
	public readonly bool Ford;

	public RiverNode(float x, float z, float width, float t, float nx, float nz,
		int order, bool ford)
	{
		X = x; Z = z; Width = width; T = t; Nx = nx; Nz = nz;
		Order = order; Ford = ford;
	}
}

/// <summary>
/// The heightfield, the water network, and the voxels.
///
/// Two rules govern everything here.
///
/// **Sizes are absolute; counts scale with area.** Break that and a bigger
/// world is not a bigger place, it is the same world stretched thin.
///
/// **A continuous slope quantises into a staircase.** Any ramp toward water,
/// however gentle, produces a tread every few blocks once rounded to integers.
/// So banks are cliffs by default, the land/water boundary is decided per cell
/// rather than per column, and beds are cell-sampled and terrace-quantised too.
/// </summary>
public sealed class Terrain
{
	public const int Height = 76;
	public const int Sea = 24;              // floor(Palette.WaterLevel)
	/// <summary>
	/// Standard terrace height. A normal exposed shelf is exactly two blocks:
	/// one grass cap over either dirt or stone. One-block changes are reserved
	/// for stairs, small rocks and other deliberately authored details.
	/// </summary>
	public const int Step = 2;
	public const int EdgeGrid = 6;          // lattice terrace edges snap to
	public const int Base = Sea + Step;     // the first dry valley terrace
	/// <summary>
	/// Terrace steps between the lowest and highest planned region.
	///
	/// This is the source-authored regional relief. Local shaping and connectivity
	/// passes keep the walkable world composed while retaining its major shelves,
	/// overlooks and valleys.
	/// </summary>
	private const int MacroRelief = 11;

	public readonly int Size;
	public readonly VoxelGrid Grid;
	public readonly Planner Plan;
	public readonly short[] Level;
	public readonly byte[] Land;
	public readonly byte[] StairMask;
	public readonly byte[] RockMask;
	public readonly float[] RiverDist;
	public readonly float[] RiverHalf;
	/// <summary>How far down its own course the nearest channel node is, 0..1.</summary>
	public readonly float[] RiverT;
	public readonly byte[] RiverFord;
	public readonly sbyte[] RiverSide;
	public readonly byte[] Wet;
	public readonly List<RiverNode> RiverPath = new();

	private struct Disc
	{
		public float Cx, Cz, R, O, Ax, Az, WAmp, WFreq;
		public float Sign;
	}

	private readonly List<Disc> _discs = new();
	private readonly Noise2D _nWarp, _nRiver, _nRock, _nTone, _nEdge, _nLedge, _nFine, _nLake, _nSand;
	private readonly Rng _rng;

	public Terrain(int seed, int size, Planner plan)
	{
		Size = size;
		Plan = plan;
		Grid = new VoxelGrid(size, Height);
		Level = new short[size * size];
		Land = new byte[size * size];
		RiverDist = new float[size * size];
		RiverHalf = new float[size * size];
		RiverT = new float[size * size];
		RiverFord = new byte[size * size];
		RiverSide = new sbyte[size * size];
		Wet = new byte[size * size];

		_rng = new Rng(seed);
		_nWarp = new Noise2D(seed + 5);
		_nRiver = new Noise2D(seed + 3);
		_nRock = new Noise2D(seed + 4);
		_nTone = new Noise2D(seed + 6);
		_nEdge = new Noise2D(seed + 7);
		_nLedge = new Noise2D(seed + 8);
		_nFine = new Noise2D(seed + 9);
		_nLake = new Noise2D(seed + 11);
		_nSand = new Noise2D(seed + 13);

		BuildDiscs();
		BuildHeights();

		// Tidy the contour field before water cuts it. Running these after the
		// carve, as the old port did, lets a land filter rewrite the shoreline.
		var lev = TerrainShape.ModeFilter(Level, size, 1, Land, 2);
		Array.Copy(lev, Level, lev.Length);
		lev = TerrainShape.Despeckle(Level, size, Land, 16);
		Array.Copy(lev, Level, lev.Length);
		AddLedges();
		lev = TerrainShape.Despeckle(Level, size, Land, 12);
		Array.Copy(lev, Level, lev.Length);

		RasteriseWater();
		CarveChannels();
		CarveLake();
		Beaches();

		// Water edges are already cell-quantised. Only remove residual isolated
		// islands; a mode filter here would iron the bank flat.
		lev = TerrainShape.Despeckle(Level, size, Land, 20);
		Array.Copy(lev, Level, lev.Length);
		var noStair = AddWaterFeatures();

		for (int i = 0; i < size * size; i++)
			Land[i] = (byte)(Level[i] > Sea || IsFordGround(i) ? 1 : 0);
		StairMask = TerrainShape.CarveStairs(Level, size, Land,
			minArea: 34, tread: 2, width: 3, maxStairs: 90, skip: noStair);
		RockMask = ScatterBoulders(noStair);

		for (int i = 0; i < size * size; i++)
		{
			Level[i] = (short)Rng.ClampI(Level[i], 1, Height - 14);
			Land[i] = (byte)(Level[i] > Sea || IsFordGround(i) ? 1 : 0);
		}

		FillVoxels();
	}

	/* ================================================================
	 * 1. Contour composition
	 * ================================================================
	 * Each entry is one terrace step. Stacking shrinking discs on a shared
	 * centre builds a stepped massif; the per-disc warp offset stops the steps
	 * looking concentric. Amplitude is always exactly one step, so a cliff is
	 * one disc edge — the only way to get a six-block cliff is to make two
	 * edges coincide, which is a decision rather than an accident. */
	private void BuildDiscs()
	{
		// Every length below was authored against a 192-block world and is
		// frozen at the 256-block tuning. A mountain stays a mountain and a
		// shelf stays a shelf; feature COUNTS scale with area instead.
		const float K = 256f / 192f;
		float area = (Size / 256f) * (Size / 256f);

		void Push(float cx, float cz, float r, float o, float sign = 1f,
			float wAmp = 11f, float ax = 1f, float az = 1f)
		{
			_discs.Add(new Disc
			{
				Cx = cx, Cz = cz, R = r, O = o, Ax = ax, Az = az,
				WAmp = wAmp, WFreq = 0.016f, Sign = sign,
			});
		}

		// A crown on the highest regions. A plateau from a blended field alone
		// has no summit, and the composition needs something to be subordinate
		// to.
		var peaks = new List<Region>(Plan.Regions);
		peaks.Sort((a, b) => b.Elevation.CompareTo(a.Elevation));
		int crowns = Math.Max(2, (int)MathF.Round(2 * area));
		for (int k = 0; k < crowns && k < peaks.Count; k++)
		{
			var r = peaks[k];
			// Alternating profiles so a range is not a row of identical cones.
			float[] radii = (k % 3 == 0) ? new[] { 30f, 24f, 18f, 13f, 8f }
			              : (k % 3 == 1) ? new[] { 26f, 19f, 13f }
			                             : new[] { 22f, 16f, 11f, 7f };
			float cx = r.Cx, cz = r.Cz;
			for (int j = 0; j < radii.Length; j++)
			{
				cx += _rng.Bell() * 5f * K;
				cz += _rng.Bell() * 5f * K;
				Push(cx, cz, radii[j] * K, 137f * (_discs.Count + 1), 1f, 12f * K * (1f - j * 0.09f));
			}
		}

		// Mid-scale relief. The camera holds about 25 blocks across, so a
		// 60-block shelf is two full screens of nothing. These are small
		// single-step discs — raised knolls and sunken dells — spread on a
		// jittered grid so wherever the player stands there is a terrace edge,
		// a lip or a hollow somewhere in frame. Rooms, not corrugation.
		int gx = Math.Max(4, (int)MathF.Floor(Size / 38f + 0.5f));
		for (int gz = 0; gz < gx; gz++)
		for (int gxi = 0; gxi < gx; gxi++)
		{
			float fx = (gxi + 0.5f) / gx + _rng.Bell() * 0.075f;
			float fz = (gz + 0.5f) / gx + _rng.Bell() * 0.075f;
			if (fx < 0.10f || fx > 0.90f || fz < 0.10f || fz > 0.90f) continue;
			float sign = _rng.Chance(0.34f) ? -1f : 1f;
			Push(fx * Size, fz * Size, _rng.Range(8.5f, 17f) * K, 971f * (gxi * 7 + gz + 3),
				sign, _rng.Range(4.5f, 8.5f) * K, _rng.Range(0.7f, 1.35f), _rng.Range(0.7f, 1.35f));
		}

		// Sunken glades on the shelf tops — a hollow reads far better than
		// another flat acre, and gives vegetation somewhere sheltered to sit.
		int glades = (int)MathF.Round(3 * area);
		for (int k = 0; k < glades; k++)
		{
			Push(_rng.Range(0.08f, 0.92f) * Size, _rng.Range(0.08f, 0.92f) * Size,
				_rng.Range(9f, 13f) * K, 4441f + k, -1f, _rng.Range(4f, 6f) * K);
		}
	}

	private float DiscAt(float x0, float z0, in Disc d)
	{
		float x = MathF.Floor(x0 / EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
		float z = MathF.Floor(z0 / EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
		float w = (_nWarp.Fbm01((x + d.O) * d.WFreq, (z + d.O) * d.WFreq, 3) - 0.5f) * 2f * d.WAmp;
		float dx = (x - d.Cx) / d.Ax, dz = (z - d.Cz) / d.Az;
		float dist = MathF.Sqrt(dx * dx + dz * dz) + w;
		return (1f - Rng.Smoothstep(d.R - 1f, d.R + 1f, dist)) * d.Sign;
	}

	/// <summary>
	/// Evaluated once per edge-grid cell rather than per column.
	///
	/// The field is smooth, so its contours are smooth, and terraces came out
	/// as rounded islands. Sampling at the centre of each cell makes it
	/// piecewise constant, so every shelf boundary is forced into straight runs
	/// of at least EdgeGrid blocks meeting at right angles. It is also exact
	/// rather than an approximation — every term was already quantised — and it
	/// is what makes a large world affordable.
	/// </summary>
	private void BuildHeights()
	{
		int cw = Size / EdgeGrid + 1;
		var cellLevel = new short[cw * cw];
		float centreX = Plan.Definition.Boundary.Centre.X * Size;
		float centreZ = Plan.Definition.Boundary.Centre.Z * Size;

		for (int cz = 0; cz < cw; cz++)
		for (int cx = 0; cx < cw; cx++)
		{
			float wx = cx * EdgeGrid + EdgeGrid * 0.5f;
			float wz = cz * EdgeGrid + EdgeGrid * 0.5f;

			// Elevation is centred exactly where the authored valley floor sits.
			// Omitting -0.34 lifted the median region almost four whole terraces.
			float sum = (Plan.ElevationAt(wx, wz) - 0.34f) * MacroRelief;
			foreach (var d in _discs)
			{
				// Cheap reject: a column lies inside a handful of the few
				// hundred discs a large map stacks up, and testing every one
				// made cost grow with area squared.
				float dx = wx - d.Cx, dz = wz - d.Cz;
				float reach = d.R * MathF.Max(d.Ax, d.Az) + d.WAmp + EdgeGrid * 2f;
				if (dx * dx + dz * dz > reach * reach) continue;
				sum += DiscAt(wx, wz, d);
			}

			int h = Base + Step * (int)MathF.Floor(sum + 0.5f);

			// The island terminates in a plinth that vanishes into the haze.
			float rx = wx - centreX, rz = wz - centreZ;
			float r = MathF.Sqrt(rx * rx + rz * rz);
			float R = Plan.RimRadius(wx, wz);
			const float K = 256f / 192f;
			if (r > R - 20f * K) h = Math.Min(h, Base + Step);
			if (r > R - 9f * K) h = Math.Min(h, Base);
			if (r > R)
				h = Math.Max(1, (int)MathF.Floor(Sea - 5f - Rng.Smoothstep(R, R + 26f * K, r) * 3f + 0.5f));

			cellLevel[cz * cw + cx] = (short)Rng.ClampI(h, 2, Height - 12);
		}

		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int cx = x / EdgeGrid, cz = z / EdgeGrid;
			Level[z * Size + x] = cellLevel[cz * cw + cx];
			Land[z * Size + x] = (byte)(Level[z * Size + x] > Sea ? 1 : 0);
		}
	}

	private void AddLedges()
	{
		var ledged = (short[])Level.Clone();
		for (int pass = 0; pass < 2; pass++)
		for (int z = 1; z < Size - 1; z++)
		for (int x = 1; x < Size - 1; x++)
		{
			int i = z * Size + x;
			if (Land[i] == 0 || TerrainShape.RiseAbove(ledged, Size, x, z) < Step * 2) continue;
			float qx = MathF.Floor(x / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
			float qz = MathF.Floor(z / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
			if (_nLedge.Fbm(qx * 0.035f, qz * 0.035f, 2) < 0.10f) continue;
			ledged[i] += Step;
		}
		Array.Copy(ledged, Level, ledged.Length);
	}

	/* ================================================================
	 * 2. The water network
	 * ================================================================ */
	private static List<(float x, float z)> Smooth(List<(float x, float z)> pts, int iters = 4)
	{
		var p = pts;
		for (int it = 0; it < iters; it++)
		{
			if (p.Count < 3) break;
			var q = new List<(float, float)> { p[0] };
			for (int i = 1; i < p.Count - 1; i++)
			{
				q.Add(((p[i - 1].x + 2 * p[i].x + p[i + 1].x) / 4f,
				       (p[i - 1].z + 2 * p[i].z + p[i + 1].z) / 4f));
			}
			q.Add(p[^1]);
			p = q;
		}
		return p;
	}

	private static List<(float x, float z)> Resample(List<(float x, float z)> pts, float spacing = 1f)
	{
		var outp = new List<(float, float)>();
		for (int i = 0; i < pts.Count - 1; i++)
		{
			var (ax, az) = pts[i];
			var (bx, bz) = pts[i + 1];
			float len = MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
			int n = Math.Max(1, (int)MathF.Round(len / spacing));
			for (int k = 0; k < n; k++)
			{
				float t = k / (float)n;
				outp.Add((ax + (bx - ax) * t, az + (bz - az) * t));
			}
		}
		if (pts.Count > 0) outp.Add(pts[^1]);
		return outp;
	}

	/// <summary>
	/// Distance to the nearest channel centreline, and the channel half-width
	/// there. Keeping width in a field rather than recomputing it per column is
	/// what lets pools and narrows exist without the bank test disagreeing with
	/// the bed test.
	/// </summary>
	private void RasteriseWater()
	{
		Array.Fill(RiverDist, 1e4f);
		Array.Clear(RiverHalf);
		Array.Clear(RiverT);
		Array.Clear(RiverFord);
		Array.Clear(RiverSide);
		RiverPath.Clear();
		const int Reach = 30;
		const float K = 256f / 192f;

		foreach (var ch in Plan.Rivers)
		{
			var pts = Resample(Smooth(ch.Points), 1f);
			if (pts.Count < 8) continue;
			int fordAt = ch.Order == 0 ? (int)MathF.Floor(pts.Count * 0.62f + 0.5f) : -1;
			int fordLen = (int)MathF.Floor(7f * K + 0.5f);
			for (int i = 0; i < pts.Count; i++)
			{
				float t = i / (float)Math.Max(1, pts.Count - 1);
				float w = (ch.Order == 0 ? 3.2f + t * 5.4f : 2.2f + t * 2.6f) * K;
				w += _nRiver.Fbm(pts[i].x * 0.020f, pts[i].z * 0.020f, 2) * 1.6f * K;

				var a = pts[Math.Max(0, i - 9)];
				var b = pts[Math.Min(pts.Count - 1, i + 9)];
				float t1x = pts[i].x - a.x, t1z = pts[i].z - a.z;
				float t2x = b.x - pts[i].x, t2z = b.z - pts[i].z;
				float l1 = MathF.Sqrt(t1x * t1x + t1z * t1z);
				float l2 = MathF.Sqrt(t2x * t2x + t2z * t2z);
				if (l1 < 0.0001f) l1 = 1f;
				if (l2 < 0.0001f) l2 = 1f;
				float turn = MathF.Abs((t1x / l1) * (t2z / l2) - (t1z / l1) * (t2x / l2));
				w += Rng.Smoothstep(0.25f, 0.75f, turn) * 6f * K;

				bool ford = fordAt >= 0 && Math.Abs(i - fordAt) < fordLen;
				if (ford) w *= 0.55f;
				float nx = -t2z / l2, nz = t2x / l2;

				// A sparse ordered copy carries the channel's local frame into the
				// authored-structure pass. Bridges cross the normal, never a fixed
				// world axis, and avoid the one place intended to be a ford.
				if (i % 3 == 0)
				{
					RiverPath.Add(new RiverNode(pts[i].x, pts[i].z, w, t,
						nx, nz, ch.Order, ford));
				}

				int x0 = Math.Max(0, (int)(pts[i].x - Reach)), x1 = Math.Min(Size - 1, (int)(pts[i].x + Reach));
				int z0 = Math.Max(0, (int)(pts[i].z - Reach)), z1 = Math.Min(Size - 1, (int)(pts[i].z + Reach));
				for (int z = z0; z <= z1; z++)
				{
					float dz = z - pts[i].z;
					for (int x = x0; x <= x1; x++)
					{
						float dx = x - pts[i].x;
						float d = MathF.Sqrt(dx * dx + dz * dz);
						int idx = z * Size + x;
						if (d < RiverDist[idx])
						{
							RiverDist[idx] = d;
							RiverHalf[idx] = w;
							RiverT[idx] = t;
							RiverFord[idx] = ford ? (byte)1 : (byte)0;
							RiverSide[idx] = (sbyte)(dx * nx + dz * nz < 0f ? -1 : 1);
						}
					}
				}
			}
		}
	}

	/// <summary>
	/// The water surface is one global plane, so a channel is only a river
	/// where its bed is under that plane. Above Sea+7 it becomes what it
	/// physically is up there — a dry ravine cut a terrace or two into the
	/// slope, feeding the river below.
	/// </summary>
	/// <summary>Snap an absolute height back onto the terrace lattice.</summary>
	private static int Quant(float v) => Base + Step * (int)MathF.Round((v - Base) / Step);

	/// <summary>
	/// The water surface is one global plane, so a channel is only a river where
	/// its bed is under that plane. That does not mean the upper course should
	/// be left alone: a channel that only carves once it is already below sea
	/// level produces a few puddles at the coast and no river anywhere, because
	/// nothing ever brought the land down to meet the water.
	///
	/// So the carve descends along its own course. Near the source it is a dry
	/// ravine cut a terrace or two into the slope; downstream it converges on a
	/// bed below sea level, and the shoulders either side come down with it.
	/// The transition is a tall step — exactly where a cascade belongs.
	/// </summary>
	private void CarveChannels()
	{
		const float K = 256f / 192f;

		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int i = z * Size + x;
			if (Land[i] == 0) continue;
			float edge = RiverDist[i] - RiverHalf[i];
			int qx = Rng.ClampI((int)(MathF.Floor(x / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f), 0, Size - 1);
			int qz = Rng.ClampI((int)(MathF.Floor(z / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f), 0, Size - 1);
			int qi = qz * Size + qx;
			float edgeCell = RiverDist[qi] - RiverHalf[qi];
			if (edgeCell > 16f) continue;

			if (Level[i] > Sea + 7)
			{
				if (edgeCell <= 0f)
				{
					int cut = edgeCell < -MathF.Max(RiverHalf[i] * 0.5f, 1f) ? 2 : 1;
					Level[i] = (short)Math.Max(Sea + 1, Level[i] - Step * cut);
				}
				continue;
			}

			float wet = 1f - Rng.Smoothstep(0f, 8f, edge);
			if (wet > 0.30f) Wet[i] = 1;

			float pool = MathF.Max(_nRiver.Fbm(qx * 0.030f + 60f, qz * 0.030f, 3), 0f);
			float wide = Rng.Clamp((RiverHalf[i] / K - 3.5f) / 6f, 0f, 1f);
			float sink = MathF.Min(4f + pool * 12f + wide * 9f, 16f);
			int bed = Sea - (int)MathF.Floor(sink / Step + 0.5f) * Step;
			bool ford = RiverFord[qi] == 1;
			if (ford) bed = Sea - 1;

			if (edgeCell <= 0f)
			{
				Level[i] = (short)Math.Max(3, bed);
				Land[i] = ford ? (byte)1 : (byte)0;
				continue;
			}

			float beach = _nRiver.Fbm(x * 0.016f + 40f, z * 0.016f, 2) + RiverSide[i] * 0.10f;
			if (beach > 0.20f)
				Level[i] = (short)Math.Min(Level[i], Sea + 1 + (int)MathF.Floor(MathF.Max(edgeCell, 0f) / 7f) * Step);
		}
	}

	private void CarveLake()
	{
		const float K = 256f / 192f;
		const int Jq = EdgeGrid * 2;
		for (int k = 0; k < Plan.Lakes.Count; k++)
		{
			var lake = Plan.Lakes[k];
			float ax = 0.86f + (k * 0.37f) % 0.6f;
			float az = 0.80f + (k * 0.53f) % 0.7f;
			float offset = k * 311f;
			float reach = lake.Radius + 24f * K;
			int x0 = Math.Max(0, (int)MathF.Floor(lake.Cx - reach * ax));
			int x1 = Math.Min(Size - 1, (int)MathF.Ceiling(lake.Cx + reach * ax));
			int z0 = Math.Max(0, (int)MathF.Floor(lake.Cz - reach * az));
			int z1 = Math.Min(Size - 1, (int)MathF.Ceiling(lake.Cz + reach * az));

			float Dist(float x, float z)
			{
				float dx = (x - lake.Cx) / ax, dz = (z - lake.Cz) / az;
				return MathF.Sqrt(dx * dx + dz * dz) - lake.Radius
				     - _nLake.Fbm(x * 0.017f + offset, z * 0.017f, 3) * 10f * K;
			}

			for (int z = z0; z <= z1; z++)
			for (int x = x0; x <= x1; x++)
			{
				int i = z * Size + x;
				float qx = MathF.Floor(x / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
				float qz = MathF.Floor(z / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
				float d = Dist(x, z);
				float dCell = Dist(qx, qz);
				if (dCell > 15f * K) continue;
				if (1f - Rng.Smoothstep(0f, 9f * K, d) > 0.30f) Wet[i] = 1;
				if (Land[i] == 0 && dCell > 0f) continue;

				if (dCell <= 0f)
				{
					float deep = 1f - Rng.Smoothstep(-lake.Radius * 0.34f, 0f, dCell);
					float trench = MathF.Max(_nLake.Fbm(qx * 0.024f + 300f, qz * 0.024f, 3), 0f);
					int h = Sea - 1
					      - (int)MathF.Floor((deep * 14f + deep * trench * 22f) / Step + 0.5f) * Step
					      + CellStep(x, z, 21, 0.16f, 0.16f, Jq) * Step
					      + (CellHash(x, z, 22, Jq * 2) < 0.10f ? Step : 0);
					Level[i] = (short)Math.Max(1, h);
					Land[i] = 0;
					continue;
				}

				float bank = _nLake.Fbm(x * 0.021f + 90f, z * 0.021f, 2);
				if (bank > 0.10f)
					Level[i] = (short)Math.Min(Level[i], Sea + 1 + (int)MathF.Floor(MathF.Max(dCell, 0f) / 8f) * Step);
			}
		}
	}

	private static float CellHash(int x, int z, int salt, int grid)
	{
		unchecked
		{
			int cx = (int)MathF.Floor(x / (float)grid);
			int cz = (int)MathF.Floor(z / (float)grid);
			uint h = (uint)(cx * 374761393 + cz * 668265263 + salt * 1442695040);
			h = (h ^ (h >> 13)) * 1274126177u;
			return (h ^ (h >> 16)) / 4294967296f;
		}
	}

	private static int CellStep(int x, int z, int salt, float up, float down, int grid)
	{
		float a = CellHash(x, z, salt, grid);
		if (a < up) return 1;
		if (a > 1f - down) return -1;
		return 0;
	}

	/// <summary>Carve only selected stretches of the outer rim into beaches.</summary>
	private void Beaches()
	{
		float centreX = Plan.Definition.Boundary.Centre.X * Size;
		float centreZ = Plan.Definition.Boundary.Centre.Z * Size;
		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int i = z * Size + x;
			if (Land[i] == 0) continue;
			float dx = x - centreX, dz = z - centreZ;
			float r = MathF.Sqrt(dx * dx + dz * dz);
			float rim = Plan.RimRadius(x, z);
			float gap = rim - r;
			if (gap > 14f) continue;
			if (_nEdge.Fbm(x * 0.014f + 200f, z * 0.014f, 2) < 0.14f) continue;

			float qx = MathF.Floor(x / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
			float qz = MathF.Floor(z / (float)EdgeGrid) * EdgeGrid + EdgeGrid * 0.5f;
			float qdx = qx - centreX, qdz = qz - centreZ;
			float gapQ = MathF.Max(Plan.RimRadius(qx, qz) - MathF.Sqrt(qdx * qdx + qdz * qdz), 0f);
			Level[i] = (short)Math.Min(Level[i], Sea + (int)MathF.Floor(gapQ / 6f) * Step);
			if (1f - Rng.Smoothstep(0f, 9f, gap) > 0.30f) Wet[i] = 1;
			if (Level[i] <= Sea) Land[i] = 0;
		}
	}

	/// <summary>
	/// Put block-sized punctuation into shallow water and return the reservation
	/// mask used by authored terrain stamps before connectivity stairs are cut.
	/// </summary>
	private byte[] AddWaterFeatures()
	{
		for (int z = 2; z < Size - 2; z++)
		for (int x = 2; x < Size - 2; x++)
		{
			int i = z * Size + x;
			if (Land[i] != 0 || Level[i] < Sea - 5 || Level[i] > Sea - 1) continue;
			float roll = CellHash(x, z, 57, EdgeGrid);
			if (roll > 0.022f) continue;
			Level[i] = (short)(roll < 0.013f ? Sea + 1 : Sea);
			if (Level[i] > Sea) Land[i] = 1;
		}

		// Authored cliffs and formations now come from the map package. Keeping
		// this mask in the pipeline lets those stamps reserve themselves from the
		// automatic stair pass when their geometry is implemented.
		return new byte[Size * Size];
	}

	private bool IsFordGround(int i) =>
		RiverFord[i] == 1 && RiverDist[i] - RiverHalf[i] <= 0f;

	private byte[] ScatterBoulders(byte[] noStair)
	{
		const int spacing = 15;
		var mask = new byte[Size * Size];

		bool FlatAround(int x, int z, int radius)
		{
			int h = Level[z * Size + x];
			for (int dz = -radius; dz <= radius; dz++)
			for (int dx = -radius; dx <= radius; dx++)
			{
				int i = (z + dz) * Size + x + dx;
				if (Land[i] == 0 || Level[i] != h || StairMask[i] != 0 || noStair[i] != 0)
					return false;
			}
			return h > Sea + 1;
		}

		for (int gz = spacing; gz < Size - spacing; gz += spacing)
		for (int gx = spacing; gx < Size - spacing; gx += spacing)
		{
			if (_rng.Chance(0.30f)) continue;
			int cx = gx + _rng.RangeInt(-5, 5);
			int cz = gz + _rng.RangeInt(-5, 5);
			if (Plan.Definition.ReservesNaturalDetail(cx / (float)Size, cz / (float)Size, 5f / Size)) continue;
			if (!FlatAround(cx, cz, 4)) continue;
			float kind = _rng.Next();
			if (kind < 0.34f)
			{
				Blob(mask, cx, cz, _rng.RangeInt(1, 2), _rng.RangeInt(1, 3), 1, 1);
			}
			else if (kind < 0.78f)
			{
				float radius = _rng.Range(1.6f, 2.8f);
				RoundRock(mask, cx, cz, radius, 1, 1);
				RoundRock(mask, cx + _rng.RangeInt(-1, 1), cz + _rng.RangeInt(-1, 1),
					radius * 0.55f, 1, 2);
			}
			else
			{
				Blob(mask, cx, cz, _rng.RangeInt(0, 1), 0, _rng.RangeInt(3, 4), 2);
				if (_rng.Chance(0.60f))
					Blob(mask, cx + _rng.RangeInt(2, 3), cz + _rng.RangeInt(-1, 1),
						0, 0, _rng.RangeInt(1, 2), 2);
			}
		}
		return mask;
	}

	private void Blob(byte[] mask, int cx, int cz, int wx, int wz, int rise, byte tone)
	{
		int baseHeight = Level[cz * Size + cx];
		for (int dz = -wz; dz <= wz; dz++)
		for (int dx = -wx; dx <= wx; dx++)
		{
			int x = cx + dx, z = cz + dz;
			if (x < 1 || z < 1 || x >= Size - 1 || z >= Size - 1) continue;
			int i = z * Size + x;
			Level[i] = (short)Math.Max(Level[i], baseHeight + rise);
			mask[i] = tone;
		}
	}

	private void RoundRock(byte[] mask, int cx, int cz, float radius, int rise, byte tone)
	{
		int baseHeight = Level[cz * Size + cx];
		int ri = (int)MathF.Ceiling(radius);
		for (int dz = -ri; dz <= ri; dz++)
		for (int dx = -ri; dx <= ri; dx++)
		{
			if (dx * dx + dz * dz > radius * radius) continue;
			int x = cx + dx, z = cz + dz;
			if (x < 1 || z < 1 || x >= Size - 1 || z >= Size - 1) continue;
			int i = z * Size + x;
			Level[i] = (short)Math.Max(Level[i], baseHeight + rise);
			mask[i] = tone;
		}
	}

	/* ================================================================
	 * 3. Voxels
	 * ================================================================ */
	private void FillVoxels()
	{
		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int i = z * Size + x;
			int h = Level[i];
			if (h < 1) h = 1;
			int drop = TerrainShape.DropBelow(Level, Size, x, z);
			float jitter = _nFine.Fbm(x * 0.13f, z * 0.13f, 2) * 0.055f;
			float rockField = _nRock.Fbm(x * 0.0082f, z * 0.0082f, 3) + jitter
			                + Rng.Smoothstep(Base + Step * 4, Base + Step * 7, h) * 0.34f
			                + Rng.Smoothstep(Step, Step * 3f, drop) * 0.10f;
			float sandField = 1f - Rng.Smoothstep(0f, 6f, RiverDist[i] - RiverHalf[i]);
			var biome = Plan.RegionAt(x, z).Biome;
			float tone = _nTone.Fbm(x * 0.0115f + 90f, z * 0.0115f + 12f, 2) + jitter;

			byte cap;
			if (Land[i] == 0) cap = Palette.SAND;
			else if (h <= Sea + 2 && (sandField > 0.15f || Wet[i] == 1)) cap = Palette.SAND;
			else if (biome == Biome.SnowyHills)
				cap = rockField > 0.43f || tone < -0.24f ? Palette.SCREE : Palette.SNOW;
			else if (biome == Biome.Wetland)
				cap = Wet[i] == 1 || tone < -0.08f ? Palette.MUD : Palette.MOSS;
			else if (biome == Biome.Highland && rockField > 0.16f)
				cap = tone > 0.08f ? Palette.STONE_PALE : Palette.SCREE;
			else if (rockField > 0.42f)
				cap = _nRock.Fbm(x * 0.03f + 9f, z * 0.03f, 2) > 0f
					? Palette.STONE : Palette.STONE_PALE;
			else
			{
				float g = tone + (biome == Biome.Plains ? 0.045f : 0f);
				cap = g > 0.13f ? Palette.GRASS_LIGHT
				    : g < -0.14f ? Palette.GRASS_DEEP : Palette.GRASS;
				if (rockField > 0.28f)
					cap = cap == Palette.GRASS_LIGHT ? Palette.GRASS_LIGHT_STONE
					    : cap == Palette.GRASS_DEEP ? Palette.GRASS_DEEP_STONE
					    : Palette.GRASS_STONE;
			}
			if (StairMask[i] == 1 && cap != Palette.SAND) cap = Palette.STONE_PALE;
			if (RockMask[i] != 0) cap = RockMask[i] == 2 ? Palette.STONE_PALE : Palette.STONE;

			bool grassCap = Palette.IsGrassSurface(cap);
			bool stoneSubstrate = Palette.HasStoneSubstrate(cap);
			byte substrate = grassCap
				? (stoneSubstrate ? Palette.STONE : Palette.SOIL)
				: cap;

			// A standard Step-high riser exposes exactly two stripes: the grass
			// cap and its geological substrate (soil or stone). Deeper stone still
			// exists for intentionally tall cliffs, river cuts and the world rim,
			// but never appears as a third stripe on an ordinary terrace.
			int soilTop = h - 1;
			int soilBottom = Math.Max(1, h - 2);
			for (int y = 0; y < soilBottom; y++)
			{
				float n = _nRock.Fbm(x * 0.035f, (z + y * 5.3f) * 0.035f, 2);
				byte stone = n > 0.24f ? Palette.STONE_WARM
				           : n < -0.28f ? Palette.STONE_PALE : Palette.STONE;
				Grid.Set(x, y, z, stone);
			}
			Grid.Column(x, z, soilBottom, soilTop, substrate);
			Grid.Column(x, z, soilTop, h, cap);
			Grid.Heights[i] = (short)h;
		}
	}

	/// <summary>Nearest dry terrain surface to an authored normalized map point.</summary>
	public (int x, int z) FindSpawn(MapPoint requested = null)
	{
		int cx = requested == null ? Size / 2
			: Rng.ClampI((int)MathF.Floor(requested.X * Size + 0.5f), 0, Size - 1);
		int cz = requested == null ? Size / 2
			: Rng.ClampI((int)MathF.Floor(requested.Z * Size + 0.5f), 0, Size - 1);
		for (int r = 0; r < Size / 2; r++)
		for (int a = 0; a < 24; a++)
		{
			float ang = a / 24f * MathF.Tau;
			int x = (int)MathF.Floor(cx + MathF.Cos(ang) * r + 0.5f);
			int z = (int)MathF.Floor(cz + MathF.Sin(ang) * r + 0.5f);
			if (x < 4 || z < 4 || x > Size - 5 || z > Size - 5) continue;
			int i = z * Size + x;
			int top = Level[i];
			byte id = Grid.At(x, top - 1, z);
			bool grass = Palette.IsGrassSurface(id);
			bool safeSurface = grass || id == Palette.PATH || id == Palette.SAND ||
				id == Palette.MOSS || id == Palette.SNOW;
			if (!safeSurface || StairMask[i] != 0) continue;
			if (TerrainShape.DropBelow(Level, Size, x, z) > 1 ||
				TerrainShape.RiseAbove(Level, Size, x, z) > 1) continue;
			return (x, z);
		}
		return (cx, cz);
	}
}
