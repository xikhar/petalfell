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

	private readonly WorldAtlasDefinition _atlas;
	private readonly Image _land;
	private readonly Image _elevation;
	private readonly Image _water;
	private readonly Image _region;
	private readonly Dictionary<int, Biome> _biomes = new();

	public readonly int OriginX;
	public readonly int OriginZ;
	public readonly int LocalSize;

	private ProductionTerrainGuide(WorldAtlasDefinition atlas, int localSize,
		int originX, int originZ, bool originIsExact)
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

		foreach (AtlasProvince province in atlas.Provinces)
			_biomes[HtmlColourKey(province.PreviewColour)] = province.Id switch
			{
				"cold-shelf" => Biome.SnowyHills,
				"scarp-quarry-belt" => Biome.Highland,
				"bloom-reach" => Biome.Sakura,
				"fen" => Biome.Wetland,
				"shallows" => Biome.Shore,
				_ => Biome.Meadow,
			};
	}

	public static ProductionTerrainGuide Create(WorldAtlasDefinition atlas, int localSize,
		int centreX, int centreZ) => new(atlas, localSize, centreX, centreZ, false);

	/// <summary>
	/// Build the same guide at a sector-aligned atlas origin. Moving runtime
	/// windows use this form so the local allocation can move while every terrain
	/// query continues to address one permanent global world.
	/// </summary>
	public static ProductionTerrainGuide CreateAtOrigin(WorldAtlasDefinition atlas,
		int localSize, int originX, int originZ) =>
		new(atlas, localSize, originX, originZ, true);

	public float GlobalX(float localX) => OriginX + localX;
	public float GlobalZ(float localZ) => OriginZ + localZ;

	public float ElevationAt(float localX, float localZ) =>
		Sample(_elevation, GlobalX(localX), GlobalZ(localZ), bilinear: true);

	public float LandAt(float localX, float localZ) =>
		Sample(_land, GlobalX(localX), GlobalZ(localZ), bilinear: true);

	public float WaterAt(float localX, float localZ) =>
		Sample(_water, GlobalX(localX), GlobalZ(localZ), bilinear: true);

	public bool AuthoredWetAt(float localX, float localZ) =>
		LandAt(localX, localZ) < .5f || WaterAt(localX, localZ) >= PermanentWater;

	public Biome BiomeAt(float localX, float localZ)
	{
		int px = Rng.ClampI((int)MathF.Floor(GlobalX(localX) / _atlas.BlocksPerPixel),
			0, _region.GetWidth() - 1);
		int pz = Rng.ClampI((int)MathF.Floor(GlobalZ(localZ) / _atlas.BlocksPerPixel),
			0, _region.GetHeight() - 1);
		return _biomes.TryGetValue(ColourKey(_region.GetPixel(px, pz)), out Biome biome)
			? biome : Biome.Meadow;
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
}
