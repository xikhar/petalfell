using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Petalfell.Core;

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
	/// <summary>
	/// Exact integer runtime voxels per measured source-plan cell. Scale one is
	/// the direct transcription used by Reference 10; Reference 1 deliberately
	/// uses three so its player-relative monument hierarchy matches the source
	/// while retaining the measured plan as the coordinate authority.
	/// </summary>
	public int RuntimePlanScale { get; set; } = 1;
	/// <summary>
	/// Optional tree-only breathing room outside the strict reconstruction footprint.
	/// The site still owns no wilderness placement: this only thins otherwise valid
	/// biome candidates and eases back to ordinary density at the stated radius.
	/// </summary>
	public int SurroundingTreeThinningRadius { get; set; }
	public float SurroundingTreeDensity { get; set; } = 1f;
	/// <summary>
	/// Optional absolute Top-Y datum for a source plan whose measured vertical
	/// courses are stored relative to one architectural threshold. Keeping it in
	/// the authored registration prevents a reconstruction from moving when the
	/// ordinary atlas compiler changes the terrain below its footprint.
	/// </summary>
	public int? VerticalDatumY { get; set; }
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
		if (RuntimePlanScale < 1 || RuntimePlanScale > 7 || RuntimePlanScale % 2 == 0)
			report.Error($"runtimePlanScale {RuntimePlanScale} must be an odd integer from 1 through 7");
		if (SurroundingTreeThinningRadius < 0 || SurroundingTreeThinningRadius > 512)
			report.Error($"surroundingTreeThinningRadius {SurroundingTreeThinningRadius} must be in 0-512");
		if (!float.IsFinite(SurroundingTreeDensity) || SurroundingTreeDensity < 0f ||
		    SurroundingTreeDensity > 1f)
			report.Error($"surroundingTreeDensity {SurroundingTreeDensity} must be finite and in 0-1");
		if (VerticalDatumY is int datum && (datum < 1 || datum >= atlas.Height))
			report.Error($"verticalDatumY {datum} lies outside the atlas height");
		if (FootprintMin == null || FootprintMax == null ||
		    FootprintMin.X >= FootprintMax.X || FootprintMin.Z >= FootprintMax.Z)
			report.Error("footprint bounds are invalid");
		else if (RuntimeFootprintMin.X < -site.ExtentX / 2 ||
		         RuntimeFootprintMax.X > site.ExtentX / 2 ||
		         RuntimeFootprintMin.Z < -site.ExtentZ / 2 ||
		         RuntimeFootprintMax.Z > site.ExtentZ / 2)
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
		if (local == null) throw new ArgumentNullException(nameof(local));
		return ToGlobalRuntime(new PlanPoint
		{
			X = local.X * RuntimePlanScale,
			Z = local.Z * RuntimePlanScale,
		});
	}

	/// <summary>Rotate an already-scaled runtime-local voxel coordinate.</summary>
	public BlockPoint ToGlobalRuntime(PlanPoint local)
	{
		if (local == null) throw new ArgumentNullException(nameof(local));
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
		(float localX, float localZ) = ToLocalRuntime(x, z);
		return localX >= RuntimeFootprintMin.X && localX <= RuntimeFootprintMax.X &&
		       localZ >= RuntimeFootprintMin.Z && localZ <= RuntimeFootprintMax.Z;
	}

	/// <summary>
	/// Tree density at an atlas point outside this site's exact footprint. A
	/// smooth rectangular-distance falloff avoids replacing the site's authored
	/// boundary with a second visible vegetation boundary.
	/// </summary>
	public float SurroundingTreeDensityAtGlobal(int x, int z)
	{
		if (SurroundingTreeThinningRadius <= 0 || SurroundingTreeDensity >= 1f)
			return 1f;
		(float localX, float localZ) = ToLocalRuntime(x, z);
		PlanPoint min = RuntimeFootprintMin, max = RuntimeFootprintMax;
		float dx = MathF.Max(MathF.Max(min.X - localX, 0f), localX - max.X);
		float dz = MathF.Max(MathF.Max(min.Z - localZ, 0f), localZ - max.Z);
		float distance = MathF.Sqrt(dx * dx + dz * dz);
		if (distance >= SurroundingTreeThinningRadius) return 1f;
		return Rng.Lerp(SurroundingTreeDensity, 1f,
			Rng.Smoothstep(0f, SurroundingTreeThinningRadius, distance));
	}

	private (float x, float z) ToLocalRuntime(int globalX, int globalZ)
	{
		float radians = AxisDegrees * MathF.PI / 180f;
		float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
		float dx = globalX - Origin.X, dz = globalZ - Origin.Z;
		return (dx * cos - dz * sin, dx * sin + dz * cos);
	}

	[JsonIgnore]
	public PlanPoint RuntimeFootprintMin
	{
		get
		{
			int half = (RuntimePlanScale - 1) / 2;
			return new PlanPoint
			{
				X = FootprintMin.X * RuntimePlanScale - half,
				Z = FootprintMin.Z * RuntimePlanScale - half,
			};
		}
	}

	[JsonIgnore]
	public PlanPoint RuntimeFootprintMax
	{
		get
		{
			int half = (RuntimePlanScale - 1) / 2;
			return new PlanPoint
			{
				X = FootprintMax.X * RuntimePlanScale + half,
				Z = FootprintMax.Z * RuntimePlanScale + half,
			};
		}
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
