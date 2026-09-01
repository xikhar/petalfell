using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// First production-atlas compiler boundary. It reads registered authored L0
/// sources and samples all variation in global coordinates, so rebuilding a
/// sector in isolation produces the same columns as rebuilding its neighbours.
/// This intentionally stops at a compact terrain/profile artifact; runtime
/// windowing and voxel-column materialisation belong to the next slice.
/// </summary>
public sealed class AtlasSectorCompiler
{
	public const int CompilerVersion = 28;
	public const int DefaultApron = 24;
	private const byte PermanentWaterValue = 240;
	private const byte HydrologyDry = 0;
	private const byte HydrologyFloodplain = 1;
	private const byte HydrologyBank = 2;
	private const byte HydrologyChannel = 3;
	private const int WaterSurfaceOffset = 1;
	private const float WaterGuideSupportThreshold = 0.90f;
	private const int WaterGuideDilationPixels = 1;
	private const float RegionBlendWavelength = 96f;
	private const float RegionBlendWander = 24f;
	private const int CoastalGradePixels = 64;
	private const int NaturalEdgeCell = 6;
	private const byte LegacyCoastWater = 1;
	private const byte LegacyChannelWater = 2;
	private const byte LegacyLakeWater = 3;
	private const int LegacyCoastReach = 14;
	private const int LegacyChannelReach = 16;
	private const int LegacyLakeReach = 20;
	private const int LegacyBedReach = 32;

	private readonly WorldAtlasDefinition _atlas;
	private readonly int _worldSeed;
	private readonly Image _land;
	private readonly Image _elevation;
	private readonly Image _water;
	private readonly Image _region;
	private readonly Dictionary<string, BiomeBuildProfile> _profiles;
	private readonly Dictionary<string, int> _profileIndices;
	private readonly Dictionary<int, AtlasProvince> _provinceByColour;
	private readonly Dictionary<string, (byte r, byte g, byte b)> _previewColourByProfile = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Noise2D[]> _profileNoise = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Noise2D> _ridgeNoise = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Noise2D> _mountainNoise = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Noise2D> _selectionNoise = new(StringComparer.Ordinal);
	private readonly Noise2D _regionBlendNoise;
	private readonly Noise2D _hydrologyEdgeNoise;
	private readonly ProductionTerrainGrammar _legacyTerrain;
	private readonly int[] _waterBody;
	private readonly Dictionary<int, float> _inlandWaterBankHeight;
	private readonly ushort[] _oceanDistance;
	private readonly float[] _waterSurfaceGuide;

	public string SourceFingerprint { get; }

