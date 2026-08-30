using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Petalfell.World;

/// <summary>
/// Authored Chapter topology (MAP_PIPELINE L2). These records answer identity,
/// location and connection only. Detailed platform and structure geometry lives
/// in per-site L3 plans, so that format can grow without moving any place.
/// </summary>
public sealed class CanonicalWorldDefinition
{
	public int Version { get; set; } = 1;
	/// <summary>
	/// Version 1 review maps are square and use worldSize. Version 2 production
	/// topology is rectangular and uses width/depth so its coordinates match the
	/// atlas without an implicit scaling step.
	/// </summary>
	public int WorldSize { get; set; }
	public int Width { get; set; }
	public int Depth { get; set; }
	public List<CanonicalDomain> Domains { get; set; } = new();
	public List<CanonicalSite> Sites { get; set; } = new();
	public List<CanonicalRouteNode> RouteNodes { get; set; } = new();
	public List<CanonicalRoute> Routes { get; set; } = new();

	[JsonIgnore] public int ExtentWidth => Version == 1 ? WorldSize : Width;
	[JsonIgnore] public int ExtentDepth => Version == 1 ? WorldSize : Depth;

	public static CanonicalWorldDefinition Load(string resourcePath)
	{
		using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException(
				$"Could not open canonical world '{resourcePath}': {Godot.FileAccess.GetOpenError()}");

		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		options.Converters.Add(new JsonStringEnumConverter());
		var world = JsonSerializer.Deserialize<CanonicalWorldDefinition>(file.GetAsText(), options)
		            ?? throw new InvalidOperationException($"Canonical world '{resourcePath}' was empty.");
		foreach (var domain in world.Domains)
			if (!string.IsNullOrWhiteSpace(domain.PlanPath))
				domain.Plan = DomainPlanDefinition.Load(domain.PlanPath);
		foreach (var site in world.Sites)
			if (!string.IsNullOrWhiteSpace(site.PlanPath))
				site.ReferencePlan = ReferenceSiteDefinition.Load(site.PlanPath);
		return world;
	}

	/// <summary>
	/// Audit the authored graph without generating terrain. Authoring errors are
	/// collected rather than thrown one at a time so one edit pass can fix them all.
	/// </summary>
	public WorldAuditReport Audit(MapDefinition map)
	{
		var report = new WorldAuditReport();
		if (Version != 1) report.Error($"version must be 1 for the review map, got {Version}");
		if (WorldSize != map.DefaultWorldSize)
			report.Error($"worldSize {WorldSize} does not match map defaultWorldSize {map.DefaultWorldSize}");
		AuditInto(report, map.DefaultWorldSize, map.DefaultWorldSize, null, null);
		return report;
	}

	public WorldAuditReport Audit(WorldAtlasDefinition atlas)
	{
		var report = new WorldAuditReport();
		if (Version != 2) report.Error($"version must be 2 for production atlas topology, got {Version}");
		if (Width != atlas.Width || Depth != atlas.Depth)
			report.Error($"topology extent {Width}x{Depth} does not match atlas {atlas.Width}x{atlas.Depth}");
		var regions = new HashSet<string>(atlas.Provinces.Select(p => p.Id), StringComparer.Ordinal);
		AuditInto(report, atlas.Width, atlas.Depth, regions, atlas);
		return report;
	}

