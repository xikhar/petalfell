using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Petalfell.World;

/// <summary>
/// Authored production-scale L0/L1 contract. Loading this file must remain cheap:
/// it describes the atlas and its registered sources but never allocates terrain.
/// </summary>
public sealed class WorldAtlasDefinition
{
	public int Version { get; set; } = 1;
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public int Width { get; set; }
	public int Depth { get; set; }
	public int Height { get; set; }
	public int SeaLevel { get; set; }
	public int SectorSize { get; set; }
	public int ChunkSize { get; set; }
	public int BlocksPerPixel { get; set; }
	public string BiomeCatalogPath { get; set; } = "";
	/// <summary>
	/// Registered L2 companion source. The manifest points to it so an atlas audit
	/// can prove that physical geography and authored topology share one extent;
	/// the topology itself remains a separate authored file.
	/// </summary>
	public string TopologyPath { get; set; } = "";
	public string PreviewReferencePath { get; set; } = "";
	public List<string> CompositionReferencePaths { get; set; } = new();
	public List<AtlasSourceLayer> SourceLayers { get; set; } = new();
	public List<AtlasProvince> Provinces { get; set; } = new();

	[JsonIgnore] public BiomeCatalogDefinition BiomeCatalog { get; private set; }
	[JsonIgnore] public CanonicalWorldDefinition Topology { get; private set; }

	public static WorldAtlasDefinition Load(string resourcePath)
	{
		var atlas = ReadJson<WorldAtlasDefinition>(resourcePath, "world atlas");
		if (!string.IsNullOrWhiteSpace(atlas.BiomeCatalogPath))
			atlas.BiomeCatalog = BiomeCatalogDefinition.Load(atlas.BiomeCatalogPath);
		if (!string.IsNullOrWhiteSpace(atlas.TopologyPath))
			atlas.Topology = CanonicalWorldDefinition.Load(atlas.TopologyPath);
		return atlas;
	}

