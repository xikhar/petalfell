using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Petalfell.Core;
using Petalfell.World.Sites;

namespace Petalfell.World;

/// <summary>
/// Deterministic address and landing rules for a production-atlas window reload.
///
/// This is intentionally independent of scene nodes. The playable runtime and
/// headless authoring checks must agree on which sectors own an address and on
/// what counts as a safe surface after that window has been materialised.
/// </summary>
public static class AtlasRuntimeHandoff
{
	public const string RecoverySiteId = "bloom-grove-court";
	public const int DefaultWalkingTriggerMargin = 8 * ChunkMesher.ChunkSize;
	public const int DefaultWalkingRearmMargin =
		DefaultWalkingTriggerMargin + 4 * ChunkMesher.ChunkSize;
	public const int DefaultWalkingCooldownFrames = 45;

	/// <summary>
	/// Materialise one edge-clamped playable mosaic with the same authored-site
	/// and wilderness passes used by normal startup. Keeping this node-free lets
	/// the runtime swap scenes only after a complete replacement has succeeded,
	/// while headless authoring verifies the identical disposable world data.
	/// </summary>
	public static AtlasPreparedWindow PrepareWindow(WorldAtlasDefinition atlas,
		int worldSeed, int centreGlobalX, int centreGlobalZ, int sectorSpan,
		Func<int, int, AtlasSectorData> loadSector, Action<string> warning = null)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		if (loadSector == null) throw new ArgumentNullException(nameof(loadSector));
		AtlasMosaicBounds bounds = WindowAround(atlas, centreGlobalX, centreGlobalZ,
			sectorSpan);
		return PrepareWindowAtBounds(atlas, worldSeed, bounds, loadSector, warning);
	}

	/// <summary>
	/// Materialise an already planned adjacent window. Walking handoff must shift
	/// exactly one sector (or one on both axes at a corner); recentering around the
	/// current point can choose the same mosaic and therefore is not equivalent.
	/// </summary>
	public static AtlasPreparedWindow PrepareWindowAtBounds(WorldAtlasDefinition atlas,
		int worldSeed, AtlasMosaicBounds bounds,
		Func<int, int, AtlasSectorData> loadSector, Action<string> warning = null)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		if (loadSector == null) throw new ArgumentNullException(nameof(loadSector));
		ValidateBounds(atlas, bounds);
		AtlasSectorData data = AtlasSectorMosaic.Compose(atlas,
			bounds.MinSectorX, bounds.MinSectorZ, bounds.MaxSectorX, bounds.MaxSectorZ,
			loadSector);
		var window = new AtlasSectorWindow(data, atlas, worldSeed);
		var exclusions = new List<ReferenceSiteDefinition>();
		var siteBuilds = new List<AtlasReferenceSiteBuild>();
		if (atlas.Topology != null)
		{
			foreach (CanonicalSite canonical in atlas.Topology.Sites)
			{
				ReferenceSiteDefinition reference = canonical.ReferencePlan;
				if (!canonical.RunsInProduction || reference == null ||
				    !ReferenceIntersectsWindow(reference, data,
				    coreOnly: false)) continue;
				exclusions.Add(reference);
				if (!ReferenceIntersectsWindow(reference, data, coreOnly: true)) continue;
				if (!ReferenceFitsWindow(reference, data))
				{
					warning?.Invoke($"reference site '{reference.SiteId}' intersects " +
					                $"mosaic {bounds} but its footprint is larger than " +
					                "the loaded window; leaving its reserved ground unbuilt");
					continue;
				}
				ReferenceSiteStatistics statistics = ReferenceSiteBuilder.Build(window,
					reference);
				siteBuilds.Add(new AtlasReferenceSiteBuild(reference.SiteId, statistics));
			}
		}
		AtlasWildernessDressingStatistics wilderness = AtlasWildernessDressing.Apply(
			window, atlas, exclusions, worldSeed);
		return new AtlasPreparedWindow(window, bounds, wilderness, siteBuilds);
	}

	public static AtlasMosaicBounds BoundsOf(WorldAtlasDefinition atlas,
		AtlasSectorData data)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		if (data == null) throw new ArgumentNullException(nameof(data));
		if (data.CoreSize % atlas.SectorSize != 0)
			throw new InvalidOperationException(
				$"window core {data.CoreSize} is not a multiple of sector size {atlas.SectorSize}");
		int span = data.CoreSize / atlas.SectorSize;
		int coreMinX = data.OriginX + data.Apron;
		int coreMinZ = data.OriginZ + data.Apron;
		if (coreMinX % atlas.SectorSize != 0 || coreMinZ % atlas.SectorSize != 0)
			throw new InvalidOperationException(
				$"window core origin {coreMinX},{coreMinZ} is not sector-aligned");
		var bounds = new AtlasMosaicBounds(coreMinX / atlas.SectorSize,
			coreMinZ / atlas.SectorSize, coreMinX / atlas.SectorSize + span - 1,
			coreMinZ / atlas.SectorSize + span - 1);
		ValidateBounds(atlas, bounds);
		return bounds;
	}

	public static AtlasMosaicBounds WindowAround(WorldAtlasDefinition atlas,
		int globalX, int globalZ, int sectorSpan)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		if (globalX < 0 || globalZ < 0 || globalX >= atlas.Width || globalZ >= atlas.Depth)
			throw new ArgumentOutOfRangeException(nameof(globalX),
				$"atlas address {globalX},{globalZ} leaves {atlas.Width}x{atlas.Depth}");
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		if (sectorSpan < 1 || sectorSpan > columns || sectorSpan > rows)
			throw new ArgumentOutOfRangeException(nameof(sectorSpan),
				$"sector span {sectorSpan} leaves the {columns}x{rows} atlas grid");

		int windowBlocks = sectorSpan * atlas.SectorSize;
		// Choose the sector-aligned window whose centre is nearest the address.
		// Flooring the raw half-window offset biased every even-span window one
		// whole sector north-west and could trigger a replacement on the first
		// playable frame (Bloom exposed this at x=9800).
		int minX = FloorDiv(globalX - windowBlocks / 2 + atlas.SectorSize / 2,
			atlas.SectorSize);
		int minZ = FloorDiv(globalZ - windowBlocks / 2 + atlas.SectorSize / 2,
			atlas.SectorSize);
		minX = Math.Clamp(minX, 0, columns - sectorSpan);
		minZ = Math.Clamp(minZ, 0, rows - sectorSpan);

		// A click inside an authored reference footprint must not centre the fixed
		// window across the wrong sector edge and silently omit half of the site.
		// Shift the otherwise centred window only as far as required to contain the
		// complete footprint. Later continuous streaming can remove this constraint;
		// the current reload handoff still needs an atomic, self-contained site.
		ReferenceSiteDefinition enclosingSite = atlas.Topology?.Sites
			.Where(site => site.RunsInProduction)
			.Select(site => site.ReferencePlan)
			.FirstOrDefault(site => site != null && Contains(site, globalX, globalZ));
		if (enclosingSite != null)
		{
			(int siteMinX, int siteMinZ, int siteMaxX, int siteMaxZ) =
				ReferenceBounds(enclosingSite);
			int siteMinSectorX = siteMinX / atlas.SectorSize;
			int siteMinSectorZ = siteMinZ / atlas.SectorSize;
			int siteMaxSectorX = siteMaxX / atlas.SectorSize;
			int siteMaxSectorZ = siteMaxZ / atlas.SectorSize;
			if (siteMaxSectorX - siteMinSectorX + 1 <= sectorSpan)
				minX = Math.Clamp(minX, siteMaxSectorX - sectorSpan + 1,
					siteMinSectorX);
			if (siteMaxSectorZ - siteMinSectorZ + 1 <= sectorSpan)
				minZ = Math.Clamp(minZ, siteMaxSectorZ - sectorSpan + 1,
					siteMinSectorZ);
			minX = Math.Clamp(minX, 0, columns - sectorSpan);
			minZ = Math.Clamp(minZ, 0, rows - sectorSpan);
		}
		return new AtlasMosaicBounds(minX, minZ,
			minX + sectorSpan - 1, minZ + sectorSpan - 1);
	}

	/// <summary>
	/// Resolve the requested cell, then concentric square perimeters in a fixed
	/// clockwise order. The first result is stable across platforms and does not
	/// depend on candidate insertion order.
	/// </summary>
	public static bool TryResolveLanding(AtlasSectorWindow window,
		int requestedGlobalX, int requestedGlobalZ, out AtlasRuntimeLanding landing,
		out string requestedRejection)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		AtlasSectorData data = window.Data;
		int minX = data.OriginX + data.Apron;
		int minZ = data.OriginZ + data.Apron;
		int maxX = minX + data.CoreSize;
		int maxZ = minZ + data.CoreSize;
		if (TryResolveExactLanding(window, requestedGlobalX, requestedGlobalZ,
		    out landing, out requestedRejection))
		{
			return true;
		}

		int maxRadius = Math.Max(
			Math.Max(Math.Abs(requestedGlobalX - minX), Math.Abs(requestedGlobalX - (maxX - 1))),
			Math.Max(Math.Abs(requestedGlobalZ - minZ), Math.Abs(requestedGlobalZ - (maxZ - 1))));
		for (int radius = 1; radius <= maxRadius; radius++)
		{
			int north = requestedGlobalZ - radius;
			for (int x = requestedGlobalX - radius; x <= requestedGlobalX + radius; x++)
				if (TryCandidate(x, north, radius, out landing)) return true;

			int east = requestedGlobalX + radius;
			for (int z = requestedGlobalZ - radius + 1; z <= requestedGlobalZ + radius; z++)
				if (TryCandidate(east, z, radius, out landing)) return true;

			int south = requestedGlobalZ + radius;
			for (int x = requestedGlobalX + radius - 1; x >= requestedGlobalX - radius; x--)
				if (TryCandidate(x, south, radius, out landing)) return true;

			int west = requestedGlobalX - radius;
			for (int z = requestedGlobalZ + radius - 1; z > requestedGlobalZ - radius; z--)
				if (TryCandidate(west, z, radius, out landing)) return true;
		}

		landing = default;
		return false;

		bool TryCandidate(int x, int z, int radius, out AtlasRuntimeLanding result)
		{
			if (LandingRejection(window, x, z, minX, minZ, maxX, maxZ) == null)
			{
				result = MakeLanding(window, x, z, exactCell: false,
					searchRadius: radius);
				return true;
			}
			result = default;
			return false;
		}
	}

	/// <summary>
	/// Validate one exact global column without searching. Walking handoff uses
	/// this stricter contract so crossing a window edge can never move the player
	/// sideways or invoke the map teleport's dry-land recovery policy.
	/// </summary>
	public static bool TryResolveExactLanding(AtlasSectorWindow window,
		int globalX, int globalZ, out AtlasRuntimeLanding landing,
		out string rejection)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		AtlasSectorData data = window.Data;
		int minX = data.OriginX + data.Apron;
		int minZ = data.OriginZ + data.Apron;
		int maxX = minX + data.CoreSize;
		int maxZ = minZ + data.CoreSize;
		rejection = LandingRejection(window, globalX, globalZ,
			minX, minZ, maxX, maxZ);
		if (rejection != null)
		{
			landing = default;
			return false;
		}
		landing = MakeLanding(window, globalX, globalZ,
			exactCell: true, searchRadius: 0);
		return true;
	}

	/// <summary>
	/// Choose the one-sector neighbour implied by the unsafe-edge trigger. At a
	/// corner both axes move in the same atomic replacement. An atlas outer edge
	/// is clamped away; if the other corner axis can move, that cardinal move is
	/// still allowed.
	/// </summary>
	public static bool TryPlanWalkingTransition(WorldAtlasDefinition atlas,
		AtlasMosaicBounds current, float globalX, float globalZ, int triggerMargin,
		out AtlasWalkingTransition transition, out string refusal)
		=> TryPlanWalkingTransition(atlas, current, globalX, globalZ,
			triggerMargin, AtlasWindowEdge.None, out transition, out refusal);

	public static bool TryPlanWalkingTransition(WorldAtlasDefinition atlas,
		AtlasMosaicBounds current, float globalX, float globalZ, int triggerMargin,
		AtlasWindowEdge ignoredEdges, out AtlasWalkingTransition transition,
		out string refusal)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		ValidateBounds(atlas, current);
		if (triggerMargin < 1 || triggerMargin * 2 >= current.Span * atlas.SectorSize)
			throw new ArgumentOutOfRangeException(nameof(triggerMargin),
				$"trigger margin {triggerMargin} leaves no safe centre in {current.Span} sectors");
		int coreMinX = current.MinSectorX * atlas.SectorSize;
		int coreMinZ = current.MinSectorZ * atlas.SectorSize;
		int coreMaxX = (current.MaxSectorX + 1) * atlas.SectorSize;
		int coreMaxZ = (current.MaxSectorZ + 1) * atlas.SectorSize;
		AtlasWindowEdge triggered = AtlasWindowEdge.None;
		int shiftX = 0, shiftZ = 0;
		if (globalX <= coreMinX + triggerMargin)
		{
			triggered |= AtlasWindowEdge.West;
			shiftX = -1;
		}
		else if (globalX >= coreMaxX - triggerMargin)
		{
			triggered |= AtlasWindowEdge.East;
			shiftX = 1;
		}
		if (globalZ <= coreMinZ + triggerMargin)
		{
			triggered |= AtlasWindowEdge.North;
			shiftZ = -1;
		}
		else if (globalZ >= coreMaxZ - triggerMargin)
		{
			triggered |= AtlasWindowEdge.South;
			shiftZ = 1;
		}
		triggered &= ~ignoredEdges;
		if ((triggered & (AtlasWindowEdge.West | AtlasWindowEdge.East)) == 0)
			shiftX = 0;
		if ((triggered & (AtlasWindowEdge.North | AtlasWindowEdge.South)) == 0)
			shiftZ = 0;
		if (triggered == AtlasWindowEdge.None)
		{
			transition = default;
			refusal = null;
			return false;
		}

		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		int nextMinX = Math.Clamp(current.MinSectorX + shiftX,
			0, columns - current.Span);
		int nextMinZ = Math.Clamp(current.MinSectorZ + shiftZ,
			0, rows - current.Span);
		var next = new AtlasMosaicBounds(nextMinX, nextMinZ,
			nextMinX + current.Span - 1, nextMinZ + current.Span - 1);
		if (next == current)
		{
			transition = new AtlasWalkingTransition(current, current, triggered,
				AtlasWindowEdge.None, globalX, globalZ);
			refusal = $"atlas outer edge {DescribeEdges(triggered)} has no adjacent window";
			return false;
		}
		AtlasWindowEdge shifted = AtlasWindowEdge.None;
		if (nextMinX < current.MinSectorX) shifted |= AtlasWindowEdge.West;
		else if (nextMinX > current.MinSectorX) shifted |= AtlasWindowEdge.East;
		if (nextMinZ < current.MinSectorZ) shifted |= AtlasWindowEdge.North;
		else if (nextMinZ > current.MinSectorZ) shifted |= AtlasWindowEdge.South;
		transition = new AtlasWalkingTransition(current, next, triggered, shifted,
			globalX, globalZ);
		refusal = null;
		return true;
	}

	public static bool InsideWalkingRearmBand(WorldAtlasDefinition atlas,
		AtlasMosaicBounds bounds, float globalX, float globalZ, int rearmMargin)
		=> InsideWalkingRearmBand(atlas, bounds, globalX, globalZ, rearmMargin,
			AtlasWindowEdge.North | AtlasWindowEdge.East |
			AtlasWindowEdge.South | AtlasWindowEdge.West);

	public static bool InsideWalkingRearmBand(WorldAtlasDefinition atlas,
		AtlasMosaicBounds bounds, float globalX, float globalZ, int rearmMargin,
		AtlasWindowEdge edges)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		ValidateBounds(atlas, bounds);
		int minX = bounds.MinSectorX * atlas.SectorSize;
		int minZ = bounds.MinSectorZ * atlas.SectorSize;
		int maxX = (bounds.MaxSectorX + 1) * atlas.SectorSize;
		int maxZ = (bounds.MaxSectorZ + 1) * atlas.SectorSize;
		return ((edges & AtlasWindowEdge.West) == 0 || globalX > minX + rearmMargin) &&
		       ((edges & AtlasWindowEdge.East) == 0 || globalX < maxX - rearmMargin) &&
		       ((edges & AtlasWindowEdge.North) == 0 || globalZ > minZ + rearmMargin) &&
		       ((edges & AtlasWindowEdge.South) == 0 || globalZ < maxZ - rearmMargin);
	}

	public static AtlasWindowEdge OppositeEdges(AtlasWindowEdge edges)
	{
		AtlasWindowEdge opposite = AtlasWindowEdge.None;
		if ((edges & AtlasWindowEdge.North) != 0) opposite |= AtlasWindowEdge.South;
		if ((edges & AtlasWindowEdge.East) != 0) opposite |= AtlasWindowEdge.West;
		if ((edges & AtlasWindowEdge.South) != 0) opposite |= AtlasWindowEdge.North;
		if ((edges & AtlasWindowEdge.West) != 0) opposite |= AtlasWindowEdge.East;
		return opposite;
	}

	/// <summary>
	/// The registered coarse sources are a cheap first hint when a click is in
	/// open water. They do not declare a final landing; the compiled window still
	/// has to pass <see cref="TryResolveLanding"/> afterwards.
	/// </summary>
	public static bool TryNearestAuthoredDryHint(WorldAtlasDefinition atlas,
		int requestedGlobalX, int requestedGlobalZ, out BlockPoint hint,
		out int pixelRadius)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		Image land = LoadLayer(atlas, AtlasLayerKind.Land);
		Image water = LoadLayer(atlas, AtlasLayerKind.Water);
		if (land == null)
		{
			hint = null;
			pixelRadius = -1;
			return false;
		}
		if (water != null && (water.GetWidth() != land.GetWidth() ||
		    water.GetHeight() != land.GetHeight()))
			throw new InvalidOperationException("registered atlas land and water layers have different dimensions");

		int px = Math.Clamp(requestedGlobalX / atlas.BlocksPerPixel, 0, land.GetWidth() - 1);
		int pz = Math.Clamp(requestedGlobalZ / atlas.BlocksPerPixel, 0, land.GetHeight() - 1);
		int maxRadius = Math.Max(land.GetWidth(), land.GetHeight());
		for (int radius = 0; radius <= maxRadius; radius++)
		{
			if (radius == 0)
			{
				if (TryPixel(px, pz, radius, out hint, out pixelRadius)) return true;
				continue;
			}
			int north = pz - radius;
			for (int x = px - radius; x <= px + radius; x++)
				if (TryPixel(x, north, radius, out hint, out pixelRadius)) return true;
			int east = px + radius;
			for (int z = pz - radius + 1; z <= pz + radius; z++)
				if (TryPixel(east, z, radius, out hint, out pixelRadius)) return true;
			int south = pz + radius;
			for (int x = px + radius - 1; x >= px - radius; x--)
				if (TryPixel(x, south, radius, out hint, out pixelRadius)) return true;
			int west = px - radius;
			for (int z = pz + radius - 1; z > pz - radius; z--)
				if (TryPixel(west, z, radius, out hint, out pixelRadius)) return true;
		}

		hint = null;
		pixelRadius = -1;
		return false;

		bool TryPixel(int x, int z, int radius, out BlockPoint point, out int foundRadius)
		{
			if (x >= 0 && z >= 0 && x < land.GetWidth() && z < land.GetHeight() &&
			    land.GetPixel(x, z).R >= .5f &&
			    (water == null || water.GetPixel(x, z).R < 240f / 255f))
			{
				point = new BlockPoint
				{
					X = Math.Min(atlas.Width - 1,
						x * atlas.BlocksPerPixel + atlas.BlocksPerPixel / 2),
					Z = Math.Min(atlas.Depth - 1,
						z * atlas.BlocksPerPixel + atlas.BlocksPerPixel / 2),
				};
				foundRadius = radius;
				return true;
			}
			point = null;
			foundRadius = -1;
			return false;
		}
	}

	/// <summary>
	/// Last-resort address owned by a transcribed site rather than by terrain
	/// heuristics. The compiled/built cell must still pass the ordinary landing
	/// resolver; this method supplies only the canonical address.
	/// </summary>
	public static bool TryGetAuthoredRecoverySpawn(WorldAtlasDefinition atlas,
		out BlockPoint spawn)
	{
		ReferenceSiteDefinition recovery = atlas?.Topology?.Sites
			.Where(site => site.RunsInProduction)
			.Select(site => site.ReferencePlan)
			.FirstOrDefault(site => site?.SiteId == RecoverySiteId);
		spawn = recovery?.ToGlobal(recovery.PlayerSpawn);
		return spawn != null;
	}

	private static AtlasRuntimeLanding MakeLanding(AtlasSectorWindow window,
		int globalX, int globalZ, bool exactCell, int searchRadius)
	{
		int localX = globalX - window.Data.OriginX;
		int localZ = globalZ - window.Data.OriginZ;
		return new AtlasRuntimeLanding(globalX, globalZ, localX, localZ,
			window.Grid.HeightAt(localX, localZ), exactCell, searchRadius);
	}

	private static string LandingRejection(AtlasSectorWindow window, int globalX,
		int globalZ, int coreMinX, int coreMinZ, int coreMaxX, int coreMaxZ)
	{
		if (globalX < coreMinX || globalZ < coreMinZ ||
		    globalX >= coreMaxX || globalZ >= coreMaxZ) return "outside-window";
		AtlasSectorData data = window.Data;
		VoxelGrid grid = window.Grid;
		int x = globalX - data.OriginX;
		int z = globalZ - data.OriginZ;
		int index = z * data.Width + x;
		int ground = grid.HeightAt(x, z);
		if (ground < 1 || ground + 2 >= grid.Height) return "vertical-bounds";
		byte cap = grid.At(x, ground - 1, z);
		if (!TraversableTop(cap)) return "blocked-material";
		if (grid.SolidAt(x, ground, z) || grid.SolidAt(x, ground + 1, z))
			return "blocked-clearance";
		if (data.WaterSurface[index] > 0 && ground <= data.WaterSurface[index] + 1)
			return "water";

		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			int gx = globalX + dx, gz = globalZ + dz;
			if (gx < coreMinX || gz < coreMinZ || gx >= coreMaxX || gz >= coreMaxZ)
				return "window-edge";
			int xx = x + dx, zz = z + dz;
			int neighbour = zz * data.Width + xx;
			int neighbourGround = grid.HeightAt(xx, zz);
			if (Math.Abs(neighbourGround - ground) > 1) return "ledge";
			if (neighbourGround < 1 || !TraversableTop(grid.At(xx, neighbourGround - 1, zz)))
				return "blocked-neighbour";
			if (data.WaterSurface[neighbour] > 0 &&
			    neighbourGround <= data.WaterSurface[neighbour] + 1)
				return "water-neighbour";
		}
		return null;
	}

	private static bool TraversableTop(byte material) =>
		Palette.IsGrassSurface(material) || material is
			Palette.SOIL or Palette.SAND or Palette.SNOW or Palette.MUD or Palette.MOSS or
			Palette.BLOSSOM_DRIFT or Palette.SCREE or Palette.STONE or Palette.STONE_PALE or
			Palette.STONE_WARM or Palette.MOSS_STONE or Palette.PATH or Palette.PAVING or
			Palette.PLANK or Palette.PLANK_PALE;

	private static Image LoadLayer(WorldAtlasDefinition atlas, AtlasLayerKind kind)
	{
		AtlasSourceLayer layer = atlas.SourceLayers.FirstOrDefault(candidate =>
			candidate.Kind == kind && candidate.Status != AtlasLayerStatus.Planned);
		if (layer == null || !Godot.FileAccess.FileExists(layer.Path)) return null;
		using var file = Godot.FileAccess.Open(layer.Path, Godot.FileAccess.ModeFlags.Read);
		if (file == null) return null;
		var image = new Image();
		Error error = image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength()));
		return error == Error.Ok && !image.IsEmpty() ? image : null;
	}

	private static bool ReferenceIntersectsWindow(ReferenceSiteDefinition site,
		AtlasSectorData data, bool coreOnly)
	{
		(int minX, int minZ, int maxX, int maxZ) = ReferenceBounds(site);
		int windowMinX = coreOnly ? data.OriginX + data.Apron : data.OriginX;
		int windowMinZ = coreOnly ? data.OriginZ + data.Apron : data.OriginZ;
		int size = coreOnly ? data.CoreSize : data.Width;
		return maxX >= windowMinX && maxZ >= windowMinZ &&
		       minX < windowMinX + size && minZ < windowMinZ + size;
	}

	private static bool Contains(ReferenceSiteDefinition site, int globalX,
		int globalZ)
	{
		(int minX, int minZ, int maxX, int maxZ) = ReferenceBounds(site);
		return globalX >= minX && globalZ >= minZ &&
		       globalX <= maxX && globalZ <= maxZ;
	}

	private static bool ReferenceFitsWindow(ReferenceSiteDefinition site,
		AtlasSectorData data)
	{
		(int minX, int minZ, int maxX, int maxZ) = ReferenceBounds(site);
		return minX >= data.OriginX && minZ >= data.OriginZ &&
		       maxX < data.OriginX + data.Width && maxZ < data.OriginZ + data.Depth;
	}

	private static (int minX, int minZ, int maxX, int maxZ) ReferenceBounds(
		ReferenceSiteDefinition site)
	{
		PlanPoint[] corners =
		{
			new() { X = site.RuntimeFootprintMin.X, Z = site.RuntimeFootprintMin.Z },
			new() { X = site.RuntimeFootprintMax.X, Z = site.RuntimeFootprintMin.Z },
			new() { X = site.RuntimeFootprintMin.X, Z = site.RuntimeFootprintMax.Z },
			new() { X = site.RuntimeFootprintMax.X, Z = site.RuntimeFootprintMax.Z },
		};
		int minX = int.MaxValue, minZ = int.MaxValue;
		int maxX = int.MinValue, maxZ = int.MinValue;
		foreach (PlanPoint corner in corners)
		{
			BlockPoint global = site.ToGlobalRuntime(corner);
			minX = Math.Min(minX, global.X); minZ = Math.Min(minZ, global.Z);
			maxX = Math.Max(maxX, global.X); maxZ = Math.Max(maxZ, global.Z);
		}
		return (minX, minZ, maxX, maxZ);
	}

	private static void ValidateBounds(WorldAtlasDefinition atlas,
		AtlasMosaicBounds bounds)
	{
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		if (bounds.MinSectorX < 0 || bounds.MinSectorZ < 0 ||
		    bounds.MaxSectorX < bounds.MinSectorX ||
		    bounds.MaxSectorZ < bounds.MinSectorZ ||
		    bounds.MaxSectorX >= columns || bounds.MaxSectorZ >= rows ||
		    bounds.MaxSectorX - bounds.MinSectorX !=
		    bounds.MaxSectorZ - bounds.MinSectorZ)
			throw new ArgumentOutOfRangeException(nameof(bounds),
				$"mosaic {bounds} leaves or is not square within {columns}x{rows} atlas sectors");
	}

	public static string DescribeEdges(AtlasWindowEdge edges)
	{
		if (edges == AtlasWindowEdge.None) return "none";
		var names = new List<string>(2);
		if ((edges & AtlasWindowEdge.North) != 0) names.Add("north");
		if ((edges & AtlasWindowEdge.East) != 0) names.Add("east");
		if ((edges & AtlasWindowEdge.South) != 0) names.Add("south");
		if ((edges & AtlasWindowEdge.West) != 0) names.Add("west");
		return string.Join('+', names);
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
	}
}

