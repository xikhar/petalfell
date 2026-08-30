using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Godot;

namespace Petalfell.World;

/// <summary>
/// Authored L3 composition for one connected domain. Coordinates are local to
/// Origin and rotate with AxisDegrees; derived sector builds may split this
/// record for storage, but may never reinterpret or move its geometry.
/// </summary>
public sealed class DomainPlanDefinition
{
	public int Version { get; set; } = 1;
	public string DomainId { get; set; } = "";
	public PlanSourceMode SourceMode { get; set; } = PlanSourceMode.ToolingFixture;
	public string ReconstructionReferencePath { get; set; } = "";
	public PlanReferenceView ReferenceView { get; set; }
	public BlockPoint Origin { get; set; } = new();
	public float AxisDegrees { get; set; }
	public List<string> ReferencePaths { get; set; } = new();
	public List<PlanPlatform> Platforms { get; set; } = new();
	public List<PlanStair> Stairs { get; set; } = new();
	public List<PlanWall> Walls { get; set; } = new();
	public List<PlanRouteSocket> RouteSockets { get; set; } = new();
	public List<PlanLandmark> Landmarks { get; set; } = new();

	public static DomainPlanDefinition Load(string resourcePath) =>
		WorldAtlasDefinition.ReadJson<DomainPlanDefinition>(resourcePath, "domain plan");