	public AtlasAuditReport Audit()
	{
		var report = new AtlasAuditReport();
		if (Version != 1) report.Error($"version must be 1, got {Version}");
		if (string.IsNullOrWhiteSpace(Id)) report.Error("id is required");
		if (string.IsNullOrWhiteSpace(DisplayName)) report.Error("displayName is required");
		if (Width <= 0 || Depth <= 0 || Height <= 0) report.Error("width, depth and height must be positive");
		if (SeaLevel <= 0 || SeaLevel >= Height)
			report.Error($"seaLevel {SeaLevel} must be above zero and below height {Height}");
		if (SectorSize <= 0 || ChunkSize <= 0) report.Error("sectorSize and chunkSize must be positive");
		else if (SectorSize % ChunkSize != 0) report.Error($"sectorSize {SectorSize} is not divisible by chunkSize {ChunkSize}");
		if (SectorSize > 0 && (Width % SectorSize != 0 || Depth % SectorSize != 0))
			report.Error($"atlas {Width}x{Depth} is not an exact grid of {SectorSize}-block sectors");
		if (BlocksPerPixel <= 0 || Width % BlocksPerPixel != 0 || Depth % BlocksPerPixel != 0)
			report.Error($"blocksPerPixel {BlocksPerPixel} must divide both atlas dimensions");

		if (string.IsNullOrWhiteSpace(BiomeCatalogPath)) report.Error("biomeCatalogPath is required");
		else if (BiomeCatalog == null) report.Error($"biome catalog '{BiomeCatalogPath}' did not load");
		else report.Include(BiomeCatalog.Audit(), "biomes");

		if (string.IsNullOrWhiteSpace(TopologyPath)) report.Error("topologyPath is required");
		else if (Topology == null) report.Error($"topology '{TopologyPath}' did not load");
		else report.Include(Topology.Audit(this), "topology");

		if (string.IsNullOrWhiteSpace(PreviewReferencePath)) report.Error("previewReferencePath is required");
		else if (!Godot.FileAccess.FileExists(PreviewReferencePath))
			report.Error($"preview reference '{PreviewReferencePath}' does not exist");
		foreach (string path in CompositionReferencePaths)
			if (!Godot.FileAccess.FileExists(path)) report.Error($"composition reference '{path}' does not exist");

		var layerIds = new HashSet<string>(StringComparer.Ordinal);
		var layerKinds = new HashSet<AtlasLayerKind>();
		var loadedLayers = new Dictionary<AtlasLayerKind, Image>();
		foreach (var layer in SourceLayers)
		{
			if (string.IsNullOrWhiteSpace(layer.Id)) report.Error("source layer id is required");
			else if (!layerIds.Add(layer.Id)) report.Error($"duplicate source layer id '{layer.Id}'");
			if (!layerKinds.Add(layer.Kind)) report.Error($"duplicate source layer kind '{layer.Kind}'");
			if (string.IsNullOrWhiteSpace(layer.Path)) report.Error($"source layer '{layer.Id}' path is required");
			if (layer.BlocksPerPixel != BlocksPerPixel)
				report.Error($"source layer '{layer.Id}' uses {layer.BlocksPerPixel} blocks/pixel, expected {BlocksPerPixel}");
			AtlasLayerFormat expected = layer.Kind switch
			{
				AtlasLayerKind.Elevation => AtlasLayerFormat.Gray16Png,
				AtlasLayerKind.Region or AtlasLayerKind.Culture => AtlasLayerFormat.IndexedRgbPng,
				_ => AtlasLayerFormat.Gray8Png,
			};
			if (layer.Format != expected)
				report.Error($"source layer '{layer.Id}' format {layer.Format} does not match required {expected}");
			if (layer.Status != AtlasLayerStatus.Planned && !Godot.FileAccess.FileExists(layer.Path))
				report.Error($"{layer.Status.ToString().ToLowerInvariant()} source layer '{layer.Id}' does not exist at '{layer.Path}'");
			else if (layer.Status != AtlasLayerStatus.Planned)
			{
				var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(layer.Path));
				int expectedW = Width / BlocksPerPixel, expectedH = Depth / BlocksPerPixel;
				if (image == null || image.GetWidth() != expectedW || image.GetHeight() != expectedH)
					report.Error($"source layer '{layer.Id}' must be {expectedW}x{expectedH} pixels");
				else if (!ValidPngEncoding(layer, out string formatError))
					report.Error($"source layer '{layer.Id}' {formatError}");
				else
					loadedLayers[layer.Kind] = image;
			}
			if (layer.Status == AtlasLayerStatus.Planned)
				report.Warning($"source layer '{layer.Id}' is planned, not authored");
			else if (layer.Status == AtlasLayerStatus.Blockout)
				report.Warning($"source layer '{layer.Id}' is a review blockout, not accepted canon");
		}
		foreach (AtlasLayerKind kind in Enum.GetValues<AtlasLayerKind>())
			if (!layerKinds.Contains(kind)) report.Error($"required source layer kind '{kind}' is missing");
		if (loadedLayers.TryGetValue(AtlasLayerKind.Region, out var regionImage))
		{
			var allowed = new HashSet<int> { 0 };
			foreach (var province in Provinces)
				if (ValidHtmlColour(province.PreviewColour)) allowed.Add(HtmlColourKey(province.PreviewColour));
			var unexpected = new HashSet<int>();
			for (int z = 0; z < regionImage.GetHeight(); z++)
			for (int x = 0; x < regionImage.GetWidth(); x++)
			{
				int key = ColourKey(regionImage.GetPixel(x, z));
				if (!allowed.Contains(key)) unexpected.Add(key);
			}
			if (unexpected.Count > 0)
				report.Error($"region source contains {unexpected.Count} colours outside black plus the province preview palette");
		}
		if (loadedLayers.TryGetValue(AtlasLayerKind.Land, out var landImage) &&
		    loadedLayers.TryGetValue(AtlasLayerKind.Water, out var waterImage))
		{
			int dryOceanPixels = 0;
			for (int z = 0; z < landImage.GetHeight(); z++)
			for (int x = 0; x < landImage.GetWidth(); x++)
				if (landImage.GetPixel(x, z).R < 0.5f && waterImage.GetPixel(x, z).R < 0.999f)
					dryOceanPixels++;
			if (dryOceanPixels > 0)
				report.Error($"water source leaves {dryOceanPixels} ocean pixels below the permanent-water value");
		}
		if (loadedLayers.TryGetValue(AtlasLayerKind.Land, out landImage) &&
		    loadedLayers.TryGetValue(AtlasLayerKind.Region, out regionImage))
		{
			int unassignedLandPixels = 0, assignedOceanPixels = 0;
			for (int z = 0; z < landImage.GetHeight(); z++)
			for (int x = 0; x < landImage.GetWidth(); x++)
			{
				bool land = landImage.GetPixel(x, z).R >= 0.5f;
				bool assigned = ColourKey(regionImage.GetPixel(x, z)) != 0;
				if (land && !assigned) unassignedLandPixels++;
				else if (!land && assigned) assignedOceanPixels++;
			}
			if (unassignedLandPixels > 0)
				report.Error($"region source leaves {unassignedLandPixels} land pixels unassigned");
			if (assignedOceanPixels > 0)
				report.Error($"region source assigns {assignedOceanPixels} ocean pixels to a province");
		}

