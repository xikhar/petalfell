using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;
using Petalfell.Render;

namespace Petalfell.World;

/// <summary>
/// Materialised runtime view of one compiled production sector. All storage is
/// local to the sector plus its apron; global coordinates remain an origin,
/// never a request to allocate continent-sized arrays.
/// </summary>
public sealed class AtlasSectorWindow
{
	private readonly AtlasSectorData _data;
	private readonly IReadOnlyList<BiomeBuildProfile> _profiles;
	private readonly Noise2D _profileMaterialField;

	public AtlasSectorData Data => _data;
	public VoxelGrid Grid { get; }
	public Vector3 GlobalOrigin => new(_data.OriginX, 0f, _data.OriginZ);

	public AtlasSectorWindow(AtlasSectorData data, WorldAtlasDefinition atlas, int worldSeed)
	{
		_data = data ?? throw new ArgumentNullException(nameof(data));
		_profiles = atlas?.BiomeCatalog?.Profiles ?? throw new ArgumentNullException(nameof(atlas));
		if (data.Width != data.Depth)
			throw new InvalidOperationException("the current VoxelGrid window requires a square sector artifact");
		_profileMaterialField = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:surface-profile")));
		Grid = new VoxelGrid(data.Width, data.WorldHeight, worldSeed, data.OriginX, data.OriginZ);
		MaterialiseColumns();
	}

	private void MaterialiseColumns()
	{
		for (int z = 0; z < _data.Depth; z++)
		for (int x = 0; x < _data.Width; x++)
		{
			int index = z * _data.Width + x;
			BiomeBuildProfile profile = ProfileAt(index, x, z);
			AtlasSurfaceSet surfaces = profile.Surfaces;
			byte cap;
			byte sub;
			if (_data.WaterSurface[index] > 0)
			{
				cap = ResolveSurface(surfaces.Underwater);
				sub = cap;
			}
			else if (_data.Hydrology[index] == 2)
			{
				cap = ResolveSurface(surfaces.Shore);
				sub = ResolveSurface(surfaces.Substrate);
			}
			else
			{
				cap = ResolveSurface(surfaces.Cap);
				sub = ResolveSurface(surfaces.Substrate);
				cap = VaryNaturalCap(cap, x + _data.OriginX, z + _data.OriginZ);
			}
			byte deep = ResolveSurface(surfaces.Cliff);
			Grid.Describe(x, z, _data.Height[index], cap, sub, deep);
		}
	}

	private BiomeBuildProfile ProfileAt(int index, int localX, int localZ)
	{
		int primary = _data.Profile[index];
		int secondary = _data.SecondaryProfile[index];
		float blend = _data.ProfileBlend[index] / 255f;
		if (blend <= 0f || primary == secondary) return _profiles[primary];
		// Block materials are categorical. A continuous global field converts the
		// compiled blend into broad interlocking patches instead of per-block confetti.
		float globalX = localX + _data.OriginX;
		float globalZ = localZ + _data.OriginZ;
		float field = _profileMaterialField.Fbm01(globalX / 72f, globalZ / 72f, 3);
		return field < blend ? _profiles[secondary] : _profiles[primary];
	}

	private byte VaryNaturalCap(byte cap, int globalX, int globalZ)
	{
		if (!Palette.IsGrassSurface(cap)) return cap;
		float tone = _profileMaterialField.Fbm01(globalX / 54f + 37.1f, globalZ / 54f - 19.4f, 3);
		bool stone = Palette.HasStoneSubstrate(cap);
		if (tone < 0.34f) return stone ? Palette.GRASS_DEEP_STONE : Palette.GRASS_DEEP;
		if (tone > 0.68f) return stone ? Palette.GRASS_LIGHT_STONE : Palette.GRASS_LIGHT;
		return stone ? Palette.GRASS_STONE : Palette.GRASS;
	}

	private static byte ResolveSurface(string id) => id switch
	{
		"snow-pale" or "ice-edge" => Palette.SNOW,
		"highland-grass" or "dry-highland-grass" => Palette.GRASS_STONE,
		"meadow-sage" or "forest-sage" or "blossom-meadow" => Palette.GRASS,
		"wetland-mat" => Palette.MOSS,
		"warm-shore-grass" => Palette.GRASS_LIGHT,
		"frozen-soil" or "stone-cold" or "stone-cold-wet" => Palette.STONE,
		"thin-stony-soil" => Palette.STONE,
		"stone-dust" or "stone-shingle" => Palette.SCREE,
		"warm-soil" or "dark-warm-soil" or "warm-cultivated-soil" => Palette.SOIL,
		"peat-mud" or "reed-mud" or "rooted-bank" or "rooted-earth" => Palette.MUD,
		"sand-over-stone" or "river-sand" or "clear-silt" or "cream-sand" => Palette.SAND,
		"stone-pale-quarried" or "stone-pale-bedded" or "stone-pale-terraced" or
			"stone-pale-wet" or "rock-shelf" => Palette.STONE_PALE,
		"stone-warm" or "old-southern-masonry" => Palette.STONE_WARM,
		"stone-mossed" => Palette.MOSS_STONE,
		"petal-bank" => Palette.BLOSSOM_DRIFT,
		"stone-deep" => Palette.STONE,
		"river-silt" or "forest-silt" or "dark-silt" => Palette.MUD,
		"drowned-paving" => Palette.PAVING,
		_ => throw new InvalidOperationException($"atlas surface '{id}' has no runtime palette material"),
	};