	public WorldAuditReport Audit(WorldAtlasDefinition atlas, CanonicalWorldDefinition world,
		CanonicalDomain domain)
	{
		var report = new WorldAuditReport();
		if (Version != 1) report.Error($"version must be 1, got {Version}");
		if (DomainId != domain.Id)
			report.Error($"domainId '{DomainId}' does not match owning domain '{domain.Id}'");
		if (MathF.Abs(NormalizeAngle(AxisDegrees - domain.AxisDegrees)) > 0.01f)
			report.Error($"axisDegrees {AxisDegrees} does not match domain axis {domain.AxisDegrees}");
		if (!InsideAtlas(Origin, atlas)) report.Error("origin lies outside the atlas");
		else if (!PointInPolygon(Origin, domain.Boundary)) report.Error("origin lies outside the domain boundary");
		if (ReferencePaths.Count == 0) report.Error("at least one visual reference is required");
		foreach (string path in ReferencePaths)
			if (!Godot.FileAccess.FileExists(path)) report.Error($"reference '{path}' does not exist");
		if (SourceMode == PlanSourceMode.ReferenceReconstruction)
		{
			if (string.IsNullOrWhiteSpace(ReconstructionReferencePath))
				report.Error("a reference reconstruction requires reconstructionReferencePath");
			else if (!ReferencePaths.Contains(ReconstructionReferencePath, StringComparer.Ordinal))
				report.Error("reconstructionReferencePath must also be the plan's visual reference");
			else if (!Godot.FileAccess.FileExists(ReconstructionReferencePath))
				report.Error($"reconstruction reference '{ReconstructionReferencePath}' does not exist");
			if (ReferencePaths.Count != 1)
				report.Error("a reference reconstruction must name exactly one structural reference");
			if (ReferenceView == null)
				report.Error("a reference reconstruction requires a locked referenceView");
			else
			{
				Point(ReferenceView.Focus, "reference view focus", requireInsideDomain: false);
				if (ReferenceView.Distance <= 0f || ReferenceView.PitchDegrees <= 0f ||
				    ReferenceView.PitchDegrees >= 89f)
					report.Error("referenceView needs positive distance and pitch in (0,89)");
				if (ReferenceView.HeightOffset < 0f || ReferenceView.HeightOffset > 64f)
					report.Error("referenceView heightOffset must be in 0–64 blocks");
				if (ReferenceView.SourceWidth <= 0 || ReferenceView.SourceHeight <= 0)
					report.Error("referenceView source dimensions must be positive");
			}
		}
		else
			report.Warning("plan is a tooling fixture, not production reconstruction content");

		var ids = new HashSet<string>(StringComparer.Ordinal);
		var sites = world.Sites.Where(s => s.DomainId == domain.Id)
			.ToDictionary(s => s.Id, StringComparer.Ordinal);
		var nodes = world.RouteNodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var platforms = new Dictionary<string, PlanPlatform>(StringComparer.Ordinal);

		bool Id(string id, string kind)
		{
			if (string.IsNullOrWhiteSpace(id)) { report.Error($"{kind} id is required"); return false; }
			if (!ids.Add(id)) { report.Error($"duplicate plan id '{id}'"); return false; }
			return true;
		}

		bool Site(string id, string owner)
		{
			if (string.IsNullOrWhiteSpace(id)) { report.Error($"{owner} siteId is required"); return false; }
			if (!sites.ContainsKey(id)) { report.Error($"{owner} references site '{id}' outside domain '{domain.Id}'"); return false; }
			return true;
		}

		void Point(PlanPoint point, string owner, bool requireInsideDomain = true)
		{
			if (point == null) { report.Error($"{owner} point is required"); return; }
			BlockPoint global = ToGlobal(point);
			if (!InsideAtlas(global, atlas)) report.Error($"{owner} maps outside the atlas at {global.X},{global.Z}");
			else if (requireInsideDomain && !PointInPolygon(global, domain.Boundary))
				report.Error($"{owner} maps outside domain '{domain.Id}' at {global.X},{global.Z}");
		}

		foreach (var platform in Platforms)
		{
			if (Id(platform.Id, "platform")) platforms[platform.Id] = platform;
			bool validSite = Site(platform.SiteId, $"platform '{platform.Id}'");
			if (platform.Polygon.Count < 3) report.Error($"platform '{platform.Id}' needs at least three points");
			else if (MathF.Abs(PolygonArea(platform.Polygon)) < 1f) report.Error($"platform '{platform.Id}' polygon has no area");
			foreach (var point in platform.Polygon)
			{
				Point(point, $"platform '{platform.Id}'");
				if (validSite && !InsideSiteEnvelope(ToGlobal(point), sites[platform.SiteId]))
					report.Error($"platform '{platform.Id}' leaves site envelope '{platform.SiteId}'");
			}
			if (platform.SurfaceY <= atlas.SeaLevel || platform.SurfaceY >= atlas.Height - 1)
				report.Error($"platform '{platform.Id}' surfaceY {platform.SurfaceY} must be above sea level and below atlas ceiling");
			if (string.IsNullOrWhiteSpace(platform.MaterialId)) report.Error($"platform '{platform.Id}' materialId is required");
			if (platform.Reclamation < 0f || platform.Reclamation > 1f)
				report.Error($"platform '{platform.Id}' reclamation {platform.Reclamation} must be in 0–1");
			foreach (var cutout in platform.Cutouts)
			{
				Id(cutout.Id, "platform cutout");
				if (cutout.Role == PlanCutoutRole.Collapsed && (cutout.Depth < 2 || cutout.Depth > 16))
					report.Error($"collapsed platform cutout '{cutout.Id}' depth {cutout.Depth} must be in 2–16");
				else if (cutout.Role == PlanCutoutRole.Terrain && cutout.Depth != 0)
					report.Error($"terrain platform cutout '{cutout.Id}' must not set a collapse depth");
				if (cutout.Reclamation < 0f || cutout.Reclamation > 1f)
					report.Error($"platform cutout '{cutout.Id}' reclamation {cutout.Reclamation} must be in 0–1");
				if (cutout.Polygon.Count < 3)
					report.Error($"platform cutout '{cutout.Id}' needs at least three points");
				else if (MathF.Abs(PolygonArea(cutout.Polygon)) < 1f)
					report.Error($"platform cutout '{cutout.Id}' polygon has no area");
				foreach (var point in cutout.Polygon)
				{
					Point(point, $"platform cutout '{cutout.Id}'");
					if (!PointInLocalPolygon(point, platform.Polygon))
						report.Error($"platform cutout '{cutout.Id}' leaves platform '{platform.Id}'");
				}
			}
			foreach (var patch in platform.SurfacePatches)
			{
				Id(patch.Id, "surface patch");
				if (patch.Coverage <= 0f || patch.Coverage > 1f)
					report.Error($"surface patch '{patch.Id}' coverage {patch.Coverage} must be in (0,1]");
				if (patch.Wavelength < 12 || patch.Wavelength > 192)
					report.Error($"surface patch '{patch.Id}' wavelength {patch.Wavelength} must be in 12–192 blocks");
				if (string.IsNullOrWhiteSpace(patch.MaterialId))
					report.Error($"surface patch '{patch.Id}' materialId is required");
				if (patch.Polygon.Count < 3)
					report.Error($"surface patch '{patch.Id}' needs at least three points");
				else if (MathF.Abs(PolygonArea(patch.Polygon)) < 1f)
					report.Error($"surface patch '{patch.Id}' polygon has no area");
				foreach (var point in patch.Polygon)
				{
					Point(point, $"surface patch '{patch.Id}'");
					if (!PointInLocalPolygon(point, platform.Polygon))
						report.Error($"surface patch '{patch.Id}' leaves platform '{platform.Id}'");
				}
			}
		}

		if (Platforms.Select(p => p.SurfaceY).Distinct().Count() < 2)
			report.Error("a connected domain plan needs a hierarchy of at least two platform levels");
		if (SourceMode == PlanSourceMode.ToolingFixture)
			foreach (string siteId in sites.Keys)
				if (!Platforms.Any(p => p.SiteId == siteId)) report.Error($"site '{siteId}' has no authored platform");
		else if (Platforms.Count == 0)
			report.Error("a reference reconstruction needs at least one measured platform");
		var activeSiteIds = Platforms.Select(p => p.SiteId)
			.Where(id => !string.IsNullOrWhiteSpace(id))
			.ToHashSet(StringComparer.Ordinal);

		foreach (var stair in Stairs)
		{
			Id(stair.Id, "stair");
			Site(stair.SiteId, $"stair '{stair.Id}'");
			if (!platforms.TryGetValue(stair.FromPlatformId, out var from))
				report.Error($"stair '{stair.Id}' references missing fromPlatformId '{stair.FromPlatformId}'");
			if (!platforms.TryGetValue(stair.ToPlatformId, out var to))
				report.Error($"stair '{stair.Id}' references missing toPlatformId '{stair.ToPlatformId}'");
			if (from != null && to != null)
			{
				int rise = Math.Abs(from.SurfaceY - to.SurfaceY);
				if (rise == 0 || rise > 20) report.Error($"stair '{stair.Id}' must connect levels separated by 1–20 blocks");
			}
			if (stair.Role == PlanStairRole.Grand && (stair.Width < 8 || stair.Width > 14))
				report.Error($"grand stair '{stair.Id}' width {stair.Width} must be 8–14 blocks");
			else if (stair.Role != PlanStairRole.Grand && stair.Width < 3)
				report.Error($"stair '{stair.Id}' width must be at least 3 blocks");
			Point(stair.From, $"stair '{stair.Id}' start");
			Point(stair.To, $"stair '{stair.Id}' end");
		}

		foreach (var wall in Walls)
		{
			Id(wall.Id, "wall");
			if (!string.IsNullOrWhiteSpace(wall.SiteId)) Site(wall.SiteId, $"wall '{wall.Id}'");
			if (wall.Points.Count < 2) report.Error($"wall '{wall.Id}' needs at least two points");
			foreach (var point in wall.Points) Point(point, $"wall '{wall.Id}'");
			if (wall.Height <= 0 || wall.Height > 40) report.Error($"wall '{wall.Id}' height must be in 1–40");
			if (string.IsNullOrWhiteSpace(wall.MaterialId)) report.Error($"wall '{wall.Id}' materialId is required");
		}

		var socketNodes = new HashSet<string>(StringComparer.Ordinal);
		var socketIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var socket in RouteSockets)
		{
			if (Id(socket.Id, "route socket")) socketIds.Add(socket.Id);
			if (!nodes.TryGetValue(socket.RouteNodeId, out var node))
				report.Error($"route socket '{socket.Id}' references missing route node '{socket.RouteNodeId}'");
			else
			{
				BlockPoint global = ToGlobal(socket.Point);
				if (!global.Same(node.Point))
					report.Error($"route socket '{socket.Id}' maps to {global.X},{global.Z}, not node '{node.Id}' at {node.Point.X},{node.Point.Z}");
				socketNodes.Add(node.Id);
			}
			if (!string.IsNullOrWhiteSpace(socket.PlatformId) && !platforms.ContainsKey(socket.PlatformId))
				report.Error($"route socket '{socket.Id}' references missing platform '{socket.PlatformId}'");
			Point(socket.Point, $"route socket '{socket.Id}'", false);
		}
		IEnumerable<CanonicalSite> socketSites = SourceMode == PlanSourceMode.ReferenceReconstruction
			? sites.Values.Where(site => activeSiteIds.Contains(site.Id))
			: sites.Values;
		foreach (var site in socketSites)
		foreach (var entrance in site.Entrances)
			if (!socketNodes.Contains(entrance.RouteNodeId))
				report.Error($"site entrance '{site.Id}/{entrance.Id}' has no L3 route socket for '{entrance.RouteNodeId}'");
		foreach (var wall in Walls)
		foreach (string socketId in wall.OpeningSocketIds)
			if (!socketIds.Contains(socketId)) report.Error($"wall '{wall.Id}' references missing opening socket '{socketId}'");