public readonly record struct AtlasMosaicBounds(int MinSectorX, int MinSectorZ,
	int MaxSectorX, int MaxSectorZ)
{
	public int Span => MaxSectorX - MinSectorX + 1;
	public override string ToString() =>
		$"{MinSectorX},{MinSectorZ}..{MaxSectorX},{MaxSectorZ}";
}

public readonly record struct AtlasRuntimeLanding(int GlobalX, int GlobalZ,
	int LocalX, int LocalZ, int SurfaceY, bool ExactCell, int SearchRadius);

public sealed record AtlasPreparedWindow(AtlasSectorWindow Window,
	AtlasMosaicBounds Bounds, AtlasWildernessDressingStatistics Wilderness,
	IReadOnlyList<AtlasReferenceSiteBuild> SiteBuilds);

public readonly record struct AtlasReferenceSiteBuild(string SiteId,
	ReferenceSiteStatistics Statistics);

[Flags]
public enum AtlasWindowEdge
{
	None = 0,
	North = 1,
	East = 2,
	South = 4,
	West = 8,
}

public readonly record struct AtlasWalkingTransition(AtlasMosaicBounds From,
	AtlasMosaicBounds To, AtlasWindowEdge TriggeredEdges,
	AtlasWindowEdge ShiftedEdges, float GlobalX, float GlobalZ);

