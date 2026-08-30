using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Petalfell.World;

/// <summary>
/// Authoritative integer top view for one reference reconstruction. This is
/// deliberately data and validation only: the site-owned blueprint remains
/// responsible for writing every visible voxel.
/// </summary>
public sealed class ReferenceSiteGroundPlan
{
	public int Version { get; set; }
	public string SiteId { get; set; } = "";
	public string ReferencePath { get; set; } = "";
	public string Basis { get; set; } = "";
	public ReferenceGroundPlanCoordinateContract CoordinateContract { get; set; } = new();
	public List<ReferenceGroundPlanTerrain> Terrain { get; set; } = new();
	public List<ReferenceGroundPlanSurfacePatch> SurfacePatches { get; set; } = new();
	public List<ReferenceGroundPlanStructure> Structures { get; set; } = new();
	public List<List<int>> SurroundingTrees { get; set; } = new();
	public ReferenceGroundPlanAcceptanceRules AcceptanceRules { get; set; } = new();

	private Dictionary<string, ReferenceGroundPlanTerrain> _terrainById =
		new(StringComparer.Ordinal);
	private Dictionary<string, ReferenceGroundPlanStructure> _structureById =
		new(StringComparer.Ordinal);
	private Dictionary<string, ReferenceGroundPlanSurfacePatch> _surfacePatchById =
		new(StringComparer.Ordinal);

	[JsonIgnore]
	public IReadOnlyDictionary<string, ReferenceGroundPlanTerrain> TerrainById => _terrainById;
	[JsonIgnore]
	public IReadOnlyDictionary<string, ReferenceGroundPlanStructure> StructureById => _structureById;
	[JsonIgnore]
	public IReadOnlyDictionary<string, ReferenceGroundPlanSurfacePatch> SurfacePatchById =>
		_surfacePatchById;

	public ReferenceGroundPlanTerrain GetTerrain(string id) =>
		_terrainById.TryGetValue(id, out var terrain)
			? terrain
			: throw new KeyNotFoundException($"Ground plan '{SiteId}' has no terrain '{id}'.");

	public ReferenceGroundPlanStructure GetStructure(string id) =>
		_structureById.TryGetValue(id, out var structure)
			? structure
			: throw new KeyNotFoundException($"Ground plan '{SiteId}' has no structure '{id}'.");

	public ReferenceGroundPlanSurfacePatch GetSurfacePatch(string id) =>
		_surfacePatchById.TryGetValue(id, out var patch)
			? patch
			: throw new KeyNotFoundException($"Ground plan '{SiteId}' has no surface patch '{id}'.");

	public static ReferenceSiteGroundPlan Load(ReferenceSiteDefinition site)
	{
		if (site == null) throw new ArgumentNullException(nameof(site));
		string resourcePath = site.GroundPlanPath;
		if (string.IsNullOrWhiteSpace(resourcePath))
			throw new InvalidOperationException(
				$"Reference site '{site.SiteId}' does not name a groundPlanPath.");

		using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException(
				$"Could not open reference-site ground plan '{resourcePath}': {Godot.FileAccess.GetOpenError()}");

		ReferenceSiteGroundPlan plan;
		try
		{
			var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			plan = JsonSerializer.Deserialize<ReferenceSiteGroundPlan>(file.GetAsText(), options);
		}
		catch (JsonException error)
		{
			string location = error.LineNumber.HasValue
				? $" at line {error.LineNumber + 1}, byte {error.BytePositionInLine + 1}"
				: "";
			throw new InvalidOperationException(
				$"Reference-site ground plan '{resourcePath}' is not valid version-2 JSON{location}: {error.Message}",
				error);
		}

		if (plan == null)
			throw new InvalidOperationException(
				$"Reference-site ground plan '{resourcePath}' was empty.");
		plan.AuditAndIndex(site, resourcePath);
		return plan;
	}