	private void AuditInto(WorldAuditReport report, int extentWidth, int extentDepth,
		HashSet<string> validRegionIds, WorldAtlasDefinition atlas)
	{
		if (extentWidth <= 0 || extentDepth <= 0)
			report.Error($"topology extent must be positive, got {extentWidth}x{extentDepth}");
		if (Domains.Count == 0) report.Error("at least one domain is required");
		if (Sites.Count == 0) report.Error("at least one site is required");

		var allIds = new HashSet<string>(StringComparer.Ordinal);
		var domains = new Dictionary<string, CanonicalDomain>(StringComparer.Ordinal);
		var sites = new Dictionary<string, CanonicalSite>(StringComparer.Ordinal);
		var nodes = new Dictionary<string, CanonicalRouteNode>(StringComparer.Ordinal);

		bool Id(string id, string kind)
		{
			if (string.IsNullOrWhiteSpace(id)) { report.Error($"{kind} id is required"); return false; }
			if (!allIds.Add(id)) { report.Error($"duplicate authored id '{id}'"); return false; }
			return true;
		}

		bool Inside(BlockPoint p) => p != null && p.X >= 0 && p.Z >= 0 && p.X < extentWidth && p.Z < extentDepth;

		foreach (var domain in Domains)
		{
			if (Id(domain.Id, "domain")) domains[domain.Id] = domain;
			if (string.IsNullOrWhiteSpace(domain.DisplayName)) report.Error($"domain '{domain.Id}' displayName is required");
			if (string.IsNullOrWhiteSpace(domain.RegionId)) report.Error($"domain '{domain.Id}' regionId is required");
			else if (validRegionIds != null && !validRegionIds.Contains(domain.RegionId))
				report.Error($"domain '{domain.Id}' references missing atlas region '{domain.RegionId}'");
			if (string.IsNullOrWhiteSpace(domain.CultureId)) report.Error($"domain '{domain.Id}' cultureId is required");
			if (domain.Boundary.Count < 3) report.Error($"domain '{domain.Id}' boundary needs at least three points");
			foreach (var p in domain.Boundary)
				if (!Inside(p)) report.Error($"domain '{domain.Id}' boundary point lies outside the world");
			if (atlas != null)
			{
				if (string.IsNullOrWhiteSpace(domain.PlanPath))
					report.Warning($"domain '{domain.Id}' has no L3 planPath");
				else if (domain.Plan == null)
					report.Error($"domain '{domain.Id}' plan '{domain.PlanPath}' did not load");
				else
					report.Include(domain.Plan.Audit(atlas, this, domain), $"domain plan '{domain.Id}'");
			}
		}

		foreach (var site in Sites)
		{
			if (Id(site.Id, "site")) sites[site.Id] = site;
			if (string.IsNullOrWhiteSpace(site.DisplayName)) report.Error($"site '{site.Id}' displayName is required");
			if (!Inside(site.Centre)) report.Error($"site '{site.Id}' centre lies outside the world");
			if (site.ExtentX <= 0 || site.ExtentZ <= 0) report.Error($"site '{site.Id}' extents must be positive");
			else if (site.Centre != null &&
			         (site.Centre.X - site.ExtentX / 2f < 0 || site.Centre.X + site.ExtentX / 2f >= extentWidth ||
			          site.Centre.Z - site.ExtentZ / 2f < 0 || site.Centre.Z + site.ExtentZ / 2f >= extentDepth))
				report.Error($"site '{site.Id}' envelope lies outside the world");
			if (site.Age < 0f || site.Age > 1f) report.Error($"site '{site.Id}' age must be in 0..1");
			if (!domains.ContainsKey(site.DomainId)) report.Error($"site '{site.Id}' references missing domain '{site.DomainId}'");
			else if (site.Centre != null && !PointInPolygon(site.Centre, domains[site.DomainId].Boundary))
				report.Error($"site '{site.Id}' centre lies outside domain '{site.DomainId}'");

			var entrances = new HashSet<string>(StringComparer.Ordinal);
			foreach (var entrance in site.Entrances)
			{
				if (string.IsNullOrWhiteSpace(entrance.Id)) report.Error($"site '{site.Id}' has an entrance without an id");
				else if (!entrances.Add(entrance.Id)) report.Error($"site '{site.Id}' repeats entrance '{entrance.Id}'");
				if (string.IsNullOrWhiteSpace(entrance.RouteNodeId))
					report.Error($"site '{site.Id}' entrance '{entrance.Id}' needs a routeNodeId");
			}
			if (site.RequiresRoute && site.Entrances.Count == 0)
				report.Error($"site '{site.Id}' requires a route but has no entrances");
			if (site.Status != SiteBuildStatus.Planned && string.IsNullOrWhiteSpace(site.PlanPath))
				report.Error($"site '{site.Id}' status {site.Status} requires a planPath");
			if (!string.IsNullOrWhiteSpace(site.PlanPath) && !Godot.FileAccess.FileExists(site.PlanPath))
				report.Error($"site '{site.Id}' planPath '{site.PlanPath}' does not exist");
			else if (atlas != null && !string.IsNullOrWhiteSpace(site.PlanPath))
			{
				if (site.ReferencePlan == null)
					report.Error($"site '{site.Id}' reference plan '{site.PlanPath}' did not load");
				else
					report.Include(site.ReferencePlan.Audit(atlas, site), $"reference site '{site.Id}'");
			}
		}

		foreach (var node in RouteNodes)
		{
			if (Id(node.Id, "route node")) nodes[node.Id] = node;
			if (!Inside(node.Point)) report.Error($"route node '{node.Id}' lies outside the world");
			if (string.IsNullOrWhiteSpace(node.SiteId) != string.IsNullOrWhiteSpace(node.EntranceId))
				report.Error($"route node '{node.Id}' must name both siteId and entranceId, or neither");
			if (!string.IsNullOrWhiteSpace(node.SiteId))
			{
				if (!sites.TryGetValue(node.SiteId, out var site))
					report.Error($"route node '{node.Id}' references missing site '{node.SiteId}'");
				else
				{
					var entrance = site.Entrances.FirstOrDefault(e => e.Id == node.EntranceId);
					if (entrance == null) report.Error($"route node '{node.Id}' references missing entrance '{node.EntranceId}' on site '{node.SiteId}'");
					else if (entrance.RouteNodeId != node.Id) report.Error($"site '{node.SiteId}' entrance '{node.EntranceId}' points to '{entrance.RouteNodeId}', not '{node.Id}'");
				}
			}
		}

		var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (var id in nodes.Keys) edges[id] = new List<string>();
		foreach (var route in Routes)
		{
			Id(route.Id, "route");
			if (!nodes.TryGetValue(route.FromNodeId, out var from)) report.Error($"route '{route.Id}' references missing fromNodeId '{route.FromNodeId}'");
			if (!nodes.TryGetValue(route.ToNodeId, out var to)) report.Error($"route '{route.Id}' references missing toNodeId '{route.ToNodeId}'");
			if (route.FromNodeId == route.ToNodeId) report.Error($"route '{route.Id}' connects a node to itself");
			if (route.Width <= 0f) report.Error($"route '{route.Id}' width must be positive");
			if (route.Points.Count < 2) report.Error($"route '{route.Id}' needs at least two points");
			foreach (var p in route.Points)
				if (!Inside(p)) report.Error($"route '{route.Id}' has a point outside the world");
			if (from != null && route.Points.Count > 0 && !route.Points[0].Same(from.Point))
				report.Error($"route '{route.Id}' first point must equal node '{from.Id}'");
			if (to != null && route.Points.Count > 0 && !route.Points[^1].Same(to.Point))
				report.Error($"route '{route.Id}' last point must equal node '{to.Id}'");
			if (from != null && to != null)
			{
				edges[from.Id].Add(to.Id);
				edges[to.Id].Add(from.Id);
			}
		}

		foreach (var site in Sites)
		{
			foreach (var entrance in site.Entrances)
				if (!nodes.ContainsKey(entrance.RouteNodeId))
					report.Error($"site '{site.Id}' entrance '{entrance.Id}' references missing route node '{entrance.RouteNodeId}'");
			foreach (string target in site.SightlineSiteIds)
				if (!sites.ContainsKey(target)) report.Error($"site '{site.Id}' references missing sightline site '{target}'");
		}

		var requiredNodes = Sites.Where(s => s.RequiresRoute)
			.SelectMany(s => s.Entrances).Select(e => e.RouteNodeId)
			.Where(nodes.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
		if (requiredNodes.Count > 0)
		{
			var reached = new HashSet<string>(StringComparer.Ordinal) { requiredNodes[0] };
			var queue = new Queue<string>(); queue.Enqueue(requiredNodes[0]);
			while (queue.Count > 0)
			{
				string at = queue.Dequeue();
				foreach (string next in edges[at]) if (reached.Add(next)) queue.Enqueue(next);
			}
			foreach (string id in requiredNodes)
				if (!reached.Contains(id)) report.Error($"required site route node '{id}' is disconnected from the authored graph");
		}

		foreach (var node in RouteNodes)
			if (edges.TryGetValue(node.Id, out var links) && links.Count == 0)
				report.Warning($"route node '{node.Id}' is unused");
		if (Sites.Count < 30)
			report.Warning($"topology is incomplete: {Sites.Count} sites authored, Chapter 1 target is 30–60");
	}

	private static bool PointInPolygon(BlockPoint p, List<BlockPoint> polygon)
	{
		if (p == null || polygon.Count < 3) return false;
		bool inside = false;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			var a = polygon[i]; var b = polygon[j];
			bool crosses = (a.Z > p.Z) != (b.Z > p.Z) &&
			               p.X < (b.X - a.X) * (p.Z - a.Z) / (float)(b.Z - a.Z) + a.X;
			if (crosses) inside = !inside;
		}
		return inside;
	}
}

