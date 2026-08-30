using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Headless, resumable orchestration for the complete production-atlas sector
/// set. The sector compiler continues to own terrain semantics; this class owns
/// only cache lifecycle, atlas-wide evidence and compact review composites.
/// </summary>
public static class AtlasBatchAuthoring
{
	private const int ManifestVersion = 1;
	private const int SevereDropThreshold = 8;
	private const string ManifestFileName = "atlas-manifest.json";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		WriteIndented = false,
	};

	public static AtlasBatchCompileResult Compile(WorldAtlasDefinition atlas,
		AtlasSectorCompiler compiler, string outputDirectory, int apron)
	{
		ValidateBatchContract(atlas, apron);
		EnsureDirectory(outputDirectory);
		int sectorsX = atlas.Width / atlas.SectorSize;
		int sectorsZ = atlas.Depth / atlas.SectorSize;
		int rebuilt = 0, reused = 0;
		var compositor = new AtlasCompositeAccumulator(atlas);
		var sectorEntries = new List<AtlasBatchSectorManifest>(sectorsX * sectorsZ);

		for (int sz = 0; sz < sectorsZ; sz++)
		{
			for (int sx = 0; sx < sectorsX; sx++)
			{
				string artifactPath = ArtifactPath(outputDirectory, sx, sz);
				AtlasSectorData data;
				string disposition;
				if (TryReadCurrentArtifact(compiler, artifactPath, sx, sz, apron,
				    out data, out string staleReason))
				{
					reused++;
					disposition = "reused";
				}
				else
				{
					data = compiler.Compile(sx, sz, apron);
					WriteArtifactAtomically(compiler, data, artifactPath);
					rebuilt++;
					disposition = "rebuilt";
					if (!string.IsNullOrWhiteSpace(staleReason))
						GD.Print($"[atlas-compile] {sx},{sz} cache refresh: {staleReason}");
				}

				string coreHash = CoreHash(data);
				string artifactHash = FileSha256(artifactPath);
				sectorEntries.Add(new AtlasBatchSectorManifest
				{
					X = sx,
					Z = sz,
					Artifact = Path.GetFileName(ProjectSettings.GlobalizePath(artifactPath)),
					CoreHash = coreHash,
					ArtifactSha256 = artifactHash,
				});
				compositor.Add(data);
				GD.Print($"[atlas-compile] {sx},{sz} {disposition} core {coreHash[..12]}");
			}
		}

		List<AtlasBatchCompositeManifest> composites = compositor.Write(outputDirectory);
		var manifest = BuildManifest(atlas, compiler.SourceFingerprint, apron,
			compositor.BlocksPerPixel, sectorEntries, composites);
		string manifestPath = JoinPath(outputDirectory, ManifestFileName);
		WriteTextAtomically(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
		string manifestHash = FileSha256(manifestPath);
		AtlasHydrologyAuditResult hydrology = AuditHydrology(
			atlas, compiler, outputDirectory, apron);
		RequireHydrologyQuality(hydrology);
		GD.Print($"[atlas-compile] complete {sectorsX}x{sectorsZ} sectors; " +
		         $"rebuilt {rebuilt}, reused {reused}, manifest {manifestHash}");
		GD.Print($"[atlas-manifest] {ProjectSettings.GlobalizePath(manifestPath)}");
		return new AtlasBatchCompileResult(rebuilt, reused, manifestHash);
	}

	public static AtlasBatchVerifyResult Verify(WorldAtlasDefinition atlas,
		AtlasSectorCompiler compiler, string outputDirectory, int apron)
	{
		ValidateBatchContract(atlas, apron);
		string manifestPath = JoinPath(outputDirectory, ManifestFileName);
		AtlasBatchManifest manifest = ReadManifest(manifestPath);
		ValidateManifest(manifest, atlas, compiler.SourceFingerprint, apron);
		AtlasHydrologyAuditResult hydrology = AuditHydrology(
			atlas, compiler, outputDirectory, apron);
		RequireHydrologyQuality(hydrology);
		var expected = manifest.Sectors.ToDictionary(entry => (entry.X, entry.Z));
		int sectorsX = atlas.Width / atlas.SectorSize;
		int sectorsZ = atlas.Depth / atlas.SectorSize;
		long deterministicCells = 0;
		long horizontalOverlapCells = 0;
		long verticalOverlapCells = 0;
		int horizontalSeams = 0, verticalSeams = 0;

		for (int sz = 0; sz < sectorsZ; sz++)
		{
			var row = new AtlasSectorData[sectorsX];
			for (int sx = 0; sx < sectorsX; sx++)
			{
				string artifactPath = ArtifactPath(outputDirectory, sx, sz);
				AtlasSectorData cached = compiler.ReadArtifact(artifactPath);
				ValidateArtifactAddress(cached, sx, sz, apron, artifactPath);
				AtlasBatchSectorManifest entry = expected[(sx, sz)];
				string coreHash = CoreHash(cached);
				if (!string.Equals(coreHash, entry.CoreHash, StringComparison.Ordinal))
					throw new InvalidDataException(
						$"sector {sx},{sz} core hash {coreHash} does not match manifest {entry.CoreHash}");
				string artifactHash = FileSha256(artifactPath);
				if (!string.Equals(artifactHash, entry.ArtifactSha256, StringComparison.Ordinal))
					throw new InvalidDataException(
						$"sector {sx},{sz} artifact hash {artifactHash} does not match manifest {entry.ArtifactSha256}");

				// The cached artifact is one independent build. Comparing it to one
				// fresh build proves the complete apron-bearing sector is repeatable
				// without tripling every build through compiler.Verify.
				AtlasSectorData fresh = compiler.Compile(sx, sz, apron);
				deterministicCells += CompareExact(cached, fresh, $"sector {sx},{sz} repeat build");
				row[sx] = cached;
				if (sx > 0)
				{
					horizontalOverlapCells += CompareOverlap(row[sx - 1], cached, "horizontal");
					horizontalSeams++;
				}
				GD.Print($"[atlas-verify] {sx},{sz} deterministic core {coreHash[..12]}");
			}

			if (sz + 1 < sectorsZ)
			{
				for (int sx = 0; sx < sectorsX; sx++)
				{
					string southPath = ArtifactPath(outputDirectory, sx, sz + 1);
					AtlasSectorData south = compiler.ReadArtifact(southPath);
					ValidateArtifactAddress(south, sx, sz + 1, apron, southPath);
					verticalOverlapCells += CompareOverlap(row[sx], south, "vertical");
					verticalSeams++;
				}
			}
		}

		foreach (AtlasBatchCompositeManifest composite in manifest.Composites)
		{
			string path = JoinPath(outputDirectory, composite.File);
			string hash = FileSha256(path);
			if (!string.Equals(hash, composite.Sha256, StringComparison.Ordinal))
				throw new InvalidDataException(
					$"atlas composite '{composite.File}' hash {hash} does not match manifest {composite.Sha256}");
		}

		GD.Print($"[atlas-verify] complete: {manifest.Sectors.Count} deterministic sectors, " +
		         $"{horizontalSeams} horizontal seams/{horizontalOverlapCells} cells, " +
		         $"{verticalSeams} vertical seams/{verticalOverlapCells} cells, " +
		         $"{deterministicCells} repeat-build cells");
		return new AtlasBatchVerifyResult(manifest.Sectors.Count, horizontalSeams, verticalSeams,
			deterministicCells, horizontalOverlapCells, verticalOverlapCells);
	}

	/// <summary>
	/// Reads only the manifest-backed cache and enumerates each atlas adjacency
	/// exactly once. This deliberately does not rebuild sectors: it is the cheap
	/// whole-continent quality gate that must run before the much more expensive
	/// deterministic repeat pass.
	/// </summary>
	public static AtlasHydrologyAuditResult AuditHydrology(WorldAtlasDefinition atlas,
		AtlasSectorCompiler compiler, string outputDirectory, int apron)
	{
		ValidateBatchContract(atlas, apron);
		string manifestPath = JoinPath(outputDirectory, ManifestFileName);
		AtlasBatchManifest manifest = ReadManifest(manifestPath);
		ValidateManifest(manifest, atlas, compiler.SourceFingerprint, apron);
		var expected = manifest.Sectors.ToDictionary(entry => (entry.X, entry.Z));
		int sectorsX = atlas.Width / atlas.SectorSize;
		int sectorsZ = atlas.Depth / atlas.SectorSize;
		var audit = new AtlasHydrologyAuditBuilder();

		for (int sz = 0; sz < sectorsZ; sz++)
		{
			var row = new AtlasSectorData[sectorsX];
			for (int sx = 0; sx < sectorsX; sx++)
			{
				string artifactPath = ArtifactPath(outputDirectory, sx, sz);
				AtlasSectorData data = compiler.ReadArtifact(artifactPath);
				ValidateArtifactAddress(data, sx, sz, apron, artifactPath);
				AtlasBatchSectorManifest entry = expected[(sx, sz)];
				string coreHash = CoreHash(data);
				if (!string.Equals(coreHash, entry.CoreHash, StringComparison.Ordinal))
					throw new InvalidDataException(
						$"sector {sx},{sz} core hash {coreHash} does not match manifest {entry.CoreHash}");
				string artifactHash = FileSha256(artifactPath);
				if (!string.Equals(artifactHash, entry.ArtifactSha256, StringComparison.Ordinal))
					throw new InvalidDataException(
						$"sector {sx},{sz} artifact hash {artifactHash} does not match manifest {entry.ArtifactSha256}");
				row[sx] = data;
				MeasureHydrology(data, atlas.Width, atlas.Depth, audit);
				if (sx > 0)
				{
					audit.HorizontalSeams++;
					audit.SeamCells += CompareOverlapForAudit(
						row[sx - 1], data, "horizontal", audit);
				}
			}

			if (sz + 1 >= sectorsZ) continue;
			for (int sx = 0; sx < sectorsX; sx++)
			{
				string southPath = ArtifactPath(outputDirectory, sx, sz + 1);
				AtlasSectorData south = compiler.ReadArtifact(southPath);
				ValidateArtifactAddress(south, sx, sz + 1, apron, southPath);
				audit.VerticalSeams++;
				audit.SeamCells += CompareOverlapForAudit(
					row[sx], south, "vertical", audit);
			}
		}

		AtlasHydrologyAuditResult result = audit.ToResult(manifest.Sectors.Count);
		GD.Print($"[atlas-hydrology] complete: {result.SectorCount} cached sectors, " +
		         $"{result.WetWetEdges} wet/wet edges, {result.WaterStepEdges} stepped, " +
		         $"{result.SevereWaterStepEdges} severe >1, " +
		         $"max {result.MaxWaterStep}@{result.MaxWaterStepX},{result.MaxWaterStepZ}; " +
		         $"submerged-dry {result.SubmergedDryBoundaryEdges}, " +
		         $"max-depth {result.MaxSubmergedDryDepth}@{result.MaxSubmergedDryX},{result.MaxSubmergedDryZ}; " +
		         $"cross-sector invariant {result.CrossSectorInvariantViolations}; " +
		         $"seams {result.HorizontalSeams} horizontal/{result.VerticalSeams} vertical, " +
		         $"{result.SeamCells} overlap cells, mismatches {result.SeamMismatches}");
		foreach (string violation in result.Violations)
			GD.PrintErr($"[atlas-hydrology-violation] {violation}");
		return result;
	}

	public static void RequireHydrologyQuality(AtlasHydrologyAuditResult audit)
	{
		if (audit.SevereWaterStepEdges == 0 &&
		    audit.SubmergedDryBoundaryEdges == 0 &&
		    audit.CrossSectorInvariantViolations == 0 &&
		    audit.SeamMismatches == 0) return;
		throw new InvalidDataException(
			$"atlas hydrology quality gate failed: severe water steps " +
			$"{audit.SevereWaterStepEdges}, submerged dry boundaries " +
			$"{audit.SubmergedDryBoundaryEdges}, cross-sector invariant violations " +
			$"{audit.CrossSectorInvariantViolations}, seam mismatches {audit.SeamMismatches}");
	}

	private static AtlasBatchManifest BuildManifest(WorldAtlasDefinition atlas,
		string sourceFingerprint, int apron, int compositeBlocksPerPixel,
		List<AtlasBatchSectorManifest> sectors, List<AtlasBatchCompositeManifest> composites)
	{
		var colours = ProfileColours(atlas);
		return new AtlasBatchManifest
		{
			Version = ManifestVersion,
			CompilerVersion = AtlasSectorCompiler.CompilerVersion,
			SourceFingerprint = sourceFingerprint,
			AtlasId = atlas.Id,
			Width = atlas.Width,
			Depth = atlas.Depth,
			Height = atlas.Height,
			SectorSize = atlas.SectorSize,
			SectorColumns = atlas.Width / atlas.SectorSize,
			SectorRows = atlas.Depth / atlas.SectorSize,
			Apron = apron,
			CompositeBlocksPerPixel = compositeBlocksPerPixel,
			SevereDropThreshold = SevereDropThreshold,
			CliffCoverageSource = "surface:Cliff",
			DropMetricSource = "slope",
			Profiles = atlas.BiomeCatalog.Profiles.Select((profile, index) =>
				new AtlasBatchProfileManifest
				{
					Index = index,
					Id = profile.Id,
					Colour = $"#{colours[index].r:x2}{colours[index].g:x2}{colours[index].b:x2}",
				}).ToList(),
			Sectors = sectors,
			Composites = composites,
		};
	}

	private static AtlasBatchManifest ReadManifest(string resourcePath)
	{
		string absolute = ProjectSettings.GlobalizePath(resourcePath);
		if (!File.Exists(absolute))
			throw new FileNotFoundException(
				$"atlas manifest '{resourcePath}' does not exist; run compile-atlas first", absolute);
		return JsonSerializer.Deserialize<AtlasBatchManifest>(File.ReadAllText(absolute), JsonOptions)
		       ?? throw new InvalidDataException($"atlas manifest '{resourcePath}' was empty");
	}

	private static void ValidateManifest(AtlasBatchManifest manifest, WorldAtlasDefinition atlas,
		string sourceFingerprint, int apron)
	{
		if (manifest.Version != ManifestVersion)
			throw new InvalidDataException(
				$"atlas manifest version {manifest.Version}, expected {ManifestVersion}");
		if (manifest.CompilerVersion != AtlasSectorCompiler.CompilerVersion)
			throw new InvalidDataException(
				$"atlas manifest compiler {manifest.CompilerVersion}, expected {AtlasSectorCompiler.CompilerVersion}");
		if (!string.Equals(manifest.SourceFingerprint, sourceFingerprint, StringComparison.Ordinal))
			throw new InvalidDataException("atlas manifest is stale against the accepted atlas sources");
		int sectorsX = atlas.Width / atlas.SectorSize;
		int sectorsZ = atlas.Depth / atlas.SectorSize;
		if (manifest.AtlasId != atlas.Id || manifest.Width != atlas.Width || manifest.Depth != atlas.Depth ||
		    manifest.Height != atlas.Height || manifest.SectorSize != atlas.SectorSize ||
		    manifest.SectorColumns != sectorsX || manifest.SectorRows != sectorsZ || manifest.Apron != apron ||
		    manifest.CompositeBlocksPerPixel != atlas.ChunkSize ||
		    manifest.SevereDropThreshold != SevereDropThreshold ||
		    manifest.CliffCoverageSource != "surface:Cliff" || manifest.DropMetricSource != "slope")
			throw new InvalidDataException("atlas manifest metadata does not match the current atlas batch contract");
		if (manifest.Profiles == null || manifest.Sectors == null || manifest.Composites == null)
			throw new InvalidDataException("atlas manifest omits profiles, sectors or composites");
		if (manifest.Profiles.Count != atlas.BiomeCatalog.Profiles.Count)
			throw new InvalidDataException(
				$"atlas manifest has {manifest.Profiles.Count} profile entries, expected {atlas.BiomeCatalog.Profiles.Count}");
		for (int i = 0; i < manifest.Profiles.Count; i++)
			if (manifest.Profiles[i].Index != i || manifest.Profiles[i].Id != atlas.BiomeCatalog.Profiles[i].Id)
				throw new InvalidDataException($"atlas manifest profile {i} does not match the biome catalog");
		if (manifest.Sectors.Count != sectorsX * sectorsZ)
			throw new InvalidDataException(
				$"atlas manifest has {manifest.Sectors.Count} sectors, expected {sectorsX * sectorsZ}");
		var addresses = new HashSet<(int x, int z)>();
		foreach (AtlasBatchSectorManifest sector in manifest.Sectors)
		{
			if (sector.X < 0 || sector.X >= sectorsX || sector.Z < 0 || sector.Z >= sectorsZ)
				throw new InvalidDataException($"atlas manifest sector {sector.X},{sector.Z} lies outside the atlas");
			if (!addresses.Add((sector.X, sector.Z)))
				throw new InvalidDataException($"atlas manifest repeats sector {sector.X},{sector.Z}");
			string expectedArtifact = $"sector-{sector.X}-{sector.Z}.pfs";
			if (!string.Equals(sector.Artifact, expectedArtifact, StringComparison.Ordinal))
				throw new InvalidDataException(
					$"atlas manifest sector {sector.X},{sector.Z} artifact is '{sector.Artifact}', expected '{expectedArtifact}'");
		}
		if (manifest.Composites.Count != 4)
			throw new InvalidDataException($"atlas manifest has {manifest.Composites.Count} composites, expected 4");
		var compositeKinds = manifest.Composites.Select(composite => composite.Kind).ToHashSet(StringComparer.Ordinal);
		string[] requiredComposites = { "height", "hydrology", "profile", "cliffDrop" };
		if (requiredComposites.Any(kind => !compositeKinds.Contains(kind)))
			throw new InvalidDataException("atlas manifest is missing one or more required composites");
	}

	private static void ValidateBatchContract(WorldAtlasDefinition atlas, int apron)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		if (atlas.Width % atlas.SectorSize != 0 || atlas.Depth % atlas.SectorSize != 0)
			throw new InvalidOperationException("atlas extent is not an exact sector grid");
		if (apron <= 0 || apron > atlas.SectorSize / 2)
			throw new ArgumentOutOfRangeException(nameof(apron),
				"atlas batch verification requires an apron above zero and no larger than half a sector");
	}

	private static bool TryReadCurrentArtifact(AtlasSectorCompiler compiler, string artifactPath,
		int sectorX, int sectorZ, int apron, out AtlasSectorData data, out string staleReason)
	{
		try
		{
			data = compiler.ReadArtifact(artifactPath);
			ValidateArtifactAddress(data, sectorX, sectorZ, apron, artifactPath);
			staleReason = "";
			return true;
		}
		catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or EndOfStreamException or IOException)
		{
			data = null;
			staleReason = ex.Message;
			return false;
		}
	}

	private static void ValidateArtifactAddress(AtlasSectorData data, int sectorX, int sectorZ,
		int apron, string resourcePath)
	{
		if (data.SectorX != sectorX || data.SectorZ != sectorZ)
			throw new InvalidDataException(
				$"sector artifact '{resourcePath}' contains {data.SectorX},{data.SectorZ}, expected {sectorX},{sectorZ}");
		if (data.Apron != apron)
			throw new InvalidDataException(
				$"sector artifact '{resourcePath}' uses apron {data.Apron}, expected {apron}");
	}

	private static long CompareExact(AtlasSectorData a, AtlasSectorData b, string label)
	{
		if (a.SectorX != b.SectorX || a.SectorZ != b.SectorZ || a.OriginX != b.OriginX ||
		    a.OriginZ != b.OriginZ || a.CoreSize != b.CoreSize || a.Apron != b.Apron ||
		    a.Width != b.Width || a.Depth != b.Depth || a.WorldHeight != b.WorldHeight ||
		    a.SeaLevel != b.SeaLevel || a.SourceFingerprint != b.SourceFingerprint)
			throw new InvalidOperationException($"{label} metadata changed");
		for (int i = 0; i < a.CellCount; i++)
			CompareCell(a, i, b, i, label, a.OriginX + i % a.Width, a.OriginZ + i / a.Width);
		return a.CellCount;
	}

	private static int CompareOverlap(AtlasSectorData a, AtlasSectorData b, string edge)
	{
		int minX = Math.Max(a.OriginX, b.OriginX);
		int minZ = Math.Max(a.OriginZ, b.OriginZ);
		int maxX = Math.Min(a.OriginX + a.Width, b.OriginX + b.Width);
		int maxZ = Math.Min(a.OriginZ + a.Depth, b.OriginZ + b.Depth);
		if (minX >= maxX || minZ >= maxZ)
			throw new InvalidOperationException(
				$"{edge} sectors {a.SectorX},{a.SectorZ} and {b.SectorX},{b.SectorZ} have no overlap");
		int compared = 0;
		for (int z = minZ; z < maxZ; z++)
		for (int x = minX; x < maxX; x++)
		{
			int ai = (z - a.OriginZ) * a.Width + x - a.OriginX;
			int bi = (z - b.OriginZ) * b.Width + x - b.OriginX;
			CompareCell(a, ai, b, bi, $"{edge} seam", x, z);
			compared++;
		}
		return compared;
	}

	private static void CompareCell(AtlasSectorData a, int ai, AtlasSectorData b, int bi,
		string label, int globalX, int globalZ)
	{
		string field = MismatchedField(a, ai, b, bi);
		if (field != null)
			throw new InvalidOperationException($"{label} {field} mismatch at global {globalX},{globalZ}");
	}

	private static string MismatchedField(AtlasSectorData a, int ai, AtlasSectorData b, int bi) =>
		a.Height[ai] != b.Height[bi] ? "height"
			: a.WaterSurface[ai] != b.WaterSurface[bi] ? "water-surface"
			: a.Land[ai] != b.Land[bi] ? "land"
			: a.Water[ai] != b.Water[bi] ? "water"
			: a.Hydrology[ai] != b.Hydrology[bi] ? "hydrology"
			: a.Profile[ai] != b.Profile[bi] ? "profile"
			: a.SecondaryProfile[ai] != b.SecondaryProfile[bi] ? "secondary-profile"
			: a.ProfileBlend[ai] != b.ProfileBlend[bi] ? "profile-blend"
			: a.Surface[ai] != b.Surface[bi] ? "surface"
			: a.Slope[ai] != b.Slope[bi] ? "slope"
			: a.Aspect[ai] != b.Aspect[bi] ? "aspect"
			: a.Curvature[ai] != b.Curvature[bi] ? "curvature"
			: a.Wetness[ai] != b.Wetness[bi] ? "wetness" : null;

	private static long CompareOverlapForAudit(AtlasSectorData a, AtlasSectorData b,
		string edge, AtlasHydrologyAuditBuilder audit)
	{
		int minX = Math.Max(a.OriginX, b.OriginX);
		int minZ = Math.Max(a.OriginZ, b.OriginZ);
		int maxX = Math.Min(a.OriginX + a.Width, b.OriginX + b.Width);
		int maxZ = Math.Min(a.OriginZ + a.Depth, b.OriginZ + b.Depth);
		if (minX >= maxX || minZ >= maxZ)
			throw new InvalidOperationException(
				$"{edge} sectors {a.SectorX},{a.SectorZ} and {b.SectorX},{b.SectorZ} have no overlap");
		long compared = 0;
		for (int z = minZ; z < maxZ; z++)
		for (int x = minX; x < maxX; x++)
		{
			int ai = (z - a.OriginZ) * a.Width + x - a.OriginX;
			int bi = (z - b.OriginZ) * b.Width + x - b.OriginX;
			string field = MismatchedField(a, ai, b, bi);
			if (field != null)
			{
				audit.SeamMismatches++;
				audit.Violations.Add(
					$"seam {edge} {a.SectorX},{a.SectorZ}<->{b.SectorX},{b.SectorZ} " +
					$"{field} at {x},{z}: {FieldValue(a, ai, field)} != {FieldValue(b, bi, field)}");
			}
			compared++;
		}
		return compared;
	}

	private static int FieldValue(AtlasSectorData data, int index, string field) => field switch
	{
		"height" => data.Height[index],
		"water-surface" => data.WaterSurface[index],
		"land" => data.Land[index],
		"water" => data.Water[index],
		"hydrology" => data.Hydrology[index],
		"profile" => data.Profile[index],
		"secondary-profile" => data.SecondaryProfile[index],
		"profile-blend" => data.ProfileBlend[index],
		"surface" => data.Surface[index],
		"slope" => data.Slope[index],
		"aspect" => data.Aspect[index],
		"curvature" => data.Curvature[index],
		"wetness" => data.Wetness[index],
		_ => -1,
	};

	private static void MeasureHydrology(AtlasSectorData data, int atlasWidth, int atlasDepth,
		AtlasHydrologyAuditBuilder audit)
	{
		for (int z = 0; z < data.CoreSize; z++)
		for (int x = 0; x < data.CoreSize; x++)
		{
			int localX = x + data.Apron, localZ = z + data.Apron;
			int current = localZ * data.Width + localX;
			int globalX = data.SectorX * data.CoreSize + x;
			int globalZ = data.SectorZ * data.CoreSize + z;
			if (globalX + 1 < atlasWidth)
				MeasurePair(current, current + 1, globalX, globalZ, globalX + 1, globalZ);
			if (globalZ + 1 < atlasDepth)
				MeasurePair(current, current + data.Width, globalX, globalZ, globalX, globalZ + 1);
		}

		void MeasurePair(int a, int b, int ax, int az, int bx, int bz)
		{
			bool aWet = data.WaterSurface[a] > 0;
			bool bWet = data.WaterSurface[b] > 0;
			bool crossSector = ax / data.CoreSize != bx / data.CoreSize ||
			                   az / data.CoreSize != bz / data.CoreSize;
			if (aWet && bWet)
			{
				audit.WetWetEdges++;
				int step = Math.Abs(data.WaterSurface[a] - data.WaterSurface[b]);
				if (step == 0) return;
				audit.WaterStepEdges++;
				if (step > audit.MaxWaterStep)
				{
					audit.MaxWaterStep = step;
					audit.MaxWaterStepX = ax;
					audit.MaxWaterStepZ = az;
				}
				if (step <= 1) return;
				audit.SevereWaterStepEdges++;
				if (crossSector) audit.CrossSectorInvariantViolations++;
				audit.Violations.Add(
					$"water-step {ax},{az}={data.WaterSurface[a]} <-> " +
					$"{bx},{bz}={data.WaterSurface[b]} drop {step}" +
					(crossSector ? " cross-sector" : ""));
				return;
			}
			if (aWet == bWet) return;
			int wet = aWet ? a : b;
			int dry = aWet ? b : a;
			int dryX = aWet ? bx : ax, dryZ = aWet ? bz : az;
			int wetX = aWet ? ax : bx, wetZ = aWet ? az : bz;
			int depth = data.WaterSurface[wet] + 1 - data.Height[dry];
			if (depth <= 0) return;
			audit.SubmergedDryBoundaryEdges++;
			if (depth > audit.MaxSubmergedDryDepth)
			{
				audit.MaxSubmergedDryDepth = depth;
				audit.MaxSubmergedDryX = dryX;
				audit.MaxSubmergedDryZ = dryZ;
			}
			if (crossSector) audit.CrossSectorInvariantViolations++;
			audit.Violations.Add(
				$"submerged-dry {dryX},{dryZ} height {data.Height[dry]} beside " +
				$"water {wetX},{wetZ} surface {data.WaterSurface[wet]} depth {depth}" +
				(crossSector ? " cross-sector" : ""));
		}
	}

	private sealed class AtlasHydrologyAuditBuilder
	{
		public long WetWetEdges;
		public long WaterStepEdges;
		public long SevereWaterStepEdges;
		public int MaxWaterStep;
		public int MaxWaterStepX;
		public int MaxWaterStepZ;
		public long SubmergedDryBoundaryEdges;
		public int MaxSubmergedDryDepth;
		public int MaxSubmergedDryX;
		public int MaxSubmergedDryZ;
		public long CrossSectorInvariantViolations;
		public int HorizontalSeams;
		public int VerticalSeams;
		public long SeamCells;
		public long SeamMismatches;
		public readonly List<string> Violations = new();

		public AtlasHydrologyAuditResult ToResult(int sectorCount) => new(
			sectorCount, WetWetEdges, WaterStepEdges, SevereWaterStepEdges,
			MaxWaterStep, MaxWaterStepX, MaxWaterStepZ,
			SubmergedDryBoundaryEdges, MaxSubmergedDryDepth,
			MaxSubmergedDryX, MaxSubmergedDryZ, CrossSectorInvariantViolations,
			HorizontalSeams, VerticalSeams, SeamCells, SeamMismatches,
			Violations.AsReadOnly());
	}

	private static string CoreHash(AtlasSectorData data)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		hash.AppendData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
			$"atlas-core-v1;{data.SourceFingerprint};{data.SectorX};{data.SectorZ};{data.CoreSize};")));
		var row = new byte[data.CoreSize * 15];
		for (int z = 0; z < data.CoreSize; z++)
		{
			int target = 0;
			int source = (z + data.Apron) * data.Width + data.Apron;
			for (int x = 0; x < data.CoreSize; x++, source++)
			{
				BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(target, 2), data.Height[source]);
				BinaryPrimitives.WriteUInt16LittleEndian(row.AsSpan(target + 2, 2), data.WaterSurface[source]);
				row[target + 4] = data.Land[source];
				row[target + 5] = data.Water[source];
				row[target + 6] = data.Hydrology[source];
				row[target + 7] = data.Profile[source];
				row[target + 8] = data.SecondaryProfile[source];
				row[target + 9] = data.ProfileBlend[source];
				row[target + 10] = data.Surface[source];
				row[target + 11] = data.Slope[source];
				row[target + 12] = data.Aspect[source];
				row[target + 13] = data.Curvature[source];
				row[target + 14] = data.Wetness[source];
				target += 15;
			}
			hash.AppendData(row);
		}
		return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
	}

	private static string ArtifactPath(string outputDirectory, int sectorX, int sectorZ) =>
		JoinPath(outputDirectory, $"sector-{sectorX}-{sectorZ}.pfs");

	private static string JoinPath(string directory, string fileName) =>
		directory.StartsWith("res://", StringComparison.Ordinal)
			? $"{directory.TrimEnd('/')}/{fileName}"
			: Path.Combine(directory, fileName);

	private static void EnsureDirectory(string resourceDirectory)
	{
		string absolute = ProjectSettings.GlobalizePath(resourceDirectory);
		Directory.CreateDirectory(absolute);
	}

	private static void WriteArtifactAtomically(AtlasSectorCompiler compiler,
		AtlasSectorData data, string resourcePath)
	{
		string temporary = resourcePath + ".tmp";
		compiler.WriteArtifact(data, temporary);
		MoveIntoPlace(temporary, resourcePath);
	}

	private static void WriteTextAtomically(string resourcePath, string content)
	{
		string temporary = resourcePath + ".tmp";
		string absolute = ProjectSettings.GlobalizePath(temporary);
		string directory = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(absolute, content, new UTF8Encoding(false));
		MoveIntoPlace(temporary, resourcePath);
	}

	private static void MoveIntoPlace(string temporaryResourcePath, string finalResourcePath)
	{
		string temporary = ProjectSettings.GlobalizePath(temporaryResourcePath);
		string final = ProjectSettings.GlobalizePath(finalResourcePath);
		File.Move(temporary, final, true);
	}

	private static string FileSha256(string resourcePath)
	{
		string absolute = ProjectSettings.GlobalizePath(resourcePath);
		using var stream = File.OpenRead(absolute);
		return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
	}

	private static (byte r, byte g, byte b)[] ProfileColours(WorldAtlasDefinition atlas)
	{
		var result = new (byte r, byte g, byte b)[atlas.BiomeCatalog.Profiles.Count];
		for (int i = 0; i < result.Length; i++)
		{
			string id = atlas.BiomeCatalog.Profiles[i].Id;
			AtlasProvince owner = atlas.Provinces.FirstOrDefault(province =>
				province.BiomeProfileIds.FirstOrDefault() == id);
			bool secondary = false;
			if (owner == null)
			{
				owner = atlas.Provinces.FirstOrDefault(province => province.BiomeProfileIds.Contains(id));
				secondary = owner != null;
			}
			(byte r, byte g, byte b) colour = owner == null
				? ((byte)174, (byte)191, (byte)145)
				: ParseColour(owner.PreviewColour);
			if (secondary)
				colour = ((byte)Math.Clamp(colour.r * 0.78f, 0f, 255f),
					(byte)Math.Clamp(colour.g * 0.86f, 0f, 255f),
					(byte)Math.Clamp(colour.b * 0.82f, 0f, 255f));
			result[i] = colour;
		}
		return result;
	}

	private static (byte r, byte g, byte b) ParseColour(string html) =>
		(Convert.ToByte(html.Substring(1, 2), 16), Convert.ToByte(html.Substring(3, 2), 16),
			Convert.ToByte(html.Substring(5, 2), 16));

	private sealed class AtlasCompositeAccumulator
	{
		private readonly WorldAtlasDefinition _atlas;
		private readonly int _width;
		private readonly int _depth;
		private readonly long[] _height;
		private readonly long[] _hydrologyR;
		private readonly long[] _hydrologyG;
		private readonly long[] _hydrologyB;
		private readonly long[] _profileR;
		private readonly long[] _profileG;
		private readonly long[] _profileB;
		private readonly int[] _maxDrop;
		private readonly int[] _cliffCells;
		private readonly int[] _severeCells;
		private readonly int[] _samples;
		private readonly (byte r, byte g, byte b)[] _profileColours;

		public int BlocksPerPixel { get; }

		public AtlasCompositeAccumulator(WorldAtlasDefinition atlas)
		{
			_atlas = atlas;
			BlocksPerPixel = atlas.ChunkSize;
			_width = (atlas.Width + BlocksPerPixel - 1) / BlocksPerPixel;
			_depth = (atlas.Depth + BlocksPerPixel - 1) / BlocksPerPixel;
			int count = _width * _depth;
			_height = new long[count];
			_hydrologyR = new long[count];
			_hydrologyG = new long[count];
			_hydrologyB = new long[count];
			_profileR = new long[count];
			_profileG = new long[count];
			_profileB = new long[count];
			_maxDrop = new int[count];
			_cliffCells = new int[count];
			_severeCells = new int[count];
			_samples = new int[count];
			_profileColours = ProfileColours(atlas);
		}

		public void Add(AtlasSectorData data)
		{
			for (int z = 0; z < data.CoreSize; z++)
			for (int x = 0; x < data.CoreSize; x++)
			{
				int localX = x + data.Apron, localZ = z + data.Apron;
				int source = localZ * data.Width + localX;
				int globalX = data.SectorX * data.CoreSize + x;
				int globalZ = data.SectorZ * data.CoreSize + z;
				int target = globalZ / BlocksPerPixel * _width + globalX / BlocksPerPixel;
				_samples[target]++;
				_height[target] += data.Height[source];

				(byte hr, byte hg, byte hb) = data.Hydrology[source] switch
				{
					1 => ((byte)154, (byte)191, (byte)157),
					2 => ((byte)110, (byte)171, (byte)159),
					3 => ((byte)104, (byte)154, (byte)202),
					_ => ((byte)200, (byte)195, (byte)154),
				};
				_hydrologyR[target] += hr;
				_hydrologyG[target] += hg;
				_hydrologyB[target] += hb;

				if (data.Land[source] == 0)
				{
					// Water cells still carry a build profile for their bed material,
					// but drawing that across open ocean hides the authored coastline.
					// A neutral blue makes this an immediately readable land-biome audit.
					_profileR[target] += 116;
					_profileG[target] += 154;
					_profileB[target] += 194;
				}
				else
				{
					(byte pr, byte pg, byte pb) = _profileColours[data.Profile[source]];
					(byte sr, byte sg, byte sb) = _profileColours[data.SecondaryProfile[source]];
					int blend = data.ProfileBlend[source];
					_profileR[target] += (pr * (255 - blend) + sr * blend + 127) / 255;
					_profileG[target] += (pg * (255 - blend) + sg * blend + 127) / 255;
					_profileB[target] += (pb * (255 - blend) + sb * blend + 127) / 255;
				}

				int drop = data.Slope[source];
				_maxDrop[target] = Math.Max(_maxDrop[target], drop);
				if (data.Surface[source] == (byte)AtlasTerrainSurface.Cliff) _cliffCells[target]++;
				if (drop >= SevereDropThreshold) _severeCells[target]++;
			}
		}

		public List<AtlasBatchCompositeManifest> Write(string outputDirectory)
		{
			byte[] height = new byte[_width * _depth * 3];
			byte[] hydrology = new byte[_width * _depth * 3];
			byte[] profile = new byte[_width * _depth * 3];
			byte[] cliffDrop = new byte[_width * _depth * 3];
			for (int i = 0; i < _samples.Length; i++)
			{
				if (_samples[i] <= 0) continue;
				int target = i * 3;
				byte h = (byte)Math.Clamp((int)Math.Round(
					_height[i] / (double)_samples[i] / Math.Max(1, _atlas.Height - 1) * 255d), 0, 255);
				height[target] = height[target + 1] = height[target + 2] = h;
				hydrology[target] = AverageByte(_hydrologyR[i], _samples[i]);
				hydrology[target + 1] = AverageByte(_hydrologyG[i], _samples[i]);
				hydrology[target + 2] = AverageByte(_hydrologyB[i], _samples[i]);
				profile[target] = AverageByte(_profileR[i], _samples[i]);
				profile[target + 1] = AverageByte(_profileG[i], _samples[i]);
				profile[target + 2] = AverageByte(_profileB[i], _samples[i]);
				// R is the strongest local drop, G is cliff-cell coverage and B
				// is severe-drop coverage. This makes both isolated scarps and broad
				// broken slopes visible in one small audit image.
				cliffDrop[target] = (byte)Math.Clamp(
					(int)Math.Round(_maxDrop[i] / 32d * 255d), 0, 255);
				cliffDrop[target + 1] = (byte)Math.Clamp(
					(int)Math.Round(_cliffCells[i] / (double)_samples[i] * 255d), 0, 255);
				cliffDrop[target + 2] = (byte)Math.Clamp(
					(int)Math.Round(_severeCells[i] / (double)_samples[i] * 255d), 0, 255);
			}

			var results = new List<AtlasBatchCompositeManifest>(4);
			WriteOne("height", "atlas-height.png", height,
				"grayscale average compiled height, black=0 and white=world ceiling");
			WriteOne("hydrology", "atlas-hydrology.png", hydrology,
				"mean class colour: dry sand, floodplain sage, bank teal, channel blue");
			WriteOne("profile", "atlas-profile.png", profile,
				"mean land primary/secondary biome-profile colour using the manifest profile legend; water is neutral blue");
			WriteOne("cliffDrop", "atlas-cliff-drop.png", cliffDrop,
				$"R=max compiled slope normalized to 32; G=compiled Cliff surface coverage; B=slope coverage >= {SevereDropThreshold}");
			return results;

			void WriteOne(string kind, string fileName, byte[] pixels, string description)
			{
				string resourcePath = JoinPath(outputDirectory, fileName);
				WritePngAtomically(resourcePath, _width, _depth, pixels);
				results.Add(new AtlasBatchCompositeManifest
				{
					Kind = kind,
					File = fileName,
					Sha256 = FileSha256(resourcePath),
					Description = description,
				});
				GD.Print($"[atlas-composite] {kind} {ProjectSettings.GlobalizePath(resourcePath)}");
			}
		}

		private static byte AverageByte(long total, int count) =>
			(byte)Math.Clamp((int)Math.Round(total / (double)count), 0, 255);
	}

	private static void WritePngAtomically(string resourcePath, int width, int depth, byte[] pixels)
	{
		var image = Image.CreateFromData(width, depth, false, Image.Format.Rgb8, pixels);
		string temporary = resourcePath + ".tmp.png";
		string absolute = ProjectSettings.GlobalizePath(temporary);
		string directory = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		Error error = image.SavePng(absolute);
		if (error != Error.Ok)
			throw new IOException($"could not write atlas composite '{resourcePath}': {error}");
		MoveIntoPlace(temporary, resourcePath);
	}
}