		var profiles = new HashSet<string>(BiomeCatalog?.Profiles.Select(p => p.Id) ?? Enumerable.Empty<string>(),
			StringComparer.Ordinal);
		var provinceIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var province in Provinces)
		{
			if (string.IsNullOrWhiteSpace(province.Id)) report.Error("province id is required");
			else if (!provinceIds.Add(province.Id)) report.Error($"duplicate province id '{province.Id}'");
			if (string.IsNullOrWhiteSpace(province.DisplayName)) report.Error($"province '{province.Id}' displayName is required");
			if (province.TransitionBlocks < 0) report.Error($"province '{province.Id}' transitionBlocks cannot be negative");
			if (province.Boundary.Count < 3) report.Error($"province '{province.Id}' boundary needs at least three points");
			foreach (var point in province.Boundary)
				if (point == null || point.X < 0 || point.Z < 0 || point.X >= Width || point.Z >= Depth)
					report.Error($"province '{province.Id}' boundary point lies outside the atlas");
			if (province.BiomeProfileIds.Count == 0) report.Error($"province '{province.Id}' needs at least one biome profile");
			foreach (string profile in province.BiomeProfileIds)
				if (!profiles.Contains(profile)) report.Error($"province '{province.Id}' references missing biome profile '{profile}'");
			if (!ValidHtmlColour(province.PreviewColour))
				report.Error($"province '{province.Id}' previewColour '{province.PreviewColour}' is invalid");
		}
		if (Provinces.Count == 0) report.Error("at least one province is required");
		return report;
	}

	private static bool ValidHtmlColour(string value) => value != null && value.Length == 7 &&
		value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

	private static int HtmlColourKey(string value) =>
		(Convert.ToByte(value.Substring(1, 2), 16) << 16) |
		(Convert.ToByte(value.Substring(3, 2), 16) << 8) |
		Convert.ToByte(value.Substring(5, 2), 16);

	private static int ColourKey(Color colour)
	{
		int r = Math.Clamp((int)MathF.Round(colour.R * 255f), 0, 255);
		int g = Math.Clamp((int)MathF.Round(colour.G * 255f), 0, 255);
		int b = Math.Clamp((int)MathF.Round(colour.B * 255f), 0, 255);
		return (r << 16) | (g << 8) | b;
	}

	private static bool ValidPngEncoding(AtlasSourceLayer layer, out string error)
	{
		error = "";
		byte[] header = new byte[26];
		using (var stream = File.OpenRead(ProjectSettings.GlobalizePath(layer.Path)))
			if (stream.Read(header, 0, header.Length) != header.Length)
			{
				error = "is not a complete PNG";
				return false;
			}
		ReadOnlySpan<byte> pngSignature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
		if (!header.AsSpan(0, 8).SequenceEqual(pngSignature) || Encoding.ASCII.GetString(header, 12, 4) != "IHDR")
		{
			error = "is not a PNG with an IHDR header";
			return false;
		}
		int bitDepth = header[24], colourType = header[25];
		bool valid = layer.Format switch
		{
			AtlasLayerFormat.Gray8Png => bitDepth == 8 && colourType == 0,
			AtlasLayerFormat.Gray16Png => bitDepth == 16 && colourType == 0,
			AtlasLayerFormat.IndexedRgbPng => colourType == 3 || (bitDepth == 8 && colourType == 2),
			_ => false,
		};
		if (!valid)
			error = $"PNG encoding is bit depth {bitDepth}, colour type {colourType}; expected {layer.Format}";
		return valid;
	}

	internal static T ReadJson<T>(string resourcePath, string kind)
	{
		using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException($"Could not open {kind} '{resourcePath}': {Godot.FileAccess.GetOpenError()}");
		var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		options.Converters.Add(new JsonStringEnumConverter());
		return JsonSerializer.Deserialize<T>(file.GetAsText(), options)
		       ?? throw new InvalidOperationException($"{kind} '{resourcePath}' was empty");
	}
}

