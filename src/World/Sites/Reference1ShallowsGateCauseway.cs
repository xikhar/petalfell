using System;
using System.Collections.Generic;
using System.Linq;
using Petalfell.Core;

namespace Petalfell.World.Sites;

/// <summary>
/// Literal voxel transcription of world-new/reference-1.png and its measured
/// overhead registration.
///
/// The ground plan owns every X/Z cell. This class owns only the vertical
/// schedule, damage, materials and source-visible blocks. Fill is low-level
/// storage shorthand inside one explicitly traced mass; it is never a reusable
/// stair, portal, column, wall or ruin stamp.
/// </summary>
public static class Reference1ShallowsGateCauseway
{
	public const string BuilderId = "reference-1-gate-and-causeway-v1";
	private const int RuntimePlanScale = 3;
	// The measured water surface remains locked to the production atlas at TopY
	// 105. At three runtime voxels per source course, threshold TopY must therefore
	// be 105 - 3*(-21) = 168. AuthoredRuntimeHeight supplies headroom above the
	// untouched 192-block natural-terrain envelope; moving the datum down would
	// instead create an enormous false water/cliff seam around the reconstruction.
	private const int MeasuredVerticalDatumY = 168;
	private const int MeasuredWaterSurfaceLocalY = -21;
	private const int MeasuredWaterBedLocalY = -23;
	private const byte HydrologyDry = 0;
	private const byte HydrologyChannel = 3;

	public static ReferenceSiteStatistics Build(AtlasSectorWindow window,
		ReferenceSiteDefinition site)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (site == null) throw new ArgumentNullException(nameof(site));
		if (site.BuilderId != BuilderId)
			throw new InvalidOperationException($"Reference 1 cannot build '{site.BuilderId}'.");
		if (site.RuntimePlanScale != RuntimePlanScale)
			throw new InvalidOperationException(
				$"Reference 1 requires integer runtimePlanScale {RuntimePlanScale}, got {site.RuntimePlanScale}.");
		if (site.VerticalDatumY != MeasuredVerticalDatumY)
			throw new InvalidOperationException(
				$"Reference 1 was measured at verticalDatumY {MeasuredVerticalDatumY}, got {site.VerticalDatumY?.ToString() ?? "null"}.");