	private void AuditAndIndex(ReferenceSiteDefinition site, string resourcePath)
	{
		var errors = new List<string>();
		if (Version != 2) errors.Add($"version must be 2, got {Version}");
		if (string.IsNullOrWhiteSpace(SiteId)) errors.Add("siteId is required");
		else if (!string.Equals(SiteId, site.SiteId, StringComparison.Ordinal))
			errors.Add($"siteId '{SiteId}' does not match owning reference site '{site.SiteId}'");
		if (string.IsNullOrWhiteSpace(ReferencePath)) errors.Add("referencePath is required");
		else if (!string.Equals(ReferencePath, site.ReferencePath, StringComparison.Ordinal))
			errors.Add($"referencePath '{ReferencePath}' does not match owning reference site '{site.ReferencePath}'");

		if (!IsCardinal(site.AxisDegrees))
			errors.Add($"owning site axisDegrees {site.AxisDegrees} is not cardinal (0, 90, 180 or 270)");
		if (site.FootprintMin == null || site.FootprintMax == null ||
		    site.FootprintMin.X > site.FootprintMax.X || site.FootprintMin.Z > site.FootprintMax.Z)
			errors.Add("owning site footprint bounds are invalid");

		AuditCoordinateContract(site, errors);

		var allIds = new HashSet<string>(StringComparer.Ordinal);
		var terrainById = new Dictionary<string, ReferenceGroundPlanTerrain>(StringComparer.Ordinal);
		foreach (var terrain in Terrain ?? new List<ReferenceGroundPlanTerrain>())
		{
			if (terrain == null) { errors.Add("terrain entries may not be null"); continue; }
			if (AuditId(terrain.Id, "terrain", allIds, errors)) terrainById[terrain.Id] = terrain;
			if (string.IsNullOrWhiteSpace(terrain.Role))
				errors.Add($"terrain '{terrain.Id}' role is required");
			if (!terrain.SurfaceY.HasValue)
				errors.Add($"terrain '{terrain.Id}' surfaceY is required");
			if (string.IsNullOrWhiteSpace(terrain.Material))
				errors.Add($"terrain '{terrain.Id}' material is required");
			if (terrain.WriteMode is not ("preserve-atlas" or "author-surface"))
				errors.Add($"terrain '{terrain.Id}' writeMode must be 'preserve-atlas' or 'author-surface'");

			bool hasFootprint = terrain.Footprint?.Count > 0;
			bool hasPolygon = terrain.Polygon?.Count > 0;
			if (hasFootprint == hasPolygon)
				errors.Add($"terrain '{terrain.Id}' must define exactly one footprint or polygon");
			else if (hasFootprint)
				AuditRectangle(terrain.Footprint, $"terrain '{terrain.Id}' footprint", site, errors);
			else
				AuditPolygon(terrain.Polygon, $"terrain '{terrain.Id}' polygon", site, errors);
		}
		if (Terrain == null || Terrain.Count == 0) errors.Add("at least one terrain shape is required");
		Dictionary<string, HashSet<ReferenceGroundPlanCell>> visibleTerrainCells =
			BuildVisibleTerrainCells(Terrain, terrainById, site);

		var surfacePatchById =
			new Dictionary<string, ReferenceGroundPlanSurfacePatch>(StringComparer.Ordinal);
		var patchedCells = new Dictionary<ReferenceGroundPlanCell, string>();
		foreach (var patch in SurfacePatches ?? new List<ReferenceGroundPlanSurfacePatch>())
		{
			if (patch == null) { errors.Add("surfacePatch entries may not be null"); continue; }
			if (AuditId(patch.Id, "surface patch", allIds, errors))
				surfacePatchById[patch.Id] = patch;
			if (string.IsNullOrWhiteSpace(patch.Material))
				errors.Add($"surface patch '{patch.Id}' material is required");
			if (!terrainById.TryGetValue(patch.TerrainId, out var ownerTerrain))
				errors.Add($"surface patch '{patch.Id}' references missing terrainId '{patch.TerrainId}'");
			else if (ownerTerrain.WriteMode != "author-surface")
				errors.Add($"surface patch '{patch.Id}' terrain '{patch.TerrainId}' does not author its surface");
			if (patch.Footprints == null || patch.Footprints.Count == 0)
				errors.Add($"surface patch '{patch.Id}' needs at least one footprint");

			var cells = new HashSet<ReferenceGroundPlanCell>();
			for (int i = 0; i < (patch.Footprints?.Count ?? 0); i++)
			{
				List<int> rectangle = patch.Footprints[i];
				AuditRectangle(rectangle, $"surface patch '{patch.Id}' footprint {i + 1}",
					site, errors);
				if (!ValidRectangle(rectangle) || !RectangleInsideSite(rectangle, site)) continue;
				foreach (ReferenceGroundPlanCell cell in RectangleCells(rectangle))
				{
					if (!cells.Add(cell))
						errors.Add($"surface patch '{patch.Id}' repeats cell {cell.X},{cell.Z}");
				}
			}
			if (visibleTerrainCells.TryGetValue(patch.TerrainId, out var terrainCells))
				foreach (ReferenceGroundPlanCell cell in cells)
					if (!terrainCells.Contains(cell))
						errors.Add($"surface patch '{patch.Id}' cell {cell.X},{cell.Z} is not visible terrain '{patch.TerrainId}'");
			foreach (ReferenceGroundPlanCell cell in cells)
			{
				if (patchedCells.TryGetValue(cell, out string previous))
					errors.Add($"surface patches '{previous}' and '{patch.Id}' overlap at {cell.X},{cell.Z}");
				else patchedCells[cell] = patch.Id;
			}
			patch.SetEffectiveCells(cells);
		}

		var structureById = new Dictionary<string, ReferenceGroundPlanStructure>(StringComparer.Ordinal);
		var rubbleCellOwners = new Dictionary<ReferenceGroundPlanCell, string>();
		foreach (var structure in Structures ?? new List<ReferenceGroundPlanStructure>())
		{
			if (structure == null) { errors.Add("structure entries may not be null"); continue; }
			if (AuditId(structure.Id, "structure", allIds, errors)) structureById[structure.Id] = structure;
			if (string.IsNullOrWhiteSpace(structure.Kind))
			{
				errors.Add($"structure '{structure.Id}' kind is required");
				continue;
			}

			if (string.Equals(structure.Kind, "stair", StringComparison.Ordinal))
				AuditStair(structure, terrainById, visibleTerrainCells, site, errors);
			else if (string.Equals(structure.Kind, "rubble-cluster", StringComparison.Ordinal))
				AuditRubbleCluster(structure, terrainById, visibleTerrainCells,
					rubbleCellOwners, site, errors);
			else
				AuditStructureFootprints(structure, terrainById, visibleTerrainCells,
					site, errors);
		}

		for (int i = 0; i < (SurroundingTrees?.Count ?? 0); i++)
			AuditPoint(SurroundingTrees[i], $"surroundingTrees[{i}]", site, errors);

		if (AcceptanceRules != null)
		{
			AuditPositivePair(AcceptanceRules.IsolatedSurvivorMaximumFootprint,
				"acceptanceRules.isolatedSurvivorMaximumFootprint", errors);
			if (AcceptanceRules.ThinWallMaximumWidth.HasValue &&
			    AcceptanceRules.ThinWallMaximumWidth.Value <= 0)
				errors.Add("acceptanceRules.thinWallMaximumWidth must be positive");
		}

		if (errors.Count > 0)
			throw InvalidPlan(resourcePath, errors);
		_terrainById = terrainById;
		_structureById = structureById;
		_surfacePatchById = surfacePatchById;
	}

