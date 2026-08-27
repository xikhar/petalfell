using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

public enum RoadClass : byte
{
	/// <summary>Trunk routes between the largest settlements. Broad and well kept.</summary>
	Major = 0,
	/// <summary>Village links and spurs. Narrower, less formal.</summary>
	Local = 1,
	/// <summary>Worn tracks out to landmarks and lonely places.</summary>
	Trail = 2,
}

public sealed class RoadSegment
{
	public RoadClass Class;
	/// <summary>Authored full surface width. Zero uses the legacy class default.</summary>
	public float Width;
	public int A, B;
	public readonly List<(int x, int z)> Points = new();
}

/// <summary>
/// The road network.
///
/// Roads are the only system in the world that has to reconcile every other
/// one: they need settlements to exist, terraces to follow, stairs to climb and
/// rivers to cross, and both vegetation and boulders have to get out of their
/// way afterwards. So they are routed last over the finished heightfield, and
/// they change no terrain at all — only which material caps a column.
///
/// That restraint is deliberate. Flattening a corridor after the contour
/// filters have run would reopen every invariant the terrain stage spent its
/// passes establishing: the terrace quantisation, the despeckled shoreline, the
/// stair carve. Instead the router is made to WANT flat ground — climbing costs
/// far more than going around, and a carved stair costs barely anything — so
/// the roads lie along the shelves because that is genuinely the cheapest way
/// across, not because a pass bulldozed them level.
///
/// Routes are laid one at a time and each one sees the ones before it at a deep
/// discount. That single rule is what turns a set of point-to-point paths into
/// a NETWORK: a later village link that runs anywhere near an existing trunk
/// finds it cheaper to join, run along it and branch off late, so the map grows
/// junctions, shared approaches and forks instead of a spray of parallel lines
/// between every pair of places.
/// </summary>
public sealed class RoadNetwork
{
	/// <summary>Routing lattice pitch, in blocks. Roads are stamped at full resolution.</summary>
	public const int Cell = 3;

	/// <summary>0 for no road, otherwise the class + 1.</summary>
	public readonly byte[] Mask;
	/// <summary>Wider corridor that vegetation, boulders and props keep out of.</summary>
	public readonly byte[] Clear;
	public readonly List<RoadSegment> Segments = new();
	/// <summary>
	/// Where a route enters open water, and which way it was heading.
	///
	/// The heading is the whole point of recording these. A bridge has to span
	/// the channel along the ROAD's axis — a deck laid perpendicular to the
	/// traffic is a jetty, not a crossing — and by the time the props stage sees
	/// a wet column there is nothing left in it to say which way anyone was
	/// going.
	/// </summary>
	public readonly List<(int x, int z, float dx, float dz)> Crossings = new();
	/// <summary>Planned links the router could not find any ground for.</summary>
	public int Unreachable;

	private readonly int _size;

	private RoadNetwork(int size)
	{
		_size = size;
		Mask = new byte[size * size];
		Clear = new byte[size * size];
	}

	private const float Blocked = float.MaxValue;

