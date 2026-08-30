using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Petalfell.UI;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Fast authoring entrypoint. It deliberately exits before Planner/Terrain are
/// constructed: topology edits need a sub-second feedback loop, not a continent
/// generation followed by a report about a typo.
/// </summary>
public static class WorldAuthoring
{
	public static bool TryRun(Node owner, string defaultMapPath)
	{
		var args = OS.GetCmdlineUserArgs();
		bool audit = false;
		string preview = null;
		string atlasPreview = null;
		string atlasTopologyPreview = null;
		string atlasMapPreview = null;
		string domainId = null;
		string atlasDomainId = null;
		string compileSector = null;
		string verifySector = null;
		string verifyWilderness = null;
		string verifyAtlasHandoff = null;
		bool verifyAtlasWalkingHandoff = false;
		bool compileAtlas = false;
		bool verifyAtlas = false;
		bool auditAtlasHydrology = false;
		string atlasOutput = "res://content/chapter_01/derived";
		string sampleAtlas = null;
		string sectorOutput = null;
		string sectorPreview = null;
		int sectorApron = AtlasSectorCompiler.DefaultApron;
		string mapPath = defaultMapPath;

		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--world-audit") audit = true;
			else if (args[i] == "--world-preview" && i + 1 < args.Length) preview = args[++i];
			else if (args[i].StartsWith("--world-preview=")) preview = args[i][16..];
			else if (args[i] == "--atlas-preview" && i + 1 < args.Length) atlasPreview = args[++i];
			else if (args[i].StartsWith("--atlas-preview=")) atlasPreview = args[i][16..];
			else if (args[i] == "--atlas-topology-preview" && i + 1 < args.Length) atlasTopologyPreview = args[++i];
			else if (args[i].StartsWith("--atlas-topology-preview=")) atlasTopologyPreview = args[i][25..];
			else if (args[i] == "--atlas-map-preview" && i + 1 < args.Length) atlasMapPreview = args[++i];
			else if (args[i].StartsWith("--atlas-map-preview=")) atlasMapPreview = args[i][20..];
			else if (args[i] == "--world-domain" && i + 1 < args.Length) domainId = args[++i];
			else if (args[i].StartsWith("--world-domain=")) domainId = args[i][15..];
			else if (args[i] == "--atlas-domain" && i + 1 < args.Length) atlasDomainId = args[++i];
			else if (args[i].StartsWith("--atlas-domain=")) atlasDomainId = args[i][15..];
			else if (args[i] == "--compile-sector" && i + 1 < args.Length) compileSector = args[++i];
			else if (args[i].StartsWith("--compile-sector=")) compileSector = args[i][17..];
			else if (args[i] == "--verify-sector" && i + 1 < args.Length) verifySector = args[++i];
			else if (args[i].StartsWith("--verify-sector=")) verifySector = args[i][16..];
			else if (args[i] == "--verify-wilderness" && i + 1 < args.Length) verifyWilderness = args[++i];
			else if (args[i].StartsWith("--verify-wilderness=")) verifyWilderness = args[i][20..];
			else if (args[i] == "--verify-atlas-handoff" && i + 1 < args.Length) verifyAtlasHandoff = args[++i];
			else if (args[i].StartsWith("--verify-atlas-handoff=")) verifyAtlasHandoff = args[i][23..];
			else if (args[i] == "--verify-atlas-walking-handoff") verifyAtlasWalkingHandoff = true;
			else if (args[i] == "--compile-atlas") compileAtlas = true;
			else if (args[i] == "--verify-atlas") verifyAtlas = true;
			else if (args[i] == "--audit-atlas-hydrology") auditAtlasHydrology = true;
			else if (args[i] == "--atlas-output" && i + 1 < args.Length) atlasOutput = args[++i];
			else if (args[i].StartsWith("--atlas-output=")) atlasOutput = args[i][15..];
			else if (args[i] == "--sample-atlas" && i + 1 < args.Length) sampleAtlas = args[++i];
			else if (args[i].StartsWith("--sample-atlas=")) sampleAtlas = args[i][15..];
			else if (args[i] == "--sector-output" && i + 1 < args.Length) sectorOutput = args[++i];
			else if (args[i].StartsWith("--sector-output=")) sectorOutput = args[i][16..];
			else if (args[i] == "--sector-preview" && i + 1 < args.Length) sectorPreview = args[++i];
			else if (args[i].StartsWith("--sector-preview=")) sectorPreview = args[i][17..];
			else if (args[i] == "--sector-apron" && i + 1 < args.Length) sectorApron = int.Parse(args[++i], CultureInfo.InvariantCulture);
			else if (args[i].StartsWith("--sector-apron=")) sectorApron = int.Parse(args[i][15..], CultureInfo.InvariantCulture);
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}