public enum AtlasWalkingHandoffDecision
{
	None,
	Suppressed,
	Transition,
	Refused,
}

/// <summary>
/// Frame-stepped hysteresis shared by runtime and headless verification. A
/// completed or refused boundary cannot request again until its cooldown has
/// elapsed and the traveller has returned to the deeper rearm band.
/// </summary>
public sealed class AtlasWalkingHandoffLatch
{
	private readonly int _cooldownFrames;
	private int _cooldownRemaining;
	private AtlasWindowEdge _rearmEdges = AtlasWindowEdge.None;
	public bool Armed { get; private set; } = true;
	public int CooldownRemaining => _cooldownRemaining;

	public AtlasWalkingHandoffLatch(int cooldownFrames)
	{
		if (cooldownFrames < 1)
			throw new ArgumentOutOfRangeException(nameof(cooldownFrames));
		_cooldownFrames = cooldownFrames;
	}

	public AtlasWalkingHandoffDecision Evaluate(WorldAtlasDefinition atlas,
		AtlasMosaicBounds current, float globalX, float globalZ,
		int triggerMargin, int rearmMargin, out AtlasWalkingTransition transition,
		out string refusal)
		=> Evaluate(atlas, current, globalX, globalZ, triggerMargin, rearmMargin,
			AtlasWindowEdge.None, out transition, out refusal);

