using System;
using System.Collections.Generic;
using Godot;

namespace Petalfell.Core;

/// <summary>
/// Ink for things that are not voxels.
///
/// The chunk mesher gets its edge graph for free because it knows every face's
/// colour and normal. A character part is an animated box, so its twelve edges
/// are built here in the same four-channel run format and fed to the same
/// material — which is what keeps the traveller inked by exactly the same
/// rules as the terrain they are standing on.
///
/// Characters take the §15.3 exception: the exterior silhouette of each part is
/// kept, and no internal seams between adjacent parts are ever emitted, so a
/// figure does not turn into a cluster of lines at the default camera distance.
/// </summary>
public static class InkBuilder
{
	private static readonly int[,] Normals =
	{
		{ 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 },
	};

	/// <summary>
	/// The twelve edges of a box centred on the origin. Each edge is shared by
	/// exactly two faces, so the light/dark rule applies unchanged: a pale part
	/// gets a pale outline, a dark one gets the plum ink.
	/// </summary>
	public static ArrayMesh Box(float w, float h, float d, bool light)
	{
		float hx = w * 0.5f, hy = h * 0.5f, hz = d * 0.5f;
		var runs = new List<(Vector3 a, Vector3 b, int f0, int f1)>(12);

		// Along X: four edges, each shared by a Y face and a Z face.
		for (int sy = -1; sy <= 1; sy += 2)
		for (int sz = -1; sz <= 1; sz += 2)
			runs.Add((new Vector3(-hx, sy * hy, sz * hz), new Vector3(hx, sy * hy, sz * hz),
				sy > 0 ? 2 : 3, sz > 0 ? 4 : 5));

		// Along Y: shared by an X face and a Z face.
		for (int sx = -1; sx <= 1; sx += 2)
		for (int sz = -1; sz <= 1; sz += 2)
			runs.Add((new Vector3(sx * hx, -hy, sz * hz), new Vector3(sx * hx, hy, sz * hz),
				sx > 0 ? 0 : 1, sz > 0 ? 4 : 5));

		// Along Z: shared by an X face and a Y face.
		for (int sx = -1; sx <= 1; sx += 2)
		for (int sy = -1; sy <= 1; sy += 2)
			runs.Add((new Vector3(sx * hx, sy * hy, -hz), new Vector3(sx * hx, sy * hy, hz),
				sx > 0 ? 0 : 1, sy > 0 ? 2 : 3));

		int n = runs.Count;
		var endpointTopology = new Dictionary<Vector3, uint>(8);
		if (light)
		{
			foreach (var run in runs)
			{
				int a = Math.Min(run.f0, run.f1);
				int b = Math.Max(run.f0, run.f1);
				uint code = (uint)(1 + a * (11 - a) / 2 + (b - a - 1));
				AddEndpoint(run.a, code);
				AddEndpoint(run.b, code);
			}
		}

		void AddEndpoint(Vector3 point, uint code)
		{
			endpointTopology.TryGetValue(point, out uint packed);
			int shift = 0;
			while (shift < 24 && ((packed >> shift) & 0xfu) != 0) shift += 4;
			if (shift < 24) endpointTopology[point] = packed | (code << shift);
		}

		var verts = new Vector3[n * 4];
		var c0 = new float[n * 16];
		var c1 = new float[n * 16];
		var c2 = new float[n * 16];
		var c3 = new float[n * 16];
		var idx = new int[n * 6];

		int v = 0, q = 0;
		foreach (var r in runs)
		{
			var na = new Vector3(Normals[r.f0, 0], Normals[r.f0, 1], Normals[r.f0, 2]);
			var nb = new Vector3(Normals[r.f1, 0], Normals[r.f1, 1], Normals[r.f1, 2]);
			for (int k = 0; k < 4; k++)
			{
				float along = (k == 1 || k == 2) ? 1f : 0f;
				float side = (k >= 2) ? 1f : -1f;
				verts[v + k] = new Vector3(along, side, 0f);
				int o = (v + k) * 4;
				c0[o + 0] = r.a.X; c0[o + 1] = r.a.Y; c0[o + 2] = r.a.Z;
				c0[o + 3] = endpointTopology.GetValueOrDefault(r.a);
				c1[o + 0] = r.b.X; c1[o + 1] = r.b.Y; c1[o + 2] = r.b.Z;
				c1[o + 3] = endpointTopology.GetValueOrDefault(r.b);
				c2[o + 0] = na.X; c2[o + 1] = na.Y; c2[o + 2] = na.Z; c2[o + 3] = light ? 1f : 0f;
				c3[o + 0] = nb.X; c3[o + 1] = nb.Y; c3[o + 2] = nb.Z; c3[o + 3] = 0f;
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

		ulong fmt = ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 13)
		          | ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 16)
		          | ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 19)
		          | ((ulong)Mesh.ArrayCustomFormat.RgbaFloat << 22);

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
			(Mesh.ArrayFormat)fmt);
		return mesh;
	}
}