		foreach (var landmark in Landmarks)
		{
			Id(landmark.Id, "landmark");
			Site(landmark.SiteId, $"landmark '{landmark.Id}'");
			Point(landmark.Point, $"landmark '{landmark.Id}'");
			if (string.IsNullOrWhiteSpace(landmark.PlatformId) || !platforms.ContainsKey(landmark.PlatformId))
				report.Error($"landmark '{landmark.Id}' references missing platform '{landmark.PlatformId}'");
			ValidateLandmarkScale(landmark, report);
		}

		IEnumerable<CanonicalSite> silhouetteSites = SourceMode == PlanSourceMode.ReferenceReconstruction
			? sites.Values.Where(site => activeSiteIds.Contains(site.Id))
			: sites.Values;
		foreach (var site in silhouetteSites.Where(s => s.Tier is SiteTier.District or SiteTier.GreatWork))
			if (!Landmarks.Any(l => l.SiteId == site.Id && l.Height >= 15))
				report.Error($"{site.Tier.ToString().ToLowerInvariant()} site '{site.Id}' has no 15-block silhouette landmark");
		return report;
	}

	public BlockPoint ToGlobal(PlanPoint local)
	{
		float radians = AxisDegrees * MathF.PI / 180f;
		float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
		return new BlockPoint
		{
			X = Origin.X + (int)MathF.Round(local.X * cos + local.Z * sin),
			Z = Origin.Z + (int)MathF.Round(-local.X * sin + local.Z * cos),
		};
	}

	private static void ValidateLandmarkScale(PlanLandmark landmark, WorldAuditReport report)
	{
		bool Range(int value, int min, int max) => value >= min && value <= max;
			switch (landmark.Kind)
			{
			case PlanLandmarkKind.Column when !Range(landmark.Height, 15, 30):
				report.Error($"column '{landmark.Id}' height {landmark.Height} must be 15–30"); break;
			case PlanLandmarkKind.Arch when !Range(landmark.Height, 12, 30) || !Range(landmark.Span, 8, 16):
				report.Error($"arch '{landmark.Id}' must be 12–30 high with an 8–16 span"); break;
			case PlanLandmarkKind.Pylon when !Range(landmark.Height, 15, 32):
				report.Error($"pylon '{landmark.Id}' height {landmark.Height} must be 15–32"); break;
			case PlanLandmarkKind.FallenColumn when !Range(landmark.Length, 10, 30):
				report.Error($"fallen column '{landmark.Id}' length {landmark.Length} must be 10–30"); break;
			case PlanLandmarkKind.Colonnade when landmark.Count < 3 || !Range(landmark.Height, 15, 30):
				report.Error($"colonnade '{landmark.Id}' needs at least 3 columns at 15–30 blocks"); break;
			case PlanLandmarkKind.Emblem or PlanLandmarkKind.Basin when landmark.Span <= 0:
				report.Error($"{landmark.Kind.ToString().ToLowerInvariant()} '{landmark.Id}' span must be positive"); break;
		}
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}

	private static bool InsideAtlas(BlockPoint point, WorldAtlasDefinition atlas) =>
		point != null && point.X >= 0 && point.Z >= 0 && point.X < atlas.Width && point.Z < atlas.Depth;

	private static float PolygonArea(List<PlanPoint> polygon)
	{
		float area = 0f;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			area += polygon[j].X * polygon[i].Z - polygon[i].X * polygon[j].Z;
		return area * .5f;
	}

	private static bool PointInLocalPolygon(PlanPoint point, List<PlanPoint> polygon)
	{
		if (point == null || polygon.Count < 3) return false;
		bool inside = false;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			PlanPoint a = polygon[i], b = polygon[j];
			bool crosses = (a.Z > point.Z) != (b.Z > point.Z) &&
			               point.X < (b.X - a.X) * (point.Z - a.Z) / (float)(b.Z - a.Z) + a.X;
			if (crosses) inside = !inside;
		}
		return inside;
	}

	private static bool InsideSiteEnvelope(BlockPoint point, CanonicalSite site)
	{
		float dx = point.X - site.Centre.X, dz = point.Z - site.Centre.Z;
		float angle = -site.OrientationDegrees * MathF.PI / 180f;
		float localX = dx * MathF.Cos(angle) - dz * MathF.Sin(angle);
		float localZ = dx * MathF.Sin(angle) + dz * MathF.Cos(angle);
		return MathF.Abs(localX) <= site.ExtentX * .5f + .5f &&
		       MathF.Abs(localZ) <= site.ExtentZ * .5f + .5f;
	}

	private static bool PointInPolygon(BlockPoint point, List<BlockPoint> polygon)
	{
		if (point == null || polygon.Count < 3) return false;
		bool inside = false;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			var a = polygon[i]; var b = polygon[j];
			bool crosses = (a.Z > point.Z) != (b.Z > point.Z) &&
			               point.X < (b.X - a.X) * (point.Z - a.Z) / (float)(b.Z - a.Z) + a.X;
			if (crosses) inside = !inside;
		}
		return inside;
	}
}

