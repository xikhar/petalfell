using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Petalfell.Core;

namespace Petalfell.World.Sites;

/// <summary>
/// Literal voxel transcription of world-new/reference-10.png.
///
/// The version-2 ground plan owns every horizontal coordinate. This file owns
/// only the unique vertical blocks, openings, damage and surface wear visible
/// in that reference. Fill is storage shorthand inside one named mass; it is
/// never a reusable stair, wall, portal, pillar or ruin generator.
/// </summary>
public static class Reference10GroveCourt
{
	public const string BuilderId = "reference-10-grove-court-v1";

	public static ReferenceSiteStatistics Build(AtlasSectorWindow window,
		ReferenceSiteDefinition site)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (site == null) throw new ArgumentNullException(nameof(site));
		if (site.BuilderId != BuilderId)
			throw new InvalidOperationException($"Reference 10 cannot build '{site.BuilderId}'.");
		ReferenceSiteGroundPlan plan = ReferenceSiteGroundPlan.Load(site);
		var build = new Blueprint(window, site, plan);
		build.Write();
		return new ReferenceSiteStatistics(build.SurfaceCells, build.Voxels);
	}

	private sealed class Blueprint
	{
		private readonly ReferenceSiteDefinition _site;
		private readonly ReferenceSiteGroundPlan _plan;
		private readonly VoxelGrid _grid;
		private readonly AtlasSectorData _data;
		private readonly byte[] _naturalCap;
		private readonly byte[] _naturalSub;
		private readonly byte[] _naturalDeep;
		private readonly HashSet<(int X, int Z)> _pavingCells = new();
		private string _activeStructure = "";
		private HashSet<(int X, int Z)> _activeProjection;
		private HashSet<(int X, int Z)> _activeTouched;

		public int SurfaceCells { get; private set; }
		public int Voxels { get; private set; }

		public Blueprint(AtlasSectorWindow window, ReferenceSiteDefinition site,
			ReferenceSiteGroundPlan plan)
		{
			_site = site;
			_plan = plan;
			_grid = window.Grid;
			_data = window.Data;
			_naturalCap = (byte[])_grid.Cap.Clone();
			_naturalSub = (byte[])_grid.Sub.Clone();
			_naturalDeep = (byte[])_grid.Deep.Clone();
		}

		public void Write()
		{
			// VoxelGrid intentionally forbids replacing terrain after a sparse edit
			// touches the same 32-block tile. The plan's platforms, stairs and exact
			// surface patches therefore precede the first architectural voxel.
			WritePlannedTerrainAndStairs();
			WriteAuthoredSurfaceWear();

			WriteNamed("north-arch-facade", WriteNorthArchFacade);
			WriteNamed("inner-west-spine", WriteInnerWestSpine);
			WriteNamed("east-inner-spine-south-west", WriteEastInnerSpineSouthWest);
			WriteNamed("east-inner-spine-south-east", WriteEastInnerSpineSouthEast);
			WriteNamed("east-inner-spine-central", WriteEastInnerSpineCentral);
			WriteNamed("east-inner-spine-north", WriteEastInnerSpineNorth);
			WriteNamed("east-central-outer-low-wall", WriteEastCentralOuterLowWall);
			WriteNamed("east-central-inner-low-wall-north", WriteEastCentralInnerLowWallNorth);
			WriteNamed("east-central-inner-low-wall-east", WriteEastCentralInnerLowWallEast);
			WriteNamed("east-north-branch", WriteEastNorthBranch);
			WriteNamed("rear-east-stele", WriteRearEastStele);
			WriteNamed("east-south-enclosure", WriteEastSouthEnclosure);
			WriteNamed("west-enclosure", WriteWestEnclosure);
			WriteNamed("south-west-wall-south", WriteSouthWestWallSouth);
			WriteNamed("south-west-wall-north", WriteSouthWestWallNorth);
			WriteNamed("south-gate-west", WriteSouthGateWest);
			WriteNamed("south-gate-east", WriteSouthGateEast);
			WriteNamed("west-stair-shoulder", WriteWestStairShoulder);
			WriteNamed("west-stair-shoulder-upper", WriteWestStairShoulderUpper);
			WriteNamed("east-inner-rubble-north", WriteEastInnerRubbleNorth);
			WriteNamed("west-enclosure-rubble", WriteWestEnclosureRubble);
			WriteNamed("north-arch-west-foot-rubble", WriteNorthArchWestFootRubble);
			WriteNamed("north-arch-east-foot-rubble", WriteNorthArchEastFootRubble);
			WriteNamed("west-stair-shoulder-rubble", WriteWestStairShoulderRubble);
			WriteNamed("east-middle-south-rubble", WriteEastMiddleSouthRubble);
			WriteNamed("east-middle-central-rubble", WriteEastMiddleCentralRubble);
			WriteNamed("east-south-enclosure-rubble", WriteEastSouthEnclosureRubble);
			WriteNamed("south-gate-west-rubble", WriteSouthGateWestRubble);
			WriteNamed("south-gate-east-rubble", WriteSouthGateEastRubble);

			WriteSurroundingTrees();
		}

		private void WritePlannedTerrainAndStairs()
		{
			// Terrain is an authored painter's stack. The broad y110 shelves are
			// written first, then their y112/y113 remnants and finally the occupied
			// courts. This produces the separated one-block ledges in the reference
			// without a runtime hash moving an edge between builds.
			foreach (ReferenceGroundPlanTerrain terrain in _plan.Terrain)
			{
				// The green outer polygon is a grading/exclusion mask traced from the
				// author's sketch. It must not flatten the atlas terrain it surrounds.
				if (terrain.WriteMode == "preserve-atlas") continue;
				int surfaceY = terrain.SurfaceY ?? throw new InvalidOperationException(
					$"Terrain '{terrain.Id}' has no surfaceY after audit.");
				bool paving = terrain.Material == "worn-paving";
				if (terrain.Footprint.Count == 4)
					TerrainRectangle(terrain.Footprint, surfaceY, paving);
				else
					TerrainPolygon(terrain.Polygon, surfaceY, paving);
			}

			foreach (ReferenceGroundPlanStructure stair in _plan.Structures)
			{
				if (stair.Kind != "stair") continue;
				foreach (ReferenceGroundPlanTread tread in stair.Treads)
				{
					// topY is VoxelGrid.Top: the first empty/walkable Y. Placing a
					// block at topY was the old off-by-one that made the final tread
					// stand above, rather than join, the y114 landing.
					TerrainRectangle(tread.Footprint, tread.TopY!.Value, true);
				}
			}
		}

		private void WriteNorthArchFacade()
		{
			// Reference 10 shows a broad, flat wall remnant with a small doorway,
			// not the thin symmetric Gothic ring from v8.  Its front three planes are
			// a connected masonry mass; x2..7 remains clear through y120 and narrows
			// once at y121 beneath the damaged lintel.
			Fill(-3, 1, 114, 125, 20, 22, Palette.STONE_PALE);
			Fill(8, 12, 114, 125, 20, 22, Palette.STONE_PALE);
			Fill(2, 2, 121, 125, 20, 22, Palette.STONE_PALE);
			Fill(7, 7, 121, 125, 20, 22, Palette.STONE_PALE);
			Fill(3, 6, 122, 125, 20, 22, Palette.STONE_PALE);
			Fill(-3, 1, 114, 114, 20, 22, Palette.STONE);
			Fill(8, 12, 114, 114, 20, 22, Palette.STONE);

			// A shallower rear plane makes the facade read as a thick ruin without
			// turning it into a second parallel wall.
			Fill(-3, 1, 114, 122, 23, 23, Palette.STONE_PALE);
			Fill(8, 12, 114, 122, 23, 23, Palette.STONE_PALE);
			Fill(2, 7, 122, 124, 23, 23, Palette.STONE_PALE);
			Fill(-3, 1, 114, 114, 23, 23, Palette.STONE);
			Fill(8, 12, 114, 114, 23, 23, Palette.STONE);

			// The crown is broad and almost flat, but its three missing bites are
			// deliberately asymmetric like the source silhouette.
			Fill(-3, 0, 126, 126, 20, 22, Palette.STONE_PALE);
			Fill(2, 9, 126, 126, 20, 22, Palette.STONE_PALE);
			Fill(11, 12, 126, 126, 20, 22, Palette.STONE_PALE);

			// The deeper overhead footprint is fallen crown masonry at ankle height.
			Fill(-3, 12, 114, 114, 24, 26, Palette.STONE);
			Fill(-3, 1, 114, 114, 27, 28, Palette.STONE);
			Fill(4, 8, 114, 114, 27, 28, Palette.STONE);
			Fill(10, 12, 114, 114, 27, 28, Palette.STONE);
			Fill(-2, 2, 115, 115, 24, 24, Palette.STONE_WARM);
			Fill(5, 10, 115, 115, 25, 25, Palette.STONE);
			Fill(-3, 0, 115, 115, 27, 27, Palette.STONE_PALE);
			Fill(4, 6, 115, 115, 28, 28, Palette.MOSS_STONE);
			Fill(10, 11, 115, 115, 27, 27, Palette.STONE_WARM);

			// Faded blocks follow visible courses rather than a per-cell hash.
			Fill(-3, 1, 116, 116, 20, 20, Palette.STONE_WARM);
			Fill(8, 12, 119, 119, 22, 22, Palette.STONE);
			Fill(2, 7, 123, 123, 20, 20, Palette.STONE_WARM);
			// Moss follows narrow connected seams on the source-facing plane. The
			// rejected isolated green cubes read as confetti rather than age.
			Fill(-3, -3, 119, 122, 20, 20, Palette.MOSS_STONE);
			Put(-1, 124, 22, Palette.STONE);
			Fill(0, 1, 125, 125, 20, 20, Palette.MOSS_STONE);
			Put(4, 126, 20, Palette.AIR);
			Put(9, 126, 22, Palette.STONE_WARM);
			Fill(11, 11, 116, 118, 20, 20, Palette.MOSS_STONE);
		}

		private void WriteInnerWestSpine()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(-10, -8, 115, 115, 10, 14, Palette.STONE_PALE);
			Fill(-11, -10, 115, 116, 15, 18, Palette.STONE_PALE);
			Fill(-11, -10, 117, 123, 16, 17, Palette.STONE_PALE);
			Fill(-11, -10, 117, 117, 16, 17, Palette.STONE);
			Fill(-10, -10, 115, 118, 21, 23, Palette.STONE_PALE);
			Fill(-11, -11, 115, 116, 26, 29, Palette.STONE_PALE);
			Fill(-9, -6, 115, 116, 30, 30, Palette.STONE_PALE);
			Fill(-5, -3, 115, 115, 31, 31, Palette.STONE_WARM);
			Put(-9, 115, 11, Palette.MOSS_STONE);
			Fill(-11, -11, 120, 121, 16, 16, Palette.STONE_WARM);
			Put(-11, 122, 16, Palette.MOSS_STONE);
			Put(-10, 117, 22, Palette.STONE_WARM);
			Put(-11, 115, 28, Palette.MOSS_STONE);
		}

		private void WriteEastInnerSpineSouthWest()
		{
			// The two lower east precincts are genuinely detached terrain slabs.  The
			// wall trace therefore stops before the two-cell channel instead of hiding
			// it beneath an apparently continuous masonry spine.
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(13, 13, 115, 116, -17, -11, Palette.STONE_PALE);
			Put(13, 116, -12, Palette.MOSS_STONE);
		}

		private void WriteEastInnerSpineSouthEast()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(17, 18, 115, 116, -16, -15, Palette.STONE_PALE);
			Fill(17, 18, 117, 125, -16, -15, Palette.STONE_PALE);
			Fill(17, 18, 117, 117, -16, -15, Palette.STONE);
			Fill(17, 17, 120, 121, -15, -15, Palette.STONE_WARM);
			Put(17, 122, -15, Palette.MOSS_STONE);
		}

		private void WriteEastInnerSpineCentral()
		{
			// The overhead and locked view read this precinct as four equal square
			// survivors carried by one two-cell stylobate.  The earlier 1x1 B-D
			// needles made a mixed post collection even though their canonical bases
			// were already two cells wide.  Keep every surviving shaft 2x2 and put
			// damage in the material courses, never in its cross-section.
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(14, 14, 115, 115, -6, 0, Palette.STONE_WARM);
			Fill(13, 13, 115, 116, 7, 13, Palette.STONE_PALE);

			// The shaft begins on the first course above the two-course stylobate.
			// Starting at y117 left an actual air slice at y116, which made the four
			// survivors look like unrelated slabs hovering over the foundation.
			Fill(15, 16, 116, 126, 3, 4, Palette.STONE_PALE);
			Fill(15, 16, 117, 117, 3, 4, Palette.STONE);
			Fill(20, 21, 116, 124, 3, 4, Palette.STONE_PALE);
			Fill(20, 21, 117, 117, 3, 4, Palette.STONE);
			Fill(20, 21, 116, 125, -2, -1, Palette.STONE_PALE);
			Fill(20, 21, 117, 117, -2, -1, Palette.STONE);
			Fill(20, 21, 116, 123, -8, -7, Palette.STONE_PALE);
			Fill(20, 21, 117, 117, -8, -7, Palette.STONE);

			// Faded courses are narrow, contiguous stains on one corner. Full-width
			// pink rings made the shafts look striped and weakened their square read.
			Fill(15, 15, 121, 122, 3, 3, Palette.STONE_WARM);
			Fill(15, 15, 123, 125, 3, 3, Palette.MOSS_STONE);
			Fill(20, 20, 120, 121, 3, 3, Palette.STONE_WARM);
			Put(20, 122, 3, Palette.MOSS_STONE);
			Fill(21, 21, 122, 123, -1, -1, Palette.STONE_WARM);
			Put(21, 124, -1, Palette.MOSS_STONE);
			Fill(20, 20, 120, 121, -8, -8, Palette.STONE_WARM);
			Put(20, 122, -8, Palette.MOSS_STONE);
		}

		private void WriteEastInnerSpineNorth()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(14, 14, 115, 117, 21, 23, Palette.STONE_PALE);
			Fill(13, 13, 115, 115, 27, 31, Palette.STONE_WARM);
			Put(14, 117, 21, Palette.MOSS_STONE);
		}

		private void WriteEastCentralOuterLowWall()
		{
			// Source +X becomes runtime world -X and therefore the locked camera's
			// visible-right direction.  The terrain edge already had the right extent;
			// this low return spreads occupied ruin across it without adding a pillar.
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(29, 33, 115, 115, 0, 1, Palette.STONE_PALE);
			Fill(35, 38, 115, 116, 0, 1, Palette.STONE_PALE);
			Fill(39, 40, 115, 115, 1, 5, Palette.STONE_WARM);
			Fill(39, 40, 115, 116, 7, 10, Palette.STONE_PALE);
			Fill(39, 40, 115, 115, 12, 14, Palette.STONE_PALE);
			Put(34, 115, 0, Palette.MOSS_STONE);
			Put(39, 116, 8, Palette.AIR);
			Put(40, 115, 13, Palette.MOSS_STONE);
		}

		private void WriteEastCentralInnerLowWallNorth()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(20, 21, 115, 115, 9, 11, Palette.STONE_PALE);
			Fill(20, 24, 115, 115, 12, 13, Palette.STONE_PALE);
			Fill(26, 27, 115, 116, 12, 13, Palette.STONE_WARM);
			Put(20, 115, 10, Palette.MOSS_STONE);
			Put(25, 115, 12, Palette.MOSS_STONE);
		}

		private void WriteEastCentralInnerLowWallEast()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(31, 32, 115, 115, 4, 6, Palette.STONE_PALE);
			Fill(31, 33, 115, 115, 4, 5, Palette.STONE_WARM);
			Fill(31, 31, 115, 116, 7, 8, Palette.STONE_PALE);
			Put(32, 115, 8, Palette.MOSS_STONE);
		}

		private void WriteEastNorthBranch()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(17, 22, 115, 116, 22, 22, Palette.STONE_PALE);
			Fill(25, 30, 115, 115, 23, 23, Palette.STONE_WARM);
			Fill(32, 36, 115, 116, 22, 23, Palette.STONE_PALE);
			Fill(35, 36, 115, 116, 25, 31, Palette.STONE_PALE);
			Fill(35, 36, 117, 122, 25, 26, Palette.STONE_PALE);
			Fill(35, 36, 117, 117, 25, 26, Palette.STONE);
			Fill(35, 36, 117, 118, 29, 30, Palette.STONE_PALE);
			Fill(35, 35, 119, 120, 25, 25, Palette.STONE_WARM);
			Put(18, 116, 22, Palette.MOSS_STONE);
			Put(29, 115, 23, Palette.MOSS_STONE);
			Put(35, 121, 25, Palette.MOSS_STONE);
		}

		private void WriteRearEastStele()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(19, 20, 115, 120, 34, 35, Palette.STONE_PALE);
			Fill(19, 20, 115, 115, 34, 35, Palette.STONE);
			Fill(19, 19, 118, 119, 34, 34, Palette.STONE_WARM);
			Fill(19, 20, 121, 121, 34, 34, Palette.STONE_WARM);
			Fill(19, 19, 119, 120, 35, 35, Palette.MOSS_STONE);
			Put(20, 121, 34, Palette.AIR);
		}

		private void WriteEastSouthEnclosure()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(115, Palette.STONE_PALE);
			Fill(21, 22, 115, 116, -23, -20, Palette.STONE_PALE);
			Fill(21, 22, 117, 119, -23, -22, Palette.STONE_PALE);
			Fill(21, 22, 117, 117, -23, -22, Palette.STONE);
			Fill(24, 28, 115, 115, -23, -23, Palette.STONE_WARM);
			Fill(31, 33, 115, 116, -23, -22, Palette.STONE_PALE);
			Fill(32, 33, 115, 116, -21, -21, Palette.STONE_PALE);
			Fill(32, 33, 115, 123, -16, -15, Palette.STONE_PALE);
			Fill(32, 33, 117, 117, -16, -15, Palette.STONE);
			Fill(32, 32, 120, 121, -16, -16, Palette.STONE_WARM);
			Fill(32, 32, 115, 117, -13, -12, Palette.STONE_WARM);
			Fill(21, 21, 118, 118, -23, -23, Palette.STONE_WARM);
			Put(21, 119, -23, Palette.MOSS_STONE);
			Put(27, 115, -23, Palette.MOSS_STONE);
			Fill(33, 33, 121, 123, -15, -15, Palette.MOSS_STONE);
		}

		private void WriteWestEnclosure()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(110, Palette.STONE_PALE);
			Fill(-26, -25, 110, 111, -6, -2, Palette.STONE_PALE);
			Fill(-26, -25, 111, 112, 4, 5, Palette.STONE_PALE);
			Fill(-26, -25, 111, 111, 4, 5, Palette.STONE);
			Fill(-24, -20, 110, 111, 8, 9, Palette.STONE_PALE);
			Fill(-19, -17, 110, 110, 9, 9, Palette.STONE_WARM);
			Fill(-15, -14, 110, 112, 5, 8, Palette.STONE_PALE);
			Fill(-15, -14, 111, 112, -1, 0, Palette.STONE_PALE);
			Fill(-15, -14, 111, 111, -1, 0, Palette.STONE);
			Put(-25, 111, -2, Palette.MOSS_STONE);
			Put(-26, 112, 5, Palette.MOSS_STONE);
			Put(-15, 112, 7, Palette.STONE_WARM);
			Put(-15, 112, 0, Palette.MOSS_STONE);
		}

		private void WriteSouthWestWallSouth()
		{
			// The former single wall ran unbroken beside the whole court. The marked
			// player-level route is now a real four-cell plan gap, so its south and
			// north pieces are separate audited masses rather than a runtime carve.
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(110, Palette.STONE_PALE);
			Fill(-15, -12, 110, 111, -28, -27, Palette.STONE_WARM);
			Fill(-11, -10, 110, 111, -24, -21, Palette.STONE_PALE);
			Fill(-11, -10, 111, 111, -20, -19, Palette.STONE_PALE);
			Fill(-11, -10, 111, 111, -20, -19, Palette.STONE);
			Fill(-10, -10, 110, 111, -13, -11, Palette.STONE_PALE);
			Put(-11, 111, -20, Palette.MOSS_STONE);
			Put(-10, 112, -12, Palette.STONE_WARM);
		}

		private void WriteSouthWestWallNorth()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(110, Palette.STONE_PALE);
			Fill(-11, -10, 110, 111, -5, -3, Palette.STONE_PALE);
			Fill(-11, -10, 111, 112, -2, -1, Palette.STONE_PALE);
			Fill(-11, -10, 111, 111, -2, -1, Palette.STONE);
			Fill(-10, -10, 110, 111, 7, 8, Palette.STONE_PALE);
			Put(-11, 112, -1, Palette.MOSS_STONE);
		}

		private void WriteSouthGateWest()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(110, Palette.STONE_PALE);
			Fill(-9, -8, 110, 111, -29, -25, Palette.STONE_PALE);
			Fill(-9, -9, 112, 112, -25, -25, Palette.STONE);
			Fill(-9, -8, 111, 112, -20, -19, Palette.STONE_PALE);
			Fill(-9, -8, 111, 111, -20, -19, Palette.STONE);
			Fill(-8, -5, 110, 111, -16, -15, Palette.STONE_WARM);
			Put(-9, 111, -25, Palette.MOSS_STONE);
			Put(-9, 112, -20, Palette.MOSS_STONE);
		}

		private void WriteSouthGateEast()
		{
			WriteProjectionBase(Palette.STONE);
			WriteProjectionCourse(110, Palette.STONE_PALE);
			Fill(7, 8, 110, 111, -29, -24, Palette.STONE_PALE);
			Fill(8, 8, 112, 112, -25, -25, Palette.STONE);
			Fill(7, 8, 110, 120, -20, -19, Palette.STONE_PALE);
			Fill(7, 8, 110, 110, -20, -19, Palette.STONE);
			Fill(7, 7, 115, 116, -20, -20, Palette.STONE_WARM);
			Fill(7, 11, 110, 111, -16, -15, Palette.STONE_PALE);
			Fill(14, 18, 110, 111, -29, -28, Palette.STONE_WARM);
			Put(8, 113, -25, Palette.MOSS_STONE);
			Fill(7, 7, 118, 120, -19, -19, Palette.MOSS_STONE);
		}

		private void WriteWestStairShoulder()
		{
			WriteProjectionBase(Palette.STONE);
			// These are two-cell graded stair cheeks, not a procession of thin posts.
			Fill(-5, -4, 110, 110, 1, 2, Palette.STONE_PALE);
			Fill(-5, -4, 110, 111, 3, 4, Palette.STONE_PALE);
			Fill(-5, -4, 110, 112, 5, 6, Palette.STONE_PALE);
			Fill(-5, -4, 110, 113, 7, 8, Palette.STONE_PALE);
			Fill(-5, -4, 110, 114, 9, 9, Palette.STONE_PALE);
			Fill(-7, -6, 110, 111, 8, 9, Palette.STONE_PALE);
			Fill(-7, -6, 112, 122, 8, 9, Palette.STONE_PALE);
			Fill(-7, -6, 112, 112, 8, 9, Palette.STONE);
			Fill(-7, -7, 118, 119, 8, 8, Palette.STONE_WARM);
			Fill(-7, -7, 120, 122, 9, 9, Palette.MOSS_STONE);
			Put(-5, 112, 6, Palette.STONE_WARM);
		}

		private void WriteWestStairShoulderUpper()
		{
			WriteProjectionBase(Palette.STONE);
			Fill(-5, -4, 115, 116, 10, 12, Palette.STONE_PALE);
			Fill(-7, -7, 115, 116, 10, 11, Palette.STONE_WARM);
			Put(-5, 116, 11, Palette.MOSS_STONE);
		}

		private void WriteEastInnerRubbleNorth()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(25, 26, 115, 115, 9, 10, Palette.STONE_WARM);
			Fill(27, 27, 115, 116, 9, 10, Palette.STONE_PALE);
			Fill(28, 28, 115, 115, 7, 7, Palette.MOSS_STONE);
			Put(24, 114, 8, Palette.MOSS_STONE);
			Put(29, 114, 10, Palette.STONE_WARM);
		}

		private void WriteWestEnclosureRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-21, -20, 110, 110, -2, -1, Palette.STONE_WARM);
			Fill(-19, -19, 110, 111, 0, 1, Palette.STONE_PALE);
			Fill(-18, -18, 110, 110, 1, 2, Palette.MOSS_STONE);
			Put(-22, 109, -1, Palette.MOSS_STONE);
			Put(-18, 109, 3, Palette.STONE_WARM);
		}

		private void WriteNorthArchWestFootRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-5, -4, 115, 115, 18, 18, Palette.STONE_WARM);
			Fill(-3, -3, 115, 116, 19, 19, Palette.STONE);
			Put(-7, 114, 18, Palette.STONE);
			Put(-5, 114, 17, Palette.MOSS_STONE);
			Put(-3, 114, 20, Palette.MOSS_STONE);
		}

		private void WriteNorthArchEastFootRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(10, 11, 115, 115, 18, 18, Palette.STONE_WARM);
			Fill(12, 12, 115, 116, 19, 19, Palette.STONE);
			Put(9, 114, 18, Palette.STONE);
			Put(11, 115, 19, Palette.MOSS_STONE);
		}

		private void WriteWestStairShoulderRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-10, -9, 110, 110, 0, 0, Palette.STONE_WARM);
			Fill(-8, -8, 110, 111, 1, 1, Palette.STONE);
			Put(-12, 109, 0, Palette.STONE);
			Put(-9, 109, -1, Palette.MOSS_STONE);
			Put(-8, 109, 2, Palette.STONE_WARM);
		}

		private void WriteEastMiddleSouthRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(18, 18, 115, 116, -11, -11, Palette.STONE);
			Put(17, 114, -11, Palette.STONE_WARM);
			Put(18, 114, -12, Palette.MOSS_STONE);
		}

		private void WriteEastMiddleCentralRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(20, 20, 115, 115, -8, -7, Palette.MOSS_STONE);
			Put(19, 114, -8, Palette.STONE_WARM);
		}

		private void WriteEastSouthEnclosureRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(26, 28, 115, 115, -20, -20, Palette.STONE_WARM);
			Fill(27, 27, 115, 116, -21, -21, Palette.STONE);
			Fill(30, 30, 115, 115, -18, -17, Palette.MOSS_STONE);
			Put(24, 114, -19, Palette.STONE);
			Put(29, 114, -19, Palette.MOSS_STONE);
		}

		private void WriteSouthGateWestRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(-5, -3, 110, 110, -27, -27, Palette.STONE_WARM);
			Fill(-4, -4, 110, 111, -28, -28, Palette.STONE);
			Fill(-2, -2, 110, 110, -25, -24, Palette.MOSS_STONE);
			Put(-7, 109, -26, Palette.STONE);
			Put(-3, 109, -26, Palette.MOSS_STONE);
		}

		private void WriteSouthGateEastRubble()
		{
			WriteProjectionBase(Palette.STONE_PALE);
			Fill(9, 11, 110, 110, -26, -26, Palette.STONE_WARM);
			Fill(10, 10, 110, 111, -27, -27, Palette.STONE);
			Fill(12, 12, 110, 110, -24, -23, Palette.MOSS_STONE);
			Put(7, 109, -25, Palette.STONE);
			Put(11, 109, -25, Palette.MOSS_STONE);
		}

		private void WriteAuthoredSurfaceWear()
		{
			// These exact, non-overlapping cells are part of the canonical top plan.
			// Keeping them there prevents a runtime decoration pass from silently
			// widening a court or inventing a paving stripe outside the source trace.
			foreach (ReferenceGroundPlanSurfacePatch patch in _plan.SurfacePatches)
			{
				int surfaceY = _plan.GetTerrain(patch.TerrainId).SurfaceY ??
					throw new InvalidOperationException(
						$"Surface patch '{patch.Id}' has no audited terrain height.");
				byte material = patch.Material switch
				{
					"worn-paving" => Palette.PAVING,
					"warm-paving" => Palette.STONE_WARM,
					"cool-paving" => Palette.STONE,
					"moss-paving" => Palette.MOSS_STONE,
					_ => throw new InvalidOperationException(
						$"Reference 10 has no surface material '{patch.Material}'.")
				};
				foreach (ReferenceGroundPlanCell cell in patch.EffectiveCells
				         .OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
				{
					_pavingCells.Add((cell.X, cell.Z));
					RepaintSurface(cell.X, cell.Z, surfaceY, material);
				}
			}
		}

		private void WriteSurroundingTrees()
		{
			// The overhead pass owns eighteen distinct anchors. Crowns use separated
			// two-to-four-cell lobes with air between them; the rejected pass used
			// three huge cuboids per tree and overwhelmed the architecture.
			Tree(0, 8, B(-3,-1,7,9,-3,-1,Palette.LEAF_PINK), B(0,2,8,10,-2,0,Palette.LEAF_BLUSH), B(-2,1,10,12,0,2,Palette.LEAF_ROSE), B(2,4,9,11,1,3,Palette.LEAF_PINK), B(-3,-1,9,11,2,4,Palette.LEAF_BLUSH));
			Tree(1, 7, B(-3,-1,7,9,-2,0,Palette.LEAF_BLUSH), B(0,2,8,11,-3,-1,Palette.LEAF_PINK), B(-1,2,10,12,0,2,Palette.LEAF_ROSE), B(2,3,8,10,2,4,Palette.LEAF_PINK), B(-3,-1,9,11,2,3,Palette.LEAF_BLUSH));
			Tree(2, 8, B(-4,-1,8,10,-3,-1,Palette.LEAF_PINK), B(0,3,8,11,-2,1,Palette.LEAF_BLUSH), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,5,Palette.LEAF_PINK), B(-4,-2,10,12,3,5,Palette.LEAF_BLUSH));
			Tree(3, 7, B(-3,-1,7,9,-3,0,Palette.LEAF_BLUSH), B(0,2,8,10,-2,1,Palette.LEAF_PINK), B(-2,1,10,12,1,3,Palette.LEAF_ROSE), B(2,3,9,11,2,4,Palette.LEAF_PINK));
			Tree(4, 8, B(-3,0,7,9,-3,-1,Palette.LEAF_PINK), B(1,3,8,10,-2,1,Palette.LEAF_BLUSH), B(-2,1,10,12,0,3,Palette.LEAF_ROSE), B(2,4,9,11,2,4,Palette.LEAF_PINK), B(-3,-1,9,11,3,4,Palette.LEAF_BLUSH));
			Tree(5, 9, B(-4,-1,8,10,-4,-1,Palette.LEAF_BLUSH), B(0,3,9,11,-3,0,Palette.LEAF_PINK), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,5,10,12,1,4,Palette.LEAF_PINK), B(-4,-2,10,12,3,5,Palette.LEAF_BLUSH), B(0,2,12,14,3,5,Palette.LEAF_PINK));
			Tree(6, 7, B(-3,-1,7,9,-3,-1,Palette.LEAF_PINK), B(0,2,8,10,-2,1,Palette.LEAF_BLUSH), B(-2,1,10,12,0,2,Palette.LEAF_ROSE), B(2,4,9,11,1,3,Palette.LEAF_PINK), B(-3,-1,9,11,2,4,Palette.LEAF_BLUSH));
			Tree(7, 8, B(-4,-1,8,10,-3,-1,Palette.LEAF_BLUSH), B(0,3,8,11,-2,1,Palette.LEAF_PINK), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,4,Palette.LEAF_PINK), B(-4,-2,10,12,2,4,Palette.LEAF_BLUSH));
			Tree(8, 7, B(-2,0,7,9,-3,-1,Palette.LEAF_PINK), B(1,3,8,10,-1,1,Palette.LEAF_BLUSH), B(-2,1,10,12,1,3,Palette.LEAF_ROSE), B(2,3,9,11,2,4,Palette.LEAF_PINK));
			Tree(9, 8, B(-3,0,8,10,-4,-2,Palette.LEAF_BLUSH), B(1,3,8,11,-2,1,Palette.LEAF_PINK), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,5,Palette.LEAF_PINK), B(-3,-1,10,12,3,5,Palette.LEAF_BLUSH));
			Tree(10, 8, B(-4,-1,7,9,-3,-1,Palette.LEAF_PINK), B(0,3,8,10,-2,1,Palette.LEAF_BLUSH), B(-2,1,10,12,0,3,Palette.LEAF_ROSE), B(2,4,9,11,2,4,Palette.LEAF_PINK), B(-4,-2,9,11,3,5,Palette.LEAF_BLUSH));
			Tree(11, 8, B(-4,-1,8,10,-4,-1,Palette.LEAF_BLUSH), B(0,3,8,11,-3,0,Palette.LEAF_PINK), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,5,Palette.LEAF_PINK), B(-4,-2,10,12,3,5,Palette.LEAF_BLUSH));
			Tree(12, 8, B(-3,0,8,10,-3,-1,Palette.LEAF_PINK), B(1,3,9,11,-2,1,Palette.LEAF_BLUSH), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,4,Palette.LEAF_PINK), B(-3,-1,10,12,3,5,Palette.LEAF_BLUSH));
			Tree(13, 8, B(-3,0,7,9,-4,-2,Palette.LEAF_BLUSH), B(1,3,8,10,-2,1,Palette.LEAF_PINK), B(-2,1,10,12,0,3,Palette.LEAF_ROSE), B(2,4,9,11,2,4,Palette.LEAF_PINK), B(-3,-1,9,11,3,5,Palette.LEAF_BLUSH));
			Tree(14, 7, B(-3,-1,7,9,-3,-1,Palette.LEAF_PINK), B(0,2,8,10,-2,1,Palette.LEAF_BLUSH), B(-2,1,10,12,0,2,Palette.LEAF_ROSE), B(2,3,9,11,2,4,Palette.LEAF_PINK));
			Tree(15, 8, B(-4,-1,8,10,-4,-1,Palette.LEAF_BLUSH), B(0,3,8,11,-3,0,Palette.LEAF_PINK), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,5,Palette.LEAF_PINK), B(-4,-2,10,12,3,5,Palette.LEAF_BLUSH));
			Tree(16, 7, B(-3,0,7,9,-3,-1,Palette.LEAF_PINK), B(1,3,8,10,-2,1,Palette.LEAF_BLUSH), B(-2,1,10,12,0,3,Palette.LEAF_ROSE), B(2,4,9,11,2,4,Palette.LEAF_PINK));
			Tree(17, 8, B(-4,-1,8,10,-4,-1,Palette.LEAF_BLUSH), B(0,3,8,11,-3,0,Palette.LEAF_PINK), B(-2,1,11,13,0,3,Palette.LEAF_ROSE), B(2,4,10,12,2,5,Palette.LEAF_PINK), B(-4,-2,10,12,3,5,Palette.LEAF_BLUSH), B(0,2,12,14,3,5,Palette.LEAF_PINK));
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

		private void WriteProjectionCourse(int y, byte material)
		{
			if (_activeProjection == null)
				throw new InvalidOperationException(
					"A canonical projection course may only be written inside a named structure.");
			foreach ((int x, int z) in _activeProjection.OrderBy(cell => cell.Z)
			         .ThenBy(cell => cell.X))
				Put(x, y, z, material);
		}

		private void WriteProjectionBase(byte material)
		{
			ReferenceGroundPlanStructure structure = _plan.GetStructure(_activeStructure);
			int baseY = structure.BaseY ?? throw new InvalidOperationException(
				$"Structure '{structure.Id}' has no audited baseY.");
			WriteProjectionCourse(baseY, material);
		}

		private void TerrainRectangle(List<int> rectangle, int surfaceY, bool paving)
		{
			for (int z = rectangle[1]; z <= rectangle[3]; z++)
			for (int x = rectangle[0]; x <= rectangle[2]; x++)
			{
				if (paving) _pavingCells.Add((x, z));
				TerrainSurface(x, z, surfaceY,
					paving ? Palette.PAVING : NaturalCap(x, z));
			}
		}

		private void TerrainPolygon(List<List<int>> vertices, int surfaceY, bool paving)
		{
			if (vertices == null || vertices.Count < 3) return;
			int minX = vertices[0][0], maxX = vertices[0][0];
			int minZ = vertices[0][1], maxZ = vertices[0][1];
			foreach (List<int> vertex in vertices)
			{
				minX = Math.Min(minX, vertex[0]); maxX = Math.Max(maxX, vertex[0]);
				minZ = Math.Min(minZ, vertex[1]); maxZ = Math.Max(maxZ, vertex[1]);
			}
			for (int z = minZ; z <= maxZ; z++)
			for (int x = minX; x <= maxX; x++)
				if (InsidePolygon(x + .5f, z + .5f, vertices))
				{
					if (paving) _pavingCells.Add((x, z));
					TerrainSurface(x, z, surfaceY,
						paving ? Palette.PAVING : NaturalCap(x, z));
				}
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

		private void RepaintSurface(int localX, int localZ, int surfaceY, byte cap)
		{
			if (!_pavingCells.Contains((localX, localZ))) return;
			(int x, int z) = LocalCell(localX, localZ);
			if (!_grid.InBounds(x, surfaceY - 1, z)) return;
			int index = z * _data.Width + x;
			if (_grid.Top[index] != surfaceY) return;
			bool natural = Palette.IsGrassSurface(cap) ||
				cap is Palette.MOSS or Palette.SAND or Palette.SOIL or Palette.MUD or
				Palette.BLOSSOM_DRIFT;
			// PAVING, STONE, STONE_WARM and MOSS_STONE all select the existing
			// PatternRock voxel shader. Their explicitly authored islands provide the
			// source's faded tonal breakup; the shader contributes only fine organic
			// weathering, never a second random layout.
			_grid.Describe(x, z, surfaceY,
				cap, natural ? _grid.Sub[index] : Palette.STONE_PALE,
				natural ? _grid.Deep[index] : Palette.STONE_PALE);
			SurfaceCells++;
		}

		private void TerrainSurface(int localX, int localZ, int surfaceY, byte cap)
		{
			(int x, int z) = LocalCell(localX, localZ);
			if (!_grid.InBounds(x, surfaceY - 1, z)) return;
			int index = z * _data.Width + x;
			bool natural = Palette.IsGrassSurface(cap) ||
				cap is Palette.MOSS or Palette.SAND or Palette.SOIL or Palette.MUD or
				Palette.BLOSSOM_DRIFT;
			// Reclaimed terraces are raised terrain, not pale stone boxes with a
			// grass tile glued on top.  Keeping the atlas substrate preserves the
			// green fringe, warm soil seam and regional cliff tone seen around the
			// source precinct; paving alone receives a masonry profile.
			_grid.RedescribeUnedited(x, z, surfaceY, cap,
				natural ? _naturalSub[index] : Palette.STONE_PALE,
				natural ? _naturalDeep[index] : Palette.STONE_PALE);
			SurfaceCells++;
		}

		private byte NaturalCap(int localX, int localZ)
		{
			(int x, int z) = LocalCell(localX, localZ);
			return _naturalCap[z * _grid.Size + x];
		}

		private int Ground(int localX, int localZ)
		{
			(int x, int z) = LocalCell(localX, localZ);
			return _grid.HeightAt(x, z);
		}

		private void Tree(int index, int trunkHeight, params TreeBox[] crown)
		{
			if (index < 0 || index >= _plan.SurroundingTrees.Count)
				throw new InvalidOperationException($"Ground plan has no tree anchor {index}.");
			List<int> point = _plan.SurroundingTrees[index];
			int x = point[0], z = point[1], y = Ground(x, z);
			Fill(x, x, y, y + trunkHeight, z, z, Palette.TRUNK_ROSE);
			foreach (TreeBox box in crown)
				Fill(x + box.X0, x + box.X1, y + box.Y0, y + box.Y1,
					z + box.Z0, z + box.Z1, box.Material);
		}

		private static TreeBox B(int x0, int x1, int y0, int y1,
			int z0, int z1, byte material) => new(x0, x1, y0, y1, z0, z1, material);

		private void Fill(int x0, int x1, int y0, int y1, int z0, int z1, byte material)
		{
			for (int y = y0; y <= y1; y++)
			for (int z = z0; z <= z1; z++)
			for (int x = x0; x <= x1; x++) Put(x, y, z, material);
		}

		private void Put(int localX, int y, int localZ, byte material)
		{
			if (!string.IsNullOrEmpty(_activeStructure))
			{
				if (_activeProjection == null || !_activeProjection.Contains((localX, localZ)))
					throw new InvalidOperationException(
						$"Structure '{_activeStructure}' attempted a voxel outside its canonical ground-plan footprint at {localX},{localZ}.");
				if (Palette.IsSolid(material)) _activeTouched?.Add((localX, localZ));
			}

			(int x, int z) = LocalCell(localX, localZ);
			if (!_grid.InBounds(x, y, z)) return;
			_grid.Set(x, y, z, material);
			if (Palette.IsSolid(material))
			{
				int index = z * _grid.Size + x;
				if (y + 1 > _grid.Heights[index]) _grid.Heights[index] = (short)(y + 1);
			}
			Voxels++;
		}

		private (int x, int z) LocalCell(int localX, int localZ)
		{
			// The source-facing top view is reflected across plan X. This reflection
			// is explicit in the version-2 coordinate contract and remains exactly
			// one-to-one; the rejected .90 scale, rounding collapse and group offsets
			// are gone.
			int planX = _plan.CoordinateContract.RuntimeMirrorX == true ? -localX : localX;
			BlockPoint global = _site.ToGlobal(new PlanPoint { X = planX, Z = localZ });
			return (global.X - _data.OriginX, global.Z - _data.OriginZ);
		}

		private readonly record struct TreeBox(int X0, int X1, int Y0, int Y1,
			int Z0, int Z1, byte Material);
	}
}

public readonly record struct ReferenceSiteStatistics(int SurfaceCells, int Voxels);
