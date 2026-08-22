using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;
using Petalfell.World;

namespace Petalfell.Player;

/// <summary>
/// Click-to-move.
///
/// The route finder does not move anything itself. It produces waypoints, and
/// the Controller turns those into the same world-space movement wish the
/// keyboard produces — so acceleration, collision, stepping, swimming and
/// animation all stay in one place and behave identically either way.
///
/// The same surface serves the player, the dog, and later townspeople and
/// wildlife: different characters may have different traversal abilities and
/// destinations while still reading the same terrain.
/// </summary>
public sealed class Navigation
{
	// Route planning and physical movement must agree on a normal terrace.
	public const float MaxStep = Terrain.Step + 0.35f;
	public const float MaxDrop = Terrain.Step + 0.35f;
	private const int MaxExpansions = 260000;

	private readonly Terrain _t;
	private readonly int _S;

	public Navigation(Terrain terrain)
	{
		_t = terrain;
		_S = terrain.Size;
	}

	public bool Walkable(int x, int z)
	{
		if (x < 1 || z < 1 || x >= _S - 1 || z >= _S - 1) return false;
		return true;   // water is swimmable; only the map edge is truly closed
	}

	public float GroundY(int x, int z)
	{
		if (x < 0 || z < 0 || x >= _S || z >= _S) return Terrain.Sea;
		return _t.Level[z * _S + x];
	}

	public bool IsWater(int x, int z) => GroundY(x, z) <= Terrain.Sea;

	/// <summary>Nearest reachable cell to a requested destination, searched outward.</summary>
	public (int x, int z) Snap(int x, int z)
	{
		for (int r = 0; r < 24; r++)
		for (int dz = -r; dz <= r; dz++)
		for (int dx = -r; dx <= r; dx++)
		{
			if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
			int nx = x + dx, nz = z + dz;
			if (Walkable(nx, nz)) return (nx, nz);
		}
		return (x, z);
	}

	private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
	private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
	private static readonly float[] Dc =
	{
		1f, 1f, 1f, 1f, 1.41421f, 1.41421f, 1.41421f, 1.41421f,
	};

	/// <summary>
	/// A*, budgeted. A destination that cannot be reached must fail quickly and
	/// visibly rather than freezing the frame while the whole map is expanded.
	/// </summary>
	public List<Vector3> FindPath(Vector3 fromWorld, Vector3 toWorld)
	{
		int sx = Mathf.FloorToInt(fromWorld.X), sz = Mathf.FloorToInt(fromWorld.Z);
		int gx = Mathf.FloorToInt(toWorld.X), gz = Mathf.FloorToInt(toWorld.Z);
		if (!Walkable(sx, sz)) (sx, sz) = Snap(sx, sz);
		if (!Walkable(gx, gz)) (gx, gz) = Snap(gx, gz);
		if (sx == gx && sz == gz) return null;

		var open = new PriorityQueue<int, float>();
		var gScore = new Dictionary<int, float>(4096);
		var came = new Dictionary<int, int>(4096);

		int start = sz * _S + sx, goal = gz * _S + gx;
		gScore[start] = 0f;
		open.Enqueue(start, 0f);

		int expansions = 0;
		bool found = false;

		while (open.Count > 0 && expansions < MaxExpansions)
		{
			int cur = open.Dequeue();
			if (cur == goal) { found = true; break; }
			expansions++;

			int cx = cur % _S, cz = cur / _S;
			float cy = GroundY(cx, cz);
			float g = gScore[cur];

			for (int d = 0; d < 8; d++)
			{
				int nx = cx + Dx[d], nz = cz + Dz[d];
				if (!Walkable(nx, nz)) continue;

				float ny = GroundY(nx, nz);
				float rise = ny - cy;
				if (rise > MaxStep || -rise > MaxDrop) continue;

				// A diagonal may not squeeze between two blocked corners.
				if (d >= 4)
				{
					float a = GroundY(cx + Dx[d], cz);
					float b = GroundY(cx, cz + Dz[d]);
					if (a - cy > MaxStep || b - cy > MaxStep) continue;
				}

				float cost = Dc[d];
				// Climbing costs more than walking, and swimming more than
				// either — so a route prefers the road and the bridge, and
				// takes to the water only when that is genuinely shorter.
				cost += Math.Abs(rise) * 0.35f;
				if (IsWater(nx, nz)) cost *= 2.4f;

				int nk = nz * _S + nx;
				float ng = g + cost;
				if (gScore.TryGetValue(nk, out float prev) && prev <= ng) continue;
				gScore[nk] = ng;
				came[nk] = cur;
				float h = Mathf.Sqrt((nx - gx) * (nx - gx) + (nz - gz) * (nz - gz));
				open.Enqueue(nk, ng + h);
			}
		}

		if (!found) return null;

		var cells = new List<int>();
		int at = goal;
		while (at != start)
		{
			cells.Add(at);
			if (!came.TryGetValue(at, out at)) return null;
		}
		cells.Reverse();

		// Thin the route: the controller steers toward waypoints, so a point
		// per block would make it stutter along a straight run.
		var route = new List<Vector3>();
		int lastDx = 0, lastDz = 0, prevCell = start;
		foreach (int c in cells)
		{
			int px = prevCell % _S, pz = prevCell / _S;
			int cx2 = c % _S, cz2 = c / _S;
			int ddx = cx2 - px, ddz = cz2 - pz;
			if (ddx != lastDx || ddz != lastDz)
			{
				route.Add(new Vector3(px + 0.5f, GroundY(px, pz), pz + 0.5f));
				lastDx = ddx; lastDz = ddz;
			}
			prevCell = c;
		}
		int fx = goal % _S, fz2 = goal / _S;
		route.Add(new Vector3(fx + 0.5f, GroundY(fx, fz2), fz2 + 0.5f));
		return route;
	}
}

/// <summary>
/// Click feedback: a brief white hemispherical pulse at the selected point. It
/// expands and disappears quickly, confirming the input without becoming a
/// persistent marker or an intrusive effect.
/// </summary>
public partial class ClickPulse : Node3D
{
	private const float Duration = 0.52f;
	private float _t = Duration;
	private MeshInstance3D _mesh;
	private ShaderMaterial _mat;

	public override void _Ready()
	{
		var sphere = new SphereMesh
		{
			Radius = 1f, Height = 1f, IsHemisphere = true,
			RadialSegments = 24, Rings = 6,
		};
		_mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pulse.gdshader") };
		_mesh = new MeshInstance3D
		{
			Mesh = sphere,
			MaterialOverride = _mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		AddChild(_mesh);
	}

	public void Fire(Vector3 at)
	{
		GlobalPosition = at + new Vector3(0, 0.06f, 0);
		_t = 0f;
		_mesh.Visible = true;
	}

	public override void _Process(double delta)
	{
		if (_t >= Duration) return;
		_t += (float)delta;
		float k = Mathf.Clamp(_t / Duration, 0f, 1f);
		float r = 0.5f + k * 2.6f;
		_mesh.Scale = new Vector3(r, r * 0.55f, r);
		_mat.SetShaderParameter("fade", 1f - k);
		if (_t >= Duration) _mesh.Visible = false;
	}
}
