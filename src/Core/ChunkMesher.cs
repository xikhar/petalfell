using System;
using System.Collections.Generic;
using Godot;

namespace Petalfell.Core;

/// <summary>
/// Turns a region of the voxel grid into renderable surfaces, collision
/// triangles, and — the part that matters most — the explicit ink edge graph.
///
/// The outline system is not a screen-space edge detector. It is data: every
/// edge knows its two owning faces, their normals, whether each face belongs to
/// the pale palette, and whether the fold is concave. That is what makes "light
/// edges only between two pale surfaces, concave always dark, dark lines
/// stopping cleanly against pale ones" expressible at all.
///
/// Edges are collected over a one-voxel margin so a boundary edge sees the
/// faces on both sides of the seam, then kept by whichever chunk contains the
/// edge's midpoint. Exactly one chunk owns each edge, and long strokes do not
/// break where chunks meet.
/// </summary>
public static class ChunkMesher
{
	public const int ChunkSize = 24;
	/// <summary>Local edge lattice spans the chunk plus one voxel of margin on each side.</summary>
	private const int Span = ChunkSize + 3;

	private static readonly int[,] Normals =
	{
		{ 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 },
	};

	// Axis of the normal, and the two in-plane axes.
	//
	// (u, v, n) is right-handed for every face — u x v = +n — so the corner walk
	// below is counter-clockwise seen from outside a positive face and clockwise
	// seen from outside a negative one. Godot treats clockwise as front-facing,
	// so positive faces get their index order reversed and negative faces are
	// emitted as walked. Getting this table inconsistent is how half the world
	// ends up inside-out.
	private static readonly int[] NAxis = { 0, 0, 1, 1, 2, 2 };
	private static readonly int[] UAxis = { 1, 1, 2, 2, 0, 0 };
	private static readonly int[] VAxis = { 2, 2, 0, 0, 1, 1 };

	private struct EdgeRec
	{
		public sbyte Dir0, Dir1;
		public byte Count;
		public bool Light0, Light1, Force, Concave;
	}

	private struct Run
	{
		public Vector3 Start, End;
		public int Dir0, Dir1;
		public bool Light;
		// Six four-bit face-pair codes fit exactly in a float-backed custom
		// channel. The shader evaluates them against the live camera and knows
		// whether at least two incident edges are actually pale at each endpoint.
		public uint PaleEdgesAtStart, PaleEdgesAtEnd;
	}

	public sealed class ChunkMeshData
	{
		public ArrayMesh Surface;
		public ArrayMesh Ink;
		/// <summary>Per ink surface: true if it holds the pale runs. Dark must draw after pale.</summary>
		public bool[] InkSurfaceIsLight;
		public Vector3[] CollisionFaces;
		public bool Empty => Surface == null;
	}

	/* ---- reusable scratch -------------------------------------------
	 * Meshing runs on the main thread with a frame budget, so one set of
	 * buffers is reused for every chunk. Allocating the edge lattice per chunk
	 * was the single largest cost in the pass. */
	private static EdgeRec[] _edges;
	private static readonly List<int> _touched = new(8192);
	private static readonly List<Vector3> _verts = new(8192);
	private static readonly List<Vector3> _norms = new(8192);
	private static readonly List<Color> _cols = new(8192);
	/// <summary>Per vertex: (pattern + drip depth, drip colour rgb).</summary>
	private static readonly List<float> _surf = new(16384);
	private static readonly List<int> _idx = new(12288);
	private static readonly List<Run> _runs = new(4096);

	private static int EdgeIndex(int lx, int ly, int lz, int axis) =>
		((ly * Span + lz) * Span + lx) * 3 + axis;