	private void AuditCoordinateContract(ReferenceSiteDefinition site, List<string> errors)
	{
		if (CoordinateContract == null)
		{
			errors.Add("coordinateContract is required");
			return;
		}
		if (!CoordinateContract.OneCellIsOneVoxel)
			errors.Add("coordinateContract.oneCellIsOneVoxel must be true");
		if (!CoordinateContract.RuntimePlanScale.HasValue ||
		    Math.Abs(CoordinateContract.RuntimePlanScale.Value - 1d) > double.Epsilon)
			errors.Add("coordinateContract.runtimePlanScale must be exactly 1");
		if (CoordinateContract.RuntimeMirrorX is not true)
			errors.Add("coordinateContract.runtimeMirrorX must be true for the locked atlas write transform");
		if (CoordinateContract.Origin == null ||
		    CoordinateContract.Origin.X != 0 || CoordinateContract.Origin.Z != 0)
			errors.Add("coordinateContract.origin must be the exact local origin 0,0");
		if (CoordinateContract.PlayerSpawn == null || site.PlayerSpawn == null ||
		    CoordinateContract.PlayerSpawn.X != site.PlayerSpawn.X ||
		    CoordinateContract.PlayerSpawn.Z != site.PlayerSpawn.Z)
			errors.Add("coordinateContract.playerSpawn must exactly match the owning reference site");
		else if (!InsideSite(CoordinateContract.PlayerSpawn.X,
		         CoordinateContract.PlayerSpawn.Z, site))
			errors.Add("coordinateContract.playerSpawn lies outside the owning site footprint");
		if (!CoordinateContract.SourceViewYawDegrees.HasValue || site.ReferenceView == null ||
		    MathF.Abs(NormalizeAngle((float)CoordinateContract.SourceViewYawDegrees.GetValueOrDefault() -
		                             site.ReferenceView.YawDegrees)) > .01f)
			errors.Add("coordinateContract.sourceViewYawDegrees must match the locked reference view");
		if (!CoordinateContract.SourceViewPitchDegrees.HasValue || site.ReferenceView == null ||
		    Math.Abs(CoordinateContract.SourceViewPitchDegrees.GetValueOrDefault() -
		             site.ReferenceView.PitchDegrees) > .01d)
			errors.Add("coordinateContract.sourceViewPitchDegrees must match the locked reference view");
	}