		ReferenceSiteGroundPlan plan = ReferenceSiteGroundPlan.Load(site);
		var build = new Blueprint(window, site, plan, site.VerticalDatumY.Value);
		build.Write();
		return new ReferenceSiteStatistics(build.SurfaceCells, build.Voxels);
	}

	private sealed class Blueprint
	{
		private readonly ReferenceSiteDefinition _site;
		private readonly ReferenceSiteGroundPlan _plan;
		private readonly VoxelGrid _grid;
		private readonly AtlasSectorData _data;
		private readonly int _verticalDatum;
		private string _activeStructure = "";
		private HashSet<(int X, int Z)> _activeProjection;
		private HashSet<(int X, int Z)> _activeTouched;

		private readonly struct TerrainMaterialStyle
		{
			public readonly byte Cap;
			public readonly byte Sub;
			public readonly byte Deep;

			public TerrainMaterialStyle(byte cap, byte sub, byte deep)
			{
				Cap = cap;
				Sub = sub;
				Deep = deep;
			}
		}

		public int SurfaceCells { get; private set; }
		public int Voxels { get; private set; }

		public Blueprint(AtlasSectorWindow window, ReferenceSiteDefinition site,
			ReferenceSiteGroundPlan plan, int verticalDatum)
		{
			_site = site;
			_plan = plan;
			_grid = window.Grid;
			_data = window.Data;
			_verticalDatum = verticalDatum;
		}

		public void Write()
		{
			AssertRuntimeEnvelope();
			// Whole columns and exact treads must be authored before sparse edits:
			// VoxelGrid deliberately rejects a terrain replacement after its 32-block
			// tile has received an architectural voxel.
			WritePlannedTerrainAndStairs();
			WriteAuthoredSurfaceWear();
			WriteCausewayPavingJoints();

			WriteNamed("gate-connected-body-west", WriteGateConnectedBodyWest);
			WriteNamed("gate-connected-body-east", WriteGateConnectedBodyEast);
			WriteNamed("gate-elevated-roof", WriteGateElevatedRoof);
			WriteNamed("gate-west-attached-buttress", WriteGateWestAttachedButtress);
			WriteNamed("gate-east-attached-buttress", WriteGateEastAttachedButtress);

			WriteNamed("causeway-west-parapet-north", WriteCausewayWestParapetNorth);
			WriteNamed("causeway-west-parapet-middle", WriteCausewayWestParapetMiddle);
			WriteNamed("causeway-west-parapet-south", WriteCausewayWestParapetSouth);
			WriteNamed("causeway-east-parapet-north", WriteCausewayEastParapetNorth);
			WriteNamed("causeway-east-parapet-middle", WriteCausewayEastParapetMiddle);
			WriteNamed("causeway-east-parapet-south", WriteCausewayEastParapetSouth);
			WriteNamed("causeway-west-side-supports", WriteCausewayWestSideSupports);
			WriteNamed("causeway-east-side-supports", WriteCausewayEastSideSupports);
			WriteNamed("causeway-south-abutment-west-drop", WriteSouthAbutmentWestDrop);
			WriteNamed("causeway-south-abutment-east-drop", WriteSouthAbutmentEastDrop);
			WriteNamed("causeway-north-deck-plinth", WriteCausewayNorthDeckPlinth);
			WriteNamed("causeway-south-east-post", WriteCausewaySouthEastPost);
			WriteNamed("causeway-mid-rubble", WriteCausewayMidRubble);

			WriteNamed("west-inner-cliff-range", WriteWestInnerCliffRange);
			WriteNamed("west-middle-parallel-range", WriteWestMiddleParallelRange);
			WriteNamed("west-outer-broken-range", WriteWestOuterBrokenRange);
			WriteNamed("west-north-rib-west", WriteWestNorthRibWest);
			WriteNamed("west-north-rib-middle", WriteWestNorthRibMiddle);
			WriteNamed("west-north-rib-east", WriteWestNorthRibEast);
			WriteNamed("west-court-survivor", WriteWestCourtSurvivor);
			WriteNamed("west-threshold-survivor", WriteWestThresholdSurvivor);
			WriteNamed("west-threshold-rubble", WriteWestThresholdRubble);
			WriteNamed("west-middle-rubble", WriteWestMiddleRubble);
			WriteNamed("west-outer-rubble", WriteWestOuterRubble);
			WriteNamed("west-north-rubble", WriteWestNorthRubble);

			WriteNamed("east-threshold-west-rib", WriteEastThresholdWestRib);
			WriteNamed("east-inner-west-rib", WriteEastInnerWestRib);
			WriteNamed("east-inner-middle-rib", WriteEastInnerMiddleRib);
			WriteNamed("east-inner-east-rib", WriteEastInnerEastRib);
			WriteNamed("east-outer-broken-range", WriteEastOuterBrokenRange);
			WriteNamed("east-north-broken-room", WriteEastNorthBrokenRoom);
			WriteNamed("east-north-room-survivor", WriteEastNorthRoomSurvivor);
			WriteNamed("east-inner-survivor", WriteEastInnerSurvivor);
			WriteNamed("east-outer-survivor", WriteEastOuterSurvivor);
			WriteNamed("east-threshold-rubble", WriteEastThresholdRubble);
			WriteNamed("east-inner-rubble", WriteEastInnerRubble);
			WriteNamed("east-outer-rubble", WriteEastOuterRubble);
			WriteNamed("east-north-rubble", WriteEastNorthRubble);

			WriteAuthoredStoneWeathering();
			WriteSurroundingTrees();
		}

		private void AssertRuntimeEnvelope()
		{
			// Fail before the first write. Silent clipping produced convincing hero
			// angles of incomplete sites in earlier passes and is especially dangerous
			// now that one source cell owns 27 runtime voxels.
			int minSolidY = Y(MeasuredWaterBedLocalY) - 1;
			int maxScheduledY = Y(24) + RuntimePlanScale - 1;
			if (minSolidY < 0 || maxScheduledY >= _grid.Height)
				throw new InvalidOperationException(
					$"Reference 1 runtime Y envelope {minSolidY}..{maxScheduledY} leaves grid height {_grid.Height}.");

			PlanPoint min = _site.RuntimeFootprintMin;
			PlanPoint max = _site.RuntimeFootprintMax;
			foreach (PlanPoint runtimeCorner in new[]
			         {
				         new PlanPoint { X = min.X, Z = min.Z },
				         new PlanPoint { X = min.X, Z = max.Z },
				         new PlanPoint { X = max.X, Z = min.Z },
				         new PlanPoint { X = max.X, Z = max.Z },
			         })
			{
				BlockPoint global = _site.ToGlobalRuntime(runtimeCorner);
				int x = global.X - _data.OriginX;
				int z = global.Z - _data.OriginZ;
				if (x < 0 || z < 0 || x >= _data.Width || z >= _data.Depth)
					throw new InvalidOperationException(
						$"Reference 1 runtime footprint corner {runtimeCorner.X},{runtimeCorner.Z} " +
						$"leaves materialised window {_data.OriginX},{_data.OriginZ} + {_data.Width}x{_data.Depth}.");
			}
		}

		private void WritePlannedTerrainAndStairs()
		{
			AssertMeasuredContextPlan();
			foreach (ReferenceGroundPlanTerrain terrain in _plan.Terrain)
			{
				// The v2 top comparison proved that preserving the unrelated compiler
				// terrain inside this measured crop can register the monument correctly
				// while showing the wrong site. Paint the traced dry context first, then
				// the three traced water shapes, then the later shelf/deck overrides.
				if (terrain.WriteMode == "preserve-atlas") continue;
				if (terrain.WriteMode == "author-water")
				{
					WriteAuthoredWater(terrain);
					continue;
				}
				if (terrain.WriteMode != "author-surface")
					throw new InvalidOperationException(
						$"Reference 1 has no terrain write mode '{terrain.WriteMode}'.");
				int localTop = terrain.SurfaceY ?? throw new InvalidOperationException(
					$"Terrain '{terrain.Id}' has no audited surfaceY.");
				TerrainMaterialStyle style = ResolveTerrainMaterialStyle(terrain.Material);
				if (terrain.Footprint.Count == 4)
					TerrainRectangle(terrain.Footprint, localTop, style);
				else
					TerrainPolygon(terrain.Polygon, localTop, style);
			}

			TerrainMaterialStyle stairStyle = ResolveTerrainMaterialStyle("worn-paving");
			foreach (ReferenceGroundPlanStructure stair in _plan.Structures)
			{
				if (stair.Kind != "stair") continue;
				int ascendingZDirection = Math.Sign(
					stair.Treads[^1].Footprint[1] - stair.Treads[0].Footprint[1]);
				if (ascendingZDirection == 0)
					throw new InvalidOperationException(
						$"Reference 1 stair '{stair.Id}' has no source-plan Z ascent.");
				for (int treadIndex = 0; treadIndex < stair.Treads.Count; treadIndex++)
				{
					ReferenceGroundPlanTread tread = stair.Treads[treadIndex];
					// The plan records VoxelGrid.Top, not the solid tread voxel. The six
					// source rises now expand into three ordinary one-voxel risers rather
					// than one unplayable three-voxel jump. The first source tread remains
					// flush with its lower landing; every later cell reaches its named TopY
					// at the ascending edge and therefore meets the upper platform exactly.
					TerrainStairRectangle(tread.Footprint, tread.TopY!.Value, stairStyle,
						ascendingZDirection, treadIndex == 0);
				}
			}

			AssertAuthoredWetDryOwnership();
		}

		private void AssertMeasuredContextPlan()
		{
			ReferenceGroundPlanTerrain context = _plan.GetTerrain("ordinary-atlas-context");
			if (_plan.Terrain.Count == 0 || _plan.Terrain[0].Id != context.Id ||
			    context.WriteMode != "author-surface" || context.SurfaceY != 0 ||
			    context.Footprint.Count != 4)
				throw new InvalidOperationException(
					"Reference 1 ordinary context must be the first painter and author the inclusive measured footprint at local TopY 0.");

			var expectedWater = new HashSet<string>(StringComparer.Ordinal)
			{
				"west-water-channel", "east-water-channel", "under-causeway-water"
			};
			var actualWater = _plan.Terrain
				.Where(item => item.WriteMode == "author-water")
				.Select(item => item.Id)
				.ToHashSet(StringComparer.Ordinal);
			if (!actualWater.SetEquals(expectedWater))
				throw new InvalidOperationException(
					"Reference 1 must author exactly the two measured side channels and under-causeway water.");
			bool reachedDryOverrides = false;
			for (int index = 1; index < _plan.Terrain.Count; index++)
			{
				ReferenceGroundPlanTerrain terrain = _plan.Terrain[index];
				if (terrain.WriteMode == "author-water")
				{
					if (reachedDryOverrides)
						throw new InvalidOperationException(
							$"Reference 1 water painter '{terrain.Id}' appears after a dry shelf override.");
				}
				else if (terrain.WriteMode == "author-surface") reachedDryOverrides = true;
			}
			foreach (string id in expectedWater)
			{
				ReferenceGroundPlanTerrain water = _plan.GetTerrain(id);
				if (water.SurfaceY != MeasuredWaterSurfaceLocalY ||
				    water.BedY != MeasuredWaterBedLocalY || water.EffectiveCells.Count == 0)
					throw new InvalidOperationException(
						$"Reference 1 water '{id}' must retain nonempty measured local surface/bed " +
						$"{MeasuredWaterSurfaceLocalY}/{MeasuredWaterBedLocalY}.");
			}

			var sourceDryShelves = new Dictionary<string, ReferenceGroundPlanCell[]>(StringComparer.Ordinal)
			{
				["west-outer-shelf-continuation"] = [new(-37, 3)],
				["east-middle-shelf-continuation"] = [new(35, 17)],
				["south-east-shoreline-continuation"] =
					[new(10, 52), new(18, 49), new(29, 48)],
				["far-east-shelf-continuation"] = [new(42, 43)],
			};
			foreach ((string id, ReferenceGroundPlanCell[] anchors) in sourceDryShelves)
			{
				ReferenceGroundPlanTerrain shelf = _plan.GetTerrain(id);
				if (shelf.WriteMode != "author-surface" || shelf.SurfaceY != 0 ||
				    shelf.Polygon.Count < 8 || shelf.AuthoredCells.Count < 64)
					throw new InvalidOperationException(
						$"Reference 1 dry shelf '{id}' is not a substantial irregular source polygon.");
				foreach (ReferenceGroundPlanCell anchor in anchors)
					if (!shelf.EffectiveCells.Contains(anchor))
						throw new InvalidOperationException(
							$"Reference 1 dry shelf '{id}' does not finally own measured anchor {anchor.X},{anchor.Z}.");
			}

			ReferenceGroundPlanTerrain eastLower = _plan.GetTerrain("east-lower-court");
			ReferenceGroundPlanTerrain eastUpper = _plan.GetTerrain("east-main-shelf");
			if (eastLower.SurfaceY != 0 || eastUpper.SurfaceY != 8 ||
			    eastLower.EffectiveCells.Any(cell => cell.Z < -7) ||
			    eastUpper.EffectiveCells.Any(cell => cell.Z > -8))
				throw new InvalidOperationException(
					"Reference 1 east P1 terrain must split the local-0 court at z>=-7 from the local-8 north plateau at z<=-8.");

			ReferenceGroundPlanStructure eastStair = _plan.GetStructure("east-side-stair");
			ReferenceGroundPlanTerrain eastStairLowerLanding =
				_plan.GetTerrain("east-stair-lower-landing");
			// The exact landing deliberately reaches two cells beyond the irregular
			// court edge so its five-cell-wide stair has a complete toe. Assert that
			// measured footprint directly instead of pretending it is a court subset.
			if (eastStairLowerLanding.SurfaceY != eastLower.SurfaceY ||
			    !eastStairLowerLanding.Footprint.SequenceEqual(new[] { 30, 0, 34, 2 }) ||
			    eastStair.FromTerrain != eastStairLowerLanding.Id ||
			    eastStair.ToTerrain != eastUpper.Id ||
			    eastStair.Treads.Count != 9)
				throw new InvalidOperationException(
					"Reference 1 east stair must retain its measured lower landing and nine treads to the north plateau.");
			for (int index = 0; index < eastStair.Treads.Count; index++)
			{
				ReferenceGroundPlanTread tread = eastStair.Treads[index];
				if (tread.TopY != index || tread.Footprint.Count != 4 ||
				    tread.Footprint[0] != 30 || tread.Footprint[2] != 34 ||
				    tread.Footprint[1] != -index || tread.Footprint[3] != -index)
					throw new InvalidOperationException(
						$"Reference 1 east stair tread {index + 1} no longer preserves the exact local 0-to-8 schedule.");
			}

			var lowerEastStructures = new HashSet<string>(StringComparer.Ordinal)
			{
				"east-inner-west-rib", "east-inner-middle-rib", "east-inner-east-rib",
				"east-outer-broken-range", "east-inner-survivor", "east-outer-survivor",
				"east-threshold-rubble", "east-inner-rubble", "east-outer-rubble"
			};
			foreach (string id in lowerEastStructures)
			{
				ReferenceGroundPlanStructure structure = _plan.GetStructure(id);
				if (structure.BaseY != 0 || structure.SupportTerrain != eastLower.Id)
					throw new InvalidOperationException(
						$"Reference 1 lower east structure '{id}' is not registered to the local-0 court.");
			}
			foreach (string id in new[]
			         {
				         "east-north-broken-room", "east-north-room-survivor", "east-north-rubble"
			         })
			{
				ReferenceGroundPlanStructure structure = _plan.GetStructure(id);
				if (structure.BaseY != 8 || structure.SupportTerrain != eastUpper.Id)
					throw new InvalidOperationException(
						$"Reference 1 north east structure '{id}' left the local-8 plateau.");
			}

			ReferenceGroundPlanTerrain southApproach = _plan.GetTerrain("south-landing");
			ReferenceGroundPlanTerrain eastShore =
				_plan.GetTerrain("south-east-shoreline-continuation");
			if (southApproach.SurfaceY != -6 || southApproach.Polygon.Count < 20 ||
			    !southApproach.EffectiveCells.Contains(new ReferenceGroundPlanCell(-7, 50)) ||
			    !southApproach.EffectiveCells.Contains(new ReferenceGroundPlanCell(1, 55)) ||
			    !southApproach.EffectiveCells.Any(cell => cell.Z >= 61) ||
			    southApproach.AuthoredCells.Overlaps(eastShore.AuthoredCells) ||
			    eastShore.SurfaceY != 0)
				throw new InvalidOperationException(
					"Reference 1 south landing must form one extended local-6 approach without lowering the east shoreline.");

			ReferenceGroundPlanTerrain westBasin = _plan.GetTerrain("west-water-channel");
			ReferenceGroundPlanTerrain underCauseway = _plan.GetTerrain("under-causeway-water");
			for (int z = 15; z <= 36; z++)
			{
				if (!westBasin.EffectiveCells.Contains(new ReferenceGroundPlanCell(-10, z)) ||
				    !underCauseway.EffectiveCells.Contains(new ReferenceGroundPlanCell(-9, z)))
					throw new InvalidOperationException(
						$"Reference 1 west basin is not joined to under-causeway water at local z={z}.");
			}
			ReferenceGroundPlanTerrain deck = _plan.GetTerrain("causeway-deck");
			if (deck.EffectiveCells.Count != 15 * 37)
				throw new InvalidOperationException(
					"Reference 1 water correction touched the measured 15x37 causeway deck.");

			// Because the ordinary rectangle is the lowest painter, the final terrain
			// owners must partition that exact crop. This proves there is no accidental
			// preserve-atlas hole between a water polygon and a reclaimed shelf.
			var finalCells = new HashSet<ReferenceGroundPlanCell>();
			foreach (ReferenceGroundPlanTerrain terrain in _plan.Terrain)
				finalCells.UnionWith(terrain.EffectiveCells);
			if (!finalCells.SetEquals(context.AuthoredCells))
				throw new InvalidOperationException(
					"Reference 1 final terrain owners do not exactly cover the measured ordinary-context footprint.");
		}

		private void WriteAuthoredWater(ReferenceGroundPlanTerrain terrain)
		{
			int localSurface = terrain.SurfaceY ?? throw new InvalidOperationException(
				$"Authored water '{terrain.Id}' has no audited surfaceY.");
			int localBed = terrain.BedY ?? throw new InvalidOperationException(
				$"Authored water '{terrain.Id}' has no audited bedY.");
			if (terrain.Footprint.Count == 4)
				WaterRectangle(terrain.Footprint, localSurface, localBed);
			else
				WaterPolygon(terrain.Polygon, localSurface, localBed);
		}

		private void AssertAuthoredWetDryOwnership()
		{
			var stairCells = new Dictionary<ReferenceGroundPlanCell,
				(int TopY, string Owner, int AscendingZDirection, bool First)>();
			foreach (ReferenceGroundPlanStructure stair in _plan.Structures)
			{
				if (stair.Kind != "stair") continue;
				int ascendingZDirection = Math.Sign(
					stair.Treads[^1].Footprint[1] - stair.Treads[0].Footprint[1]);
				for (int treadIndex = 0; treadIndex < stair.Treads.Count; treadIndex++)
				{
					ReferenceGroundPlanTread tread = stair.Treads[treadIndex];
				for (int z = tread.Footprint[1]; z <= tread.Footprint[3]; z++)
				for (int x = tread.Footprint[0]; x <= tread.Footprint[2]; x++)
				{
					var cell = new ReferenceGroundPlanCell(x, z);
					var authored = (TopY: tread.TopY!.Value, Owner: stair.Id,
						AscendingZDirection: ascendingZDirection, First: treadIndex == 0);
					if (stairCells.TryGetValue(cell, out var previous) &&
					    previous.TopY != authored.TopY)
						throw new InvalidOperationException(
							$"Stairs '{previous.Owner}' and '{stair.Id}' disagree at {x},{z}.");
					stairCells[cell] = authored;
				}
				}
			}

			foreach (ReferenceGroundPlanTerrain terrain in _plan.Terrain)
			{
				if (terrain.WriteMode == "preserve-atlas")
				{
					if (terrain.EffectiveCells.Count > 0)
						throw new InvalidOperationException(
							$"Reference 1 leaves {terrain.EffectiveCells.Count} visible cells of '{terrain.Id}' to the atlas.");
					continue;
				}
				foreach (ReferenceGroundPlanCell cell in terrain.EffectiveCells)
				{
					// Exact treads are the last terrain writer and intentionally replace
					// the broad context owner beneath their one-cell courses.
					if (stairCells.ContainsKey(cell)) continue;
					if (terrain.WriteMode == "author-water")
						AssertWetCell(cell.X, cell.Z, terrain.SurfaceY!.Value, terrain.BedY!.Value,
							terrain.Id);
					else
						AssertDryCell(cell.X, cell.Z, terrain.SurfaceY!.Value, terrain.Id,
							ResolveTerrainMaterialStyle(terrain.Material));
				}
			}

			TerrainMaterialStyle stairStyle = ResolveTerrainMaterialStyle("worn-paving");
			foreach (var pair in stairCells)
			{
				ReferenceGroundPlanCell cell = pair.Key;
				var stair = pair.Value;
				AssertStairDryCell(cell.X, cell.Z, stair.TopY, stair.Owner, stairStyle,
					stair.AscendingZDirection, stair.First);
			}

			AssertTreeAnchorsDry();
			AssertAuthoredWaterTransitions(_plan.GetTerrain("ordinary-atlas-context").AuthoredCells);
		}

		private void AssertTreeAnchorsDry()
		{
			for (int i = 0; i < _plan.SurroundingTrees.Count; i++)
			{
				List<int> anchor = _plan.SurroundingTrees[i];
				(int x, int z) = LocalCell(anchor[0], anchor[1]);
				int index = z * _data.Width + x;
				if (_data.WaterSurface[index] != 0 || _data.Land[index] == 0 ||
				    _data.Height[index] <= Y(MeasuredWaterSurfaceLocalY))
					throw new InvalidOperationException(
						$"Reference 1 source tree {i + 1} at {anchor[0]},{anchor[1]} is not on dry land above measured water.");
			}
		}

		private void AssertAuthoredWaterTransitions(
			IReadOnlySet<ReferenceGroundPlanCell> authoredContext)
		{
			foreach (ReferenceGroundPlanCell cell in authoredContext)
			{
				for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
				for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
				{
					(int x, int z) = RuntimeCell(cell.X, cell.Z, dx, dz);
					CheckNeighbour(x, z, x + 1, z);
					CheckNeighbour(x, z, x - 1, z);
					CheckNeighbour(x, z, x, z + 1);
					CheckNeighbour(x, z, x, z - 1);
				}
			}

			void CheckNeighbour(int x, int z, int nx, int nz)
			{
				if (nx < 0 || nz < 0 || nx >= _data.Width || nz >= _data.Depth)
					throw new InvalidOperationException(
						"Reference 1 authored context reaches the edge of its materialised review window.");
				int a = z * _data.Width + x;
				int b = nz * _data.Width + nx;
				bool aWet = _data.WaterSurface[a] > 0;
				bool bWet = _data.WaterSurface[b] > 0;
				if (aWet && bWet)
				{
					int step = Math.Abs(_data.WaterSurface[a] - _data.WaterSurface[b]);
					if (step > 1)
						throw new InvalidOperationException(
							$"Reference 1 connected water step is {step} blocks at window {x},{z} -> {nx},{nz}.");
					return;
				}
				if (aWet == bWet) return;
				int wet = aWet ? a : b;
				int dry = aWet ? b : a;
				if (_data.Height[dry] < _data.WaterSurface[wet] + 1)
					throw new InvalidOperationException(
						$"Reference 1 dry bank at window {dry % _data.Width},{dry / _data.Width} " +
						$"sits below adjacent water surface {_data.WaterSurface[wet]}.");
			}
		}

		private void WriteAuthoredSurfaceWear()
		{
			foreach (ReferenceGroundPlanSurfacePatch patch in _plan.SurfacePatches)
			{
				int localTop = _plan.GetTerrain(patch.TerrainId).SurfaceY ??
					throw new InvalidOperationException(
						$"Surface patch '{patch.Id}' has no audited terrain height.");
				foreach (ReferenceGroundPlanCell cell in patch.EffectiveCells
				         .OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
				{
					TerrainMaterialStyle style = ResolveSurfacePatchStyle(patch.Material);
					RepaintSurface(cell.X, cell.Z, localTop, style);
				}
			}
		}

		private void WriteCausewayPavingJoints()
		{
			// The source bridge reads as large fitted slabs rather than one blank white
			// strip. At 3x, these one-runtime-voxel seams stay subordinate to the
			// fifteen-cell deck and never introduce a new architectural rhythm.
			int surfaceVoxelY = Y(0) - 1;
			int[] crossJoints = { 4, 9, 15, 21, 28, 34 };
			for (int index = 0; index < crossJoints.Length; index++)
			{
				byte material = index % 2 == 0 ? Palette.STONE : Palette.STONE_WARM;
				for (int x = -7; x <= 7; x++)
				for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
					PutRuntime(x, surfaceVoxelY, crossJoints[index], dx, 1, material);
			}
			// Short longitudinal joints make unequal source-sized slabs without drawing
			// a full modern grid down the processional axis.
			foreach ((int x, int z0, int z1) in new[]
			         {
			         	(-4, 4, 14), (3, 10, 20), (-2, 22, 33), (5, 28, 35)
			         })
				for (int z = z0; z <= z1; z++)
				for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
					PutRuntime(x, surfaceVoxelY, z, 1, dz, Palette.STONE);
		}

		private void CarveFineVerticalSlot(int localX, int localZ,
			int localY0, int localY1, int offsetX, int offsetZ)
		{
			for (int y = localY0; y <= localY1; y++)
			for (int dy = 0; dy < RuntimePlanScale; dy++)
				PutFine(localX, y, localZ, offsetX, dy, offsetZ, Palette.AIR);
		}

		private void CarveFineJamb(int localX, int localZ0, int localZ1,
			int localY0, int localY1, int offsetX)
		{
			for (int z = localZ0; z <= localZ1; z++)
			for (int y = localY0; y <= localY1; y++)
			for (int dy = 0; dy < RuntimePlanScale; dy++)
				PutFine(localX, y, z, offsetX, dy, 0, Palette.AIR);
		}

		private void WriteGateConnectedBodyWest()
		{
			// The locked source shows daylight through the monumental gate. The west
			// half therefore owns only its side of the rear mass: the separately
			// recorded x=-3..3 continuation stays open through local course 13.
			WriteProjectionBase(Palette.STONE);
			Fill(-11, -4, Y(1), Y(15), -18, -9, Palette.STONE_PALE);

			// A four-course backing connects the front composition without turning
			// its whole 8x8 measured envelope into a featureless tower. The two
			// three-cell-wide deep ribs below are the west pair of exactly four principal
			// pylons traced across the facade.
			Fill(-11, -4, Y(1), Y(4), -8, -1, Palette.STONE_PALE);
			Fill(-11, -9, Y(1), Y(21), -8, -3, Palette.STONE_PALE);
			Fill(-6, -4, Y(1), Y(18), -8, -2, Palette.STONE_PALE);

			// Only the source-facing rear shoulder survives above the main side
			// wall. The former 4x6x7 cap read as a second tower at locked distance.
			Fill(-11, -9, Y(16), Y(21), -18, -15, Palette.STONE_PALE);

			// Faded courses remain broad enough to read, but stop at the passage and
			// in the recessed bays instead of recreating the rejected blanket face.
			Fill(-11, -4, Y(4), Y(4), -18, -9, Palette.STONE_WARM);
			Fill(-11, -9, Y(7), Y(7), -8, -3, Palette.STONE);
			Fill(-6, -4, Y(7), Y(7), -8, -2, Palette.STONE);
			Fill(-11, -9, Y(12), Y(13), -8, -3, Palette.STONE_WARM);
			Fill(-6, -6, Y(3), Y(9), -8, -2, Palette.MOSS_STONE);
			Chip(-10, 21, -16);
			CarveFineVerticalSlot(-10, -3, 5, 16, 0, 1);
			CarveFineVerticalSlot(-5, -2, 5, 14, 0, 1);
			CarveFineJamb(-4, -8, -2, 3, 12, 1);
		}

		private void WriteGateConnectedBodyEast()
		{
			// Keep this half separate from the west body so plan and runtime cannot
			// silently seal the candidate hidden continuation of the source passage.
			WriteProjectionBase(Palette.STONE);
			Fill(4, 11, Y(1), Y(15), -18, -9, Palette.STONE_PALE);
			Fill(4, 11, Y(1), Y(4), -8, -1, Palette.STONE_PALE);

			// East pair of the same four principal attached pylons. They keep their
			// independently observed heights rather than being stamped from the west.
			Fill(9, 11, Y(1), Y(21), -8, -3, Palette.STONE_PALE);
			Fill(4, 6, Y(1), Y(18), -8, -2, Palette.STONE_PALE);
			Fill(9, 11, Y(16), Y(21), -18, -15, Palette.STONE_PALE);

			Fill(4, 11, Y(4), Y(4), -18, -9, Palette.STONE_WARM);
			Fill(9, 11, Y(7), Y(7), -8, -3, Palette.STONE);
			Fill(4, 6, Y(7), Y(7), -8, -2, Palette.STONE);
			Fill(9, 11, Y(12), Y(13), -8, -3, Palette.STONE_WARM);
			Fill(6, 6, Y(2), Y(7), -8, -2, Palette.MOSS_STONE);
			Chip(10, 21, -15);
			CarveFineVerticalSlot(10, -3, 5, 16, 0, 1);
			CarveFineVerticalSlot(5, -2, 5, 14, 0, 1);
			CarveFineJamb(4, -8, -2, 3, 12, -1);
		}

		private void WriteGateElevatedRoof()
		{
			// The portal's measured clear height is 14. This structure intentionally
			// starts at local 14 rather than at its plan baseY=0: the latter records
			// its registration datum, not permission to plug the passage.
			WriteProjectionCourse(Y(14), Palette.STONE);
			Fill(-12, 12, Y(15), Y(19), -20, -8, Palette.STONE_PALE);
			Fill(-11, 11, Y(20), Y(20), -19, -9, Palette.STONE_PALE);
			// This elevated lintel is the only central masonry above the continuous
			// passage. Keeping it in the roof structure makes the clearance explicit.
			Fill(-2, 2, Y(15), Y(17), -18, -9, Palette.STONE_WARM);
			Chip(-1, 17, -10);
			Chip(2, 16, -9);

			// Reference 1 reads as one broad roof plane, with a thin damaged rear
			// parapet and short returns. The former multi-course cap formed a stepped
			// ziggurat and obscured the source's portal-to-roof height relationship.
			Fill(-8, -2, Y(21), Y(22), -19, -18, Palette.STONE_PALE);
			Fill(0, 6, Y(21), Y(22), -19, -18, Palette.STONE_PALE);
			Fill(-10, -9, Y(21), Y(21), -18, -15, Palette.STONE_PALE);
			Fill(8, 9, Y(21), Y(21), -18, -16, Palette.STONE_PALE);
			Fill(-5, -3, Y(23), Y(23), -19, -18, Palette.STONE_PALE);
			Fill(-11, 11, Y(19), Y(19), -19, -9, Palette.STONE_WARM);
			Fill(-12, -12, Y(16), Y(19), -18, -10, Palette.MOSS_STONE);
			Chip(-4, 23, -18);
			Chip(2, 22, -18);
			for (int x = -12; x <= 12; x++)
				PutFine(x, 19, -8, 0, 1, 1, Palette.STONE_WARM);
			PutFine(-12, 19, -8, -1, 2, 1, Palette.AIR);
			PutFine(12, 19, -8, 1, 2, 1, Palette.AIR);
			PutFine(-7, 19, -8, 0, 2, 1, Palette.AIR);
			PutFine(4, 19, -8, 1, 2, 1, Palette.AIR);
		}

		private void WriteGateWestAttachedButtress()
		{
			WriteProjectionBase(Palette.STONE);
			// Keep the traced 2x16 ground projection connected, but let only three
			// attached ribs rise above its low backing. A single full-height strip
			// read as an invented side tower in the v4 locked capture.
			Fill(-13, -12, Y(1), Y(9), -17, -2, Palette.STONE_PALE);
			Fill(-13, -12, Y(10), Y(16), -17, -14, Palette.STONE_PALE);
			Fill(-13, -12, Y(10), Y(13), -11, -8, Palette.STONE_PALE);
			Fill(-13, -12, Y(10), Y(15), -5, -2, Palette.STONE_PALE);
			Fill(-11, -9, Y(1), Y(18), -3, -2, Palette.STONE_PALE);
			Fill(-13, -12, Y(4), Y(4), -17, -2, Palette.STONE_WARM);
			Fill(-11, -9, Y(10), Y(10), -3, -2, Palette.STONE);
			Fill(-13, -13, Y(8), Y(13), -12, -8, Palette.MOSS_STONE);
			Chip(-9, 18, -2);
		}

		private void WriteGateEastAttachedButtress()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(12, 13, Y(1), Y(9), -17, -2, Palette.STONE_PALE);
			Fill(12, 13, Y(10), Y(17), -17, -14, Palette.STONE_PALE);
			Fill(12, 13, Y(10), Y(13), -11, -8, Palette.STONE_PALE);
			Fill(12, 13, Y(10), Y(15), -5, -2, Palette.STONE_PALE);
			Fill(9, 11, Y(1), Y(16), -3, -2, Palette.STONE_PALE);
			Fill(12, 13, Y(5), Y(5), -17, -2, Palette.STONE_WARM);
			Fill(9, 11, Y(9), Y(9), -3, -2, Palette.STONE);
			Fill(13, 13, Y(7), Y(11), -15, -11, Palette.MOSS_STONE);
			Chip(13, 17, -15);
			Chip(10, 16, -2);
		}

		private void WriteCausewayWestParapetNorth()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-7, -7, Y(1), Y(2), 1, 8, Palette.STONE_PALE);
			Put(-7, Y(2), 4, Palette.STONE_WARM);
			Chip(-7, 2, 7);
		}

		private void WriteCausewayWestParapetMiddle()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-7, -7, Y(1), Y(2), 10, 24, Palette.STONE_PALE);
			Fill(-7, -7, Y(1), Y(1), 15, 18, Palette.STONE_WARM);
			Chip(-7, 2, 20);
		}

		private void WriteCausewayWestParapetSouth()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-7, -7, Y(1), Y(2), 26, 35, Palette.STONE_PALE);
			Fill(-7, -7, Y(2), Y(2), 27, 29, Palette.STONE_WARM);
			Chip(-7, 2, 34);
		}

		private void WriteCausewayEastParapetNorth()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(7, 7, Y(1), Y(2), 1, 8, Palette.STONE_PALE);
			Fill(7, 7, Y(1), Y(1), 2, 5, Palette.STONE_WARM);
			Chip(7, 2, 6);
		}

		private void WriteCausewayEastParapetMiddle()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(7, 7, Y(1), Y(2), 10, 24, Palette.STONE_PALE);
			Fill(7, 7, Y(2), Y(2), 12, 15, Palette.STONE_WARM);
			Chip(7, 2, 22);
		}

		private void WriteCausewayEastParapetSouth()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(7, 7, Y(1), Y(2), 26, 35, Palette.STONE_PALE);
			Fill(7, 7, Y(1), Y(1), 30, 33, Palette.STONE_WARM);
			Chip(7, 2, 28);
		}

		private void WriteCausewayWestSideSupports()
		{
			// The water plane is local -21 (absolute 105), while the measured bed is
			// local -23. Extending only these same traced X/Z support cells two blocks
			// below the plan base is the conservative hidden continuation that prevents
			// visibly floating piers; it adds no new footprint or support rhythm. The
			// two submerged blocks are provisional collision continuity, not claimed
			// source-observed support shape.
			Fill(-9, -8, Y(-23), Y(-1), 5, 8, Palette.STONE_PALE);
			Fill(-9, -8, Y(-23), Y(-1), 15, 19, Palette.STONE_PALE);
			Fill(-9, -8, Y(-23), Y(-1), 28, 32, Palette.STONE_PALE);
			Fill(-9, -8, Y(-20), Y(-20), 5, 8, Palette.STONE_WARM);
			Fill(-9, -8, Y(-12), Y(-12), 15, 19, Palette.STONE);
			Fill(-9, -8, Y(-7), Y(-7), 28, 32, Palette.STONE_WARM);
			Chip(-9, -1, 8);
			Chip(-8, -1, 31);
		}

		private void WriteCausewayEastSideSupports()
		{
			Fill(8, 9, Y(-23), Y(-1), 5, 8, Palette.STONE_PALE);
			Fill(8, 9, Y(-23), Y(-1), 16, 20, Palette.STONE_PALE);
			Fill(8, 9, Y(-23), Y(-1), 29, 33, Palette.STONE_PALE);
			Fill(8, 9, Y(-18), Y(-18), 5, 8, Palette.STONE_WARM);
			Fill(8, 9, Y(-10), Y(-10), 16, 20, Palette.STONE);
			Fill(8, 9, Y(-5), Y(-5), 29, 33, Palette.STONE_WARM);
			Chip(9, -1, 5);
			Chip(8, -1, 30);
		}

		private void WriteSouthAbutmentWestDrop()
		{
			Fill(-9, -8, Y(-23), Y(-1), 34, 35, Palette.STONE_PALE);
			Fill(-9, -8, Y(-15), Y(-15), 34, 35, Palette.STONE_WARM);
			Fill(-9, -9, Y(-7), Y(-2), 34, 35, Palette.MOSS_STONE);
		}

		private void WriteSouthAbutmentEastDrop()
		{
			Fill(8, 9, Y(-23), Y(-1), 34, 35, Palette.STONE_PALE);
			Fill(8, 9, Y(-13), Y(-13), 34, 35, Palette.STONE);
			Fill(9, 9, Y(-8), Y(-3), 34, 35, Palette.MOSS_STONE);
		}

		private void WriteCausewayNorthDeckPlinth()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-2, -1, Y(1), Y(3), 8, 9, Palette.STONE_PALE);
			Fill(-2, -1, Y(2), Y(2), 8, 9, Palette.STONE_WARM);
			Chip(-1, 3, 9);
		}

		private void WriteCausewaySouthEastPost()
		{
			WriteProjectionBase(Palette.STONE);
			Put(3, Y(1), 27, Palette.STONE_PALE);
			Put(3, Y(2), 27, Palette.STONE_WARM);
		}

		private void WriteCausewayMidRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(2, 2, Y(1), Y(2), 12, 12, Palette.STONE_WARM);
			Fill(3, 3, Y(1), Y(1), 12, 13, Palette.STONE);
			Put(2, Y(1), 13, Palette.MOSS_STONE);
			Fill(1, 1, Y(1), Y(2), 13, 13, Palette.STONE_PALE);
		}

		private void WriteWestInnerCliffRange()
		{
			// The western precinct is a dense family of attached square ribs. These
			// three traced returns form one wall range; they are not random pillars.
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			WriteProjectionCourse(Y(2), Palette.STONE_PALE);
			// V2 proved that extruding this entire two-wide trace to local 14
			// produced one featureless slab. The source instead shows a low connected
			// foundation carrying several square ribs of unequal survival height.
			Fill(-16, -15, Y(3), Y(14), -4, -3, Palette.STONE_PALE);
			Fill(-16, -15, Y(3), Y(10), 1, 2, Palette.STONE_PALE);
			Fill(-16, -15, Y(3), Y(13), 6, 7, Palette.STONE_PALE);
			Fill(-16, -15, Y(3), Y(11), 12, 13, Palette.STONE_PALE);
			Fill(-18, -17, Y(2), Y(8), -4, -3, Palette.STONE_PALE);
			Fill(-18, -17, Y(2), Y(9), 12, 13, Palette.STONE_PALE);
			Fill(-16, -15, Y(6), Y(6), 6, 7, Palette.STONE_WARM);
			Fill(-16, -16, Y(7), Y(11), 12, 13, Palette.MOSS_STONE);
			Chip(-15, 14, -3);
			Chip(-16, 13, 7);
		}

		private void WriteWestMiddleParallelRange()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			WriteProjectionCourse(Y(2), Palette.STONE_PALE);
			Fill(-22, -21, Y(3), Y(11), -5, -4, Palette.STONE_PALE);
			Fill(-22, -21, Y(3), Y(9), 0, 1, Palette.STONE_PALE);
			Fill(-22, -21, Y(3), Y(10), 5, 6, Palette.STONE_PALE);
			Fill(-22, -21, Y(3), Y(8), 10, 11, Palette.STONE_PALE);
			Fill(-20, -19, Y(2), Y(7), -5, -4, Palette.STONE_PALE);
			Fill(-20, -19, Y(2), Y(6), 10, 11, Palette.STONE_PALE);
			Fill(-22, -21, Y(5), Y(5), 5, 6, Palette.STONE_WARM);
			Fill(-22, -22, Y(6), Y(8), 0, 1, Palette.MOSS_STONE);
			Chip(-21, 11, -4);
		}

		private void WriteWestOuterBrokenRange()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			Fill(-29, -28, Y(2), Y(8), -3, -2, Palette.STONE_PALE);
			Fill(-29, -28, Y(2), Y(6), 2, 3, Palette.STONE_PALE);
			Fill(-29, -28, Y(2), Y(7), 7, 8, Palette.STONE_PALE);
			Fill(-27, -24, Y(2), Y(4), 7, 8, Palette.STONE_PALE);
			Fill(-25, -24, Y(2), Y(7), -4, -3, Palette.STONE_PALE);
			Fill(-25, -24, Y(2), Y(5), 1, 2, Palette.STONE_PALE);
			Fill(-29, -28, Y(3), Y(3), 2, 3, Palette.STONE_WARM);
			Chip(-29, 8, -2);
			Chip(-24, 7, -3);
			Chip(-26, 4, 8);
		}

		private void WriteWestNorthRibWest()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			Fill(-20, -19, Y(2), Y(13), -12, -11, Palette.STONE_PALE);
			Fill(-20, -19, Y(2), Y(10), -8, -7, Palette.STONE_PALE);
			Fill(-20, -19, Y(2), Y(8), -6, -5, Palette.STONE_PALE);
			Fill(-20, -20, Y(7), Y(10), -8, -7, Palette.MOSS_STONE);
			Chip(-19, 13, -11);
		}

		private void WriteWestNorthRibMiddle()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			Fill(-17, -16, Y(2), Y(10), -11, -10, Palette.STONE_PALE);
			Fill(-17, -16, Y(2), Y(8), -7, -6, Palette.STONE_PALE);
			Fill(-17, -16, Y(5), Y(5), -7, -6, Palette.STONE_WARM);
			Chip(-16, 10, -10);
		}

		private void WriteWestNorthRibEast()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			Fill(-15, -14, Y(2), Y(8), -10, -9, Palette.STONE_PALE);
			Fill(-15, -14, Y(2), Y(7), -6, -5, Palette.STONE_PALE);
			Fill(-15, -15, Y(4), Y(6), -6, -5, Palette.MOSS_STONE);
			Chip(-14, 8, -9);
		}

		private void WriteWestCourtSurvivor()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-19, -18, Y(1), Y(12), 4, 5, Palette.STONE_PALE);
			Fill(-19, -18, Y(5), Y(5), 4, 5, Palette.STONE_WARM);
			Chip(-18, 12, 5);
		}

		private void WriteWestThresholdSurvivor()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-15, -14, Y(1), Y(9), 1, 2, Palette.STONE_PALE);
			Fill(-15, -14, Y(4), Y(4), 1, 2, Palette.STONE_WARM);
			Chip(-14, 9, 2);
		}

		private void WriteWestThresholdRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-14, -14, Y(1), Y(2), 4, 4, Palette.STONE_WARM);
			Fill(-15, -15, Y(1), Y(1), 4, 6, Palette.STONE);
			Fill(-16, -16, Y(1), Y(2), 5, 5, Palette.STONE_PALE);
			Put(-16, Y(1), 6, Palette.MOSS_STONE);
		}

		private void WriteWestMiddleRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-20, -20, Y(1), Y(2), 5, 5, Palette.STONE_WARM);
			Fill(-21, -21, Y(1), Y(1), 5, 6, Palette.STONE);
			Fill(-22, -22, Y(1), Y(2), 6, 6, Palette.STONE_PALE);
			Put(-22, Y(1), 7, Palette.MOSS_STONE);
		}

		private void WriteWestOuterRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-27, -27, Y(1), Y(1), 9, 9, Palette.STONE_WARM);
			Fill(-28, -28, Y(1), Y(2), 9, 10, Palette.STONE);
			Fill(-29, -29, Y(1), Y(1), 10, 11, Palette.MOSS_STONE);
		}

		private void WriteWestNorthRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-18, -18, Y(1), Y(2), -8, -7, Palette.STONE_WARM);
			Fill(-19, -19, Y(1), Y(1), -8, -7, Palette.STONE);
			Put(-17, Y(1), -8, Palette.MOSS_STONE);
		}

		private void WriteEastThresholdWestRib()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(12, 13, Y(1), Y(14), -3, -1, Palette.STONE_PALE);
			Fill(12, 13, Y(5), Y(5), -3, -1, Palette.STONE_WARM);
			Fill(13, 13, Y(8), Y(11), -3, -2, Palette.MOSS_STONE);
			Chip(12, 14, -1);
		}

		private void WriteEastInnerWestRib()
		{
			// V5 exposed that the broad +8 painter lifted this source-visible lower
			// court. These courses retain their former above-ground spans after the
			// measured footprint moves back to local 0.
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			WriteProjectionCourse(Y(2), Palette.STONE_PALE);
			Fill(12, 13, Y(3), Y(14), 0, 2, Palette.STONE_PALE);
			Fill(12, 13, Y(3), Y(10), 5, 7, Palette.STONE_PALE);
			Fill(12, 13, Y(3), Y(13), 11, 13, Palette.STONE_PALE);
			Fill(13, 13, Y(7), Y(10), 5, 7, Palette.MOSS_STONE);
			Chip(12, 14, 2);
			Chip(13, 13, 13);
		}

		private void WriteEastInnerMiddleRib()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			WriteProjectionCourse(Y(2), Palette.STONE_PALE);
			Fill(16, 17, Y(3), Y(11), 2, 4, Palette.STONE_PALE);
			Fill(16, 17, Y(3), Y(9), 7, 9, Palette.STONE_PALE);
			Fill(16, 17, Y(3), Y(10), 12, 14, Palette.STONE_PALE);
			Fill(17, 17, Y(6), Y(9), 7, 9, Palette.MOSS_STONE);
			Chip(16, 11, 2);
		}

		private void WriteEastInnerEastRib()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			Fill(20, 21, Y(2), Y(9), 3, 5, Palette.STONE_PALE);
			Fill(20, 21, Y(2), Y(7), 8, 10, Palette.STONE_PALE);
			Fill(20, 21, Y(2), Y(8), 12, 13, Palette.STONE_PALE);
			Fill(20, 21, Y(4), Y(4), 8, 10, Palette.STONE_WARM);
			Chip(21, 9, 5);
			Put(20, Y(8), 13, Palette.MOSS_STONE);
		}

		private void WriteEastOuterBrokenRange()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(Y(1), Palette.STONE_PALE);
			WriteProjectionCourse(Y(2), Palette.STONE_PALE);
			Fill(22, 24, Y(3), Y(6), -1, 0, Palette.STONE_PALE);
			Fill(27, 29, Y(3), Y(5), -1, 0, Palette.STONE_PALE);
			Fill(28, 29, Y(3), Y(8), 1, 3, Palette.STONE_PALE);
			Fill(28, 29, Y(3), Y(6), 6, 8, Palette.STONE_PALE);
			Fill(28, 29, Y(3), Y(7), 10, 11, Palette.STONE_PALE);
			Fill(23, 25, Y(3), Y(6), 11, 12, Palette.STONE_PALE);
			Fill(27, 27, Y(3), Y(5), 11, 12, Palette.STONE_PALE);
			Chip(29, 8, 3);
			Chip(23, 6, 12);
		}

		private void WriteEastNorthBrokenRoom()
		{
			// The overhead view resolves one broken room outline, not four parallel
			// freestanding bars. Its south return remains open at x17..19.
			WriteProjectionBase(Palette.STONE);
			Fill(15, 27, Y(9), Y(18), -17, -16, Palette.STONE_PALE);
			Fill(15, 16, Y(9), Y(16), -15, -8, Palette.STONE_PALE);
			Fill(26, 27, Y(9), Y(17), -15, -10, Palette.STONE_PALE);
			Fill(20, 27, Y(9), Y(15), -9, -8, Palette.STONE_PALE);
			Fill(15, 27, Y(12), Y(12), -17, -16, Palette.STONE_WARM);
			Fill(26, 27, Y(13), Y(13), -15, -10, Palette.STONE);
			Fill(16, 16, Y(11), Y(15), -13, -10, Palette.MOSS_STONE);
			Chip(17, 18, -17);
			Chip(18, 18, -17);
			Chip(27, 17, -10);
			Chip(21, 15, -8);
		}

		private void WriteEastNorthRoomSurvivor()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(18, 19, Y(9), Y(21), -12, -11, Palette.STONE_PALE);
			Fill(18, 19, Y(14), Y(14), -12, -11, Palette.STONE_WARM);
			Fill(18, 18, Y(16), Y(19), -12, -12, Palette.MOSS_STONE);
			Chip(19, 21, -11);
		}

		private void WriteEastInnerSurvivor()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(14, 15, Y(1), Y(10), 5, 6, Palette.STONE_PALE);
			Fill(14, 15, Y(5), Y(5), 5, 6, Palette.STONE_WARM);
			Chip(15, 10, 6);
		}

		private void WriteEastOuterSurvivor()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(24, 25, Y(1), Y(8), 5, 6, Palette.STONE_PALE);
			Fill(24, 25, Y(3), Y(3), 5, 6, Palette.STONE_WARM);
			Chip(24, 8, 5);
		}

		private void WriteEastThresholdRubble()
		{
			// These sparse courses were already expressed relative to local 0; only
			// their stale plan support was at +8, so subtracting again would bury them.
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(12, 12, Y(1), Y(2), 1, 1, Palette.STONE_WARM);
			Fill(13, 13, Y(1), Y(1), 1, 2, Palette.STONE);
			Fill(14, 14, Y(1), Y(2), 2, 3, Palette.STONE_PALE);
			Put(15, Y(1), 3, Palette.MOSS_STONE);
		}

		private void WriteEastInnerRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(17, 17, Y(1), Y(2), 8, 8, Palette.STONE_WARM);
			Fill(18, 18, Y(1), Y(1), 8, 9, Palette.STONE);
			Fill(19, 19, Y(1), Y(2), 9, 10, Palette.STONE_PALE);
			Put(20, Y(1), 10, Palette.MOSS_STONE);
		}

		private void WriteEastOuterRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(25, 25, Y(1), Y(2), 7, 7, Palette.STONE_WARM);
			Fill(26, 26, Y(1), Y(1), 7, 8, Palette.STONE);
			Fill(27, 27, Y(1), Y(2), 8, 9, Palette.STONE_PALE);
			Put(28, Y(1), 9, Palette.MOSS_STONE);
		}

		private void WriteEastNorthRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(22, 22, Y(9), Y(10), -13, -12, Palette.STONE_WARM);
			Fill(23, 23, Y(9), Y(9), -14, -13, Palette.STONE);
			Fill(24, 24, Y(9), Y(10), -14, -14, Palette.STONE_PALE);
			Put(25, Y(9), -14, Palette.MOSS_STONE);
		}

		private void WriteAuthoredStoneWeathering()
		{
			// A source cell is now a three-voxel measurement envelope. Leaving every
			// exposed voxel in that envelope STONE_PALE turns a seventy-block facade
			// into one unmodulated card, especially after close shader detail fades.
			// This field changes colour only; the individually authored silhouette and
			// damage remain untouched. Its 31- and 67-block wavelengths produce broad
			// faded runs like the source instead of the rejected per-block confetti.
			var age = new Noise2D(Rng.StableHash("reference-1:stone-weathering-v1"));
			ReferenceGroundPlanTerrain context = _plan.GetTerrain("ordinary-atlas-context");
			int x0 = context.Footprint[0], z0 = context.Footprint[1];
			int x1 = context.Footprint[2], z1 = context.Footprint[3];
			int minimumY = Math.Max(0, Y(MeasuredWaterBedLocalY) - 1);

			for (int localZ = z0; localZ <= z1; localZ++)
			for (int localX = x0; localX <= x1; localX++)
			for (int offsetZ = -RuntimePlanScale / 2;
			     offsetZ <= RuntimePlanScale / 2; offsetZ++)
			for (int offsetX = -RuntimePlanScale / 2;
			     offsetX <= RuntimePlanScale / 2; offsetX++)
			{
				(int x, int z) = RuntimeCell(localX, localZ, offsetX, offsetZ);
				int index = z * _data.Width + x;
				int top = _grid.HeightAt(x, z);
				int globalX = x + _data.OriginX;
				int globalZ = z + _data.OriginZ;
				for (int y = minimumY; y < top; y++)
				{
					byte material = _grid.At(x, y, z);
					if (material != Palette.STONE_PALE || !Exposed(x, y, z)) continue;

					float broad = age.Fbm(globalX / 31f + y / 109f,
						globalZ / 31f - y / 83f, 3);
					float drift = age.Fbm(globalX / 67f - 37f,
						globalZ / 67f + y / 151f + 19f, 2);
					byte replacement = broad > 0.34f && drift > -0.18f
						? Palette.STONE_WARM
						: broad < -0.39f ? Palette.STONE : Palette.STONE_PALE;

					// Damp growth stays in coherent low courses beside the measured
					// water and at wall feet. It may tint masonry; it may not invent a
					// new break, silhouette, or freestanding mass.
					int groundTop = _data.Height[index];
					float damp = age.Fbm(globalX / 48f + 71f, globalZ / 48f - 43f, 2);
					if (damp > 0.46f &&
					    y <= Math.Max(Y(MeasuredWaterSurfaceLocalY) + 8, groundTop + 2))
						replacement = Palette.MOSS_STONE;

					if (replacement == material) continue;
					_grid.Set(x, y, z, replacement);
					Voxels++;
				}
			}

			bool Exposed(int x, int y, int z) =>
				!_grid.SolidAt(x + 1, y, z) || !_grid.SolidAt(x - 1, y, z) ||
				!_grid.SolidAt(x, y + 1, z) || !_grid.SolidAt(x, y - 1, z) ||
				!_grid.SolidAt(x, y, z + 1) || !_grid.SolidAt(x, y, z - 1);
		}

		private void WriteSurroundingTrees()
		{
			// Each anchor and crown below is a separate source transcription. There is
			// deliberately no reusable tree stamp: repeating one crown around this very
			// large composition made the framing read procedural and erased scale.
			(int x, int y, int z) = TreeOrigin(0);
			// TreeOrigin returns the authored runtime terrain top, while the offsets
			// below remain measured source courses. A local Fill keeps those unique
			// crowns intact and expands their offsets by the same integer site scale.
			void Fill(int x0, int x1, int y0, int y1, int z0, int z1, byte material) =>
				FillTree(x0, x1, y0, y1, z0, z1, y, material);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 7, y + 10, z - 2, z, Palette.LEAF_PINK);
			Fill(x, x + 3, y + 8, y + 11, z - 1, z + 2, Palette.LEAF_BLUSH);
			Fill(x - 1, x + 2, y + 10, y + 12, z + 1, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(1);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x - 1, y + 7, y + 9, z - 2, z + 1, Palette.LEAF_BLUSH);
			Fill(x, x + 3, y + 8, y + 11, z - 3, z, Palette.LEAF_PINK);
			Fill(x - 2, x + 1, y + 10, y + 12, z, z + 3, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(2);
			Fill(x, x, y, y + 9, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 8, y + 11, z - 4, z - 1, Palette.LEAF_PINK);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 1, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 2, y + 11, y + 14, z + 1, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(3);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 2, x + 1, y + 8, y + 10, z - 4, z - 1, Palette.LEAF_BLUSH);
			Fill(x + 1, x + 4, y + 7, y + 10, z - 1, z + 2, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 10, y + 13, z + 1, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(4);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x - 1, y + 7, y + 10, z - 3, z, Palette.LEAF_PINK);
			Fill(x, x + 3, y + 8, y + 11, z - 2, z + 2, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 1, y + 10, y + 12, z + 2, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(5);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 7, y + 10, z - 3, z, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 8, y + 11, z - 1, z + 3, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 11, y + 13, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(6);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 7, y + 9, z - 2, z + 1, Palette.LEAF_PINK);
			Fill(x, x + 3, y + 8, y + 11, z - 3, z, Palette.LEAF_BLUSH);
			Fill(x - 1, x + 2, y + 10, y + 12, z + 1, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(7);
			Fill(x, x, y, y + 9, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x - 1, y + 8, y + 11, z - 3, z, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 2, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 11, y + 14, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(8);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 2, x + 1, y + 7, y + 10, z - 3, z, Palette.LEAF_PINK);
			Fill(x + 1, x + 4, y + 8, y + 11, z, z + 3, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 2, y + 10, y + 12, z + 2, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(9);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x, y + 8, y + 10, z - 4, z - 1, Palette.LEAF_BLUSH);
			Fill(x, x + 3, y + 8, y + 12, z - 2, z + 2, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 11, y + 13, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(10);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 7, y + 10, z - 3, z, Palette.LEAF_PINK);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 2, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 1, y + 11, y + 13, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(11);
			Fill(x, x, y, y + 9, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x - 1, y + 8, y + 11, z - 4, z - 1, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 2, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 12, y + 14, z + 1, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(12);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 8, y + 11, z - 3, z, Palette.LEAF_PINK);
			Fill(x + 1, x + 4, y + 9, y + 12, z - 1, z + 3, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 2, y + 11, y + 13, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(13);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x + 1, y + 7, y + 10, z - 4, z - 1, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 8, y + 11, z, z + 3, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 10, y + 12, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(14);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x - 1, y + 8, y + 10, z - 2, z + 1, Palette.LEAF_PINK);
			Fill(x, x + 3, y + 9, y + 12, z - 3, z, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 2, y + 11, y + 13, z + 1, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(15);
			Fill(x, x, y, y + 9, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x, y + 8, y + 11, z - 4, z, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 2, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 12, y + 14, z + 1, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(16);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 7, y + 10, z - 3, z, Palette.LEAF_PINK);
			Fill(x + 1, x + 4, y + 8, y + 11, z - 1, z + 3, Palette.LEAF_BLUSH);
			Fill(x - 2, x + 2, y + 10, y + 12, z + 2, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(17);
			Fill(x, x, y, y + 8, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x - 1, y + 8, y + 11, z - 4, z - 1, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 2, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 11, y + 14, z + 2, z + 5, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(18);
			Fill(x, x, y, y + 7, z, z, Palette.TRUNK_ROSE);
			Fill(x - 3, x, y + 7, y + 10, z - 2, z + 1, Palette.LEAF_PINK);
			Fill(x, x + 3, y + 8, y + 11, z - 3, z, Palette.LEAF_BLUSH);
			Fill(x - 1, x + 2, y + 10, y + 12, z + 1, z + 4, Palette.LEAF_ROSE);

			(x, y, z) = TreeOrigin(19);
			Fill(x, x, y, y + 9, z, z, Palette.TRUNK_ROSE);
			Fill(x - 4, x, y + 8, y + 11, z - 4, z - 1, Palette.LEAF_BLUSH);
			Fill(x, x + 4, y + 9, y + 12, z - 2, z + 3, Palette.LEAF_PINK);
			Fill(x - 2, x + 2, y + 12, y + 14, z + 2, z + 5, Palette.LEAF_ROSE);
		}

		private void WriteNamed(string structureId, Action writer)
		{
			if (!string.IsNullOrEmpty(_activeStructure))
				throw new InvalidOperationException("Reference-site structure contexts may not nest.");
			ReferenceGroundPlanStructure structure = _plan.GetStructure(structureId);
			_activeStructure = structureId;
			_activeProjection = Projection(structure);
			_activeTouched = new HashSet<(int X, int Z)>();
			try
			{
				writer();
				if (_activeTouched.Count == 0)
					throw new InvalidOperationException(
						$"Structure '{structureId}' wrote no solid voxels inside its plan footprint.");
				if (!_activeTouched.SetEquals(_activeProjection))
				{
					(int X, int Z) missing = _activeProjection.First(cell =>
						!_activeTouched.Contains(cell));
					throw new InvalidOperationException(
						$"Structure '{structureId}' left canonical plan cell {missing.X},{missing.Z} unbuilt.");
				}
				int localBase = structure.BaseY ?? 0;
				foreach ((int sourceX, int sourceZ) in _activeProjection)
					if (!ExpandedCellHasFinalSolid(sourceX, sourceZ, Y(localBase)))
						throw new InvalidOperationException(
							$"Structure '{structureId}' removed every final runtime solid from " +
							$"canonical plan cell {sourceX},{sourceZ}.");
				foreach (List<int> rectangle in Rectangles(structure))
				{
					bool touched = false;
					for (int z = rectangle[1]; z <= rectangle[3] && !touched; z++)
					for (int x = rectangle[0]; x <= rectangle[2]; x++)
						if (_activeTouched.Contains((x, z))) { touched = true; break; }
					if (!touched)
						throw new InvalidOperationException(
							$"Structure '{structureId}' left one declared plan footprint entirely unbuilt.");
				}
			}
			finally
			{
				_activeStructure = "";
				_activeProjection = null;
				_activeTouched = null;
			}
		}

		private bool ExpandedCellHasFinalSolid(int localX, int localZ, int minimumY)
		{
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				(int x, int z) = RuntimeCell(localX, localZ, dx, dz);
				for (int y = Math.Max(0, minimumY); y < _grid.HeightAt(x, z); y++)
					if (Palette.IsSolid(_grid.At(x, y, z))) return true;
			}
			return false;
		}

		private static IEnumerable<List<int>> Rectangles(ReferenceGroundPlanStructure structure)
		{
			if (structure.Footprint.Count == 4) yield return structure.Footprint;
			foreach (List<int> rectangle in structure.Footprints) yield return rectangle;
		}

		private static HashSet<(int X, int Z)> Projection(ReferenceGroundPlanStructure structure)
		{
			return structure.ProjectionCells
				.Select(cell => (X: cell.X, Z: cell.Z))
				.ToHashSet();
		}

		private void WriteProjectionCourse(int absoluteY, byte material)
		{
			if (_activeProjection == null)
				throw new InvalidOperationException(
					"A canonical projection course may only be written inside a named structure.");
			foreach ((int x, int z) in _activeProjection.OrderBy(cell => cell.Z)
			         .ThenBy(cell => cell.X))
				Put(x, absoluteY, z, material);
		}

		private void WriteProjectionBase(byte material)
		{
			ReferenceGroundPlanStructure structure = _plan.GetStructure(_activeStructure);
			int localBase = structure.BaseY ?? throw new InvalidOperationException(
				$"Structure '{structure.Id}' has no audited baseY.");
			WriteProjectionCourse(Y(localBase), material);
		}

		private static TerrainMaterialStyle ResolveTerrainMaterialStyle(string material) =>
			material switch
			{
				// The site replaces atlas water as well as atlas land. Its dry shelves
				// therefore need the named Drowned Shallows profile, never whatever cap
				// happened to exist in the compiled column before the site painter ran.
				"natural-atlas" => new TerrainMaterialStyle(
					Palette.GRASS_LIGHT, Palette.SAND, Palette.STONE_PALE),
				"cliff-stone-and-turf" => new TerrainMaterialStyle(
					Palette.GRASS_LIGHT, Palette.SOIL, Palette.STONE_PALE),
				"reclaimed-cliff-terrace" => new TerrainMaterialStyle(
					Palette.GRASS_LIGHT, Palette.SOIL, Palette.STONE_PALE),
				"reclaimed-landing" => new TerrainMaterialStyle(
					Palette.GRASS_LIGHT, Palette.SAND, Palette.STONE_PALE),
				"worn-paving" => new TerrainMaterialStyle(
					Palette.PAVING, Palette.STONE_PALE, Palette.STONE_PALE),
				"worn-gate-stone" => new TerrainMaterialStyle(
					Palette.STONE_PALE, Palette.STONE_PALE, Palette.STONE_PALE),
				_ => throw new InvalidOperationException(
					$"Reference 1 has no terrain material style '{material}'.")
			};

		private static TerrainMaterialStyle ResolveSurfacePatchStyle(string material) =>
			material switch
			{
				"cool-paving" => new TerrainMaterialStyle(
					Palette.STONE, Palette.STONE_PALE, Palette.STONE_PALE),
				"worn-paving" => ResolveTerrainMaterialStyle("worn-paving"),
				"moss-paving" => new TerrainMaterialStyle(
					Palette.MOSS_STONE, Palette.STONE_PALE, Palette.STONE_PALE),
				// Reclamation is explicit dry turf. Sampling the previous column here
				// reintroduced drowned paving wherever the landing replaced water.
				"reclaimed-turf" => new TerrainMaterialStyle(
					Palette.GRASS_LIGHT, Palette.SOIL, Palette.STONE_PALE),
				_ => throw new InvalidOperationException(
					$"Reference 1 has no surface material style '{material}'.")
			};

		private void TerrainRectangle(List<int> rectangle, int localTop,
			TerrainMaterialStyle style)
		{
			for (int z = rectangle[1]; z <= rectangle[3]; z++)
			for (int x = rectangle[0]; x <= rectangle[2]; x++)
				TerrainSurface(x, z, localTop, style);
		}

		private void WaterRectangle(List<int> rectangle, int localSurface, int localBed)
		{
			for (int z = rectangle[1]; z <= rectangle[3]; z++)
			for (int x = rectangle[0]; x <= rectangle[2]; x++)
				WaterSurface(x, z, localSurface, localBed);
		}

		private void TerrainPolygon(List<List<int>> vertices, int localTop,
			TerrainMaterialStyle style)
		{
			if (vertices == null || vertices.Count < 3) return;
			int minX = vertices[0][0], maxX = vertices[0][0];
			int minZ = vertices[0][1], maxZ = vertices[0][1];
			foreach (List<int> vertex in vertices)
			{
				minX = Math.Min(minX, vertex[0]);
				maxX = Math.Max(maxX, vertex[0]);
				minZ = Math.Min(minZ, vertex[1]);
				maxZ = Math.Max(maxZ, vertex[1]);
			}
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
				if (InsidePolygon(x + .5f, z + .5f, vertices))
					TerrainSurface(x, z, localTop, style);
		}

		private void WaterPolygon(List<List<int>> vertices, int localSurface, int localBed)
		{
			if (vertices == null || vertices.Count < 3) return;
			int minX = vertices[0][0], maxX = vertices[0][0];
			int minZ = vertices[0][1], maxZ = vertices[0][1];
			foreach (List<int> vertex in vertices)
			{
				minX = Math.Min(minX, vertex[0]);
				maxX = Math.Max(maxX, vertex[0]);
				minZ = Math.Min(minZ, vertex[1]);
				maxZ = Math.Max(maxZ, vertex[1]);
			}
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
				if (InsidePolygon(x + .5f, z + .5f, vertices))
					WaterSurface(x, z, localSurface, localBed);
		}

		private static bool InsidePolygon(float x, float z, List<List<int>> vertices)
		{
			bool inside = false;
			for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
			{
				List<int> a = vertices[i], b = vertices[j];
				bool crosses = (a[1] > z) != (b[1] > z) &&
					x < (b[0] - a[0]) * (z - a[1]) / (float)(b[1] - a[1]) + a[0];
				if (crosses) inside = !inside;
			}
			return inside;
		}

		private void TerrainSurface(int localX, int localZ, int localTop,
			TerrainMaterialStyle style)
		{
			int absoluteTop = Y(localTop);
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
				WriteDryRuntimeColumn(localX, localZ, dx, dz, absoluteTop, style);
		}

		private void TerrainStairRectangle(List<int> rectangle, int localTop,
			TerrainMaterialStyle style, int ascendingZDirection, bool firstTread)
		{
			for (int z = rectangle[1]; z <= rectangle[3]; z++)
			for (int x = rectangle[0]; x <= rectangle[2]; x++)
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				int absoluteTop = StairRuntimeTop(localTop, dz,
					ascendingZDirection, firstTread);
				WriteDryRuntimeColumn(x, z, dx, dz, absoluteTop, style);
			}
		}

		private int StairRuntimeTop(int localTop, int offsetZ,
			int ascendingZDirection, bool firstTread)
		{
			if (firstTread) return Y(localTop);
			int half = RuntimePlanScale / 2;
			int progress = ascendingZDirection < 0 ? half - offsetZ : half + offsetZ;
			return Y(localTop) - (RuntimePlanScale - 1) + progress;
		}

		private void WriteDryRuntimeColumn(int localX, int localZ, int offsetX,
			int offsetZ, int absoluteTop, TerrainMaterialStyle style)
		{
			(int x, int z) = RuntimeCell(localX, localZ, offsetX, offsetZ);
			if (!_grid.InBounds(x, absoluteTop - 1, z))
				throw new InvalidOperationException(
					$"Reference 1 dry column {localX},{localZ} offset {offsetX},{offsetZ} " +
					$"at TopY {absoluteTop} leaves the runtime window.");
			int index = z * _data.Width + x;
			// A later measured dry shelf or stair must erase every compiled-water
			// channel field, not merely cover its mesh with a taller voxel column.
			_data.Height[index] = checked((ushort)absoluteTop);
			_data.WaterSurface[index] = 0;
			_data.Land[index] = 255;
			_data.Water[index] = 0;
			_data.Hydrology[index] = HydrologyDry;
			_data.Surface[index] = (byte)AtlasTerrainSurface.Cap;
			_data.Wetness[index] = 0;
			// Each named dry style owns the complete visible column. This matters most
			// where a source shelf replaces compiled water: inheriting that column's
			// pre-site underwater caps produced the rejected v4 pale/maroon quilt.
			_grid.RedescribeUnedited(x, z, absoluteTop,
				style.Cap, style.Sub, style.Deep);
			SurfaceCells++;
		}

		private void WaterSurface(int localX, int localZ, int localSurface, int localBed)
		{
			int absoluteSurface = Y(localSurface);
			int absoluteBed = Y(localBed);
			if (absoluteBed >= absoluteSurface)
				throw new InvalidOperationException(
					$"Reference 1 water bed {absoluteBed} is not below surface {absoluteSurface}.");
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				(int x, int z) = RuntimeCell(localX, localZ, dx, dz);
				if (!_grid.InBounds(x, absoluteBed - 1, z))
					throw new InvalidOperationException(
						$"Reference 1 water column {localX},{localZ} offset {dx},{dz} " +
						$"at bed TopY {absoluteBed} leaves the runtime window.");
				int index = z * _data.Width + x;
				_data.Height[index] = checked((ushort)absoluteBed);
				_data.WaterSurface[index] = checked((ushort)absoluteSurface);
				_data.Land[index] = 0;
				_data.Water[index] = 255;
				_data.Hydrology[index] = HydrologyChannel;
				_data.Surface[index] = (byte)AtlasTerrainSurface.Underwater;
				_data.Wetness[index] = 255;
				// Reference 1 shows a pale shallow bed through lavender water. A uniform
				// site-owned stone bed also avoids exposing the unrelated compiled biome
				// quilt after the exact channel polygon replaces it.
				_grid.RedescribeUnedited(x, z, absoluteBed, Palette.STONE_PALE,
					Palette.STONE_PALE, Palette.STONE_PALE);
				SurfaceCells++;
			}
		}

		private void AssertWetCell(int localX, int localZ, int localSurface, int localBed,
			string owner)
		{
			int absoluteSurface = Y(localSurface);
			int absoluteBed = Y(localBed);
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				(int x, int z) = RuntimeCell(localX, localZ, dx, dz);
				if (x < 0 || z < 0 || x >= _data.Width || z >= _data.Depth)
					throw new InvalidOperationException(
						$"Authored water '{owner}' leaves the materialised site window at {localX},{localZ}.");
				int index = z * _data.Width + x;
				if (_data.Height[index] != absoluteBed ||
				    _data.WaterSurface[index] != absoluteSurface ||
				    _data.Land[index] != 0 || _data.Water[index] != 255 ||
				    _data.Hydrology[index] != HydrologyChannel ||
				    _data.Surface[index] != (byte)AtlasTerrainSurface.Underwater ||
				    _data.Wetness[index] != 255 || _grid.Top[index] != absoluteBed ||
				    _grid.Cap[index] != Palette.STONE_PALE ||
				    _grid.Sub[index] != Palette.STONE_PALE ||
				    _grid.Deep[index] != Palette.STONE_PALE)
					throw new InvalidOperationException(
						$"Authored water '{owner}' failed wet/bed ownership at {localX},{localZ} offset {dx},{dz}.");
			}
		}

		private void AssertDryCell(int localX, int localZ, int localTop, string owner,
			TerrainMaterialStyle style)
		{
			int absoluteTop = Y(localTop);
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				(int x, int z) = RuntimeCell(localX, localZ, dx, dz);
				if (x < 0 || z < 0 || x >= _data.Width || z >= _data.Depth)
					throw new InvalidOperationException(
						$"Authored dry terrain '{owner}' leaves the materialised site window at {localX},{localZ}.");
				int index = z * _data.Width + x;
				if (_data.Height[index] != absoluteTop || _data.WaterSurface[index] != 0 ||
				    _data.Land[index] != 255 || _data.Water[index] != 0 ||
				    _data.Hydrology[index] != HydrologyDry ||
				    _data.Surface[index] != (byte)AtlasTerrainSurface.Cap ||
				    _data.Wetness[index] != 0 || _grid.Top[index] != absoluteTop ||
				    _grid.Cap[index] != style.Cap || _grid.Sub[index] != style.Sub ||
				    _grid.Deep[index] != style.Deep)
					throw new InvalidOperationException(
						$"Authored dry terrain '{owner}' failed water/material ownership at {localX},{localZ} offset {dx},{dz}.");
			}
		}

		private void AssertStairDryCell(int localX, int localZ, int localTop,
			string owner, TerrainMaterialStyle style, int ascendingZDirection,
			bool firstTread)
		{
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				int absoluteTop = StairRuntimeTop(localTop, dz,
					ascendingZDirection, firstTread);
				(int x, int z) = RuntimeCell(localX, localZ, dx, dz);
				if (x < 0 || z < 0 || x >= _data.Width || z >= _data.Depth)
					throw new InvalidOperationException(
						$"Authored stair '{owner}' leaves the materialised site window at {localX},{localZ}.");
				int index = z * _data.Width + x;
				if (_data.Height[index] != absoluteTop || _data.WaterSurface[index] != 0 ||
				    _data.Land[index] != 255 || _data.Water[index] != 0 ||
				    _data.Hydrology[index] != HydrologyDry ||
				    _data.Surface[index] != (byte)AtlasTerrainSurface.Cap ||
				    _data.Wetness[index] != 0 || _grid.Top[index] != absoluteTop ||
				    _grid.Cap[index] != style.Cap || _grid.Sub[index] != style.Sub ||
				    _grid.Deep[index] != style.Deep)
					throw new InvalidOperationException(
						$"Authored stair '{owner}' failed one-voxel subdivision at " +
						$"{localX},{localZ} offset {dx},{dz}; expected TopY {absoluteTop}.");
			}
		}

		private void RepaintSurface(int localX, int localZ, int localTop,
			TerrainMaterialStyle style)
		{
			int absoluteTop = Y(localTop);
			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			{
				(int x, int z) = RuntimeCell(localX, localZ, dx, dz);
				if (!_grid.InBounds(x, absoluteTop - 1, z))
					throw new InvalidOperationException(
						$"Reference 1 surface patch at {localX},{localZ} offset {dx},{dz} leaves the runtime window.");
				int index = z * _data.Width + x;
				if (_grid.Top[index] != absoluteTop) continue;
				_grid.Describe(x, z, absoluteTop, style.Cap, style.Sub, style.Deep);
				SurfaceCells++;
			}
		}

		private (int X, int Y, int Z) TreeOrigin(int index)
		{
			if (index < 0 || index >= _plan.SurroundingTrees.Count)
				throw new InvalidOperationException($"Ground plan has no tree anchor {index}.");
			List<int> point = _plan.SurroundingTrees[index];
			(int x, int z) = LocalCell(point[0], point[1]);
			return (point[0], _grid.HeightAt(x, z), point[1]);
		}

		private void FillTree(int x0, int x1, int y0, int y1,
			int z0, int z1, int baseY, byte material)
		{
			for (int sourceY = y0; sourceY <= y1; sourceY++)
			{
				int runtimeY = baseY + (sourceY - baseY) * RuntimePlanScale;
				for (int z = z0; z <= z1; z++)
				for (int x = x0; x <= x1; x++)
					Put(x, runtimeY, z, material);
			}
		}

		private int Y(int localY) => _verticalDatum + localY * RuntimePlanScale;

		private void Chip(int localX, int localY, int localZ)
		{
			// The previous 1x source-cell chip became a 3x3x3 cavity after the scale
			// correction: every notch was larger than the traveller and repeated the
			// same U silhouette. Keep the measured damage location, but resolve its
			// broken corner in ordinary runtime voxels inside that three-voxel envelope.
			int sideX = ((localX + localZ) & 1) == 0 ? 1 : -1;
			int sideZ = ((localX * 3 - localZ) & 1) == 0 ? 1 : -1;
			PutFine(localX, localY, localZ, sideX, 2, sideZ, Palette.AIR);
			PutFine(localX, localY, localZ, 0, 2, sideZ, Palette.AIR);
			PutFine(localX, localY, localZ, sideX, 2, 0, Palette.AIR);
			PutFine(localX, localY, localZ, sideX, 1, sideZ, Palette.AIR);
		}

		private void Fill(int x0, int x1, int y0, int y1, int z0, int z1, byte material)
		{
			if ((y0 - _verticalDatum) % RuntimePlanScale != 0 ||
			    (y1 - _verticalDatum) % RuntimePlanScale != 0)
				throw new InvalidOperationException(
					$"Reference 1 plan fill Y {y0}..{y1} is not aligned to runtime scale {RuntimePlanScale}.");
			for (int y = y0; y <= y1; y += RuntimePlanScale)
			for (int z = z0; z <= z1; z++)
			for (int x = x0; x <= x1; x++) Put(x, y, z, material);
		}

		private void Put(int localX, int absoluteY, int localZ, byte material)
		{
			if (!string.IsNullOrEmpty(_activeStructure))
			{
				if (_activeProjection == null || !_activeProjection.Contains((localX, localZ)))
					throw new InvalidOperationException(
						$"Structure '{_activeStructure}' attempted a voxel outside its canonical ground-plan footprint at {localX},{localZ}.");
				if (Palette.IsSolid(material)) _activeTouched?.Add((localX, localZ));
			}

			for (int dz = -RuntimePlanScale / 2; dz <= RuntimePlanScale / 2; dz++)
			for (int dx = -RuntimePlanScale / 2; dx <= RuntimePlanScale / 2; dx++)
			for (int dy = 0; dy < RuntimePlanScale; dy++)
				PutRuntime(localX, absoluteY + dy, localZ, dx, dz, material);
		}

		/// <summary>
		/// Write one runtime voxel inside a measured plan cell. Fine edits are the
		/// only place the provisional three-voxel transcription may recess a joint,
		/// chip a corner, or vary a masonry course without replacing the measured
		/// plan cell with another reusable architectural part.
		/// </summary>
		private void PutFine(int localX, int localY, int localZ,
			int offsetX, int offsetY, int offsetZ, byte material)
		{
			if (Math.Abs(offsetX) > RuntimePlanScale / 2 ||
			    Math.Abs(offsetZ) > RuntimePlanScale / 2 ||
			    offsetY < 0 || offsetY >= RuntimePlanScale)
				throw new ArgumentOutOfRangeException(nameof(offsetX),
					$"Reference 1 fine offset {offsetX},{offsetY},{offsetZ} leaves one {RuntimePlanScale}x{RuntimePlanScale}x{RuntimePlanScale} source cell.");
			PutRuntime(localX, Y(localY) + offsetY, localZ,
				offsetX, offsetZ, material);
		}

		private void PutRuntime(int localX, int absoluteY, int localZ,
			int offsetX, int offsetZ, byte material)
		{
			(int x, int z) = RuntimeCell(localX, localZ, offsetX, offsetZ);
			if (!_grid.InBounds(x, absoluteY, z))
				throw new InvalidOperationException(
					$"Reference 1 voxel {localX},{absoluteY},{localZ} offset {offsetX},{offsetZ} leaves the runtime window.");
			_grid.Set(x, absoluteY, z, material);
			if (Palette.IsSolid(material))
			{
				int index = z * _grid.Size + x;
				if (absoluteY + 1 > _grid.Heights[index])
					_grid.Heights[index] = (short)(absoluteY + 1);
			}
			Voxels++;
		}

		private (int x, int z) LocalCell(int localX, int localZ)
			=> RuntimeCell(localX, localZ, 0, 0);

		private (int x, int z) RuntimeCell(int localX, int localZ,
			int offsetX, int offsetZ)
		{
			if (Math.Abs(offsetX) > RuntimePlanScale / 2 ||
			    Math.Abs(offsetZ) > RuntimePlanScale / 2)
				throw new ArgumentOutOfRangeException(nameof(offsetX),
					$"Reference 1 runtime offset {offsetX},{offsetZ} leaves one source cell.");

			bool mirrorX = _plan.CoordinateContract.RuntimeMirrorX == true;
			int planX = mirrorX ? -localX : localX;
			int runtimeX = planX * RuntimePlanScale + (mirrorX ? -offsetX : offsetX);
			int runtimeZ = localZ * RuntimePlanScale + offsetZ;
			BlockPoint global = _site.ToGlobalRuntime(new PlanPoint
			{
				X = runtimeX,
				Z = runtimeZ,
			});
			return (global.X - _data.OriginX, global.Z - _data.OriginZ);
		}
	}
}