	public static ChunkMeshData Build(VoxelGrid grid, int ci, int ck)
	{
		_edges ??= new EdgeRec[Span * Span * grid.Height * 3];

		int x0 = ci * ChunkSize, z0 = ck * ChunkSize;
		int x1 = Math.Min(grid.Size, x0 + ChunkSize), z1 = Math.Min(grid.Size, z0 + ChunkSize);

		_verts.Clear(); _norms.Clear(); _cols.Clear(); _idx.Clear(); _surf.Clear();
		foreach (int t in _touched) _edges[t] = default;
		_touched.Clear();

		// Nothing above the tallest column in the neighbourhood is worth
		// visiting, and most of a chunk is solid rock nobody will ever see.
		int yTop = 0;
		for (int z = z0 - 1; z <= z1; z++)
		for (int x = x0 - 1; x <= x1; x++)
		{
			if (x < 0 || z < 0 || x >= grid.Size || z >= grid.Size) continue;
			yTop = Math.Max(yTop, grid.Heights[z * grid.Size + x]);
		}
		yTop = Math.Min(grid.Height, yTop + 1);

		for (int y = 0; y < yTop; y++)
		for (int z = z0 - 1; z <= z1; z++)
		for (int x = x0 - 1; x <= x1; x++)
		{
			if (x < 0 || z < 0 || x >= grid.Size || z >= grid.Size) continue;
			byte id = grid.Blocks[grid.Index(x, y, z)];
			if (id == Palette.AIR) continue;
			bool inside = x >= x0 && x < x1 && z >= z0 && z < z1;
			var def = Palette.Get(id);

			for (int f = 0; f < 6; f++)
			{
				int nx = Normals[f, 0], ny = Normals[f, 1], nz = Normals[f, 2];
				if (grid.SolidAt(x + nx, y + ny, z + nz)) continue;

				Color face = f == 2 ? def.Top : (f == 3 ? def.Bottom : def.Side);
				bool faceLight = f == 2 ? def.TopLight : (f == 3 ? def.BottomLight : def.SideLight);

				// Emissive rides in the vertex alpha rather than in a separate
				// unlit group; one surface keeps collision and the ink graph
				// reading from the same triangles.
				if (inside) EmitFace(grid, x, y, z, f, face, def.Emissive, def.Pattern);

				// The grass cap promotes its own convex perimeter to the pale
				// ink even where the substrate below it is not pale enough to
				// qualify on luminance alone.
				CollectEdges(grid, x0, z0, x, y, z, f, faceLight, def.LightEdge && f == 2);
			}
		}

		var data = new ChunkMeshData();
		if (_verts.Count == 0) return data;

		var mesh = new ArrayMesh();
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = _verts.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = _norms.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = _cols.ToArray();
		arrays[(int)Mesh.ArrayType.Custom0] = _surf.ToArray();
		var indices = _idx.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices;
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
			(Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 13));
		data.Surface = mesh;

		// Collision reuses the triangles we already computed. A second
		// definition of the world is a second thing to drift out of sync.
		var faces = new Vector3[indices.Length];
		for (int i = 0; i < indices.Length; i++) faces[i] = _verts[indices[i]];
		data.CollisionFaces = faces;

		MergeRuns(grid, x0, z0, x1, z1, yTop);
		if (_runs.Count > 0) data.Ink = BuildInkMesh(out data.InkSurfaceIsLight);

