using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Streams chunk meshes, ink and collision around the player.
///
/// A chunk is never visible without its body: loading must never visibly
/// remove ground or leave the player standing on air. Work is budgeted per
/// frame rather than threaded — at C# speed a chunk meshes in a couple of
/// milliseconds, and keeping mesh and resource creation on the main thread
/// removes a whole class of engine-threading hazard for no measurable cost.
/// </summary>
public partial class ChunkStreamer : Node3D
{
	private sealed class Chunk
	{
		public Node3D Root;
		public MeshInstance3D Surface;
		public MeshInstance3D Ink;
		public StaticBody3D Body;
	}

	private readonly Dictionary<long, Chunk> _loaded = new();
	private readonly List<long> _pending = new();
	private readonly HashSet<long> _pendingSet = new();

	private VoxelGrid _grid;
	private Terrain _terrain;
	private ShaderMaterial _voxelMat;
	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private ShaderMaterial _detailMat;
	private ShaderMaterial _waterDetailMat;

	public int LoadRadius = 8;
	public int UnloadPadding = 3;
	/// <summary>Milliseconds of meshing allowed per frame once the world is up.</summary>
	public double FrameBudgetMs = 5.0;

	public int LoadedCount => _loaded.Count;
	public int PendingCount => _pending.Count;

	public void Setup(Terrain terrain, ShaderMaterial voxel, ShaderMaterial inkLight,
		ShaderMaterial inkDark, ShaderMaterial detail, ShaderMaterial waterDetail)
	{
		_terrain = terrain;
		_grid = terrain.Grid;
		_voxelMat = voxel;
		_inkLight = inkLight;
		_inkDark = inkDark;
		_detailMat = detail;
		_waterDetailMat = waterDetail;
	}

	private static long Key(int ci, int ck) => ((long)(ci + 4096) << 20) | (uint)(ck + 4096);

	public void UpdateAround(Vector3 pos, bool prime = false)
	{
		int cs = ChunkMesher.ChunkSize;
		int pi = Mathf.FloorToInt(pos.X / cs);
		int pk = Mathf.FloorToInt(pos.Z / cs);
		int maxChunk = _grid.Size / cs;

		int radius = LoadRadius;

		// Nearest first, so the ground under the player exists before the
		// horizon does.
		_pending.Clear();
		_pendingSet.Clear();
		var wanted = new List<(long key, int ci, int ck, int d2)>();
		for (int dk = -radius; dk <= radius; dk++)
		for (int di = -radius; di <= radius; di++)
		{
			int ci = pi + di, ck = pk + dk;
			if (ci < 0 || ck < 0 || ci >= maxChunk || ck >= maxChunk) continue;
			if (di * di + dk * dk > radius * radius) continue;
			long k = Key(ci, ck);
			if (_loaded.ContainsKey(k)) continue;
			wanted.Add((k, ci, ck, di * di + dk * dk));
		}
		wanted.Sort((a, b) => a.d2.CompareTo(b.d2));

		// Priming happens before the first frame is shown, so it is allowed to
		// take as long as it needs: an empty world is worse than a slow boot.
		var sw = Time.GetTicksUsec();
		double budget = prime ? 20_000_000.0 : FrameBudgetMs * 1000.0;

		int built = 0;
		foreach (var w in wanted)
		{
			BuildChunk(w.ci, w.ck);
			built++;
			if (Time.GetTicksUsec() - sw > budget) break;
		}
		if (prime && built > 0)
		{
			GD.Print($"[stream] primed {built} chunks in {(Time.GetTicksUsec() - sw) / 1000}ms");
		}

		// Unload with a margin, so walking back and forth across a boundary
		// does not thrash.
		int drop = radius + UnloadPadding;
		var stale = new List<long>();
		foreach (var kv in _loaded)
		{
			int ci = (int)((kv.Key >> 20) - 4096);
			int ck = (int)(kv.Key & 0xFFFFF) - 4096;
			int di = ci - pi, dk = ck - pk;
			if (di * di + dk * dk > drop * drop) stale.Add(kv.Key);
		}
		foreach (long k in stale)
		{
			_loaded[k].Root.QueueFree();
			_loaded.Remove(k);
		}
	}

	private void BuildChunk(int ci, int ck)
	{
		var data = ChunkMesher.Build(_grid, ci, ck);
		long k = Key(ci, ck);
		if (data.Empty)
		{
			// Still record it, or an empty chunk is re-meshed every frame.
			var empty = new Node3D { Name = $"c{ci}_{ck}" };
			AddChild(empty);
			_loaded[k] = new Chunk { Root = empty };
			return;
		}

		var root = new Node3D { Name = $"c{ci}_{ck}" };
		AddChild(root);

		var surf = new MeshInstance3D
		{
			Mesh = data.Surface,
			MaterialOverride = _voxelMat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		root.AddChild(surf);

		var chunk = new Chunk { Root = root, Surface = surf };

		if (data.Ink != null)
		{
			var ink = new MeshInstance3D
			{
				Mesh = data.Ink,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			for (int s = 0; s < data.Ink.GetSurfaceCount(); s++)
				ink.SetSurfaceOverrideMaterial(s, data.InkSurfaceIsLight[s] ? _inkLight : _inkDark);

			// The ink mesh's vertices carry quad corners, not positions, so the
			// engine cannot derive a bounding box from them.
			int cs = ChunkMesher.ChunkSize;
			// Include the screen-space skirt and edges owned on the positive
			// chunk plane. An exact surface AABB can cull a complete ink object
			// while its widened stroke is still inside the frame.
			ink.CustomAabb = new Aabb(
				new Vector3(ci * cs - 1, -1, ck * cs - 1),
				new Vector3(cs + 2, _grid.Height + 2, cs + 2));
			root.AddChild(ink);
			chunk.Ink = ink;
		}

		// Ground detail. Tufts, flowers and pebbles are what make a shelf read as
		// made rather than as fill, and one merged mesh per chunk makes them the
		// cheapest life in the world.
		var detail = GroundDetail.Build(_terrain, ci, ck);
		if (detail != null)
		{
			root.AddChild(new MeshInstance3D
			{
				Mesh = detail,
				MaterialOverride = _detailMat,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}

		var floating = GroundDetail.BuildWater(_terrain, ci, ck);
		if (floating != null)
		{
			root.AddChild(new MeshInstance3D
			{
				Mesh = floating,
				MaterialOverride = _waterDetailMat,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				// Same layer as the water: these lie ON the surface, so the
				// reflection pass has nothing to gain from them and would only
				// reflect a petal onto itself.
				Layers = Render.PlanarReflection.WaterLayer,
			});
		}

		if (data.CollisionFaces is { Length: > 0 })
		{
			var shape = new ConcavePolygonShape3D();
			shape.SetFaces(data.CollisionFaces);
			var body = new StaticBody3D();
			body.AddChild(new CollisionShape3D { Shape = shape });
			root.AddChild(body);
			chunk.Body = body;
		}

		_loaded[k] = chunk;
	}
}
