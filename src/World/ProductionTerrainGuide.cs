using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Read-only macro guidance for one quickly generated production-world window.
/// The accepted continent owns land, elevation, hydrology and province intent;
/// Terrain owns the local block grammar that realises those fields.
/// </summary>
public sealed class ProductionTerrainGuide
{
	private const float PermanentWater = 240f / 255f;
	private const float RegionBlendWavelength = 96f;
	private const float RegionBlendWander = 24f;
	private const float RegionWeaveWavelength = 140f;

	private readonly WorldAtlasDefinition _atlas;
	private readonly Image _land;
	private readonly Image _elevation;
	private readonly Image _water;
	private readonly Image _region;
	private readonly Dictionary<int, Biome> _biomes = new();
	private readonly Dictionary<int, int> _provinceIndexByColour = new();
	private readonly Noise2D _regionBlendNoise;
	private readonly Noise2D _regionWeaveNoise;
	private readonly RegionTransitionField _regionTransition;
	private readonly byte[] _biomeByCell;

	public readonly int OriginX;
	public readonly int OriginZ;
	public readonly int LocalSize;
	public int WorldWidth => _atlas.Width;
	public int WorldDepth => _atlas.Depth;
	public int WorldHeight => _atlas.Height;

	private ProductionTerrainGuide(WorldAtlasDefinition atlas, int localSize,
		int originX, int originZ, bool originIsExact, int worldSeed)
	{
		_atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		LocalSize = localSize;
		// Runtime windows centre on the requested atlas address. Terrain fields are
		// sampled in global coordinates, so moving this bounded view never moves
		// the world underneath it.
		OriginX = originIsExact
			? originX
			: Rng.ClampI(originX - localSize / 2, 0, Math.Max(0, atlas.Width - localSize));
		OriginZ = originIsExact
			? originZ
			: Rng.ClampI(originZ - localSize / 2, 0, Math.Max(0, atlas.Depth - localSize));
		if (OriginX < 0 || OriginZ < 0 || OriginX + localSize > atlas.Width ||
		    OriginZ + localSize > atlas.Depth)
			throw new ArgumentOutOfRangeException(nameof(originX),
				$"terrain window {OriginX},{OriginZ}+{localSize} leaves " +
				$"{atlas.Width}x{atlas.Depth} atlas");

		_land = Load(atlas, AtlasLayerKind.Land);
		_elevation = Load(atlas, AtlasLayerKind.Elevation);
		_water = Load(atlas, AtlasLayerKind.Water);
		_region = Load(atlas, AtlasLayerKind.Region);
		_regionBlendNoise = new Noise2D(unchecked(worldSeed ^
			Rng.StableHash("production:region-transition-distance")));
		_regionWeaveNoise = new Noise2D(unchecked(worldSeed ^
			Rng.StableHash("production:region-transition-weave")));

		for (int index = 0; index < atlas.Provinces.Count; index++)
		{
			AtlasProvince province = atlas.Provinces[index];
			_biomes[HtmlColourKey(province.PreviewColour)] = province.Id switch
			{
				"cold-shelf" => Biome.SnowyHills,
				"scarp-quarry-belt" => Biome.Highland,
				"bloom-reach" => Biome.Sakura,
				"fen" => Biome.Wetland,
				"shallows" => Biome.Shore,
				_ => Biome.Meadow,
			};
			_provinceIndexByColour[HtmlColourKey(province.PreviewColour)] = index;
		}
		_regionTransition = BuildRegionTransitionField();
		_biomeByCell = BuildBiomeMap();
	}

	public static ProductionTerrainGuide Create(WorldAtlasDefinition atlas, int localSize,
		int centreX, int centreZ, int worldSeed) =>
		new(atlas, localSize, centreX, centreZ, false, worldSeed);

