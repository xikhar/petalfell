using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Petalfell.World;

/// <summary>
/// Registration and locked-review metadata for one literal reference
/// reconstruction. The architecture is deliberately not expressed as shared
/// parts here: the matching site-owned builder is the voxel blueprint.
/// </summary>
public sealed class ReferenceSiteDefinition
{
	public int Version { get; set; } = 1;
	public string SiteId { get; set; } = "";
	public string BuilderId { get; set; } = "";
	public string ReferencePath { get; set; } = "";
	public string GroundPlanPath { get; set; } = "";
	public BlockPoint Origin { get; set; } = new();
	public float AxisDegrees { get; set; }
	public PlanPoint FootprintMin { get; set; } = new();
	public PlanPoint FootprintMax { get; set; } = new();
	public PlanPoint PlayerSpawn { get; set; } = new();
	public PlanReferenceView ReferenceView { get; set; } = new();

	public static ReferenceSiteDefinition Load(string resourcePath)
	{
		using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException(
				$"Could not open reference site '{resourcePath}': {Godot.FileAccess.GetOpenError()}");
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		options.Converters.Add(new JsonStringEnumConverter());
		return JsonSerializer.Deserialize<ReferenceSiteDefinition>(file.GetAsText(), options) ??
		       throw new InvalidOperationException($"Reference site '{resourcePath}' was empty.");
	}

	public WorldAuditReport Audit(WorldAtlasDefinition atlas, CanonicalSite site)
	{
		var report = new WorldAuditReport();
		if (Version != 1) report.Error($"version must be 1, got {Version}");
		if (SiteId != site.Id) report.Error($"siteId '{SiteId}' does not match '{site.Id}'");
		if (string.IsNullOrWhiteSpace(BuilderId)) report.Error("builderId is required");
		if (!Godot.FileAccess.FileExists(ReferencePath))
			report.Error($"reference '{ReferencePath}' does not exist");
		if (string.IsNullOrWhiteSpace(GroundPlanPath) ||
		    !Godot.FileAccess.FileExists(GroundPlanPath))
			report.Error($"ground plan '{GroundPlanPath}' does not exist");
		if (Origin == null || Origin.X < 0 || Origin.Z < 0 ||
		    Origin.X >= atlas.Width || Origin.Z >= atlas.Depth)
			report.Error("origin lies outside the atlas");
		else if (!Origin.Same(site.Centre))
			report.Error($"origin {Origin.X},{Origin.Z} does not match site centre {site.Centre.X},{site.Centre.Z}");
		if (MathF.Abs(NormalizeAngle(AxisDegrees - site.OrientationDegrees)) > .01f)
			report.Error("axisDegrees does not match the canonical site orientation");
		if (FootprintMin == null || FootprintMax == null ||
		    FootprintMin.X >= FootprintMax.X || FootprintMin.Z >= FootprintMax.Z)
			report.Error("footprint bounds are invalid");
		else if (FootprintMin.X < -site.ExtentX / 2 || FootprintMax.X > site.ExtentX / 2 ||
		         FootprintMin.Z < -site.ExtentZ / 2 || FootprintMax.Z > site.ExtentZ / 2)
			report.Error("footprint leaves the canonical site envelope");
		if (ReferenceView == null || ReferenceView.Distance <= 0f ||
		    ReferenceView.PitchDegrees <= 0f || ReferenceView.PitchDegrees >= 89f ||
		    ReferenceView.SourceWidth <= 0 || ReferenceView.SourceHeight <= 0)
			report.Error("locked referenceView is incomplete");
		if (!ContainsLocal(PlayerSpawn)) report.Error("playerSpawn lies outside the footprint");
		if (!ContainsLocal(ReferenceView?.Focus)) report.Error("referenceView focus lies outside the footprint");
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

	public bool ContainsGlobal(int x, int z)
	{
		float radians = AxisDegrees * MathF.PI / 180f;
		float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
		float dx = x - Origin.X, dz = z - Origin.Z;
		float localX = dx * cos - dz * sin;
		float localZ = dx * sin + dz * cos;
		return localX >= FootprintMin.X && localX <= FootprintMax.X &&
		       localZ >= FootprintMin.Z && localZ <= FootprintMax.Z;
	}

	private bool ContainsLocal(PlanPoint point) => point != null &&
		point.X >= FootprintMin.X && point.X <= FootprintMax.X &&
		point.Z >= FootprintMin.Z && point.Z <= FootprintMax.Z;

	private static float NormalizeAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}
}
