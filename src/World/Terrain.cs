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
	/// <summary>
	/// Dry atlas-bank cells whose local channel frame resolved to each side of an
	/// inland river-like reach. These are diagnostics for the bounded production
	/// derivation; the accepted wet map remains the only owner of water identity.
	/// </summary>
	public int ProductionRiverBankPositive { get; private set; }
	public int ProductionRiverBankNegative { get; private set; }

	/// <summary>Where people live. Chosen before roads, because roads connect them.</summary>
	public List<SettlementSite> Sites { get; private set; } = new();
	/// <summary>Per-stage generation cost, for the boot diagnostics.</summary>
	public string Timings = "";
	/// <summary>The road network laid over the finished heightfield.</summary>
	public RoadNetwork Roads { get; private set; }
	/// <summary>Everything worth walking to that is not a remnant.</summary>
	public List<Landmark> Marks { get; private set; } = new();
	/// <summary>Sparse map-guided natural 3-D forms written above the heightfield.</summary>
	public NaturalFormationStatistics NaturalFormations { get; private set; }

	private struct Disc
	{
		public float Cx, Cz, R, O, Ax, Az, WAmp, WFreq;
		public float Sign;
	}

	private readonly List<Disc> _discs = new();
	private readonly Noise2D _nWarp, _nRiver, _nRock, _nTone, _nEdge, _nLedge, _nFine, _nLake, _nSand;
	private readonly Rng _rng;
	private readonly int _seed;
	private readonly ProductionTerrainGrammar _productionGrammar;
	private float GlobalX(float localX) => Plan.AtlasGuide?.GlobalX(localX) ?? localX;
	private float GlobalZ(float localZ) => Plan.AtlasGuide?.GlobalZ(localZ) ?? localZ;

	public Terrain(int seed, int size, Planner plan, bool terrainOnly = false)
	{
		Size = size;
		Plan = plan;
		Grid = new VoxelGrid(size,
			plan.AtlasGuide != null ? AtlasSectorWindow.AuthoredRuntimeHeight : Height,
			seed,
			plan.AtlasGuide?.OriginX ?? 0, plan.AtlasGuide?.OriginZ ?? 0);
		Level = new short[size * size];
		Land = new byte[size * size];
		RiverDist = new float[size * size];
		RiverHalf = new float[size * size];
		RiverT = new float[size * size];
		RiverFord = new byte[size * size];
		RiverSide = new sbyte[size * size];
		Wet = new byte[size * size];

		_rng = new Rng(seed);
		_seed = seed;
		_productionGrammar = plan.AtlasGuide == null ? null : new ProductionTerrainGrammar(seed);
		_nWarp = new Noise2D(seed + 5);
		_nRiver = new Noise2D(seed + 3);
		_nRock = new Noise2D(seed + 4);
		_nTone = new Noise2D(seed + 6);
		_nEdge = new Noise2D(seed + 7);
		_nLedge = new Noise2D(seed + 8);
		_nFine = new Noise2D(seed + 9);
		_nLake = new Noise2D(seed + 11);
		_nSand = new Noise2D(seed + 13);

		var clock = System.Diagnostics.Stopwatch.StartNew();
		long mark = 0;
		void Stage(string name)
		{
			Timings += $"{name} {clock.ElapsedMilliseconds - mark}ms  ";
			mark = clock.ElapsedMilliseconds;
		}

		BuildDiscs();
		BuildHeights();
		Stage("heights");

		// Tidy the contour field before water cuts it. Running these after the
		// carve, as the old port did, lets a land filter rewrite the shoreline.
		var lev = TerrainShape.ModeFilter(Level, size, 1, Land, 2);
		Array.Copy(lev, Level, lev.Length);
		lev = TerrainShape.Despeckle(Level, size, Land, 16);
		Array.Copy(lev, Level, lev.Length);
		AddLedges();
		lev = TerrainShape.Despeckle(Level, size, Land, 12);
		Array.Copy(lev, Level, lev.Length);
		Stage("filter");

		if (Plan.AtlasGuide != null)
			ApplyProductionHydrology();
		else
		{
			RasteriseWater();
			CarveChannels();
			CarveLake();
			Beaches();
		}

		// Water edges are already cell-quantised. Only remove residual isolated
		// islands; a mode filter here would iron the bank flat. Despeckle changes
		// components of at most twenty cells, so the production moving window's
		// much wider overlap margin contains its complete dependency footprint.
		lev = TerrainShape.Despeckle(Level, size, Land, 20);
		Array.Copy(lev, Level, lev.Length);
		Stage("water");
		var noStair = AddWaterFeatures();

		for (int i = 0; i < size * size; i++)
			Land[i] = (byte)(Level[i] > Sea || IsFordGround(i) ? 1 : 0);
		if (Plan.AtlasGuide != null)
			StairMask = TerrainShape.CarveAtlasStairs(Level, size, Land,
				Grid.OriginX, Grid.OriginZ, _seed, tread: 2, width: 3, skip: noStair);
		else
		{
			// Stair COUNT scales with area, like every other feature count here. The
			// old fixed ninety was tuned against a 1024 map; leaving it there on a
			// world eleven times the size would connect one corner and strand the rest.
			float stairArea = (Size / 256f) * (Size / 256f);
			StairMask = TerrainShape.CarveStairs(Level, size, Land,
				minArea: 34, tread: 2, width: 3,
				maxStairs: Math.Max(90, (int)(6f * stairArea)), skip: noStair);
		}
		Stage("stairs");
		RockMask = ScatterBoulders(noStair);
		Stage("boulders");

		int finalCeiling = Plan.AtlasGuide == null ? Height - 14 :
			Plan.AtlasGuide.WorldHeight - 1;
		for (int i = 0; i < size * size; i++)
		{
			Level[i] = (short)Rng.ClampI(Level[i], 1, finalCeiling);
			// A wet shoreline column may not end exactly on the water plane.
			// Production water has a translucent surface and a
			// separately described bed, so that zero-depth state is contradictory.
			// Keep authored ford ground dry, but give every other production water
			// column at least one visible block of depth in both data and collision.
			if (Plan.AtlasGuide != null && Level[i] <= Sea && !IsFordGround(i))
				Level[i] = (short)Math.Min((int)Level[i], Sea - 1);
			Land[i] = (byte)(Level[i] > Sea || IsFordGround(i) ? 1 : 0);
		}

			// Important places and routes come from authored L2 topology. Production
			// terrain never fills gaps with seed-chosen settlements or landmarks.
			if (terrainOnly)
			{
				Sites = new List<SettlementSite>();
				Marks = new List<Landmark>();
				Stage("sites");
				Roads = RoadNetwork.Empty(size);
			}
			else if (Plan.Definition.CanonicalWorld != null)
			{
				Sites = new List<SettlementSite>();
				Marks = new List<Landmark>();
				Stage("sites");
				Roads = RoadNetwork.BuildAuthored(this, Plan.Definition.CanonicalWorld);
				// Cairns are subordinate road dressing, not significant sites.
				Landmarks.PlanCairns(this, Sites, Marks, seed);
			}
			else
			{
				Sites = Settlements.PlanSites(this, seed);
				// Level the town platforms BEFORE the roads are routed over them.
				Settlements.TerraceSites(this);
				Stage("sites");
				// Landmarks before roads, so trails can be routed out to them; cairns
				// after, because a cairn's whole job is to stand beside a road.
				Marks = Landmarks.PlanSignificant(this, Sites, seed);
				Roads = RoadNetwork.Build(this, Sites, Marks, seed);
				Landmarks.PlanCairns(this, Sites, Marks, seed);
			}
			Stage("roads");

		DescribeColumns();
		Stage("columns");
		if (_productionGrammar != null)
		{
			BuildNaturalFormations();
			Stage("formations");
		}
	}

	/// <summary>Refresh gameplay height/land fields after an authored site reshapes columns.</summary>
	public void SyncAuthoredTerrain()
	{
		for (int i = 0; i < Level.Length; i++)
		{
			Level[i] = Grid.Top[i];
			Land[i] = (byte)(Level[i] > Sea ? 1 : 0);
		}
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
		// Production windows query the extracted shelf primitive directly in atlas
		// coordinates. Rebuilding a local RNG catalogue in every moving
		// window would slide the rooms underneath the player at each handoff.
		if (_productionGrammar != null) return;
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
		float w = (_nWarp.Fbm01((GlobalX(x) + d.O) * d.WFreq,
			(GlobalZ(z) + d.O) * d.WFreq, 3) - 0.5f) * 2f * d.WAmp;
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
	/// <summary>
	/// Rasterise the disc stack into a terraced heightfield.
	///
	/// Discs are BUCKETED by centre, and that is not an optimisation so much as
	/// the difference between a big world and no big world. Testing every column
	/// against every disc costs cells x discs, and BOTH of those scale with area
	/// — so the term is quadratic in area, and a world eleven times the size paid
	/// a hundred and fifty times the cost. It was twenty seconds of a
	/// twenty-six-second generation. Bucketing turns the inner loop into the
	/// handful of discs that could actually reach this column.
	/// </summary>
	private void BuildHeights()
	{
		int cw = Size / EdgeGrid + 1;
		var cellLevel = new short[cw * cw];
		float centreX = Plan.Definition.Boundary.Centre.X * Size;
		float centreZ = Plan.Definition.Boundary.Centre.Z * Size;

		const int Bucket = 64;
		int bw = Size / Bucket + 2;
		var buckets = new List<int>[bw * bw];
		for (int i = 0; i < buckets.Length; i++) buckets[i] = new List<int>();

		float maxReach = 0f;
		for (int d = 0; d < _discs.Count; d++)
		{
			var disc = _discs[d];
			float reach = disc.R * MathF.Max(disc.Ax, disc.Az) + disc.WAmp + EdgeGrid * 2f;
			if (reach > maxReach) maxReach = reach;
			int bx = Rng.ClampI((int)(disc.Cx / Bucket), 0, bw - 1);
			int bz = Rng.ClampI((int)(disc.Cz / Bucket), 0, bw - 1);
			buckets[bz * bw + bx].Add(d);
		}
		// One extra ring, because a column sits anywhere inside its own bucket.
		int rings = (int)MathF.Ceiling(maxReach / Bucket) + 1;

		System.Threading.Tasks.Parallel.For(0, cw, cz =>
		{
			for (int cx = 0; cx < cw; cx++)
			{
				float wx = cx * EdgeGrid + EdgeGrid * 0.5f;
				float wz = cz * EdgeGrid + EdgeGrid * 0.5f;

				// Elevation is centred exactly where the authored valley floor sits.
				// Omitting -0.34 lifted the median region almost four whole terraces.
				float authoredElevation = Plan.ElevationAt(wx, wz);
				float sum;
				if (_productionGrammar != null)
				{
					int globalX = (int)MathF.Floor(GlobalX(wx));
					int globalZ = (int)MathF.Floor(GlobalZ(wz));
					var warp = _productionGrammar.GuideWarpAt(globalX, globalZ);
					authoredElevation = Plan.AtlasGuide.GuidedLandElevationAt(wx, wz, warp);
					float macroHeight = ProductionTerrainGuide.TerrainHeightForElevation(
						authoredElevation);
					sum = (macroHeight - Base) / Step;
					sum += _productionGrammar.MountainReliefAt(globalX, globalZ,
						authoredElevation) / Step;
					sum += _productionGrammar.TerraceOffsetAt(globalX, globalZ,
						authoredElevation);
				}
				else sum = (authoredElevation - 0.34f) * MacroRelief;

				int bx0 = Rng.ClampI((int)(wx / Bucket) - rings, 0, bw - 1);
				int bx1 = Rng.ClampI((int)(wx / Bucket) + rings, 0, bw - 1);
				int bz0 = Rng.ClampI((int)(wz / Bucket) - rings, 0, bw - 1);
				int bz1 = Rng.ClampI((int)(wz / Bucket) + rings, 0, bw - 1);

				for (int bz = bz0; bz <= bz1; bz++)
				for (int bxi = bx0; bxi <= bx1; bxi++)
				{
					var list = buckets[bz * bw + bxi];
					for (int k = 0; k < list.Count; k++)
					{
						var d = _discs[list[k]];
						float dx = wx - d.Cx, dz = wz - d.Cz;
						float reach = d.R * MathF.Max(d.Ax, d.Az) + d.WAmp + EdgeGrid * 2f;
						if (dx * dx + dz * dz > reach * reach) continue;
						sum += DiscAt(wx, wz, d);
					}
				}

				int h = Base + Step * (int)MathF.Floor(sum + 0.5f);

				// Historical non-atlas callers used a radial world boundary. Production
				// always has an AtlasGuide and never enters this preserved branch.
				if (Plan.AtlasGuide == null)
				{
					float rx = wx - centreX, rz = wz - centreZ;
					float r = MathF.Sqrt(rx * rx + rz * rz);
					float R = Plan.RimRadius(wx, wz);
					const float K = 256f / 192f;
					if (r > R - 20f * K) h = Math.Min(h, Base + Step);
					if (r > R - 9f * K) h = Math.Min(h, Base);
					if (r > R)
						h = Math.Max(1, (int)MathF.Floor(Sea - 5f - Rng.Smoothstep(R, R + 26f * K, r) * 3f + 0.5f));
				}

				// Leave three old two-block ledge passes below the atlas's natural
				// ceiling. The 256-block VoxelGrid headroom belongs to authored monuments,
				// not to procedural peaks.
				int ceiling = Plan.AtlasGuide == null ? Height - 12 :
					Plan.AtlasGuide.WorldHeight - 7;
				cellLevel[cz * cw + cx] = (short)Rng.ClampI(h, 2, ceiling);
			}
		});

		System.Threading.Tasks.Parallel.For(0, Size, z =>
		{
			for (int x = 0; x < Size; x++)
			{
				int cx = x / EdgeGrid, cz = z / EdgeGrid;
				Level[z * Size + x] = cellLevel[cz * cw + cx];
				Land[z * Size + x] = (byte)(Level[z * Size + x] > Sea ? 1 : 0);
			}
		});
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
			if (_nLedge.Fbm(GlobalX(qx) * 0.035f,
				GlobalZ(qz) * 0.035f, 2) < 0.10f) continue;
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
	/// Realise accepted continent hydrology with the production water grammar. The map
	/// is sampled on the six-block terrain lattice and displaced by a continuous
	/// atlas-space field; gradual bank courses and underwater shelves own the
	/// final block shape instead of tracing source pixels literally.
	/// </summary>
	private void ApplyProductionHydrology()
	{
		Array.Fill(RiverDist, float.MaxValue);
		Array.Clear(RiverHalf);
		Array.Clear(RiverT);
		Array.Clear(RiverFord);
		Array.Clear(RiverSide);
		ProductionRiverBankPositive = 0;
		ProductionRiverBankNegative = 0;
		var wetGuide = new bool[Size * Size];
		var oceanGuide = new bool[Size * Size];
		int cellW = (Size + EdgeGrid - 1) / EdgeGrid;
		for (int cz = 0; cz < cellW; cz++)
		for (int cx = 0; cx < cellW; cx++)
		{
			float qx = Math.Min(Size - .5f, cx * EdgeGrid + EdgeGrid * .5f);
			float qz = Math.Min(Size - .5f, cz * EdgeGrid + EdgeGrid * .5f);
			float gx = GlobalX(qx), gz = GlobalZ(qz);
			float wx = _nEdge.Fbm(gx * .011f + 137f, gz * .011f + 41f, 3) * 10f;
			float wz = _nEdge.Fbm(gx * .011f - 83f, gz * .011f + 211f, 3) * 10f;
			bool wet = Plan.AtlasGuide.AuthoredWetAt(qx + wx, qz + wz);
			bool ocean = Plan.AtlasGuide.LandAt(qx + wx, qz + wz) < .5f;
			int x0 = cx * EdgeGrid, z0 = cz * EdgeGrid;
			for (int z = z0; z < Math.Min(Size, z0 + EdgeGrid); z++)
			for (int x = x0; x < Math.Min(Size, x0 + EdgeGrid); x++)
			{
				int i = z * Size + x;
				wetGuide[i] = wet;
				oceanGuide[i] = ocean;
			}
		}

		// The old 14-20 block response looked right in its 256-block fixture, but
		// became a razor rim beside continent-scale water. Keep the same signed
		// distance grammar over a broader reach: lowland beaches can now occupy a
		// real foreground and the bed remains visible through several submerged
		// courses before it falls into deep water.
		const int HydrologyReach = 96;
		int[] toWet = DistanceTo(wetGuide, target: true, HydrologyReach);
		int[] toDry = DistanceTo(wetGuide, target: false, HydrologyReach);
		int[] toOcean = DistanceTo(oceanGuide, target: true, HydrologyReach);
		var riverFrames = new ProductionRiverBankFrame[cellW * cellW];
		var riverFrameKnown = new bool[riverFrames.Length];
		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int i = z * Size + x;
			if (wetGuide[i])
			{
				float gx = GlobalX(x), gz = GlobalZ(z);
				float trench = MathF.Max(_nLake.Fbm(gx * .024f + 300f, gz * .024f, 3), 0f);
				float edge = toDry[i];
				float depth = oceanGuide[i]
					? 1f + Rng.Smoothstep(0f, 12f, edge) * 4f +
					  Rng.Smoothstep(8f, 52f, edge) * 12f +
					  Rng.Smoothstep(42f, 72f, edge) * 13f
					: 1f + Rng.Smoothstep(0f, 20f, edge) * (13f + trench * 8f);
				int depthCourse = 1 + (int)MathF.Floor(depth / Step + .5f) * Step;
				int breakup = edge > 8f
					? CellStep(x, z, 21, .16f, .16f, EdgeGrid * 2) * Step +
					  (CellHash(x, z, 22, EdgeGrid * 4) < .10f ? Step : 0)
					: 0;
				int bed = Math.Min(Sea - 1, Sea - depthCourse + breakup);
				Level[i] = (short)Math.Max(1, Math.Min(Level[i], bed));
				Land[i] = 0;
				Wet[i] = 1;
				continue;
			}

			int gap = toWet[i];
			if (gap > HydrologyReach) continue;
			float gxDry = GlobalX(x), gzDry = GlobalZ(z);
			bool oceanBank = toOcean[i] <= gap + 1;
			bool lowland = Level[i] <= Sea + 34;
			Biome biome = Plan.RegionAt(x, z).Biome;
			int uncutHeight = Level[i];
			if (!lowland && uncutHeight >= Sea + 56 && gap <= 90)
			{
				// A high accepted river or coast may legitimately descend a hundred
				// blocks. Compressing that descent into the old fourteen-block beach
				// reach made one map-edge wall; the first attempt to repair it attached
				// three six-to-eleven-block ledges to that wall and read as architecture.
				// These are terrain-sized contour shoulders instead: four broad benches
				// climb through the full high mass, with a low-frequency displacement so
				// no shelf traces the accepted water mask literally.
				float shoulderRun = _nLedge.Fbm(gxDry * .0052f + 311f,
					gzDry * .0052f - 127f, 3);
				if (shoulderRun > -.32f)
				{
					float contourBreak = _nEdge.Fbm(gxDry * .012f - 413f,
						gzDry * .012f + 271f, 3) * 11f;
					float shoulderDistance = gap + contourBreak;
					float fraction = shoulderDistance <= 14f ? .18f :
						shoulderDistance <= 34f ? .38f :
						shoulderDistance <= 58f ? .62f : .82f;
					int shoulder = Sea + 1 + (int)MathF.Floor(
						(uncutHeight - Sea) * fraction / Step + .5f) * Step;
					Level[i] = (short)Math.Min(Level[i], shoulder);
					continue;
				}
			}
			if (!oceanBank)
			{
				bool gorge = biome == Biome.Highland || biome == Biome.SnowyHills;
				int reach = lowland ? 44 : 22;
				if (gap > reach) continue;
				int frameX = x / EdgeGrid;
				int frameZ = z / EdgeGrid;
				int frameIndex = frameZ * cellW + frameX;
				if (!riverFrameKnown[frameIndex])
				{
					int qx = Math.Min(Size - 1, frameX * EdgeGrid + EdgeGrid / 2);
					int qz = Math.Min(Size - 1, frameZ * EdgeGrid + EdgeGrid / 2);
					riverFrames[frameIndex] = MeasureProductionRiverBankFrame(qx, qz,
						wetGuide, oceanGuide, toWet, toDry);
					riverFrameKnown[frameIndex] = true;
				}
				ProductionRiverBankFrame frame = riverFrames[frameIndex];
				float bank = _nLake.Fbm(gxDry * .008f + 90f, gzDry * .008f, 3);
				if (frame.RiverLike)
				{
					// This is the old channel rule: selected stretches receive a small
					// side bias rather than lowering both banks in lockstep. The side is
					// now derived from the accepted channel silhouette and oriented toward
					// lower mapped elevation, because production has no seed-planned line.
					bank += frame.Side * .10f;
					RiverSide[i] = frame.Side;
					RiverHalf[i] = frame.HalfWidth;
					RiverDist[i] = frame.HalfWidth + gap;
					if (frame.Side > 0) ProductionRiverBankPositive++;
					else ProductionRiverBankNegative++;
				}
				float threshold = lowland ? -.18f : gorge ? .30f : .08f;
				if (bank > threshold)
					Level[i] = (short)Math.Min(Level[i],
						Sea + 1 + (int)MathF.Floor(Math.Max(gap - 1, 0) /
							(lowland ? 10f : 8f)) * Step);
				if (gap <= 18) Wet[i] = 1;
			}
			else
			{
				int reach = lowland ? 60 : 22;
				float beach = _nEdge.Fbm(gxDry * .0065f + 200f,
					gzDry * .0065f - 71f, 3);
				if (gap > reach || beach < (lowland ? -.28f : .28f))
					continue;
				Level[i] = (short)Math.Min(Level[i], Sea + 1 +
					(int)MathF.Floor(Math.Max(gap - 1, 0) / (lowland ? 11f : 7f)) * Step);
				if (gap <= 20) Wet[i] = 1;
				if (Level[i] <= Sea) Land[i] = 0;
			}
		}
	}

	private readonly record struct ProductionRiverBankFrame(bool RiverLike,
		sbyte Side, float HalfWidth);

	/// <summary>
	/// Recover the local channel-side fact needed for organic bank
	/// cadence: which side of a narrow reach this bank occupies. The atlas owns a
	/// wet silhouette rather than a centreline, so a signed-distance normal finds
	/// the channel interior, a perpendicular gives its tangent, and mapped
	/// elevation orients that tangent downhill. Wide lakes, ocean and junctions
	/// deliberately fall back to the symmetric lake/coast grammar.
	/// </summary>
	private ProductionRiverBankFrame MeasureProductionRiverBankFrame(int x, int z,
		bool[] wetGuide, bool[] oceanGuide, int[] toWet, int[] toDry)
	{
		int start = z * Size + x;
		int gap = toWet[start];
		if (wetGuide[start] || gap < 1 || gap > 48)
			return default;

		float SignedDistance(int px, int pz)
		{
			px = Rng.ClampI(px, 0, Size - 1);
			pz = Rng.ClampI(pz, 0, Size - 1);
			int i = pz * Size + px;
			return wetGuide[i] ? Math.Min(toDry[i], 32) : -Math.Min(toWet[i], 32);
		}

		const int derivative = EdgeGrid;
		float nx = SignedDistance(x + derivative, z) -
			SignedDistance(x - derivative, z);
		float nz = SignedDistance(x, z + derivative) -
			SignedDistance(x, z - derivative);
		float normalLength = MathF.Sqrt(nx * nx + nz * nz);
		if (normalLength < .5f) return default;
		nx /= normalLength;
		nz /= normalLength;

		int centreX = x, centreZ = z;
		int halfWidth = 0;
		int lastInterior = 0;
		// Walk across the bank normal until the interior distance stops growing.
		// The cap is intentionally smaller than HydrologyReach and the 192-block
		// moving-window comparison margin.
		for (int step = Math.Max(1, gap - 2); step <= gap + 48; step += 2)
		{
			int px = Rng.ClampI((int)MathF.Round(x + nx * step), 0, Size - 1);
			int pz = Rng.ClampI((int)MathF.Round(z + nz * step), 0, Size - 1);
			int i = pz * Size + px;
			if (oceanGuide[i]) return default;
			int interior = wetGuide[i] ? toDry[i] : 0;
			if (interior > halfWidth)
			{
				halfWidth = interior;
				centreX = px;
				centreZ = pz;
			}
			if (halfWidth > 0 && interior + 3 < lastInterior) break;
			lastInterior = interior;
		}
		// Narrow accepted ponds use the lake grammar; broad lakes and confluences
		// exceed this width and likewise remain symmetric.
		if (halfWidth < 2 || halfWidth > 22) return default;

		float tx = -nz;
		float tz = nx;
		int along = 0;
		for (int direction = -1; direction <= 1; direction += 2)
		for (int step = 2; step <= 64; step += 2)
		{
			int px = Rng.ClampI((int)MathF.Round(centreX + tx * step * direction),
				0, Size - 1);
			int pz = Rng.ClampI((int)MathF.Round(centreZ + tz * step * direction),
				0, Size - 1);
			int i = pz * Size + px;
			if (!wetGuide[i] || oceanGuide[i]) break;
			along += 2;
		}
		if (along < Math.Max(24, halfWidth * 3)) return default;

		// Compare the dry bank a short distance along both tangent directions.
		// Water pixels have no reliable elevation value; stepping outward by the
		// measured half-width keeps the samples on the bank that owns this frame.
		float outward = halfWidth + 8f;
		const float station = 24f;
		float forward = Plan.AtlasGuide.ElevationAt(
			x + tx * station - nx * outward,
			z + tz * station - nz * outward);
		float backward = Plan.AtlasGuide.ElevationAt(
			x - tx * station - nx * outward,
			z - tz * station - nz * outward);
		if (forward > backward + .001f)
		{
			tx = -tx;
			tz = -tz;
		}
		else if (MathF.Abs(forward - backward) <= .001f &&
			(tz < 0f || MathF.Abs(tz) < .15f && tx < 0f))
		{
			// Flat reaches have no downhill evidence. Give them one stable global
			// orientation so opposite banks still receive opposite legacy biases.
			tx = -tx;
			tz = -tz;
		}

		float cross = tx * nz - tz * nx;
		sbyte side = cross >= 0f ? (sbyte)1 : (sbyte)-1;
		return new ProductionRiverBankFrame(true, side, halfWidth);
	}

	private int[] DistanceTo(bool[] mask, bool target, int cap)
	{
		// The accepted river and lake grammar measures a Euclidean signed edge. A
		// four-neighbour flood instead makes every broad beach, submerged shelf and
		// canyon shoulder expand as a visible diamond. A 3/4 chamfer retains the
		// bounded integer field needed by this hot path while closely following the
		// old round distance. HydrologyReach is smaller than the moving-window safety
		// margin, so targets outside the allocation cannot affect compared terrain.
		const int Cardinal = 3;
		const int Diagonal = 4;
		int n = mask.Length;
		int maxCost = cap * Cardinal;
		int unreachable = maxCost + Cardinal;
		var cost = new int[n];
		for (int i = 0; i < n; i++) cost[i] = mask[i] == target ? 0 : unreachable;

		void Lower(int i, int neighbour, int step)
		{
			int candidate = cost[neighbour] + step;
			if (candidate < cost[i]) cost[i] = candidate;
		}

		for (int z = 0; z < Size; z++)
		for (int x = 0; x < Size; x++)
		{
			int i = z * Size + x;
			if (x > 0) Lower(i, i - 1, Cardinal);
			if (z > 0)
			{
				Lower(i, i - Size, Cardinal);
				if (x > 0) Lower(i, i - Size - 1, Diagonal);
				if (x + 1 < Size) Lower(i, i - Size + 1, Diagonal);
			}
		}

		for (int z = Size - 1; z >= 0; z--)
		for (int x = Size - 1; x >= 0; x--)
		{
			int i = z * Size + x;
			if (x + 1 < Size) Lower(i, i + 1, Cardinal);
			if (z + 1 < Size)
			{
				Lower(i, i + Size, Cardinal);
				if (x > 0) Lower(i, i + Size - 1, Diagonal);
				if (x + 1 < Size) Lower(i, i + Size + 1, Diagonal);
			}
		}

		var distance = new int[n];
		for (int i = 0; i < n; i++)
			distance[i] = cost[i] > maxCost
				? cap + 1
				: Math.Min(cap, (cost[i] + Cardinal / 2) / Cardinal);
		return distance;
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
				w += _nRiver.Fbm(GlobalX(pts[i].x) * 0.020f,
					GlobalZ(pts[i].z) * 0.020f, 2) * 1.6f * K;

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

			float pool = MathF.Max(_nRiver.Fbm(GlobalX(qx) * 0.030f + 60f,
				GlobalZ(qz) * 0.030f, 3), 0f);
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

			float beach = _nRiver.Fbm(GlobalX(x) * 0.016f + 40f,
				GlobalZ(z) * 0.016f, 2) + RiverSide[i] * 0.10f;
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
				     - _nLake.Fbm(GlobalX(x) * 0.017f + offset,
					     GlobalZ(z) * 0.017f, 3) * 10f * K;
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
					float trench = MathF.Max(_nLake.Fbm(GlobalX(qx) * 0.024f + 300f,
						GlobalZ(qz) * 0.024f, 3), 0f);
					int h = Sea - 1
					      - (int)MathF.Floor((deep * 14f + deep * trench * 22f) / Step + 0.5f) * Step
					      + CellStep(x, z, 21, 0.16f, 0.16f, Jq) * Step
					      + (CellHash(x, z, 22, Jq * 2) < 0.10f ? Step : 0);
					Level[i] = (short)Math.Max(1, h);
					Land[i] = 0;
					continue;
				}

				float bank = _nLake.Fbm(GlobalX(x) * 0.021f + 90f,
					GlobalZ(z) * 0.021f, 2);
				if (bank > 0.10f)
					Level[i] = (short)Math.Min(Level[i], Sea + 1 + (int)MathF.Floor(MathF.Max(dCell, 0f) / 8f) * Step);
			}
		}
	}

	private float CellHash(int x, int z, int salt, int grid)
	{
		unchecked
		{
			int cx = (int)MathF.Floor(GlobalX(x) / grid);
			int cz = (int)MathF.Floor(GlobalZ(z) / grid);
			uint h = (uint)(cx * 374761393 + cz * 668265263 + salt * 1442695040);
			h = (h ^ (h >> 13)) * 1274126177u;
			return (h ^ (h >> 16)) / 4294967296f;
		}
	}

	private int CellStep(int x, int z, int salt, float up, float down, int grid)
	{
		float a = CellHash(x, z, salt, grid);
		if (a < up) return 1;
		if (a > 1f - down) return -1;
		return 0;
	}

	/// <summary>Carve only selected stretches of the outer rim into beaches.</summary>
	private void Beaches()
	{
		if (Plan.AtlasGuide != null) return;
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
			if (_nEdge.Fbm(GlobalX(x) * 0.014f + 200f,
				GlobalZ(z) * 0.014f, 2) < 0.14f) continue;

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

		int originX = Grid.OriginX;
		int originZ = Grid.OriginZ;
		int firstGlobalX = Plan.AtlasGuide == null ? spacing
			: (int)MathF.Ceiling((originX + spacing) / (float)spacing) * spacing;
		int firstGlobalZ = Plan.AtlasGuide == null ? spacing
			: (int)MathF.Ceiling((originZ + spacing) / (float)spacing) * spacing;
		for (int globalZ = firstGlobalZ; globalZ < originZ + Size - spacing; globalZ += spacing)
		for (int globalX = firstGlobalX; globalX < originX + Size - spacing; globalX += spacing)
		{
			Rng rng = Plan.AtlasGuide == null ? _rng :
				new Rng(unchecked(_seed ^ StableCellSeed(globalX / spacing,
					globalZ / spacing, 0x4217)));
			if (rng.Chance(0.30f)) continue;
			int cx = globalX - originX + rng.RangeInt(-5, 5);
			int cz = globalZ - originZ + rng.RangeInt(-5, 5);
			if (Plan.AtlasGuide == null &&
			    Plan.Definition.ReservesNaturalDetail(cx / (float)Size,
				    cz / (float)Size, 5f / Size)) continue;
			if (!FlatAround(cx, cz, 4)) continue;
			float kind = rng.Next();
			if (kind < 0.34f)
			{
				Blob(mask, cx, cz, rng.RangeInt(1, 2), rng.RangeInt(1, 3), 1, 1);
			}
			else if (kind < 0.78f)
			{
				float radius = rng.Range(1.6f, 2.8f);
				RoundRock(mask, cx, cz, radius, 1, 1);
				RoundRock(mask, cx + rng.RangeInt(-1, 1), cz + rng.RangeInt(-1, 1),
					radius * 0.55f, 1, 2);
			}
			else
			{
				Blob(mask, cx, cz, rng.RangeInt(0, 1), 0, rng.RangeInt(3, 4), 2);
				if (rng.Chance(0.60f))
					Blob(mask, cx + rng.RangeInt(2, 3), cz + rng.RangeInt(-1, 1),
						0, 0, rng.RangeInt(1, 2), 2);
			}
		}
		return mask;
	}

	private static int StableCellSeed(int x, int z, int salt)
	{
		unchecked
		{
			uint h = (uint)(x * 374761393 + z * 668265263 + salt * 1442695040);
			h = (h ^ (h >> 13)) * 1274126177u;
			return (int)(h ^ (h >> 16));
		}
	}

	/// <summary>
	/// Add rare erosion arches to high accepted stone country.
	///
	/// The ordinary terrain is a heightfield and therefore cannot make an
	/// overhang. These forms are the smallest possible extension: candidates live
	/// on one global lattice, the accepted elevation/land/biome fields decide
	/// whether a candidate is legal, and each included column starts at the
	/// already-finished old-terrain surface. They are natural punctuation rather
	/// than sites, never replace the map's macro geography, and never alter the
	/// navigation heightfield beneath their opening.
	/// </summary>
	private void BuildNaturalFormations()
	{
		const int CandidateGrid = 720;
		const int CandidateMargin = 96;
		int originX = Grid.OriginX, originZ = Grid.OriginZ;
		int minCellX = FloorDiv(originX - CandidateMargin, CandidateGrid) - 1;
		int maxCellX = FloorDiv(originX + Size + CandidateMargin, CandidateGrid) + 1;
		int minCellZ = FloorDiv(originZ - CandidateMargin, CandidateGrid) - 1;
		int maxCellZ = FloorDiv(originZ + Size + CandidateMargin, CandidateGrid) + 1;
		int arches = 0, voxels = 0, firstX = -1, firstZ = -1,
			lastX = -1, lastZ = -1;
		ulong manifest = 1469598103934665603UL;

		void Hash(int value)
		{
			unchecked
			{
				manifest ^= (uint)value;
				manifest *= 1099511628211UL;
			}
		}

		for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
		for (int cellX = minCellX; cellX <= maxCellX; cellX++)
		{
			var rng = new Rng(unchecked(_seed ^ StableCellSeed(cellX, cellZ, 0x5a17)));
			if (!rng.Chance(.24f)) continue;
			int centreX = cellX * CandidateGrid + CandidateGrid / 2 + rng.RangeInt(-180, 180);
			int centreZ = cellZ * CandidateGrid + CandidateGrid / 2 + rng.RangeInt(-180, 180);
			bool alongX = rng.Chance(.5f);
			int halfSpan = rng.RangeInt(18, 25);
			int halfWidth = rng.RangeInt(7, 10);
			int openingHalf = halfSpan - rng.RangeInt(7, 10);
			int openingHeight = rng.RangeInt(14, 19);
			int crownThickness = rng.RangeInt(6, 9);
			int openingShift = rng.RangeInt(-2, 2);
			int leftShoulder = rng.RangeInt(-2, 2);
			int rightShoulder = rng.RangeInt(-2, 2);
			int localCentreX = centreX - originX;
			int localCentreZ = centreZ - originZ;
			int reach = halfSpan + halfWidth + 3;
			if (localCentreX + reach < 0 || localCentreZ + reach < 0 ||
			    localCentreX - reach >= Size || localCentreZ - reach >= Size) continue;

			float centreLocalX = localCentreX, centreLocalZ = localCentreZ;
			Biome biome = Plan.AtlasGuide.BiomeAt(centreLocalX, centreLocalZ);
			if (biome is not (Biome.Highland or Biome.SnowyHills)) continue;
			if (Plan.AtlasGuide.LandAt(centreLocalX, centreLocalZ) < .72f ||
			    Plan.AtlasGuide.AuthoredWetAt(centreLocalX, centreLocalZ)) continue;
			float elevation = Plan.AtlasGuide.ElevationAt(centreLocalX, centreLocalZ);
			if (elevation < .62f) continue;

			int minGround = int.MaxValue, maxGround = int.MinValue;
			bool suitable = true;
			(int u, int v)[] probes =
			{
				(0, 0), (-halfSpan, 0), (halfSpan, 0),
				(-openingHalf, -halfWidth), (-openingHalf, halfWidth),
				(openingHalf, -halfWidth), (openingHalf, halfWidth),
			};
			foreach ((int u, int v) in probes)
			{
				int gx = centreX + (alongX ? u : v);
				int gz = centreZ + (alongX ? v : u);
				float lx = gx - originX, lz = gz - originZ;
				if (gx < 2 || gz < 2 || gx >= Plan.AtlasGuide.WorldWidth - 2 ||
				    gz >= Plan.AtlasGuide.WorldDepth - 2 ||
				    Plan.AtlasGuide.LandAt(lx, lz) < .72f ||
				    Plan.AtlasGuide.AuthoredWetAt(lx, lz))
				{
					suitable = false;
					break;
				}
				int ground = ApproximateProductionHeightAt(gx, gz);
				minGround = Math.Min(minGround, ground);
				maxGround = Math.Max(maxGround, ground);
			}
			if (!suitable || maxGround - minGround > 10) continue;
			int baseY = maxGround;
			int crownY = baseY + openingHeight + crownThickness;
			if (crownY >= Plan.AtlasGuide.WorldHeight - 2) continue;

			int written = 0;
			for (int u = -halfSpan; u <= halfSpan; u++)
			for (int v = -halfWidth - 4; v <= halfWidth + 4; v++)
			{
				int gx = centreX + (alongX ? u : v);
				int gz = centreZ + (alongX ? v : u);
				int x = gx - originX, z = gz - originZ;
				if (x < 0 || z < 0 || x >= Size || z >= Size) continue;
				int i = z * Size + x;
				if (Land[i] == 0) continue;

				// A natural arch is one eroded rock mass, not a rectangular deck with
				// holes cut through it. The outer body swells at its buttresses, rounds
				// over both axes and takes only shallow coherent chips along its surface.
				// All noise is sampled in atlas coordinates so the same formation is
				// byte-identical when a moving window approaches it from another side.
				float absU = Math.Abs(u);
				float u01 = absU / halfSpan;
				float spanRound = MathF.Sqrt(MathF.Max(0f, 1f - u01 * u01));
				float edgeNoise = _nEdge.Fbm(gx * .071f + 410f,
					gz * .071f - 270f, 2);
				float openingDistance = Math.Abs(u - openingShift);
				float buttress = openingDistance >= openingHalf ? 2.8f : 0f;
				float localHalfWidth = halfWidth * (.72f + .28f * spanRound) +
					buttress + edgeNoise * 1.8f;
				if (Math.Abs(v) > localHalfWidth) continue;

				float v01 = Math.Abs(v) / MathF.Max(localHalfWidth, 1f);
				float outerRound = MathF.Sqrt(MathF.Max(0f,
					1f - u01 * u01 * .48f - v01 * v01 * .72f));
				float shoulderBias = u < 0 ? leftShoulder : rightShoulder;
				int top = baseY + 3 + (int)MathF.Round(
					(openingHeight + crownThickness) * outerRound + edgeNoise * 1.7f +
					shoulderBias * (1f - spanRound * .55f));
				bool leg = openingDistance >= openingHalf;
				float openingU = Rng.Clamp(openingDistance / openingHalf, 0f, 1f);
				int intrados = baseY + 2 + (int)MathF.Round(
					openingHeight * MathF.Sqrt(MathF.Max(0f, 1f - openingU * openingU)) +
					2.2f * v01 * v01);
				int from = leg ? Level[i] : Math.Max(Level[i], intrados);
				if (from >= top) continue;
				int columnTop = -1;

				bool InsideRock(int y)
				{
					float vertical = Rng.Clamp((y - baseY) /
						(float)Math.Max(openingHeight + crownThickness, 1), 0f, 1f);
					float rockNoise = _nEdge.Fbm(gx * .063f + y * .037f + 93f,
						gz * .063f - y * .029f - 157f, 2);
					float vShift = _nLedge.Fbm(gx * .019f + y * .021f,
						gz * .019f - 511f, 2) * 1.6f;
					float allowedWidth = localHalfWidth -
						Rng.Smoothstep(.18f, 1f, vertical) * 2.8f + rockNoise * .85f;
					float allowedSpan = halfSpan -
						Rng.Smoothstep(.32f, 1f, vertical) * 2.2f + rockNoise * .55f;
					float edgeRoom = MathF.Min(allowedWidth - Math.Abs(v - vShift),
						allowedSpan - absU);
					return edgeRoom >= 0f && (edgeRoom >= 1.15f || rockNoise >= -.08f);
				}

				for (int y = from; y < top; y++)
					if (InsideRock(y)) columnTop = y + 1;
				if (columnTop < 0) continue;
				for (int y = from; y < top; y++)
				{
					if (!InsideRock(y)) continue;
					byte block = y == columnTop - 1 ? Grid.Cap[i]
						: y == columnTop - 2 ? Grid.Sub[i]
						: (_nEdge.Fbm(gx * .046f + y * .031f,
							gz * .046f - y * .019f, 2) > .31f
								? Palette.STONE_PALE : Palette.STONE);
					Grid.Set(x, y, z, block);
					written++;
					Hash(gx); Hash(gz); Hash(y); Hash(block);
				}
				// Meshing/collision must see the roof, while gameplay must continue to use
				// the real ground beneath the opening. A single height value cannot express
				// both, so overhangs advertise a separate conservative mesh ceiling.
				if (columnTop > 0) Grid.RaiseOverhangCeiling(x, z, columnTop);
			}
			if (written == 0) continue;
			if (arches == 0) { firstX = centreX; firstZ = centreZ; }
			lastX = centreX;
			lastZ = centreZ;
			arches++;
			voxels += written;
			Hash(centreX); Hash(centreZ); Hash(alongX ? 1 : 0);
			Hash(halfSpan); Hash(halfWidth); Hash(openingHeight);
			Hash(openingShift); Hash(leftShoulder); Hash(rightShoulder);
		}

		NaturalFormations = new NaturalFormationStatistics(arches, voxels, manifest,
			firstX, firstZ, arches > 0 ? lastX : -1, arches > 0 ? lastZ : -1);
	}

	private int ApproximateProductionHeightAt(int globalX, int globalZ)
	{
		float localX = globalX - Grid.OriginX;
		float localZ = globalZ - Grid.OriginZ;
		var warp = _productionGrammar.GuideWarpAt(globalX, globalZ);
		float elevation = Plan.AtlasGuide.GuidedLandElevationAt(localX, localZ, warp);
		float height = ProductionTerrainGuide.TerrainHeightForElevation(elevation);
		float sum = (height - Base) / Step +
			_productionGrammar.MountainReliefAt(globalX, globalZ, elevation) / Step +
			_productionGrammar.TerraceOffsetAt(globalX, globalZ, elevation);
		return Rng.ClampI(Base + Step * (int)MathF.Floor(sum + .5f), 2,
			Plan.AtlasGuide.WorldHeight - 7);
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
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
	private void DescribeColumns()
	{
		// Parallel over rows. Every column writes only its own slice of the block
		// array and its own height entry, so there is nothing to contend over, and
		// the noise fields are pure functions of their coordinates — which is what
		// makes this safe AND keeps it deterministic. A given seed produces the
		// same world whatever order the rows happen to finish in.
		System.Threading.Tasks.Parallel.For(0, Size, z =>
		{
		for (int x = 0; x < Size; x++)
		{
			int i = z * Size + x;
			int h = Level[i];
			if (h < 1) h = 1;
			int drop = TerrainShape.DropBelow(Level, Size, x, z);
			float gx = GlobalX(x), gz = GlobalZ(z);
			float jitter = _nFine.Fbm(gx * 0.13f, gz * 0.13f, 2) * 0.055f;
			float rockField = _nRock.Fbm(gx * 0.0082f, gz * 0.0082f, 3) + jitter
			                + Rng.Smoothstep(Base + Step * 4, Base + Step * 7, h) * 0.34f
			                + Rng.Smoothstep(Step, Step * 3f, drop) * 0.10f;
			float sandField = 1f - Rng.Smoothstep(0f, 6f, RiverDist[i] - RiverHalf[i]);
			var biome = Plan.RegionAt(x, z).Biome;
			float tone = _nTone.Fbm(gx * 0.0115f + 90f, gz * 0.0115f + 12f, 2) + jitter;

			byte cap;
			// One generic wet-sand vocabulary turns the production fen into a
			// pale beach. Keep the proven column grammar, but let the mapped biome choose
			// its one appropriate wet cap: saturated fen uses mud while coast, lake and
			// river country retain the original sand.
			byte wetCap = biome == Biome.Wetland ? Palette.MUD : Palette.SAND;
			if (Land[i] == 0) cap = wetCap;
			else if (h <= Sea + 2 && (sandField > 0.15f || Wet[i] == 1)) cap = wetCap;
			else if (biome == Biome.SnowyHills)
				cap = rockField > 0.43f || tone < -0.24f ? Palette.SCREE : Palette.SNOW;
			else if (biome == Biome.Wetland)
				cap = Wet[i] == 1 || tone < -0.08f ? Palette.MUD : Palette.MOSS;
			else if (biome == Biome.Highland && rockField > 0.16f)
				cap = tone > 0.08f ? Palette.STONE_PALE : Palette.SCREE;
			else if (rockField > 0.42f)
				cap = _nRock.Fbm(gx * 0.03f + 9f, gz * 0.03f, 2) > 0f
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
			// Boulders were scattered before the roads were routed, so a few of
			// them are now standing in one.
			if (RockMask[i] != 0 && (Roads == null || Roads.Clear[i] == 0))
				cap = RockMask[i] == 2 ? Palette.STONE_PALE : Palette.STONE;

			// The road surface. A kept road is pale trodden stone; a trail is bare
			// worn earth, which is both what a track actually is and the only way
			// to tell the two classes apart from the air.
			if (Roads != null && Roads.Mask[i] != 0 && Land[i] != 0)
				cap = Roads.Mask[i] == (byte)RoadClass.Trail + 1 ? Palette.SOIL : Palette.PATH;

			bool grassCap = Palette.IsGrassSurface(cap);
			bool stoneSubstrate = Palette.HasStoneSubstrate(cap);
			byte substrate = grassCap
				? (stoneSubstrate ? Palette.STONE : Palette.SOIL)
				: cap;

			// A standard Step-high riser exposes exactly two stripes: the grass
			// cap and its geological substrate (soil or stone). Deeper stone still
			// exists for intentionally tall cliffs, river cuts and the world rim,
			// but never appears as a third stripe on an ordinary terrace.
			//
			// Recorded rather than written out. Two bytes and a height describe the
			// whole column; VoxelGrid reconstitutes any block in it on demand. See
			// the note there for why the dense array had to go.
			Grid.Describe(x, z, h, cap, substrate);
			Grid.Heights[i] = (short)h;
		}
		});
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
			// DRY land, tested first. Sand is a legitimate surface to stand on and
			// it is also what the lakebed is made of, so a material check alone
			// happily spawned the traveller fifteen blocks under the surface of a
			// lake. Only the waterline can tell those two apart.
			if (top <= Sea) continue;
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