	private static bool AuditId(string id, string kind, HashSet<string> ids, List<string> errors)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			errors.Add($"{kind} id is required");
			return false;
		}
		if (ids.Add(id)) return true;
		errors.Add($"duplicate authored id '{id}'");
		return false;
	}

	private static void AuditStair(ReferenceGroundPlanStructure stair,
		IReadOnlyDictionary<string, ReferenceGroundPlanTerrain> terrainById,
		IReadOnlyDictionary<string, HashSet<ReferenceGroundPlanCell>> visibleTerrainCells,
		ReferenceSiteDefinition site, List<string> errors)
	{
		if (!terrainById.TryGetValue(stair.FromTerrain, out var from))
			errors.Add($"stair '{stair.Id}' references missing fromTerrain '{stair.FromTerrain}'");
		if (!terrainById.TryGetValue(stair.ToTerrain, out var to))
			errors.Add($"stair '{stair.Id}' references missing toTerrain '{stair.ToTerrain}'");
		if (string.Equals(stair.FromTerrain, stair.ToTerrain, StringComparison.Ordinal) &&
		    !string.IsNullOrWhiteSpace(stair.FromTerrain))
			errors.Add($"stair '{stair.Id}' must connect two different terrain shapes");
		if (stair.Treads == null || stair.Treads.Count < 2)
		{
			errors.Add($"stair '{stair.Id}' needs at least two authored treads");
			return;
		}

		var projection = new HashSet<ReferenceGroundPlanCell>();
		for (int i = 0; i < stair.Treads.Count; i++)
		{
			var tread = stair.Treads[i];
			if (tread == null)
			{
				errors.Add($"stair '{stair.Id}' tread {i + 1} may not be null");
				continue;
			}
			AuditRectangle(tread.Footprint,
				$"stair '{stair.Id}' tread {i + 1} footprint", site, errors);
			if (ValidRectangle(tread.Footprint) && RectangleInsideSite(tread.Footprint, site))
				projection.UnionWith(RectangleCells(tread.Footprint));
			if (!tread.TopY.HasValue)
				errors.Add($"stair '{stair.Id}' tread {i + 1} topY is required");
			if (i == 0) continue;
			var previous = stair.Treads[i - 1];
			if (previous == null || !ValidRectangle(previous.Footprint) ||
			    !ValidRectangle(tread.Footprint)) continue;
			if (!RectanglesTouch(previous.Footprint, tread.Footprint))
				errors.Add($"stair '{stair.Id}' treads {i} and {i + 1} do not touch");
			if (previous.TopY.HasValue && tread.TopY.HasValue &&
			    Math.Abs(tread.TopY.Value - previous.TopY.Value) > 1)
				errors.Add($"stair '{stair.Id}' treads {i} and {i + 1} rise by more than one voxel");
		}

		var first = stair.Treads[0];
		var last = stair.Treads[^1];
		if (from?.SurfaceY is int fromY && first?.TopY != fromY)
			errors.Add($"stair '{stair.Id}' first topY must equal fromTerrain '{from.Id}' surfaceY {fromY}");
		if (to?.SurfaceY is int toY && last?.TopY != toY)
			errors.Add($"stair '{stair.Id}' last topY must equal toTerrain '{to.Id}' surfaceY {toY}");
		if (from?.SurfaceY is int startY && to?.SurfaceY is int endY)
		{
			int direction = Math.Sign(endY - startY);
			if (direction == 0)
				errors.Add($"stair '{stair.Id}' named terrains are on the same level");
			for (int i = 1; i < stair.Treads.Count; i++)
			{
				if (stair.Treads[i - 1]?.TopY is not int a || stair.Treads[i]?.TopY is not int b)
					continue;
				int step = Math.Sign(b - a);
				if (direction != 0 && step != 0 && step != direction)
					errors.Add($"stair '{stair.Id}' reverses vertical direction at tread {i + 1}");
			}
		}
		stair.SetProjectionCells(projection);
		AuditOptionalStairLandings(stair, from, to, visibleTerrainCells, site, errors);
	}

	private static void AuditStructureFootprints(ReferenceGroundPlanStructure structure,
		IReadOnlyDictionary<string, ReferenceGroundPlanTerrain> terrainById,
		IReadOnlyDictionary<string, HashSet<ReferenceGroundPlanCell>> visibleTerrainCells,
		ReferenceSiteDefinition site, List<string> errors)
	{
		var rectangles = new List<List<int>>();
		if (structure.Footprint?.Count > 0) rectangles.Add(structure.Footprint);
		if (structure.Footprints != null) rectangles.AddRange(structure.Footprints);
		if (rectangles.Count == 0)
			errors.Add($"structure '{structure.Id}' needs at least one footprint");
		var projection = new HashSet<ReferenceGroundPlanCell>();
		for (int i = 0; i < rectangles.Count; i++)
		{
			AuditRectangle(rectangles[i], $"structure '{structure.Id}' footprint {i + 1}",
				site, errors);
			if (ValidRectangle(rectangles[i]) && RectangleInsideSite(rectangles[i], site))
				projection.UnionWith(RectangleCells(rectangles[i]));
		}
		if (!structure.BaseY.HasValue)
			errors.Add($"structure '{structure.Id}' baseY is required");
		if (structure.Height.HasValue && structure.Height.Value <= 0)
			errors.Add($"structure '{structure.Id}' height must be positive when present");
		if (string.Equals(structure.Kind, "isolated-survivor", StringComparison.Ordinal) &&
		    (!structure.Height.HasValue || structure.Height.Value <= 0))
			errors.Add($"isolated survivor '{structure.Id}' height must be positive");
		if (structure.Kind is "connected-wall-run" or "connected-facade" &&
		    projection.Count > 0 && !Connected(projection))
			errors.Add($"connected structure '{structure.Id}' projection is not 4-connected");
		AuditOptionalSupport(structure, projection, terrainById, visibleTerrainCells,
			strictSubset: false, errors);
		structure.SetProjectionCells(projection);
	}

	private static void AuditRubbleCluster(ReferenceGroundPlanStructure rubble,
		IReadOnlyDictionary<string, ReferenceGroundPlanTerrain> terrainById,
		IReadOnlyDictionary<string, HashSet<ReferenceGroundPlanCell>> visibleTerrainCells,
		Dictionary<ReferenceGroundPlanCell, string> rubbleCellOwners,
		ReferenceSiteDefinition site, List<string> errors)
	{
		if (rubble.Footprint?.Count > 0 || rubble.Footprints?.Count > 0)
			errors.Add($"rubble cluster '{rubble.Id}' must use exact cells and envelope, not footprints");
		AuditRectangle(rubble.Envelope, $"rubble cluster '{rubble.Id}' envelope", site, errors);
		if (!rubble.BaseY.HasValue)
			errors.Add($"rubble cluster '{rubble.Id}' baseY is required");
		if (rubble.Cells == null || rubble.Cells.Count == 0)
			errors.Add($"rubble cluster '{rubble.Id}' needs at least one exact occupied cell");

		var projection = new HashSet<ReferenceGroundPlanCell>();
		for (int i = 0; i < (rubble.Cells?.Count ?? 0); i++)
		{
			List<int> point = rubble.Cells[i];
			AuditPoint(point, $"rubble cluster '{rubble.Id}' cell {i + 1}", site, errors);
			if (point == null || point.Count != 2) continue;
			var cell = new ReferenceGroundPlanCell(point[0], point[1]);
			if (!projection.Add(cell))
				errors.Add($"rubble cluster '{rubble.Id}' repeats cell {cell.X},{cell.Z}");
			if (ValidRectangle(rubble.Envelope) && !InsideRectangle(cell, rubble.Envelope))
				errors.Add($"rubble cluster '{rubble.Id}' cell {cell.X},{cell.Z} lies outside its envelope");
			if (rubbleCellOwners.TryGetValue(cell, out string previous))
				errors.Add($"rubble clusters '{previous}' and '{rubble.Id}' overlap at {cell.X},{cell.Z}");
			else rubbleCellOwners[cell] = rubble.Id;
		}
		if (projection.Count > 0 && !Connected(projection))
			errors.Add($"rubble cluster '{rubble.Id}' cells must form one 4-connected component");
		AuditOptionalSupport(rubble, projection, terrainById, visibleTerrainCells,
			strictSubset: true, errors);
		rubble.SetProjectionCells(projection);
	}

	private static void AuditOptionalSupport(ReferenceGroundPlanStructure structure,
		IReadOnlySet<ReferenceGroundPlanCell> projection,
		IReadOnlyDictionary<string, ReferenceGroundPlanTerrain> terrainById,
		IReadOnlyDictionary<string, HashSet<ReferenceGroundPlanCell>> visibleTerrainCells,
		bool strictSubset, List<string> errors)
	{
		if (string.IsNullOrWhiteSpace(structure.SupportTerrain))
		{
			if (strictSubset)
				errors.Add($"{structure.Kind} '{structure.Id}' supportTerrain is required");
			return;
		}
		if (!terrainById.TryGetValue(structure.SupportTerrain, out var terrain))
		{
			errors.Add($"structure '{structure.Id}' references missing supportTerrain '{structure.SupportTerrain}'");
			return;
		}
		if (structure.BaseY.HasValue && terrain.SurfaceY.HasValue &&
		    structure.BaseY.Value != terrain.SurfaceY.Value)
			errors.Add($"structure '{structure.Id}' baseY {structure.BaseY} does not match supportTerrain '{terrain.Id}' surfaceY {terrain.SurfaceY}");
		if (!visibleTerrainCells.TryGetValue(terrain.Id, out var support)) return;
		foreach (ReferenceGroundPlanCell cell in projection)
		{
			bool supported = support.Contains(cell);
			if (!strictSubset && !supported) supported = Neighbours(cell).Any(support.Contains);
			if (!supported)
				errors.Add($"structure '{structure.Id}' cell {cell.X},{cell.Z} is not supported by visible terrain '{terrain.Id}'");
		}
	}

	private static void AuditOptionalStairLandings(ReferenceGroundPlanStructure stair,
		ReferenceGroundPlanTerrain from, ReferenceGroundPlanTerrain to,
		IReadOnlyDictionary<string, HashSet<ReferenceGroundPlanCell>> visibleTerrainCells,
		ReferenceSiteDefinition site, List<string> errors)
	{
		bool hasAny = stair.FromLanding?.Count > 0 || stair.ToLanding?.Count > 0 ||
			stair.Axis?.Count > 0;
		if (!hasAny) return;
		AuditRectangle(stair.FromLanding, $"stair '{stair.Id}' fromLanding", site, errors);
		AuditRectangle(stair.ToLanding, $"stair '{stair.Id}' toLanding", site, errors);
		if (stair.Axis == null || stair.Axis.Count != 2 ||
		    Math.Abs(stair.Axis[0]) + Math.Abs(stair.Axis[1]) != 1)
			errors.Add($"stair '{stair.Id}' axis must be one cardinal integer unit [x,z]");
		if (!ValidRectangle(stair.FromLanding) || !ValidRectangle(stair.ToLanding) ||
		    !RectangleInsideSite(stair.FromLanding, site) ||
		    !RectangleInsideSite(stair.ToLanding, site)) return;

		if (from != null && visibleTerrainCells.TryGetValue(from.Id, out var fromCells))
			foreach (ReferenceGroundPlanCell cell in RectangleCells(stair.FromLanding))
				if (!fromCells.Contains(cell))
					errors.Add($"stair '{stair.Id}' fromLanding cell {cell.X},{cell.Z} is not visible terrain '{from.Id}'");
		if (to != null && visibleTerrainCells.TryGetValue(to.Id, out var toCells))
			foreach (ReferenceGroundPlanCell cell in RectangleCells(stair.ToLanding))
				if (!toCells.Contains(cell))
					errors.Add($"stair '{stair.Id}' toLanding cell {cell.X},{cell.Z} is not visible terrain '{to.Id}'");
		if (stair.Treads?.Count > 0)
		{
			if (ValidRectangle(stair.Treads[0]?.Footprint) &&
			    !RectanglesTouch(stair.FromLanding, stair.Treads[0].Footprint))
				errors.Add($"stair '{stair.Id}' fromLanding does not touch its first tread");
			if (ValidRectangle(stair.Treads[^1]?.Footprint) &&
			    !RectanglesTouch(stair.ToLanding, stair.Treads[^1].Footprint))
				errors.Add($"stair '{stair.Id}' toLanding does not touch its last tread");
		}
		if (stair.Axis?.Count == 2)
		{
			int fromX2 = stair.FromLanding[0] + stair.FromLanding[2];
			int fromZ2 = stair.FromLanding[1] + stair.FromLanding[3];
			int toX2 = stair.ToLanding[0] + stair.ToLanding[2];
			int toZ2 = stair.ToLanding[1] + stair.ToLanding[3];
			if ((toX2 - fromX2) * stair.Axis[0] + (toZ2 - fromZ2) * stair.Axis[1] <= 0)
				errors.Add($"stair '{stair.Id}' axis does not point from fromLanding to toLanding");
		}
	}

	private static Dictionary<string, HashSet<ReferenceGroundPlanCell>>
		BuildVisibleTerrainCells(IEnumerable<ReferenceGroundPlanTerrain> terrain,
			IReadOnlyDictionary<string, ReferenceGroundPlanTerrain> terrainById,
			ReferenceSiteDefinition site)
	{
		// Terrain is a painter's stack: later authored levels replace earlier cells.
		// Patches and support checks use the resulting visible owner, not merely the
		// broad polygon underneath it, or geometry can silently bind to the wrong Y.
		var owner = new Dictionary<ReferenceGroundPlanCell, string>();
		foreach (ReferenceGroundPlanTerrain item in terrain ??
		         Enumerable.Empty<ReferenceGroundPlanTerrain>())
		{
			if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;
			HashSet<ReferenceGroundPlanCell> authored = TerrainCells(item, site);
			item.SetAuthoredCells(authored);
			foreach (ReferenceGroundPlanCell cell in authored) owner[cell] = item.Id;
		}

		var visible = terrainById.Keys.ToDictionary(id => id,
			_ => new HashSet<ReferenceGroundPlanCell>(), StringComparer.Ordinal);
		foreach ((ReferenceGroundPlanCell cell, string id) in owner)
			if (visible.TryGetValue(id, out var cells)) cells.Add(cell);
		foreach ((string id, ReferenceGroundPlanTerrain item) in terrainById)
			item.SetEffectiveCells(visible[id]);
		return visible;
	}

	private static HashSet<ReferenceGroundPlanCell> TerrainCells(
		ReferenceGroundPlanTerrain terrain, ReferenceSiteDefinition site)
	{
		if (ValidRectangle(terrain.Footprint) && RectangleInsideSite(terrain.Footprint, site))
			return RectangleCells(terrain.Footprint);
		var cells = new HashSet<ReferenceGroundPlanCell>();
		if (terrain.Polygon == null || terrain.Polygon.Count < 3 ||
		    terrain.Polygon.Any(point => point == null || point.Count != 2 ||
				!InsideSite(point[0], point[1], site))) return cells;
		int minX = terrain.Polygon.Min(point => point[0]);
		int maxX = terrain.Polygon.Max(point => point[0]);
		int minZ = terrain.Polygon.Min(point => point[1]);
		int maxZ = terrain.Polygon.Max(point => point[1]);
		for (int z = minZ; z <= maxZ; z++)
		for (int x = minX; x <= maxX; x++)
			if (InsidePolygon(x + .5, z + .5, terrain.Polygon))
				cells.Add(new ReferenceGroundPlanCell(x, z));
		return cells;
	}

	private static bool InsidePolygon(double x, double z, IReadOnlyList<List<int>> polygon)
	{
		bool inside = false;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			List<int> a = polygon[i], b = polygon[j];
			if ((a[1] > z) != (b[1] > z) &&
			    x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0])
				inside = !inside;
		}
		return inside;
	}

	private static HashSet<ReferenceGroundPlanCell> RectangleCells(List<int> rectangle)
	{
		var cells = new HashSet<ReferenceGroundPlanCell>();
		if (!ValidRectangle(rectangle)) return cells;
		for (int z = rectangle[1]; z <= rectangle[3]; z++)
		for (int x = rectangle[0]; x <= rectangle[2]; x++)
			cells.Add(new ReferenceGroundPlanCell(x, z));
		return cells;
	}

	private static bool InsideRectangle(ReferenceGroundPlanCell cell, List<int> rectangle) =>
		ValidRectangle(rectangle) && cell.X >= rectangle[0] && cell.X <= rectangle[2] &&
		cell.Z >= rectangle[1] && cell.Z <= rectangle[3];

	private static bool Connected(IReadOnlySet<ReferenceGroundPlanCell> cells)
	{
		if (cells.Count == 0) return false;
		var remaining = new HashSet<ReferenceGroundPlanCell>(cells);
		var queue = new Queue<ReferenceGroundPlanCell>();
		ReferenceGroundPlanCell first = remaining.First();
		remaining.Remove(first);
		queue.Enqueue(first);
		while (queue.Count > 0)
			foreach (ReferenceGroundPlanCell neighbour in Neighbours(queue.Dequeue()))
				if (remaining.Remove(neighbour)) queue.Enqueue(neighbour);
		return remaining.Count == 0;
	}

	private static IEnumerable<ReferenceGroundPlanCell> Neighbours(ReferenceGroundPlanCell cell)
	{
		yield return new ReferenceGroundPlanCell(cell.X + 1, cell.Z);
		yield return new ReferenceGroundPlanCell(cell.X - 1, cell.Z);
		yield return new ReferenceGroundPlanCell(cell.X, cell.Z + 1);
		yield return new ReferenceGroundPlanCell(cell.X, cell.Z - 1);
	}

	private static void AuditRectangle(List<int> rectangle, string owner,
		ReferenceSiteDefinition site, List<string> errors)
	{
		if (!ValidRectangle(rectangle))
		{
			errors.Add($"{owner} must be four normalized integers [x0,z0,x1,z1] with x0<=x1 and z0<=z1");
			return;
		}
		if (!InsideSite(rectangle[0], rectangle[1], site) ||
		    !InsideSite(rectangle[2], rectangle[3], site))
			errors.Add($"{owner} leaves the owning site footprint");
	}

	private static void AuditPolygon(List<List<int>> polygon, string owner,
		ReferenceSiteDefinition site, List<string> errors)
	{
		if (polygon == null || polygon.Count < 3)
		{
			errors.Add($"{owner} needs at least three integer points");
			return;
		}
		long twiceArea = 0;
		for (int i = 0; i < polygon.Count; i++)
		{
			var point = polygon[i];
			AuditPoint(point, $"{owner} point {i + 1}", site, errors);
			var next = polygon[(i + 1) % polygon.Count];
			if (point?.Count == 2 && next?.Count == 2)
				twiceArea += (long)point[0] * next[1] - (long)next[0] * point[1];
		}
		if (twiceArea == 0) errors.Add($"{owner} has no area");
	}

	private static void AuditPoint(List<int> point, string owner,
		ReferenceSiteDefinition site, List<string> errors)
	{
		if (point == null || point.Count != 2)
		{
			errors.Add($"{owner} must be an integer [x,z] pair");
			return;
		}
		if (!InsideSite(point[0], point[1], site))
			errors.Add($"{owner} lies outside the owning site footprint");
	}

	private static void AuditPositivePair(List<int> pair, string owner, List<string> errors)
	{
		if (pair == null || pair.Count != 2 || pair.Any(value => value <= 0))
			errors.Add($"{owner} must be two positive integers");
	}

	private static bool ValidRectangle(List<int> rectangle) =>
		rectangle != null && rectangle.Count == 4 &&
		rectangle[0] <= rectangle[2] && rectangle[1] <= rectangle[3];

	private static bool RectangleInsideSite(List<int> rectangle,
		ReferenceSiteDefinition site) => ValidRectangle(rectangle) &&
		InsideSite(rectangle[0], rectangle[1], site) &&
		InsideSite(rectangle[2], rectangle[3], site);

	private static bool RectanglesTouch(List<int> a, List<int> b)
	{
		bool xOverlap = a[0] <= b[2] && b[0] <= a[2];
		bool zOverlap = a[1] <= b[3] && b[1] <= a[3];
		if (xOverlap && zOverlap) return true;
		bool xAdjacent = a[2] + 1 == b[0] || b[2] + 1 == a[0];
		bool zAdjacent = a[3] + 1 == b[1] || b[3] + 1 == a[1];
		return (xAdjacent && zOverlap) || (zAdjacent && xOverlap);
	}

	private static bool InsideSite(int x, int z, ReferenceSiteDefinition site) =>
		site.FootprintMin != null && site.FootprintMax != null &&
		x >= site.FootprintMin.X && x <= site.FootprintMax.X &&
		z >= site.FootprintMin.Z && z <= site.FootprintMax.Z;

	private static bool IsCardinal(float angle)
	{
		float normalized = NormalizeAngle(angle);
		return MathF.Abs(normalized - MathF.Round(normalized / 90f) * 90f) <= .01f;
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}

	private static InvalidOperationException InvalidPlan(string resourcePath,
		IReadOnlyList<string> errors)
	{
		var message = new StringBuilder()
			.Append("Reference-site ground-plan audit failed for '")
			.Append(resourcePath).Append("'.");
		foreach (string error in errors) message.Append("\n  ERROR: ").Append(error);
		return new InvalidOperationException(message.ToString());
	}
}

