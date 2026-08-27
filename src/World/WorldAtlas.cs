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
	public string PreviewReferencePath { get; set; } = "";
	public List<string> CompositionReferencePaths { get; set; } = new();
	public List<AtlasSourceLayer> SourceLayers { get; set; } = new();
	public List<AtlasProvince> Provinces { get; set; } = new();

	[JsonIgnore] public BiomeCatalogDefinition BiomeCatalog { get; private set; }

	public static WorldAtlasDefinition Load(string resourcePath)
	{
		var atlas = ReadJson<WorldAtlasDefinition>(resourcePath, "world atlas");
		if (!string.IsNullOrWhiteSpace(atlas.BiomeCatalogPath))
			atlas.BiomeCatalog = BiomeCatalogDefinition.Load(atlas.BiomeCatalogPath);
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
	public List<BiomeBuildProfile> Profiles { get; set; } = new();

	public static BiomeCatalogDefinition Load(string resourcePath) =>
		WorldAtlasDefinition.ReadJson<BiomeCatalogDefinition>(resourcePath, "biome catalog");

	public AtlasAuditReport Audit()
	{
		var report = new AtlasAuditReport();
		if (Version != 1) report.Error($"version must be 1, got {Version}");
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
	public AtlasHydrologyProfile Hydrology { get; set; } = new();
	public List<AtlasNoiseBand> NoiseBands { get; set; } = new();
	public AtlasSurfaceSet Surfaces { get; set; } = new();
	public string VegetationSetId { get; set; } = "";
	public string GroundDetailSetId { get; set; } = "";
	public string AtmosphereProfileId { get; set; } = "";
	public string ShaderProfileId { get; set; } = "";
	public string RoadProfileId { get; set; } = "";
	public string ArchitecturePaletteId { get; set; } = "";

	public IEnumerable<(string name, string value)> RequiredReferences()
	{
		yield return ("vegetationSetId", VegetationSetId);
		yield return ("groundDetailSetId", GroundDetailSetId);
		yield return ("atmosphereProfileId", AtmosphereProfileId);
		yield return ("shaderProfileId", ShaderProfileId);
		yield return ("roadProfileId", RoadProfileId);
		yield return ("architecturePaletteId", ArchitecturePaletteId);
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