	/// <summary>
	/// Realise the authored connection graph directly. Major roads no longer ask
	/// A* where the story should go; their polylines are already the answer. The
	/// dense sampling keeps the existing stamp, reclamation and crossing code.
	/// </summary>
	public static RoadNetwork BuildAuthored(Terrain terrain, CanonicalWorldDefinition world)
	{
		var net = new RoadNetwork(terrain.Size);
		foreach (var route in world.Routes)
		{
			var seg = new RoadSegment
			{
				Class = route.Kind switch
				{
					RoadKind.Major => RoadClass.Major,
					RoadKind.Local or RoadKind.Street or RoadKind.Abandoned => RoadClass.Local,
					_ => RoadClass.Trail,
				},
				Width = route.Width,
				A = -1,
				B = -1,
			};

			for (int p = 0; p + 1 < route.Points.Count; p++)
			{
				var a = route.Points[p]; var b = route.Points[p + 1];
				float dx = b.X - a.X, dz = b.Z - a.Z;
				int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(dx * dx + dz * dz) / 2f));
				for (int s = p == 0 ? 0 : 1; s <= steps; s++)
				{
					float t = s / (float)steps;
					var point = ((int)MathF.Round(a.X + dx * t), (int)MathF.Round(a.Z + dz * t));
					if (seg.Points.Count == 0 || seg.Points[^1] != point) seg.Points.Add(point);
				}
			}
			net.Segments.Add(seg);
		}
		net.Stamp(terrain);
		return net;
	}

	public static RoadNetwork Build(Terrain terrain, List<SettlementSite> sites,
		List<Landmark> marks, int seed)
	{
		var net = new RoadNetwork(terrain.Size);
		if (sites == null || sites.Count < 2) return net;

		int lw = (terrain.Size + Cell - 1) / Cell;
		var router = new Router(terrain, lw, seed);

		// Which lattice node each site sits on.
		var nodes = new int[sites.Count];
		for (int i = 0; i < sites.Count; i++)
			nodes[i] = router.NodeOf(sites[i].X, sites[i].Z);

		foreach (var (a, b, cls) in PlanEdges(sites, seed))
		{
			var lattice = router.Route(nodes[a], nodes[b]);
			if (lattice == null) { net.Unreachable++; continue; }

			var seg = new RoadSegment { Class = cls, A = a, B = b };
			var raw = new List<(float x, float z)>(lattice.Count);
			foreach (int n in lattice)
			{
				raw.Add((n % lw * Cell + Cell / 2, n / lw * Cell + Cell / 2));
				router.MarkUsed(n);
			}
			foreach (var (x, z) in Smooth(raw))
				seg.Points.Add(((int)MathF.Round(x), (int)MathF.Round(z)));
			net.Segments.Add(seg);
		}

		// Trails out to the landmarks.
		//
		// This is what stops the road network being a closed loop between places
		// nobody lives and turns it into something that leads somewhere. A trail
		// is routed from each significant landmark back to its nearest remnant,
		// and because routes see earlier ones at a deep discount it will find the
		// existing network and join it rather than cutting its own line home —
		// so what actually appears is a spur off the road, which is what a track
		// to a watchtower is.
		if (marks != null && sites.Count > 0)
		{
			int budget = 24 + sites.Count * 2;
			foreach (var mark in marks)
			{
				if (budget <= 0) break;
				if (!mark.Significant) continue;

				int nearest = -1;
				float best = float.MaxValue;
				for (int i = 0; i < sites.Count; i++)
				{
					float dx = sites[i].X - mark.X, dz = sites[i].Z - mark.Z;
					float d = dx * dx + dz * dz;
					if (d < best) { best = d; nearest = i; }
				}
				// Too far and the trail is longer than the thing it leads to is
				// worth; those landmarks are found by wandering, which is fine.
				if (nearest < 0 || best > 260f * 260f) continue;

				var lattice = router.Route(router.NodeOf(mark.X, mark.Z), nodes[nearest]);
				if (lattice == null) continue;

				var seg = new RoadSegment { Class = RoadClass.Trail, A = -1, B = nearest };
				var raw = new List<(float x, float z)>(lattice.Count);
				foreach (int n in lattice)
				{
					raw.Add((n % lw * Cell + Cell / 2, n / lw * Cell + Cell / 2));
					router.MarkUsed(n);
				}
				foreach (var (x, z) in Smooth(raw))
					seg.Points.Add(((int)MathF.Round(x), (int)MathF.Round(z)));
				net.Segments.Add(seg);
				budget--;
			}
		}

		net.Stamp(terrain);
		return net;
	}

	/// <summary>
	/// Chaikin corner cutting.
	///
	/// The router works on an eight-way lattice, so every route it returns is
	/// made of steps that are either axis-aligned or at exactly forty-five
	/// degrees, and the joints between them are visible as hard kinks. Two
	/// rounds of corner cutting pull those into curves without moving the line
	/// more than a block, which is well inside the road's own width — the route
	/// still goes where the search decided it should go.
	/// </summary>
	private static List<(float x, float z)> Smooth(List<(float x, float z)> path)
	{
		var cur = path;
		for (int pass = 0; pass < 2 && cur.Count > 2; pass++)
		{
			var next = new List<(float x, float z)>(cur.Count * 2) { cur[0] };
			for (int i = 0; i + 1 < cur.Count; i++)
			{
				var a = cur[i];
				var b = cur[i + 1];
				next.Add((a.x * 0.75f + b.x * 0.25f, a.z * 0.75f + b.z * 0.25f));
				next.Add((a.x * 0.25f + b.x * 0.75f, a.z * 0.25f + b.z * 0.75f));
			}
			next.Add(cur[^1]);
			cur = next;
		}
		return cur;
	}

	/* ----------------------------------------------------------------
	 * Which places connect to which.
	 * ---------------------------------------------------------------- */
	private static List<(int a, int b, RoadClass cls)> PlanEdges(List<SettlementSite> sites, int seed)
	{
		var edges = new List<(int a, int b, RoadClass cls)>();
		int n = sites.Count;

		float D2(int i, int j)
		{
			float dx = sites[i].X - sites[j].X, dz = sites[i].Z - sites[j].Z;
			return dx * dx + dz * dz;
		}

		// A minimum spanning tree first. Every settlement is reachable from every
		// other one before a single optional road is considered, which is what
		// stops a village ending up stranded behind a river with no way in.
		var inTree = new bool[n];
		var best = new float[n];
		var from = new int[n];
		Array.Fill(best, float.MaxValue);
		Array.Fill(from, -1);
		best[0] = 0f;
		var tree = new List<(int a, int b)>();

		for (int k = 0; k < n; k++)
		{
			int pick = -1;
			for (int i = 0; i < n; i++)
				if (!inTree[i] && (pick < 0 || best[i] < best[pick])) pick = i;
			if (pick < 0) break;
			inTree[pick] = true;
			if (from[pick] >= 0) tree.Add((from[pick], pick));
			for (int i = 0; i < n; i++)
			{
				if (inTree[i]) continue;
				float d = D2(pick, i);
				if (d >= best[i]) continue;
				best[i] = d;
				from[i] = pick;
			}
		}

		// A tree alone has no loops, and a world with no loops has no shortcuts,
		// no alternative routes and no junctions worth the name. Add the shortest
		// few links that are not already in the tree.
		var extra = new List<(float d, int a, int b)>();
		for (int i = 0; i < n; i++)
		for (int j = i + 1; j < n; j++)
		{
			if (tree.Contains((i, j)) || tree.Contains((j, i))) continue;
			extra.Add((D2(i, j), i, j));
		}
		extra.Sort((p, q) => p.d.CompareTo(q.d));
		int loops = Math.Max(1, n / 3);

		// A road's class comes from what it joins, not from how it was chosen: a
		// link between two towns is a highway however incidental it was to the
		// spanning tree.
		RoadClass Classify(int a, int b)
		{
			var lo = (SettlementKind)Math.Min((int)sites[a].Kind, (int)sites[b].Kind);
			return lo switch
			{
				SettlementKind.Town => RoadClass.Major,
				SettlementKind.Village => RoadClass.Local,
				_ => RoadClass.Trail,
			};
		}

		// Trunks first, so the humbler routes discover them and join on.
		foreach (var (a, b) in tree) edges.Add((a, b, Classify(a, b)));
		for (int i = 0; i < loops && i < extra.Count; i++)
			edges.Add((extra[i].a, extra[i].b, Classify(extra[i].a, extra[i].b)));
		edges.Sort((p, q) => p.cls.CompareTo(q.cls));
		return edges;
	}

	/* ----------------------------------------------------------------
	 * Painting.
	 * ---------------------------------------------------------------- */
	private void Stamp(Terrain terrain)
	{
		int S = _size;
		foreach (var seg in Segments)
		{
			float half = seg.Width > 0f ? seg.Width * 0.5f : seg.Class switch
			{
				RoadClass.Major => 2.2f,
				RoadClass.Local => 1.5f,
				_ => 0.9f,
			};
			float clear = half + 3.5f;
			byte tag = (byte)((byte)seg.Class + 1);

			for (int p = 0; p + 1 < seg.Points.Count; p++)
			{
				var (x0, z0) = seg.Points[p];
				var (x1, z1) = seg.Points[p + 1];
				// After smoothing the points are a fraction of a block apart, so
				// two samples per span already overlap.
				int steps = 2;
				for (int s = 0; s <= steps; s++)
				{
					float t = s / (float)steps;
					float cx = x0 + (x1 - x0) * t, cz = z0 + (z1 - z0) * t;
					Disc(terrain, cx, cz, half, clear, tag);
				}
			}
		}

		// Where each route enters a CHANNEL — not merely where it enters open
		// water, which was the original test and never once fired.
		//
		// The reason is worth recording. A river gets fords, and a ford is marked
		// as land so that things can walk over it, so a road crossing a river at
		// its shallowest point is never standing on water by the time anything
		// downstream looks. The router had been quietly doing the sensible thing
		// all along — finding the fords and using them — and the bridge stage was
		// asking a question that could only ever be answered "no".
		//
		// The channel is the right thing to test anyway. A ford is exactly where
		// you would build a bridge: it is the narrow, shallow, already-chosen
		// place to get across.
		//
		// Walked along the segments rather than swept over the mask, because only
		// the segment knows the direction of travel.
		foreach (var seg in Segments)
		{
			bool wasWet = false;
			for (int p = 0; p < seg.Points.Count; p++)
			{
				var (x, z) = seg.Points[p];
				if (x < 0 || z < 0 || x >= S || z >= S) continue;
				int idx = z * S + x;
				bool wet = terrain.Land[idx] == 0 ||
					(terrain.RiverHalf[idx] > 2f && terrain.RiverDist[idx] <= terrain.RiverHalf[idx] + 1f);

				if (wet && !wasWet)
				{
					// Look a little way either side for the heading: consecutive
					// points after smoothing are a fraction of a block apart, so
					// neighbours alone give a direction quantised to nothing useful.
					int a = Math.Max(0, p - 6), b = Math.Min(seg.Points.Count - 1, p + 6);
					float dx = seg.Points[b].x - seg.Points[a].x;
					float dz = seg.Points[b].z - seg.Points[a].z;
					float len = MathF.Sqrt(dx * dx + dz * dz);
					if (len > 0.01f) Crossings.Add((x, z, dx / len, dz / len));
				}
				wasWet = wet;
			}
		}
	}

	private void Disc(Terrain terrain, float cx, float cz, float half, float clear, byte tag)
	{
		int S = _size;
		int r = (int)MathF.Ceiling(clear);
		int x0 = Rng.ClampI((int)cx - r, 0, S - 1), x1 = Rng.ClampI((int)cx + r, 0, S - 1);
		int z0 = Rng.ClampI((int)cz - r, 0, S - 1), z1 = Rng.ClampI((int)cz + r, 0, S - 1);

		for (int z = z0; z <= z1; z++)
		for (int x = x0; x <= x1; x++)
		{
			float dx = x + 0.5f - cx, dz = z + 0.5f - cz;
			float d = MathF.Sqrt(dx * dx + dz * dz);
			if (d > clear) continue;
			int i = z * S + x;
			if (Clear[i] == 0) Clear[i] = 1;
			if (d > half) continue;

			// Reclamation. plan.md §12.4: most of this network is being taken
			// back, and how far depends on how long ago the far end stopped
			// mattering. Sampled as coherent patches rather than per block — a
			// road fails in stretches, and a per-block sprinkle reads as a
			// dithered texture rather than as a surface going under.
			float age = terrain.Plan.AbandonmentAt(x, z);
			if (age > 0.18f)
			{
				float patch = 0.5f + 0.5f
					* MathF.Sin(x * 0.055f + z * 0.021f) * MathF.Cos(z * 0.048f - x * 0.017f);
				if (patch < (age - 0.18f) * 1.45f) continue;
			}
			// A road may not be painted onto a cliff face. The router avoids them,
			// but the stamp has width, and half a carriageway hanging over a
			// two-block riser reads as a paint spill rather than a surface.
			if (TerrainShape.RiseAbove(terrain.Level, S, x, z) > Terrain.Step) continue;
			if (Mask[i] == 0 || tag < Mask[i]) Mask[i] = tag;
		}
	}

	/* ----------------------------------------------------------------
	 * A* over the routing lattice.
	 * ---------------------------------------------------------------- */
	private sealed class Router
	{
		private readonly Terrain _t;
		private readonly int _lw;
		private readonly float[] _g;
		private readonly int[] _from;
		private readonly bool[] _closed;
		private readonly bool[] _used;
		private readonly short[] _level;
		private readonly byte[] _land, _stair;
		private readonly float[] _half;
		private readonly float[] _wander;

		public Router(Terrain terrain, int latticeWidth, int seed)
		{
			_t = terrain;
			_lw = latticeWidth;
			int n = _lw * _lw;
			_g = new float[n];
			_from = new int[n];
			_closed = new bool[n];
			_used = new bool[n];

			// One sample per lattice node, taken once. Re-reading the block arrays
			// inside the search costs more than the whole search.
			_level = new short[n];
			_land = new byte[n];
			_stair = new byte[n];
			_half = new float[n];
			_wander = new float[n];
			var noise = new Noise2D(seed + 601);
			for (int lz = 0; lz < _lw; lz++)
			for (int lx = 0; lx < _lw; lx++)
			{
				int x = Rng.ClampI(lx * Cell + Cell / 2, 0, terrain.Size - 1);
				int z = Rng.ClampI(lz * Cell + Cell / 2, 0, terrain.Size - 1);
				int i = z * terrain.Size + x, j = lz * _lw + lx;
				_level[j] = terrain.Level[i];
				_land[j] = terrain.Land[i];
				_stair[j] = terrain.StairMask[i];
				_half[j] = terrain.RiverHalf[i];
				// Coarse enough that a route bends over tens of blocks rather than
				// jittering block to block, and shallow enough that it never beats a
				// genuine reason to go somewhere.
				_wander[j] = 1f + noise.Fbm(x * 0.013f, z * 0.013f, 3) * 0.95f;
			}
		}

		public int NodeOf(int x, int z) =>
			Rng.ClampI(z / Cell, 0, _lw - 1) * _lw + Rng.ClampI(x / Cell, 0, _lw - 1);

		public void MarkUsed(int node) => _used[node] = true;

		private float Enter(int from, int to, float step)
		{
			if (_land[to] == 0)
			{
				// Open water. A road may cross a CHANNEL — that is what bridges are
				// for — but never strike out across a lake or the sea.
				//
				// Testing only the upper width bound was a real bug rather than a
				// missing nicety: still water carries no channel width at all, so a
				// lake read as a nought-wide stream and the router was free to lay a
				// highway straight across it at a modest premium.
				if (_half[to] < 0.5f || _half[to] > 9f) return Blocked;
				// Six times the going rate, not sixteen.
				//
				// At sixteen a six-block channel costs about a hundred blocks of
				// detour to cross, and on a landmass this shape there is almost
				// always a way round that cheap — so the router never once got its
				// feet wet and the whole crossing path went untested. Six still
				// says "prefer the bank", but it stops pretending a ford is worse
				// than walking half a province.
				return step * 6f;
			}

			int climb = Math.Abs(_level[to] - _level[from]);
			float cost;
			if (_stair[to] == 1)
			{
				// A carved stair is the terrain telling you where it expects to be
				// climbed. Roads should find those and use them.
				cost = step * 0.6f;
			}
			else if (climb > Terrain.Step)
			{
				// A riser taller than one terrace is a cliff. Not forbidden — a
				// mountain road has to get up somehow — but expensive enough that
				// going round is almost always cheaper.
				cost = step * (1f + climb * climb * 1.6f);
			}
			else cost = step * (1f + climb * 1.1f);

			// Wander.
			//
			// Over a flat meadow every direction costs exactly the same, and A* on
			// a tie runs dead straight along the lattice — the first network came
			// out as rectangles with 45-degree corners, which is the one thing a
			// country road never looks like. A gentle noise field over the ground
			// gives the search something to prefer, so routes bend around soft
			// ground that is not there in the same way they bend around a hill that
			// is. It costs nothing and it is the whole difference between a road
			// and a ruler.
			cost *= _wander[to];

			// Braiding. See the class note: this is what makes it a network.
			if (_used[to]) cost *= 0.3f;
			return cost;
		}

		public List<int> Route(int start, int goal)
		{
			if (start == goal) return new List<int> { start };
			Array.Fill(_g, float.MaxValue);
			Array.Fill(_from, -1);
			Array.Clear(_closed);

			int gx = goal % _lw, gz = goal / _lw;
			float H(int n)
			{
				float dx = n % _lw - gx, dz = n / _lw - gz;
				return MathF.Sqrt(dx * dx + dz * dz) * Cell;
			}

			var open = new PriorityQueue<int, float>();
			_g[start] = 0f;
			open.Enqueue(start, H(start));

			while (open.TryDequeue(out int cur, out _))
			{
				if (cur == goal) break;
				if (_closed[cur]) continue;
				_closed[cur] = true;

				int cx = cur % _lw, cz = cur / _lw;
				for (int dz = -1; dz <= 1; dz++)
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dz == 0) continue;
					int nx = cx + dx, nz = cz + dz;
					if (nx < 0 || nz < 0 || nx >= _lw || nz >= _lw) continue;
					int next = nz * _lw + nx;
					if (_closed[next]) continue;

					float step = (dx != 0 && dz != 0) ? Cell * 1.41421f : Cell;
					float add = Enter(cur, next, step);
					if (add >= Blocked) continue;

					float g = _g[cur] + add;
					if (g >= _g[next]) continue;
					_g[next] = g;
					_from[next] = cur;
					open.Enqueue(next, g + H(next));
				}
			}

			if (_from[goal] < 0 && start != goal) return null;

			var path = new List<int>();
			for (int n = goal; n >= 0; n = _from[n])
			{
				path.Add(n);
				if (n == start) break;
			}
			path.Reverse();
			return path.Count > 1 ? path : null;
		}
	}
}