public enum AtlasLayerKind { Land, Elevation, Water, Region, Culture, Abandonment, Wilderness }
public enum AtlasLayerFormat { Gray8Png, Gray16Png, IndexedRgbPng }
public enum AtlasLayerStatus { Planned, Blockout, Accepted }

public sealed class AtlasSourceLayer
{
	public string Id { get; set; } = "";
	public AtlasLayerKind Kind { get; set; }
	public AtlasLayerFormat Format { get; set; }
	public AtlasLayerStatus Status { get; set; }
	public string Path { get; set; } = "";
	public int BlocksPerPixel { get; set; }
}

public sealed class AtlasProvince
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string PreviewColour { get; set; } = "#ffffff";
	public int TransitionBlocks { get; set; }
	public List<string> BiomeProfileIds { get; set; } = new();
	public List<BlockPoint> Boundary { get; set; } = new();
}

public sealed class BiomeCatalogDefinition
{
	public int Version { get; set; } = 1;
	public List<AtlasVegetationSet> VegetationSets { get; set; } = new();
	public List<AtlasBoulderSet> BoulderSets { get; set; } = new();
	public List<BiomeBuildProfile> Profiles { get; set; } = new();

	public static BiomeCatalogDefinition Load(string resourcePath) =>
		WorldAtlasDefinition.ReadJson<BiomeCatalogDefinition>(resourcePath, "biome catalog");