	/// <summary>
	/// Build the same guide at a sector-aligned atlas origin. Moving runtime
	/// windows use this form so the local allocation can move while every terrain
	/// query continues to address one permanent global world.
	/// </summary>
	public static ProductionTerrainGuide CreateAtOrigin(WorldAtlasDefinition atlas,
		int localSize, int originX, int originZ, int worldSeed) =>
		new(atlas, localSize, originX, originZ, true, worldSeed);

	public float GlobalX(float localX) => OriginX + localX;
	public float GlobalZ(float localZ) => OriginZ + localZ;

	public float ElevationAt(float localX, float localZ) =>
		Sample(_elevation, GlobalX(localX), GlobalZ(localZ), bilinear: true);

	/// <summary>
	/// Read the accepted elevation as a broad guide rather than an eight-block
	/// stencil. The production terrain displaces every shelf before it quantises the
	/// result; retaining that wander is what makes a source contour become a
	/// natural mass instead of the edge of a cut-out image.
	///
	/// Elevation texels under water are no-data. Renormalising the bilinear sample
	/// over land contributors prevents that zero from dragging a dry coastal cliff
	/// to the world floor before the old beach pass has a chance to shape it.
	/// </summary>
	public float GuidedLandElevationAt(float localX, float localZ, Vector2 atlasWarp)
	{
		float globalX = GlobalX(localX), globalZ = GlobalZ(localZ);
		float original = LandElevationAt(globalX, globalZ);
		float warpedX = globalX + atlasWarp.X, warpedZ = globalZ + atlasWarp.Y;
		if (Sample(_land, warpedX, warpedZ, bilinear: true) < .5f)
			return original;
		float warped = LandElevationAt(warpedX, warpedZ);
		return Rng.Lerp(original, warped, .72f);
	}

	/// <summary>
	/// Translate the accepted map's compressed land band into the production
	/// world's vertical composition. The source deliberately stores all dry land
	/// between roughly .50 and .98; treating that value as the old eleven-course
	/// planner relief made the entire continent only a few blocks tall.
	///
	/// These measured anchors keep southern lowlands close to the old sea-level
	/// grammar, open room through the waist, and reserve most of the 192-block
	/// natural envelope for the northern massif. Smooth interpolation keeps the
	/// map a guide; the six-block lattice, warped rooms, crowns and ledges still
	/// decide every visible local edge.
	/// </summary>
	public static float TerrainHeightForElevation(float elevation)
	{
		ReadOnlySpan<float> source = stackalloc float[]
			{ .50f, .55f, .60f, .68f, .78f, .90f, .98f };
		ReadOnlySpan<float> height = stackalloc float[]
			{ 26f, 30f, 40f, 64f, 105f, 150f, 166f };
		if (elevation <= source[0]) return height[0];
		for (int i = 1; i < source.Length; i++)
		{
			if (elevation > source[i]) continue;
			float t = (elevation - source[i - 1]) / (source[i] - source[i - 1]);
			t = t * t * (3f - 2f * t);
			return Rng.Lerp(height[i - 1], height[i], t);
		}
		return height[^1];
	}

	public float LandAt(float localX, float localZ) =>
		Sample(_land, GlobalX(localX), GlobalZ(localZ), bilinear: true);

	public float WaterAt(float localX, float localZ) =>
		Sample(_water, GlobalX(localX), GlobalZ(localZ), bilinear: true);

	public bool AuthoredWetAt(float localX, float localZ) =>
		LandAt(localX, localZ) < .5f || WaterAt(localX, localZ) >= PermanentWater;

	public Biome BiomeAt(float localX, float localZ)
	{
		int x = (int)MathF.Floor(localX), z = (int)MathF.Floor(localZ);
		if (x >= 0 && z >= 0 && x < LocalSize && z < LocalSize)
			return (Biome)_biomeByCell[z * LocalSize + x];
		return BiomeAtGlobal(Rng.ClampI(OriginX + x, 0, _atlas.Width - 1),
			Rng.ClampI(OriginZ + z, 0, _atlas.Depth - 1));
	}

