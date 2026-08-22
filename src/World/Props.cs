using System;
using System.Collections.Generic;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Built things: bridges, their approach paths and lanterns.
///
/// This stage runs after terrain and before vegetation. It is the first layer
/// that says somebody lives in the landscape, and it also supplies the opening
/// composition: as in the Three.js source, the player starts on the first
/// successful crossing rather than on an arbitrary patch near map centre.
/// </summary>
public sealed class BuiltProps
{
	public sealed class Bridge
	{
		public int X, Z, DeckY;
		public bool AlongX;
		public int NearX, NearZ, FarX, FarZ;
	}

	public readonly List<Bridge> Bridges = new();
	public readonly List<List<(int x, int z)>> Paths = new();
	public readonly List<(int x, int y, int z)> Lanterns = new();

	public static BuiltProps Build(Terrain terrain, int seed)
	{
		var result = new BuiltProps();
		var rng = new Rng(seed ^ 0xB17D6E);
		var candidates = new List<RiverNode>();
		foreach (var node in terrain.RiverPath)
		{
			int x = Rng.ClampI((int)MathF.Floor(node.X + 0.5f), 0, terrain.Size - 1);
			int z = Rng.ClampI((int)MathF.Floor(node.Z + 0.5f), 0, terrain.Size - 1);
			if (!node.Ford && terrain.Level[z * terrain.Size + x] <= Terrain.Sea)
				candidates.Add(node);
		}
		if (candidates.Count == 0)
		{
			foreach (var node in terrain.RiverPath)
			{
				int x = Rng.ClampI((int)MathF.Floor(node.X + 0.5f), 0, terrain.Size - 1);
				int z = Rng.ClampI((int)MathF.Floor(node.Z + 0.5f), 0, terrain.Size - 1);
				if (terrain.Level[z * terrain.Size + x] <= Terrain.Sea) candidates.Add(node);
			}
		}
		candidates.Sort((a, b) => a.Width.CompareTo(b.Width));

		float area = terrain.Size / 256f;
		int maxBridges = Math.Max(3, (int)MathF.Floor(3f * area * area + 0.5f));
		foreach (var node in candidates)
		{
			if (result.Bridges.Count >= maxBridges) break;
			bool close = false;
			foreach (var old in result.Bridges)
			{
				float dx = old.X - node.X, dz = old.Z - node.Z;
				if (MathF.Sqrt(dx * dx + dz * dz) < 41f) { close = true; break; }
			}
			if (close) continue;
			var bridge = BuildBridge(terrain, node);
			if (bridge != null) result.Bridges.Add(bridge);
		}

		foreach (var bridge in result.Bridges)
		{
			int dx = Math.Sign(bridge.NearX - bridge.FarX);
			int dz = Math.Sign(bridge.NearZ - bridge.FarZ);
			result.Paths.Add(CarvePath(terrain,
				bridge.NearX + dx * 7, bridge.NearZ + dz * 7,
				bridge.NearX, bridge.NearZ, 1));
			result.Paths.Add(CarvePath(terrain,
				bridge.FarX, bridge.FarZ,
				bridge.FarX - dx * 8, bridge.FarZ - dz * 8, 1));
		}

		foreach (var path in result.Paths)
		for (int i = 2; i < path.Count; i += 9)
		{
			var (x, z) = path[i];
			int top = terrain.Level[z * terrain.Size + x];
			if (!terrain.Grid.SolidAt(x + 2, top - 1, z)) continue;
			PlaceLantern(terrain.Grid, x + 2, top, z);
			result.Lanterns.Add((x + 2, top, z));
		}

		return result;
	}

