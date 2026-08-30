using System;
using System.Collections.Generic;
using System.Linq;
using Petalfell.Core;

namespace Petalfell.World.Sites;

/// <summary>
/// Site-owned terrain blockout for the author's Reference 12 Meshy sculptures:
/// a broad field of detached worn slabs around a three-course leg plinth and
/// low fallen-head court. Visible sculpture and collision attach separately in
/// Reference12SculptureDetail.
/// </summary>
public static class Reference12FallenColossus
{
	public const string BuilderId = "reference-12-fallen-colossus-v1";

	public static ReferenceSiteStatistics Build(AtlasSectorWindow window,
		ReferenceSiteDefinition site, int verticalOffset = 0)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (site == null) throw new ArgumentNullException(nameof(site));
		if (site.BuilderId != BuilderId)
			throw new InvalidOperationException($"Reference 12 cannot build '{site.BuilderId}'.");
		ReferenceSiteGroundPlan plan = ReferenceSiteGroundPlan.Load(site);
		var build = new Blueprint(window, site, plan, verticalOffset);
		build.Write();
		return new ReferenceSiteStatistics(build.SurfaceCells, build.Voxels);
	}

	private sealed class Blueprint
	{
		private readonly VoxelGrid _grid;
		private readonly AtlasSectorData _data;
		private readonly ReferenceSiteDefinition _site;
		private readonly ReferenceSiteGroundPlan _plan;
		private readonly int _verticalOffset;
		private readonly byte[] _naturalCap, _naturalSub, _naturalDeep;
		private string _activeStructure = "";
		private HashSet<(int X, int Z)> _activeProjection;
		private HashSet<(int X, int Z)> _activeTouched;

		public int SurfaceCells { get; private set; }
		public int Voxels { get; private set; }

		public Blueprint(AtlasSectorWindow window, ReferenceSiteDefinition site,
			ReferenceSiteGroundPlan plan, int verticalOffset)
		{
			_grid = window.Grid;
			_data = window.Data;
			_site = site;
			_plan = plan;
			_verticalOffset = verticalOffset;
			_naturalCap = (byte[])_grid.Cap.Clone();
			_naturalSub = (byte[])_grid.Sub.Clone();
			_naturalDeep = (byte[])_grid.Deep.Clone();
		}

		public void Write()
		{
			WriteTerrain();
			WriteSurfaceWear();

			// The Meshy GLBs now own the complete visible sculpture and their invisible
			// collision volumes. Keeping the old integer-voxel shells here made smooth
			// slabs poke through the authored legs, head and fallen debris.
			ReserveExternalGeometry("fallen-crown");
			ReserveExternalGeometry("fallen-head-core");
			ReserveExternalGeometry("left-severed-leg");
			ReserveExternalGeometry("right-severed-leg");
			WriteNamed("west-north-pillar", () => WritePillar("west-north-pillar", 0));
			WriteNamed("west-south-pillar", () => WritePillar("west-south-pillar", 1));
			WriteNamed("north-west-pillar", () => WritePillar("north-west-pillar", 2));
			WriteNamed("north-east-pillar", () => WritePillar("north-east-pillar", 3));
			WriteNamed("southwest-pillar", () => WritePillar("southwest-pillar", 4));
			WriteNamed("south-pillar", () => WritePillar("south-pillar", 5));
			WriteNamed("east-north-pillar", () => WritePillar("east-north-pillar", 6));
			WriteNamed("east-south-pillar", () => WritePillar("east-south-pillar", 7));
			WriteNamed("west-broken-foundation", () =>
				WriteBrokenFoundation("west-broken-foundation", 0));
			WriteNamed("north-broken-foundation", () =>
				WriteBrokenFoundation("north-broken-foundation", 1));
			WriteNamed("south-east-broken-foundation", () =>
				WriteBrokenFoundation("south-east-broken-foundation", 2));
			WriteNamed("east-broken-foundation", () =>
				WriteBrokenFoundation("east-broken-foundation", 3));
			WriteNamed("west-rubble", () => WriteRubble("west-rubble", 0));
			WriteNamed("north-rubble", () => WriteRubble("north-rubble", 1));
			WriteNamed("south-east-rubble", () => WriteRubble("south-east-rubble", 2));
			WriteNamed("east-rubble", () => WriteRubble("east-rubble", 3));
		}

		private void WriteTerrain()
		{
			// Like Reference 10, terrain is a painter's stack: broad lower islands are
			// written first and their narrower remnants later. Unpainted gaps retain
			// the atlas terrain, so the precinct never becomes one clean presentation pad.
			foreach (ReferenceGroundPlanTerrain terrain in _plan.Terrain)
			{
				if (terrain.WriteMode == "preserve-atlas") continue;
				if (terrain.SurfaceY is not int surfaceY)
					throw new InvalidOperationException($"Terrain '{terrain.Id}' needs surfaceY.");
				foreach (ReferenceGroundPlanCell cell in terrain.EffectiveCells)
					TerrainSurface(cell.X, cell.Z, surfaceY, TerrainMaterial(terrain.Material,
						cell.X, cell.Z));
			}

			foreach (ReferenceGroundPlanStructure stair in _plan.Structures)
			{
				if (stair.Kind != "stair") continue;
				foreach (ReferenceGroundPlanTread tread in stair.Treads)
				{
					int topY = tread.TopY ?? throw new InvalidOperationException(
						$"Stair '{stair.Id}' has an unaudited tread height.");
					for (int z = tread.Footprint[1]; z <= tread.Footprint[3]; z++)
					for (int x = tread.Footprint[0]; x <= tread.Footprint[2]; x++)
						TerrainSurface(x, z, topY, Palette.PAVING);
				}
			}
		}

		private void WriteSurfaceWear()
		{
			foreach (ReferenceGroundPlanSurfacePatch patch in _plan.SurfacePatches)
			{
				ReferenceGroundPlanTerrain owner = _plan.GetTerrain(patch.TerrainId);
				int surfaceY = owner.SurfaceY ??
					throw new InvalidOperationException($"Surface owner '{owner.Id}' needs surfaceY.");
				byte material = patch.Material switch
				{
					"worn-paving" => Palette.PAVING,
					"warm-paving" => Palette.STONE_WARM,
					"cool-paving" => Palette.STONE,
					"moss-paving" => Palette.MOSS_STONE,
					_ => throw new InvalidOperationException(
						$"Reference 12 has no surface material '{patch.Material}'.")
				};
				foreach (ReferenceGroundPlanCell cell in patch.EffectiveCells)
					RepaintSurface(cell.X, cell.Z, surfaceY, material);
			}
		}

		private void ReserveExternalGeometry(string id)
		{
			ReferenceGroundPlanStructure structure = _plan.GetStructure(id);
			if (structure.ProjectionCells.Count == 0)
				throw new InvalidOperationException(
					$"External structure '{id}' has no authored ground projection.");
		}

		private void WritePillar(string id, int variant)
		{
			ReferenceGroundPlanStructure pillar = _plan.GetStructure(id);
			int baseY = pillar.BaseY ?? throw new InvalidOperationException(
				$"Pillar '{id}' needs baseY.");
			int height = pillar.Height ?? throw new InvalidOperationException(
				$"Pillar '{id}' needs height.");
			foreach (ReferenceGroundPlanCell cell in pillar.ProjectionCells)
			for (int y = baseY; y < baseY + height; y++)
			{
				// Long coherent bands give each square shaft age and scale without
				// changing its fixed 2x2 cross-section or scattering random bumps.
				int course = y - baseY;
				byte material = course == 2 + variant % 3 ? Palette.STONE_WARM
					: course == height - 2 && variant % 2 == 0
						? Palette.MOSS_STONE : Palette.STONE_PALE;
				Put(cell.X, y, cell.Z, material);
			}
		}

		private void WriteBrokenFoundation(string id, int variant)
		{
			ReferenceGroundPlanStructure foundation = _plan.GetStructure(id);
			int baseY = foundation.BaseY ?? throw new InvalidOperationException(
				$"Foundation '{id}' needs baseY.");
			foreach (ReferenceGroundPlanCell cell in foundation.ProjectionCells)
			{
				// Every trace has one continuous structural root. The second and third
				// courses break in long site-specific runs, avoiding both loose cubes and
				// the pristine extruded bars from the previous pass.
				Put(cell.X, baseY, cell.Z,
					((cell.X + cell.Z + variant * 3) & 7) < 2
						? Palette.STONE_WARM : Palette.STONE);
				int run = Math.Abs(cell.X * (variant + 3) + cell.Z * (variant + 5));
				if (run % 11 is not (0 or 1))
					Put(cell.X, baseY + 1, cell.Z,
						run % 9 == 2 ? Palette.MOSS_STONE : Palette.STONE_PALE);
				if (run % 17 is 5 or 6 or 7)
					Put(cell.X, baseY + 2, cell.Z, Palette.STONE_WARM);
			}
		}

		private void WriteRubble(string id, int variant)
		{
			ReferenceGroundPlanStructure rubble = _plan.GetStructure(id);
			int baseY = rubble.BaseY ?? throw new InvalidOperationException(
				$"Rubble '{id}' needs baseY.");
			int index = 0;
			foreach (ReferenceGroundPlanCell cell in rubble.ProjectionCells
			         .OrderBy(cell => cell.Z).ThenBy(cell => cell.X))
			{
				int courses = 1 + Math.Abs(cell.X * 5 + cell.Z * 7 + variant) % 3;
				for (int y = 0; y < courses; y++)
					Put(cell.X, baseY + y, cell.Z,
						(index + y) % 5 == 0 ? Palette.MOSS_STONE
						: (index + y) % 3 == 0 ? Palette.STONE_WARM : Palette.RUBBLE);
				index++;
			}
		}

		private void ChipPedestalEdges()
		{
			// These are deliberate missing edge courses seen in the source. Breaking
			// only corners and short runs keeps the four-step hierarchy readable.
			TerrainSurface(-14, -12, 40, NaturalCap(-14, -12));
			TerrainSurface(-13, -12, 40, NaturalCap(-13, -12));
			TerrainSurface(14, 10, 40, NaturalCap(14, 10));
			TerrainSurface(14, 11, 40, NaturalCap(14, 11));
			TerrainSurface(-12, 10, 41, Palette.STONE_WARM);
			TerrainSurface(-11, 10, 41, Palette.STONE_WARM);
			TerrainSurface(12, -10, 41, Palette.MOSS_STONE);
			TerrainSurface(11, -10, 41, Palette.MOSS_STONE);
			TerrainSurface(-11, -9, 42, Palette.STONE_WARM);
			TerrainSurface(11, 8, 42, Palette.STONE_WARM);
			TerrainSurface(-10, 8, 43, Palette.STONE_WARM);
			TerrainSurface(10, -8, 43, Palette.STONE_WARM);
		}

		private void WriteSurvivor(int x, int z, int height, int variant)
		{
			Fill(x, x + 2, 40, 41, z, z + 2, Palette.STONE_WARM);
			Fill(x, x + 2, 42, 39 + height, z, z + 2, Palette.STONE_PALE);
			if (variant == 0)
			{
				Put(x + 2, 39 + height, z, Palette.STONE_WARM);
				Put(x, 39 + height, z + 2, Palette.AIR);
			}
			else if (variant == 1)
			{
				Fill(x, x, 43, 45, z + 2, z + 2, Palette.MOSS_STONE);
				Put(x + 2, 39 + height, z + 2, Palette.AIR);
			}
			else
			{
				Fill(x + 2, x + 2, 44, 47, z, z, Palette.MOSS_STONE);
				Put(x, 39 + height, z, Palette.AIR);
			}
		}

		private void WriteImpactRubble(string id)
		{
			ReferenceGroundPlanStructure rubble = _plan.GetStructure(id);
			int index = 0;
			foreach (ReferenceGroundPlanCell cell in rubble.ProjectionCells)
			{
				int height = 1 + (index * 7 % 3);
				Fill(cell.X, cell.X, 40, 39 + height, cell.Z, cell.Z,
					index % 4 == 0 ? Palette.MOSS_STONE : Palette.RUBBLE);
				index++;
			}
		}

		private void WriteTrees()
		{
			for (int i = 0; i < _plan.SurroundingTrees.Count; i++)
			{
				List<int> point = _plan.SurroundingTrees[i];
				int x = point[0], z = point[1];
				(int gx, int gz) = LocalCell(x, z);
				int ground = _grid.HeightAt(gx, gz) - _verticalOffset;
				int trunk = 5 + i % 3;
				Fill(x, x, ground, ground + trunk - 1, z, z, Palette.TRUNK_ROSE);
				byte leaf = i % 3 == 0 ? Palette.LEAF_PINK
					: i % 3 == 1 ? Palette.LEAF_BLUSH : Palette.LEAF_CREAM;
				Fill(x - 2, x + 2, ground + trunk, ground + trunk + 2,
					z - 1, z + 1, leaf);
				Fill(x - 1, x + 1, ground + trunk - 1, ground + trunk + 3,
					z - 2, z + 2, leaf);
				Put(x + (i % 2 == 0 ? 2 : -2), ground + trunk + 2, z, Palette.LEAF_ROSE);
			}
		}

		private void WriteNamed(string id, Action write)
		{
			ReferenceGroundPlanStructure structure = _plan.GetStructure(id);
			_activeStructure = id;
			_activeProjection = structure.ProjectionCells
				.Select(cell => (cell.X, cell.Z)).ToHashSet();
			_activeTouched = new HashSet<(int X, int Z)>();
			write();
			if (!_activeProjection.SetEquals(_activeTouched))
			{
				(int X, int Z) missing = _activeProjection.Except(_activeTouched).First();
				throw new InvalidOperationException(
					$"Structure '{id}' left its plan projection empty at {missing.X},{missing.Z}.");
			}
			_activeStructure = "";
			_activeProjection = null;
			_activeTouched = null;
		}

		private byte TerrainMaterial(string material, int localX, int localZ)
		{
			if (material.Contains("natural", StringComparison.Ordinal) ||
			    material.Contains("soil", StringComparison.Ordinal)) return NaturalCap(localX, localZ);
			if (material.Contains("moss", StringComparison.Ordinal)) return Palette.MOSS_STONE;
			if (material.Contains("warm", StringComparison.Ordinal)) return Palette.STONE_WARM;
			return Palette.PAVING;
		}

		private void TerrainSurface(int localX, int localZ, int surfaceY, byte cap)
		{
			surfaceY += _verticalOffset;
			(int x, int z) = LocalCell(localX, localZ);
			if (!_grid.InBounds(x, surfaceY - 1, z)) return;
			int index = z * _grid.Size + x;
			bool natural = Palette.IsGrassSurface(cap) || cap is Palette.MOSS or
				Palette.SAND or Palette.SOIL or Palette.MUD or Palette.BLOSSOM_DRIFT;
			_grid.RedescribeUnedited(x, z, surfaceY, cap,
				natural ? _naturalSub[index] : Palette.STONE_PALE,
				natural ? _naturalDeep[index] : Palette.STONE_PALE);
			SurfaceCells++;
		}

		private void RepaintSurface(int localX, int localZ, int surfaceY, byte cap)
		{
			surfaceY += _verticalOffset;
			(int x, int z) = LocalCell(localX, localZ);
			if (!_grid.InBounds(x, surfaceY - 1, z)) return;
			_grid.Describe(x, z, surfaceY, cap, Palette.STONE_PALE, Palette.STONE_PALE);
			SurfaceCells++;
		}

		private byte NaturalCap(int localX, int localZ)
		{
			(int x, int z) = LocalCell(localX, localZ);
			return _naturalCap[z * _grid.Size + x];
		}

		private void Fill(int x0, int x1, int y0, int y1, int z0, int z1, byte material)
		{
			if (y1 < y0) return;
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
						$"Structure '{_activeStructure}' wrote outside its plan at {localX},{localZ}.");
				if (Palette.IsSolid(material)) _activeTouched.Add((localX, localZ));
			}
			y += _verticalOffset;
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
			int planX = _plan.CoordinateContract.RuntimeMirrorX == true ? -localX : localX;
			BlockPoint global = _site.ToGlobal(new PlanPoint { X = planX, Z = localZ });
			return (global.X - _data.OriginX, global.Z - _data.OriginZ);
		}
	}
}