	/// <summary>Choose a deterministic, information-rich land point near the core centre.</summary>
	public Vector3 FindReviewFocus()
	{
		int centre = _data.Apron + _data.CoreSize / 2;
		int min = _data.Apron + 96;
		int max = _data.Apron + _data.CoreSize - 97;
		float bestScore = float.MinValue;
		int bestX = centre, bestZ = centre;
		for (int z = min; z <= max; z += 6)
		for (int x = min; x <= max; x += 6)
		{
			int index = z * _data.Width + x;
			if (_data.Land[index] == 0) continue;
			int low = _data.Height[index], high = low;
			bool waterNearby = false;
			for (int dz = -18; dz <= 18; dz += 6)
			for (int dx = -18; dx <= 18; dx += 6)
			{
				int sample = (z + dz) * _data.Width + x + dx;
				low = Math.Min(low, _data.Height[sample]);
				high = Math.Max(high, _data.Height[sample]);
				waterNearby |= _data.WaterSurface[sample] > 0;
			}
			float centreDistance = new Vector2(x - centre, z - centre).Length();
			float score = (waterNearby ? 20f : 0f) + (high - low) * 1.3f +
				_data.ProfileBlend[index] / 32f - centreDistance * 0.018f;
			if (_data.Hydrology[index] == 2) score += 8f;
			else if (_data.Hydrology[index] == 1) score += 3f;
			if (score <= bestScore) continue;
			bestScore = score;
			bestX = x;
			bestZ = z;
		}
		return new Vector3(bestX + 0.5f, _data.Height[bestZ * _data.Width + bestX] + 1.2f, bestZ + 0.5f);
	}

	public Vector3 FocusAtGlobal(int globalX, int globalZ, int streamRadius)
	{
		int margin = streamRadius * ChunkMesher.ChunkSize;
		int x = Math.Clamp(globalX - _data.OriginX, margin, _data.Width - margin - 1);
		int z = Math.Clamp(globalZ - _data.OriginZ, margin, _data.Depth - margin - 1);
		return new Vector3(x + 0.5f, Grid.HeightAt(x, z) + 1.2f, z + 0.5f);
	}

	/// <summary>Greedily merge equal-height water cells into the actual runtime surface.</summary>
	public MeshInstance3D BuildWater(ShaderMaterial material)
	{
		var visited = new bool[_data.CellCount];
		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var indices = new List<int>();
		for (int z = 0; z < _data.Depth; z++)
		for (int x = 0; x < _data.Width; x++)
		{
			int cell = z * _data.Width + x;
			ushort surface = _data.WaterSurface[cell];
			if (surface == 0 || visited[cell]) continue;

			int runWidth = 1;
			while (x + runWidth < _data.Width)
			{
				int next = cell + runWidth;
				if (visited[next] || _data.WaterSurface[next] != surface) break;
				runWidth++;
			}
			int runDepth = 1;
			while (z + runDepth < _data.Depth)
			{
				bool matches = true;
				int row = (z + runDepth) * _data.Width + x;
				for (int dx = 0; dx < runWidth; dx++)
					if (visited[row + dx] || _data.WaterSurface[row + dx] != surface)
					{
						matches = false;
						break;
					}
				if (!matches) break;
				runDepth++;
			}
			for (int dz = 0; dz < runDepth; dz++)
				for (int dx = 0; dx < runWidth; dx++)
					visited[(z + dz) * _data.Width + x + dx] = true;

			float y = surface + 0.35f;
			int first = vertices.Count;
			vertices.Add(new Vector3(x, y, z));
			vertices.Add(new Vector3(x + runWidth, y, z));
			vertices.Add(new Vector3(x + runWidth, y, z + runDepth));
			vertices.Add(new Vector3(x, y, z + runDepth));
			for (int i = 0; i < 4; i++) normals.Add(Vector3.Up);
			// Godot's clockwise front face is opposite the right-handed cross
			// product used by the supplied normal. This order matches ChunkMesher's
			// +Y faces; the first implementation used the mathematical +Y winding
			// and every water quad was culled from above.
			indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
			indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
		}

		if (vertices.Count == 0) return null;
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return new MeshInstance3D
		{
			Name = "AtlasWater",
			Mesh = mesh,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = PlanarReflection.WaterLayer,
		};
	}
}