public sealed class WorldAuditReport
{
	public readonly List<string> Errors = new();
	public readonly List<string> Warnings = new();
	public bool Valid => Errors.Count == 0;
	public void Error(string message) => Errors.Add(message);
	public void Warning(string message) => Warnings.Add(message);

	public void Include(WorldAuditReport child, string scope)
	{
		foreach (string error in child.Errors) Error($"{scope}: {error}");
		foreach (string warning in child.Warnings) Warning($"{scope}: {warning}");
	}

	public string Format(string source)
	{
		var text = new StringBuilder();
		text.Append(Valid ? "Canonical world audit passed" : "Canonical world audit failed")
			.Append(" for '").Append(source).Append("'.");
		foreach (string error in Errors) text.Append("\n  ERROR: ").Append(error);
		foreach (string warning in Warnings) text.Append("\n  WARNING: ").Append(warning);
		return text.ToString();
	}
}

public sealed class BlockPoint
{
	public int X { get; set; }
	public int Z { get; set; }
	public bool Same(BlockPoint other) => other != null && X == other.X && Z == other.Z;
}

public sealed class CanonicalDomain
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string RegionId { get; set; } = "";
	public string CultureId { get; set; } = "";
	/// <summary>Clockwise from world +Z; site plans use the same convention.</summary>
	public float AxisDegrees { get; set; }
	public List<BlockPoint> Boundary { get; set; } = new();
	public string PlanPath { get; set; } = "";
	[JsonIgnore] public DomainPlanDefinition Plan { get; internal set; }
}