		return data;
	}

	/* ================================================================
	 * surface
	 * ================================================================ */

	private static void EmitFace(VoxelGrid grid, int x, int y, int z, int f,
		Color color, float emissive, float pattern)
	{
		int na = NAxis[f], ua = UAxis[f], va = VAxis[f];
		int sign = Normals[f, na];
		var n = new Vector3(Normals[f, 0], Normals[f, 1], Normals[f, 2]);

		// The grass drip.
		//
		// A shelf in the reference is not a green band butted flat against a
		// terracotta one: turf spills raggedly over the lip and hangs a little
		// way down the face below it. That ragged boundary is the single most
		// recognisable thing about the terraces, and it lives on the block
		// UNDER the grass rather than on the grass itself — a grass block's own
		// side is already green, so spilling over its own face does nothing.
		float fringe = 0f;
		var fringeColor = default(Color);
		if (na != 1)   // side faces only; a top or bottom face has no lip
		{
			byte above = grid.At(x, y + 1, z);
			if (Palette.IsGrassSurface(above) || above == Palette.MOSS)
			{
				fringe = 1f;
				fringeColor = Palette.Get(above).Fringe;
			}
		}

		int baseIdx = _verts.Count;
		Span<float> ao = stackalloc float[4];

		for (int corner = 0; corner < 4; corner++)
		{
			int cu = (corner == 1 || corner == 2) ? 1 : 0;
			int cv = (corner >= 2) ? 1 : 0;

			var p = new Vector3(x, y, z);
			p[na] += sign > 0 ? 1 : 0;
			p[ua] += cu;
			p[va] += cv;
			_verts.Add(p);
			_norms.Add(n);
			ao[corner] = VertexAo(grid, x, y, z, na, ua, va, sign, cu, cv);
		}

		for (int corner = 0; corner < 4; corner++)
		{
			// AO is the main reason the geometry reads at all; it is baked here
			// rather than approximated in the shader.
			float a = ao[corner];
			_cols.Add(new Color(color.R * a, color.G * a, color.B * a, emissive));

			// One custom channel carries everything the surface shader needs:
			// the pattern id and the drip depth packed into a single float, then
			// the drip colour. Occlusion is folded into the drip colour here so
			// the shader never has to reconstruct it.
			_surf.Add(pattern + fringe * 0.9f);
			_surf.Add(fringeColor.R * a);
			_surf.Add(fringeColor.G * a);
			_surf.Add(fringeColor.B * a);
		}

		if (sign > 0)
		{
			_idx.Add(baseIdx + 0); _idx.Add(baseIdx + 2); _idx.Add(baseIdx + 1);
			_idx.Add(baseIdx + 0); _idx.Add(baseIdx + 3); _idx.Add(baseIdx + 2);
		}
		else
		{
			_idx.Add(baseIdx + 0); _idx.Add(baseIdx + 1); _idx.Add(baseIdx + 2);
			_idx.Add(baseIdx + 0); _idx.Add(baseIdx + 2); _idx.Add(baseIdx + 3);
		}
	}

	/// <summary>Classic three-neighbour voxel AO, sampled in the plane just outside the face.</summary>
	private static float VertexAo(VoxelGrid grid, int x, int y, int z,
		int na, int ua, int va, int sign, int cu, int cv)
	{
		int du = cu == 1 ? 1 : -1;
		int dv = cv == 1 ? 1 : -1;

		Span<int> b = stackalloc int[3];
		b[0] = x; b[1] = y; b[2] = z;
		b[na] += sign;

		bool s1 = SolidOffset(grid, b, ua, du, -1, 0);
		bool s2 = SolidOffset(grid, b, va, dv, -1, 0);
		bool cor = SolidOffset(grid, b, ua, du, va, dv);

		int occ = (s1 && s2) ? 3 : ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cor ? 1 : 0));
		// 1.0 down to ~0.62 — deep enough to describe the form, shallow enough
		// that the palette stays high-key.
		return 1f - occ * 0.127f;
	}

	private static bool SolidOffset(VoxelGrid grid, Span<int> c, int a1, int d1, int a2, int d2)
	{
		Span<int> p = stackalloc int[3];
		p[0] = c[0]; p[1] = c[1]; p[2] = c[2];
		p[a1] += d1;
		if (a2 >= 0) p[a2] += d2;
		return grid.SolidAt(p[0], p[1], p[2]);
	}

	/* ================================================================
	 * the edge graph
	 * ================================================================ */

	/// <summary>
	/// Register this face's ownership of its four border edges.
	///
	/// Three cases per border, and only two of them produce ink:
	///   b solid          -> the surface folds inward. Concave, always dark.
	///   a air            -> the face ends here. Convex silhouette edge.
	///   a solid, b air   -> the same plane continues into the next voxel. That
	///                       is a per-voxel seam, and the art direction says
	///                       never to draw those.
	/// </summary>
	private static void CollectEdges(VoxelGrid grid, int x0, int z0,
		int x, int y, int z, int f, bool light, bool force)
	{
		int na = NAxis[f], ua = UAxis[f], va = VAxis[f];
		int sign = Normals[f, na];

		// Allocated once for the whole call, not once per border. A stackalloc
		// inside a loop is not released until the method returns, so the frame
		// grows with the iteration count — harmless at four fixed iterations,
		// but this is the hottest function in the mesher and the pattern is only
		// ever one careless edit away from being unbounded.
		Span<int> a = stackalloc int[3];
		Span<int> b = stackalloc int[3];
		Span<int> p = stackalloc int[3];

		for (int bi = 0; bi < 4; bi++)
		{
			int axis = (bi < 2) ? ua : va;
			int dir = (bi % 2 == 0) ? 1 : -1;

			a[0] = x; a[1] = y; a[2] = z;
			a[axis] += dir;
			bool aSolid = grid.SolidAt(a[0], a[1], a[2]);

			b[0] = a[0]; b[1] = a[1]; b[2] = a[2];
			b[na] += sign;
			bool bSolid = grid.SolidAt(b[0], b[1], b[2]);

			bool concave = bSolid;
			if (!concave && aSolid) continue;   // coplanar continuation

			// The border segment: the face's two corners on the `dir` side of
			// `axis`. It runs along whichever in-plane axis is not `axis`.
			int along = (axis == ua) ? va : ua;

			p[0] = x; p[1] = y; p[2] = z;
			p[na] += sign > 0 ? 1 : 0;
			if (dir > 0) p[axis] += 1;

			int lx = p[0] - x0 + 1, lz = p[2] - z0 + 1;
			if (lx < 0 || lz < 0 || lx >= Span || lz >= Span) continue;
			if (p[1] < 0 || p[1] >= grid.Height) continue;

			int ei = EdgeIndex(lx, p[1], lz, along);
			ref var rec = ref _edges[ei];
			if (rec.Count == 0 && rec.Dir0 == 0 && rec.Dir1 == 0) _touched.Add(ei);

			rec.Concave |= concave;
			rec.Force |= force;
			sbyte fs = (sbyte)(f + 1);
			if (rec.Dir0 == 0) { rec.Dir0 = fs; rec.Light0 = light; rec.Count = 1; }
			else if (rec.Dir0 == fs) rec.Light0 = rec.Light0 && light;
			else if (rec.Dir1 == 0)
			{
				// Face traversal order changes across a chunk halo. Keep the two
				// owners canonical so the same physical edge always has the same
				// merge style in both chunks.
				if (fs < rec.Dir0)
				{
					rec.Dir1 = rec.Dir0; rec.Light1 = rec.Light0;
					rec.Dir0 = fs; rec.Light0 = light;
				}
				else
				{
					rec.Dir1 = fs; rec.Light1 = light;
				}
				rec.Count = 2;
			}
			else if (rec.Dir1 == fs) rec.Light1 = rec.Light1 && light;
			else rec.Count = 3;   // non-manifold: three or more faces meet. Always dark.
		}
	}

	/// <summary>
	/// Walk the edge lattice along each axis and merge adjacent unit edges with
	/// identical topology into clean long runs.
	/// </summary>
	private static void MergeRuns(VoxelGrid grid, int x0, int z0, int x1, int z1, int yTop)
	{
		_runs.Clear();
		int yMax = Math.Min(grid.Height, yTop + 1);

		// Hoisted above the axis loop for the same reason as in CollectEdges: a
		// stackalloc lives until the method returns, so one inside a loop grows
		// the frame every iteration.
		Span<int> local = stackalloc int[3];

		for (int axis = 0; axis < 3; axis++)
		{
			int spanAlong = axis == 1 ? yMax : Span;
			int oa = axis == 0 ? 1 : 0;              // first fixed axis
			int ob = axis == 2 ? 1 : 2;              // second fixed axis
			int spanA = oa == 1 ? yMax : Span;
			int spanB = ob == 1 ? yMax : Span;

			for (int fa = 0; fa < spanA; fa++)
			for (int fb = 0; fb < spanB; fb++)
			{
				int runStart = -1;
				EdgeRec style = default;

				for (int a = 0; a <= spanAlong; a++)
				{
					bool has = false;
					EdgeRec rec = default;
					if (a < spanAlong)
					{
						local[axis] = a; local[oa] = fa; local[ob] = fb;
						int ei = EdgeIndex(local[0], local[1], local[2], axis);
						rec = _edges[ei];
						// Ownership belongs to each unit edge, before merging. Filtering a
						// completed halo run by its midpoint makes adjacent chunks both emit
						// several of the same units (or neither emit them), which is the
						// source of locally doubled and abruptly cut strokes.
						has = rec.Dir0 != 0 && UnitBelongsToChunk(
							grid, local[0], local[2], axis,
							x0, z0, x1, z1);
					}

					bool same = has && runStart >= 0 &&
					            rec.Dir0 == style.Dir0 && rec.Dir1 == style.Dir1 &&
					            rec.Count == style.Count && rec.Concave == style.Concave &&
					            IsLight(rec) == IsLight(style);

					if (same) continue;

					if (runStart >= 0)
					{
						local[axis] = runStart; local[oa] = fa; local[ob] = fb;
						var start = new Vector3(local[0] + x0 - 1, local[1], local[2] + z0 - 1);
						var end = start;
						end[axis] = start[axis] + (a - runStart);
						AddRun(start, end, style);
					}

					runStart = has ? a : -1;
					style = rec;
				}
			}
		}

		EncodeEndpointTopology(grid, x0, z0);
	}

	private static bool IsLight(in EdgeRec r) =>
		r.Count == 2 && !r.Concave && ((r.Light0 && r.Light1) || r.Force);

	/// <summary>
	/// Stable ownership for one lattice edge. Edges on a chunk plane belong to
	/// the cell on its positive side (clamped at the world rim), exactly once.
	/// The one-cell halo still supplies both owning faces to that chunk.
	/// </summary>
	private static bool UnitBelongsToChunk(VoxelGrid grid,
		int lx, int lz, int axis, int x0, int z0, int x1, int z1)
	{
		float mx = lx + x0 - 1 + (axis == 0 ? 0.5f : 0f);
		float mz = lz + z0 - 1 + (axis == 2 ? 0.5f : 0f);
		int cellX = Math.Clamp((int)MathF.Floor(mx), 0, grid.Size - 1);
		int cellZ = Math.Clamp((int)MathF.Floor(mz), 0, grid.Size - 1);
		return cellX >= x0 && cellX < x1 && cellZ >= z0 && cellZ < z1;
	}

	private static void AddRun(Vector3 start, Vector3 end, in EdgeRec rec)
	{
		int d0 = rec.Dir0 - 1;
		int d1 = rec.Dir1 > 0 ? rec.Dir1 - 1 : d0;

		_runs.Add(new Run
		{
			Start = start, End = end,
			Dir0 = d0, Dir1 = d1,
			Light = IsLight(rec),
		});
	}

	/// <summary>
	/// Packs every pale-eligible unit edge incident to each run endpoint. This is
	/// read from the complete edge lattice rather than from merged runs, so a
	/// dark branch meeting the middle of one long pale run still sees the two
	/// pale directions passing through that vertex.
	/// </summary>
	private static void EncodeEndpointTopology(VoxelGrid grid, int x0, int z0)
	{
		for (int i = 0; i < _runs.Count; i++)
		{
			var run = _runs[i];
			run.PaleEdgesAtStart = PackPaleEdges(run.Start);
			run.PaleEdgesAtEnd = PackPaleEdges(run.End);
			_runs[i] = run;
		}

		uint PackPaleEdges(Vector3 point)
		{
			int lx = Mathf.RoundToInt(point.X) - x0 + 1;
			int ly = Mathf.RoundToInt(point.Y);
			int lz = Mathf.RoundToInt(point.Z) - z0 + 1;
			uint packed = 0;
			int shift = 0;

			for (int axis = 0; axis < 3; axis++)
			{
				Add(lx, ly, lz, axis);
				int ex = lx, ey = ly, ez = lz;
				if (axis == 0) ex--;
				else if (axis == 1) ey--;
				else ez--;
				Add(ex, ey, ez, axis);
			}
			return packed;

			void Add(int ex, int ey, int ez, int axis)
			{
				if (shift >= 24 || ex < 0 || ez < 0 || ey < 0 ||
					ex >= Span || ez >= Span || ey >= grid.Height) return;
				var edge = _edges[EdgeIndex(ex, ey, ez, axis)];
				if (!IsLight(edge)) return;

				int a = edge.Dir0 - 1;
				int b = edge.Dir1 - 1;
				if (a > b) (a, b) = (b, a);
				if (a < 0 || b <= a) return;
				int code = 1 + a * (11 - a) / 2 + (b - a - 1);
				packed |= (uint)code << shift;
				shift += 4;
			}
		}
	}

	/* ================================================================
	 * ink geometry
	 * ================================================================
	 * Four vertices per run, expanded to a screen-space-width quad in the vertex
	 * shader. VERTEX carries the quad corner rather than a position; everything
	 * the shader actually needs rides in the 16 spare custom floats.
	 *
	 * Surface 0 is the pale ink and surface 1 the dark, so the material's
	 * render_priority can make dark win at a junction. */
	private static ArrayMesh BuildInkMesh(out bool[] isLight)
	{
		var mesh = new ArrayMesh();
		var flags = new List<bool>(2);

		for (int pass = 0; pass < 2; pass++)
		{
			bool wantLight = pass == 0;
			int n = 0;
			foreach (var r in _runs) if (r.Light == wantLight) n++;
			if (n == 0) continue;

			var verts = new Vector3[n * 4];
			var c0 = new float[n * 16];
			var c1 = new float[n * 16];
			var c2 = new float[n * 16];
			var c3 = new float[n * 16];
			var idx = new int[n * 6];

			int v = 0, q = 0;
			foreach (var r in _runs)
			{
				if (r.Light != wantLight) continue;
				var na = new Vector3(Normals[r.Dir0, 0], Normals[r.Dir0, 1], Normals[r.Dir0, 2]);
				var nb = new Vector3(Normals[r.Dir1, 0], Normals[r.Dir1, 1], Normals[r.Dir1, 2]);

				for (int k = 0; k < 4; k++)
				{
					// x: 0 at the start endpoint, 1 at the end. y: which side.
					float along = (k == 1 || k == 2) ? 1f : 0f;
					float side = (k >= 2) ? 1f : -1f;
					verts[v + k] = new Vector3(along, side, 0f);

					int o = (v + k) * 4;
					c0[o + 0] = r.Start.X; c0[o + 1] = r.Start.Y; c0[o + 2] = r.Start.Z;
					c0[o + 3] = r.PaleEdgesAtStart;
					c1[o + 0] = r.End.X; c1[o + 1] = r.End.Y; c1[o + 2] = r.End.Z;
					c1[o + 3] = r.PaleEdgesAtEnd;
					c2[o + 0] = na.X; c2[o + 1] = na.Y; c2[o + 2] = na.Z;
					c2[o + 3] = r.Light ? 1f : 0f;
					c3[o + 0] = nb.X; c3[o + 1] = nb.Y; c3[o + 2] = nb.Z;
					c3[o + 3] = 0f;
				}
				idx[q + 0] = v + 0; idx[q + 1] = v + 1; idx[q + 2] = v + 2;
				idx[q + 3] = v + 0; idx[q + 4] = v + 2; idx[q + 5] = v + 3;
				v += 4; q += 6;
			}

			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = verts;
			arrays[(int)Mesh.ArrayType.Custom0] = c0;
			arrays[(int)Mesh.ArrayType.Custom1] = c1;
			arrays[(int)Mesh.ArrayType.Custom2] = c2;
			arrays[(int)Mesh.ArrayType.Custom3] = c3;
			arrays[(int)Mesh.ArrayType.Index] = idx;

			// Four channels of RGBA_FLOAT. The shifts are 13/16/19/22 — three
			// bits per custom channel, straight out of Mesh.ArrayFormat.
			ulong fmt = ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 13)
			          | ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 16)
			          | ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 19)
			          | ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 22);

			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
				(Mesh.ArrayFormat)fmt);
			flags.Add(wantLight);
		}

		isLight = flags.ToArray();
		return mesh.GetSurfaceCount() > 0 ? mesh : null;
	}
}
