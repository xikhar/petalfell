using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Deterministic L3 blockout compiler for the authored domain composition.
/// It realises levels, routes and silhouette masses; it does not invent their
/// positions, decay state or connections. Fine masonry, coherent damage and
/// reclamation remain later production passes over this exact composition.
/// </summary>
public static class DomainPlanBlockout
{
	public static DomainBlockoutStatistics Compile(AtlasSectorWindow window,
		CanonicalWorldDefinition world, CanonicalDomain domain)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (world == null) throw new ArgumentNullException(nameof(world));
		if (domain?.Plan == null) throw new ArgumentNullException(nameof(domain));
		var build = new Builder(window, world, domain);
		return build.Compile();
	}

	private sealed class Builder
	{
		private readonly AtlasSectorWindow _window;
		private readonly VoxelGrid _grid;
		private readonly AtlasSectorData _data;
		private readonly CanonicalWorldDefinition _world;
		private readonly CanonicalDomain _domain;
		private readonly DomainPlanDefinition _plan;
		private readonly Dictionary<string, PlanPlatform> _platforms;
		private readonly Dictionary<string, PlanRouteSocket> _sockets;
		private readonly Noise2D _platformEdge;
		private readonly HashSet<int> _routeCells = new();
		private int _platformCells;
		private int _terrainCapCells;
		private int _pavedCapCells;
		private int _reclaimedCapCells;
		private int _cutoutCells;
		private int _stairCells;
		private int _placedBlocks;

		public Builder(AtlasSectorWindow window, CanonicalWorldDefinition world,
			CanonicalDomain domain)
		{
			_window = window;
			_grid = window.Grid;
			_data = window.Data;
			_world = world;
			_domain = domain;
			_plan = domain.Plan;
			_platforms = _plan.Platforms.ToDictionary(p => p.Id, StringComparer.Ordinal);
			_sockets = _plan.RouteSockets.ToDictionary(s => s.Id, StringComparer.Ordinal);
			_platformEdge = new Noise2D(Rng.StableHash($"domain:{domain.Id}:platform-edge"));
		}

		public DomainBlockoutStatistics Compile()
		{
			// Stairs replace part of the already-raised platforms. Both passes must
			// therefore happen before paths and masonry create sparse edits.
			foreach (PlanPlatform platform in _plan.Platforms) BuildPlatform(platform);
			foreach (PlanPlatform platform in _plan.Platforms) BuildPlatformCutouts(platform);
			foreach (PlanStair stair in _plan.Stairs) BuildStair(stair);
			BuildPlatformDressing();
			BuildAuthoredRoutes();
			foreach (PlanLandmark landmark in _plan.Landmarks) BuildLandmark(landmark);
			foreach (PlanWall wall in _plan.Walls) BuildWall(wall);
			return new DomainBlockoutStatistics(_plan.Platforms.Count, _platformCells,
				_terrainCapCells, _pavedCapCells, _reclaimedCapCells,
				_plan.Platforms.Sum(p => p.Cutouts.Count), _cutoutCells,
				_plan.Stairs.Count, _stairCells, _routeCells.Count, _plan.Walls.Count,
				_plan.Landmarks.Count, _placedBlocks);
		}

		private void BuildPlatform(PlanPlatform platform)
		{
			List<Vector2> polygon = platform.Polygon.Select(LocalPoint).ToList();
			List<List<Vector2>> cutouts = platform.Cutouts
				.Select(c => c.Polygon.Select(LocalPoint).ToList()).ToList();
			int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
			int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
			int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
			int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
			byte masonry = ResolveMasonry(platform.MaterialId);
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
			{
				var cell = new Vector2(x + .5f, z + .5f);
				if (!InsidePolygon(cell, polygon) || cutouts.Any(c => InsidePolygon(cell, c)) ||
				    FrayedAway(platform, cell, polygon)) continue;
				int index = z * _data.Width + x;
				int target = Math.Max(_data.Height[index], platform.SurfaceY);
				if (target == _data.Height[index] && _data.Height[index] > platform.SurfaceY) continue;
				byte naturalCap = _grid.Cap[index];
				byte cap = PlatformCap(platform, x, z, naturalCap);
				if (cap == naturalCap) _terrainCapCells++;
				else if (cap is Palette.MOSS or Palette.MOSS_STONE) _reclaimedCapCells++;
				else _pavedCapCells++;
				_grid.Describe(x, z, target, cap, masonry, masonry);
				_platformCells++;
			}
		}

		private bool FrayedAway(PlanPlatform platform, Vector2 cell, IReadOnlyList<Vector2> polygon)
		{
			if (platform.EdgeTreatment is not (PlanEdgeTreatment.Ragged or PlanEdgeTreatment.Submerged))
				return false;
			float edgeDistance = DistanceToEdges(cell, polygon);
			float globalX = cell.X + _data.OriginX;
			float globalZ = cell.Y + _data.OriginZ;
			float field = _platformEdge.Fbm(globalX / 24f, globalZ / 24f, 3);
			float retreat = platform.EdgeTreatment == PlanEdgeTreatment.Submerged
				? 2.5f + Math.Max(0f, field) * 8f
				: 1.5f + Math.Max(0f, field) * 6f;
			if (edgeDistance < retreat) return true;
			// A trace survives as broad archaeological patches, not a complete
			// slab. This is a wavelength field inside an authored envelope.
			return platform.Role == PlanPlatformRole.Trace &&
			       _platformEdge.Fbm(globalX / 38f + 19f, globalZ / 38f - 7f, 3) > .34f;
		}

		private void BuildPlatformCutouts(PlanPlatform platform)
		{
			foreach (PlanPlatformCutout cutout in platform.Cutouts)
			{
				if (cutout.Role != PlanCutoutRole.Collapsed) continue;
				List<Vector2> polygon = cutout.Polygon.Select(LocalPoint).ToList();
				int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
				int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
				int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
				int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
				int collapsedTop = platform.SurfaceY - cutout.Depth;
				for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++)
				{
					if (!InsidePolygon(new Vector2(x + .5f, z + .5f), polygon)) continue;
					int index = z * _data.Width + x;
					int target = Math.Min(_data.Height[index], collapsedTop);
					if (target <= _data.SeaLevel) target = _data.SeaLevel + 1;
					float age = _platformEdge.Fbm((x + _data.OriginX) / 30f + 7f,
						(z + _data.OriginZ) / 30f - 13f, 3);
					byte cap = age > .12f ? Palette.MOSS_STONE : Palette.STONE_WARM;
					_grid.RedescribeUnedited(x, z, target, cap,
						Palette.STONE_PALE, Palette.STONE_PALE);
					_cutoutCells++;
				}
			}
		}

		private byte PlatformCap(PlanPlatform platform, int localX, int localZ, byte naturalCap)
		{
			int globalX = localX + _data.OriginX;
			int globalZ = localZ + _data.OriginZ;
			if (platform.Role == PlanPlatformRole.Causeway)
				return PositiveModulo(globalZ, 10) == 0 ? Palette.STONE_WARM : Palette.PAVING;
			PlanPoint planLocal = ToPlanLocal(globalX, globalZ);
			float pavingField = _platformEdge.Fbm(globalX / 34f - 11f, globalZ / 34f + 23f, 3);
			bool processionalAxis = Math.Abs(planLocal.X) <= 25;
			bool keepTerrainCap = platform.Role switch
			{
				PlanPlatformRole.Court => !processionalAxis && pavingField > -.12f,
				PlanPlatformRole.Terrace => pavingField > -.18f,
				PlanPlatformRole.Trace => pavingField > -.42f,
				_ => false,
			};
			if (keepTerrainCap)
			{
				// Reclamation belongs to broad surviving-ground patches. Sampling a
				// 72-block field preserves the authored court/axis while preventing the
				// salt-and-pepper moss that failed here three times before.
				float reclaim = _platformEdge.Fbm(globalX / 72f + 109f, globalZ / 72f - 103f, 3);
				float threshold = .58f - platform.Reclamation * .92f;
				if (platform.Reclamation > 0f && reclaim > threshold)
					return Palette.HasStoneSubstrate(naturalCap) ||
					       naturalCap is Palette.STONE or Palette.STONE_PALE or Palette.STONE_WARM
						? Palette.MOSS_STONE
						: Palette.MOSS;
				return naturalCap;
			}
			// Large staggered flagstone courses keep a district-sized plane from
			// greedily merging into one blank quad. The course grid belongs only to
			// made ground; natural terrain remains explicitly forbidden to use it.
			int row = FloorDiv(globalZ, 9);
			int staggeredX = globalX + ((row & 1) == 0 ? 0 : 6);
			bool joint = PositiveModulo(staggeredX, 12) == 0 || PositiveModulo(globalZ, 9) == 0;
			return joint ? Palette.STONE_WARM : Palette.PAVING;
		}

		private PlanPoint ToPlanLocal(int globalX, int globalZ)
		{
			float radians = _plan.AxisDegrees * MathF.PI / 180f;
			float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
			float dx = globalX - _plan.Origin.X, dz = globalZ - _plan.Origin.Z;
			return new PlanPoint
			{
				X = (int)MathF.Round(dx * cos - dz * sin),
				Z = (int)MathF.Round(dx * sin + dz * cos),
			};
		}

		private void BuildPlatformDressing()
		{
			foreach (PlanPlatform platform in _plan.Platforms)
			{
				List<Vector2> polygon = platform.Polygon.Select(LocalPoint).ToList();
				if (platform.EdgeTreatment != PlanEdgeTreatment.None)
				for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
				{
					Vector2 from = polygon[edgeIndex];
					Vector2 to = polygon[(edgeIndex + 1) % polygon.Count];
					Vector2 edge = to - from;
					float length = edge.Length();
					if (length < 1f) continue;
					Vector2 direction = edge / length;
					Vector2 inward = PolygonArea(polygon) >= 0f
						? new Vector2(-direction.Y, direction.X)
						: new Vector2(direction.Y, -direction.X);
					Vector2 outward = -inward;
					for (float along = 1f; along < length; along += 1.5f)
					{
						Vector2 centre = from + direction * along;
						float gx = centre.X + _data.OriginX, gz = centre.Y + _data.OriginZ;
						float field = _platformEdge.Fbm(gx / 43f - 31f, gz / 43f + 17f, 3);
						BuildTerraceApron(platform, centre, outward, field);
					}
					int spacing = platform.EdgeTreatment switch
					{
						PlanEdgeTreatment.Revetment => 22,
						PlanEdgeTreatment.Submerged => 28,
						_ => 13,
					};
					for (float along = spacing * .5f; along < length; along += spacing)
					{
						Vector2 centre = from + direction * along;
						float gx = centre.X + _data.OriginX, gz = centre.Y + _data.OriginZ;
						float field = _platformEdge.Fbm(gx / 31f + 41f, gz / 31f - 29f, 3);
						if (field < -.28f) continue;
						switch (platform.EdgeTreatment)
						{
						case PlanEdgeTreatment.Revetment:
							BuildEdgeButtress(centre, outward, 4 + (field > .24f ? 2 : 0)); break;
						case PlanEdgeTreatment.Submerged:
							BuildEdgeButtress(centre + outward * 2f, outward, 3 + (field > .3f ? 2 : 0)); break;
						case PlanEdgeTreatment.Ragged:
							BuildRubbleCluster(centre + outward * (field > .15f ? 2f : -2f), edgeIndex); break;
						}
					}
				}
				foreach (PlanPlatformCutout cutout in platform.Cutouts)
					BuildCutoutRim(platform, cutout);
			}
		}

		private void BuildTerraceApron(PlanPlatform platform, Vector2 centre,
			Vector2 outward, float field)
		{
			int width;
			int drop;
			switch (platform.EdgeTreatment)
			{
				case PlanEdgeTreatment.Revetment:
					width = 5; drop = field > .18f ? 1 : 2; break;
				case PlanEdgeTreatment.PrecinctWall:
					width = 3; drop = 1; break;
				case PlanEdgeTreatment.Submerged when field > -.20f:
					width = 4; drop = field > .24f ? 1 : 3; break;
				case PlanEdgeTreatment.Ragged when field > .08f:
					width = 2; drop = 1; break;
				default:
					return;
			}

			for (int band = 1; band <= width; band++)
			{
				Vector2 point = centre + outward * band;
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				int top = platform.SurfaceY - drop - (band > width - 2 ? 1 : 0);
				if (top <= floor) continue;
				for (int y = floor; y < top; y++)
				{
					bool exposed = y == top - 1;
					byte material = exposed && field > .30f
						? Palette.MOSS_STONE
						: y > floor && (y - floor) % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE;
					Put(x, y, z, material);
				}
			}
		}

		private void BuildCutoutRim(PlanPlatform platform, PlanPlatformCutout cutout)
		{
			List<Vector2> polygon = cutout.Polygon.Select(LocalPoint).ToList();
			for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
			{
				Vector2 from = polygon[edgeIndex];
				Vector2 to = polygon[(edgeIndex + 1) % polygon.Count];
				Vector2 delta = to - from;
				float length = delta.Length();
				if (length < 1f) continue;
				Vector2 direction = delta / length;
				Vector2 side = new(-direction.Y, direction.X);
				for (float along = 2f; along < length - 1f; along += 3f)
				{
					Vector2 edge = from + direction * along;
					Vector2 a = edge + side * 1.4f;
					Vector2 b = edge - side * 1.4f;
					Vector2 platformSide = InsidePolygon(a, polygon) ? b : a;
					float gx = edge.X + _data.OriginX, gz = edge.Y + _data.OriginZ;
					float field = _platformEdge.Fbm(gx / 25f + 53f, gz / 25f - 47f, 3);
					if (field < -.34f) continue;
					int height = cutout.Role == PlanCutoutRole.Collapsed
						? 2 + (field > .20f ? 2 : 0)
						: 1 + (field > .32f ? 1 : 0);
					BuildMasonryColumn((int)MathF.Round(platformSide.X),
						(int)MathF.Round(platformSide.Y), height, Palette.STONE_PALE);
					if (cutout.Role == PlanCutoutRole.Collapsed && field > -.02f)
					{
						Vector2 pitSide = platformSide == a ? b : a;
						BuildRubbleCluster(pitSide + direction * (edgeIndex % 2), edgeIndex);
					}
				}
			}
		}

		private void BuildEdgeButtress(Vector2 centre, Vector2 normal, int height)
		{
			Vector2 tangent = new(normal.Y, -normal.X);
			for (int n = 0; n < 2; n++)
			for (int t = -1; t <= 1; t++)
			{
				Vector2 point = centre + normal * n + tangent * t;
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				for (int y = 0; y < height; y++)
					Put(x, floor + y, z, y == height - 1 ? Palette.STONE_WARM : Palette.STONE_PALE);
			}
		}

		private void BuildRubbleCluster(Vector2 centre, int phase)
		{
			ReadOnlySpan<Vector2I> offsets = phase % 2 == 0
				? new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(-1, 0) }
				: new[] { new Vector2I(0, 0), new Vector2I(-1, 1), new Vector2I(1, 1) };
			for (int i = 0; i < offsets.Length; i++)
			{
				int x = (int)MathF.Round(centre.X) + offsets[i].X;
				int z = (int)MathF.Round(centre.Y) + offsets[i].Y;
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				Put(x, floor, z, i == 0 ? Palette.STONE_PALE : Palette.RUBBLE);
				if (i == 0) Put(x, floor + 1, z, Palette.RUBBLE);
			}
		}

		private void BuildStair(PlanStair stair)
		{
			Vector2 from = LocalPoint(stair.From);
			Vector2 to = LocalPoint(stair.To);
			Vector2 delta = to - from;
			float length = delta.Length();
			if (length < .5f) return;
			Vector2 direction = delta / length;
			Vector2 across = new(-direction.Y, direction.X);
			int fromY = _platforms[stair.FromPlatformId].SurfaceY;
			int toY = _platforms[stair.ToPlatformId].SurfaceY;
			var cells = new HashSet<int>();
			int samples = Math.Max(1, (int)MathF.Ceiling(length * 2f));
			for (int sample = 0; sample <= samples; sample++)
			{
				float t = sample / (float)samples;
				Vector2 centre = from + delta * t;
				int stairY = (int)MathF.Round(Rng.Lerp(fromY, toY, t));
				for (int lane = 0; lane < stair.Width; lane++)
				{
					float offset = lane - (stair.Width - 1) * .5f;
					int x = (int)MathF.Round(centre.X + across.X * offset);
					int z = (int)MathF.Round(centre.Y + across.Y * offset);
					if (!InWindow(x, z)) continue;
					int key = z * _grid.Size + x;
					if (!cells.Add(key)) continue;
					int target = Math.Max(_data.Height[key], stairY);
					_grid.RedescribeUnedited(x, z, target, Palette.PAVING,
						Palette.STONE_PALE, Palette.STONE_PALE);
					_stairCells++;
				}
			}
		}

		private void BuildAuthoredRoutes()
		{
			var domainNodes = _plan.RouteSockets.Select(s => s.RouteNodeId)
				.ToHashSet(StringComparer.Ordinal);
			foreach (CanonicalRoute route in _world.Routes)
			{
				if (!domainNodes.Contains(route.FromNodeId) && !domainNodes.Contains(route.ToNodeId))
					continue;
				byte material = route.Kind == RoadKind.Trail ? Palette.PATH : Palette.PAVING;
				for (int i = 1; i < route.Points.Count; i++)
					DrawGroundLine(GlobalPoint(route.Points[i - 1]), GlobalPoint(route.Points[i]),
						Math.Max(1, (int)MathF.Round(route.Width)), material);
			}
		}

		private void DrawGroundLine(Vector2 from, Vector2 to, int width, byte material)
		{
			Vector2 delta = to - from;
			float length = delta.Length();
			if (length < .1f) return;
			Vector2 direction = delta / length;
			Vector2 across = new(-direction.Y, direction.X);
			int samples = Math.Max(1, (int)MathF.Ceiling(length * 1.5f));
			for (int sample = 0; sample <= samples; sample++)
			{
				Vector2 centre = from + delta * (sample / (float)samples);
				for (int lane = 0; lane < width; lane++)
				{
					float offset = lane - (width - 1) * .5f;
					int x = (int)MathF.Round(centre.X + across.X * offset);
					int z = (int)MathF.Round(centre.Y + across.Y * offset);
					if (!InWindow(x, z)) continue;
					int key = z * _grid.Size + x;
					int y = _grid.Top[key] - 1;
					if (y < 0) continue;
					Put(x, y, z, material);
					_routeCells.Add(key);
				}
			}
		}

		private void BuildWall(PlanWall wall)
		{
			byte material = ResolveMasonry(wall.MaterialId);
			var openings = wall.OpeningSocketIds.Where(_sockets.ContainsKey)
				.Select(id => LocalPoint(_sockets[id].Point)).ToArray();
			for (int part = 1; part < wall.Points.Count; part++)
			{
				Vector2 from = LocalPoint(wall.Points[part - 1]);
				Vector2 to = LocalPoint(wall.Points[part]);
				Vector2 delta = to - from;
				float length = delta.Length();
				if (length < .1f) continue;
				Vector2 direction = delta / length;
				Vector2 across = new(-direction.Y, direction.X);
				int samples = Math.Max(1, (int)MathF.Ceiling(length));
				int thickness = wall.Height >= 15 ? 3 : 2;
				float phase = ((uint)Rng.StableHash(wall.Id) & 0xffffu) / 65535f * MathF.Tau;
				for (int sample = 0; sample <= samples; sample++)
				{
					float t = sample / (float)samples;
					Vector2 centre = from + delta * t;
					if (openings.Any(o => o.DistanceTo(centre) <= Math.Max(3f, thickness * 2f))) continue;
					int realisedHeight = WallHeight(wall, sample, phase);
					int baseX = (int)MathF.Round(centre.X);
					int baseZ = (int)MathF.Round(centre.Y);
					int baseFloor = InWindow(baseX, baseZ)
						? _grid.Top[baseZ * _grid.Size + baseX]
						: -1;
					for (int lane = 0; lane < thickness; lane++)
					{
						float offset = lane - (thickness - 1) * .5f;
						int x = (int)MathF.Round(centre.X + across.X * offset);
						int z = (int)MathF.Round(centre.Y + across.Y * offset);
						BuildMasonryColumn(x, z, realisedHeight, material);
					}
					if (baseFloor >= 0 && realisedHeight >= 7)
						BuildWallCoping(centre, across, baseFloor + realisedHeight - 1,
							thickness + 1, material);
					if (sample > 0 && sample < samples && sample % 18 == 0)
						BuildWallButtress(centre, direction, across,
							Math.Min(realisedHeight, 5 + (sample / 18) % 3), material);
					float gx = centre.X + _data.OriginX, gz = centre.Y + _data.OriginZ;
					float footAge = _platformEdge.Fbm(gx / 32f - 71f, gz / 32f + 61f, 3);
					if (wall.State != PlanWallState.Standing && sample % 11 == 0 && footAge > -.06f)
						BuildRubbleCluster(centre + across * (footAge > .2f ? 2.5f : -2.5f), sample);
				}
			}
		}

		private void BuildWallCoping(Vector2 centre, Vector2 across, int y,
			int halfWidth, byte material)
		{
			for (int lane = -halfWidth; lane <= halfWidth; lane++)
			{
				Vector2 point = centre + across * lane;
				Put((int)MathF.Round(point.X), y, (int)MathF.Round(point.Y),
					lane is -1 or 0 or 1 ? Palette.STONE_WARM : material);
			}
		}

		private void BuildWallButtress(Vector2 centre, Vector2 direction,
			Vector2 across, int height, byte material)
		{
			for (int side = -1; side <= 1; side += 2)
			for (int projection = 2; projection <= 4; projection++)
			for (int along = -1; along <= 1; along++)
			{
				Vector2 point = centre + across * (side * projection) + direction * along;
				int steppedHeight = Math.Max(2, height - (projection - 2));
				BuildMasonryColumn((int)MathF.Round(point.X), (int)MathF.Round(point.Y),
					steppedHeight, material);
			}
		}

		private static int WallHeight(PlanWall wall, int sample, float phase) => wall.State switch
		{
			PlanWallState.Standing => wall.Height,
			PlanWallState.Trace => 1,
			PlanWallState.Stub => Math.Min(wall.Height, 4),
			_ => Math.Clamp((int)MathF.Round(wall.Height *
				(.58f + .22f * MathF.Sin((sample / 12) * 1.7f + phase))), 2, wall.Height),
		};

		private void BuildMasonryColumn(int x, int z, int height, byte material)
		{
			if (!InWindow(x, z) || height <= 0) return;
			int floor = _grid.Top[z * _grid.Size + x];
			float age = _platformEdge.Fbm((x + _data.OriginX) / 29f + 83f,
				(z + _data.OriginZ) / 29f - 79f, 3);
			for (int y = 0; y < height; y++)
			{
				byte course = y <= 1 && age > .16f
					? Palette.MOSS_STONE
					: y > 0 && y % 5 == 0 ? Palette.STONE_WARM : material;
				Put(x, floor + y, z, course);
			}
		}

		private void BuildLandmark(PlanLandmark landmark)
		{
			Vector2 centre = LocalPoint(landmark.Point);
			float orientation = NormalizeDegrees(_plan.AxisDegrees + landmark.OrientationDegrees);
			switch (landmark.Kind)
			{
				case PlanLandmarkKind.Column:
					BuildColumn(centre, EffectiveHeight(landmark), landmark.State == PlanLandmarkState.Standing); break;
				case PlanLandmarkKind.Pylon:
					BuildPylon(centre, EffectiveHeight(landmark), orientation,
						landmark.State == PlanLandmarkState.Standing); break;
				case PlanLandmarkKind.Arch:
					BuildArch(centre, landmark.Height, landmark.Span, orientation, landmark.State); break;
				case PlanLandmarkKind.Colonnade:
					BuildColonnade(centre, landmark, orientation); break;
				case PlanLandmarkKind.FallenColumn:
					BuildFallenColumn(centre, landmark.Length, orientation); break;
				case PlanLandmarkKind.Emblem:
					BuildEmblem(centre, landmark.Span, landmark.PlatformId); break;
				case PlanLandmarkKind.Basin:
					BuildBasin(centre, landmark.Span, landmark.PlatformId); break;
			}
		}

		private static int EffectiveHeight(PlanLandmark landmark) => landmark.State switch
		{
			PlanLandmarkState.Stump => Math.Max(4, landmark.Height / 3),
			PlanLandmarkState.Broken => Math.Max(6, (int)MathF.Round(landmark.Height * .76f)),
			_ => landmark.Height,
		};

		private void BuildColumn(Vector2 centre, int height, bool capital)
		{
			int cx = (int)MathF.Round(centre.X), cz = (int)MathF.Round(centre.Y);
			int floor = FootprintFloor(cx, cz, 2);
			if (floor < 0) return;
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++) Put(cx + dx, floor, cz + dz, Palette.STONE_PALE);
			for (int y = 1; y < height - (capital ? 2 : 0); y++)
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++)
			{
				if (dx != 0 && dz != 0) continue;
				Put(cx + dx, floor + y, cz + dz, y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE);
			}
			if (!capital) return;
			for (int y = height - 2; y < height; y++)
			for (int dz = -2; dz <= 2; dz++)
			for (int dx = -2; dx <= 2; dx++)
				if (Math.Abs(dx) + Math.Abs(dz) <= 3)
					Put(cx + dx, floor + y, cz + dz, y == height - 1 ? Palette.STONE_WARM : Palette.STONE_PALE);
		}

		private void BuildPylon(Vector2 centre, int height, float degrees, bool intact)
		{
			int floor = FootprintFloor((int)MathF.Round(centre.X), (int)MathF.Round(centre.Y), 4);
			if (floor < 0) return;
			for (int y = 0; y < height; y++)
			{
				int halfRight = y < 2 || (intact && y >= height - 2) ? 2 : 1;
				int halfForward = y == 0 ? 2 : 1;
				for (int f = -halfForward; f <= halfForward; f++)
				for (int r = -halfRight; r <= halfRight; r++)
				{
					if (!intact && y > height - 3 && r + f > 1) continue;
					Vector2 point = Oriented(centre, r, f, degrees);
					bool motif = f == -halfForward && r == 0 && y >= 4 && y < height - 3 &&
					             (y % 6 is 0 or 1 || y % 6 == 4);
					Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
						motif || y > 0 && y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE);
				}
			}
		}

		private void BuildArch(Vector2 centre, int height, int span, float degrees,
			PlanLandmarkState state)
		{
			int floor = FootprintFloor((int)MathF.Round(centre.X), (int)MathF.Round(centre.Y), span / 2 + 4);
			if (floor < 0) return;
			int pierOffset = span / 2 + 2;
			int spring = Math.Max(5, height - 5);
			for (int side = -1; side <= 1; side += 2)
			for (int y = 0; y < 2; y++)
			for (int f = -3; f <= 3; f++)
			for (int r = -3; r <= 3; r++)
			{
				if (Math.Abs(r) + Math.Abs(f) > 5) continue;
				Vector2 point = Oriented(centre, side * pierOffset + r, f, degrees);
				Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
					y == 0 ? Palette.STONE_WARM : Palette.STONE_PALE);
			}
			for (int side = -1; side <= 1; side += 2)
			for (int y = 0; y < spring; y++)
			for (int f = -2; f <= 2; f++)
			for (int r = -2; r <= 2; r++)
			{
				Vector2 point = Oriented(centre, side * pierOffset + r, f, degrees);
				Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
					y > 0 && y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE);
			}

			int outer = span / 2 + 4;
			for (int y = spring; y < height; y++)
			for (int r = -outer; r <= outer; r++)
			for (int f = -2; f <= 2; f++)
			{
				// A broken arch keeps enough lintel to read as one monument, but
				// loses a coherent upper-right bite rather than salt-and-pepper blocks.
				if (state == PlanLandmarkState.Broken && y >= height - 2 && r > span / 4) continue;
				int corbel = Math.Max(0, y - spring);
				if (y < height - 2 && Math.Abs(r) < Math.Max(0, span / 2 - corbel * 2)) continue;
				Vector2 point = Oriented(centre, r, f, degrees);
				Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
					y == height - 1 ? Palette.STONE_WARM : Palette.STONE_PALE);
			}
		}

		private void BuildColonnade(Vector2 centre, PlanLandmark landmark, float degrees)
		{
			int count = Math.Max(1, landmark.Count);
			BuildStylobate(centre, landmark.Length, degrees);
			for (int i = 0; i < count; i++)
			{
				float along = count == 1 ? 0f : -landmark.Length * .5f + landmark.Length * i / (count - 1f);
				Vector2 point = Oriented(centre, 0f, along, degrees);
				int height = landmark.State switch
				{
					PlanLandmarkState.Stump => 4 + i % 3,
					PlanLandmarkState.Broken => Math.Max(7,
						(int)MathF.Round(landmark.Height * (.58f + .14f * (i % 3)))),
					_ => landmark.Height,
				};
				BuildColumn(point, height, landmark.State == PlanLandmarkState.Standing);
			}
			if (landmark.State is PlanLandmarkState.Standing or PlanLandmarkState.Broken)
				BuildColonnadeLintel(centre, landmark, degrees);
		}

		private void BuildStylobate(Vector2 centre, int length, float degrees)
		{
			for (int along = -length / 2 - 3; along <= length / 2 + 3; along++)
			for (int right = -3; right <= 3; right++)
			{
				Vector2 point = Oriented(centre, right, along, degrees);
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				Put(x, floor, z, right is -3 or 3 ? Palette.STONE_WARM : Palette.STONE_PALE);
				if (Math.Abs(right) <= 2) Put(x, floor + 1, z, Palette.STONE_PALE);
			}
		}

		private void BuildColonnadeLintel(Vector2 centre, PlanLandmark landmark, float degrees)
		{
			int floor = _platforms[landmark.PlatformId].SurfaceY;
			for (int along = -landmark.Length / 2; along <= landmark.Length / 2; along++)
			{
				Vector2 spine = Oriented(centre, 0f, along, degrees);
				float field = _platformEdge.Fbm((spine.X + _data.OriginX) / 19f + 101f,
					(spine.Y + _data.OriginZ) / 19f - 97f, 3);
				if (landmark.State == PlanLandmarkState.Broken && field < .02f) continue;
				for (int right = -2; right <= 2; right++)
				for (int y = 0; y < 2; y++)
				{
					Vector2 point = Oriented(centre, right, along, degrees);
					Put((int)MathF.Round(point.X), floor + landmark.Height - 2 + y,
						(int)MathF.Round(point.Y), y == 1 ? Palette.STONE_WARM : Palette.STONE_PALE);
				}
			}
		}

		private void BuildFallenColumn(Vector2 centre, int length, float degrees)
		{
			for (int i = 0; i < length; i++)
			for (int across = -1; across <= 1; across++)
			{
				Vector2 point = Oriented(centre, across, i - (length - 1) * .5f, degrees);
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				Put(x, floor, z, i % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE);
				if (across == 0) Put(x, floor + 1, z, Palette.STONE_PALE);
			}
		}

		private void BuildEmblem(Vector2 centre, int span, string platformId)
		{
			int radius = Math.Max(2, span / 2);
			int floor = _platforms[platformId].SurfaceY - 1;
			int cx = (int)MathF.Round(centre.X), cz = (int)MathF.Round(centre.Y);
			for (int z = -radius; z <= radius; z++)
			for (int x = -radius; x <= radius; x++)
			{
				float d = MathF.Sqrt(x * x + z * z);
				bool ring = MathF.Abs(d - radius * .78f) < 1f || MathF.Abs(d - radius * .42f) < 1f;
				bool cross = (Math.Abs(x) <= 1 && Math.Abs(z) <= radius / 2) ||
				             (Math.Abs(z) <= 1 && Math.Abs(x) <= radius / 2);
				if (ring || cross) Put(cx + x, floor, cz + z, Palette.STONE_WARM);
			}
		}

		private void BuildBasin(Vector2 centre, int span, string platformId)
		{
			int radius = Math.Max(2, span / 2);
			int floor = _platforms[platformId].SurfaceY;
			int cx = (int)MathF.Round(centre.X), cz = (int)MathF.Round(centre.Y);
			for (int z = -radius; z <= radius; z++)
			for (int x = -radius; x <= radius; x++)
			{
				int edge = Math.Max(Math.Abs(x), Math.Abs(z));
				if (edge == radius) Put(cx + x, floor, cz + z, Palette.STONE_PALE);
				else if (edge < radius) Put(cx + x, floor - 1, cz + z, Palette.STONE_WARM);
			}
		}

		private int FootprintFloor(int cx, int cz, int radius)
		{
			int floor = -1;
			for (int z = cz - radius; z <= cz + radius; z++)
			for (int x = cx - radius; x <= cx + radius; x++)
				if (InWindow(x, z)) floor = Math.Max(floor, _grid.HeightAt(x, z));
			return floor;
		}

		private void Put(int x, int y, int z, byte material)
		{
			if (!_grid.InBounds(x, y, z)) return;
			_grid.Set(x, y, z, material);
			if (Palette.IsSolid(material))
			{
				int index = z * _grid.Size + x;
				if (y + 1 > _grid.Heights[index]) _grid.Heights[index] = (short)(y + 1);
			}
			_placedBlocks++;
		}

		private Vector2 LocalPoint(PlanPoint point) => GlobalPoint(_plan.ToGlobal(point));

		private Vector2 GlobalPoint(BlockPoint point) =>
			new(point.X - _data.OriginX, point.Z - _data.OriginZ);

		private bool InWindow(int x, int z) => x >= 0 && z >= 0 && x < _grid.Size && z < _grid.Size;

		private static Vector2 Oriented(Vector2 centre, float right, float forward, float degrees)
		{
			float radians = degrees * MathF.PI / 180f;
			Vector2 f = new(MathF.Sin(radians), MathF.Cos(radians));
			Vector2 r = new(MathF.Cos(radians), -MathF.Sin(radians));
			return centre + r * right + f * forward;
		}

		private static float NormalizeDegrees(float degrees)
		{
			while (degrees >= 360f) degrees -= 360f;
			while (degrees < 0f) degrees += 360f;
			return degrees;
		}

		private static bool InsidePolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
		{
			bool inside = false;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				Vector2 a = polygon[i], b = polygon[j];
				bool crosses = (a.Y > point.Y) != (b.Y > point.Y) &&
				               point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X;
				if (crosses) inside = !inside;
			}
			return inside;
		}

		private static float DistanceToEdges(Vector2 point, IReadOnlyList<Vector2> polygon)
		{
			float best = float.PositiveInfinity;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				Vector2 a = polygon[j], b = polygon[i];
				Vector2 edge = b - a;
				float lengthSquared = edge.LengthSquared();
				float t = lengthSquared <= .0001f ? 0f : Math.Clamp((point - a).Dot(edge) / lengthSquared, 0f, 1f);
				best = Math.Min(best, point.DistanceTo(a + edge * t));
			}
			return best;
		}

		private static float PolygonArea(IReadOnlyList<Vector2> polygon)
		{
			float area = 0f;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
				area += polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
			return area * .5f;
		}

		private static int PositiveModulo(int value, int modulus)
		{
			int result = value % modulus;
			return result < 0 ? result + modulus : result;
		}

		private static int FloorDiv(int value, int divisor)
		{
			int quotient = value / divisor;
			return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
		}

		private static byte ResolveMasonry(string id) => id switch
		{
			"stone-pale-bedded" or "stone-pale-terraced" or "stone-pale-quarried" => Palette.STONE_PALE,
			"old-southern-masonry" or "stone-warm" => Palette.STONE_WARM,
			"stone-mossed" => Palette.MOSS_STONE,
			_ => throw new InvalidOperationException($"domain masonry material '{id}' has no blockout palette mapping"),
		};
	}
}

public readonly record struct DomainBlockoutStatistics(int Platforms, int PlatformCells,
	int TerrainCapCells, int PavedCapCells, int ReclaimedCapCells, int Cutouts, int CutoutCells,
	int Stairs, int StairCells, int RouteCells, int Walls, int Landmarks, int PlacedBlocks);