public enum SiteTier { Mark, Precinct, District, GreatWork }
public enum SiteArchetype { Monumental, Drowned, Causeway, Trace, Quarry, Shrine, Holdout, Junction, Unexplained }
public enum SiteBuildStatus { Planned, Blockout, Production, Accepted }

public sealed class CanonicalSite
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string DomainId { get; set; } = "";
	public SiteTier Tier { get; set; }
	public SiteArchetype Archetype { get; set; }
	public SiteBuildStatus Status { get; set; }
	public BlockPoint Centre { get; set; } = new();
	public int ExtentX { get; set; }
	public int ExtentZ { get; set; }
	public float OrientationDegrees { get; set; }
	public float Age { get; set; }
	public bool RequiresRoute { get; set; } = true;
	public string PlanPath { get; set; } = "";
	[JsonIgnore] public ReferenceSiteDefinition ReferencePlan { get; internal set; }
	public List<CanonicalSiteEntrance> Entrances { get; set; } = new();
	public List<string> SightlineSiteIds { get; set; } = new();
}

public sealed class CanonicalSiteEntrance
{
	public string Id { get; set; } = "";
	public string RouteNodeId { get; set; } = "";
}

public sealed class CanonicalRouteNode
{
	public string Id { get; set; } = "";
	public BlockPoint Point { get; set; } = new();
	public string SiteId { get; set; } = "";
	public string EntranceId { get; set; } = "";
}

public sealed class CanonicalRoute
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public RoadKind Kind { get; set; }
	public string FromNodeId { get; set; } = "";
	public string ToNodeId { get; set; } = "";
	public float Width { get; set; } = 4f;
	public string Construction { get; set; } = "";
	public List<BlockPoint> Points { get; set; } = new();
}