	private static Bridge BuildBridge(Terrain terrain, in RiverNode node)
	{
		var grid = terrain.Grid;
		int size = terrain.Size;
		bool alongX = Math.Abs(node.Nx) >= Math.Abs(node.Nz);
		int ax = alongX ? 1 : 0, az = alongX ? 0 : 1;
		int px = az, pz = ax;
		int cx = (int)MathF.Floor(node.X + 0.5f), cz = (int)MathF.Floor(node.Z + 0.5f);

		int Index(int i, int j = 0)
		{
			int x = Rng.ClampI(cx + ax * i + px * j, 0, size - 1);
			int z = Rng.ClampI(cz + az * i + pz * j, 0, size - 1);
			return z * size + x;
		}
		// Level retains the source heightfield's numeric values even though the
		// Godot voxel store represents its top block one index lower. Bank finding
		// is a heightfield test, so use the source waterline verbatim here.
		bool Bank(int i) => terrain.Level[Index(i)] > Math.Max(terrain.Level[Index(0)] + 1, Terrain.Sea);

		int left = 0, right = 0;
		while (left > -40 && !Bank(left)) left--;
		while (right < 40 && !Bank(right)) right++;
		if (right - left < 6 || right - left > 46) return null;

		// Level is the first empty voxel, whereas the Three.js heightfield stores
		// the top solid voxel. Placing the deck at Level is the same source height.
		int deckY = Math.Max(terrain.Level[Index(left)], terrain.Level[Index(right)]);
		const int halfW = 1;

		void Put(int i, int j, int y, byte id)
		{
			int x = cx + ax * i + px * j, z = cz + az * i + pz * j;
			SetRaised(grid, x, y, z, id);
		}

		for (int i = left; i <= right; i++)
		{
			for (int j = -halfW; j <= halfW; j++) Put(i, j, deckY, Palette.PLANK);
			foreach (int side in new[] { -halfW - 1, halfW + 1 })
			{
				Put(i, side, deckY, Palette.BEAM);
				if (i % 3 == 0) Put(i, side, deckY + 1, Palette.BEAM);
				Put(i, side, deckY + 2, Palette.PLANK_PALE);
			}
			if ((i - left) % 7 == 3)
			{
				int x = cx + ax * i, z = cz + az * i;
				for (int y = deckY - 1; y > 0; y--)
				{
					if (grid.SolidAt(x, y, z)) break;
					Put(i, 0, y, Palette.BEAM);
				}
			}
		}

		foreach (int i in new[] { left + 1, right - 1 })
		{
			Put(i, -halfW - 1, deckY + 3, Palette.BEAM);
			Put(i, -halfW - 1, deckY + 4, Palette.LANTERN);
		}

		return new Bridge
		{
			X = cx, Z = cz, DeckY = deckY, AlongX = alongX,
			NearX = cx + ax * left, NearZ = cz + az * left,
			FarX = cx + ax * right, FarZ = cz + az * right,
		};
	}

	private static List<(int x, int z)> CarvePath(Terrain terrain,
		int ax, int az, int bx, int bz, int width)
	{
		int size = terrain.Size;
		int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) * 1.4f));
		var path = new List<(int, int)>(steps + 1);
		for (int i = 0; i <= steps; i++)
		{
			float t = i / (float)steps;
			float wobble = MathF.Sin(t * MathF.PI * 3.1f) * 3.5f * MathF.Sin(t * MathF.PI);
			float nx = bx - ax, nz = bz - az;
			float len = MathF.Sqrt(nx * nx + nz * nz);
			if (len < 0.001f) len = 1f;
			float sx = -nz / len, sz = nx / len;
			int x = (int)MathF.Floor(ax + (bx - ax) * t + sx * wobble + 0.5f);
			int z = (int)MathF.Floor(az + (bz - az) * t + sz * wobble + 0.5f);
			for (int dz = -width; dz <= width; dz++)
			for (int dx = -width; dx <= width; dx++)
			{
				if (dx * dx + dz * dz > width * width + 0.5f) continue;
				int xx = Rng.ClampI(x + dx, 0, size - 1);
				int zz = Rng.ClampI(z + dz, 0, size - 1);
				int top = terrain.Level[zz * size + xx];
				if (!terrain.Grid.SolidAt(xx, top - 1, zz)) continue;
				terrain.Grid.Set(xx, top, zz, Palette.AIR);
				terrain.Grid.Set(xx, top - 1, zz, Palette.PATH);
			}
			path.Add((Rng.ClampI(x, 0, size - 1), Rng.ClampI(z, 0, size - 1)));
		}
		return path;
	}

	private static void PlaceLantern(VoxelGrid grid, int x, int y, int z)
	{
		SetRaised(grid, x, y, z, Palette.BEAM);
		SetRaised(grid, x, y + 1, z, Palette.BEAM);
		SetRaised(grid, x, y + 2, z, Palette.BEAM);
		SetRaised(grid, x, y + 3, z, Palette.LANTERN);
		SetRaised(grid, x, y + 4, z, Palette.BEAM);
	}

	private static void SetRaised(VoxelGrid grid, int x, int y, int z, byte id)
	{
		if (!grid.InBounds(x, y, z)) return;
		grid.Set(x, y, z, id);
		if (!Palette.IsSolid(id)) return;
		int col = z * grid.Size + x;
		if (y + 1 > grid.Heights[col]) grid.Heights[col] = (short)(y + 1);
	}
}