public enum PlanSourceMode { ToolingFixture, ReferenceReconstruction }

public sealed class PlanReferenceView
{
	public PlanPoint Focus { get; set; } = new();
	public float Distance { get; set; }
	public float YawDegrees { get; set; }
	public float PitchDegrees { get; set; }
	public float HeightOffset { get; set; }
	public int SourceWidth { get; set; }
	public int SourceHeight { get; set; }
}

public sealed class PlanPoint
{
	public int X { get; set; }
	public int Z { get; set; }
}

public enum PlanPlatformRole { Slab, Deck, Court, Causeway, Terrace, Trace }
public enum PlanEdgeTreatment { Revetment, PrecinctWall, Ragged, Submerged, None }
public enum PlanCutoutRole { Terrain, Collapsed }
public enum PlanSurfacePatchRole { ReclaimedEarth, BrokenPaving, RubbleField }
public enum PlanStairRole { Grand, Side, Water }
public enum PlanWallState { Standing, Broken, Stub, Trace }
public enum PlanLandmarkKind { Column, FallenColumn, Arch, Pylon, Colonnade, Emblem, Basin }
public enum PlanLandmarkState { Standing, Broken, Stump, Fallen, Toppled, Buried }

public sealed class PlanPlatform
{
	public string Id { get; set; } = "";
	public string SiteId { get; set; } = "";
	public PlanPlatformRole Role { get; set; }
	public int SurfaceY { get; set; }
	public string MaterialId { get; set; } = "";
	public PlanEdgeTreatment EdgeTreatment { get; set; }
	/// <summary>Authored density intent for broad growth on surviving natural cap.</summary>
	public float Reclamation { get; set; }
	public List<PlanPoint> Polygon { get; set; } = new();
	public List<PlanPlatformCutout> Cutouts { get; set; } = new();
	/// <summary>
	/// Authored L4 envelopes for the places where the platform has gone back to
	/// earth, lost its paving or accumulated collapse. Their coherent interiors
	/// are derived; their location and narrative role are permanent.
	/// </summary>
	public List<PlanSurfacePatch> SurfacePatches { get; set; } = new();
}