	public AtlasWalkingHandoffDecision Evaluate(WorldAtlasDefinition atlas,
		AtlasMosaicBounds current, float globalX, float globalZ,
		int triggerMargin, int rearmMargin, AtlasWindowEdge ignoredEdges,
		out AtlasWalkingTransition transition, out string refusal)
	{
		transition = default;
		refusal = null;
		if (_cooldownRemaining > 0)
		{
			_cooldownRemaining--;
			return AtlasWalkingHandoffDecision.Suppressed;
		}
		if (!Armed)
		{
			if (!AtlasRuntimeHandoff.InsideWalkingRearmBand(atlas, current,
			    globalX, globalZ, rearmMargin, _rearmEdges))
				return AtlasWalkingHandoffDecision.Suppressed;
			Armed = true;
			_rearmEdges = AtlasWindowEdge.None;
		}
		if (AtlasRuntimeHandoff.TryPlanWalkingTransition(atlas, current,
		    globalX, globalZ, triggerMargin, ignoredEdges,
		    out transition, out refusal))
		{
			Armed = false;
			return AtlasWalkingHandoffDecision.Transition;
		}
		if (refusal != null)
		{
			Armed = false;
			// Outer-only refusal is enforced by the runtime's blocked-edge barrier.
			// Once cooldown elapses, the same blocked edge is ignored so walking
			// parallel to the atlas boundary can still shift on the other axis.
			_rearmEdges = transition.ShiftedEdges;
			_cooldownRemaining = _cooldownFrames;
			return AtlasWalkingHandoffDecision.Refused;
		}
		return AtlasWalkingHandoffDecision.None;
	}

	public void Complete(AtlasWalkingTransition transition)
	{
		Armed = false;
		_rearmEdges = AtlasRuntimeHandoff.OppositeEdges(transition.ShiftedEdges);
		_cooldownRemaining = _cooldownFrames;
	}

	public void Reject(AtlasWalkingTransition transition)
	{
		Armed = false;
		_rearmEdges = transition.TriggeredEdges;
		_cooldownRemaining = _cooldownFrames;
	}

	public void Reset()
	{
		Armed = true;
		_cooldownRemaining = 0;
		_rearmEdges = AtlasWindowEdge.None;
	}
}