	private Biome BiomeAtGlobal(int globalX, int globalZ)
	{
		Biome fallback = RawBiomeAt(globalX, globalZ);
		if (!_regionTransition.TrySample(globalX, globalZ, _atlas.BlocksPerPixel,
		    out int primaryIndex, out int secondaryIndex, out float distanceCost) ||
		    primaryIndex < 0 || secondaryIndex < 0 || primaryIndex == secondaryIndex ||
		    float.IsPositiveInfinity(distanceCost)) return fallback;

		AtlasProvince primary = _atlas.Provinces[primaryIndex];
		if (primary.TransitionBlocks <= 0) return fallback;
		// Region colours author province ownership, not a knife-cut material seam.
		// Convert their declared transition width into the broad interlocking patches
		// the old planner's natural regions produced. A stable pair orientation makes
		// the choice continuous when primary/secondary swap across the source border.
		float distanceBlocks = MathF.Max(0f,
			distanceCost / 10f * _atlas.BlocksPerPixel +
			_regionBlendNoise.Value(globalX / RegionBlendWavelength,
				globalZ / RegionBlendWavelength) * RegionBlendWander);
		float secondaryWeight = .5f * (1f - Rng.Smoothstep(0f,
			primary.TransitionBlocks, distanceBlocks));
		int low = Math.Min(primaryIndex, secondaryIndex);
		int high = Math.Max(primaryIndex, secondaryIndex);
		float highWeight = primaryIndex == high ? 1f - secondaryWeight : secondaryWeight;
		float weave = _regionWeaveNoise.Fbm01(globalX / RegionWeaveWavelength,
			globalZ / RegionWeaveWavelength, 3);
		return BiomeForProvince(weave < highWeight ? high : low);
	}

	public string Describe() =>
		$"production window {OriginX},{OriginZ}..{OriginX + LocalSize},{OriginZ + LocalSize}";

	private float Sample(Image image, float globalX, float globalZ, bool bilinear)
	{
		float px = globalX / _atlas.BlocksPerPixel;
		float pz = globalZ / _atlas.BlocksPerPixel;
		if (bilinear) return Bilinear(image, px, pz);
		return image.GetPixel(Rng.ClampI((int)px, 0, image.GetWidth() - 1),
			Rng.ClampI((int)pz, 0, image.GetHeight() - 1)).R;
	}

	private Biome RawBiomeAt(int globalX, int globalZ)
	{
		int px = Rng.ClampI(globalX / _atlas.BlocksPerPixel, 0, _region.GetWidth() - 1);
		int pz = Rng.ClampI(globalZ / _atlas.BlocksPerPixel, 0, _region.GetHeight() - 1);
		return _biomes.TryGetValue(ColourKey(_region.GetPixel(px, pz)), out Biome biome)
			? biome : Biome.Meadow;
	}

	private byte[] BuildBiomeMap()
	{
		var result = new byte[LocalSize * LocalSize];
		// Terrain columns, atlas metadata and vegetation all ask for the same biome.
		// Caching the bounded answer once keeps the organic transition cheaper than
		// repeating its distance/noise work for several million columns per stage.
		System.Threading.Tasks.Parallel.For(0, LocalSize, z =>
		{
			for (int x = 0; x < LocalSize; x++)
				result[z * LocalSize + x] = (byte)BiomeAtGlobal(OriginX + x, OriginZ + z);
		});
		return result;
	}

	private Biome BiomeForProvince(int provinceIndex)
	{
		if (provinceIndex < 0 || provinceIndex >= _atlas.Provinces.Count)
			return Biome.Meadow;
		return _biomes.TryGetValue(HtmlColourKey(_atlas.Provinces[provinceIndex].PreviewColour),
			out Biome biome) ? biome : Biome.Meadow;
	}