	public AtlasAuditReport Audit()
	{
		var report = new AtlasAuditReport();
		if (Version != 1) report.Error($"version must be 1, got {Version}");
		var vegetationIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (AtlasVegetationSet set in VegetationSets)
		{
			if (string.IsNullOrWhiteSpace(set.Id)) report.Error("vegetation set id is required");
			else if (!vegetationIds.Add(set.Id)) report.Error($"duplicate vegetation set id '{set.Id}'");
			if (set.CandidateSpacing < 8 || set.CandidateSpacing > 64)
				report.Error($"vegetation set '{set.Id}' candidateSpacing must be in 8-64 blocks");
			if (!float.IsFinite(set.Density) || set.Density < 0f || set.Density > 1f)
				report.Error($"vegetation set '{set.Id}' density must be finite and in 0-1");
			if (!float.IsFinite(set.GroveWavelength) ||
			    set.GroveWavelength < set.CandidateSpacing * 4f)
				report.Error($"vegetation set '{set.Id}' groveWavelength must be at least four candidate cells");
			if (!float.IsFinite(set.ScaleMin) || !float.IsFinite(set.ScaleMax) ||
			    set.ScaleMin < .1f || set.ScaleMin > set.ScaleMax || set.ScaleMax > 1f)
				report.Error($"vegetation set '{set.Id}' scale range must satisfy 0.1 <= min <= max <= 1");
			if (set.MaxSlope < 0 || set.MaxSlope > 16)
				report.Error($"vegetation set '{set.Id}' maxSlope must be in 0-16 blocks");
			if (set.MinWetness < 0 || set.MinWetness > set.MaxWetness || set.MaxWetness > 255)
				report.Error($"vegetation set '{set.Id}' wetness range must satisfy 0 <= min <= max <= 255");
			if (set.CanopyPalette.Count == 0)
				report.Error($"vegetation set '{set.Id}' needs at least one canopy palette entry");
			foreach (string palette in set.CanopyPalette)
				if (!AtlasWildernessPalette.IsCanopy(palette))
					report.Error($"vegetation set '{set.Id}' names unknown canopy palette '{palette}'");
		}

		var boulderIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (AtlasBoulderSet set in BoulderSets)
		{
			if (string.IsNullOrWhiteSpace(set.Id)) report.Error("boulder set id is required");
			else if (!boulderIds.Add(set.Id)) report.Error($"duplicate boulder set id '{set.Id}'");
			if (set.CandidateSpacing < 8 || set.CandidateSpacing > 96)
				report.Error($"boulder set '{set.Id}' candidateSpacing must be in 8-96 blocks");
			if (!float.IsFinite(set.Density) || set.Density < 0f || set.Density > 1f)
				report.Error($"boulder set '{set.Id}' density must be finite and in 0-1");
			if (!float.IsFinite(set.ClusterWavelength) ||
			    set.ClusterWavelength < set.CandidateSpacing * 4f)
				report.Error($"boulder set '{set.Id}' clusterWavelength must be at least four candidate cells");
			if (set.RadiusMin < 1 || set.RadiusMin > set.RadiusMax || set.RadiusMax > 4)
				report.Error($"boulder set '{set.Id}' radius range must satisfy 1 <= min <= max <= 4");
			if (set.HeightMin < 1 || set.HeightMin > set.HeightMax || set.HeightMax > 6)
				report.Error($"boulder set '{set.Id}' height range must satisfy 1 <= min <= max <= 6");
			if (set.MaxSlope < 0 || set.MaxSlope > 24)
				report.Error($"boulder set '{set.Id}' maxSlope must be in 0-24 blocks");
			if (set.MinWetness < 0 || set.MinWetness > set.MaxWetness || set.MaxWetness > 255)
				report.Error($"boulder set '{set.Id}' wetness range must satisfy 0 <= min <= max <= 255");
			if (set.StonePalette.Count == 0)
				report.Error($"boulder set '{set.Id}' needs at least one stone palette entry");
			foreach (string palette in set.StonePalette)
				if (!AtlasWildernessPalette.IsStone(palette))
					report.Error($"boulder set '{set.Id}' names unknown stone palette '{palette}'");
		}
		if (VegetationSets.Count == 0) report.Error("at least one vegetation set is required");
		if (BoulderSets.Count == 0) report.Error("at least one boulder set is required");
		if (Profiles.Count == 0) report.Error("at least one biome build profile is required");
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var profile in Profiles)
		{
			if (string.IsNullOrWhiteSpace(profile.Id)) report.Error("biome profile id is required");
			else if (!ids.Add(profile.Id)) report.Error($"duplicate biome profile id '{profile.Id}'");
			if (string.IsNullOrWhiteSpace(profile.DisplayName)) report.Error($"profile '{profile.Id}' displayName is required");
			if (profile.RuntimeBiomes.Count == 0) report.Error($"profile '{profile.Id}' needs at least one runtimeBiome");
			foreach (string biome in profile.RuntimeBiomes)
				if (!Enum.TryParse<Biome>(biome, out _)) report.Error($"profile '{profile.Id}' names unknown runtime biome '{biome}'");
			if (profile.TerraceStep <= 0) report.Error($"profile '{profile.Id}' terraceStep must be positive");
			if (string.IsNullOrWhiteSpace(profile.ErosionResponse)) report.Error($"profile '{profile.Id}' erosionResponse is required");
			if (profile.Relief == null) report.Error($"profile '{profile.Id}' relief is required");
			else
			{
				if (profile.Relief.CellSize < 2 || profile.Relief.CellSize > 12)
					report.Error($"profile '{profile.Id}' relief cellSize must be between 2 and 12");
				if (profile.Relief.CliffStep < profile.TerraceStep || profile.Relief.CliffStep > 16)
					report.Error($"profile '{profile.Id}' relief cliffStep must be at least terraceStep and no more than 16");
				if (!float.IsFinite(profile.Relief.SlopeStart) || !float.IsFinite(profile.Relief.SlopeFull) ||
				    profile.Relief.SlopeStart < 0f || profile.Relief.SlopeStart >= profile.Relief.SlopeFull ||
				    profile.Relief.SlopeFull > 2f)
					report.Error($"profile '{profile.Id}' relief slopeStart/slopeFull must satisfy 0 <= start < full <= 2");
				if (!float.IsFinite(profile.Relief.RidgeStart) || profile.Relief.RidgeStart < 0f || profile.Relief.RidgeStart > 1f)
					report.Error($"profile '{profile.Id}' relief ridgeStart must be between 0 and 1");
				if (!float.IsFinite(profile.Relief.RidgeStrength) || profile.Relief.RidgeStrength < 0f || profile.Relief.RidgeStrength > 32f)
					report.Error($"profile '{profile.Id}' relief ridgeStrength must be between 0 and 32 blocks");
				if (!float.IsFinite(profile.Relief.RidgeAngleDegrees) ||
				    profile.Relief.RidgeAngleDegrees < -180f ||
				    profile.Relief.RidgeAngleDegrees > 180f)
					report.Error($"profile '{profile.Id}' relief ridgeAngleDegrees must be between -180 and 180");
				float minimumWavelength = profile.Relief.CellSize * 4f;
				if (!float.IsFinite(profile.Relief.RidgeWavelength) || profile.Relief.RidgeWavelength < minimumWavelength)
					report.Error($"profile '{profile.Id}' relief ridgeWavelength must be at least four cell widths");
				if (!float.IsFinite(profile.Relief.LedgeWavelength) || profile.Relief.LedgeWavelength < minimumWavelength)
					report.Error($"profile '{profile.Id}' relief ledgeWavelength must be at least four cell widths");
				if (!float.IsFinite(profile.Relief.LedgeThreshold) || profile.Relief.LedgeThreshold < -1f || profile.Relief.LedgeThreshold > 1f)
					report.Error($"profile '{profile.Id}' relief ledgeThreshold must be between -1 and 1");
				if (profile.Relief.LedgeHeight < 0 || profile.Relief.LedgeHeight > 8)
					report.Error($"profile '{profile.Id}' relief ledgeHeight must be between 0 and 8 blocks");
				if (profile.Relief.ShoreWidth < 0 || profile.Relief.ShoreWidth > 32)
					report.Error($"profile '{profile.Id}' relief shoreWidth must be between 0 and 32 blocks");
			}
			if (profile.Selection == null) report.Error($"profile '{profile.Id}' selection is required");
			else
			{
				foreach (var (name, value) in profile.Selection.ScoreTerms())
					if (!float.IsFinite(value) || value < -4f || value > 4f)
						report.Error($"profile '{profile.Id}' selection {name} must be finite and between -4 and 4");
				if (!float.IsFinite(profile.Selection.PatchWavelength) || profile.Selection.PatchWavelength < 32f)
					report.Error($"profile '{profile.Id}' selection patchWavelength must be at least 32 blocks");
			}
			if (profile.Hydrology == null) report.Error($"profile '{profile.Id}' hydrology is required");
			else
			{
				if (profile.Hydrology.FloodplainStart < 0f || profile.Hydrology.FloodplainStart >= profile.Hydrology.BankStart)
					report.Error($"profile '{profile.Id}' floodplainStart must be at least zero and below bankStart");
				if (profile.Hydrology.BankStart >= AtlasHydrologyProfile.PermanentWaterStart)
					report.Error($"profile '{profile.Id}' bankStart must be below the permanent-water threshold");
				if (profile.Hydrology.BankRise < 0 || profile.Hydrology.FloodplainRise < profile.Hydrology.BankRise)
					report.Error($"profile '{profile.Id}' floodplainRise must be at least bankRise and both must be non-negative");
				if (profile.Hydrology.SurfaceDrop < 0 || profile.Hydrology.WaterDepth <= 0)
					report.Error($"profile '{profile.Id}' surfaceDrop must be non-negative and waterDepth positive");
				if (profile.Hydrology.PreserveCutRise < 1 || profile.Hydrology.PreserveCutRise > 64)
					report.Error($"profile '{profile.Id}' preserveCutRise must be between 1 and 64 blocks");
			}
			float previous = float.MaxValue;
			foreach (var band in profile.NoiseBands)
			{
				if (band.Wavelength <= 0 || band.Amplitude < 0) report.Error($"profile '{profile.Id}' has an invalid noise band");
				if (band.Wavelength >= previous) report.Error($"profile '{profile.Id}' noise bands must be ordered largest wavelength first");
				previous = band.Wavelength;
			}
			if (profile.NoiseBands.Count == 0) report.Error($"profile '{profile.Id}' needs at least one noise band");
			if (!profile.Surfaces.Complete) report.Error($"profile '{profile.Id}' must name cap, substrate, cliff, shore and underwater surfaces");
			foreach (var (name, value) in profile.RequiredReferences())
				if (string.IsNullOrWhiteSpace(value)) report.Error($"profile '{profile.Id}' {name} is required");
			if (!string.IsNullOrWhiteSpace(profile.VegetationSetId) &&
			    !vegetationIds.Contains(profile.VegetationSetId))
				report.Error($"profile '{profile.Id}' references missing vegetation set '{profile.VegetationSetId}'");
			if (!string.IsNullOrWhiteSpace(profile.BoulderSetId) &&
			    !boulderIds.Contains(profile.BoulderSetId))
				report.Error($"profile '{profile.Id}' references missing boulder set '{profile.BoulderSetId}'");
		}
		return report;
	}
}

