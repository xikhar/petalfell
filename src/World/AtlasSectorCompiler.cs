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
	public const int CompilerVersion = 3;
	public const int DefaultApron = 24;
	private const byte PermanentWaterValue = 240;
	private const byte HydrologyDry = 0;
	private const byte HydrologyFloodplain = 1;
	private const byte HydrologyBank = 2;
	private const byte HydrologyChannel = 3;
	private const float RegionBlendWavelength = 96f;
	private const float RegionBlendWander = 24f;

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
	private readonly Noise2D _regionBlendNoise;
	private readonly Dictionary<int, float> _valleyGuideCache = new();
	private readonly int[] _waterBody;
	private readonly Dictionary<int, float> _inlandWaterBankHeight;

	public string SourceFingerprint { get; }

	public AtlasSectorCompiler(WorldAtlasDefinition atlas, int worldSeed, string atlasResourcePath)
	{
		_atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		_worldSeed = worldSeed;
		_regionBlendNoise = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:region-transition")));
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
		RegionTransitionField transitions = BuildRegionTransitionField(originX, originZ, width, depth);

		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int globalX = originX + x;
			int globalZ = originZ + z;
			int index = z * width + x;
			RegionBlendSample region = RegionBlendAt(transitions, globalX, globalZ);
			BiomeBuildProfile profile = PrimaryProfile(region.Primary);
			BiomeBuildProfile secondary = PrimaryProfile(region.Secondary ?? region.Primary);
			int profileIndex = _profileIndices[profile.Id];
			int secondaryIndex = _profileIndices[secondary.Id];
			float profileBlend = region.SecondaryWeight;
			bool sourceLand = Sample(_land, globalX, globalZ) >= 0.5f;
			float water = _water == null ? 0f : Sample(_water, globalX, globalZ);
			if (!sourceLand) water = 1f;

			data.Water[index] = (byte)Math.Clamp((int)MathF.Round(water * 255f), 0, 255);
			data.Profile[index] = (byte)profileIndex;
			data.SecondaryProfile[index] = (byte)secondaryIndex;
			data.ProfileBlend[index] = (byte)Math.Clamp((int)MathF.Round(profileBlend * 255f), 0, 255);

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
					data.Height[index] = (ushort)Math.Clamp(
						(int)MathF.Round(surface - 9f + seaRelief * 3f), 1, surface - 1);
				}
				else
				{
					float bankHeight = _inlandWaterBankHeight.TryGetValue(waterBody, out float found)
						? found : _atlas.SeaLevel + 2f;
					surface = QuantizeWaterSurface(bankHeight - hydrology.SurfaceDrop);
					data.Height[index] = (ushort)Math.Clamp(surface - hydrology.WaterDepth, 1, surface - 1);
				}
				data.Land[index] = 0;
				data.WaterSurface[index] = (ushort)surface;
				data.Hydrology[index] = HydrologyChannel;
				continue;
			}

			float authoredHeight = Sample(_elevation, globalX, globalZ) * (_atlas.Height - 1);
			float height = Rng.Lerp(HeightForProfile(authoredHeight, profile, globalX, globalZ),
				HeightForProfile(authoredHeight, secondary, globalX, globalZ), profileBlend);
			float valleyGuide = ValleyGuideAt(globalX, globalZ);
			int waterSurface = QuantizeWaterSurface(valleyGuide - hydrology.SurfaceDrop);
			if (data.Water[index] >= PermanentWaterValue)
			{
				data.Land[index] = 0;
				data.WaterSurface[index] = (ushort)waterSurface;
				data.Height[index] = (ushort)Math.Clamp(waterSurface - hydrology.WaterDepth, 1, waterSurface - 1);
				data.Hydrology[index] = HydrologyChannel;
				continue;
			}

			float floodplain = Rng.Smoothstep(hydrology.FloodplainStart, hydrology.BankStart, water);
			float bank = Rng.Smoothstep(hydrology.BankStart, AtlasHydrologyProfile.PermanentWaterStart, water);
			float floodplainTarget = waterSurface + hydrology.FloodplainRise;
			if (height > floodplainTarget)
				height = Rng.Lerp(height, floodplainTarget, floodplain * 0.82f);
			float bankTarget = waterSurface + hydrology.BankRise;
			if (height > bankTarget) height = Rng.Lerp(height, bankTarget, bank);

			int terraceStep = Math.Max(1, (int)MathF.Round(Rng.Lerp(
				profile.TerraceStep, secondary.TerraceStep, profileBlend)));
			int quantized = QuantizeAboveSea((int)MathF.Round(height), terraceStep);
			data.Height[index] = (ushort)Math.Clamp(quantized, _atlas.SeaLevel + 1, _atlas.Height - 2);
			data.Land[index] = 255;
			data.Hydrology[index] = water >= hydrology.BankStart
				? HydrologyBank
				: water >= hydrology.FloodplainStart ? HydrologyFloodplain : HydrologyDry;
		}

		data.Validate(_atlas.BiomeCatalog.Profiles.Count);
		return data;
	}

	private BiomeBuildProfile PrimaryProfile(AtlasProvince province)
	{
		string id = province?.BiomeProfileIds.FirstOrDefault() ?? "river-waist";
		return _profiles.TryGetValue(id, out var found) ? found : _atlas.BiomeCatalog.Profiles[0];
	}

	private float HeightForProfile(float authoredHeight, BiomeBuildProfile profile, int globalX, int globalZ)
	{
		float height = authoredHeight;
		for (int band = 0; band < profile.NoiseBands.Count; band++)
		{
			AtlasNoiseBand settings = profile.NoiseBands[band];
			height += NoiseFor(profile, band).Value(
				globalX / settings.Wavelength, globalZ / settings.Wavelength) * settings.Amplitude;
		}
		return height;
	}

	private static HydrologySample BlendHydrology(AtlasHydrologyProfile a, AtlasHydrologyProfile b, float t) => new(
		Rng.Lerp(a.FloodplainStart, b.FloodplainStart, t),
		Rng.Lerp(a.BankStart, b.BankStart, t),
		(int)MathF.Round(Rng.Lerp(a.FloodplainRise, b.FloodplainRise, t)),
		(int)MathF.Round(Rng.Lerp(a.BankRise, b.BankRise, t)),
		(int)MathF.Round(Rng.Lerp(a.SurfaceDrop, b.SurfaceDrop, t)),
		(int)MathF.Round(Rng.Lerp(a.WaterDepth, b.WaterDepth, t)));

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

	private int WaterBodyAt(int globalX, int globalZ)
	{
		if (globalX < 0 || globalZ < 0 || globalX >= _atlas.Width || globalZ >= _atlas.Depth) return 0;
		int px = Math.Clamp(globalX / _atlas.BlocksPerPixel, 0, _land.GetWidth() - 1);
		int pz = Math.Clamp(globalZ / _atlas.BlocksPerPixel, 0, _land.GetHeight() - 1);
		return Math.Max(0, _waterBody[pz * _land.GetWidth() + px]);
	}

	/// <summary>
	/// The generated water field says where a channel is; the authored elevation
	/// still decides its altitude. A five-pixel minimum finds the local valley
	/// floor before profile noise, preventing a river from climbing a noisy bank.
	/// </summary>
	private float ValleyGuideAt(int globalX, int globalZ)
	{
		int width = _land.GetWidth(), depth = _land.GetHeight();
		int px = Math.Clamp(globalX / _atlas.BlocksPerPixel, 0, width - 1);
		int pz = Math.Clamp(globalZ / _atlas.BlocksPerPixel, 0, depth - 1);
		int key = pz * width + px;
		if (_valleyGuideCache.TryGetValue(key, out float cached)) return cached;

		float guide = float.MaxValue;
		for (int dz = -2; dz <= 2; dz++)
		for (int dx = -2; dx <= 2; dx++)
		{
			int sx = Math.Clamp(px + dx, 0, width - 1);
			int sz = Math.Clamp(pz + dz, 0, depth - 1);
			int source = sz * width + sx;
			if (_waterBody[source] == -1)
			{
				float elevation = _elevation.GetPixel(sx, sz).R * (_atlas.Height - 1);
				if (elevation > _atlas.SeaLevel) guide = Math.Min(guide, elevation);
			}
			else if (_waterBody[source] > 0 &&
			         _inlandWaterBankHeight.TryGetValue(_waterBody[source], out float bank))
				guide = Math.Min(guide, bank);
		}
		if (guide == float.MaxValue) guide = _atlas.SeaLevel + 2f;
		_valleyGuideCache[key] = guide;
		return guide;
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
		using var stream = new MemoryStream(128 + data.CellCount * 10);
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
			}
		}
		byte[] bytes = stream.ToArray();
		File.WriteAllBytes(absolute, bytes);
		return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
			    a.ProfileBlend[ai] != b.ProfileBlend[bi])
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
		int FloodplainRise, int BankRise, int SurfaceDrop, int WaterDepth);

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
				SecondaryProfile[i], ProfileBlend[i] });
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
			if (WaterSurface[i] > 0)
			{
				if (Land[i] != 0 || Height[i] >= WaterSurface[i] || Hydrology[i] != 3)
					throw new InvalidOperationException($"sector cell {i} has inconsistent water surface, bed or hydrology data");
			}
			else if (Land[i] == 0 || Hydrology[i] == 3)
				throw new InvalidOperationException($"sector cell {i} is marked as water without a water surface");
		}
	}

	public AtlasSectorStatistics CoreStatistics()
	{
		int landCells = 0, waterCells = 0, floodplainCells = 0, bankCells = 0, blendedCells = 0;
		int minHeight = int.MaxValue, maxHeight = int.MinValue;
		int minWaterSurface = int.MaxValue, maxWaterSurface = int.MinValue;
		for (int z = Apron; z < Apron + CoreSize; z++)
		for (int x = Apron; x < Apron + CoreSize; x++)
		{
			int index = z * Width + x;
			if (WaterSurface[index] > 0)
			{
				waterCells++;
				minWaterSurface = Math.Min(minWaterSurface, WaterSurface[index]);
				maxWaterSurface = Math.Max(maxWaterSurface, WaterSurface[index]);
			}
			if (Hydrology[index] == 1) floodplainCells++;
			else if (Hydrology[index] == 2) bankCells++;
			if (ProfileBlend[index] > 0) blendedCells++;
			if (Land[index] == 0) continue;
			landCells++;
			minHeight = Math.Min(minHeight, Height[index]);
			maxHeight = Math.Max(maxHeight, Height[index]);
		}
		if (landCells == 0) minHeight = maxHeight = SeaLevel;
		if (waterCells == 0) minWaterSurface = maxWaterSurface = 0;
		return new AtlasSectorStatistics(landCells, waterCells, floodplainCells, bankCells, blendedCells,
			minHeight, maxHeight, minWaterSurface, maxWaterSurface);
	}
}

public readonly record struct AtlasSectorVerification(string ContentHash, int EastOverlapCells,
	int SouthOverlapCells);

public readonly record struct AtlasSectorStatistics(int LandCells, int WaterCells, int FloodplainCells,
	int BankCells, int BlendedCells, int MinHeight, int MaxHeight, int MinWaterSurface, int MaxWaterSurface);