	/// <summary>
	/// Build only this bounded runtime window's coarse province-border distance
	/// patch. The support is the largest authored transition width, so independent
	/// windows derive the same global result without allocating a continent field.
	/// </summary>
	private RegionTransitionField BuildRegionTransitionField()
	{
		int blocksPerPixel = _atlas.BlocksPerPixel;
		int margin = (int)MathF.Ceiling((_atlas.Provinces.Max(p => p.TransitionBlocks) +
			RegionBlendWander) / blocksPerPixel) + 2;
		int minPx = Math.Clamp(FloorDiv(OriginX, blocksPerPixel) - margin,
			0, _region.GetWidth() - 1);
		int minPz = Math.Clamp(FloorDiv(OriginZ, blocksPerPixel) - margin,
			0, _region.GetHeight() - 1);
		int maxPx = Math.Clamp(FloorDiv(OriginX + LocalSize - 1, blocksPerPixel) + margin,
			0, _region.GetWidth() - 1);
		int maxPz = Math.Clamp(FloorDiv(OriginZ + LocalSize - 1, blocksPerPixel) + margin,
			0, _region.GetHeight() - 1);
		int width = maxPx - minPx + 1, depth = maxPz - minPz + 1;
		int count = width * depth;
		var owner = Enumerable.Repeat(-1, count).ToArray();
		var secondary = Enumerable.Repeat(-1, count).ToArray();
		var distance = Enumerable.Repeat(int.MaxValue, count).ToArray();
		var seedKey = Enumerable.Repeat(int.MaxValue, count).ToArray();

		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int colour = ColourKey(_region.GetPixel(minPx + x, minPz + z));
			if (_provinceIndexByColour.TryGetValue(colour, out int province))
				owner[z * width + x] = province;
		}

