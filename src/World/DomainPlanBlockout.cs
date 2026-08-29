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
		private readonly int _gateSlotHalf;
		private readonly int _gateSlotFloor;
		private readonly int _gatePlanZ;
		private readonly int _approachY;
		private readonly HashSet<int> _routeCells = new();
		private int _platformCells;
		private int _terrainCapCells;
		private int _pavedCapCells;
		private int _reclaimedCapCells;
		private int _cutoutCells;
		private int _stairCells;
		private readonly HashSet<int> _stairKeys = new();
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
			PlanLandmark gate = _plan.Landmarks.FirstOrDefault(l => l.Id == "gate-hero-arch")
				?? _plan.Landmarks.FirstOrDefault(l => l.Kind == PlanLandmarkKind.Gate);
			_gateSlotHalf = gate != null ? Math.Max(5, gate.Span / 2) : 0;
			_gateSlotFloor = gate != null && _platforms.TryGetValue(gate.PlatformId, out PlanPlatform gateFloor)
				? gateFloor.SurfaceY : 0;
			// Masonry-face and slot-floor bands are offsets from this Z. A
			// hardcoded 14–58 band stayed on the old slot when the massif moved
			// and left an 18-wide palace rib in the near frame.
			_gatePlanZ = gate?.Point.Z ?? 48;
			_approachY = _platforms.TryGetValue("gate-forecourt", out PlanPlatform approach)
				? approach.SurfaceY : 104;
		}

		public DomainBlockoutStatistics Compile()
		{
			// Stairs replace part of the already-raised platforms. Both passes must
			// therefore happen before paths and masonry create sparse edits.
			foreach (PlanPlatform platform in _plan.Platforms) BuildPlatform(platform);
			foreach (PlanPlatform platform in _plan.Platforms) BuildPlatformCutouts(platform);
			foreach (PlanStair stair in _plan.Stairs) BuildStair(stair);
			StainPrecinctCourt();
			InlayPavingGlyphs();
			DrapeRevetmentRims();
			BuildPlatformDressing();
			GrowRevetmentFaces();
			// Do not punch AIR holes in 114/124 south faces. A meander cadence
			// there reads as windows, not glyphs (reference-1 is a sheer cliff;
			// reference-6 carves columns and pylons, not the massif).
			foreach (PlanPlatform platform in _plan.Platforms) BuildCausewayPiers(platform);
			BuildAuthoredRoutes();
			foreach (PlanLandmark landmark in _plan.Landmarks)
			{
				BuildLandmark(landmark);
				BuildLandmarkRubble(landmark);
			}
			foreach (PlanWall wall in _plan.Walls) BuildWall(wall);
			return new DomainBlockoutStatistics(_plan.Platforms.Count, _platformCells,
				_terrainCapCells, _pavedCapCells, _reclaimedCapCells,
				_plan.Platforms.Sum(p => p.Cutouts.Count), _cutoutCells,
				_plan.Stairs.Count, _stairCells, _routeCells.Count, _plan.Walls.Count,
				_plan.Landmarks.Count, _placedBlocks);
		}

		private void BuildPlatform(PlanPlatform platform)
		{
			int cellsBefore = _platformCells;
			List<Vector2> polygon = platform.Polygon.Select(LocalPoint).ToList();
			List<List<Vector2>> collapsed = platform.Cutouts
				.Where(c => c.Role == PlanCutoutRole.Collapsed)
				.Select(c => c.Polygon.Select(LocalPoint).ToList()).ToList();
			float platformSouth = platform.Polygon.Min(p => p.Z);
			List<TerrainCourt> terrainCourts = platform.Cutouts
				.Where(c => c.Role == PlanCutoutRole.Terrain)
				.Select(c => new TerrainCourt(
					c.Polygon.Select(LocalPoint).ToList(),
					c.Polygon.Min(p => p.Z),
					c.Polygon.Max(p => p.Z),
					// Only a cutout that meets the south polyline is a Massif
					// cleft. +14 treated rear plateau courts as south ramps and
					// punched them down to the approach, leaving 144 overlay
					// seated on 104 — a 40-block tower in a trench.
					c.Polygon.Min(p => p.Z) <= platformSouth + 4f))
				.ToList();
			int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
			int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
			int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
			int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
			byte masonry = ResolveMasonry(platform.MaterialId);
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
			{
				var cell = new Vector2(x + .5f, z + .5f);
				if (!InsidePolygon(cell, polygon) || collapsed.Any(c => InsidePolygon(cell, c)) ||
				    CollapseMouth(cell, polygon, collapsed) ||
				    FrayedAway(platform, cell, polygon)) continue;
				int index = z * _data.Width + x;
				PlanPoint planLocal = ToPlanLocal(x + _data.OriginX, z + _data.OriginZ);
				// A column cannot be 144 with a hole through it. Leave the
				// processional at the gate floor so overlay lintel and 144
				// cheeks read as a slot in the cliff (reference-1/2), not a U
				// standing on a terrace in front of a solid mesa.
				if (_gateSlotHalf > 0 && platform.SurfaceY >= 140 &&
				    Math.Abs(planLocal.X) < _gateSlotHalf)
				{
					int passage = Math.Max(_data.Height[index], _gateSlotFloor);
					if (passage <= _data.SeaLevel) passage = _data.SeaLevel + 1;
					_grid.Describe(x, z, passage, Palette.PAVING, masonry, masonry);
					_platformCells++;
					_pavedCapCells++;
					continue;
				}
				int target = Math.Max(_data.Height[index], platform.SurfaceY);
				if (platform.Id == "causeway-gate-pier")
				{
					// The scarp's even-odd notch can leave a 114 lip in the
					// channel. This spur is the drowned processional into the
					// waterfront gate (reference-1); leftover shelf cannot
					// seal it.
					target = platform.SurfaceY;
					if (target <= _data.SeaLevel) target = _data.SeaLevel + 1;
				}
				else if (target == _data.Height[index] && _data.Height[index] > platform.SurfaceY) continue;
				byte naturalCap = _grid.Cap[index];
				TerrainCourt? gash = null;
				foreach (TerrainCourt court in terrainCourts)
				{
					if (!InsidePolygon(cell, court.Local)) continue;
					gash = court;
					break;
				}
				bool terrainCourt = gash != null;
				byte cap = terrainCourt
					? (Palette.IsGrassSurface(naturalCap) ? naturalCap : Palette.GRASS)
					: PlatformCap(platform, x, z, naturalCap);
				bool cameraRim = !InsidePolygon(new Vector2(x + .5f, z + 1.5f), polygon) ||
				                 !InsidePolygon(new Vector2(x + 1.5f, z + .5f), polygon);
				// South-face terrain gashes are hillside ramps, not flush grass
				// rectangles and not excavated boxes. Raising them with the terrace
				// sealed the Massif opening; skipping the raise left 10-block pits.
				if (gash is { OpensSouth: true } openGash && Math.Abs(planLocal.X) > 9)
				{
					if (platform.SurfaceY >= 120)
					{
						// Upper-court south gashes are clefts between slabs, not
						// ramps. RUINS §5a: height only changes at a slab edge;
						// the grand stair is the notch through that face.
						target = Math.Max((int)_data.Height[index], _approachY);
						if (target <= _data.SeaLevel) target = _data.SeaLevel + 1;
					}
					else
					{
						float span = Math.Max(1f, openGash.MaxPlanZ - openGash.MinPlanZ);
						float t = Math.Clamp((planLocal.Z - openGash.MinPlanZ) / span, 0f, 1f);
						float blend = Math.Clamp((t - 0.06f) / 0.70f, 0f, 1f);
						int open = platform.SurfaceY - 12;
						int hillside = Math.Clamp(_data.Height[index], open - 2, open);
						target = (int)MathF.Round(hillside + (platform.SurfaceY - hillside) * blend);
						if (target <= _data.SeaLevel) target = _data.SeaLevel + 1;
					}
				}
				else if (platform.EdgeTreatment == PlanEdgeTreatment.Revetment &&
				         Math.Abs(planLocal.X) > 9 &&
				         platform.SurfaceY < 110)
				{
					// 104/108 rims: a one-cell bevel on a 6–10-block sheer face
					// was the green-striped canal lock. Ramp those courts into
					// the hillside. 114 Massif cheeks stay sheer slabs
					// (RUINS §5a: height only changes at a slab edge).
					// 124 hinterland stays sheer too: ramping |X|>22 made
					// ziggurat bleachers from 520u (reference-1/2 are one
					// masonry cliff with a gate cut in). Jagged south edges
					// at different Z keep that from reading as a palace wall.
					float edge = DistanceToEdges(cell, polygon);
					if (edge < 8f)
					{
						float blend = Math.Clamp((edge - 0.5f) / 7f, 0f, 1f);
						int open = platform.SurfaceY - Math.Min(8, Math.Max(3,
							platform.SurfaceY - _data.Height[index]));
						int hillside = Math.Clamp(_data.Height[index], open - 2, open);
						target = (int)MathF.Round(hillside + (platform.SurfaceY - hillside) * blend);
						if (target <= _data.SeaLevel) target = _data.SeaLevel + 1;
					}
				}
				if (cap == naturalCap || Palette.IsGrassSurface(cap) || cap is Palette.MOSS or Palette.MOSS_STONE)
				{
					if (cap is Palette.MOSS or Palette.MOSS_STONE) _reclaimedCapCells++;
					else _terrainCapCells++;
				}
				else _pavedCapCells++;
				// Column profiles only expose cap+sub; Deep is the cliff. A
				// per-column grass/moss mix on a six-block drop inks as a palisade.
				// Camera-facing rims share one living Deep sampled at court scale.
				byte deep = masonry;
				if (cameraRim && !KeepsMasonryFace(planLocal, platform.SurfaceY) &&
				    platform.SurfaceY < 110 &&
				    platform.Role != PlanPlatformRole.Causeway)
				{
					// Living Deep belongs to the 104/108 hillside, not the 114
					// Massif cheeks. Grass Deep on those 10-block slabs inks as
					// the same canal lock the ramp was meant to hide.
					deep = Palette.GRASS;
				}
				if (platform.Id == "causeway-gate-pier")
					_grid.RedescribeUnedited(x, z, target, cap, masonry, deep);
				else
					_grid.Describe(x, z, target, cap, masonry, deep);
				_platformCells++;
			}
			if (platform.SurfaceY >= 140 || platform.Id == "causeway-gate-pier")
				GD.Print($"[domain-blockout] platform '{platform.Id}' {_platformCells - cellsBefore} cells");
		}

		/// <summary>
		/// A collapsed bay inset from the south/east polyline left a masonry lip
		/// in front of the pit. Opening that lip is what makes a Massif gash rather
		/// than a rectangular basin. The processional ribbon stays intact.
		/// </summary>
		private bool CollapseMouth(Vector2 cell, IReadOnlyList<Vector2> polygon,
			List<List<Vector2>> collapsed)
		{
			if (collapsed.Count == 0) return false;
			PlanPoint planLocal = ToPlanLocal((int)MathF.Floor(cell.X) + _data.OriginX,
				(int)MathF.Floor(cell.Y) + _data.OriginZ);
			if (Math.Abs(planLocal.X) <= 9) return false;
			if (DistanceToEdges(cell, polygon) > 6.5f) return false;
			bool cameraRim = !InsidePolygon(new Vector2(cell.X, cell.Y + 1f), polygon) ||
			                 !InsidePolygon(new Vector2(cell.X + 1f, cell.Y), polygon);
			if (!cameraRim) return false;
			return collapsed.Any(cutout => DistanceToEdges(cell, cutout) < 3.4f);
		}

		/// <summary>
		/// Reference-2/11: grass and moss sit on the masonry rim so the cliff and
		/// the court are one mass. A bare pale face at review distance is a wall.
		/// </summary>
		private void DrapeRevetmentRims()
		{
			foreach (PlanPlatform platform in _plan.Platforms)
			{
				if (platform.EdgeTreatment != PlanEdgeTreatment.Revetment) continue;
				List<Vector2> polygon = platform.Polygon.Select(LocalPoint).ToList();
				int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
				int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
				int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
				int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
				for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++)
				{
					var cell = new Vector2(x + .5f, z + .5f);
					if (!InsidePolygon(cell, polygon)) continue;
					float edge = DistanceToEdges(cell, polygon);
					if (edge < 0.4f || edge > 2.8f) continue;
					PlanPoint planLocal = ToPlanLocal(x + _data.OriginX, z + _data.OriginZ);
					if (Math.Abs(planLocal.X) <= 9) continue;
					int index = z * _grid.Size + x;
					_grid.Sub[index] = Palette.SAND;
					if (platform.SurfaceY >= 110) continue;
					float field = _platformEdge.Fbm((x + _data.OriginX) / 22f + 5f,
						(z + _data.OriginZ) / 22f - 7f, 3);
					// A moss lip on the 104/108 rim, not a mossed court. The tan
					// cap has to survive or the plateau reads as sage from 380
					// units. 114 Massif cheeks skipped above: a moss stripe on
					// those rims was the lime-green trim in every near frame.
					if (field > .46f) _grid.Cap[index] = Palette.MOSS;
					else if (field > .18f && _grid.Cap[index] != Palette.STONE_WARM)
						_grid.Cap[index] = Palette.MOSS_STONE;
				}
			}
		}

		/// <summary>
		/// Column profiles only expose one grass cap over masonry Deep, so a
		/// six-block terrace reads as a wall. Grow the cap down the camera-facing
		/// drop with a wavelength field, leaving a short masonry plinth. This is
		/// the reference-2 fact that the land has grown over the city.
		/// </summary>
		private void GrowRevetmentFaces()
		{
			foreach (PlanPlatform platform in _plan.Platforms)
			{
				if (platform.EdgeTreatment != PlanEdgeTreatment.Revetment) continue;
				List<Vector2> polygon = platform.Polygon.Select(LocalPoint).ToList();
				int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
				int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
				int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
				int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
				for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++)
				{
					var cell = new Vector2(x + .5f, z + .5f);
					if (!InsidePolygon(cell, polygon)) continue;
					int index = z * _grid.Size + x;
					int floor = _grid.Top[index];
					if (floor < platform.SurfaceY - 1) continue;
					PlanPoint planLocal = ToPlanLocal(x + _data.OriginX, z + _data.OriginZ);
					if (Math.Abs(planLocal.X) <= 9) continue;
					// Living Deep on the stair cheeks left a paving cut through
					// a dirt bank. Keep masonry on the slot and the actual
					// flights — not a 48-block |X|<=24 palace face.
					if (KeepsMasonryFace(planLocal, platform.SurfaceY))
						continue;
					int drop = 0;
					if (InWindow(x, z + 1))
						drop = Math.Max(drop, floor - _grid.Top[(z + 1) * _grid.Size + x]);
					if (InWindow(x, z - 1))
						drop = Math.Max(drop, floor - _grid.Top[(z - 1) * _grid.Size + x]);
					if (InWindow(x + 1, z))
						drop = Math.Max(drop, floor - _grid.Top[z * _grid.Size + x + 1]);
					if (InWindow(x - 1, z))
						drop = Math.Max(drop, floor - _grid.Top[z * _grid.Size + x - 1]);
					if (drop < 3) continue;
					byte masonry = ResolveMasonry(platform.MaterialId);
					// Made courts keep their cap. Growing grass there painted the
					// red/green lip that every near capture read as trim, not a
					// tan plateau (reference-1). 124/144 drops stay one masonry
					// Deep — living Deep on a 12-block face inks as a ribbed
					// palisade, and pulling the slab away only revealed the
					// same scarp in the atlas heightfield (reference-1/2: the
					// cliff and the gate are the same stone).
					if (_grid.Cap[index] is Palette.PAVING or Palette.PATH or Palette.STONE_WARM)
					{
						if (drop >= 5)
						{
							_grid.Sub[index] = Palette.SAND;
							_grid.Deep[index] = platform.SurfaceY < 110 ? Palette.GRASS : masonry;
						}
						continue;
					}
					if (platform.SurfaceY < 110)
					{
						// Approach shelf: tan/olive plateau over a living hillside, not
						// a second masonry wall in front of the 124 massif. 114
						// cheeks fall through to one masonry Deep so the two
						// ~10-block faces are the same stone as the gate.
						_grid.Cap[index] = Palette.GRASS;
						_grid.Sub[index] = Palette.SAND;
						_grid.Deep[index] = drop >= 12 ? masonry : Palette.GRASS;
						continue;
					}
					// 124/144: one masonry Deep. MOSS_STONE vs pale masonry on
					// neighbouring columns was the grey/green palisade — ink
					// draws a vertical between every material change.
					_grid.Sub[index] = Palette.SAND;
					_grid.Deep[index] = masonry;
				}
			}
		}

		/// <summary>
		/// Masonry stays on the processional slot and the actual stair
		/// footprints. A 48-block |X|&lt;=24 band made the 124 south face a
		/// palace wall; living Deep between the grand flight and the side
		/// stair is the hillside the gate is cut from.
		/// </summary>
		private bool KeepsMasonryFace(PlanPoint p, int surfaceY)
		{
			int gz = _gatePlanZ;
			if (Math.Abs(p.X) <= 9)
			{
				// 104/110 processional ribbon stays a masonry cut.
				if (surfaceY < 120) return true;
				// 124/144: only the gate opening and the stair approach. An
				// 18-wide un-frayed rib the length of the north plateau was a
				// palace wall from every 3/4 camera (reference-1 is a tan
				// landform with a gate cut in, not a corridor of masonry).
				return p.Z >= gz - 34 && p.Z <= gz + 10;
			}
			if (surfaceY < 120) return false;
			// Grand stair: width 14 at x=0 plus one-block cheeks.
			if (Math.Abs(p.X) <= 10 && p.Z >= gz - 34 && p.Z <= gz + 4) return true;
			// East cliff stair: width 8 plus cheeks on the west 124 edge.
			if (p.X >= 14 && p.X <= 26 && p.Z >= gz - 34 && p.Z <= gz + 2) return true;
			// Waterfront stair and the planar E–W south face it bites. A
			// 24-wide stair box left the rest of that 76-wide face to fray
			// into a palisade; the wide camera's cliff is this whole edge.
			if (p.X <= -84 && p.X >= -160 && p.Z <= -140 && p.Z >= -150) return true;
			// Crown stair: along the overlay lip — not the whole west hinterland.
			if (p.Z >= gz + 2 && p.Z <= gz + 10 && p.X >= 30 && p.X <= 42) return true;
			if (p.Z >= gz + 2 && p.Z <= gz + 10 && p.X >= 14 && p.X <= 24) return true;
			// 144 inner cheeks around the overlay, seated on the 124 lip.
			if (surfaceY >= 140 && p.Z >= gz + 2 && p.Z <= gz + 12
			    && Math.Abs(p.X) >= 8 && Math.Abs(p.X) <= 22) return true;
			return false;
		}

		private bool FrayedAway(PlanPlatform platform, Vector2 cell, IReadOnlyList<Vector2> polygon)
		{
			// 144 overlay lips are five-by-four seating on the 124. Retreat of
			// 1.6 already eats the whole slab, and a rear terrain gash then
			// seats the remnant on 104 — a tower. Authored overlay stays intact.
			if (platform.SurfaceY >= 140) return false;
			// Reference-1's approach is a masonry bridge into the cliff gate.
			// The east-lobe hole field is for drowned patches in open water; it
			// would fray this 12-wide spur to nothing. Revetment retreat on a
			// 12-block Deck would eat the same walkway.
			if (platform.Id == "causeway-gate-pier") return false;
			if (platform.EdgeTreatment is not (PlanEdgeTreatment.Ragged or PlanEdgeTreatment.Submerged
			    or PlanEdgeTreatment.Revetment))
				return false;
			float edgeDistance = DistanceToEdges(cell, polygon);
			float globalX = cell.X + _data.OriginX;
			float globalZ = cell.Y + _data.OriginZ;
			float field = _platformEdge.Fbm(globalX / 24f, globalZ / 24f, 3);
			float retreat = platform.EdgeTreatment switch
			{
				PlanEdgeTreatment.Submerged => 2.5f + Math.Max(0f, field) * 8f,
				PlanEdgeTreatment.Revetment when platform.SurfaceY >= 120 =>
					KeepsMasonryFace(ToPlanLocal((int)MathF.Floor(cell.X) + _data.OriginX,
						(int)MathF.Floor(cell.Y) + _data.OriginZ), platform.SurfaceY)
						? 1.6f + (field * .5f + .5f) * 3.2f
						: 4.5f + (field * .5f + .5f) * 14f,
				PlanEdgeTreatment.Revetment => 1.6f + (field * .5f + .5f) * 5.2f,
				_ => 1.5f + Math.Max(0f, field) * 6f,
			};
			if (edgeDistance < retreat)
			{
				PlanPoint planLocal = ToPlanLocal((int)MathF.Floor(cell.X) + _data.OriginX,
					(int)MathF.Floor(cell.Y) + _data.OriginZ);
				if (KeepsMasonryFace(planLocal, platform.SurfaceY)) return false;
				// Camera-facing Massif band (local +Z, toward the near camera).
				// Ink skips coplanar faces and draws a convex silhouette at every
				// 1-block height change, so a noisy south edge is a palisade.
				// Keep eight blocks of uniform height; east/north lobes still fray.
				if (platform.SurfaceY >= 110)
				{
					for (int dz = 1; dz <= 8; dz++)
					{
						if (!InsidePolygon(new Vector2(cell.X, cell.Y + dz), polygon))
							return false;
					}
				}
				return true;
			}
			if (platform.Role == PlanPlatformRole.Causeway && platform.Id != "causeway-gate-pier")
			{
				// The land spine stays a walkable stain. East of it the atlas
				// water lives at plan x≈-60…-140; a solid 102 slab there was a
				// dry island. Reference-1/2 are posts and paving patches in the
				// shallows, so a wavelength field opens the lobe.
				PlanPoint planLocal = ToPlanLocal((int)MathF.Floor(cell.X) + _data.OriginX,
					(int)MathF.Floor(cell.Y) + _data.OriginZ);
				if (Math.Abs(planLocal.X) > 14f)
				{
					float holes = _platformEdge.Fbm(globalX / 56f + 9f, globalZ / 56f - 13f, 3);
					if (holes > .06f) return true;
				}
			}
			// A trace survives as broad archaeological patches, not a complete
			// slab. This is a wavelength field inside an authored envelope.
			return platform.Role == PlanPlatformRole.Trace &&
			       _platformEdge.Fbm(globalX / 38f + 19f, globalZ / 38f - 7f, 3) > .34f;
		}

		/// <summary>
		/// Reference-7/10: a meander picked out of surviving paving, one block
		/// deep, so the court is a glyph rather than a tiled plaza.
		/// </summary>
		private void InlayPavingGlyphs()
		{
			foreach (PlanPlatform platform in _plan.Platforms)
			{
				List<Vector2> polygon = platform.Polygon.Select(LocalPoint).ToList();
				int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
				int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
				int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
				int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
				for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++)
				{
					if (!InsidePolygon(new Vector2(x + .5f, z + .5f), polygon)) continue;
					int index = z * _grid.Size + x;
					byte cap = _grid.Cap[index];
					if (cap is not (Palette.PAVING or Palette.STONE_WARM or Palette.STONE_PALE
					    or Palette.PATH)) continue;
					int gx = x + _data.OriginX, gz = z + _data.OriginZ;
					PlanPoint planLocal = ToPlanLocal(gx, gz);
					if (platform.Id is "gate-approach-dais" or "gate-approach-inner"
					    or "lower-inner-court" or "stela-terrace")
					{
						// Reference-7/10: walking-distance courts are a meander
						// stain, not a blank flagstone pad. Lower precinct and
						// stela courts take the same density (reference-9).
						// Sample every block so the pattern reads at 240 units.
						if (!RuinKit.Meander(PositiveModulo(gx, 6), PositiveModulo(gz, 8)))
							continue;
						_grid.Cap[index] = Palette.RUBBLE;
						continue;
					}
					if (!RuinKit.Meander(PositiveModulo(gx / 2, 4), PositiveModulo(gz / 2, 8))) continue;
					float field = _platformEdge.Fbm(gx / 36f + 3f, gz / 36f - 9f, 2);
					if (cap == Palette.STONE_WARM && field < .22f) continue;
					// Crystal on the processional ribbon read as a magenta carpet.
					// Reference-7/10 pick the meander in warm stone on the axis.
					_grid.Cap[index] = Math.Abs(planLocal.X) > 9 && field > .18f
						&& PositiveModulo(gz, 3) == 1
						? Palette.CRYSTAL
						: Palette.STONE_WARM;
				}
			}
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
					byte cap = age > .22f ? Palette.MOSS
						: age > -.10f ? Palette.GRASS : Palette.MOSS_STONE;
					_grid.RedescribeUnedited(x, z, target, cap,
						Palette.GRASS, Palette.GRASS);
					_cutoutCells++;
				}
			}
		}

		private byte PlatformCap(PlanPlatform platform, int localX, int localZ, byte naturalCap)
		{
			int globalX = localX + _data.OriginX;
			int globalZ = localZ + _data.OriginZ;
			if (platform.Role == PlanPlatformRole.Causeway)
			{
				// Reference-1's causeway is pale masonry over water, not a mauve
				// plaza. SAND as a walking cap lifts through ACES and reads white.
				float drown = _platformEdge.Fbm(globalX / 42f + 17f, globalZ / 42f - 19f, 3);
				if (drown > .20f) return drown > .38f ? Palette.MOSS_STONE : Palette.STONE_WARM;
				return Palette.STONE_PALE;
			}
			PlanPoint planLocal = ToPlanLocal(globalX, globalZ);
			if (platform.Id is "gate-approach-dais" or "gate-approach-inner")
			{
				// Reference-6/8: the walking-distance dais is pale masonry rings
				// around the emblem, not a grass lawn with two posts on it.
				float daisBreak = _platformEdge.Fbm(globalX / 48f + 61f, globalZ / 48f - 67f, 3);
				// 0.40 punched a lawn through the rings (reference-8 is pale
				// masonry tiers with a frayed hem, not grass with two posts).
				if (daisBreak > .58f)
					return Palette.IsGrassSurface(naturalCap) ? naturalCap : Palette.GRASS;
				int daisRow = FloorDiv(globalZ, 9);
				int daisStagger = globalX + ((daisRow & 1) == 0 ? 0 : 6);
				bool daisJoint = PositiveModulo(daisStagger, 12) == 0 || PositiveModulo(globalZ, 9) == 0;
				return daisJoint ? Palette.STONE_WARM : Palette.STONE_PALE;
			}
			float pavingField = _platformEdge.Fbm(globalX / 34f - 11f, globalZ / 34f + 23f, 3);
			// References 2/5/9 are olive and tan courts with a stain of paving, not
			// a lavender plaza. A 16-block solid ribbon is the purple runway that
			// pitch-38 near reads as the grand stair (refs 6/8 are misty land
			// behind the shrine). Keep a broken 8-block stain.
			bool processionalAxis = Math.Abs(planLocal.X) <= 4;
			// 124/144 tops are land (reference-1's beige plateaus). A paving
			// ribbon on the Deck axis was a purple roof on the cliff. Keep
			// made ground only on the through-slot floor, where the traveller
			// walks the opening.
			bool slotFloor = platform.SurfaceY >= 120
				&& Math.Abs(planLocal.X) <= 9
				&& planLocal.Z >= _gatePlanZ - 4 && planLocal.Z <= _gatePlanZ + 8;
			bool keepTerrainCap = platform.Role switch
			{
				// Processional and low-field patches stay paving so breakup can
				// stain them (reference-7/10). The rest is dusty mauve court.
				PlanPlatformRole.Court => !processionalAxis && pavingField > .05f,
				// Residual pavingField holes on 114/124/144 were purple roofs
				// on otherwise tan land. Reference-1/2: high slabs are beige
				// plateaus; masonry lives only on the vertical faces and the
				// through-slot floor.
				PlanPlatformRole.Deck when platform.SurfaceY >= 120 => !slotFloor,
				PlanPlatformRole.Deck => !processionalAxis && pavingField > -.28f,
				PlanPlatformRole.Terrace when platform.SurfaceY >= 110 => true,
				PlanPlatformRole.Terrace => pavingField > -.38f,
				PlanPlatformRole.Slab when platform.SurfaceY >= 140 => true,
				// 104 mid-shelf: land up to the 124 toe. Axis paving here was the
				// 22-block purple carpet from the dais to the gate hole.
				PlanPlatformRole.Slab when platform.SurfaceY < 110 => true,
				PlanPlatformRole.Slab => !processionalAxis && pavingField > -.04f,
				PlanPlatformRole.Trace => pavingField > -.50f,
				_ => false,
			};
				if (keepTerrainCap)
			{
				// Reclamation belongs to broad surviving-ground patches. Sampling a
				// 72-block field preserves the authored court/axis while preventing the
				// salt-and-pepper moss that failed here three times before.
				float reclaim = _platformEdge.Fbm(globalX / 72f + 109f, globalZ / 72f - 103f, 3);
				// High reclamation used to threshold at ~0.05, so half the court
				// became moss and the rest stayed sage — a lawn. References 1/2/6
				// are dusty mauve courts with moss islands.
				float threshold = .50f - platform.Reclamation * .22f;
				if (platform.Reclamation > 0f && reclaim > threshold)
					return Palette.HasStoneSubstrate(naturalCap) ||
					       naturalCap is Palette.STONE or Palette.STONE_PALE or Palette.STONE_WARM
						? Palette.MOSS_STONE
						: Palette.MOSS;
				// Cream sand as a cap lifts through ACES. Raised plates are the
				// tan/olive plateau of reference-1/10. A mauve cap on 124/144
				// made the massif a palace roof; masonry lives on the vertical
				// faces and in the gate overlay.
				return Palette.IsGrassSurface(naturalCap) ? naturalCap : Palette.GRASS;
			}
			// Ragged paving: holes the size of a court, never per-block speckle.
			// 0.34 on the axis left too little paving for the meander stain
			// (reference-7/10). Off-axis stays greedy so courts don't tile.
			float breakup = _platformEdge.Fbm(globalX / 48f + 61f, globalZ / 48f - 67f, 3);
			if (breakup > (processionalAxis ? .10f : .08f))
			{
				if (Palette.IsGrassSurface(naturalCap)) return naturalCap;
				return breakup > .38f ? Palette.SAND : Palette.GRASS;
			}
			// Large staggered flagstone courses keep a district-sized plane from
			// greedily merging into one blank quad. The course grid belongs only to
			// made ground; natural terrain remains explicitly forbidden to use it.
			int row = FloorDiv(globalZ, 9);
			int staggeredX = globalX + ((row & 1) == 0 ? 0 : 6);
			bool joint = PositiveModulo(staggeredX, 12) == 0 || PositiveModulo(globalZ, 9) == 0;
			if (processionalAxis)
			{
				// Reference-1's approach is pale masonry, not a mauve carpet.
				// Routes used to restamp this ribbon as PAVING and undo the stain.
				return joint ? Palette.STONE_WARM : Palette.STONE_PALE;
			}
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
							// Buttresses on this face became the palisade. A cliff
							// keeps rubble at the toe, not attached columns.
							if (field > -.08f) BuildRubbleCluster(centre + outward * 2.5f, edgeIndex);
							if (field > .16f) BuildRubbleCluster(centre + outward * 4.2f, edgeIndex + 3);
							break;
						case PlanEdgeTreatment.Submerged:
							BuildEdgeButtress(centre + outward * 2f, outward, 3 + (field > .3f ? 2 : 0)); break;
						case PlanEdgeTreatment.Ragged:
							BuildRubbleCluster(centre + outward * (field > .15f ? 2f : -2f), edgeIndex); break;
						}
					}
				}
				foreach (PlanPlatformCutout cutout in platform.Cutouts)
					BuildCutoutRim(platform, cutout);
				BuildCourtRubble(platform, polygon);
			}
		}

		private void BuildTerraceApron(PlanPlatform platform, Vector2 centre,
			Vector2 outward, float field)
		{
			int width = platform.EdgeTreatment switch
			{
				PlanEdgeTreatment.Revetment => 10,
				PlanEdgeTreatment.PrecinctWall => 4,
				PlanEdgeTreatment.Submerged => 8,
				PlanEdgeTreatment.Ragged when field > .08f => 3,
				_ => 0,
			};
			if (width == 0) return;
			// Near/wide look from the drowned south-east. Any shelf on those faces
			// — masonry or soil — sits between the camera and the terrace and
			// reads as a palisade. Downhill of a revetment is the lower court or
			// the natural hillside (RuinKit), not a second wall.
			if (platform.EdgeTreatment == PlanEdgeTreatment.Revetment &&
			    (outward.X > .22f || outward.Y > .22f))
				return;

			for (int band = 1; band <= width; band++)
			{
				Vector2 point = centre + outward * band;
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				int index = z * _data.Width + x;
				int natural = _data.Height[index];
				if (_data.WaterSurface[index] > 0)
					natural = Math.Min(natural, Math.Max(_data.SeaLevel - 1,
						_data.WaterSurface[index] - 10));
				int existing = _grid.Top[index];
				// The upper-court apron was filling ten bands of the already-raised
				// forecourt, which is the 12-block palisade in every near capture.
				if (existing > natural + 2) continue;
				int baseY = Math.Max(natural, existing);
				int rise = platform.SurfaceY - baseY;
				// Two shelves, not a 1-block staircase. Ink on each 1-block riser
				// reads as a palisade at review distance (RUINS.md §5a: slabs).
				int drop = platform.EdgeTreatment == PlanEdgeTreatment.Revetment
					? (band <= 4 ? Math.Min(rise / 2, 6) : Math.Min(rise - 2, 10))
					: (band > width / 2 ? 1 : 0) + (band == width ? 1 : 0);
				if (platform.EdgeTreatment == PlanEdgeTreatment.Revetment)
					drop += field > .22f ? 1 : field < -.22f ? -1 : 0;
				int top = platform.SurfaceY - Math.Max(0, drop);
				if (top <= baseY + 1) continue;
				for (int y = baseY; y < top; y++)
				{
					byte material = y == top - 1 ? Palette.STONE_WARM
						: field > .30f && y == top - 2 ? Palette.MOSS_STONE
						: y > baseY && (y - baseY) % 5 == 0 ? Palette.STONE_WARM
						: Palette.STONE;
					Put(x, y, z, material);
				}
			}
		}

		private void BuildCourtRubble(PlanPlatform platform, List<Vector2> polygon)
		{
			if (platform.Role == PlanPlatformRole.Causeway) return;
			int minX = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.X)));
			int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.X)));
			int minZ = Math.Max(0, (int)MathF.Floor(polygon.Min(p => p.Y)));
			int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(polygon.Max(p => p.Y)));
			for (int z = minZ; z <= maxZ; z += 6)
			for (int x = minX; x <= maxX; x += 6)
			{
				var cell = new Vector2(x + .5f, z + .5f);
				if (!InsidePolygon(cell, polygon) || DistanceToEdges(cell, polygon) < 5f) continue;
				PlanPoint planLocal = ToPlanLocal(x + _data.OriginX, z + _data.OriginZ);
				// Reference-1/4's processional is a stain, not a rubble carpet.
				if (Math.Abs(planLocal.X) <= 18) continue;
				float gx = x + _data.OriginX, gz = z + _data.OriginZ;
				float field = _platformEdge.Fbm(gx / 48f + 71f, gz / 48f - 83f, 3);
				if (field < .22f) continue;
				BuildRubbleCluster(cell, x + z);
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
					// Cutout rims on a revetment are grass-cliff gashes, not a
					// colonnade. The columns re-drew the palisade around every bay.
					if (platform.EdgeTreatment == PlanEdgeTreatment.Revetment)
					{
						if (cutout.Role == PlanCutoutRole.Collapsed && field > -.02f)
						{
							Vector2 pitSide = platformSide == a ? b : a;
							BuildRubbleCluster(pitSide + direction * (edgeIndex % 2), edgeIndex);
						}
						continue;
					}
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
			// Reference-7/10 rubble is a few warm stones in the grass, not a
			// lavender cube carpet. Stacking pale stone here also marked every
			// court as "structure" and starved the grove.
			ReadOnlySpan<Vector2I> offsets = phase % 2 == 0
				? new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(0, 1) }
				: new[] { new Vector2I(0, 0), new Vector2I(-1, 1), new Vector2I(1, 0) };
			for (int i = 0; i < offsets.Length; i++)
			{
				int x = (int)MathF.Round(centre.X) + offsets[i].X;
				int z = (int)MathF.Round(centre.Y) + offsets[i].Y;
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				Put(x, floor, z, i == 0 ? Palette.STONE_WARM : Palette.RUBBLE);
			}
		}

		private void BuildStair(PlanStair stair)
		{
			// RUINS §5a / Massif: a stair is a notch bitten into the high slab,
			// one tread per block of travel. Interpolating height along the whole
			// authored segment raised a free-standing ramp in front of the 124
			// face; from the side that ramp is a vertical cutaway.
			int fromY = _platforms[stair.FromPlatformId].SurfaceY;
			int toY = _platforms[stair.ToPlatformId].SurfaceY;
			Vector2 low = LocalPoint(stair.From);
			Vector2 high = LocalPoint(stair.To);
			if (fromY > toY)
			{
				(low, high) = (high, low);
				(fromY, toY) = (toY, fromY);
			}
			int rise = toY - fromY;
			if (rise < 1) return;
			int stepX = 0, stepZ = 0;
			float dx = high.X - low.X, dz = high.Y - low.Y;
			if (MathF.Abs(dx) >= MathF.Abs(dz))
				stepX = Math.Sign(dx);
			else
				stepZ = Math.Sign(dz);
			if (stepX == 0 && stepZ == 0) return;
			int acrossX = -stepZ, acrossZ = stepX;
			int x = (int)MathF.Round(low.X), z = (int)MathF.Round(low.Y);
			int faceX = -1, faceZ = -1;
			int highEndX = (int)MathF.Round(high.X), highEndZ = (int)MathF.Round(high.Y);
			for (int guard = 0; guard < 256 && InWindow(x, z); guard++)
			{
				if (_grid.Top[z * _grid.Size + x] >= toY - 1)
				{
					faceX = x;
					faceZ = z;
					break;
				}
				if (Passed(x, z, highEndX, highEndZ, stepX, stepZ)) break;
				x += stepX;
				z += stepZ;
			}
			if (faceX < 0)
			{
				GD.Print($"[domain-blockout] stair '{stair.Id}' found no {toY} face on its path");
				return;
			}
			var cells = new HashSet<int>();
			for (int s = 1; s <= 3; s++)
				StampStairRow(faceX - stepX * s, faceZ - stepZ * s, acrossX, acrossZ,
					stair.Width, fromY, toY, cells, notch: false);
			for (int s = 0; s <= rise; s++)
				StampStairRow(faceX + stepX * s, faceZ + stepZ * s, acrossX, acrossZ,
					stair.Width, fromY + s, toY, cells, notch: true);
			GD.Print($"[domain-blockout] stair '{stair.Id}' notch {fromY}→{toY} at {faceX},{faceZ} " +
			         $"along {stepX},{stepZ}  {cells.Count} cells");
		}

		private static bool Passed(int x, int z, int endX, int endZ, int stepX, int stepZ)
		{
			if (stepX > 0 && x > endX) return true;
			if (stepX < 0 && x < endX) return true;
			if (stepZ > 0 && z > endZ) return true;
			if (stepZ < 0 && z < endZ) return true;
			return false;
		}

		/// <summary>
		/// Reference-6/8/9: walking-distance ground is the same pale stone as the
		/// kit. Atlas grass is sage; a rectangle of paving was the 104 carpet
		/// toward the gate. This stain is a ragged disk around the emblem, cut
		/// off north of plan z=16 so the massif approach stays land.
		/// </summary>
		private void StainPrecinctCourt()
		{
			Vector2 centre = LocalPoint(new PlanPoint { X = 0, Z = -8 });
			const int radius = 38;
			int minX = Math.Max(0, (int)MathF.Floor(centre.X - radius));
			int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(centre.X + radius));
			int minZ = Math.Max(0, (int)MathF.Floor(centre.Y - radius));
			int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(centre.Y + radius));
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
			{
				float dx = x + .5f - centre.X, dz = z + .5f - centre.Y;
				float d = MathF.Sqrt(dx * dx + dz * dz);
				if (d > radius) continue;
				int index = z * _grid.Size + x;
				if (_stairKeys.Contains(index)) continue;
				if (_data.Land[index] == 0 || _data.WaterSurface[index] > 0) continue;
				PlanPoint planLocal = ToPlanLocal(x + _data.OriginX, z + _data.OriginZ);
				if (planLocal.Z > 16 || Math.Abs(planLocal.X) > 46) continue;
				if (_grid.Top[index] >= 114) continue;
				byte existing = _grid.Cap[index];
				if (!Palette.IsGrassSurface(existing) && existing is not Palette.MOSS)
					continue;
				float gx = x + _data.OriginX, gz = z + _data.OriginZ;
				float field = _platformEdge.Fbm(gx / 36f + 21f, gz / 36f - 27f, 3);
				float edge = d / radius;
				if (edge > .52f && field < .06f) continue;
				if (field > .34f && edge > .18f) continue;
				byte cap = field > .08f ? Palette.STONE_PALE : Palette.PATH;
				// Reference-7/10: the walking-distance court is a meander stain,
				// not a blank flagstone pad. The dais platforms already inlay;
				// this is the ragged disk outside them.
				if (RuinKit.Meander(PositiveModulo(x + _data.OriginX, 6),
					PositiveModulo(z + _data.OriginZ, 8)))
					cap = Palette.RUBBLE;
				_grid.Cap[index] = cap;
			}
		}

		private void StampStairRow(int cx, int cz, int acrossX, int acrossZ, int width,
			int level, int highY, HashSet<int> cells, bool notch)
		{
			int half = (width - 1) / 2;
			for (int lane = 0; lane < width; lane++)
			{
				int offset = lane - half;
				int x = cx + acrossX * offset;
				int z = cz + acrossZ * offset;
				if (!InWindow(x, z)) continue;
				int key = z * _grid.Size + x;
				if (!cells.Add(key)) continue;
				int index = key;
				int natural = Math.Max(_data.Height[index], _data.SeaLevel + 1);
				int existing = _grid.Top[index];
				if (notch)
				{
					int target = Math.Min(existing, Math.Max(level, natural));
					if (target < existing || existing == level)
					{
						// Pitch-38 near looks down on the grand notch. PAVING
						// treads were a 14-wide purple roof on the 124. Reference-1
						// is a cut in tan land; masonry lives on the risers.
						byte cap = Palette.PAVING;
						if (highY >= 120)
						{
							byte existingCap = _grid.Cap[index];
							cap = Palette.IsGrassSurface(existingCap)
								? existingCap
								: Palette.GRASS;
						}
						_grid.RedescribeUnedited(x, z, target, cap,
							Palette.STONE_PALE, Palette.STONE_PALE);
						_stairKeys.Add(key);
						_stairCells++;
					}
				}
				else if (Math.Abs(existing - level) <= 1)
				{
					// The three-row apron sits in the pitch-38 frame. PAVING here
					// is the same purple carpet as the treads; massif landings
					// stay land, dais stairs keep made ground.
					byte cap = Palette.PAVING;
					if (highY >= 120)
					{
						byte existingCap = _grid.Cap[index];
						cap = Palette.IsGrassSurface(existingCap)
							? existingCap
							: Palette.GRASS;
					}
					_grid.Cap[index] = cap;
					_stairKeys.Add(key);
					_stairCells++;
				}
			}
			if (!notch) return;
			// Massif cheeks: one masonry column proud of each tread, but only
			// where the high slab already stands. Raising them on the 112 court
			// was a palisade beside a free-standing ramp.
			for (int side = -1; side <= 1; side += 2)
			{
				int x = cx + acrossX * (side * (half + 1));
				int z = cz + acrossZ * (side * (half + 1));
				if (!InWindow(x, z)) continue;
				int index = z * _grid.Size + x;
				int existing = _grid.Top[index];
				if (existing < highY - 1) continue;
				_grid.Deep[index] = Palette.STONE_PALE;
				_grid.Sub[index] = Palette.STONE_PALE;
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
				byte material = RouteMaterial(route);
				int width = Math.Max(1, (int)MathF.Round(route.Width));
				for (int i = 1; i < route.Points.Count; i++)
					DrawGroundLine(GlobalPoint(route.Points[i - 1]), GlobalPoint(route.Points[i]),
						width, material);
			}
		}

		private static byte RouteMaterial(CanonicalRoute route)
		{
			if (route.Kind == RoadKind.Trail) return Palette.PATH;
			if (route.Kind == RoadKind.Abandoned) return Palette.STONE_WARM;
			if (route.Construction.Contains("causeway", StringComparison.Ordinal) ||
			    route.Construction.Contains("drowned", StringComparison.Ordinal) ||
			    route.Construction.Contains("paving", StringComparison.Ordinal) ||
			    route.Construction.Contains("stone", StringComparison.Ordinal))
				return Palette.STONE_PALE;
			return Palette.PAVING;
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
					// Massif courts stay the authored cap. A 12-wide paving stamp
					// over the 124 slot was a lavender carpet through the gate.
					// The processional also restamped every notch tread below 120
					// (the grand flight is 104–123), hiding the land cap under
					// STONE_PALE edits. Stair footprints keep the notch material.
					if (_stairKeys.Contains(key)) continue;
					if (_grid.Top[key] >= 120) continue;
					byte cap = _grid.Cap[key];
					// Authored land caps win. Stamping STONE_PALE on grass put a
					// 6-wide carpet on the 104 shelf and the massif notch.
					if (Palette.IsGrassSurface(cap) || cap is Palette.MOSS or Palette.MOSS_STONE)
						continue;
					float gx = x + _data.OriginX, gz = z + _data.OriginZ;
					float field = _platformEdge.Fbm(gx / 48f + 11f, gz / 48f - 17f, 2);
					// Outer lanes fray so a Major route is a stain, not a ruler.
					float edge = MathF.Abs(offset) / Math.Max(1f, (width - 1) * .5f);
					if (edge > .35f && field > .04f) continue;
					if (field > .42f) continue;
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
			int height = StandingWallHeight(wall);
			for (int part = 1; part < wall.Points.Count; part++)
			{
				Vector2 from = LocalPoint(wall.Points[part - 1]);
				Vector2 to = LocalPoint(wall.Points[part]);
				if (from.DistanceTo(to) < .1f) continue;
				float radius = height >= 8 ? 2.05f : 1.15f;
				int minX = Math.Max(0, (int)MathF.Floor(Math.Min(from.X, to.X) - radius - 1));
				int maxX = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(Math.Max(from.X, to.X) + radius + 1));
				int minZ = Math.Max(0, (int)MathF.Floor(Math.Min(from.Y, to.Y) - radius - 1));
				int maxZ = Math.Min(_grid.Size - 1, (int)MathF.Ceiling(Math.Max(from.Y, to.Y) + radius + 1));
				// Shared crest only diagnoses a terrace drop. Stacking from the
				// shelf up to that crest was a palisade; capping every downhill
				// cell at two blocks made reference-9's woods runs disappear.
				int crestBase = -1;
				for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++)
				{
					if (DistToSegment(new Vector2(x + .5f, z + .5f), from, to) > radius) continue;
					crestBase = Math.Max(crestBase, _grid.Top[z * _grid.Size + x]);
				}
				if (crestBase < 0) continue;
				int gapSeed = 0;
				for (int z = minZ; z <= maxZ; z++)
				for (int x = minX; x <= maxX; x++)
				{
					var cell = new Vector2(x + .5f, z + .5f);
					if (DistToSegment(cell, from, to) > radius) continue;
					if (openings.Any(o => o.DistanceTo(cell) <= Math.Max(5f, radius * 3f))) continue;
					PlanPoint planLocal = ToPlanLocal(x + _data.OriginX, z + _data.OriginZ);
					// Precinct walls stay off the processional. A Trace ring around
					// the approach emblem has to cross the axis or it becomes two
					// parallel curbs (the near-shot boulevard).
					if (Math.Abs(planLocal.X) <= 9 && wall.State != PlanWallState.Trace) continue;
					float gx = x + _data.OriginX, gz = z + _data.OriginZ;
					float gapField = _platformEdge.Fbm(gx / 72f + 13f, gz / 72f - 19f, 1);
					// 0.30 left continuous runs; 0.18 still read as a trimmed
					// palisade. 0.10 left the precinct-link walls too intact
					// against reference-9. Trace rings use a milder cut so the
					// dais still reads as a ring (reference-8) rather than two
					// curbs. The field wavelength is /72, not per-block.
					bool gapped = wall.State == PlanWallState.Broken && gapField > .08f
						|| wall.State == PlanWallState.Trace && gapField > .24f;
					if (gapped)
					{
						if (gapSeed++ % 3 == 0) BuildRubbleCluster(cell, x + z);
						// Reference-9: a failed stretch still leaves 2-high
						// stumps, not a clean missing run. The .08–.20 band is
						// the collapse hem; above that the wall is gone.
						if (wall.State == PlanWallState.Broken && gapField < .20f
						    && gapSeed % 3 == 1)
						{
							int stumpFloor = _grid.Top[z * _grid.Size + x];
							FillWallColumn(x, z, stumpFloor + 2, material);
						}
						continue;
					}
					int floor = _grid.Top[z * _grid.Size + x];
					int localDrop = crestBase - floor;
					int columnHeight = localDrop > 8 ? Math.Min(3, height) : height;
					if (wall.State == PlanWallState.Broken)
						columnHeight = Math.Max(2, columnHeight - (int)((gapField * .5f + .5f) * 3f));
					FillWallColumn(x, z, floor + columnHeight, material);
					if (gapField > .12f && DistToSegment(cell, from, to) < radius * .55f)
						Put(x, floor, z, Palette.RUBBLE);
				}
			}
			PostWallStelae(wall, openings);
		}

		/// <summary>
		/// Reference-9: stelae posted along precinct walls, repeating between
		/// authored endpoints. Not a new site — the wall is the composition.
		/// </summary>
		private void PostWallStelae(PlanWall wall, Vector2[] openings)
		{
			// Reference-9: stelae belong on the long precinct links through the
			// woods. Grove and causeway runs at height 8 were posting a 15-block
			// pylon every 44 blocks and read as a colonnade from wide/far.
			if (wall.Id is not ("west-precinct-link" or "east-precinct-link")) return;
			if (StandingWallHeight(wall) < 7) return;
			for (int part = 1; part < wall.Points.Count; part++)
			{
				Vector2 from = LocalPoint(wall.Points[part - 1]);
				Vector2 to = LocalPoint(wall.Points[part]);
				float length = from.DistanceTo(to);
				if (length < 40f) continue;
				Vector2 direction = (to - from) / length;
				float degrees = MathF.Atan2(direction.X, direction.Y) * (180f / MathF.PI);
				for (float along = 24f; along < length - 20f; along += 88f)
				{
					Vector2 point = from + direction * along;
					if (openings.Any(o => o.DistanceTo(point) < 18f)) continue;
					int gx = (int)MathF.Round(point.X) + _data.OriginX;
					int gz = (int)MathF.Round(point.Y) + _data.OriginZ;
					PlanPoint planLocal = ToPlanLocal(gx, gz);
					if (Math.Abs(planLocal.X) <= 12) continue;
					int index = (int)along / 88;
					// 15-block pylons every 44 were a second skyline from 520u
					// (reference-1/2 are a cliff with a gate, not a colonnaded
					// city). Height 8 every 88 still posts reference-9's wall
					// stelae without topping the 124.
					BuildPylon(point, 8 + index % 2, degrees, false, 0);
				}
			}
		}

		private void FillWallColumn(int x, int z, int crest, byte material)
		{
			if (!InWindow(x, z)) return;
			int floor = _grid.Top[z * _grid.Size + x];
			if (floor >= crest) return;
			for (int y = floor; y < crest; y++)
			{
				bool cap = y >= crest - 1;
				bool band = y == crest - 2;
				bool course = y > floor && (y - floor) % 5 == 0;
				Put(x, y, z, cap || band || course ? Palette.STONE_WARM : material);
			}
		}

		private static int StandingWallHeight(PlanWall wall) => wall.State switch
		{
			// Authored height 2 on the dais ring was collapsed to a one-block
			// curb. Reference-8's ring is a low ruined wall, not a kerb.
			PlanWallState.Trace => Math.Clamp(wall.Height, 1, 3),
			PlanWallState.Stub => Math.Min(wall.Height, 4),
			_ => wall.Height,
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
						landmark.State == PlanLandmarkState.Standing, landmark.Span); break;
				case PlanLandmarkKind.Arch:
					BuildArch(centre, landmark.Height, landmark.Span, orientation, landmark.State); break;
				case PlanLandmarkKind.Gate:
					BuildGate(centre, landmark.Height, landmark.Span, orientation, landmark.State); break;
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
			// Reference-10: moss on the outer skin of weathered shafts, a
			// 28-block field so neighbouring columns share an age.
			float moss = _platformEdge.Fbm(cx / 28f + 9f, cz / 28f - 11f, 2);
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++) Put(cx + dx, floor, cz + dz, Palette.STONE_PALE);
			for (int y = 1; y < height - (capital ? 2 : 0); y++)
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++)
			{
				if (dx != 0 && dz != 0) continue;
				// Reference-6/7: a crystal core, seen through a south groove.
				// Filling the south skin hid the core; skipping the core left
				// a pale chimney.
				if (dx == 0 && dz == 0)
				{
					Put(cx, floor + y, cz, y % 3 == 0 ? Palette.STONE_WARM : Palette.CRYSTAL);
					continue;
				}
				if (dz == 1 && dx == 0 && y >= 3 && y < height - 3)
					continue;
				// Reference-6/10: east/west skins carry the meander so a
				// walking-distance shaft is inscribed, not a blank chimney.
				bool motif = y >= 4 && y < height - 3 && RuinKit.Meander(dx + 1, y);
				byte skin = motif ? Palette.STONE
					: !capital && moss > .14f && y % 4 == 2 ? Palette.MOSS_STONE
					: y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE;
				Put(cx + dx, floor + y, cz + dz, skin);
			}
			if (!capital) return;
			for (int y = height - 2; y < height; y++)
			for (int dz = -2; dz <= 2; dz++)
			for (int dx = -2; dx <= 2; dx++)
				if (Math.Abs(dx) + Math.Abs(dz) <= 3)
					Put(cx + dx, floor + y, cz + dz, y == height - 1 ? Palette.STONE_WARM : Palette.STONE_PALE);
		}

		private void BuildPylon(Vector2 centre, int height, float degrees, bool intact, int span)
		{
			int floor = FootprintFloor((int)MathF.Round(centre.X), (int)MathF.Round(centre.Y), 4);
			if (floor < 0) return;
			// Span marks an inscribed stele (reference-8): a solid camera face
			// with the meander recessed one block, not a crystal groove. Other
			// pylons keep the reference-6 core behind a skipped +forward skin.
			bool inscribed = span >= 4;
			for (int y = 0; y < height; y++)
			{
				int halfRight = y < 2 || (intact && y >= height - 2) || inscribed ? 2 : 1;
				int halfForward = y == 0 || inscribed ? 2 : 1;
				for (int f = -halfForward; f <= halfForward; f++)
				for (int r = -halfRight; r <= halfRight; r++)
				{
					if (!intact && y > height - 3 && r + f > 1) continue;
					Vector2 point = Oriented(centre, r, f, degrees);
					if (!inscribed && intact && r == 0 && f == halfForward && y >= 3 && y < height - 3)
						continue;
					bool core = !inscribed && intact && r == 0 && f == 0 && y >= 3 && y < height - 2;
					bool motif = f == halfForward && y >= 4 && y < height - 3 &&
					             (inscribed
						             ? RuinKit.SteleInscription(r + halfRight, y - 4,
							             halfRight * 2 + 1, height - 7)
						             : Math.Abs(r) <= 1 && RuinKit.Meander(r + 1, y));
					if (inscribed)
					{
						// Reference-8's walking-distance stele is a solid face
						// with a dark meander. Skipping those cells (the 240-unit
						// ink recess) reads as a window grid at 150 units.
						// STONE on STONE_PALE is ~20 RGB and vanishes at 118;
						// rubble is the next darker masonry, not a second palette.
						byte inscribedMat = motif ? Palette.RUBBLE
							: y > 0 && y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE;
						Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
							inscribedMat);
						continue;
					}
					// RuinKit.Pylon / RUINS §9: pick the meander one block deep
					// so ink has a recess. Painted warm-stone on a flat face
					// vanished at 240 units.
					if (motif && !core) continue;
					byte material = core ? Palette.CRYSTAL
						: y > 0 && y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE;
					Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y), material);
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
				// Reference-7's vertical purple recesses: skip the camera-facing
				// skin of the pier so ink has a groove to draw.
				if (f == 2 && r == 0 && y >= 3 && y < spring - 1) continue;
				Vector2 point = Oriented(centre, side * pierOffset + r, f, degrees);
				bool core = f == 1 && r == 0 && y >= 3 && y < spring - 1;
				Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
					core ? Palette.CRYSTAL
					: y > 0 && y % 5 == 0 ? Palette.STONE_WARM : Palette.STONE_PALE);
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

		/// <summary>
		/// Reference-1/2 temple front: two piers, a heavy lintel and a through
		/// opening. A crystal veil in the slot read as a magenta door; a solid
		/// back wall on the +forward face read as a pale box. The opening stays
		/// empty so the isometric camera sees landscape through it.
		/// </summary>
		private void BuildGate(Vector2 centre, int height, int span, float degrees,
			PlanLandmarkState state)
		{
			int cx = (int)MathF.Round(centre.X);
			int cz = (int)MathF.Round(centre.Y);
			// The 144 cheeks around the slot make a radius-max floor sit on the
			// crown. The opening is at the gate platform, so use the centre.
			int floor = InWindow(cx, cz) ? _grid.HeightAt(cx, cz) : -1;
			if (floor < 0) return;
			int openingHalf = Math.Max(5, span / 2);
			// Full-height piers at depth 3 were a temple facade on the 124
			// face. Reference-1's slot is a hole in the cliff: the 144 cheeks
			// are the mass, overlay is a one-block jamb and a flush lintel.
			int pier = 1;
			int outer = openingHalf + pier;
			int depth = 1;
			// Height 32 from the 124 floor stuck a 12-block roof into the sky
			// above the 144 cap — a temple on the cliff. The lintel *is* the
			// 144 seating, four blocks of overlay flush with that cap.
			int crownY = 144;
			foreach (PlanPlatform platform in _plan.Platforms)
				if (platform.SurfaceY >= 140 && platform.SurfaceY < crownY)
					crownY = platform.SurfaceY;
			height = Math.Min(height, Math.Max(18, crownY - floor));
			int openingHeight = Math.Max(12, height - 4);
			for (int y = 0; y < height; y++)
			for (int r = -outer; r <= outer; r++)
			for (int f = -depth; f <= depth; f++)
			{
				bool inOpening = Math.Abs(r) < openingHalf && y >= 1 && y < openingHeight;
				bool inJamb = Math.Abs(r) == openingHalf && y < openingHeight;
				bool inLintel = y >= openingHeight && Math.Abs(r) <= outer;
				if (inOpening) continue;
				if (!inJamb && !inLintel) continue;
				if (state == PlanLandmarkState.Broken && y >= height - 4 && r > openingHalf / 2 && f > 0)
					continue;
				bool roof = y >= height - 2;
				bool course = y > 0 && y % 5 == 0;
				bool innerFace = Math.Abs(r) == openingHalf && y >= 4 && y < openingHeight;
				bool glyph = innerFace && RuinKit.Meander(PositiveModulo(f + depth, 3), y);
				bool crystal = innerFace && f == 0 && y % 3 != 0;
				byte material = crystal ? Palette.CRYSTAL
					: roof || glyph || course ? Palette.STONE_WARM : Palette.STONE_PALE;
				Vector2 point = Oriented(centre, r, f, degrees);
				Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y), material);
			}

			// No roof tower. The 144 cheeks are the flanking mass (reference-1).
			// A 12-block shaft on the west pier read as a palace turret on the
			// cliff, which is the near-shot failure every time it was present.

			// Seat the gate in the hillside behind the piers. Combined orientation
			// 0° points +forward at the approach, so negative forward is the cliff.
			// The opening itself stays empty so the camera reads landscape through
			// it (reference-1/2) instead of a plugged pale box.
			// The 144 wrap is only the south face. Extra plug stone turned the
			// slot into a masonry corridor; the cheeks are the seating.
			int plug = 0;
			for (int y = 0; y < height - 3; y++)
			for (int r = -outer + 1; r <= outer - 1; r++)
			for (int f = -depth - 1; f >= -depth - plug; f--)
			{
				if (Math.Abs(r) < openingHalf && y < openingHeight) continue;
				int inset = -depth - 1 - f;
				if (Math.Abs(r) > outer - 2 - inset / 3) continue;
				if (state == PlanLandmarkState.Broken && y > height / 2 && r > 3 && inset > 7)
					continue;
				bool course = y > 0 && y % 5 == 0;
				Vector2 point = Oriented(centre, r, f, degrees);
				Put((int)MathF.Round(point.X), floor + y, (int)MathF.Round(point.Y),
					course ? Palette.STONE_WARM : Palette.STONE);
			}

			for (int side = -1; side <= 1; side += 2)
			{
				Vector2 cheek = Oriented(centre, side * (openingHalf + 3), depth + 2, degrees);
				BuildRubbleCluster(cheek, side + 3);
			}
		}

		private void BuildLandmarkRubble(PlanLandmark landmark)
		{
			if (landmark.Kind is PlanLandmarkKind.Emblem or PlanLandmarkKind.Basin) return;
			Vector2 centre = LocalPoint(landmark.Point);
			int phase = (int)((uint)Rng.StableHash(landmark.Id) & 7);
			BuildRubbleCluster(centre + new Vector2(3, -2), phase);
			BuildRubbleCluster(centre + new Vector2(-4, 1), phase + 3);
			if (landmark.Kind is PlanLandmarkKind.Pylon or PlanLandmarkKind.Column
			    or PlanLandmarkKind.FallenColumn or PlanLandmarkKind.Gate)
				BuildRubbleCluster(centre + new Vector2(-2, 3), phase + 1);
		}

		private void BuildCausewayPiers(PlanPlatform platform)
		{
			// Authored water pylons are the drowned posts. Generated rails and
			// stems were the crenellated boulevard in every wide/far frame.
			_ = platform;
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
			if (landmark.State == PlanLandmarkState.Standing)
				BuildColonnadeLintel(centre, landmark, degrees);
		}

		private void BuildStylobate(Vector2 centre, int length, float degrees)
		{
			// A one-block step under the columns. The previous 7-wide two-high bar
			// read as a palisade wall at every review distance.
			for (int along = -length / 2; along <= length / 2; along++)
			for (int right = -1; right <= 1; right++)
			{
				Vector2 point = Oriented(centre, right, along, degrees);
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				int floor = _grid.HeightAt(x, z);
				Put(x, floor, z, right == 0 ? Palette.STONE_PALE : Palette.STONE_WARM);
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
			// RuinKit's fallen column: a plinth where it stood, drums with gaps
			// and a slight drift, capital past the end. A 2-high 3-wide bar on
			// the cap reads as paving from the near camera.
			int shaft = Math.Max(12, length);
			Vector2 plinth = Oriented(centre, 0f, -(shaft - 1) * .5f - 2f, degrees);
			int px = (int)MathF.Round(plinth.X), pz = (int)MathF.Round(plinth.Y);
			int plinthFloor = FootprintFloor(px, pz, 2);
			if (plinthFloor >= 0)
			{
				for (int r = -1; r <= 1; r++)
				for (int f = -1; f <= 1; f++)
				{
					Vector2 point = Oriented(plinth, r, f, degrees);
					int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
					if (!InWindow(x, z)) continue;
					Put(x, plinthFloor, z, Palette.STONE);
					if (r == 0 || f == 0)
						Put(x, plinthFloor + 1, z, Palette.STONE_PALE);
				}
			}
			int gapA = shaft / 3, gapB = (2 * shaft) / 3;
			for (int i = 0; i < shaft; i++)
			{
				if (i == gapA || i == gapB) continue;
				float drift = i >= gapB ? 1f : 0f;
				for (int across = 0; across <= 1; across++)
				{
					Vector2 point = Oriented(centre, across + drift, i - (shaft - 1) * .5f, degrees);
					int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
					if (!InWindow(x, z)) continue;
					int floor = _grid.HeightAt(x, z);
					for (int k = 0; k <= 1; k++)
					{
						byte material = k == 1 && i % 5 == 2 ? Palette.CRYSTAL
							: i % 5 == 0 ? Palette.STONE_WARM
							: k == 1 ? Palette.STONE_PALE : Palette.STONE;
						Put(x, floor + k, z, material);
					}
				}
			}
			Vector2 capital = Oriented(centre, 0.5f, (shaft - 1) * .5f + 3f, degrees);
			int cx = (int)MathF.Round(capital.X), cz = (int)MathF.Round(capital.Y);
			int capitalFloor = FootprintFloor(cx, cz, 1);
			if (capitalFloor < 0) return;
			for (int r = -1; r <= 1; r++)
			for (int f = -1; f <= 1; f++)
			{
				Vector2 point = Oriented(capital, r, f, degrees);
				int x = (int)MathF.Round(point.X), z = (int)MathF.Round(point.Y);
				if (!InWindow(x, z)) continue;
				Put(x, capitalFloor, z, Palette.STONE_WARM);
				if (r == 0 && f == 0) Put(x, capitalFloor + 1, z, Palette.STONE_WARM);
			}
		}

		private void BuildEmblem(Vector2 centre, int span, string platformId)
		{
			int radius = Math.Max(3, span / 2);
			int cx = (int)MathF.Round(centre.X), cz = (int)MathF.Round(centre.Y);
			int floor = InWindow(cx, cz) ? _grid.HeightAt(cx, cz) : _platforms[platformId].SurfaceY;
			for (int z = -radius; z <= radius; z++)
			for (int x = -radius; x <= radius; x++)
			{
				float d = MathF.Sqrt(x * x + z * z);
				if (d > radius) continue;
				float field = _platformEdge.Fbm((cx + x + _data.OriginX) / 22f + 4f,
					(cz + z + _data.OriginZ) / 22f - 6f, 2);
				if (d > radius * .78f && field < .04f) continue;
				bool hub = d <= 1.8f;
				bool inner = MathF.Abs(d - radius * .38f) < 1.15f;
				bool mid = MathF.Abs(d - radius * .62f) < 1.15f;
				bool outer = MathF.Abs(d - radius * .88f) < 1.2f;
				if (!hub && !inner && !mid && !outer) continue;
				byte inlaid = hub ? Palette.CRYSTAL
					: inner || (mid && field > .04f) ? Palette.STONE_WARM : Palette.STONE_PALE;
				Put(cx + x, floor - 1, cz + z, inlaid);
				if (d <= radius * .52f)
					Put(cx + x, floor, cz + z, hub && d < 1.1f ? Palette.CRYSTAL : Palette.STONE_WARM);
			}
		}

		private void BuildBasin(Vector2 centre, int span, string platformId)
		{
			int radius = Math.Max(2, span / 2);
			int cx = (int)MathF.Round(centre.X), cz = (int)MathF.Round(centre.Y);
			int floor = InWindow(cx, cz) ? _grid.HeightAt(cx, cz) : _platforms[platformId].SurfaceY;
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
			_ = radius;
			if (!InWindow(cx, cz)) return -1;
			return _grid.HeightAt(cx, cz);
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

		private static float DistToSegment(Vector2 point, Vector2 a, Vector2 b)
		{
			Vector2 edge = b - a;
			float lengthSquared = edge.LengthSquared();
			float t = lengthSquared <= .0001f ? 0f
				: Math.Clamp((point - a).Dot(edge) / lengthSquared, 0f, 1f);
			return point.DistanceTo(a + edge * t);
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

		private readonly record struct TerrainCourt(
			List<Vector2> Local, float MinPlanZ, float MaxPlanZ, bool OpensSouth);

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