public sealed class BiomeBuildProfile
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public List<string> RuntimeBiomes { get; set; } = new();
	public int TerraceStep { get; set; } = 2;
	public string ErosionResponse { get; set; } = "";
	public AtlasReliefProfile Relief { get; set; } = new();
	public AtlasBiomeSelectionProfile Selection { get; set; } = new();
	public AtlasHydrologyProfile Hydrology { get; set; } = new();
	public List<AtlasNoiseBand> NoiseBands { get; set; } = new();
	public AtlasSurfaceSet Surfaces { get; set; } = new();
	public string VegetationSetId { get; set; } = "";
	public string BoulderSetId { get; set; } = "";
	public string GroundDetailSetId { get; set; } = "";
	public string AtmosphereProfileId { get; set; } = "";
	public string ShaderProfileId { get; set; } = "";
	public string RoadProfileId { get; set; } = "";
	public string ArchitecturePaletteId { get; set; } = "";

	public IEnumerable<(string name, string value)> RequiredReferences()
	{
		yield return ("vegetationSetId", VegetationSetId);
		yield return ("boulderSetId", BoulderSetId);
		yield return ("groundDetailSetId", GroundDetailSetId);
		yield return ("atmosphereProfileId", AtmosphereProfileId);
		yield return ("shaderProfileId", ShaderProfileId);
		yield return ("roadProfileId", RoadProfileId);
		yield return ("architecturePaletteId", ArchitecturePaletteId);
	}
}