		if (!audit && preview == null && atlasPreview == null && atlasTopologyPreview == null &&
		    atlasMapPreview == null &&
		    compileSector == null && verifySector == null && verifyWilderness == null &&
		    verifyAtlasHandoff == null && !verifyAtlasWalkingHandoff &&
		    !compileAtlas && !verifyAtlas && !auditAtlasHydrology &&
		    sampleAtlas == null) return false;
		int exit = 0;
		try
		{
			var map = MapDefinition.Load(mapPath);
			if (map.CanonicalAtlas != null)
			{
				var atlasReport = map.CanonicalAtlas.Audit();
				GD.Print(atlasReport.Format(map.CanonicalAtlasPath));
				GD.Print($"[atlas-audit] version {map.CanonicalAtlas.Version}  " +
				         $"atlas {map.CanonicalAtlas.Width}x{map.CanonicalAtlas.Depth}x{map.CanonicalAtlas.Height}  " +
				         $"sectors {map.CanonicalAtlas.Width / map.CanonicalAtlas.SectorSize}x{map.CanonicalAtlas.Depth / map.CanonicalAtlas.SectorSize}  " +
				         $"provinces {map.CanonicalAtlas.Provinces.Count}  profiles {map.CanonicalAtlas.BiomeCatalog?.Profiles.Count ?? 0}");
				if (map.CanonicalAtlas.Topology != null)
				{
					var topology = map.CanonicalAtlas.Topology;
					GD.Print($"[atlas-topology] version {topology.Version}  extent {topology.ExtentWidth}x{topology.ExtentDepth}  " +
					         $"domains {topology.Domains.Count}  sites {topology.Sites.Count}  " +
					         $"nodes {topology.RouteNodes.Count}  routes {topology.Routes.Count}");
					foreach (var domain in topology.Domains)
						GD.Print($"[domain-sectors] {domain.Id}: {SectorCoverage(domain, map.CanonicalAtlas)}");
				}
				if (!atlasReport.Valid) exit = 2;
				if (atlasReport.Valid && atlasPreview != null)
				{
					WriteAtlasSvg(map.CanonicalAtlas, atlasPreview);
					GD.Print($"[atlas-preview] {ProjectSettings.GlobalizePath(atlasPreview)}");
				}
				if (atlasReport.Valid && atlasTopologyPreview != null)
				{
					WriteAtlasTopologySvg(map.CanonicalAtlas, atlasTopologyPreview, atlasDomainId);
					GD.Print($"[atlas-topology-preview] {ProjectSettings.GlobalizePath(atlasTopologyPreview)}");
				}
				if (atlasReport.Valid && atlasMapPreview != null)
				{
					var mapCompiler = new AtlasSectorCompiler(map.CanonicalAtlas,
						map.DefaultSeed, map.CanonicalAtlasPath);
					Image image = AtlasWorldMap.RenderBackground(map.CanonicalAtlas,
						atlasOutput, mapCompiler.SourceFingerprint, out string source);
					string absolute = ProjectSettings.GlobalizePath(atlasMapPreview);
					string directory = Path.GetDirectoryName(absolute);
					if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
					Error error = image.SavePng(absolute);
					if (error != Error.Ok)
						throw new IOException($"could not write atlas map preview '{atlasMapPreview}': {error}");
					GD.Print($"[atlas-map-preview] {absolute} from {source}");
				}
				if (atlasReport.Valid && verifyAtlasWalkingHandoff)
				{
					AtlasWalkingHandoffVerification result =
						AtlasWalkingHandoffAuthoring.Verify(map.CanonicalAtlas,
							AtlasRuntimeHandoff.DefaultWalkingTriggerMargin,
							AtlasRuntimeHandoff.DefaultWalkingRearmMargin,
							AtlasRuntimeHandoff.DefaultWalkingCooldownFrames);
					GD.Print($"[atlas-walk-handoff-verify] " +
					         $"{result.CardinalTransitions} cardinal/" +
					         $"{result.CornerTransitions} corner/" +
					         $"{result.PartialOuterCorners} partial-outer transitions; " +
					         $"{result.OuterRefusals} outer refusal; " +
					         $"{result.SuppressedRepeats} suppressed repeats; " +
					         $"{result.ReturnTransitions} rearmed return");
				}
				if (atlasReport.Valid && (compileSector != null || verifySector != null ||
				    verifyWilderness != null || verifyAtlasHandoff != null ||
				    compileAtlas || verifyAtlas || auditAtlasHydrology || sampleAtlas != null))
				{
					var compiler = new AtlasSectorCompiler(map.CanonicalAtlas, map.DefaultSeed, map.CanonicalAtlasPath);
					if (compileAtlas)
						AtlasBatchAuthoring.Compile(map.CanonicalAtlas, compiler, atlasOutput, sectorApron);
					if (verifyAtlas)
						AtlasBatchAuthoring.Verify(map.CanonicalAtlas, compiler, atlasOutput, sectorApron);
					if (auditAtlasHydrology)
						AtlasBatchAuthoring.AuditHydrology(
							map.CanonicalAtlas, compiler, atlasOutput, sectorApron);
					if (sampleAtlas != null)
					{
						(int x, int z) = ParsePoint(sampleAtlas);
						if (x < 0 || z < 0 || x >= map.CanonicalAtlas.Width || z >= map.CanonicalAtlas.Depth)
							throw new InvalidOperationException($"atlas sample {x},{z} lies outside the atlas");
						int sx = x / map.CanonicalAtlas.SectorSize, sz = z / map.CanonicalAtlas.SectorSize;
						var data = compiler.Compile(sx, sz, sectorApron);
						int localX = x - data.OriginX, localZ = z - data.OriginZ;
						int index = localZ * data.Width + localX;
						string primary = map.CanonicalAtlas.BiomeCatalog.Profiles[data.Profile[index]].Id;
						string secondary = map.CanonicalAtlas.BiomeCatalog.Profiles[data.SecondaryProfile[index]].Id;
						GD.Print($"[atlas-sample] {x},{z} sector {sx},{sz} height {data.Height[index]} " +
						         $"water-surface {data.WaterSurface[index]} land {data.Land[index] != 0} " +
						         $"hydrology {data.Hydrology[index]} water-value {data.Water[index]} " +
						         $"surface {(AtlasTerrainSurface)data.Surface[index]} slope {data.Slope[index]} " +
						         $"wetness {data.Wetness[index]} profile {primary} secondary {secondary} " +
						         $"weight {data.ProfileBlend[index] / 255f:0.000}");
					}
					if (compileSector != null)
					{
						(int sx, int sz) = ParseSector(compileSector);
						var data = compiler.Compile(sx, sz, sectorApron);
						sectorOutput ??= $"res://content/chapter_01/derived/sector-{sx}-{sz}.pfs";
						sectorPreview ??= $"res://../shots/atlas-sector-{sx}-{sz}.png";
						string artifactHash = compiler.WriteArtifact(data, sectorOutput);
						compiler.WritePreview(data, sectorPreview);
						var stats = data.CoreStatistics();
						GD.Print($"[sector-compile] {sx},{sz} origin {data.OriginX},{data.OriginZ} " +
						         $"window {data.Width}x{data.Depth} apron {data.Apron} " +
						         $"land {stats.LandCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"water {stats.WaterCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"floodplain {stats.FloodplainCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"bank {stats.BankCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"cliff {stats.CliffCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"shore {stats.ShoreCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"water-steps {stats.WaterStepEdges} severe {stats.SevereWaterStepEdges} " +
						         $"max-step {stats.MaxWaterStep}@{stats.MaxWaterStepX},{stats.MaxWaterStepZ} " +
						         $"submerged-dry {stats.SubmergedDryBoundaryEdges} " +
						         $"max-depth {stats.MaxSubmergedDryDepth}@{stats.MaxSubmergedDryX},{stats.MaxSubmergedDryZ} " +
						         $"region-blend {stats.BlendedCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"land-height {stats.MinHeight}..{stats.MaxHeight} " +
						         $"water-surface {stats.MinWaterSurface}..{stats.MaxWaterSurface} hash {artifactHash}");
						GD.Print($"[sector-artifact] {ProjectSettings.GlobalizePath(sectorOutput)}");
						GD.Print($"[sector-preview] {ProjectSettings.GlobalizePath(sectorPreview)}");
					}
					if (verifySector != null)
					{
						(int sx, int sz) = ParseSector(verifySector);
						var result = compiler.Verify(sx, sz, sectorApron);
						GD.Print($"[sector-verify] {sx},{sz} repeat hash {result.ContentHash} " +
						         $"east overlap {result.EastOverlapCells} cells south overlap {result.SouthOverlapCells} cells");
					}
					if (verifyWilderness != null)
					{
						(int sx, int sz) = ParseSector(verifyWilderness);
						AtlasWildernessVerification result = AtlasWildernessAuthoring.Verify(
							map.CanonicalAtlas, compiler, map.DefaultSeed, sx, sz, sectorApron);
						AtlasWildernessDressingStatistics stats = result.Statistics;
						GD.Print($"[wilderness-verify] {sx},{sz} manifest {stats.ManifestHash:x16} " +
						         $"{stats.Trees} trees/{stats.Boulders} boulders from {stats.Candidates} candidates; " +
						         $"repeat {result.RepeatColumns} columns/{result.RepeatVoxels} voxels; " +
						         $"east {result.EastColumns} columns/{result.EastVoxels} voxels; " +
						         $"south {result.SouthColumns} columns/{result.SouthVoxels} voxels");
					}
					if (verifyAtlasHandoff != null)
					{
						(int x, int z) = ParsePoint(verifyAtlasHandoff);
						string output = atlasOutput.TrimEnd('/');
						AtlasMosaicBounds requestedBounds = AtlasRuntimeHandoff.WindowAround(
							map.CanonicalAtlas, x, z, sectorSpan: 2);
						string resolution = "requested address";
						if (!TryPrepare(x, z, out AtlasPreparedWindow prepared,
						    out AtlasRuntimeLanding landing, out string rejection))
						{
							string originalRejection = rejection ??
								"no traversable surface in requested mosaic";
							if (AtlasRuntimeHandoff.TryNearestAuthoredDryHint(map.CanonicalAtlas,
							    x, z, out BlockPoint dryHint, out int pixelRadius) &&
							    TryPrepare(dryHint.X, dryHint.Z, out prepared, out landing,
								    out string dryRejection))
							{
								resolution = $"registered dry hint {dryHint.X},{dryHint.Z} " +
								             $"at pixel radius {pixelRadius}";
								rejection = originalRejection;
								if (dryRejection != null)
									resolution += $", hint rejected {dryRejection}/radius " +
									              $"{landing.SearchRadius}";
							}
							else if (TryBloomRecovery(out prepared, out landing,
							         out string bloomRejection, out BlockPoint bloomSpawn))
							{
								resolution = $"authored Bloom recovery {bloomSpawn.X}," +
								             $"{bloomSpawn.Z}";
								rejection = originalRejection;
								if (bloomRejection != null)
									resolution += $", spawn rejected {bloomRejection}/radius " +
									              $"{landing.SearchRadius}";
							}
							else throw new InvalidOperationException(
								$"no deterministic traversable landing for {x},{z}; " +
								$"requested mosaic {requestedBounds} rejected {originalRejection}");
						}
						GD.Print($"[atlas-handoff-verify] request {x},{z} requested-sectors " +
						         $"{requestedBounds}; {resolution}; resolved-sectors " +
						         $"{prepared.Bounds} => " +
						         $"{landing.GlobalX},{landing.GlobalZ},{landing.SurfaceY} " +
						         $"exact {landing.ExactCell} radius {landing.SearchRadius} " +
						         $"rejection {rejection ?? "none"}; wilderness " +
						         $"{prepared.Wilderness.Trees} trees/" +
						         $"{prepared.Wilderness.Boulders} boulders " +
						         $"manifest {prepared.Wilderness.ManifestHash:x16}; sites " +
						         $"{string.Join(',', prepared.SiteBuilds.Select(site => site.SiteId))}");

						bool TryPrepare(int centreX, int centreZ,
							out AtlasPreparedWindow result, out AtlasRuntimeLanding resultLanding,
							out string resultRejection)
						{
							result = AtlasRuntimeHandoff.PrepareWindow(map.CanonicalAtlas,
								map.DefaultSeed, centreX, centreZ, sectorSpan: 2,
								LoadHandoffSector,
								message => GD.PrintErr($"[atlas-handoff-verify] {message}"));
							return AtlasRuntimeHandoff.TryResolveLanding(result.Window,
								centreX, centreZ, out resultLanding, out resultRejection);
						}

						bool TryBloomRecovery(out AtlasPreparedWindow result,
							out AtlasRuntimeLanding resultLanding, out string resultRejection,
							out BlockPoint spawn)
						{
							if (AtlasRuntimeHandoff.TryGetAuthoredRecoverySpawn(map.CanonicalAtlas,
							    out spawn) && TryPrepare(spawn.X, spawn.Z, out result,
							    out resultLanding, out resultRejection)) return true;
							result = null;
							resultLanding = default;
							resultRejection = null;
							return false;
						}

						AtlasSectorData LoadHandoffSector(int sx, int sz)
						{
							string artifact = $"{output}/sector-{sx}-{sz}.pfs";
							try { return compiler.ReadArtifact(artifact); }
							catch (Exception ex) when (ex is FileNotFoundException or
							       InvalidDataException or EndOfStreamException)
							{
								GD.Print($"[atlas-handoff-verify] compiling {sx},{sz}: {ex.Message}");
								return compiler.Compile(sx, sz, AtlasSectorCompiler.DefaultApron);
							}
						}
					}
				}
			}
			else if (atlasPreview != null || atlasTopologyPreview != null || atlasMapPreview != null ||
			         compileSector != null || verifySector != null || verifyWilderness != null ||
			         verifyAtlasHandoff != null || verifyAtlasWalkingHandoff ||
			         compileAtlas || verifyAtlas || auditAtlasHydrology ||
			         sampleAtlas != null)
				throw new InvalidOperationException($"Map '{mapPath}' has no canonicalAtlasPath.");
			if (map.CanonicalWorld == null)
			{
				if (preview != null) throw new InvalidOperationException($"Map '{mapPath}' has no canonicalWorldPath.");
				owner.GetTree().Quit(exit);
				return true;
			}

			var report = map.CanonicalWorld.Audit(map);
			GD.Print(report.Format(map.CanonicalWorldPath));
			GD.Print($"[world-audit] version {map.CanonicalWorld.Version}  world {map.CanonicalWorld.WorldSize}  " +
			         $"domains {map.CanonicalWorld.Domains.Count}  sites {map.CanonicalWorld.Sites.Count}  " +
			         $"nodes {map.CanonicalWorld.RouteNodes.Count}  routes {map.CanonicalWorld.Routes.Count}");
			if (!report.Valid) exit = 2;
			if (report.Valid && preview != null)
			{
				WriteSvg(map, preview, domainId);
				GD.Print($"[world-preview] {ProjectSettings.GlobalizePath(preview)}");
			}
		}
		catch (Exception ex)
		{
			GD.PushError($"[world-authoring] {ex.Message}");
			exit = 2;
		}

		owner.GetTree().Quit(exit);
		return true;
	}

	private static string SectorCoverage(CanonicalDomain domain, WorldAtlasDefinition atlas)
	{
		int minX = Math.Clamp(domain.Boundary.Min(p => p.X) / atlas.SectorSize, 0, atlas.Width / atlas.SectorSize - 1);
		int maxX = Math.Clamp(domain.Boundary.Max(p => p.X) / atlas.SectorSize, 0, atlas.Width / atlas.SectorSize - 1);
		int minZ = Math.Clamp(domain.Boundary.Min(p => p.Z) / atlas.SectorSize, 0, atlas.Depth / atlas.SectorSize - 1);
		int maxZ = Math.Clamp(domain.Boundary.Max(p => p.Z) / atlas.SectorSize, 0, atlas.Depth / atlas.SectorSize - 1);
		var addresses = new List<string>();
		for (int z = minZ; z <= maxZ; z++)
		for (int x = minX; x <= maxX; x++)
			addresses.Add($"{x},{z}");
		return string.Join(" ", addresses);
	}

	private static (int x, int z) ParseSector(string address)
	{
		string[] parts = address.Split(',');
		if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
		    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
			throw new InvalidOperationException($"sector address '{address}' must be x,z");
		return (x, z);
	}

	private static (int x, int z) ParsePoint(string address)
	{
		string[] parts = address.Split(',');
		if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
		    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
			throw new InvalidOperationException($"atlas point '{address}' must be x,z");
		return (x, z);
	}

	public static void WriteAtlasSvg(WorldAtlasDefinition atlas, string outputPath)
	{
		const int canvasW = 1600, canvasH = 1260, pad = 60, header = 105;
		float scale = Math.Min((canvasW - pad * 2f) / atlas.Width,
			(canvasH - header - pad) / (float)atlas.Depth);
		float mapW = atlas.Width * scale, mapH = atlas.Depth * scale;
		float ox = (canvasW - mapW) * 0.5f, oz = header + (canvasH - header - mapH) * 0.5f;
		string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
		float X(int x) => ox + x * scale;
		float Z(int z) => oz + z * scale;
		string Esc(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
			.Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
		string Points(IEnumerable<BlockPoint> points) =>
			string.Join(" ", points.Select(p => $"{F(X(p.X))},{F(Z(p.Z))}"));

		var svg = new StringBuilder(4_000_000);
		svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{canvasW}\" height=\"{canvasH}\" viewBox=\"0 0 {canvasW} {canvasH}\">");
		svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#191622\"/>");
		svg.AppendLine("<defs><filter id=\"atlasTerrain\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\".55 0 0 0 .10  .55 0 0 0 .16  .50 0 0 0 .24  0 0 0 1 0\"/></filter><filter id=\"atlasWater\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"0 0 0 0 .28  0 0 0 0 .62  0 0 0 0 .90  .2126 .7152 .0722 0 0\"/><feComponentTransfer><feFuncA type=\"gamma\" amplitude=\".88\" exponent=\"2.6\" offset=\"0\"/></feComponentTransfer></filter><filter id=\"atlasRegion\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  .2126 .7152 .0722 0 0\"/><feComponentTransfer><feFuncA type=\"linear\" slope=\".5\"/></feComponentTransfer></filter></defs>");
		svg.AppendLine("<style>text{font-family:Inter,system-ui,sans-serif}.label{paint-order:stroke;stroke:#191622;stroke-width:5px;stroke-linejoin:round}.province{font-size:18px;font-weight:750;fill:#fff}.small{font-size:13px;fill:#d8d0df}</style>");

		AtlasSourceLayer elevation = atlas.SourceLayers.FirstOrDefault(l => l.Kind == AtlasLayerKind.Elevation &&
			l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
		string underlayPath = elevation?.Path ?? atlas.PreviewReferencePath;
		if (!string.IsNullOrWhiteSpace(underlayPath) && Godot.FileAccess.FileExists(underlayPath))
		{
			string absoluteReference = ProjectSettings.GlobalizePath(underlayPath);
			string encoded = Convert.ToBase64String(File.ReadAllBytes(absoluteReference));
			string filter = elevation == null ? "" : " filter=\"url(#atlasTerrain)\"";
			svg.AppendLine($"<image x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" preserveAspectRatio=\"none\" opacity=\".88\"{filter} href=\"data:image/png;base64,{encoded}\"/>");
		}
		AtlasSourceLayer water = atlas.SourceLayers.FirstOrDefault(l => l.Kind == AtlasLayerKind.Water &&
			l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
		if (water != null)
		{
			string absoluteWater = ProjectSettings.GlobalizePath(water.Path);
			string encoded = Convert.ToBase64String(File.ReadAllBytes(absoluteWater));
			svg.AppendLine($"<image x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" preserveAspectRatio=\"none\" filter=\"url(#atlasWater)\" href=\"data:image/png;base64,{encoded}\"/>");
		}
		AtlasSourceLayer region = atlas.SourceLayers.FirstOrDefault(l => l.Kind == AtlasLayerKind.Region &&
			l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
		if (region != null)
		{
			string absoluteRegion = ProjectSettings.GlobalizePath(region.Path);
			string encoded = Convert.ToBase64String(File.ReadAllBytes(absoluteRegion));
			svg.AppendLine($"<image x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" preserveAspectRatio=\"none\" filter=\"url(#atlasRegion)\" href=\"data:image/png;base64,{encoded}\"/>");
		}

		for (int x = 0; x <= atlas.Width; x += atlas.SectorSize)
		{
			float px = X(x);
			svg.AppendLine($"<line x1=\"{F(px)}\" y1=\"{F(oz)}\" x2=\"{F(px)}\" y2=\"{F(oz + mapH)}\" stroke=\"#fff\" stroke-opacity=\".19\" stroke-width=\"1\"/>");
		}
		for (int z = 0; z <= atlas.Depth; z += atlas.SectorSize)
		{
			float pz = Z(z);
			svg.AppendLine($"<line x1=\"{F(ox)}\" y1=\"{F(pz)}\" x2=\"{F(ox + mapW)}\" y2=\"{F(pz)}\" stroke=\"#fff\" stroke-opacity=\".19\" stroke-width=\"1\"/>");
		}

		foreach (var province in atlas.Provinces)
		{
			string fillOpacity = region == null ? ".16" : "0";
			string guideStyle = region == null
				? "stroke-width=\"4\""
				: "stroke-width=\"2\" stroke-opacity=\".32\" stroke-dasharray=\"8 7\"";
			svg.AppendLine($"<polygon points=\"{Points(province.Boundary)}\" fill=\"{province.PreviewColour}\" fill-opacity=\"{fillOpacity}\" stroke=\"{province.PreviewColour}\" {guideStyle} stroke-linejoin=\"round\"/>");
			float cx = province.Boundary.Average(p => X(p.X));
			float cz = province.Boundary.Average(p => Z(p.Z));
			svg.AppendLine($"<text class=\"province label\" x=\"{F(cx)}\" y=\"{F(cz)}\" text-anchor=\"middle\">{Esc(province.DisplayName)}</text>");
			svg.AppendLine($"<text class=\"small label\" x=\"{F(cx)}\" y=\"{F(cz + 19)}\" text-anchor=\"middle\">{Esc(string.Join(" + ", province.BiomeProfileIds))}</text>");
		}

		svg.AppendLine($"<rect x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" fill=\"none\" stroke=\"#fff7f1\" stroke-opacity=\".65\" stroke-width=\"2\"/>");
		int planned = atlas.SourceLayers.Count(l => l.Status == AtlasLayerStatus.Planned);
		int blockout = atlas.SourceLayers.Count(l => l.Status == AtlasLayerStatus.Blockout);
		int accepted = atlas.SourceLayers.Count(l => l.Status == AtlasLayerStatus.Accepted);
		svg.AppendLine($"<text x=\"{pad}\" y=\"38\" fill=\"#fff7f1\" font-size=\"24\" font-weight=\"750\">{Esc(atlas.DisplayName)} — province and sector blockout</text>");
		svg.AppendLine($"<text x=\"{pad}\" y=\"62\" fill=\"#cfc4d8\" font-size=\"14\">{atlas.Width} × {atlas.Depth} × {atlas.Height} blocks · sea {atlas.SeaLevel} · {atlas.Width / atlas.SectorSize} × {atlas.Depth / atlas.SectorSize} sectors · {atlas.Provinces.Count} provinces · layers {accepted} accepted / {blockout} blockout / {planned} planned</text>");
		string underlayNote = elevation == null
			? "Reference image underlay only; its generated labels, exact coastline and landmark density are not canonical."
			: water == null
				? "Underlay is registered elevation; province polygons remain provisional until the region layer is painted."
				: region == null
					? "Underlay is registered elevation with the water blockout in blue; province polygons remain allocation guides."
					: "Registered elevation, water and categorical region blockouts; polygon outlines are allocation guides only.";
		svg.AppendLine($"<text x=\"{pad}\" y=\"84\" fill=\"#a99db4\" font-size=\"12\">{underlayNote}</text>");
		svg.AppendLine("</svg>");

		string absolute = ProjectSettings.GlobalizePath(outputPath);
		string dir = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(absolute, svg.ToString(), new UTF8Encoding(false));
	}

	public static void WriteSvg(MapDefinition map, string outputPath, string domainId = null)
	{
		var world = map.CanonicalWorld ?? throw new InvalidOperationException("No canonical review world loaded.");
		WriteTopologySvg(world, map.DisplayName, outputPath, domainId, null, map.Boundary);
	}

	public static void WriteAtlasTopologySvg(WorldAtlasDefinition atlas, string outputPath, string domainId = null)
	{
		var world = atlas.Topology ?? throw new InvalidOperationException(
			$"Atlas '{atlas.Id}' has no registered production topology.");
		WriteTopologySvg(world, atlas.DisplayName, outputPath, domainId, atlas, null);
	}

	private static void WriteTopologySvg(CanonicalWorldDefinition world, string displayName,
		string outputPath, string domainId, WorldAtlasDefinition atlas, MapBoundary reviewBoundary)
	{
		const int canvasW = 1600, canvasH = 1260, pad = 60, header = 95;
		CanonicalDomain focus = null;
		int minX = 0, minZ = 0, maxX = world.ExtentWidth, maxZ = world.ExtentDepth;
		if (!string.IsNullOrWhiteSpace(domainId))
		{
			focus = world.Domains.FirstOrDefault(d => d.Id == domainId)
			        ?? throw new InvalidOperationException($"Unknown authored domain '{domainId}'.");
			const int margin = 140;
			minX = Math.Max(0, focus.Boundary.Min(p => p.X) - margin);
			minZ = Math.Max(0, focus.Boundary.Min(p => p.Z) - margin);
			maxX = Math.Min(world.ExtentWidth, focus.Boundary.Max(p => p.X) + margin);
			maxZ = Math.Min(world.ExtentDepth, focus.Boundary.Max(p => p.Z) + margin);
		}

		float scale = Math.Min((canvasW - pad * 2f) / (maxX - minX),
			(canvasH - header - pad) / (float)(maxZ - minZ));
		float mapW = (maxX - minX) * scale, mapH = (maxZ - minZ) * scale;
		float left = (canvasW - mapW) * 0.5f, top = header + (canvasH - header - mapH) * 0.5f;
		string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
		float X(int x) => left + (x - minX) * scale;
		float Z(int z) => top + (z - minZ) * scale;
		string Esc(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
			.Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
		string Points(IEnumerable<BlockPoint> points) =>
			string.Join(" ", points.Select(p => $"{F(X(p.X))},{F(Z(p.Z))}"));

		var svg = new StringBuilder(4_000_000);
		svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{canvasW}\" height=\"{canvasH}\" viewBox=\"0 0 {canvasW} {canvasH}\">");
		svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#191622\"/>");
		svg.AppendLine($"<defs><clipPath id=\"mapClip\"><rect x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\"/></clipPath><filter id=\"atlasTerrain\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\".55 0 0 0 .10  .55 0 0 0 .16  .50 0 0 0 .24  0 0 0 1 0\"/></filter><filter id=\"atlasWater\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"0 0 0 0 .28  0 0 0 0 .62  0 0 0 0 .90  .2126 .7152 .0722 0 0\"/><feComponentTransfer><feFuncA type=\"gamma\" amplitude=\".88\" exponent=\"2.6\" offset=\"0\"/></feComponentTransfer></filter><filter id=\"atlasRegion\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  .2126 .7152 .0722 0 0\"/><feComponentTransfer><feFuncA type=\"linear\" slope=\".5\"/></feComponentTransfer></filter></defs>");
		svg.AppendLine("<style>text{font-family:Inter,system-ui,sans-serif}.label{paint-order:stroke;stroke:#191622;stroke-width:5px;stroke-linejoin:round}.small{font-size:12px;fill:#d8d0df}.site{font-size:14px;font-weight:650;fill:#fff7f1}.domain{font-size:15px;font-weight:700;fill:#ead8ff;letter-spacing:.03em}.sector{font-size:11px;fill:#fff;opacity:.62}</style>");

		if (atlas != null)
		{
			svg.AppendLine("<g clip-path=\"url(#mapClip)\">");
			void Layer(AtlasLayerKind kind, string filter, float opacity)
			{
				var layer = atlas.SourceLayers.FirstOrDefault(l => l.Kind == kind &&
					l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
				if (layer == null) return;
				string encoded = Convert.ToBase64String(File.ReadAllBytes(ProjectSettings.GlobalizePath(layer.Path)));
				svg.AppendLine($"<image x=\"{F(X(0))}\" y=\"{F(Z(0))}\" width=\"{F(world.ExtentWidth * scale)}\" height=\"{F(world.ExtentDepth * scale)}\" preserveAspectRatio=\"none\" opacity=\"{F(opacity)}\" filter=\"url(#{filter})\" href=\"data:image/png;base64,{encoded}\"/>");
			}
			Layer(AtlasLayerKind.Elevation, "atlasTerrain", .88f);
			Layer(AtlasLayerKind.Water, "atlasWater", 1f);
			Layer(AtlasLayerKind.Region, "atlasRegion", 1f);
			int firstSectorX = Math.Max(0, minX / atlas.SectorSize);
			int lastSectorX = Math.Min(atlas.Width / atlas.SectorSize - 1, (maxX - 1) / atlas.SectorSize);
			int firstSectorZ = Math.Max(0, minZ / atlas.SectorSize);
			int lastSectorZ = Math.Min(atlas.Depth / atlas.SectorSize - 1, (maxZ - 1) / atlas.SectorSize);
			for (int sx = firstSectorX; sx <= lastSectorX + 1; sx++)
			{
				float px = X(sx * atlas.SectorSize);
				svg.AppendLine($"<line x1=\"{F(px)}\" y1=\"{F(top)}\" x2=\"{F(px)}\" y2=\"{F(top + mapH)}\" stroke=\"#fff\" stroke-opacity=\".25\" stroke-width=\"1\"/>");
			}
			for (int sz = firstSectorZ; sz <= lastSectorZ + 1; sz++)
			{
				float pz = Z(sz * atlas.SectorSize);
				svg.AppendLine($"<line x1=\"{F(left)}\" y1=\"{F(pz)}\" x2=\"{F(left + mapW)}\" y2=\"{F(pz)}\" stroke=\"#fff\" stroke-opacity=\".25\" stroke-width=\"1\"/>");
			}
			if (focus != null)
				for (int sz = firstSectorZ; sz <= lastSectorZ; sz++)
				for (int sx = firstSectorX; sx <= lastSectorX; sx++)
					svg.AppendLine($"<text class=\"sector label\" x=\"{F(X(sx * atlas.SectorSize) + 7)}\" y=\"{F(Z(sz * atlas.SectorSize) + 16)}\">{sx},{sz}</text>");
			svg.AppendLine("</g>");
		}
		else if (reviewBoundary != null)
		{
			float bx = X((int)(reviewBoundary.Centre.X * world.ExtentWidth));
			float bz = Z((int)(reviewBoundary.Centre.Z * world.ExtentDepth));
			float brx = reviewBoundary.RadiusX * world.ExtentWidth * scale;
			float brz = reviewBoundary.RadiusZ * world.ExtentDepth * scale;
			svg.AppendLine($"<ellipse cx=\"{F(bx)}\" cy=\"{F(bz)}\" rx=\"{F(brx)}\" ry=\"{F(brz)}\" fill=\"#546789\" fill-opacity=\".28\" stroke=\"#8da0c8\" stroke-width=\"2\"/>");
		}

		var visibleDomains = focus == null ? world.Domains : new List<CanonicalDomain> { focus };
		foreach (var domain in visibleDomains)
		{
			svg.AppendLine($"<polygon points=\"{Points(domain.Boundary)}\" fill=\"#9c7fc4\" fill-opacity=\".18\" stroke=\"#e0c2ff\" stroke-width=\"3\" stroke-dasharray=\"10 6\"/>");
			float dx = domain.Boundary.Average(p => X(p.X));
			float dz = domain.Boundary.Min(p => Z(p.Z)) - 10;
			svg.AppendLine($"<text class=\"domain label\" x=\"{F(dx)}\" y=\"{F(dz)}\" text-anchor=\"middle\">{Esc(domain.DisplayName)}</text>");
		}

		string RouteColour(RoadKind kind) => kind switch
		{
			RoadKind.Major => "#fff0dc",
			RoadKind.Local or RoadKind.Street => "#e7bfc9",
			RoadKind.Abandoned => "#d799ac",
			_ => "#c7a08f",
		};
		var visibleSiteIds = new HashSet<string>((focus == null ? world.Sites : world.Sites.Where(s => s.DomainId == focus.Id))
			.Select(s => s.Id), StringComparer.Ordinal);
		var visibleNodeIds = new HashSet<string>(world.RouteNodes.Where(n => focus == null ||
			visibleSiteIds.Contains(n.SiteId) || (n.Point.X >= minX && n.Point.X <= maxX && n.Point.Z >= minZ && n.Point.Z <= maxZ))
			.Select(n => n.Id), StringComparer.Ordinal);
		var visibleRoutes = world.Routes.Where(route => focus == null || visibleNodeIds.Contains(route.FromNodeId) ||
			visibleNodeIds.Contains(route.ToNodeId)).ToList();
		foreach (var route in visibleRoutes)
		{
			float width = Math.Clamp(route.Width * scale, 1.5f, 8f);
			svg.AppendLine($"<polyline points=\"{Points(route.Points)}\" fill=\"none\" stroke=\"#191622\" stroke-width=\"{F(width + 3f)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
			svg.AppendLine($"<polyline points=\"{Points(route.Points)}\" fill=\"none\" stroke=\"{RouteColour(route.Kind)}\" stroke-width=\"{F(width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
		}
		if (focus?.Plan != null)
			AppendDomainPlan(svg, focus.Plan, X, Z, scale, F, Esc);

		string SiteColour(SiteTier tier) => tier switch
		{
			SiteTier.GreatWork => "#ffe08a", SiteTier.District => "#ffb9a6",
			SiteTier.Precinct => "#d9b8ef", _ => "#a9dbd4",
		};
		var visibleSites = world.Sites.Where(site => focus == null || site.DomainId == focus.Id).ToList();
		foreach (var site in visibleSites)
		{
			float cx = X(site.Centre.X), cz = Z(site.Centre.Z);
			float w = Math.Max(8f, site.ExtentX * scale), h = Math.Max(8f, site.ExtentZ * scale);
			string colour = SiteColour(site.Tier);
			svg.AppendLine($"<rect x=\"{F(cx - w / 2)}\" y=\"{F(cz - h / 2)}\" width=\"{F(w)}\" height=\"{F(h)}\" rx=\"5\" fill=\"{colour}\" fill-opacity=\".22\" stroke=\"{colour}\" stroke-width=\"3\" transform=\"rotate({F(site.OrientationDegrees)} {F(cx)} {F(cz)})\"/>");
			float length = Math.Max(12f, Math.Min(w, h) * .38f);
			float rad = site.OrientationDegrees * MathF.PI / 180f;
			svg.AppendLine($"<line x1=\"{F(cx)}\" y1=\"{F(cz)}\" x2=\"{F(cx + MathF.Sin(rad) * length)}\" y2=\"{F(cz + MathF.Cos(rad) * length)}\" stroke=\"{colour}\" stroke-width=\"3\"/>");
			svg.AppendLine($"<circle cx=\"{F(cx)}\" cy=\"{F(cz)}\" r=\"5\" fill=\"{colour}\" stroke=\"#191622\" stroke-width=\"2\"/>");
			if (focus != null || site.Tier is SiteTier.District or SiteTier.GreatWork)
			{
				float labelX = focus?.Plan == null ? cx + 9 : cx - w * .5f + 7;
				float labelZ = focus?.Plan == null ? cz - 9 : cz - h * .5f + 17;
				svg.AppendLine($"<text class=\"site label\" x=\"{F(labelX)}\" y=\"{F(labelZ)}\">{Esc(site.DisplayName)}</text>");
				if (focus?.Plan == null)
					svg.AppendLine($"<text class=\"small label\" x=\"{F(cx + 9)}\" y=\"{F(cz + 7)}\">{Esc(site.Tier.ToString())} · {site.Centre.X},{site.Centre.Z}</text>");
			}
		}

		foreach (var node in world.RouteNodes.Where(n => visibleNodeIds.Contains(n.Id)))
			svg.AppendLine($"<circle cx=\"{F(X(node.Point.X))}\" cy=\"{F(Z(node.Point.Z))}\" r=\"3\" fill=\"#fff\" stroke=\"#191622\" stroke-width=\"1.5\"/>");

		svg.AppendLine($"<rect x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" fill=\"none\" stroke=\"#fff7f1\" stroke-opacity=\".65\" stroke-width=\"2\"/>");
		string scope = focus == null ? (atlas == null ? "review topology" : "production topology") : $"{focus.DisplayName} detail";
		svg.AppendLine($"<text x=\"{pad}\" y=\"35\" fill=\"#fff7f1\" font-size=\"22\" font-weight=\"750\">{Esc(displayName)} — {Esc(scope)}</text>");
		string detail = $"extent {world.ExtentWidth} × {world.ExtentDepth} · version {world.Version} · {visibleDomains.Count} domains · {visibleSites.Count} sites · {visibleRoutes.Count} routes";
		if (focus != null && atlas != null) detail += $" · sectors {SectorCoverage(focus, atlas)}";
		svg.AppendLine($"<text x=\"{pad}\" y=\"58\" fill=\"#bdb2c8\" font-size=\"13\">{Esc(detail)}</text>");
		svg.AppendLine("</svg>");

		string absolute = ProjectSettings.GlobalizePath(outputPath);
		string dir = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(absolute, svg.ToString(), new UTF8Encoding(false));
	}

	private static void AppendDomainPlan(StringBuilder svg, DomainPlanDefinition plan,
		Func<int, float> xMap, Func<int, float> zMap, float scale,
		Func<float, string> format, Func<string, string> escape)
	{
		(float x, float z) At(PlanPoint local)
		{
			BlockPoint global = plan.ToGlobal(local);
			return (xMap(global.X), zMap(global.Z));
		}
		string Points(IEnumerable<PlanPoint> points) => string.Join(" ", points.Select(point =>
		{
			(float x, float z) = At(point);
			return $"{format(x)},{format(z)}";
		}));
		string PlatformColour(PlanPlatformRole role) => role switch
		{
			PlanPlatformRole.Slab => "#ffb9a6",
			PlanPlatformRole.Deck => "#d8bbed",
			PlanPlatformRole.Court => "#f2cfaa",
			PlanPlatformRole.Causeway => "#8fd7ee",
			PlanPlatformRole.Terrace => "#b8d6a2",
			_ => "#d8d1c8",
		};

		foreach (var platform in plan.Platforms)
		{
			string colour = PlatformColour(platform.Role);
			svg.AppendLine($"<polygon points=\"{Points(platform.Polygon)}\" fill=\"{colour}\" fill-opacity=\".42\" stroke=\"{colour}\" stroke-width=\"3\" stroke-linejoin=\"round\"/>");
			float cx = platform.Polygon.Average(p => At(p).x);
			float cz = platform.Polygon.Average(p => At(p).z);
			svg.AppendLine($"<text class=\"small label\" x=\"{format(cx)}\" y=\"{format(cz)}\" text-anchor=\"middle\">{platform.Role} · Y{platform.SurfaceY}</text>");
			foreach (var cutout in platform.Cutouts)
				svg.AppendLine($"<polygon points=\"{Points(cutout.Polygon)}\" fill=\"#191622\" fill-opacity=\".34\" stroke=\"#fff7f1\" stroke-width=\"2\" stroke-dasharray=\"5 4\"/>");
		}

		foreach (var stair in plan.Stairs)
		{
			(float x1, float z1) = At(stair.From);
			(float x2, float z2) = At(stair.To);
			float width = Math.Clamp(stair.Width * scale, 5f, 13f);
			svg.AppendLine($"<line x1=\"{format(x1)}\" y1=\"{format(z1)}\" x2=\"{format(x2)}\" y2=\"{format(z2)}\" stroke=\"#191622\" stroke-width=\"{format(width + 4)}\" stroke-linecap=\"butt\"/>");
			svg.AppendLine($"<line x1=\"{format(x1)}\" y1=\"{format(z1)}\" x2=\"{format(x2)}\" y2=\"{format(z2)}\" stroke=\"#fff0dc\" stroke-width=\"{format(width)}\" stroke-dasharray=\"3 3\"/>");
		}

		foreach (var wall in plan.Walls)
		{
			string dash = wall.State switch
			{
				PlanWallState.Trace => " stroke-dasharray=\"3 5\"",
				PlanWallState.Stub => " stroke-dasharray=\"9 5\"",
				PlanWallState.Broken => " stroke-dasharray=\"16 6\"",
				_ => "",
			};
			float width = Math.Clamp(2f + wall.Height * .12f, 2.5f, 7f);
			svg.AppendLine($"<polyline points=\"{Points(wall.Points)}\" fill=\"none\" stroke=\"#f7e9dc\" stroke-width=\"{format(width)}\" stroke-linejoin=\"round\"{dash}/>");
		}

		foreach (var socket in plan.RouteSockets)
		{
			(float x, float z) = At(socket.Point);
			svg.AppendLine($"<rect x=\"{format(x - 5)}\" y=\"{format(z - 5)}\" width=\"10\" height=\"10\" fill=\"#ffe08a\" stroke=\"#191622\" stroke-width=\"2\" transform=\"rotate(45 {format(x)} {format(z)})\"/>");
		}

		foreach (var landmark in plan.Landmarks)
		{
			(float x, float z) = At(landmark.Point);
			float radius = Math.Clamp(4f + Math.Max(landmark.Height, landmark.Span) * scale * .08f, 6f, 12f);
			string colour = landmark.Kind switch
			{
				PlanLandmarkKind.Arch => "#ff9f8c",
				PlanLandmarkKind.Pylon => "#ffe08a",
				PlanLandmarkKind.Colonnade => "#d9b8ef",
				PlanLandmarkKind.FallenColumn => "#b7cfd4",
				_ => "#a9dbd4",
			};
			svg.AppendLine($"<circle cx=\"{format(x)}\" cy=\"{format(z)}\" r=\"{format(radius)}\" fill=\"{colour}\" stroke=\"#191622\" stroke-width=\"2\"/>");
			if (landmark.Kind is PlanLandmarkKind.Emblem or PlanLandmarkKind.Basin) continue;
			string measure = landmark.Height > 0 ? $"H{landmark.Height}" : landmark.Length > 0 ? $"L{landmark.Length}" : $"S{landmark.Span}";
			if (landmark.Count > 1) measure = $"{landmark.Count}×{measure}";
			(float ox, float oz) = (xMap(plan.Origin.X), zMap(plan.Origin.Z));
			float outwardX = MathF.Abs(x - ox) < 3f ? 1f : MathF.Sign(x - ox);
			float outwardZ = MathF.Abs(z - oz) < 3f ? -1f : MathF.Sign(z - oz);
			string anchor = outwardX < 0 ? "end" : "start";
			svg.AppendLine($"<text class=\"small label\" x=\"{format(x + outwardX * (radius + 4))}\" y=\"{format(z + outwardZ * (radius + 3))}\" text-anchor=\"{anchor}\">{escape(landmark.Kind.ToString())} · {measure}</text>");
		}
	}
}