public sealed class ReferenceGroundPlanCoordinateContract
{
	public PlanPoint Origin { get; set; } = new();
	public PlanPoint PlayerSpawn { get; set; } = new();
	public bool OneCellIsOneVoxel { get; set; }
	public double? RuntimePlanScale { get; set; }
	public bool? RuntimeMirrorX { get; set; }
	public double? SourceViewYawDegrees { get; set; }
	public double? SourceViewPitchDegrees { get; set; }
}

public sealed class ReferenceGroundPlanTerrain
{
	public string Id { get; set; } = "";
	public string Role { get; set; } = "";
	public int? SurfaceY { get; set; }
	public string Material { get; set; } = "";
	public string WriteMode { get; set; } = "";
	public List<int> Footprint { get; set; } = new();
	public List<List<int>> Polygon { get; set; } = new();

	private HashSet<ReferenceGroundPlanCell> _authoredCells = new();
	private HashSet<ReferenceGroundPlanCell> _effectiveCells = new();
	/// <summary>All cells rasterised by this shape before later terrain overrides it.</summary>
	[JsonIgnore] public IReadOnlySet<ReferenceGroundPlanCell> AuthoredCells => _authoredCells;
	/// <summary>Cells for which this is the final visible terrain owner.</summary>
	[JsonIgnore] public IReadOnlySet<ReferenceGroundPlanCell> EffectiveCells => _effectiveCells;
	internal void SetAuthoredCells(HashSet<ReferenceGroundPlanCell> cells) => _authoredCells = cells;
	internal void SetEffectiveCells(HashSet<ReferenceGroundPlanCell> cells) => _effectiveCells = cells;
}