/// <summary>
/// Data-backed ordinary wilderness grammar. The globally anchored scatter pass
/// owns candidate identity; a biome set owns only suitability, density and look.
/// </summary>
public sealed class AtlasVegetationSet
{
	public string Id { get; set; } = "";
	public int CandidateSpacing { get; set; } = 16;
	public float Density { get; set; }
	public float GroveWavelength { get; set; } = 192f;
	public float ScaleMin { get; set; } = .2f;
	public float ScaleMax { get; set; } = .8f;
	public int MaxSlope { get; set; } = 2;
	public int MinWetness { get; set; }
	public int MaxWetness { get; set; } = 255;
	public List<string> CanopyPalette { get; set; } = new();
}

/// <summary>
/// Data-backed punctuation for natural surfaces. Geometry remains a derived
/// block assembly and never alters the compiled height or macro geography.
/// </summary>
public sealed class AtlasBoulderSet
{
	public string Id { get; set; } = "";
	public int CandidateSpacing { get; set; } = 22;
	public float Density { get; set; }
	public float ClusterWavelength { get; set; } = 224f;
	public int RadiusMin { get; set; } = 1;
	public int RadiusMax { get; set; } = 2;
	public int HeightMin { get; set; } = 1;
	public int HeightMax { get; set; } = 3;
	public int MaxSlope { get; set; } = 4;
	public int MinWetness { get; set; }
	public int MaxWetness { get; set; } = 255;
	public List<string> StonePalette { get; set; } = new();
}