public readonly record struct AtlasBatchCompileResult(int RebuiltSectors, int ReusedSectors,
	string ManifestSha256);

public readonly record struct AtlasBatchVerifyResult(int SectorCount, int HorizontalSeams,
	int VerticalSeams, long DeterministicCells, long HorizontalOverlapCells, long VerticalOverlapCells);

public readonly record struct AtlasHydrologyAuditResult(int SectorCount,
	long WetWetEdges, long WaterStepEdges, long SevereWaterStepEdges,
	int MaxWaterStep, int MaxWaterStepX, int MaxWaterStepZ,
	long SubmergedDryBoundaryEdges, int MaxSubmergedDryDepth,
	int MaxSubmergedDryX, int MaxSubmergedDryZ,
	long CrossSectorInvariantViolations, int HorizontalSeams, int VerticalSeams,
	long SeamCells, long SeamMismatches, IReadOnlyList<string> Violations);

public sealed class AtlasBatchManifest
{
	public int Version { get; set; }
	public int CompilerVersion { get; set; }
	public string SourceFingerprint { get; set; } = "";
	public string AtlasId { get; set; } = "";
	public int Width { get; set; }
	public int Depth { get; set; }
	public int Height { get; set; }
	public int SectorSize { get; set; }
	public int SectorColumns { get; set; }
	public int SectorRows { get; set; }
	public int Apron { get; set; }
	public int CompositeBlocksPerPixel { get; set; }
	public int SevereDropThreshold { get; set; }
	public string CliffCoverageSource { get; set; } = "";
	public string DropMetricSource { get; set; } = "";
	public List<AtlasBatchProfileManifest> Profiles { get; set; } = new();
	public List<AtlasBatchSectorManifest> Sectors { get; set; } = new();
	public List<AtlasBatchCompositeManifest> Composites { get; set; } = new();
}

public sealed class AtlasBatchProfileManifest
{
	public int Index { get; set; }
	public string Id { get; set; } = "";
	public string Colour { get; set; } = "";
}

public sealed class AtlasBatchSectorManifest
{
	public int X { get; set; }
	public int Z { get; set; }
	public string Artifact { get; set; } = "";
	public string CoreHash { get; set; } = "";
	public string ArtifactSha256 { get; set; } = "";
}

public sealed class AtlasBatchCompositeManifest
{
	public string Kind { get; set; } = "";
	public string File { get; set; } = "";
	public string Sha256 { get; set; } = "";
	public string Description { get; set; } = "";
}