public sealed class ReferenceGroundPlanSurfacePatch
{
	public string Id { get; set; } = "";
	public string TerrainId { get; set; } = "";
	public string Material { get; set; } = "";
	public List<List<int>> Footprints { get; set; } = new();

	private HashSet<ReferenceGroundPlanCell> _effectiveCells = new();
	/// <summary>The exact non-overlapping cells produced by the inclusive rectangles.</summary>
	[JsonIgnore] public IReadOnlySet<ReferenceGroundPlanCell> EffectiveCells => _effectiveCells;
	internal void SetEffectiveCells(HashSet<ReferenceGroundPlanCell> cells) => _effectiveCells = cells;
}

public sealed class ReferenceGroundPlanStructure
{
	public string Id { get; set; } = "";
	public string Kind { get; set; } = "";
	public int? BaseY { get; set; }
	public int? Height { get; set; }
	public List<int> Footprint { get; set; } = new();
	public List<List<int>> Footprints { get; set; } = new();
	public string FromTerrain { get; set; } = "";
	public string ToTerrain { get; set; } = "";
	public string SupportTerrain { get; set; } = "";
	public List<ReferenceGroundPlanTread> Treads { get; set; } = new();
	public List<int> FromLanding { get; set; } = new();
	public List<int> ToLanding { get; set; } = new();
	public List<int> Axis { get; set; } = new();
	public List<int> Envelope { get; set; } = new();
	/// <summary>Exact occupied XZ projection used only by rubble-cluster structures.</summary>
	public List<List<int>> Cells { get; set; } = new();
	public ReferenceGroundPlanOpening Opening { get; set; }