/// <summary>
/// Sector-local geometric response to accepted elevation and hydrology. These
/// values may articulate an authored mountain, cliff or shore, but may not move
/// the macro feature or create a new one.
/// </summary>
public sealed class AtlasReliefProfile
{
	public int CellSize { get; set; } = 6;
	public int CliffStep { get; set; } = 4;
	public float SlopeStart { get; set; } = 0.25f;
	public float SlopeFull { get; set; } = 0.75f;
	public float RidgeStart { get; set; } = 0.65f;
	public float RidgeStrength { get; set; } = 0f;
	/// <summary>
	/// Atlas-space direction of the long ridge axis. This is fixed profile data:
	/// rotating noise independently at each cell changes its phase and creates
	/// disconnected cliff fragments instead of one continuous landform.
	/// </summary>
	public float RidgeAngleDegrees { get; set; }
	public float RidgeWavelength { get; set; } = 256f;
	public float LedgeWavelength { get; set; } = 96f;
	public float LedgeThreshold { get; set; } = 0.35f;
	public int LedgeHeight { get; set; } = 2;
	public int ShoreWidth { get; set; } = 8;
}

/// <summary>
/// Suitability coefficients used to choose among a province's allowed profile
/// IDs. The eventual score combines normalized authored altitude, water-derived
/// moisture, macro slope and coherent patch noise; it refines a province's
/// accepted geography and never changes province ownership.
/// </summary>
public sealed class AtlasBiomeSelectionProfile
{
	public float Bias { get; set; }
	public float AltitudeWeight { get; set; }
	public float MoistureWeight { get; set; }
	public float SlopeWeight { get; set; }
	public float PatchWeight { get; set; }
	public float PatchWavelength { get; set; } = 256f;

	public IEnumerable<(string name, float value)> ScoreTerms()
	{
		yield return ("bias", Bias);
		yield return ("altitudeWeight", AltitudeWeight);
		yield return ("moistureWeight", MoistureWeight);
		yield return ("slopeWeight", SlopeWeight);
		yield return ("patchWeight", PatchWeight);
	}
}

/// <summary>
/// Profile response to the shared authored hydrology field. The thresholds
/// choose how broadly a province expresses the same watershed; they never move
/// a river or manufacture a new one.
/// </summary>
public sealed class AtlasHydrologyProfile
{
	public const float PermanentWaterStart = 240f / 255f;
	public float FloodplainStart { get; set; } = 0.62f;
	public float BankStart { get; set; } = 0.80f;
	public int FloodplainRise { get; set; } = 4;
	public int BankRise { get; set; } = 2;
	public int SurfaceDrop { get; set; } = 1;
	public int WaterDepth { get; set; } = 3;
	public int PreserveCutRise { get; set; } = 12;
}

public sealed class AtlasNoiseBand
{
	public float Wavelength { get; set; }
	public float Amplitude { get; set; }
}

public sealed class AtlasSurfaceSet
{
	public string Cap { get; set; } = "";
	public string Substrate { get; set; } = "";
	public string Cliff { get; set; } = "";
	public string Shore { get; set; } = "";
	public string Underwater { get; set; } = "";
	[JsonIgnore] public bool Complete => !string.IsNullOrWhiteSpace(Cap) && !string.IsNullOrWhiteSpace(Substrate) &&
		!string.IsNullOrWhiteSpace(Cliff) && !string.IsNullOrWhiteSpace(Shore) && !string.IsNullOrWhiteSpace(Underwater);
}

public sealed class AtlasAuditReport
{
	public readonly List<string> Errors = new();
	public readonly List<string> Warnings = new();
	public bool Valid => Errors.Count == 0;
	public void Error(string message) => Errors.Add(message);
	public void Warning(string message) => Warnings.Add(message);

	public void Include(AtlasAuditReport child, string scope)
	{
		foreach (string error in child.Errors) Error($"{scope}: {error}");
		foreach (string warning in child.Warnings) Warning($"{scope}: {warning}");
	}

	public void Include(WorldAuditReport child, string scope)
	{
		foreach (string error in child.Errors) Error($"{scope}: {error}");
		foreach (string warning in child.Warnings) Warning($"{scope}: {warning}");
	}

	public string Format(string source)
	{
		var text = new StringBuilder();
		text.Append(Valid ? "Production atlas audit passed" : "Production atlas audit failed")
			.Append(" for '").Append(source).Append("'.");
		foreach (string error in Errors) text.Append("\n  ERROR: ").Append(error);
		foreach (string warning in Warnings) text.Append("\n  WARNING: ").Append(warning);
		return text.ToString();
	}
}
