using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Petalfell.Core;
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
		string verifyProductionTerrain = null;
		bool auditProductionTerrain = false;
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
			else if (args[i] == "--atlas-preview" && i + 1 < args.Length) atlasPreview = args[++i];
			else if (args[i].StartsWith("--atlas-preview=")) atlasPreview = args[i][16..];
			else if (args[i] == "--atlas-topology-preview" && i + 1 < args.Length) atlasTopologyPreview = args[++i];
			else if (args[i].StartsWith("--atlas-topology-preview=")) atlasTopologyPreview = args[i][25..];
			else if (args[i] == "--atlas-map-preview" && i + 1 < args.Length) atlasMapPreview = args[++i];
			else if (args[i].StartsWith("--atlas-map-preview=")) atlasMapPreview = args[i][20..];
			else if (args[i] == "--atlas-domain" && i + 1 < args.Length) atlasDomainId = args[++i];
			else if (args[i].StartsWith("--atlas-domain=")) atlasDomainId = args[i][15..];
			else if (args[i] == "--verify-production-terrain" && i + 1 < args.Length) verifyProductionTerrain = args[++i];
			else if (args[i].StartsWith("--verify-production-terrain=")) verifyProductionTerrain = args[i][28..];
			else if (args[i] == "--audit-production-terrain") auditProductionTerrain = true;
			else if (args[i] == "--verify-atlas-walking-handoff") verifyAtlasWalkingHandoff = true;
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}

		if (!audit && preview == null && atlasPreview == null && atlasTopologyPreview == null &&
		    atlasMapPreview == null &&
		    compileSector == null && verifySector == null && verifyWilderness == null &&
		    verifyAtlasHandoff == null && verifyProductionTerrain == null &&
		    !auditProductionTerrain &&
		    !verifyAtlasWalkingHandoff &&
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
				if (atlasReport.Valid && verifyProductionTerrain != null)
				{
					(int x, int z) = ParsePoint(verifyProductionTerrain);
					if (x < 0 || z < 0 || x >= map.CanonicalAtlas.Width ||
					    z >= map.CanonicalAtlas.Depth)
						throw new InvalidOperationException(
							$"production terrain address {x},{z} lies outside the atlas");
					AtlasMosaicBounds bounds = AtlasRuntimeHandoff.WindowAround(
						map.CanonicalAtlas, x, z, sectorSpan: 2);

					AtlasPreparedWindow first = ProductionTerrainWindow.Build(map,
						map.DefaultSeed, bounds,
						message => GD.PrintErr($"[production-terrain-verify] {message}"));
					first.Window.Data.Validate(map.CanonicalAtlas.BiomeCatalog.Profiles.Count);
					string firstHash = first.Window.Data.ContentHash();
					AtlasSectorStatistics stats = first.Window.Data.CoreStatistics();
					if (!AtlasRuntimeHandoff.TryResolveLanding(first.Window, x, z,
					    out AtlasRuntimeLanding landing, out string rejection))
						throw new InvalidOperationException(
							$"production terrain {x},{z} has no safe deterministic landing: {rejection}");
					ProductionTraversalAudit traversal = AuditProductionTraversal(
						first.Window.Data, landing);
					if (traversal.ReachableCells < 4096 || traversal.MaxRadius < 48)
						throw new InvalidOperationException(
							$"production terrain landing {landing.GlobalX},{landing.GlobalZ} is locally stranded: " +
							$"{traversal.ReachableCells} cells, radius {traversal.MaxRadius}");

					AtlasPreparedWindow second = ProductionTerrainWindow.Build(map,
						map.DefaultSeed, bounds,
						message => GD.PrintErr($"[production-terrain-verify] {message}"));
					second.Window.Data.Validate(map.CanonicalAtlas.BiomeCatalog.Profiles.Count);
					string secondHash = second.Window.Data.ContentHash();
					if (firstHash != secondHash)
						throw new InvalidOperationException(
							$"production terrain repeat mismatch {firstHash} != {secondHash}");
					if (!first.SiteBuilds.SequenceEqual(second.SiteBuilds))
						throw new InvalidOperationException(
							"production site overlay statistics changed across the repeat build");
					if (first.NaturalFormations != second.NaturalFormations)
						throw new InvalidOperationException(
							"production natural formations changed across the repeat build");
					if (!AtlasRuntimeHandoff.TryResolveLanding(second.Window, x, z,
					    out AtlasRuntimeLanding secondLanding, out string secondRejection) ||
					    secondLanding != landing || secondRejection != rejection)
						throw new InvalidOperationException(
							"production landing changed across the repeat build");

					int atlasColumns = map.CanonicalAtlas.Width / map.CanonicalAtlas.SectorSize;
					int neighbourMinX = bounds.MinSectorX +
						(bounds.MaxSectorX + 1 < atlasColumns ? 1 : -1);
					var neighbourBounds = new AtlasMosaicBounds(neighbourMinX,
						bounds.MinSectorZ, neighbourMinX + bounds.Span - 1,
						bounds.MaxSectorZ);
					AtlasPreparedWindow neighbour = ProductionTerrainWindow.Build(map,
						map.DefaultSeed, neighbourBounds,
						message => GD.PrintErr($"[production-terrain-verify] {message}"));
					ProductionOverlapAudit overlap = CompareProductionOverlap(first,
						neighbour, AtlasRuntimeHandoff.DefaultWalkingTriggerMargin);
					ProductionWalkingTransferAudit walkingTransfer =
						AuditProductionWalkingTransfer(first, neighbour,
							AtlasRuntimeHandoff.DefaultWalkingTriggerMargin);

					GD.Print($"[production-terrain-verify] {x},{z} bounds {bounds} " +
					         $"repeat {firstHash}; landing {landing.GlobalX},{landing.GlobalZ}," +
					         $"{landing.SurfaceY} exact {landing.ExactCell} radius {landing.SearchRadius} " +
					         $"rejection {rejection ?? "none"}; traversal {traversal.ReachableCells} " +
						         $"cells ({traversal.ReachableLand} land/{traversal.ReachableWater} water) " +
						         $"radius {traversal.MaxRadius} extents {traversal.West}/{traversal.East}/" +
						         $"{traversal.North}/{traversal.South} surface " +
						         $"{traversal.MinSurface}..{traversal.MaxSurface} land-surface " +
						         $"{traversal.MinLandSurface}..{traversal.MaxLandSurface}; " +
						         $"land {stats.LandCells} " +
					         $"water {stats.WaterCells} cliffs {stats.CliffCells} shores {stats.ShoreCells} " +
					         $"water-steps {stats.WaterStepEdges} severe {stats.SevereWaterStepEdges} " +
					         $"submerged-dry {stats.SubmergedDryBoundaryEdges} " +
					         $"height {stats.MinHeight}..{stats.MaxHeight}; natural " +
					         $"{first.NaturalFormations.Arches} arches/" +
					         $"{first.NaturalFormations.Voxels} voxels/" +
					         $"{first.NaturalFormations.ManifestHash:x16} first " +
					         $"{first.NaturalFormations.FirstGlobalX}," +
					         $"{first.NaturalFormations.FirstGlobalZ} last " +
					         $"{first.NaturalFormations.LastGlobalX}," +
					         $"{first.NaturalFormations.LastGlobalZ}; overlap " +
					         $"{overlap.SafeCells} safe cells/{overlap.PlacedColumns} " +
					         $"placed columns/{overlap.PlacedVoxels} placed voxels/" +
					         $"{overlap.OverhangColumns} overhang columns/" +
					         $"{overlap.OverhangVoxels} overhang voxels with " +
					         $"{neighbourBounds}; walking transfer " +
					         $"{walkingTransfer.TestedCells} cells, " +
					         $"{walkingTransfer.LegacyRejectedCells} former invisible-wall " +
					         $"cells ({walkingTransfer.LegacyRejectedWater} water/" +
					         $"{walkingTransfer.LegacyRejectedDry} dry); sites " +
					         $"{string.Join(',', first.SiteBuilds.Select(site => site.SiteId))}");
				}
				if (atlasReport.Valid && auditProductionTerrain)
					AuditProductionTerrainAtlas(map);
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
			         verifyAtlasHandoff != null || verifyProductionTerrain != null ||
			         verifyAtlasWalkingHandoff ||
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

	/// <summary>
	/// Measure the same two-block surface graph used by gameplay from one resolved
	/// landing. Wet columns meet at the water plane rather than at their sculpted
	/// beds; this catches locally stranded shelves without rejecting swimmable
	/// lakes merely because their underwater terraces are deep.
	/// </summary>
	private static ProductionTraversalAudit AuditProductionTraversal(
		AtlasSectorData data, AtlasRuntimeLanding landing)
	{
		int width = data.Width, depth = data.Depth;
		int startX = landing.GlobalX - data.OriginX;
		int startZ = landing.GlobalZ - data.OriginZ;
		if (startX < 0 || startZ < 0 || startX >= width || startZ >= depth)
			throw new InvalidOperationException("resolved production landing lies outside its window");

		var seen = new bool[width * depth];
		var queue = new int[width * depth];
		int read = 0, write = 0;
		int start = startZ * width + startX;
		seen[start] = true;
		queue[write++] = start;
		int land = 0, water = 0, maxRadius = 0;
		int minSurface = int.MaxValue, maxSurface = int.MinValue;
		int minLandSurface = int.MaxValue, maxLandSurface = int.MinValue;
		int minX = startX, maxX = startX, minZ = startZ, maxZ = startZ;

		int Surface(int index) => data.Land[index] == 0
			? Terrain.Sea : data.Height[index];
		void Visit(int from, int nx, int nz)
		{
			if (nx < 1 || nz < 1 || nx >= width - 1 || nz >= depth - 1) return;
			int next = nz * width + nx;
			if (seen[next] || Math.Abs(Surface(next) - Surface(from)) > Terrain.Step) return;
			seen[next] = true;
			queue[write++] = next;
		}

		while (read < write)
		{
			int at = queue[read++];
			int cx = at % width, cz = at / width;
			int surface = Surface(at);
			minSurface = Math.Min(minSurface, surface);
			maxSurface = Math.Max(maxSurface, surface);
			if (data.Land[at] == 0) water++;
			else
			{
				land++;
				minLandSurface = Math.Min(minLandSurface, surface);
				maxLandSurface = Math.Max(maxLandSurface, surface);
			}
			maxRadius = Math.Max(maxRadius, Math.Abs(cx - startX) + Math.Abs(cz - startZ));
			minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
			minZ = Math.Min(minZ, cz); maxZ = Math.Max(maxZ, cz);
			Visit(at, cx + 1, cz);
			Visit(at, cx - 1, cz);
			Visit(at, cx, cz + 1);
			Visit(at, cx, cz - 1);
		}

		if (land == 0) minLandSurface = maxLandSurface = Terrain.Sea;
		return new ProductionTraversalAudit(write, land, water, maxRadius,
			startX - minX, maxX - startX, startZ - minZ, maxZ - startZ,
			minSurface, maxSurface, minLandSurface, maxLandSurface);
	}

	private readonly record struct ProductionTraversalAudit(int ReachableCells,
		int ReachableLand, int ReachableWater, int MaxRadius,
		int West, int East, int North, int South,
		int MinSurface, int MaxSurface, int MinLandSurface, int MaxLandSurface);

	/// <summary>
	/// Verify the region that remains playable while a walking handoff swaps two
	/// overlapping two-sector windows. Base terrain needs exact identity only past
	/// the runtime's walking-trigger safety margin; true overhang voxels are compared in
	/// the whole intersection because a high silhouette can remain visible behind
	/// the traveller even after its ground has left the playable core.
	/// </summary>
	private static ProductionOverlapAudit CompareProductionOverlap(
		AtlasPreparedWindow a, AtlasPreparedWindow b, int safeMargin)
	{
		AtlasSectorData ad = a.Window.Data, bd = b.Window.Data;
		var ag = a.Window.Grid;
		var bg = b.Window.Grid;
		int fullMinX = Math.Max(ad.OriginX, bd.OriginX);
		int fullMinZ = Math.Max(ad.OriginZ, bd.OriginZ);
		int fullMaxX = Math.Min(ad.OriginX + ad.Width, bd.OriginX + bd.Width);
		int fullMaxZ = Math.Min(ad.OriginZ + ad.Depth, bd.OriginZ + bd.Depth);
		if (fullMinX >= fullMaxX || fullMinZ >= fullMaxZ)
			throw new InvalidOperationException(
				$"production windows {a.Bounds} and {b.Bounds} do not overlap");

		int safeMinX = Math.Max(ad.OriginX + safeMargin, bd.OriginX + safeMargin);
		int safeMinZ = Math.Max(ad.OriginZ + safeMargin, bd.OriginZ + safeMargin);
		int safeMaxX = Math.Min(ad.OriginX + ad.Width - safeMargin,
			bd.OriginX + bd.Width - safeMargin);
		int safeMaxZ = Math.Min(ad.OriginZ + ad.Depth - safeMargin,
			bd.OriginZ + bd.Depth - safeMargin);
		int safeCells = 0, placedColumns = 0, placedVoxels = 0;

		for (int globalZ = safeMinZ; globalZ < safeMaxZ; globalZ++)
		for (int globalX = safeMinX; globalX < safeMaxX; globalX++)
		{
			int ax = globalX - ad.OriginX, az = globalZ - ad.OriginZ;
			int bx = globalX - bd.OriginX, bz = globalZ - bd.OriginZ;
			int ai = az * ad.Width + ax, bi = bz * bd.Width + bx;
			void Same(int av, int bv, string field)
			{
				if (av != bv) throw new InvalidOperationException(
					$"production overlap {field} mismatch at {globalX},{globalZ}: {av} != {bv}");
			}
			Same(ad.Height[ai], bd.Height[bi], "height");
			Same(ad.WaterSurface[ai], bd.WaterSurface[bi], "water-surface");
			Same(ad.Land[ai], bd.Land[bi], "land");
			Same(ad.Water[ai], bd.Water[bi], "water");
			Same(ad.Hydrology[ai], bd.Hydrology[bi], "hydrology");
			Same(ad.Profile[ai], bd.Profile[bi], "profile");
			Same(ad.SecondaryProfile[ai], bd.SecondaryProfile[bi], "secondary-profile");
			Same(ad.ProfileBlend[ai], bd.ProfileBlend[bi], "profile-blend");
			Same(ad.Surface[ai], bd.Surface[bi], "surface");
			if (ad.Slope[ai] != bd.Slope[bi])
			{
				string Cross(AtlasSectorData data, int localX, int localZ) =>
					$"{data.Height[localZ * data.Width + localX]}/" +
					$"{data.Height[localZ * data.Width + localX - 1]}/" +
					$"{data.Height[localZ * data.Width + localX + 1]}/" +
					$"{data.Height[(localZ - 1) * data.Width + localX]}/" +
					$"{data.Height[(localZ + 1) * data.Width + localX]}";
				string CapCross(VoxelGrid grid, int localX, int localZ) =>
					$"{grid.Cap[localZ * grid.Size + localX]}/" +
					$"{grid.Cap[localZ * grid.Size + localX - 1]}/" +
					$"{grid.Cap[localZ * grid.Size + localX + 1]}/" +
					$"{grid.Cap[(localZ - 1) * grid.Size + localX]}/" +
					$"{grid.Cap[(localZ + 1) * grid.Size + localX]}";
				throw new InvalidOperationException(
					$"production overlap slope mismatch at {globalX},{globalZ}: " +
					$"{ad.Slope[ai]} != {bd.Slope[bi]}; " +
					$"height cross centre/w/e/n/s {Cross(ad, ax, az)} != {Cross(bd, bx, bz)}; " +
					$"cap cross {CapCross(ag, ax, az)} != {CapCross(bg, bx, bz)}");
			}
			Same(ad.Aspect[ai], bd.Aspect[bi], "aspect");
			Same(ad.Curvature[ai], bd.Curvature[bi], "curvature");
			Same(ad.Wetness[ai], bd.Wetness[bi], "wetness");
			Same(ag.Top[ai], bg.Top[bi], "grid-top");
			Same(ag.Cap[ai], bg.Cap[bi], "grid-cap");
			Same(ag.Sub[ai], bg.Sub[bi], "grid-substrate");
			Same(ag.Deep[ai], bg.Deep[bi], "grid-deep");
			Same(ag.Heights[ai], bg.Heights[bi], "grid-height");
			Same(ag.MeshHeightAt(ax, az), bg.MeshHeightAt(bx, bz), "grid-mesh-height");
			int meshTop = ag.MeshHeightAt(ax, az);
			if (meshTop > ag.Top[ai])
			{
				placedColumns++;
				for (int y = ag.Top[ai]; y < meshTop; y++)
				{
					byte av = ag.At(ax, y, az), bv = bg.At(bx, y, bz);
					if (av != bv)
						throw new InvalidOperationException(
							$"production placed voxel mismatch at {globalX},{y},{globalZ}: {av} != {bv}");
					if (av != Palette.AIR) placedVoxels++;
				}
			}
			safeCells++;
		}

		int overhangColumns = 0, overhangVoxels = 0;
		for (int globalZ = fullMinZ; globalZ < fullMaxZ; globalZ++)
		for (int globalX = fullMinX; globalX < fullMaxX; globalX++)
		{
			int ax = globalX - ad.OriginX, az = globalZ - ad.OriginZ;
			int bx = globalX - bd.OriginX, bz = globalZ - bd.OriginZ;
			int aGround = ag.HeightAt(ax, az), bGround = bg.HeightAt(bx, bz);
			int aMesh = ag.MeshHeightAt(ax, az), bMesh = bg.MeshHeightAt(bx, bz);
			if (aMesh <= aGround && bMesh <= bGround) continue;
			if (aGround != bGround || aMesh != bMesh)
				throw new InvalidOperationException(
					$"production overhang bound mismatch at {globalX},{globalZ}: " +
					$"ground {aGround}/{bGround}, mesh {aMesh}/{bMesh}");
			overhangColumns++;
			for (int y = Math.Min(aGround, bGround); y < Math.Max(aMesh, bMesh); y++)
			{
				byte av = ag.At(ax, y, az), bv = bg.At(bx, y, bz);
				if (av != bv)
					throw new InvalidOperationException(
						$"production overhang voxel mismatch at {globalX},{y},{globalZ}: {av} != {bv}");
				if (av != Palette.AIR) overhangVoxels++;
			}
		}
		return new ProductionOverlapAudit(safeCells, placedColumns, placedVoxels,
			overhangColumns, overhangVoxels);
	}

	private readonly record struct ProductionOverlapAudit(int SafeCells,
		int PlacedColumns, int PlacedVoxels, int OverhangColumns, int OverhangVoxels);

	/// <summary>
	/// Exercise the exact moving-window trigger line against two real production
	/// windows. The old teleport-style validator rejected water, terrace edges and
	/// occupied neighbours even when both owners described identical collision;
	/// every such cell used to arm the runtime's invisible boundary clamp.
	/// </summary>
	private static ProductionWalkingTransferAudit AuditProductionWalkingTransfer(
		AtlasPreparedWindow current, AtlasPreparedWindow next, int triggerMargin)
	{
		AtlasSectorData a = current.Window.Data, b = next.Window.Data;
		bool east = b.OriginX > a.OriginX;
		bool west = b.OriginX < a.OriginX;
		if (!east && !west)
			throw new InvalidOperationException(
				$"walking transfer audit expects an east/west neighbour, got " +
				$"{current.Bounds} -> {next.Bounds}");
		int globalX = east ? a.OriginX + a.Width - triggerMargin
			: a.OriginX + triggerMargin;
		int minZ = Math.Max(a.OriginZ, b.OriginZ) + triggerMargin + 2;
		int maxZ = Math.Min(a.OriginZ + a.Depth, b.OriginZ + b.Depth) -
			triggerMargin - 2;
		int tested = 0, legacyRejected = 0, rejectedWater = 0, rejectedDry = 0;
		for (int globalZ = minZ; globalZ < maxZ; globalZ++)
		{
			int ax = globalX - a.OriginX, az = globalZ - a.OriginZ;
			int ai = az * a.Width + ax;
			bool water = a.WaterSurface[ai] > 0;
			float playerY = water
				? a.WaterSurface[ai] - .85f
				: current.Window.Grid.HeightAt(ax, az) + .2f;
			if (!AtlasRuntimeHandoff.TryResolveWalkingTransfer(current.Window,
			    next.Window, globalX, globalZ, playerY, out _, out string rejection))
				throw new InvalidOperationException(
					$"walking transfer {current.Bounds}->{next.Bounds} rejected " +
					$"identical trigger cell {globalX},{globalZ}: {rejection}");
			tested++;
			if (!AtlasRuntimeHandoff.TryResolveExactLanding(current.Window,
			    globalX, globalZ, out _, out _))
			{
				legacyRejected++;
				if (water) rejectedWater++;
				else rejectedDry++;
			}
		}
		if (tested == 0)
			throw new InvalidOperationException("walking transfer audit tested no cells");
		return new ProductionWalkingTransferAudit(tested, legacyRejected,
			rejectedWater, rejectedDry);
	}

	private readonly record struct ProductionWalkingTransferAudit(int TestedCells,
		int LegacyRejectedCells, int LegacyRejectedWater, int LegacyRejectedDry);

	/// <summary>
	/// Build every two-sector window the normal runtime can own exactly once. A
	/// Walking-margin-sized fingerprint lattice turns the 165-window cross-product into a
	/// bounded audit: chunks are compared only when they are beyond the handoff
	/// margin in every dimension where their owners differ. Overhang fingerprints
	/// compare everywhere because a high roof remains visible after its ground has
	/// left the playable band. This proves the moving runtime directly; it neither
	/// writes compiler artifacts nor allocates the continent as one heightfield.
	/// </summary>
	private static void AuditProductionTerrainAtlas(MapDefinition map)
	{
		WorldAtlasDefinition atlas = map.CanonicalAtlas;
		const int span = 2;
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		int chunkSize = AtlasRuntimeHandoff.DefaultWalkingTriggerMargin;
		if (atlas.SectorSize % chunkSize != 0)
			throw new InvalidOperationException(
				$"production audit chunk {chunkSize} does not divide sector {atlas.SectorSize}");
		int chunksPerSector = atlas.SectorSize / chunkSize;
		int chunksPerWindow = span * chunksPerSector;
		int atlasChunkColumns = atlas.Width / chunkSize;
		int atlasChunkRows = atlas.Depth / chunkSize;
		int expectedWindows = (columns - span + 1) * (rows - span + 1);
		var owners = new Dictionary<int, List<ProductionChunkOwner>>(
			atlasChunkColumns * atlasChunkRows);
		long startMs = (long)Time.GetTicksMsec();
		int windows = 0, fingerprints = 0, terrainComparisons = 0;
		int overhangComparisons = 0, observedOverhangColumns = 0;
		int observedOverhangVoxels = 0, observedSites = 0;
		int minHeight = int.MaxValue, maxHeight = int.MinValue;
		long observedCells = 0;

		for (int minSectorZ = 0; minSectorZ <= rows - span; minSectorZ++)
		{
			for (int minSectorX = 0; minSectorX <= columns - span; minSectorX++)
			{
				var bounds = new AtlasMosaicBounds(minSectorX, minSectorZ,
					minSectorX + span - 1, minSectorZ + span - 1);
				AtlasPreparedWindow prepared = ProductionTerrainWindow.Build(map,
					map.DefaultSeed, bounds,
					message => GD.PrintErr($"[production-atlas-audit] {message}"));
				AtlasSectorData data = prepared.Window.Data;
				data.Validate(atlas.BiomeCatalog.Profiles.Count);
				AtlasSectorStatistics stats = data.CoreStatistics();
				if (stats.WaterStepEdges != 0 || stats.SevereWaterStepEdges != 0 ||
				    stats.SubmergedDryBoundaryEdges != 0)
					throw new InvalidOperationException(
						$"production window {bounds} violates water invariants: " +
						$"steps {stats.WaterStepEdges}, severe {stats.SevereWaterStepEdges}, " +
						$"submerged-dry {stats.SubmergedDryBoundaryEdges}");
				minHeight = Math.Min(minHeight, stats.MinHeight);
				maxHeight = Math.Max(maxHeight, stats.MaxHeight);
				observedCells += (long)data.Width * data.Depth;
				observedSites += prepared.SiteBuilds.Count;

				for (int localChunkZ = 0; localChunkZ < chunksPerWindow; localChunkZ++)
				for (int localChunkX = 0; localChunkX < chunksPerWindow; localChunkX++)
				{
					int globalChunkX = minSectorX * chunksPerSector + localChunkX;
					int globalChunkZ = minSectorZ * chunksPerSector + localChunkZ;
					int key = globalChunkZ * atlasChunkColumns + globalChunkX;
					ProductionChunkFingerprint fingerprint = FingerprintProductionChunk(
						prepared, localChunkX, localChunkZ, chunkSize);
					if (!owners.TryGetValue(key, out List<ProductionChunkOwner> existing))
						owners[key] = existing = new List<ProductionChunkOwner>(4);
					foreach (ProductionChunkOwner previous in existing)
					{
						if (OwnersSharePlayableChunk(previous.MinSectorX,
						    previous.MinSectorZ, minSectorX, minSectorZ,
						    globalChunkX, globalChunkZ, chunksPerSector,
						    chunksPerWindow))
						{
							terrainComparisons++;
							if (previous.Fingerprint.TerrainHash != fingerprint.TerrainHash)
								throw new InvalidOperationException(
									$"production terrain chunk {globalChunkX},{globalChunkZ} " +
									$"mismatch between windows {previous.MinSectorX}," +
									$"{previous.MinSectorZ} and {minSectorX},{minSectorZ}: " +
									$"{previous.Fingerprint.TerrainHash:x16} != " +
									$"{fingerprint.TerrainHash:x16}");
						}
						overhangComparisons++;
						if (previous.Fingerprint.OverhangHash != fingerprint.OverhangHash)
							throw new InvalidOperationException(
								$"production overhang chunk {globalChunkX},{globalChunkZ} " +
								$"mismatch between windows {previous.MinSectorX}," +
								$"{previous.MinSectorZ} and {minSectorX},{minSectorZ}: " +
								$"{previous.Fingerprint.OverhangHash:x16} != " +
								$"{fingerprint.OverhangHash:x16}");
					}
					existing.Add(new ProductionChunkOwner(minSectorX, minSectorZ,
						fingerprint));
					fingerprints++;
					observedOverhangColumns += fingerprint.OverhangColumns;
					observedOverhangVoxels += fingerprint.OverhangVoxels;
				}
				windows++;
			}

			GD.Print($"[production-atlas-audit] row {minSectorZ + 1}/" +
			         $"{rows - span + 1}: {windows}/{expectedWindows} windows, " +
			         $"{terrainComparisons} safe chunk comparisons");
			// Terrain windows own several continent-scale local arrays. Nothing from a
			// completed row survives except compact fingerprints, so collect here rather
			// than letting the authoring process retain generations of dead images.
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		int expectedChunks = atlasChunkColumns * atlasChunkRows;
		if (windows != expectedWindows || owners.Count != expectedChunks)
			throw new InvalidOperationException(
				$"production audit coverage {windows}/{expectedWindows} windows and " +
				$"{owners.Count}/{expectedChunks} chunks");
		ulong manifest = 1469598103934665603UL;
		foreach (int key in owners.Keys.OrderBy(key => key))
		{
			ProductionChunkOwner canonical = owners[key][0];
			MixFingerprint(ref manifest, (ulong)(uint)key);
			MixFingerprint(ref manifest, canonical.Fingerprint.TerrainHash);
			MixFingerprint(ref manifest, canonical.Fingerprint.OverhangHash);
		}
		long elapsedMs = (long)Time.GetTicksMsec() - startMs;
		GD.Print($"[production-atlas-audit] PASS {windows} windows/" +
		         $"{owners.Count} global chunks/{fingerprints} fingerprints; " +
		         $"{terrainComparisons} safe terrain and {overhangComparisons} " +
		         $"all-overlap overhang comparisons; observed " +
		         $"{observedCells} cells/{observedSites} site builds/" +
		         $"{observedOverhangColumns} overhang columns/" +
		         $"{observedOverhangVoxels} overhang voxels; height " +
		         $"{minHeight}..{maxHeight}; manifest {manifest:x16}; " +
		         $"{elapsedMs / 1000f:0.0}s");
	}

	private static ProductionChunkFingerprint FingerprintProductionChunk(
		AtlasPreparedWindow prepared, int localChunkX, int localChunkZ, int chunkSize)
	{
		AtlasSectorData data = prepared.Window.Data;
		VoxelGrid grid = prepared.Window.Grid;
		int x0 = localChunkX * chunkSize, z0 = localChunkZ * chunkSize;
		int globalChunkX = (data.OriginX + x0) / chunkSize;
		int globalChunkZ = (data.OriginZ + z0) / chunkSize;
		ulong terrain = 1469598103934665603UL;
		ulong overhang = 1099511628211UL;
		MixFingerprint(ref terrain, (ulong)(uint)globalChunkX);
		MixFingerprint(ref terrain, (ulong)(uint)globalChunkZ);
		MixFingerprint(ref overhang, (ulong)(uint)globalChunkX);
		MixFingerprint(ref overhang, (ulong)(uint)globalChunkZ);
		int overhangColumns = 0, overhangVoxels = 0;

		for (int z = z0; z < z0 + chunkSize; z++)
		for (int x = x0; x < x0 + chunkSize; x++)
		{
			int index = z * data.Width + x;
			ulong dataA = data.Height[index] |
				((ulong)data.WaterSurface[index] << 16) |
				((ulong)data.Land[index] << 32) |
				((ulong)data.Water[index] << 40) |
				((ulong)data.Hydrology[index] << 48) |
				((ulong)data.Profile[index] << 56);
			ulong dataB = data.SecondaryProfile[index] |
				((ulong)data.ProfileBlend[index] << 8) |
				((ulong)data.Surface[index] << 16) |
				((ulong)data.Slope[index] << 24) |
				((ulong)data.Aspect[index] << 32) |
				((ulong)data.Curvature[index] << 40) |
				((ulong)data.Wetness[index] << 48) |
				((ulong)grid.Cap[index] << 56);
			int meshTop = grid.MeshHeightAt(x, z);
			ulong gridData = (ushort)grid.Top[index] |
				((ulong)(ushort)grid.Heights[index] << 16) |
				((ulong)(ushort)meshTop << 32) |
				((ulong)grid.Sub[index] << 48) |
				((ulong)grid.Deep[index] << 56);
			MixFingerprint(ref terrain, dataA);
			MixFingerprint(ref terrain, dataB);
			MixFingerprint(ref terrain, gridData);
			for (int y = grid.Top[index]; y < meshTop; y++)
				MixFingerprint(ref terrain,
					((ulong)(uint)y << 8) | grid.At(x, y, z));

			int ground = grid.HeightAt(x, z);
			if (meshTop <= ground) continue;
			overhangColumns++;
			MixFingerprint(ref overhang,
				((ulong)(uint)(x + data.OriginX) << 32) |
				(uint)(z + data.OriginZ));
			MixFingerprint(ref overhang,
				((ulong)(ushort)ground << 16) | (ushort)meshTop);
			for (int y = ground; y < meshTop; y++)
			{
				byte material = grid.At(x, y, z);
				MixFingerprint(ref overhang,
					((ulong)(uint)y << 8) | material);
				if (material != Palette.AIR) overhangVoxels++;
			}
		}
		return new ProductionChunkFingerprint(terrain, overhang,
			overhangColumns, overhangVoxels);
	}

	private static bool OwnersSharePlayableChunk(int aSectorX, int aSectorZ,
		int bSectorX, int bSectorZ, int globalChunkX, int globalChunkZ,
		int chunksPerSector, int chunksPerWindow)
	{
		bool SafeDimension(int globalChunk, int aSector, int bSector)
		{
			if (aSector == bSector) return true;
			int aLocal = globalChunk - aSector * chunksPerSector;
			int bLocal = globalChunk - bSector * chunksPerSector;
			return aLocal >= 1 && aLocal < chunksPerWindow - 1 &&
			       bLocal >= 1 && bLocal < chunksPerWindow - 1;
		}
		return SafeDimension(globalChunkX, aSectorX, bSectorX) &&
		       SafeDimension(globalChunkZ, aSectorZ, bSectorZ);
	}

	private static void MixFingerprint(ref ulong hash, ulong value)
	{
		unchecked
		{
			hash ^= value;
			hash *= 1099511628211UL;
			hash ^= hash >> 32;
		}
	}

	private readonly record struct ProductionChunkFingerprint(ulong TerrainHash,
		ulong OverhangHash, int OverhangColumns, int OverhangVoxels);

	private readonly record struct ProductionChunkOwner(int MinSectorX,
		int MinSectorZ, ProductionChunkFingerprint Fingerprint);

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