	private HashSet<ReferenceGroundPlanCell> _projectionCells = new();
	/// <summary>
	/// Audited exact XZ occupancy: tread union, rectangle union, or rubble cells.
	/// Runtime touched-cell parity is checked against this set without re-parsing JSON.
	/// </summary>
	[JsonIgnore] public IReadOnlySet<ReferenceGroundPlanCell> ProjectionCells => _projectionCells;
	internal void SetProjectionCells(HashSet<ReferenceGroundPlanCell> cells) =>
		_projectionCells = cells;
}

public sealed class ReferenceGroundPlanTread
{
	public List<int> Footprint { get; set; } = new();
	public int? TopY { get; set; }
}

public sealed class ReferenceGroundPlanOpening
{
	public List<int> LeftBase { get; set; } = new();
	public List<int> RightBase { get; set; } = new();
	public int? ClearHeight { get; set; }
}

public sealed class ReferenceGroundPlanAcceptanceRules
{
	public List<int> IsolatedSurvivorMaximumFootprint { get; set; } = new();
	public int? ThinWallMaximumWidth { get; set; }
	public bool ThickerMassesMustBeConnected { get; set; }
	public bool EachStairMustTouchBothNamedTerrains { get; set; }
	public bool SignificantGeometryMustAppearInThisPlan { get; set; }
	public bool GeneratorMayNotMoveOrAddStructures { get; set; }
}

public readonly record struct ReferenceGroundPlanCell(int X, int Z);