		var queue = new PriorityQueue<TransitionNode,
			(int cost, int secondary, int seed, int cell)>();
		int[] nx = { -1, 0, 1, -1, 1, -1, 0, 1 };
		int[] nz = { -1, -1, -1, 0, 0, 1, 1, 1 };
		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			int cell = z * width + x;
			if (owner[cell] < 0) continue;
			int neighbourOwner = int.MaxValue;
			for (int n = 0; n < nx.Length; n++)
			{
				int xx = x + nx[n], zz = z + nz[n];
				if (xx < 0 || zz < 0 || xx >= width || zz >= depth) continue;
				int candidate = owner[zz * width + xx];
				if (candidate >= 0 && candidate != owner[cell])
					neighbourOwner = Math.Min(neighbourOwner, candidate);
			}
			if (neighbourOwner == int.MaxValue) continue;
			distance[cell] = 5;
			secondary[cell] = neighbourOwner;
			seedKey[cell] = (minPz + z) * _region.GetWidth() + minPx + x;
			queue.Enqueue(new TransitionNode(cell, 5, neighbourOwner, seedKey[cell]),
				(5, neighbourOwner, seedKey[cell], cell));
		}

		int maxCost = (int)MathF.Ceiling((_atlas.Provinces.Max(p => p.TransitionBlocks) +
			RegionBlendWander) / blocksPerPixel * 10f) + 14;
		while (queue.TryDequeue(out TransitionNode node, out _))
		{
			if (node.Cost != distance[node.Cell] || node.Secondary != secondary[node.Cell] ||
			    node.Seed != seedKey[node.Cell]) continue;
			int x = node.Cell % width, z = node.Cell / width;
			for (int n = 0; n < nx.Length; n++)
			{
				int xx = x + nx[n], zz = z + nz[n];
				if (xx < 0 || zz < 0 || xx >= width || zz >= depth) continue;
				int next = zz * width + xx;
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
		return new RegionTransitionField(minPx, minPz, width, depth,
			owner, secondary, distance);
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = value / divisor;
		return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
	}

	private float LandElevationAt(float globalX, float globalZ)
	{
		float px = Rng.Clamp(globalX / _atlas.BlocksPerPixel,
			0f, _elevation.GetWidth() - 1.001f);
		float pz = Rng.Clamp(globalZ / _atlas.BlocksPerPixel,
			0f, _elevation.GetHeight() - 1.001f);
		int x0 = (int)MathF.Floor(px), z0 = (int)MathF.Floor(pz);
		int x1 = Math.Min(x0 + 1, _elevation.GetWidth() - 1);
		int z1 = Math.Min(z0 + 1, _elevation.GetHeight() - 1);
		float tx = px - x0, tz = pz - z0;
		float value = 0f, weight = 0f;
		void Add(int x, int z, float sampleWeight)
		{
			if (_land.GetPixel(x, z).R < .5f) return;
			value += _elevation.GetPixel(x, z).R * sampleWeight;
			weight += sampleWeight;
		}
		Add(x0, z0, (1f - tx) * (1f - tz));
		Add(x1, z0, tx * (1f - tz));
		Add(x0, z1, (1f - tx) * tz);
		Add(x1, z1, tx * tz);
		return weight > .0001f ? value / weight : Bilinear(_elevation, px, pz);
	}

	private static Image Load(WorldAtlasDefinition atlas, AtlasLayerKind kind)
	{
		AtlasSourceLayer layer = atlas.SourceLayers.FirstOrDefault(candidate =>
			candidate.Kind == kind && candidate.Status != AtlasLayerStatus.Planned)
			?? throw new InvalidOperationException($"atlas has no registered {kind} guide");
		Image image = Image.LoadFromFile(ProjectSettings.GlobalizePath(layer.Path));
		return image ?? throw new InvalidOperationException($"could not load atlas guide '{layer.Path}'");
	}

	private static float Bilinear(Image image, float x, float z)
	{
		float fx = Rng.Clamp(x, 0f, image.GetWidth() - 1.001f);
		float fz = Rng.Clamp(z, 0f, image.GetHeight() - 1.001f);
		int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
		int x1 = Math.Min(x0 + 1, image.GetWidth() - 1);
		int z1 = Math.Min(z0 + 1, image.GetHeight() - 1);
		float tx = fx - x0, tz = fz - z0;
		float a = Rng.Lerp(image.GetPixel(x0, z0).R, image.GetPixel(x1, z0).R, tx);
		float b = Rng.Lerp(image.GetPixel(x0, z1).R, image.GetPixel(x1, z1).R, tx);
		return Rng.Lerp(a, b, tz);
	}

	private static int ColourKey(Color colour) =>
		((int)MathF.Round(colour.R * 255f) << 16) |
		((int)MathF.Round(colour.G * 255f) << 8) |
		(int)MathF.Round(colour.B * 255f);

	private static int HtmlColourKey(string html) => Convert.ToInt32(html[1..], 16);

	private readonly record struct TransitionNode(int Cell, int Cost,
		int Secondary, int Seed);

	private sealed class RegionTransitionField
	{
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
			int x = globalX / blocksPerPixel - _originX;
			int z = globalZ / blocksPerPixel - _originZ;
			if (x < 0 || z < 0 || x >= _width || z >= _depth) return false;
			int index = z * _width + x;
			owner = _owner[index];
			secondary = _secondary[index];
			if (owner < 0 || secondary < 0 || _distance[index] == int.MaxValue) return true;
			int pairOwner = owner, pairSecondary = secondary;

			float fx = (globalX + .5f) / blocksPerPixel - .5f - _originX;
			float fz = (globalZ + .5f) / blocksPerPixel - .5f - _originZ;
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
				bool same = _owner[sample] == pairOwner &&
				            _secondary[sample] == pairSecondary;
				bool reverse = _owner[sample] == pairSecondary &&
				               _secondary[sample] == pairOwner;
				if ((!same && !reverse) || _distance[sample] == int.MaxValue) return;
				weighted += _distance[sample] * weight;
				total += weight;
			}
		}
	}
}