public sealed class PlanPlatformCutout
{
	public string Id { get; set; } = "";
	public PlanCutoutRole Role { get; set; }
	/// <summary>
	/// Authored vertical loss below the owning platform. Only collapsed voids use
	/// it: their floor is a compositional level, so the compiler may not roll it.
	/// </summary>
	public int Depth { get; set; }
	/// <summary>Authored density intent for deterministic growth inside the void.</summary>
	public float Reclamation { get; set; }
	public List<PlanPoint> Polygon { get; set; } = new();
}

public sealed class PlanSurfacePatch
{
	public string Id { get; set; } = "";
	public PlanSurfacePatchRole Role { get; set; }
	public string MaterialId { get; set; } = "";
	/// <summary>Authored fraction of the envelope affected by the coherent field.</summary>
	public float Coverage { get; set; }
	/// <summary>Authored visual scale of the patch; never replaced by per-block hashes.</summary>
	public int Wavelength { get; set; } = 48;
	public List<PlanPoint> Polygon { get; set; } = new();
}

public sealed class PlanStair
{
	public string Id { get; set; } = "";
	public string SiteId { get; set; } = "";
	public PlanStairRole Role { get; set; }
	public string FromPlatformId { get; set; } = "";
	public string ToPlatformId { get; set; } = "";
	public PlanPoint From { get; set; } = new();
	public PlanPoint To { get; set; } = new();
	public int Width { get; set; }
}

public sealed class PlanWall
{
	public string Id { get; set; } = "";
	public string SiteId { get; set; } = "";
	public List<PlanPoint> Points { get; set; } = new();
	public int Height { get; set; }
	public PlanWallState State { get; set; }
	public string MaterialId { get; set; } = "";
	public List<string> OpeningSocketIds { get; set; } = new();
}

public sealed class PlanRouteSocket
{
	public string Id { get; set; } = "";
	public string RouteNodeId { get; set; } = "";
	public PlanPoint Point { get; set; } = new();
	public string PlatformId { get; set; } = "";
}

public sealed class PlanLandmark
{
	public string Id { get; set; } = "";
	public string SiteId { get; set; } = "";
	public string PlatformId { get; set; } = "";
	public PlanLandmarkKind Kind { get; set; }
	public PlanLandmarkState State { get; set; }
	public PlanPoint Point { get; set; } = new();
	public float OrientationDegrees { get; set; }
	public int Height { get; set; }
	public int Span { get; set; }
	public int Length { get; set; }
	public int Count { get; set; } = 1;
}