	public AtlasSectorCompiler(WorldAtlasDefinition atlas, int worldSeed, string atlasResourcePath)
	{
		_atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		_worldSeed = worldSeed;
		_regionBlendNoise = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:region-transition")));
		_hydrologyEdgeNoise = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:hydrology-edge")));
		_legacyTerrain = new ProductionTerrainGrammar(worldSeed);
		_profiles = atlas.BiomeCatalog.Profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
		_profileIndices = atlas.BiomeCatalog.Profiles.Select((profile, index) => (profile.Id, index))
			.ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);
		_provinceByColour = atlas.Provinces.ToDictionary(p => ColourKey(ParseColour(p.PreviewColour)));
		foreach (var province in atlas.Provinces)
		{
			string primary = province.BiomeProfileIds.FirstOrDefault();
			if (primary != null) _previewColourByProfile[primary] = ParseColour(province.PreviewColour);
		}
		_land = LoadRequiredLayer(AtlasLayerKind.Land);
		_elevation = LoadRequiredLayer(AtlasLayerKind.Elevation);
		_water = LoadOptionalLayer(AtlasLayerKind.Water);
		_region = LoadOptionalLayer(AtlasLayerKind.Region);
		(_waterBody, _inlandWaterBankHeight) = BuildWaterBodies();
		_oceanDistance = BuildOceanDistance();
		_waterSurfaceGuide = BuildWaterSurfaceGuide();
		SourceFingerprint = BuildSourceFingerprint(atlasResourcePath);
	}

	public AtlasSectorData Compile(int sectorX, int sectorZ, int apron = DefaultApron)
	{
		int sectorsX = _atlas.Width / _atlas.SectorSize;
		int sectorsZ = _atlas.Depth / _atlas.SectorSize;
		if (sectorX < 0 || sectorX >= sectorsX || sectorZ < 0 || sectorZ >= sectorsZ)
			throw new ArgumentOutOfRangeException(nameof(sectorX),
				$"sector {sectorX},{sectorZ} lies outside 0..{sectorsX - 1},0..{sectorsZ - 1}");
		if (apron < 0 || apron > _atlas.SectorSize / 2)
			throw new ArgumentOutOfRangeException(nameof(apron), "apron must be between zero and half a sector");

		int width = _atlas.SectorSize + apron * 2;
		int depth = _atlas.SectorSize + apron * 2;
		int originX = sectorX * _atlas.SectorSize - apron;
		int originZ = sectorZ * _atlas.SectorSize - apron;
		var data = new AtlasSectorData(sectorX, sectorZ, originX, originZ,
			_atlas.SectorSize, apron, width, depth, _atlas.Height, _atlas.SeaLevel,
			SourceFingerprint);

		// Metrics and shoreline distance need neighbouring cells beyond the
		// persisted apron. Build that support transiently and crop it away. This
		// is why a sector edge sees exactly the same slope/wetness result whether
		// its east or west owner happened to compile first.
		// Two synchronous toe passes read one cardinal neighbour per pass. Forty
		// transient columns cover those dependencies, the component cleanup, the
		// one-cell metric kernel, and the
		// existing shoreline reach without enlarging the persisted apron.
		int support = Math.Max(40,
			_atlas.BiomeCatalog.Profiles.Max(p => p.Relief.ShoreWidth) + 2);
		int workWidth = width + support * 2;
		int workDepth = depth + support * 2;
		int workOriginX = originX - support;
		int workOriginZ = originZ - support;
		var work = new AtlasSectorData(sectorX, sectorZ, workOriginX, workOriginZ,
			_atlas.SectorSize, apron + support, workWidth, workDepth, _atlas.Height,
			_atlas.SeaLevel, SourceFingerprint);
		RegionTransitionField transitions = BuildRegionTransitionField(
			workOriginX, workOriginZ, workWidth, workDepth);
		var naturalHeight = new short[work.CellCount];

		for (int z = 0; z < workDepth; z++)
		for (int x = 0; x < workWidth; x++)
		{
			int globalX = workOriginX + x;
			int globalZ = workOriginZ + z;
			int index = z * workWidth + x;
			bool sourceLand = GuidedLandAt(globalX, globalZ);
			// elevation.png deliberately stores zero beneath water. Ordinary bilinear
			// sampling therefore pulled the first dry bank cells down toward zero and
			// manufactured 60-plus-block shoreline walls. Renormalise only over valid
			// land texels; water cells use the continuous valley guide for profile choice.
			float authoredElevation = sourceLand && TryGuidedLandElevation(globalX, globalZ, out float landElevation)
				? landElevation
				: ValleyGuideAt(globalX, globalZ) / (_atlas.Height - 1f);
			float water = _water == null ? 0f : Sample(_water, globalX, globalZ);
			if (sourceLand && water > 0f && water < 1f)
			{
				// Waver only the authored transition band. Zero, fully wet, coast and
				// major channel identity remain exactly where the painted layers put them.
				float transition = 4f * water * (1f - water);
				water += _hydrologyEdgeNoise.Fbm(globalX / 112f, globalZ / 112f, 3) * 0.035f * transition;
				water = Rng.Clamp(water, 0f, 1f);
			}
			if (!sourceLand) water = 1f;

			float macroSlope = MacroSlopeAt(globalX, globalZ);
			RegionBlendSample region = RegionBlendAt(transitions, globalX, globalZ);
			BiomeBuildProfile profile = ResolveProfile(region.Primary, authoredElevation, water,
				macroSlope, globalX, globalZ);
			BiomeBuildProfile secondary = ResolveProfile(region.Secondary ?? region.Primary,
				authoredElevation, water, macroSlope, globalX, globalZ);
			float profileBlend = profile.Id == secondary.Id ? 0f : region.SecondaryWeight;
			work.Water[index] = (byte)Math.Clamp((int)MathF.Round(water * 255f), 0, 255);
			work.Profile[index] = (byte)_profileIndices[profile.Id];
			work.SecondaryProfile[index] = (byte)_profileIndices[secondary.Id];
			work.ProfileBlend[index] = (byte)Math.Clamp((int)MathF.Round(profileBlend * 255f), 0, 255);

			HydrologySample hydrology = BlendHydrology(profile.Hydrology, secondary.Hydrology, profileBlend);
			if (!sourceLand)
			{
				int waterBody = WaterBodyAt(globalX, globalZ);
				int surface;
				if (waterBody == 0)
				{
					surface = _atlas.SeaLevel;
					float seaRelief = Rng.Lerp(NoiseFor(profile, 0).Value(globalX / 384f, globalZ / 384f),
						NoiseFor(secondary, 0).Value(globalX / 384f, globalZ / 384f), profileBlend);
					work.Height[index] = (ushort)Math.Clamp(
						(int)MathF.Round(surface - 9f + seaRelief * 3f), 1, surface - 1);
				}
				else
				{
					// The land mask and hydrology mask have independent antialiased
					// fringes. Sampling a lake's body-bank altitude here while an adjacent
					// water-valued land cell sampled ValleyGuideAt produced jagged 2-5 block
					// wet/wet walls along that registration boundary. The valley guide
					// already carries this body's global bank height and coastal grade;
					// using it on both sides keeps the authored body/river slope while
					// removing a branch-dependent discontinuity.
					surface = QuantizeWaterSurface(
						ValleyGuideAt(globalX, globalZ) - WaterSurfaceOffset);
					work.Height[index] = (ushort)Math.Clamp(surface - ChannelDepth(hydrology), 1, surface - 1);
				}
				work.Land[index] = 0;
				work.WaterSurface[index] = (ushort)surface;
				work.Hydrology[index] = HydrologyChannel;
				continue;
			}

			ReliefSample firstRelief = HeightForProfile(profile, globalX, globalZ,
				authoredElevation, macroSlope);
			ReliefSample secondRelief = HeightForProfile(secondary, globalX, globalZ,
				authoredElevation, macroSlope);
			float height = Rng.Lerp(firstRelief.Height, secondRelief.Height, profileBlend);
			float valleyGuide = ValleyGuideAt(globalX, globalZ);
			// Permanent authored water carves through profile relief. Capping its
			// surface by the noisy pre-carve height made a single channel inherit every
			// shelf and ridge discontinuity, including 68-block jumps between adjacent
			// cells. Banks may descend toward this guide; they never push the guide down.
			// A connected water surface cannot jump because its biome changed. The
			// profile's historical surfaceDrop now contributes to incision depth; one
			// shared offset keeps the longitudinal surface owned only by the valley guide.
			int waterSurface = QuantizeWaterSurface(valleyGuide - WaterSurfaceOffset);
			if (work.Water[index] >= PermanentWaterValue)
			{
				work.Land[index] = 0;
				work.WaterSurface[index] = (ushort)waterSurface;
				work.Height[index] = (ushort)Math.Clamp(waterSurface - ChannelDepth(hydrology), 1, waterSurface - 1);
				work.Hydrology[index] = HydrologyChannel;
				continue;
			}

			// The pre-clean field deliberately includes the sparse macro-front course.
			// It gives the ledge pass below an actual realised rise to articulate. The
			// final terrain is returned to the ordinary biome course after that pass;
			// a thresholded mask must never become the terrain's general quantiser.
			int terraceStep = Math.Max(1, (int)MathF.Round(Rng.Lerp(
				firstRelief.TerraceStep, secondRelief.TerraceStep, profileBlend)));
			float terraceBlend = 0f;
			int naturalQuantized = QuantizeWithTerraceBlend(height, terraceStep, terraceBlend);
			naturalHeight[index] = (short)Math.Clamp(
				naturalQuantized, _atlas.SeaLevel + 1, _atlas.Height - 2);

			float floodplain = Rng.Smoothstep(hydrology.FloodplainStart, hydrology.BankStart, water);
			float bank = Rng.Smoothstep(hydrology.BankStart, AtlasHydrologyProfile.PermanentWaterStart, water);
			float floodplainTarget = waterSurface + hydrology.FloodplainRise;
			float bankTarget = waterSurface + hydrology.BankRise;
			float macroCutRise = authoredElevation * (_atlas.Height - 1f) - valleyGuide;
			bool preserveAuthoredCut = macroCutRise >= hydrology.PreserveCutRise;
			if (!preserveAuthoredCut)
			{
				// Ordinary banks converge on the profile-backed floodplain from either
				// direction. The former lowering-only pass left low noisy terrain beneath
				// the water; genuine authored scarps are protected by their source rise.
				height = Rng.Lerp(height, floodplainTarget, floodplain * 0.82f);
				height = Rng.Lerp(height, bankTarget, bank);
			}

			int quantized = QuantizeWithTerraceBlend(height, terraceStep, terraceBlend);
			// A dry voxel top may meet the water plane, but it may never sit beneath it.
			// Round upward by whole terrain courses so this safety invariant does not
			// introduce a thin non-grammar ledge at the shoreline.
			int minimumDryHeight = waterSurface + 1;
			if (quantized < minimumDryHeight)
			{
				int minimumAboveSea = minimumDryHeight - _atlas.SeaLevel;
				quantized = _atlas.SeaLevel +
					(int)MathF.Ceiling(minimumAboveSea / (float)terraceStep) * terraceStep;
			}
			work.Height[index] = (ushort)Math.Clamp(quantized, _atlas.SeaLevel + 1, _atlas.Height - 2);
			work.Land[index] = 255;
			work.Hydrology[index] = water >= hydrology.BankStart
				? HydrologyBank
				: water >= hydrology.FloodplainStart ? HydrologyFloodplain : HydrologyDry;
		}

		// Restore the sunset terrain's exact pre-water morphology order. Two mode
		// iterations establish broad piecewise-flat shelves; the first component pass
		// removes tiny islands, the ledge pass articulates only real cliff toes, and
		// the second component pass removes any isolated residue. Water is reapplied
		// afterwards and is never blurred by these land operators.
		short[] cleanedNatural = TerrainShape.ModeFilter(
			naturalHeight, workWidth, radius: 1, work.Land, iterations: 2);
		cleanedNatural = TerrainShape.Despeckle(
			cleanedNatural, workWidth, work.Land, minArea: 16);
		cleanedNatural = ApplyLegacyToeLedges(cleanedNatural, work);
		cleanedNatural = TerrainShape.Despeckle(
			cleanedNatural, workWidth, work.Land, minArea: 12);
		// The old fixture did not expose the source mask as a ruler-straight bank.
		// It sampled the nearest water edge on a six-block lattice, then lowered only
		// coherent stretches into a few broad courses. Typed global-distance fields
		// retain that grammar without allowing a sector build to choose its own shore.
		(byte[] legacyWaterKind, int[] coastDistance, int[] channelDistance,
			int[] lakeDistance, int[] dryDistance) = BuildLegacyShoreFields(work);
		for (int z = 0; z < workDepth; z++)
		for (int x = 0; x < workWidth; x++)
		{
			int index = z * workWidth + x;
			if (work.WaterSurface[index] > 0) continue;
			int globalX = workOriginX + x;
			int globalZ = workOriginZ + z;
			BiomeBuildProfile profile = _atlas.BiomeCatalog.Profiles[work.Profile[index]];
			BiomeBuildProfile secondary = _atlas.BiomeCatalog.Profiles[work.SecondaryProfile[index]];
			float profileBlend = work.ProfileBlend[index] / 255f;
			HydrologySample hydrology = BlendHydrology(
				profile.Hydrology, secondary.Hydrology, profileBlend);
			float geometryWater = GeometricWaterAt(globalX, globalZ);
			float valleyGuide = ValleyGuideAt(globalX, globalZ);
			int waterSurface = QuantizeWaterSurface(valleyGuide - WaterSurfaceOffset);
			float floodplain = Rng.Smoothstep(
				hydrology.FloodplainStart, hydrology.BankStart, geometryWater);
			float bank = Rng.Smoothstep(
				hydrology.BankStart, AtlasHydrologyProfile.PermanentWaterStart, geometryWater);
			float height = cleanedNatural[index];
			float authoredElevation = TrySampleLandElevation(globalX, globalZ, out float found)
				? found : valleyGuide / (_atlas.Height - 1f);
			float macroCutRise = authoredElevation * (_atlas.Height - 1f) - valleyGuide;
			int legacyBankCeiling = int.MaxValue;
			if (macroCutRise < hydrology.PreserveCutRise)
			{
				height = Rng.Lerp(height, waterSurface + hydrology.FloodplainRise,
					floodplain * 0.82f);
				height = Rng.Lerp(height, waterSurface + hydrology.BankRise, bank);
				int qx = FloorDiv(globalX, NaturalEdgeCell) * NaturalEdgeCell + NaturalEdgeCell / 2;
				int qz = FloorDiv(globalZ, NaturalEdgeCell) * NaturalEdgeCell + NaturalEdgeCell / 2;
				int qLocalX = Math.Clamp(qx - workOriginX, 0, workWidth - 1);
				int qLocalZ = Math.Clamp(qz - workOriginZ, 0, workDepth - 1);
				int qIndex = qLocalZ * workWidth + qLocalX;
				const int course = 2;

				// These are the legacy beach/channel/lake selections and wavelengths.
				// Applying a minimum is intentional: the map still owns where water is,
				// while the terrain decides how selected banks descend toward it.
				if (coastDistance[qIndex] <= LegacyCoastReach &&
				    _hydrologyEdgeNoise.Fbm(qx * 0.014f + 200f, qz * 0.014f, 2) >= 0.14f)
					legacyBankCeiling = Math.Min(legacyBankCeiling, waterSurface + 1 +
						Math.Max(0, coastDistance[qIndex] - 1) / 6 * course);
				if (channelDistance[qIndex] <= LegacyChannelReach &&
				    _hydrologyEdgeNoise.Fbm(qx * 0.016f + 40f, qz * 0.016f, 2) > 0.20f)
					legacyBankCeiling = Math.Min(legacyBankCeiling, waterSurface + 1 +
						Math.Max(0, channelDistance[qIndex] - 1) / 7 * course);
				if (lakeDistance[qIndex] <= LegacyLakeReach &&
				    _hydrologyEdgeNoise.Fbm(qx * 0.021f + 90f, qz * 0.021f, 2) > 0.10f)
					legacyBankCeiling = Math.Min(legacyBankCeiling, waterSurface + 1 +
						Math.Max(0, lakeDistance[qIndex] - 1) / 8 * course);
				if (legacyBankCeiling < int.MaxValue)
					height = Math.Min(height, legacyBankCeiling);
			}
			int terraceStep = BlendedTerraceStep(profile, secondary, profileBlend,
				globalX, globalZ, authoredElevation);
			float terraceBlend = 0f;
			int quantized = QuantizeWithTerraceBlend(height, terraceStep, terraceBlend);
			if (legacyBankCeiling < int.MaxValue)
				quantized = Math.Min(quantized, legacyBankCeiling);
			int minimumDryHeight = waterSurface + 1;
			if (quantized < minimumDryHeight)
			{
				int minimumAboveSea = minimumDryHeight - _atlas.SeaLevel;
				quantized = _atlas.SeaLevel +
					(int)MathF.Ceiling(minimumAboveSea / (float)terraceStep) * terraceStep;
			}
			work.Height[index] = (ushort)Math.Clamp(
				quantized, _atlas.SeaLevel + 1, _atlas.Height - 2);
		}

		// Continue the same stepped grammar below the water plane. The previous atlas
		// assigned one fixed bed depth per water kind, producing an opaque-looking
		// trench at the exact mask edge. Interior distance now controls depth and the
		// old global 12/24-cell breakup keeps broad beds from becoming flat sheets.
		for (int z = 0; z < workDepth; z++)
		for (int x = 0; x < workWidth; x++)
		{
			int index = z * workWidth + x;
			if (work.WaterSurface[index] == 0) continue;
			int globalX = workOriginX + x;
			int globalZ = workOriginZ + z;
			int qx = FloorDiv(globalX, NaturalEdgeCell) * NaturalEdgeCell + NaturalEdgeCell / 2;
			int qz = FloorDiv(globalZ, NaturalEdgeCell) * NaturalEdgeCell + NaturalEdgeCell / 2;
			int qLocalX = Math.Clamp(qx - workOriginX, 0, workWidth - 1);
			int qLocalZ = Math.Clamp(qz - workOriginZ, 0, workDepth - 1);
			int interior = Math.Max(1, dryDistance[qLocalZ * workWidth + qLocalX]);
			BiomeBuildProfile profile = _atlas.BiomeCatalog.Profiles[work.Profile[index]];
			BiomeBuildProfile secondary = _atlas.BiomeCatalog.Profiles[work.SecondaryProfile[index]];
			HydrologySample hydrology = BlendHydrology(profile.Hydrology, secondary.Hydrology,
				work.ProfileBlend[index] / 255f);
			int maxDepth = legacyWaterKind[index] switch
			{
				LegacyLakeWater => 30,
				LegacyChannelWater => Math.Max(10, ChannelDepth(hydrology) * 2 + 4),
				_ => 18,
			};
			int bedDepth = 1 + Math.Max(0, interior - 1) / 3 * 2;
			bedDepth += LegacyCellStep(globalX, globalZ, 21, 0.16f, 0.16f, NaturalEdgeCell * 2) * 2;
			if (LegacyCellHash(globalX, globalZ, 22, NaturalEdgeCell * 4) < 0.10f) bedDepth += 2;
			bedDepth = Math.Clamp(bedDepth, 1, maxDepth);
			work.Height[index] = (ushort)Math.Clamp(work.WaterSurface[index] - bedDepth,
				1, work.WaterSurface[index] - 1);
		}

		// The valley guide may rise by one block between the final dry sample and
		// its wet neighbour. Repair against the completed support field, not a
		// partially built scanline, so every cardinal boundary uses the actual
		// adjacent water surface and sector seams make the same decision.
		for (int z = 1; z < workDepth - 1; z++)
		for (int x = 1; x < workWidth - 1; x++)
		{
			int index = z * workWidth + x;
			if (work.WaterSurface[index] > 0) continue;
			int adjacentWater = Math.Max(
				Math.Max(work.WaterSurface[index - 1], work.WaterSurface[index + 1]),
				Math.Max(work.WaterSurface[index - workWidth], work.WaterSurface[index + workWidth]));
			if (adjacentWater == 0 || work.Height[index] >= adjacentWater + 1) continue;
			BiomeBuildProfile primary = _atlas.BiomeCatalog.Profiles[work.Profile[index]];
			BiomeBuildProfile secondary = _atlas.BiomeCatalog.Profiles[work.SecondaryProfile[index]];
			float blend = work.ProfileBlend[index] / 255f;
			float authoredElevation = TrySampleLandElevation(
				workOriginX + x, workOriginZ + z, out float foundElevation)
				? foundElevation : 0f;
			int step = BlendedTerraceStep(primary, secondary, blend,
				workOriginX + x, workOriginZ + z, authoredElevation);
			int minimumAboveSea = adjacentWater + 1 - _atlas.SeaLevel;
			int repaired = _atlas.SeaLevel +
				(int)MathF.Ceiling(minimumAboveSea / (float)step) * step;
			work.Height[index] = (ushort)Math.Clamp(repaired, _atlas.SeaLevel + 1, _atlas.Height - 2);
		}

		int[] waterDistance = BuildWaterDistance(work);
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int sourceX = x + support;
			int sourceZ = z + support;
			int source = sourceZ * workWidth + sourceX;
			int target = z * width + x;
			CopyBaseCell(work, source, data, target);

			int centre = work.Height[source];
			int maxDelta = 0;
			int dryMaxDelta = 0;
			int steepestDown = 0;
			int aspectX = 0, aspectZ = 0;
			int cardinalSum = 0;
			for (int dz = -1; dz <= 1; dz++)
			for (int dx = -1; dx <= 1; dx++)
			{
				if (dx == 0 && dz == 0) continue;
				int neighbourIndex = (sourceZ + dz) * workWidth + sourceX + dx;
				int neighbour = work.Height[neighbourIndex];
				int delta = Math.Abs(centre - neighbour);
				maxDelta = Math.Max(maxDelta, delta);
				if (work.WaterSurface[neighbourIndex] == 0)
					dryMaxDelta = Math.Max(dryMaxDelta, delta);
				int down = centre - neighbour;
				if (down > steepestDown)
				{
					steepestDown = down;
					aspectX = dx;
					aspectZ = dz;
				}
				if (dx == 0 || dz == 0) cardinalSum += neighbour;
			}

			data.Slope[target] = (byte)Math.Clamp(maxDelta, 0, 255);
			float angle = aspectX == 0 && aspectZ == 0 ? 0f : MathF.Atan2(aspectZ, aspectX);
			data.Aspect[target] = (byte)Math.Clamp(
				(int)MathF.Round((angle + MathF.PI) / MathF.Tau * 255f), 0, 255);
			int curvature = cardinalSum - centre * 4;
			data.Curvature[target] = (byte)Math.Clamp(128 + curvature * 4, 0, 255);

			int globalX = data.OriginX + x;
			int globalZ = data.OriginZ + z;
			BiomeBuildProfile metricProfile = _atlas.BiomeCatalog.Profiles[data.Profile[target]];
			BiomeBuildProfile metricSecondary =
				_atlas.BiomeCatalog.Profiles[data.SecondaryProfile[target]];
			float metricBlend = data.ProfileBlend[target] / 255f;
			float metricAuthoredElevation = TrySampleLandElevation(
				globalX, globalZ, out float metricFoundElevation)
				? metricFoundElevation : data.Height[target] / (float)(_atlas.Height - 1);
			int metricTerraceStep = BlendedTerraceStep(metricProfile, metricSecondary,
				metricBlend, globalX, globalZ, metricAuthoredElevation);
			int shoreWidth = metricProfile.Relief.ShoreWidth;
			int distance = waterDistance[source];
			int nearWater = shoreWidth > 0 && distance <= shoreWidth
				? 255 - distance * 255 / (shoreWidth + 1) : 0;
			int wetness = Math.Max(data.Water[target], nearWater);
			if (data.Hydrology[target] == HydrologyBank) wetness = Math.Max(wetness, 224);
			else if (data.Hydrology[target] == HydrologyFloodplain) wetness = Math.Max(wetness, 160);
			data.Wetness[target] = (byte)Math.Clamp(wetness, 0, 255);

			if (data.WaterSurface[target] > 0)
				data.Surface[target] = (byte)AtlasTerrainSurface.Underwater;
			else
			{
				int ordinaryMetricStep = Math.Max(1, (int)MathF.Round(Rng.Lerp(
					metricProfile.TerraceStep, metricSecondary.TerraceStep, metricBlend)));
				int cliffDrop = Math.Max(ordinaryMetricStep * 2,
					metricProfile.Relief.CliffStep - 1);
				int nearestWaterSurface = distance == 1
					? NearestWaterSurfaceAtDistance(work, sourceX, sourceZ, distance)
					: 0;
				int riseAboveWater = nearestWaterSurface > 0
					? centre - nearestWaterSurface : int.MaxValue;
				// The persisted slope still measures the complete voxel silhouette for
				// wilderness suitability. Presentation compares a bank to the water plane,
				// not its submerged bed, which previously turned almost every low edge into
				// a cliff and pushed the pale strip several blocks inland.
				// ShoreWidth still owns the broader wetness/reed reach above; the visible
				// pale lip is exactly one water-connected cell, so it cannot float inland.
				bool lowShore = data.Hydrology[target] == HydrologyBank &&
					shoreWidth > 0 && distance == 1 && nearestWaterSurface > 0 &&
					riseAboveWater >= 1 && riseAboveWater <= metricTerraceStep &&
					dryMaxDelta <= metricTerraceStep;
				bool presentationCliff = dryMaxDelta >= cliffDrop ||
					(distance == 1 && nearestWaterSurface > 0 && riseAboveWater >= cliffDrop);
				data.Surface[target] = (byte)(presentationCliff ? AtlasTerrainSurface.Cliff
					: lowShore ? AtlasTerrainSurface.Shore : AtlasTerrainSurface.Cap);
				if (data.Surface[target] == (byte)AtlasTerrainSurface.Shore)
					RequireVisibleShoreContract(work, data, sourceX, sourceZ, target,
						globalX, globalZ, shoreWidth, metricTerraceStep);
			}
		}

		data.Validate(_atlas.BiomeCatalog.Profiles.Count);
		return data;
	}

	private BiomeBuildProfile ResolveProfile(AtlasProvince province, float authoredElevation,
		float water, float macroSlope, int globalX, int globalZ)
	{
		IReadOnlyList<string> allowed = province?.BiomeProfileIds;
		if (allowed == null || allowed.Count == 0) return _atlas.BiomeCatalog.Profiles[0];
		float moisture = Rng.Clamp(water + (1f - authoredElevation) * 0.18f, 0f, 1f);
		float slope = Rng.Clamp(macroSlope / 1.25f, 0f, 1f);
		BiomeBuildProfile best = null;
		float bestScore = float.NegativeInfinity;
		foreach (string id in allowed)
		{
			if (!_profiles.TryGetValue(id, out BiomeBuildProfile candidate)) continue;
			AtlasBiomeSelectionProfile rule = candidate.Selection;
			float patch = SelectionNoise(candidate).Fbm(
				globalX / rule.PatchWavelength, globalZ / rule.PatchWavelength, 3);
			float score = rule.Bias + authoredElevation * rule.AltitudeWeight +
				moisture * rule.MoistureWeight + slope * rule.SlopeWeight + patch * rule.PatchWeight;
			if (score <= bestScore) continue;
			best = candidate;
			bestScore = score;
		}
		return best ?? _atlas.BiomeCatalog.Profiles[0];
	}

	private ReliefSample HeightForProfile(BiomeBuildProfile profile, int globalX, int globalZ,
		float authoredElevation, float macroSlope)
	{
		int x = FloorDiv(globalX, ProductionTerrainGrammar.EdgeGrid) *
			ProductionTerrainGrammar.EdgeGrid + ProductionTerrainGrammar.EdgeGrid / 2;
		int z = FloorDiv(globalZ, ProductionTerrainGrammar.EdgeGrid) *
			ProductionTerrainGrammar.EdgeGrid + ProductionTerrainGrammar.EdgeGrid / 2;
		int terraceStep = Math.Max(1, profile.TerraceStep);

		// An earlier sector pass layered broad thresholded profile fields over the map
		// and then changed quantisers at their masks. Those masks became the visible
		// geography: giant sparse shelves with hard cutoffs. The sunset terrain used
		// a much simpler contract. Its macro planner supplied height; warped +/- one
		// course rooms and nested high-ground crowns supplied local composition; the
		// ordinary course quantised everything. Preserve that exact division here,
		// with the accepted atlas elevation replacing only the old planner map.
		float height = authoredElevation * (_atlas.Height - 1f);
		height += _legacyTerrain.TerraceOffsetAt(x, z, authoredElevation) * terraceStep;
		return new ReliefSample(height, terraceStep, 0f);
	}

	/// <summary>
	/// Adds a continuous, zero-centred mountain field to the accepted macro height.
	/// Broad warped ridges connect massif shoulders; a second scale cuts readable
	/// spines and saddles through them. Nothing is accepted or stamped per feature,
	/// so quantisation cannot expose a recognisable pad footprint or a closed ring.
	/// </summary>
	private float MountainReliefAt(BiomeBuildProfile profile, int globalX, int globalZ,
		float authoredElevation, float macroSlope)
	{
		AtlasReliefProfile relief = profile.Relief;
		float altitudeMask = Rng.Smoothstep(relief.RidgeStart - 0.12f, 1f,
			authoredElevation);
		float slopeMask = Rng.Smoothstep(relief.SlopeStart * 0.72f,
			relief.SlopeFull, macroSlope);
		float mountainMask = Math.Max(slopeMask, altitudeMask * 0.78f) * altitudeMask;
		if (mountainMask <= 0.001f) return 0f;

		float ridgeAngle = relief.RidgeAngleDegrees * MathF.PI / 180f;
		Vector2 along = new(MathF.Cos(ridgeAngle), MathF.Sin(ridgeAngle));
		Vector2 across = new(-along.Y, along.X);
		float longWave = Rng.Clamp(relief.RidgeWavelength * 0.78f, 384f, 704f);
		float crossWave = Rng.Clamp(relief.LedgeWavelength, 112f, 192f);
		float warpWave = Math.Max(384f, longWave * 0.75f);
		float slopeWarp = MountainNoise(profile, "warp-slope").Fbm(
			globalX / warpWave, globalZ / warpWave, 2) * longWave * 0.03f;
		float contourWarp = MountainNoise(profile, "warp-contour").Fbm(
			(globalX + 137f) / warpWave, (globalZ - 83f) / warpWave, 2) * crossWave * 0.04f;
		Vector2 warped = new Vector2(globalX, globalZ) +
			along * slopeWarp + across * contourWarp;

		float primaryOffset = AnisotropicFbm(MountainNoise(profile, "massif-signed"),
			warped, ridgeAngle, longWave, crossWave, 3);
		float secondaryOffset = AnisotropicFbm(MountainNoise(profile, "shoulder-signed"),
			warped + new Vector2(53f, -31f), ridgeAngle + 0.82f,
			Math.Max(384f, longWave * 0.74f), Math.Max(88f, crossWave * 0.82f), 3);
		float primaryRidge = MathF.Pow(1f - MathF.Abs(primaryOffset), 4f);
		float secondaryRidge = MathF.Pow(1f - MathF.Abs(secondaryOffset), 4f) * 0.82f;
		float ridgeNetwork = Math.Max(primaryRidge, secondaryRidge);
		// Fbm is concentrated around zero. The former squared-ridge threshold kept
		// almost that entire distribution as a summit, so the accepted mountain
		// remained one broad blanket with parallel contour courses. A fourth-power
		// ridge keeps only narrow connected spines and carves the surrounding source
		// mass into broad shoulders and saddles. The crossing secondary family avoids
		// a single set of corrugated parallel stripes.
		float strength = relief.RidgeStrength;
		float spineKeep = Rng.Smoothstep(0.20f, 0.62f, ridgeNetwork);
		float incision = -(1f - spineKeep) * strength * 1.02f;
		float shoulder = (ridgeNetwork - 0.38f) * strength * 0.28f;
		// Compiler 22 declared ridgeStrength=16 but then clamped the whole mountain
		// response to four blocks. At atlas scale that could only wrinkle the source
		// contour. The declared strength now survives, while the accepted altitude
		// and slope masks still prevent a ridge from appearing on an unrelated flat.
		float negativeLimit = Math.Max(6f, strength * 0.95f);
		float positiveLimit = Math.Max(2f, strength * 0.24f);
		return Rng.Clamp((incision + shoulder) * mountainMask,
			-negativeLimit, positiveLimit);
	}

	private static float AnisotropicFbm(Noise2D noise, Vector2 point,
		float angle, float alongWave, float acrossWave,
		int octaves)
	{
		float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
		float alongCoordinate = (point.X * cos + point.Y * sin) / alongWave;
		float acrossCoordinate = (-point.X * sin + point.Y * cos) / acrossWave;
		return noise.Fbm(alongCoordinate, acrossCoordinate, octaves);
	}

	private int TerraceStepForProfile(BiomeBuildProfile profile, int globalX, int globalZ,
		float authoredElevation, float macroSlope)
	{
		AtlasReliefProfile relief = profile.Relief;
		int ordinary = Math.Max(1, profile.TerraceStep);
		if (relief.CliffStep <= ordinary) return ordinary;
		if (FrontLedgeStrength(profile, globalX, globalZ,
			authoredElevation, macroSlope) < 0.58f) return ordinary;
		// A sparse macro front needs enough vertical room for the three ordinary
		// toe courses added after cleanup. The profile cliffStep remains the minimum
		// face and the presentation threshold; four ordinary courses is the realised
		// front grammar, not the general terrain course.
		return Math.Max(relief.CliffStep, ordinary * 4);
	}

	/// <summary>
	/// A continuous atlas-space eligibility field for deliberate cliff fronts.
	/// Its long axis follows the profile's authored ridge direction; its short
	/// axis crosses that ridge. Reversing those wavelengths produced small pads
	/// and broken contour fragments in compiler 23, because the field changed
	/// fastest along the very front that was meant to stay coherent.
	/// </summary>
	private float FrontLedgeStrength(BiomeBuildProfile profile, int globalX, int globalZ,
		float authoredElevation, float macroSlope)
	{
		AtlasReliefProfile relief = profile.Relief;
		// The accepted elevation source is a continent-scale painted gradient. Its
		// mountain fronts are intentionally broad, so their per-block derivative is
		// much smaller than a finished voxel cliff. Use the lower source-gradient
		// gate here; the realised-rise probe below is the strict geometric test.
		if (relief.LedgeHeight <= 0 || macroSlope < relief.SlopeStart * 0.12f)
			return 0f;
		float ridgeAngle = relief.RidgeAngleDegrees * MathF.PI / 180f;
		float front = AnisotropicFbm(MountainNoise(profile, "macro-front"),
			new Vector2(globalX, globalZ), ridgeAngle + 0.20f,
			Math.Max(256f, relief.RidgeWavelength * 0.62f),
			Math.Max(72f, relief.LedgeWavelength * 0.72f), 3);
		float slopeMask = Rng.Smoothstep(relief.SlopeStart * 0.12f,
			relief.SlopeFull * 0.18f, macroSlope);
		float altitudeFloor = profile.ErosionResponse is "wind-scoured" or
			"scree-and-fracture" or "cut-face-and-talus"
			? relief.RidgeStart - 0.20f : relief.RidgeStart - 0.06f;
		float altitudeMask = Rng.Smoothstep(altitudeFloor,
			Math.Min(1f, relief.RidgeStart + 0.18f), authoredElevation);
		return Rng.Smoothstep(-0.08f, 0.38f, front) * slopeMask * altitudeMask;
	}

	private int BlendedTerraceStep(BiomeBuildProfile primary, BiomeBuildProfile secondary,
		float blend, int globalX, int globalZ, float authoredElevation)
	{
		// Once a front has been articulated into explicit courses, all subsequent
		// bank repair and metric work uses the ordinary biome grammar. Letting the
		// front mask continue to switch quantisers collapsed two-block ledges back
		// into four-block ribbons at its threshold boundary.
		return Math.Max(1, (int)MathF.Round(Rng.Lerp(
			primary.TerraceStep, secondary.TerraceStep, blend)));
	}

	/// <summary>
	/// The old AddLedges pass, made synchronous for bounded sector support. Two
	/// passes raise only the low foot of an existing face of at least two ordinary
	/// courses, using the original q6/.035/two-octave threshold. It cannot invent a
	/// shelf away from a realised rise and it has no profile-mask boundary of its
	/// own.
	/// </summary>
	private short[] ApplyLegacyToeLedges(short[] source, AtlasSectorData work)
	{
		const int passes = 2;
		int width = work.Width;
		int depth = work.Depth;
		var current = (short[])source.Clone();
		var raised = new int[passes];
		for (int pass = 0; pass < passes; pass++)
		{
			var next = (short[])current.Clone();
			for (int z = 1; z < depth - 1; z++)
			for (int x = 1; x < width - 1; x++)
			{
				int index = z * width + x;
				if (work.Land[index] == 0 || work.Hydrology[index] != HydrologyDry) continue;
				BiomeBuildProfile profile = _atlas.BiomeCatalog.Profiles[work.Profile[index]];
				BiomeBuildProfile secondary =
					_atlas.BiomeCatalog.Profiles[work.SecondaryProfile[index]];
				float blend = work.ProfileBlend[index] / 255f;
				int ordinary = Math.Max(1, (int)MathF.Round(Rng.Lerp(
					profile.TerraceStep, secondary.TerraceStep, blend)));
				int rise = Math.Max(
					Math.Max(current[index - 1], current[index + 1]),
					Math.Max(current[index - width], current[index + width])) - current[index];
				if (rise < ordinary * 2) continue;

				int globalX = work.OriginX + x;
				int globalZ = work.OriginZ + z;
				if (!_legacyTerrain.KeepToeLedge(globalX, globalZ)) continue;
				next[index] = (short)Math.Clamp(current[index] + ordinary,
					_atlas.SeaLevel + 1, _atlas.Height - 2);
				raised[pass]++;
			}
			current = next;
		}
		GD.Print($"[atlas-legacy-ledges] sector {work.SectorX},{work.SectorZ} " +
			$"raised {raised[0]}/{raised[1]}");
		return current;
	}

	private int QuantizeWithTerraceBlend(float height, int baseStep, float terraceBlend)
	{
		if (baseStep != 1 || terraceBlend <= 0f)
			return QuantizeAboveSea((int)MathF.Round(height), baseStep);
		int fourBlockTarget = QuantizeAboveSea((int)MathF.Round(height), 4);
		float blendedHeight = Rng.Lerp(height, fourBlockTarget, Rng.Clamp(terraceBlend, 0f, 1f));
		return QuantizeAboveSea((int)MathF.Round(blendedHeight), 1);
	}

	/// <summary>
	/// Articulate accepted non-alpine terrain with broad, continuous geomorphic
	/// fields. A contour-tangent shoulder can add or remove one ordinary course on
	/// an existing rise; a quieter isotropic field breaks otherwise empty flats.
	/// Unlike the retired 48-block room/wing stamps, neither term has a rectangular
	/// footprint, a centre, or a per-feature acceptance roll.
	/// </summary>
	private float GeomorphicReliefAt(BiomeBuildProfile profile, int globalX, int globalZ,
		float authoredElevation, float macroSlope)
	{
		AtlasReliefProfile relief = profile.Relief;
		float ordinary = Math.Max(1, profile.TerraceStep);
		float result = 0f;

		if (relief.LedgeHeight <= 0)
			return result;
		float ridgeAngle = relief.RidgeAngleDegrees * MathF.PI / 180f;
		float shoulder = AnisotropicFbm(MountainNoise(profile, "contour-shoulder"),
			new Vector2(globalX, globalZ), ridgeAngle + 0.46f,
			Math.Max(192f, relief.RidgeWavelength * 0.52f),
			Math.Max(48f, relief.LedgeWavelength * 0.62f), 3);
		float slopeMask = Rng.Smoothstep(relief.SlopeStart * 0.58f,
			relief.SlopeFull, macroSlope);
		float response = profile.ErosionResponse switch
		{
			"cut-face-and-talus" => 1.35f,
			"scree-and-fracture" => 1.18f,
			"river-cut" => 0.82f,
			"root-held" => 0.58f,
			"cultivated-terrace" => 0.72f,
			"saturated-and-buried" => 0.28f,
			"tidal-undercut" => 0.62f,
			_ => 0.65f,
		};
		// A continuous signed displacement may wander an existing contour without
		// creating an independent closed feature. The old thresholded positive and
		// negative masks each generated their own isolated oval pads.
		result += shoulder * Math.Min(ordinary, relief.LedgeHeight) *
			slopeMask * response * 0.72f;
		return result;
	}

	private float MacroSlopeAt(int globalX, int globalZ)
	{
		int reach = Math.Max(8, _atlas.BlocksPerPixel * 2);
		if (!TrySampleLandElevation(globalX, globalZ, out float centre)) return 0f;
		float east = TrySampleLandElevation(globalX + reach, globalZ, out float eastFound)
			? eastFound : centre;
		float west = TrySampleLandElevation(globalX - reach, globalZ, out float westFound)
			? westFound : centre;
		float south = TrySampleLandElevation(globalX, globalZ + reach, out float southFound)
			? southFound : centre;
		float north = TrySampleLandElevation(globalX, globalZ - reach, out float northFound)
			? northFound : centre;
		float scale = (_atlas.Height - 1) / (reach * 2f);
		float dx = (east - west) * scale;
		float dz = (south - north) * scale;
		return MathF.Sqrt(dx * dx + dz * dz);
	}

	private Vector2 MacroGradientAt(int globalX, int globalZ, int reach = 0)
	{
		if (reach <= 0) reach = Math.Max(8, _atlas.BlocksPerPixel * 2);
		if (!TrySampleLandElevation(globalX, globalZ, out float centre)) return Vector2.Zero;
		float east = TrySampleLandElevation(globalX + reach, globalZ, out float eastFound)
			? eastFound : centre;
		float west = TrySampleLandElevation(globalX - reach, globalZ, out float westFound)
			? westFound : centre;
		float south = TrySampleLandElevation(globalX, globalZ + reach, out float southFound)
			? southFound : centre;
		float north = TrySampleLandElevation(globalX, globalZ - reach, out float northFound)
			? northFound : centre;
		float scale = (_atlas.Height - 1) / (reach * 2f);
		return new Vector2((east - west) * scale, (south - north) * scale);
	}

	private static void CopyBaseCell(AtlasSectorData source, int sourceIndex,
		AtlasSectorData target, int targetIndex)
	{
		target.Height[targetIndex] = source.Height[sourceIndex];
		target.WaterSurface[targetIndex] = source.WaterSurface[sourceIndex];
		target.Land[targetIndex] = source.Land[sourceIndex];
		target.Water[targetIndex] = source.Water[sourceIndex];
		target.Hydrology[targetIndex] = source.Hydrology[sourceIndex];
		target.Profile[targetIndex] = source.Profile[sourceIndex];
		target.SecondaryProfile[targetIndex] = source.SecondaryProfile[sourceIndex];
		target.ProfileBlend[targetIndex] = source.ProfileBlend[sourceIndex];
	}

	private (byte[] kind, int[] coast, int[] channel, int[] lake, int[] dry)
		BuildLegacyShoreFields(AtlasSectorData data)
	{
		var kind = new byte[data.CellCount];
		for (int z = 0; z < data.Depth; z++)
		for (int x = 0; x < data.Width; x++)
		{
			int index = z * data.Width + x;
			if (data.WaterSurface[index] == 0) continue;
			int globalX = data.OriginX + x;
			int globalZ = data.OriginZ + z;
			if (GuidedLandAt(globalX, globalZ))
				kind[index] = LegacyChannelWater;
			else
				kind[index] = WaterBodyAt(globalX, globalZ) > 0
					? LegacyLakeWater : LegacyCoastWater;
		}

		return (kind,
			BuildCappedDistance(kind, LegacyCoastWater, data.Width, data.Depth, LegacyCoastReach),
			BuildCappedDistance(kind, LegacyChannelWater, data.Width, data.Depth, LegacyChannelReach),
			BuildCappedDistance(kind, LegacyLakeWater, data.Width, data.Depth, LegacyLakeReach),
			BuildCappedDistance(kind, 0, data.Width, data.Depth, LegacyBedReach));
	}

	private static int[] BuildCappedDistance(byte[] field, byte seed,
		int width, int depth, int cap)
	{
		int far = cap + 1;
		var distance = new int[field.Length];
		for (int i = 0; i < distance.Length; i++)
			distance[i] = field[i] == seed ? 0 : far;
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int i = z * width + x;
			if (x > 0) distance[i] = Math.Min(distance[i], distance[i - 1] + 1);
			if (z > 0) distance[i] = Math.Min(distance[i], distance[i - width] + 1);
			distance[i] = Math.Min(distance[i], far);
		}
		for (int z = depth - 1; z >= 0; z--)
		for (int x = width - 1; x >= 0; x--)
		{
			int i = z * width + x;
			if (x + 1 < width) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
			if (z + 1 < depth) distance[i] = Math.Min(distance[i], distance[i + width] + 1);
			distance[i] = Math.Min(distance[i], far);
		}
		return distance;
	}

	private static float LegacyCellHash(int x, int z, int salt, int grid)
	{
		unchecked
		{
			int cx = (int)MathF.Floor(x / (float)grid);
			int cz = (int)MathF.Floor(z / (float)grid);
			uint hash = (uint)(cx * 374761393 + cz * 668265263 + salt * 1442695040);
			hash = (hash ^ (hash >> 13)) * 1274126177u;
			return (hash ^ (hash >> 16)) / 4294967296f;
		}
	}

	private static int LegacyCellStep(int x, int z, int salt,
		float up, float down, int grid)
	{
		float value = LegacyCellHash(x, z, salt, grid);
		if (value < up) return 1;
		if (value > 1f - down) return -1;
		return 0;
	}

	private static int[] BuildWaterDistance(AtlasSectorData data)
	{
		const int Far = 1 << 20;
		var distance = new int[data.CellCount];
		for (int i = 0; i < distance.Length; i++)
			distance[i] = data.WaterSurface[i] > 0 ? 0 : Far;
		for (int z = 0; z < data.Depth; z++)
		for (int x = 0; x < data.Width; x++)
		{
			int i = z * data.Width + x;
			if (x > 0) distance[i] = Math.Min(distance[i], distance[i - 1] + 1);
			if (z > 0) distance[i] = Math.Min(distance[i], distance[i - data.Width] + 1);
		}
		for (int z = data.Depth - 1; z >= 0; z--)
		for (int x = data.Width - 1; x >= 0; x--)
		{
			int i = z * data.Width + x;
			if (x + 1 < data.Width) distance[i] = Math.Min(distance[i], distance[i + 1] + 1);
			if (z + 1 < data.Depth) distance[i] = Math.Min(distance[i], distance[i + data.Width] + 1);
		}
		return distance;
	}

	private static int NearestWaterSurfaceAtDistance(AtlasSectorData data,
		int centreX, int centreZ, int distance)
	{
		if (distance <= 0) return 0;
		int nearestSurface = 0;
		for (int dz = -distance; dz <= distance; dz++)
		{
			int dx = distance - Math.Abs(dz);
			Accumulate(centreX - dx, centreZ + dz);
			if (dx > 0) Accumulate(centreX + dx, centreZ + dz);
		}
		return nearestSurface;

		void Accumulate(int x, int z)
		{
			if (x < 0 || z < 0 || x >= data.Width || z >= data.Depth) return;
			nearestSurface = Math.Max(nearestSurface, data.WaterSurface[z * data.Width + x]);
		}
	}

	private void RequireVisibleShoreContract(AtlasSectorData work, AtlasSectorData data,
		int sourceX, int sourceZ, int index, int globalX, int globalZ,
		int shoreWidth, int terraceStep)
	{
		int source = sourceZ * work.Width + sourceX;
		int nearestWaterSurface = Math.Max(
			Math.Max(work.WaterSurface[source - 1], work.WaterSurface[source + 1]),
			Math.Max(work.WaterSurface[source - work.Width],
				work.WaterSurface[source + work.Width]));
		int centre = work.Height[source];
		int dryMaxDelta = 0;
		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			if (dx == 0 && dz == 0) continue;
			int neighbour = (sourceZ + dz) * work.Width + sourceX + dx;
			if (work.WaterSurface[neighbour] == 0)
				dryMaxDelta = Math.Max(dryMaxDelta,
					Math.Abs(centre - work.Height[neighbour]));
		}
		int riseAboveWater = nearestWaterSurface > 0
			? centre - nearestWaterSurface : int.MaxValue;
		if (data.Surface[index] == (byte)AtlasTerrainSurface.Shore &&
		    data.Land[index] != 0 && data.WaterSurface[index] == 0 &&
		    data.Hydrology[index] == HydrologyBank && shoreWidth > 0 &&
		    nearestWaterSurface > 0 &&
		    riseAboveWater >= 1 && riseAboveWater <= terraceStep &&
		    dryMaxDelta <= terraceStep)
			return;
		throw new InvalidOperationException(
			$"visible shore {globalX},{globalZ} violates its low-bank contract " +
			$"(direct water {nearestWaterSurface}, shore reach {shoreWidth}, " +
			$"rise {riseAboveWater}, dry slope {dryMaxDelta})");
	}

	private static HydrologySample BlendHydrology(AtlasHydrologyProfile a, AtlasHydrologyProfile b, float t) => new(
		Rng.Lerp(a.FloodplainStart, b.FloodplainStart, t),
		Rng.Lerp(a.BankStart, b.BankStart, t),
		(int)MathF.Round(Rng.Lerp(a.FloodplainRise, b.FloodplainRise, t)),
		(int)MathF.Round(Rng.Lerp(a.BankRise, b.BankRise, t)),
		(int)MathF.Round(Rng.Lerp(a.SurfaceDrop, b.SurfaceDrop, t)),
		(int)MathF.Round(Rng.Lerp(a.WaterDepth, b.WaterDepth, t)),
		(int)MathF.Round(Rng.Lerp(a.PreserveCutRise, b.PreserveCutRise, t)));

	private static int ChannelDepth(HydrologySample hydrology) =>
		hydrology.WaterDepth + Math.Max(0, hydrology.SurfaceDrop - WaterSurfaceOffset);

	/// <summary>
	/// Labels black land-mask components once at coarse source resolution. The
	/// edge-connected component is ocean; enclosed components are authored lakes
	/// whose surface follows their lowest surrounding bank. This keeps a mountain
	/// lake above sea level without turning the whole 12k atlas into runtime arrays.
	/// </summary>
	private (int[] labels, Dictionary<int, float> bankHeights) BuildWaterBodies()
	{
		int width = _land.GetWidth(), depth = _land.GetHeight(), count = width * depth;
		var labels = new int[count];
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
			labels[z * width + x] = _land.GetPixel(x, z).R >= 0.5f ? -1 : -2;

		var bankHeights = new Dictionary<int, float>();
		var queue = new int[count];
		int nextBody = 1;
		for (int start = 0; start < count; start++)
		{
			if (labels[start] != -2) continue;
			int head = 0, tail = 0;
			queue[tail++] = start;
			labels[start] = -3;
			bool touchesEdge = false;
			float bankHeight = float.MaxValue;
			while (head < tail)
			{
				int cell = queue[head++];
				int x = cell % width, z = cell / width;
				if (x == 0 || z == 0 || x == width - 1 || z == depth - 1) touchesEdge = true;
				Visit(x - 1, z);
				Visit(x + 1, z);
				Visit(x, z - 1);
				Visit(x, z + 1);

				void Visit(int nx, int nz)
				{
					if (nx < 0 || nz < 0 || nx >= width || nz >= depth) return;
					int neighbour = nz * width + nx;
					if (labels[neighbour] == -2)
					{
						labels[neighbour] = -3;
						queue[tail++] = neighbour;
					}
					else if (labels[neighbour] == -1)
					{
						float elevation = _elevation.GetPixel(nx, nz).R * (_atlas.Height - 1);
						if (elevation > _atlas.SeaLevel) bankHeight = Math.Min(bankHeight, elevation);
					}
				}
			}

			int body = touchesEdge ? 0 : nextBody++;
			for (int i = 0; i < tail; i++) labels[queue[i]] = body;
			if (body > 0)
				bankHeights[body] = bankHeight < float.MaxValue ? bankHeight : _atlas.SeaLevel + 2f;
		}
		return (labels, bankHeights);
	}

	/// <summary>
	/// Distance to the edge-connected ocean at registered source-pixel scale.
	/// This is deliberately not a block-sized continent array: it is one compact
	/// derived field beside the authored L0 images. A broad grade gives connected
	/// rivers enough horizontal run to meet sea level instead of dropping seventy
	/// blocks at the first ocean-labelled cell.
	/// </summary>
	private ushort[] BuildOceanDistance()
	{
		int width = _land.GetWidth(), depth = _land.GetHeight();
		var distance = new ushort[width * depth];
		for (int i = 0; i < distance.Length; i++)
			distance[i] = _waterBody[i] == 0 ? (ushort)0 : ushort.MaxValue;

		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int i = z * width + x;
			if (x > 0) Relax(i, i - 1);
			if (z > 0) Relax(i, i - width);
		}
		for (int z = depth - 1; z >= 0; z--)
		for (int x = width - 1; x >= 0; x--)
		{
			int i = z * width + x;
			if (x + 1 < width) Relax(i, i + 1);
			if (z + 1 < depth) Relax(i, i + width);
		}
		return distance;

		void Relax(int target, int neighbour)
		{
			if (distance[neighbour] == ushort.MaxValue) return;
			int candidate = distance[neighbour] + 1;
			if (candidate < distance[target]) distance[target] = (ushort)candidate;
		}
	}

	private float OceanDistanceAt(int globalX, int globalZ)
	{
		int width = _land.GetWidth(), depth = _land.GetHeight();
		float sourceX = (globalX + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		float sourceZ = (globalZ + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		sourceX = Math.Clamp(sourceX, 0f, width - 1f);
		sourceZ = Math.Clamp(sourceZ, 0f, depth - 1f);
		int x0 = (int)MathF.Floor(sourceX), z0 = (int)MathF.Floor(sourceZ);
		int x1 = Math.Min(x0 + 1, width - 1), z1 = Math.Min(z0 + 1, depth - 1);
		float tx = sourceX - x0, tz = sourceZ - z0;
		float north = Rng.Lerp(_oceanDistance[z0 * width + x0],
			_oceanDistance[z0 * width + x1], tx);
		float south = Rng.Lerp(_oceanDistance[z1 * width + x0],
			_oceanDistance[z1 * width + x1], tx);
		return Rng.Lerp(north, south, tz);
	}

	private float ApplyCoastalGrade(float guide, float oceanDistance)
	{
		if (oceanDistance >= CoastalGradePixels) return guide;
		float inland = Rng.Smoothstep(0f, CoastalGradePixels, oceanDistance);
		return Rng.Lerp(_atlas.SeaLevel, guide, inland);
	}

	private int WaterBodyAt(int globalX, int globalZ)
	{
		if (globalX < 0 || globalZ < 0 || globalX >= _atlas.Width || globalZ >= _atlas.Depth) return 0;
		int width = _land.GetWidth(), depth = _land.GetHeight();
		float sourceX = (globalX + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		float sourceZ = (globalZ + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		sourceX = Math.Clamp(sourceX, 0f, width - 1f);
		sourceZ = Math.Clamp(sourceZ, 0f, depth - 1f);
		int x0 = (int)MathF.Floor(sourceX), z0 = (int)MathF.Floor(sourceZ);
		int x1 = Math.Min(x0 + 1, width - 1), z1 = Math.Min(z0 + 1, depth - 1);
		float tx = sourceX - x0, tz = sourceZ - z0;
		int selected = -1;
		float selectedWeight = -1f;
		Consider(x0, z0, (1f - tx) * (1f - tz));
		Consider(x1, z0, tx * (1f - tz));
		Consider(x0, z1, (1f - tx) * tz);
		Consider(x1, z1, tx * tz);
		return selected >= 0 ? selected : 0;

		void Consider(int px, int pz, float weight)
		{
			int label = _waterBody[pz * width + px];
			if (label < 0 || weight < selectedWeight) return;
			if (Mathf.IsEqualApprox(weight, selectedWeight) && selected >= 0 && label >= selected) return;
			selected = label;
			selectedWeight = weight;
		}
	}

	/// <summary>
	/// The generated water field says where a channel is; the authored elevation
	/// still decides its altitude. A five-pixel minimum finds the local valley
	/// floor before profile noise, preventing a river from climbing a noisy bank.
	/// Interpolating the pixel guides across their registered block footprint is
	/// essential: nearest-pixel lookup made every eight-block source boundary a
	/// full-width artificial weir in the materialised water.
	/// </summary>
	private float ValleyGuideAt(int globalX, int globalZ)
	{
		int width = _land.GetWidth(), depth = _land.GetHeight();
		float sourceX = (globalX + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		float sourceZ = (globalZ + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		sourceX = Math.Clamp(sourceX, 0f, width - 1f);
		sourceZ = Math.Clamp(sourceZ, 0f, depth - 1f);
		int x0 = (int)MathF.Floor(sourceX), z0 = (int)MathF.Floor(sourceZ);
		int x1 = Math.Min(x0 + 1, width - 1), z1 = Math.Min(z0 + 1, depth - 1);
		float tx = sourceX - x0, tz = sourceZ - z0;
		float north = Rng.Lerp(_waterSurfaceGuide[z0 * width + x0],
			_waterSurfaceGuide[z0 * width + x1], tx);
		float south = Rng.Lerp(_waterSurfaceGuide[z1 * width + x0],
			_waterSurfaceGuide[z1 * width + x1], tx);
		return Rng.Lerp(north, south, tz);
	}

	/// <summary>
	/// Builds one compact source-pixel water altitude field for the whole atlas.
	/// Raw authored valleys retain their slope wherever it is already traversable;
	/// only connected permanent-water support edges steeper than one block per
	/// realised block are lowered. This is a source-layer derivative, not a
	/// continent-sized runtime array.
	/// </summary>
	private float[] BuildWaterSurfaceGuide()
	{
		int width = _land.GetWidth(), depth = _land.GetHeight(), count = width * depth;
		float[] guide = BuildRawWaterSurfaceGuide(width, depth);
		var support = new bool[count];
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int index = z * width + x;
			// A 0.90 source value is deliberately below the smallest bilinear
			// sample that can cross the 240/255 permanent-water threshold even
			// after the hydrology edge waver. Therefore every realised permanent
			// water cell has at least one base-support corner.
			support[index] = _waterBody[index] >= 0 ||
				(_water != null && _water.GetPixel(x, z).R >= WaterGuideSupportThreshold);
		}

		int[] owner = LabelWaterGuideComponents(support, width, depth);
		// Ownership is also the narrow future waterfall seam: an authored fall can
		// split one support component at its declared edge instead of weakening the
		// connected-water invariant everywhere. No waterfall edges are authored yet.
		owner = DilateWaterGuideOwners(owner, width, depth);
		RelaxWaterGuide(guide, owner, width, depth);
		return guide;
	}

	private float[] BuildRawWaterSurfaceGuide(int width, int depth)
	{
		int count = width * depth;
		var sourceGuide = new float[count];
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int index = z * width + x;
			float value = float.MaxValue;
			if (_waterBody[index] == -1)
			{
				float elevation = _elevation.GetPixel(x, z).R * (_atlas.Height - 1);
				if (elevation > _atlas.SeaLevel) value = elevation;
			}
			else if (_waterBody[index] > 0 &&
			         _inlandWaterBankHeight.TryGetValue(_waterBody[index], out float bank))
				value = bank;
			sourceGuide[index] = value;
		}

		// The former lazy 5x5 minimum is separable. Two five-sample passes
		// reproduce it exactly while avoiding a dictionary with one entry per
		// atlas source pixel when the connected-water constraint needs them all.
		var horizontal = new float[count];
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			float value = float.MaxValue;
			for (int dx = -2; dx <= 2; dx++)
				value = Math.Min(value, sourceGuide[z * width + Math.Clamp(x + dx, 0, width - 1)]);
			horizontal[z * width + x] = value;
		}

		var guide = new float[count];
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			float value = float.MaxValue;
			for (int dz = -2; dz <= 2; dz++)
				value = Math.Min(value, horizontal[Math.Clamp(z + dz, 0, depth - 1) * width + x]);
			if (value == float.MaxValue) value = _atlas.SeaLevel + 2f;
			int index = z * width + x;
			guide[index] = ApplyCoastalGrade(value, _oceanDistance[index]);
		}
		return guide;
	}

	private static int[] LabelWaterGuideComponents(bool[] support, int width, int depth)
	{
		var owner = new int[support.Length];
		var queue = new int[support.Length];
		int component = 0;
		for (int start = 0; start < support.Length; start++)
		{
			if (!support[start] || owner[start] != 0) continue;
			component++;
			int head = 0, tail = 0;
			queue[tail++] = start;
			owner[start] = component;
			while (head < tail)
			{
				int cell = queue[head++];
				int x = cell % width, z = cell / width;
				Visit(x - 1, z);
				Visit(x + 1, z);
				Visit(x, z - 1);
				Visit(x, z + 1);

				void Visit(int nx, int nz)
				{
					if (nx < 0 || nz < 0 || nx >= width || nz >= depth) return;
					int neighbour = nz * width + nx;
					if (!support[neighbour] || owner[neighbour] != 0) return;
					owner[neighbour] = component;
					queue[tail++] = neighbour;
				}
			}
		}
		return owner;
	}

	private static int[] DilateWaterGuideOwners(int[] baseOwner, int width, int depth)
	{
		var owner = (int[])baseOwner.Clone();
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int index = z * width + x;
			if (baseOwner[index] != 0) continue;
			int selectedOwner = 0, selectedDistance = int.MaxValue;
			for (int dz = -WaterGuideDilationPixels; dz <= WaterGuideDilationPixels; dz++)
			for (int dx = -WaterGuideDilationPixels; dx <= WaterGuideDilationPixels; dx++)
			{
				if (dx == 0 && dz == 0) continue;
				int nx = x + dx, nz = z + dz;
				if (nx < 0 || nz < 0 || nx >= width || nz >= depth) continue;
				int candidateOwner = baseOwner[nz * width + nx];
				if (candidateOwner == 0) continue;
				int distance = dx * dx + dz * dz;
				if (distance > selectedDistance ||
				    (distance == selectedDistance && selectedOwner != 0 &&
				     candidateOwner >= selectedOwner)) continue;
				selectedOwner = candidateOwner;
				selectedDistance = distance;
			}
			owner[index] = selectedOwner;
		}

		// Dilation reads only immutable base labels and assigns one existing
		// owner to each added pixel. It never propagates a dilated label and the
		// relaxation below crosses only equal owners, so touching one-pixel fringes
		// cannot merge disconnected lakes or manufacture a river connection.
		return owner;
	}

	private void RelaxWaterGuide(float[] guide, int[] owner, int width, int depth)
	{
		var queue = new Queue<int>();
		var queued = new bool[guide.Length];
		for (int i = 0; i < guide.Length; i++)
		{
			if (owner[i] == 0) continue;
			queue.Enqueue(i);
			queued[i] = true;
		}

		float maximumSourceStep = _atlas.BlocksPerPixel;
		while (queue.Count > 0)
		{
			int cell = queue.Dequeue();
			queued[cell] = false;
			int x = cell % width, z = cell / width;
			Relax(x - 1, z);
			Relax(x + 1, z);
			Relax(x, z - 1);
			Relax(x, z + 1);

			void Relax(int nx, int nz)
			{
				if (nx < 0 || nz < 0 || nx >= width || nz >= depth) return;
				int neighbour = nz * width + nx;
				if (owner[neighbour] == 0 || owner[neighbour] != owner[cell]) return;
				float candidate = guide[cell] + maximumSourceStep;
				if (guide[neighbour] <= candidate + 0.0001f) return;
				guide[neighbour] = candidate;
				if (queued[neighbour]) return;
				queue.Enqueue(neighbour);
				queued[neighbour] = true;
			}
		}

		// This source-scale assertion is the mathematical side of the runtime
		// atlas audit: bilinear interpolation spans BlocksPerPixel realised blocks,
		// so an equal-owner source edge no steeper than that can change by at most
		// one quantised water block between cardinal realised neighbours.
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int cell = z * width + x;
			if (owner[cell] == 0) continue;
			if (x + 1 < width) RequireBound(cell, cell + 1);
			if (z + 1 < depth) RequireBound(cell, cell + width);
		}

		void RequireBound(int a, int b)
		{
			if (owner[a] != owner[b]) return;
			float step = MathF.Abs(guide[a] - guide[b]);
			if (step <= maximumSourceStep + 0.0001f) return;
			throw new InvalidOperationException(
				$"water guide component {owner[a]} retains source step {step:0.###}, " +
				$"above {maximumSourceStep:0.###}");
		}
	}

	private int QuantizeWaterSurface(float height) =>
		Math.Clamp((int)MathF.Round(height), _atlas.SeaLevel, _atlas.Height - 2);

	/// <summary>
	/// Builds only the coarse transition patch needed by this sector, expanded by
	/// the largest declared blend width. The chamfer propagation is deterministic
	/// and keyed in atlas pixels, so two sector builds compute identical overlap.
	/// </summary>
	private RegionTransitionField BuildRegionTransitionField(int originX, int originZ, int width, int depth)
	{
		if (_region == null) return RegionTransitionField.Empty;
		int blocksPerPixel = _atlas.BlocksPerPixel;
		int margin = (int)MathF.Ceiling((_atlas.Provinces.Max(p => p.TransitionBlocks) + RegionBlendWander) /
			blocksPerPixel) + 2;
		int minPx = Math.Clamp(FloorDiv(originX, blocksPerPixel) - margin, 0, _region.GetWidth() - 1);
		int minPz = Math.Clamp(FloorDiv(originZ, blocksPerPixel) - margin, 0, _region.GetHeight() - 1);
		int maxPx = Math.Clamp(FloorDiv(originX + width - 1, blocksPerPixel) + margin, 0, _region.GetWidth() - 1);
		int maxPz = Math.Clamp(FloorDiv(originZ + depth - 1, blocksPerPixel) + margin, 0, _region.GetHeight() - 1);
		int patchWidth = maxPx - minPx + 1, patchDepth = maxPz - minPz + 1;
		int count = patchWidth * patchDepth;
		var owner = Enumerable.Repeat(-1, count).ToArray();
		var secondary = Enumerable.Repeat(-1, count).ToArray();
		var distance = Enumerable.Repeat(int.MaxValue, count).ToArray();
		var seedKey = Enumerable.Repeat(int.MaxValue, count).ToArray();

		for (int z = 0; z < patchDepth; z++)
		for (int x = 0; x < patchWidth; x++)
		{
			int colour = ColourKey(_region.GetPixel(minPx + x, minPz + z));
			if (_provinceByColour.TryGetValue(colour, out var province))
				owner[z * patchWidth + x] = _atlas.Provinces.IndexOf(province);
		}

		var queue = new PriorityQueue<TransitionNode, (int cost, int secondary, int seed, int cell)>();
		int[] nx = { -1, 0, 1, -1, 1, -1, 0, 1 };
		int[] nz = { -1, -1, -1, 0, 0, 1, 1, 1 };
		for (int z = 0; z < patchDepth; z++)
		for (int x = 0; x < patchWidth; x++)
		{
			int cell = z * patchWidth + x;
			if (owner[cell] < 0) continue;
			int neighbourOwner = int.MaxValue;
			for (int n = 0; n < nx.Length; n++)
			{
				int xx = x + nx[n], zz = z + nz[n];
				if (xx < 0 || zz < 0 || xx >= patchWidth || zz >= patchDepth) continue;
				int candidate = owner[zz * patchWidth + xx];
				if (candidate >= 0 && candidate != owner[cell]) neighbourOwner = Math.Min(neighbourOwner, candidate);
			}
			if (neighbourOwner == int.MaxValue) continue;
			distance[cell] = 5; // Half a source pixel places the conceptual border between the two colours.
			secondary[cell] = neighbourOwner;
			seedKey[cell] = (minPz + z) * _region.GetWidth() + minPx + x;
			queue.Enqueue(new TransitionNode(cell, 5, neighbourOwner, seedKey[cell]),
				(5, neighbourOwner, seedKey[cell], cell));
		}

		int maxCost = (int)MathF.Ceiling((_atlas.Provinces.Max(p => p.TransitionBlocks) + RegionBlendWander) /
			blocksPerPixel * 10f) + 14;
		while (queue.TryDequeue(out TransitionNode node, out _))
		{
			if (node.Cost != distance[node.Cell] || node.Secondary != secondary[node.Cell] ||
			    node.Seed != seedKey[node.Cell]) continue;
			int x = node.Cell % patchWidth, z = node.Cell / patchWidth;
			for (int n = 0; n < nx.Length; n++)
			{
				int xx = x + nx[n], zz = z + nz[n];
				if (xx < 0 || zz < 0 || xx >= patchWidth || zz >= patchDepth) continue;
				int next = zz * patchWidth + xx;
				if (owner[next] != owner[node.Cell]) continue;
				int cost = node.Cost + (nx[n] == 0 || nz[n] == 0 ? 10 : 14);
				if (cost > maxCost) continue;
				bool better = cost < distance[next] ||
				              cost == distance[next] && (node.Secondary < secondary[next] ||
				              node.Secondary == secondary[next] && node.Seed < seedKey[next]);
				if (!better) continue;
				distance[next] = cost;
				secondary[next] = node.Secondary;
				seedKey[next] = node.Seed;
				queue.Enqueue(new TransitionNode(next, cost, node.Secondary, node.Seed),
					(cost, node.Secondary, node.Seed, next));
			}
		}
		return new RegionTransitionField(minPx, minPz, patchWidth, patchDepth, owner, secondary, distance);
	}

	private RegionBlendSample RegionBlendAt(RegionTransitionField field, int globalX, int globalZ)
	{
		AtlasProvince fallback = ProvinceAt(globalX, globalZ) ?? _atlas.Provinces[0];
		if (!field.TrySample(globalX, globalZ, _atlas.BlocksPerPixel,
		    out int primaryIndex, out int secondaryIndex, out float distanceCost) || primaryIndex < 0)
			return new RegionBlendSample(fallback, null, 0f);

		AtlasProvince primary = _atlas.Provinces[primaryIndex];
		if (secondaryIndex < 0 || secondaryIndex == primaryIndex || float.IsPositiveInfinity(distanceCost) ||
		    primary.TransitionBlocks <= 0)
			return new RegionBlendSample(primary, null, 0f);
		// The categorical source is eight blocks per pixel. A low-frequency offset
		// removes visible eight-block bands without moving the authored boundary
		// outside its declared transition width.
		float distanceBlocks = MathF.Max(0f, distanceCost / 10f * _atlas.BlocksPerPixel +
			_regionBlendNoise.Value(globalX / RegionBlendWavelength, globalZ / RegionBlendWavelength) *
			RegionBlendWander);
		float weight = 0.5f * (1f - Rng.Smoothstep(0f, primary.TransitionBlocks, distanceBlocks));
		return new RegionBlendSample(primary, _atlas.Provinces[secondaryIndex], weight);
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
	}

	public AtlasSectorVerification Verify(int sectorX, int sectorZ, int apron = DefaultApron)
	{
		var first = Compile(sectorX, sectorZ, apron);
		var second = Compile(sectorX, sectorZ, apron);
		string firstHash = first.ContentHash();
		string secondHash = second.ContentHash();
		if (!string.Equals(firstHash, secondHash, StringComparison.Ordinal))
			throw new InvalidOperationException($"sector {sectorX},{sectorZ} changed between identical builds");

		int eastCells = 0, southCells = 0;
		if (sectorX + 1 < _atlas.Width / _atlas.SectorSize)
			eastCells = CompareOverlap(first, Compile(sectorX + 1, sectorZ, apron), "east");
		if (sectorZ + 1 < _atlas.Depth / _atlas.SectorSize)
			southCells = CompareOverlap(first, Compile(sectorX, sectorZ + 1, apron), "south");
		return new AtlasSectorVerification(firstHash, eastCells, southCells);
	}

	public string WriteArtifact(AtlasSectorData data, string resourcePath)
	{
		string absolute = ProjectSettings.GlobalizePath(resourcePath);
		string directory = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		using var stream = new MemoryStream(128 + data.CellCount * 15);
		using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
		{
			writer.Write(Encoding.ASCII.GetBytes("PTFLSEC2"));
			writer.Write(CompilerVersion);
			writer.Write(data.SectorX);
			writer.Write(data.SectorZ);
			writer.Write(data.OriginX);
			writer.Write(data.OriginZ);
			writer.Write(data.CoreSize);
			writer.Write(data.Apron);
			writer.Write(data.Width);
			writer.Write(data.Depth);
			writer.Write(data.WorldHeight);
			writer.Write(data.SeaLevel);
			writer.Write(data.SourceFingerprint);
			for (int i = 0; i < data.CellCount; i++)
			{
				writer.Write(data.Height[i]);
				writer.Write(data.WaterSurface[i]);
				writer.Write(data.Land[i]);
				writer.Write(data.Water[i]);
				writer.Write(data.Hydrology[i]);
				writer.Write(data.Profile[i]);
				writer.Write(data.SecondaryProfile[i]);
				writer.Write(data.ProfileBlend[i]);
				writer.Write(data.Surface[i]);
				writer.Write(data.Slope[i]);
				writer.Write(data.Aspect[i]);
				writer.Write(data.Curvature[i]);
				writer.Write(data.Wetness[i]);
			}
		}
		byte[] bytes = stream.ToArray();
		File.WriteAllBytes(absolute, bytes);
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
	}

	/// <summary>
	/// Load a disposable sector artifact only when it still describes the current
	/// authored sources. A stale derived file is not a degraded success: callers
	/// must rebuild it, otherwise an atlas edit can appear to have had no effect in
	/// the runtime review window.
	/// </summary>
	public AtlasSectorData ReadArtifact(string resourcePath)
	{
		string absolute = ProjectSettings.GlobalizePath(resourcePath);
		if (!File.Exists(absolute))
			throw new FileNotFoundException($"sector artifact '{resourcePath}' does not exist", absolute);

		using var stream = File.OpenRead(absolute);
		using var reader = new BinaryReader(stream, Encoding.UTF8, false);
		string magic = Encoding.ASCII.GetString(reader.ReadBytes(8));
		if (magic != "PTFLSEC2")
			throw new InvalidDataException($"sector artifact '{resourcePath}' has unknown magic '{magic}'");
		int version = reader.ReadInt32();
		if (version != CompilerVersion)
			throw new InvalidDataException(
				$"sector artifact '{resourcePath}' uses compiler {version}, expected {CompilerVersion}");

		int sectorX = reader.ReadInt32();
		int sectorZ = reader.ReadInt32();
		int originX = reader.ReadInt32();
		int originZ = reader.ReadInt32();
		int coreSize = reader.ReadInt32();
		int apron = reader.ReadInt32();
		int width = reader.ReadInt32();
		int depth = reader.ReadInt32();
		int worldHeight = reader.ReadInt32();
		int seaLevel = reader.ReadInt32();
		string fingerprint = reader.ReadString();

		if (fingerprint != SourceFingerprint)
			throw new InvalidDataException($"sector artifact '{resourcePath}' is stale against the accepted atlas sources");
		if (coreSize != _atlas.SectorSize || worldHeight != _atlas.Height || seaLevel != _atlas.SeaLevel ||
		    width != coreSize + apron * 2 || depth != coreSize + apron * 2)
			throw new InvalidDataException($"sector artifact '{resourcePath}' metadata does not match the atlas contract");
		if (originX != sectorX * coreSize - apron || originZ != sectorZ * coreSize - apron)
			throw new InvalidDataException($"sector artifact '{resourcePath}' has an invalid global origin");

		var data = new AtlasSectorData(sectorX, sectorZ, originX, originZ, coreSize, apron,
			width, depth, worldHeight, seaLevel, fingerprint);
		long expectedPayload = data.CellCount * 15L;
		if (stream.Length - stream.Position != expectedPayload)
			throw new InvalidDataException(
				$"sector artifact '{resourcePath}' has {stream.Length - stream.Position} payload bytes, expected {expectedPayload}");
		for (int i = 0; i < data.CellCount; i++)
		{
			data.Height[i] = reader.ReadUInt16();
			data.WaterSurface[i] = reader.ReadUInt16();
			data.Land[i] = reader.ReadByte();
			data.Water[i] = reader.ReadByte();
			data.Hydrology[i] = reader.ReadByte();
			data.Profile[i] = reader.ReadByte();
			data.SecondaryProfile[i] = reader.ReadByte();
			data.ProfileBlend[i] = reader.ReadByte();
			data.Surface[i] = reader.ReadByte();
			data.Slope[i] = reader.ReadByte();
			data.Aspect[i] = reader.ReadByte();
			data.Curvature[i] = reader.ReadByte();
			data.Wetness[i] = reader.ReadByte();
		}
		data.Validate(_atlas.BiomeCatalog.Profiles.Count);
		return data;
	}

	public void WritePreview(AtlasSectorData data, string resourcePath)
	{
		int size = data.CoreSize;
		var pixels = new byte[size * size * 3];
		for (int z = 0; z < size; z++)
		for (int x = 0; x < size; x++)
		{
			int source = (z + data.Apron) * data.Width + x + data.Apron;
			int target = (z * size + x) * 3;
			if (data.WaterSurface[source] > 0)
			{
				float waterDepth = data.WaterSurface[source] - data.Height[source];
				float deep = Rng.Smoothstep(1f, 9f, waterDepth);
				float altitude = Rng.Clamp((data.WaterSurface[source] - _atlas.SeaLevel) /
					(float)Math.Max(1, _atlas.Height - _atlas.SeaLevel), 0f, 1f);
				pixels[target] = (byte)MathF.Round(Rng.Lerp(126f + altitude * 18f, 91f, deep));
				pixels[target + 1] = (byte)MathF.Round(Rng.Lerp(184f + altitude * 12f, 144f, deep));
				pixels[target + 2] = (byte)MathF.Round(Rng.Lerp(222f + altitude * 10f, 197f, deep));
				continue;
			}

			(byte r, byte g, byte b) = PreviewColour(data.Profile[source]);
			(byte sr, byte sg, byte sb) = PreviewColour(data.SecondaryProfile[source]);
			float profileBlend = data.ProfileBlend[source] / 255f;
			r = (byte)MathF.Round(Rng.Lerp(r, sr, profileBlend));
			g = (byte)MathF.Round(Rng.Lerp(g, sg, profileBlend));
			b = (byte)MathF.Round(Rng.Lerp(b, sb, profileBlend));
			int left = x + data.Apron > 0 ? source - 1 : source;
			int up = z + data.Apron > 0 ? source - data.Width : source;
			float slopeLight = Math.Clamp((data.Height[left] - data.Height[source]) * 0.025f +
				(data.Height[up] - data.Height[source]) * 0.018f, -0.16f, 0.16f);
			bool contour = data.Height[left] / 8 != data.Height[source] / 8 ||
			               data.Height[up] / 8 != data.Height[source] / 8;
			float relief = (0.68f + data.Height[source] / (float)(_atlas.Height - 1) * 0.43f + slopeLight) *
			               (contour ? 0.86f : 1f);
			float rr = Math.Clamp(r * relief, 0f, 255f);
			float gg = Math.Clamp(g * relief, 0f, 255f);
			float bb = Math.Clamp(b * relief, 0f, 255f);
			if (data.Surface[source] == (byte)AtlasTerrainSurface.Cliff)
			{
				rr = Rng.Lerp(rr, 184f, 0.62f);
				gg = Rng.Lerp(gg, 178f, 0.62f);
				bb = Rng.Lerp(bb, 194f, 0.62f);
			}
			else if (data.Surface[source] == (byte)AtlasTerrainSurface.Shore)
			{
				rr = Rng.Lerp(rr, 218f, 0.44f);
				gg = Rng.Lerp(gg, 201f, 0.44f);
				bb = Rng.Lerp(bb, 159f, 0.44f);
			}
			float wetTint = data.Hydrology[source] == HydrologyBank ? 0.28f
				: data.Hydrology[source] == HydrologyFloodplain ? 0.12f : 0f;
			pixels[target] = (byte)MathF.Round(Rng.Lerp(rr, 139f, wetTint));
			pixels[target + 1] = (byte)MathF.Round(Rng.Lerp(gg, 174f, wetTint));
			pixels[target + 2] = (byte)MathF.Round(Rng.Lerp(bb, 174f, wetTint));
		}

		var image = Image.CreateFromData(size, size, false, Image.Format.Rgb8, pixels);
		string absolute = ProjectSettings.GlobalizePath(resourcePath);
		string directory = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		Error error = image.SavePng(absolute);
		if (error != Error.Ok) throw new IOException($"could not write sector preview '{resourcePath}': {error}");
	}

	private Image LoadRequiredLayer(AtlasLayerKind kind)
	{
		AtlasSourceLayer layer = _atlas.SourceLayers.Single(l => l.Kind == kind);
		if (layer.Status == AtlasLayerStatus.Planned)
			throw new InvalidOperationException($"source layer '{layer.Id}' must reach Blockout before sectors can compile");
		var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(layer.Path));
		if (image == null || image.IsEmpty())
			throw new InvalidOperationException($"could not load source layer '{layer.Id}' from '{layer.Path}'");
		return image;
	}

	private Image LoadOptionalLayer(AtlasLayerKind kind)
	{
		AtlasSourceLayer layer = _atlas.SourceLayers.Single(l => l.Kind == kind);
		if (layer.Status == AtlasLayerStatus.Planned) return null;
		var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(layer.Path));
		if (image == null || image.IsEmpty())
			throw new InvalidOperationException($"could not load source layer '{layer.Id}' from '{layer.Path}'");
		return image;
	}

	private float Sample(Image image, int globalX, int globalZ)
	{
		if (globalX < 0 || globalZ < 0 || globalX >= _atlas.Width || globalZ >= _atlas.Depth) return 0f;
		float px = (globalX + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		float pz = (globalZ + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		px = Math.Clamp(px, 0f, image.GetWidth() - 1f);
		pz = Math.Clamp(pz, 0f, image.GetHeight() - 1f);
		int x0 = Math.Clamp((int)MathF.Floor(px), 0, image.GetWidth() - 1);
		int z0 = Math.Clamp((int)MathF.Floor(pz), 0, image.GetHeight() - 1);
		int x1 = Math.Min(x0 + 1, image.GetWidth() - 1);
		int z1 = Math.Min(z0 + 1, image.GetHeight() - 1);
		float tx = px - MathF.Floor(px), tz = pz - MathF.Floor(pz);
		float a = Rng.Lerp(image.GetPixel(x0, z0).R, image.GetPixel(x1, z0).R, tx);
		float b = Rng.Lerp(image.GetPixel(x0, z1).R, image.GetPixel(x1, z1).R, tx);
		return Rng.Lerp(a, b, tz);
	}

	/// <summary>
	/// The accepted land/elevation images own the continent, but their source-pixel
	/// edges are not meant to become visible rulers in the voxel world. The sunset
	/// terrain displaced its shelf boundaries before rasterising them; sample the
	/// new map through the same low-frequency, six-cell-registered wander. Thirty
	/// blocks is tiny against the atlas and large enough to hide its eight-block
	/// source grid.
	/// </summary>
	private bool GuidedLandAt(int globalX, int globalZ)
	{
		if (globalX < 0 || globalZ < 0 || globalX >= _atlas.Width || globalZ >= _atlas.Depth)
			return false;
		int qx = FloorDiv(globalX, ProductionTerrainGrammar.EdgeGrid) *
			ProductionTerrainGrammar.EdgeGrid + ProductionTerrainGrammar.EdgeGrid / 2;
		int qz = FloorDiv(globalZ, ProductionTerrainGrammar.EdgeGrid) *
			ProductionTerrainGrammar.EdgeGrid + ProductionTerrainGrammar.EdgeGrid / 2;
		Vector2 warp = _legacyTerrain.GuideWarpAt(qx, qz);
		int sampleX = Math.Clamp((int)MathF.Round(globalX + warp.X), 0, _atlas.Width - 1);
		int sampleZ = Math.Clamp((int)MathF.Round(globalZ + warp.Y), 0, _atlas.Depth - 1);
		return Sample(_land, sampleX, sampleZ) >= 0.5f;
	}

	private bool TryGuidedLandElevation(int globalX, int globalZ, out float elevation)
	{
		int qx = FloorDiv(globalX, ProductionTerrainGrammar.EdgeGrid) *
			ProductionTerrainGrammar.EdgeGrid + ProductionTerrainGrammar.EdgeGrid / 2;
		int qz = FloorDiv(globalZ, ProductionTerrainGrammar.EdgeGrid) *
			ProductionTerrainGrammar.EdgeGrid + ProductionTerrainGrammar.EdgeGrid / 2;
		Vector2 warp = _legacyTerrain.GuideWarpAt(qx, qz);
		int sampleX = Math.Clamp((int)MathF.Round(globalX + warp.X), 0, _atlas.Width - 1);
		int sampleZ = Math.Clamp((int)MathF.Round(globalZ + warp.Y), 0, _atlas.Depth - 1);
		bool hasWarped = TrySampleLandElevation(sampleX, sampleZ, out float warped);
		bool hasOriginal = TrySampleLandElevation(globalX, globalZ, out float original);
		if (!hasWarped && !hasOriginal)
		{
			elevation = 0f;
			return false;
		}
		elevation = hasWarped && hasOriginal ? Rng.Lerp(original, warped, 0.72f)
			: hasWarped ? warped : original;
		return true;
	}

	/// <summary>
	/// Samples the authored elevation only where its companion land mask says the
	/// value is meaningful. Water pixels are no-data zeroes, not sea-level terrain;
	/// accepting their bilinear weight creates submerged dry banks at every coast.
	/// </summary>
	private bool TrySampleLandElevation(int globalX, int globalZ, out float elevation)
	{
		elevation = 0f;
		if (globalX < 0 || globalZ < 0 || globalX >= _atlas.Width || globalZ >= _atlas.Depth)
			return false;
		float px = (globalX + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		float pz = (globalZ + 0.5f) / _atlas.BlocksPerPixel - 0.5f;
		px = Math.Clamp(px, 0f, _elevation.GetWidth() - 1f);
		pz = Math.Clamp(pz, 0f, _elevation.GetHeight() - 1f);
		int x0 = Math.Clamp((int)MathF.Floor(px), 0, _elevation.GetWidth() - 1);
		int z0 = Math.Clamp((int)MathF.Floor(pz), 0, _elevation.GetHeight() - 1);
		int x1 = Math.Min(x0 + 1, _elevation.GetWidth() - 1);
		int z1 = Math.Min(z0 + 1, _elevation.GetHeight() - 1);
		float tx = px - MathF.Floor(px), tz = pz - MathF.Floor(pz);
		float total = 0f, acceptedWeight = 0f;
		Accumulate(x0, z0, (1f - tx) * (1f - tz));
		Accumulate(x1, z0, tx * (1f - tz));
		Accumulate(x0, z1, (1f - tx) * tz);
		Accumulate(x1, z1, tx * tz);
		if (acceptedWeight <= 0.00001f) return false;
		elevation = total / acceptedWeight;
		return true;

		void Accumulate(int x, int z, float weight)
		{
			if (weight <= 0f || _land.GetPixel(x, z).R < 0.5f) return;
			total += _elevation.GetPixel(x, z).R * weight;
			acceptedWeight += weight;
		}
	}

	/// <summary>
	/// Geometry responds to the same continuous authored hydrology, sampled on
	/// the atlas-wide natural-edge lattice. Wetness keeps the per-block sample;
	/// only height shaping is cell-registered so a bank becomes a few decisive
	/// voxel runs instead of hundreds of one-column teeth.
	/// </summary>
	private float GeometricWaterAt(int globalX, int globalZ)
	{
		int x = FloorDiv(globalX, NaturalEdgeCell) * NaturalEdgeCell + NaturalEdgeCell / 2;
		int z = FloorDiv(globalZ, NaturalEdgeCell) * NaturalEdgeCell + NaturalEdgeCell / 2;
		bool sourceLand = GuidedLandAt(x, z);
		float water = _water == null ? 0f : Sample(_water, x, z);
		if (sourceLand && water > 0f && water < 1f)
		{
			float transition = 4f * water * (1f - water);
			water += _hydrologyEdgeNoise.Fbm(x / 112f, z / 112f, 3) * 0.035f * transition;
			water = Rng.Clamp(water, 0f, 1f);
		}
		return sourceLand ? water : 1f;
	}

	private AtlasProvince ProvinceAt(int x, int z)
	{
		if (_region != null && x >= 0 && z >= 0 && x < _atlas.Width && z < _atlas.Depth)
		{
			Color colour = SampleNearest(_region, x, z);
			if (_provinceByColour.TryGetValue(ColourKey(colour), out var painted)) return painted;
		}
		AtlasProvince result = null;
		foreach (var province in _atlas.Provinces)
			if (Contains(province.Boundary, x, z)) result = province;
		return result;
	}

	private static bool Contains(IReadOnlyList<BlockPoint> polygon, int x, int z)
	{
		bool inside = false;
		for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
		{
			BlockPoint a = polygon[i], b = polygon[j];
			bool crosses = (a.Z > z) != (b.Z > z) &&
			               x < (b.X - a.X) * (z - a.Z) / (double)(b.Z - a.Z) + a.X;
			if (crosses) inside = !inside;
		}
		return inside;
	}

	private Color SampleNearest(Image image, int globalX, int globalZ)
	{
		int px = Math.Clamp(globalX / _atlas.BlocksPerPixel, 0, image.GetWidth() - 1);
		int pz = Math.Clamp(globalZ / _atlas.BlocksPerPixel, 0, image.GetHeight() - 1);
		return image.GetPixel(px, pz);
	}

	private Noise2D NoiseFor(BiomeBuildProfile profile, int band)
	{
		if (!_profileNoise.TryGetValue(profile.Id, out var fields))
		{
			fields = new Noise2D[Math.Max(1, profile.NoiseBands.Count)];
			for (int i = 0; i < fields.Length; i++)
				fields[i] = new Noise2D(unchecked(_worldSeed ^ Rng.StableHash($"atlas:{profile.Id}:{i}")));
			_profileNoise[profile.Id] = fields;
		}
		return fields[Math.Min(band, fields.Length - 1)];
	}

	private Noise2D RidgeNoise(BiomeBuildProfile profile)
	{
		if (!_ridgeNoise.TryGetValue(profile.Id, out Noise2D noise))
		{
			noise = new Noise2D(unchecked(_worldSeed ^ Rng.StableHash($"atlas:{profile.Id}:ridge")));
			_ridgeNoise[profile.Id] = noise;
		}
		return noise;
	}

	private Noise2D MountainNoise(BiomeBuildProfile profile, string layer)
	{
		string key = $"{profile.Id}:{layer}";
		if (!_mountainNoise.TryGetValue(key, out Noise2D noise))
		{
			noise = new Noise2D(unchecked(_worldSeed ^
				Rng.StableHash($"atlas:{profile.Id}:mountain:{layer}")));
			_mountainNoise[key] = noise;
		}
		return noise;
	}

	private Noise2D SelectionNoise(BiomeBuildProfile profile)
	{
		if (!_selectionNoise.TryGetValue(profile.Id, out Noise2D noise))
		{
			noise = new Noise2D(unchecked(_worldSeed ^ Rng.StableHash($"atlas:{profile.Id}:selection")));
			_selectionNoise[profile.Id] = noise;
		}
		return noise;
	}

	private int QuantizeAboveSea(int height, int step)
	{
		int aboveSea = Math.Max(1, height - _atlas.SeaLevel);
		return _atlas.SeaLevel + (int)MathF.Round(aboveSea / (float)step) * step;
	}

	private int CompareOverlap(AtlasSectorData a, AtlasSectorData b, string edge)
	{
		int minX = Math.Max(a.OriginX, b.OriginX);
		int minZ = Math.Max(a.OriginZ, b.OriginZ);
		int maxX = Math.Min(a.OriginX + a.Width, b.OriginX + b.Width);
		int maxZ = Math.Min(a.OriginZ + a.Depth, b.OriginZ + b.Depth);
		int compared = 0;
		for (int z = minZ; z < maxZ; z++)
		for (int x = minX; x < maxX; x++)
		{
			int ai = (z - a.OriginZ) * a.Width + x - a.OriginX;
			int bi = (z - b.OriginZ) * b.Width + x - b.OriginX;
			if (a.Height[ai] != b.Height[bi] || a.WaterSurface[ai] != b.WaterSurface[bi] ||
			    a.Land[ai] != b.Land[bi] || a.Water[ai] != b.Water[bi] ||
			    a.Hydrology[ai] != b.Hydrology[bi] || a.Profile[ai] != b.Profile[bi] ||
			    a.SecondaryProfile[ai] != b.SecondaryProfile[bi] ||
			    a.ProfileBlend[ai] != b.ProfileBlend[bi] || a.Surface[ai] != b.Surface[bi] ||
			    a.Slope[ai] != b.Slope[bi] || a.Aspect[ai] != b.Aspect[bi] ||
			    a.Curvature[ai] != b.Curvature[bi] || a.Wetness[ai] != b.Wetness[bi])
				throw new InvalidOperationException($"{edge} seam mismatch at global {x},{z}");
			compared++;
		}
		return compared;
	}

	private string BuildSourceFingerprint(string atlasResourcePath)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		hash.AppendData(Encoding.UTF8.GetBytes($"compiler:{CompilerVersion};seed:{_worldSeed};"));
		void AddFile(string resourcePath)
		{
			byte[] path = Encoding.UTF8.GetBytes(resourcePath);
			hash.AppendData(path);
			hash.AppendData(File.ReadAllBytes(ProjectSettings.GlobalizePath(resourcePath)));
		}
		AddFile(atlasResourcePath);
		AddFile(_atlas.BiomeCatalogPath);
		foreach (var layer in _atlas.SourceLayers.Where(l => l.Status != AtlasLayerStatus.Planned)
			         .OrderBy(l => l.Id, StringComparer.Ordinal))
			AddFile(layer.Path);
		return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
	}

	private static (byte r, byte g, byte b) ParseColour(string html)
	{
		return (Convert.ToByte(html.Substring(1, 2), 16), Convert.ToByte(html.Substring(3, 2), 16),
			Convert.ToByte(html.Substring(5, 2), 16));
	}

	private (byte r, byte g, byte b) PreviewColour(int profileIndex)
	{
		if (profileIndex >= 0 && profileIndex < _atlas.BiomeCatalog.Profiles.Count)
		{
			string id = _atlas.BiomeCatalog.Profiles[profileIndex].Id;
			if (_previewColourByProfile.TryGetValue(id, out var colour)) return colour;
		}
		return ParseColour("#aebf91");
	}

	private static int ColourKey((byte r, byte g, byte b) colour) =>
		(colour.r << 16) | (colour.g << 8) | colour.b;

	private static int ColourKey(Color colour)
	{
		int r = Math.Clamp((int)MathF.Round(colour.R * 255f), 0, 255);
		int g = Math.Clamp((int)MathF.Round(colour.G * 255f), 0, 255);
		int b = Math.Clamp((int)MathF.Round(colour.B * 255f), 0, 255);
		return (r << 16) | (g << 8) | b;
	}

	private readonly record struct HydrologySample(float FloodplainStart, float BankStart,
		int FloodplainRise, int BankRise, int SurfaceDrop, int WaterDepth, int PreserveCutRise);

	private readonly record struct ReliefSample(float Height, int TerraceStep, float TerraceBlend);

	private readonly record struct RegionBlendSample(AtlasProvince Primary, AtlasProvince Secondary,
		float SecondaryWeight);

	private readonly record struct TransitionNode(int Cell, int Cost, int Secondary, int Seed);

	private sealed class RegionTransitionField
	{
		public static readonly RegionTransitionField Empty = new(0, 0, 0, 0,
			Array.Empty<int>(), Array.Empty<int>(), Array.Empty<int>());

		private readonly int _originX;
		private readonly int _originZ;
		private readonly int _width;
		private readonly int _depth;
		private readonly int[] _owner;
		private readonly int[] _secondary;
		private readonly int[] _distance;

		public RegionTransitionField(int originX, int originZ, int width, int depth,
			int[] owner, int[] secondary, int[] distance)
		{
			_originX = originX;
			_originZ = originZ;
			_width = width;
			_depth = depth;
			_owner = owner;
			_secondary = secondary;
			_distance = distance;
		}

		public bool TrySample(int globalX, int globalZ, int blocksPerPixel,
			out int owner, out int secondary, out float distance)
		{
			owner = -1;
			secondary = -1;
			distance = float.PositiveInfinity;
			if (globalX < 0 || globalZ < 0 || _width == 0 || _depth == 0) return false;
			int x = globalX / blocksPerPixel - _originX;
			int z = globalZ / blocksPerPixel - _originZ;
			if (x < 0 || z < 0 || x >= _width || z >= _depth) return false;
			int index = z * _width + x;
			owner = _owner[index];
			secondary = _secondary[index];
			if (owner < 0 || secondary < 0 || _distance[index] == int.MaxValue) return true;
			int pairOwner = owner, pairSecondary = secondary;

			float fx = (globalX + 0.5f) / blocksPerPixel - 0.5f - _originX;
			float fz = (globalZ + 0.5f) / blocksPerPixel - 0.5f - _originZ;
			int x0 = Math.Clamp((int)MathF.Floor(fx), 0, _width - 1);
			int z0 = Math.Clamp((int)MathF.Floor(fz), 0, _depth - 1);
			int x1 = Math.Min(x0 + 1, _width - 1);
			int z1 = Math.Min(z0 + 1, _depth - 1);
			float tx = Math.Clamp(fx - MathF.Floor(fx), 0f, 1f);
			float tz = Math.Clamp(fz - MathF.Floor(fz), 0f, 1f);
			float total = 0f, weighted = 0f;
			Accumulate(x0, z0, (1f - tx) * (1f - tz));
			Accumulate(x1, z0, tx * (1f - tz));
			Accumulate(x0, z1, (1f - tx) * tz);
			Accumulate(x1, z1, tx * tz);
			distance = total > 0f ? weighted / total : _distance[index];
			return true;

			void Accumulate(int sx, int sz, float weight)
			{
				if (weight <= 0f) return;
				int sample = sz * _width + sx;
				bool samePair = _owner[sample] == pairOwner && _secondary[sample] == pairSecondary;
				bool reversePair = _owner[sample] == pairSecondary && _secondary[sample] == pairOwner;
				if ((!samePair && !reversePair) || _distance[sample] == int.MaxValue) return;
				weighted += _distance[sample] * weight;
				total += weight;
			}
		}
	}
}

public enum AtlasTerrainSurface : byte
{
	Cap = 0,
	Shore = 1,
	Cliff = 2,
	Underwater = 3,
}

public sealed class AtlasSectorData
{
	public int SectorX { get; }
	public int SectorZ { get; }
	public int OriginX { get; }
	public int OriginZ { get; }
	public int CoreSize { get; }
	public int Apron { get; }
	public int Width { get; }
	public int Depth { get; }
	public int WorldHeight { get; }
	public int SeaLevel { get; }
	public string SourceFingerprint { get; }
	public ushort[] Height { get; }
	public ushort[] WaterSurface { get; }
	public byte[] Land { get; }
	public byte[] Water { get; }
	public byte[] Hydrology { get; }
	public byte[] Profile { get; }
	public byte[] SecondaryProfile { get; }
	public byte[] ProfileBlend { get; }
	public byte[] Surface { get; }
	public byte[] Slope { get; }
	public byte[] Aspect { get; }
	public byte[] Curvature { get; }
	public byte[] Wetness { get; }
	public int CellCount => Width * Depth;

	public AtlasSectorData(int sectorX, int sectorZ, int originX, int originZ, int coreSize, int apron,
		int width, int depth, int worldHeight, int seaLevel, string sourceFingerprint)
	{
		SectorX = sectorX;
		SectorZ = sectorZ;
		OriginX = originX;
		OriginZ = originZ;
		CoreSize = coreSize;
		Apron = apron;
		Width = width;
		Depth = depth;
		WorldHeight = worldHeight;
		SeaLevel = seaLevel;
		SourceFingerprint = sourceFingerprint;
		Height = new ushort[CellCount];
		WaterSurface = new ushort[CellCount];
		Land = new byte[CellCount];
		Water = new byte[CellCount];
		Hydrology = new byte[CellCount];
		Profile = new byte[CellCount];
		SecondaryProfile = new byte[CellCount];
		ProfileBlend = new byte[CellCount];
		Surface = new byte[CellCount];
		Slope = new byte[CellCount];
		Aspect = new byte[CellCount];
		Curvature = new byte[CellCount];
		Wetness = new byte[CellCount];
	}

	public string ContentHash()
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		hash.AppendData(Encoding.UTF8.GetBytes(SourceFingerprint));
		hash.AppendData(BitConverter.GetBytes(SectorX));
		hash.AppendData(BitConverter.GetBytes(SectorZ));
		hash.AppendData(BitConverter.GetBytes(Apron));
		for (int i = 0; i < CellCount; i++)
		{
			hash.AppendData(BitConverter.GetBytes(Height[i]));
			hash.AppendData(BitConverter.GetBytes(WaterSurface[i]));
			hash.AppendData(new[] { Land[i], Water[i], Hydrology[i], Profile[i],
				SecondaryProfile[i], ProfileBlend[i], Surface[i], Slope[i], Aspect[i],
				Curvature[i], Wetness[i] });
		}
		return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
	}

	public void Validate(int profileCount)
	{
		for (int i = 0; i < CellCount; i++)
		{
			if (Profile[i] >= profileCount || SecondaryProfile[i] >= profileCount)
				throw new InvalidOperationException($"sector cell {i} references a profile outside 0..{profileCount - 1}");
			if (ProfileBlend[i] > 128)
				throw new InvalidOperationException($"sector cell {i} gives the secondary profile more than half ownership");
			if (ProfileBlend[i] > 0 && Profile[i] == SecondaryProfile[i])
				throw new InvalidOperationException($"sector cell {i} blends a profile with itself");
			if (Surface[i] > (byte)AtlasTerrainSurface.Underwater)
				throw new InvalidOperationException($"sector cell {i} has unknown terrain surface {Surface[i]}");
			if (WaterSurface[i] > 0)
			{
				if (Land[i] != 0 || Height[i] >= WaterSurface[i] || Hydrology[i] != 3)
					throw new InvalidOperationException($"sector cell {i} has inconsistent water surface, bed or hydrology data");
				if (Surface[i] != (byte)AtlasTerrainSurface.Underwater || Wetness[i] != 255)
					throw new InvalidOperationException($"sector cell {i} has inconsistent underwater surface metrics");
			}
			else
			{
				if (Land[i] == 0 || Hydrology[i] == 3)
					throw new InvalidOperationException($"sector cell {i} is marked as water without a water surface");
				if (Surface[i] == (byte)AtlasTerrainSurface.Underwater)
					throw new InvalidOperationException($"sector cell {i} is dry but has an underwater surface class");
			}
		}
		for (int z = 0; z < Depth; z++)
		for (int x = 0; x < Width; x++)
		{
			int current = z * Width + x;
			if (x + 1 < Width) RequireDryAboveWater(current, current + 1);
			if (z + 1 < Depth) RequireDryAboveWater(current, current + Width);
		}

		void RequireDryAboveWater(int a, int b)
		{
			bool aWater = WaterSurface[a] > 0;
			bool bWater = WaterSurface[b] > 0;
			if (aWater == bWater) return;
			int dry = aWater ? b : a;
			int wet = aWater ? a : b;
			if (Height[dry] >= WaterSurface[wet] + 1) return;
			int dryX = dry % Width, dryZ = dry / Width;
			throw new InvalidOperationException(
				$"sector dry boundary {OriginX + dryX},{OriginZ + dryZ} has height {Height[dry]} " +
				$"below adjacent water surface {WaterSurface[wet]}");
		}
	}

	public AtlasSectorStatistics CoreStatistics()
	{
		int landCells = 0, waterCells = 0, floodplainCells = 0, bankCells = 0, blendedCells = 0;
		int cliffCells = 0, shoreCells = 0;
		int waterStepEdges = 0, severeWaterStepEdges = 0, maxWaterStep = 0;
		int maxWaterStepX = 0, maxWaterStepZ = 0;
		int submergedDryBoundaryEdges = 0, maxSubmergedDryDepth = 0;
		int maxSubmergedDryX = 0, maxSubmergedDryZ = 0;
		int minHeight = int.MaxValue, maxHeight = int.MinValue;
		int minWaterSurface = int.MaxValue, maxWaterSurface = int.MinValue;
		for (int z = Apron; z < Apron + CoreSize; z++)
		for (int x = Apron; x < Apron + CoreSize; x++)
		{
			int index = z * Width + x;
			MeasureDryBoundary(index, index + 1, x + 1 < Width, x, z, 1, 0);
			MeasureDryBoundary(index, index + Width, z + 1 < Depth, x, z, 0, 1);
			if (WaterSurface[index] > 0)
			{
				waterCells++;
				minWaterSurface = Math.Min(minWaterSurface, WaterSurface[index]);
				maxWaterSurface = Math.Max(maxWaterSurface, WaterSurface[index]);
				// The east/south apron neighbours let this core own the cross-sector
				// adjacency as well. Omitting the final core row/column made the summary
				// blind to the exact seam whose persisted payload is meant to protect.
				MeasureWaterStep(index, index + 1, x + 1 < Width, x, z);
				MeasureWaterStep(index, index + Width, z + 1 < Depth, x, z);
			}
			if (Hydrology[index] == 1) floodplainCells++;
			else if (Hydrology[index] == 2) bankCells++;
			if (Surface[index] == (byte)AtlasTerrainSurface.Cliff) cliffCells++;
			else if (Surface[index] == (byte)AtlasTerrainSurface.Shore) shoreCells++;
			if (ProfileBlend[index] > 0) blendedCells++;
			if (Land[index] == 0) continue;
			landCells++;
			minHeight = Math.Min(minHeight, Height[index]);
			maxHeight = Math.Max(maxHeight, Height[index]);
		}
		if (landCells == 0) minHeight = maxHeight = SeaLevel;
		if (waterCells == 0) minWaterSurface = maxWaterSurface = 0;
		return new AtlasSectorStatistics(landCells, waterCells, floodplainCells, bankCells, blendedCells,
			cliffCells, shoreCells, waterStepEdges, severeWaterStepEdges, maxWaterStep,
			OriginX + maxWaterStepX, OriginZ + maxWaterStepZ,
			submergedDryBoundaryEdges, maxSubmergedDryDepth,
			OriginX + maxSubmergedDryX, OriginZ + maxSubmergedDryZ,
			minHeight, maxHeight, minWaterSurface, maxWaterSurface);

		void MeasureDryBoundary(int current, int neighbour, bool inside,
			int x, int z, int neighbourX, int neighbourZ)
		{
			if (!inside) return;
			bool currentWater = WaterSurface[current] > 0;
			bool neighbourWater = WaterSurface[neighbour] > 0;
			if (currentWater == neighbourWater) return;
			int dry = currentWater ? neighbour : current;
			int wet = currentWater ? current : neighbour;
			int depth = WaterSurface[wet] + 1 - Height[dry];
			if (depth <= 0) return;
			submergedDryBoundaryEdges++;
			if (depth <= maxSubmergedDryDepth) return;
			maxSubmergedDryDepth = depth;
			maxSubmergedDryX = x + (currentWater ? neighbourX : 0);
			maxSubmergedDryZ = z + (currentWater ? neighbourZ : 0);
		}

		void MeasureWaterStep(int current, int neighbour, bool insideCore, int x, int z)
		{
			if (!insideCore || WaterSurface[neighbour] == 0) return;
			int step = Math.Abs(WaterSurface[current] - WaterSurface[neighbour]);
			if (step == 0) return;
			waterStepEdges++;
			if (step > 1) severeWaterStepEdges++;
			if (step > maxWaterStep)
			{
				maxWaterStep = step;
				maxWaterStepX = x;
				maxWaterStepZ = z;
			}
		}
	}
}

public readonly record struct AtlasSectorVerification(string ContentHash, int EastOverlapCells,
	int SouthOverlapCells);

public readonly record struct AtlasSectorStatistics(int LandCells, int WaterCells, int FloodplainCells,
	int BankCells, int BlendedCells, int CliffCells, int ShoreCells, int WaterStepEdges,
	int SevereWaterStepEdges, int MaxWaterStep, int MaxWaterStepX, int MaxWaterStepZ,
	int SubmergedDryBoundaryEdges, int MaxSubmergedDryDepth,
	int MaxSubmergedDryX, int MaxSubmergedDryZ,
	int MinHeight, int MaxHeight, int MinWaterSurface, int MaxWaterSurface);
