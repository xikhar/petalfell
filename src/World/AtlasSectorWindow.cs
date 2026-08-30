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
	/// <summary>
	/// Runtime headroom for authored monuments. Compiled sector height remains the
	/// canonical 192-block natural-terrain envelope; raising this sparse window
	/// bound does not enlarge atlas source arrays or alter any compiled elevation.
	/// Reference 1 needs the extra courses because its source-measured plan now
	/// expands to three runtime voxels per cell while preserving water at TopY 105.
	/// </summary>
	public const int AuthoredRuntimeHeight = 256;
	private readonly AtlasSectorData _data;
	private readonly IReadOnlyList<BiomeBuildProfile> _profiles;
	private readonly Noise2D _profileMaterialField;
	private readonly Noise2D _naturalRockField;
	private readonly Noise2D _naturalToneField;

	public AtlasSectorData Data => _data;
	public VoxelGrid Grid { get; }
	public Vector3 GlobalOrigin => new(_data.OriginX, 0f, _data.OriginZ);

	public AtlasSectorWindow(AtlasSectorData data, WorldAtlasDefinition atlas, int worldSeed)
		: this(data, atlas, worldSeed, null)
	{
	}

	/// <summary>
	/// Wrap a terrain grid generated directly from the production maps. Reference
	/// blueprints need the global origin and voxel store, not a compiler cache.
	/// </summary>
	public AtlasSectorWindow(AtlasSectorData data, WorldAtlasDefinition atlas, int worldSeed,
		VoxelGrid existingGrid)
	{
		_data = data ?? throw new ArgumentNullException(nameof(data));
		_profiles = atlas?.BiomeCatalog?.Profiles ?? throw new ArgumentNullException(nameof(atlas));
		if (data.Width != data.Depth)
			throw new InvalidOperationException("the current VoxelGrid window requires a square sector artifact");
		_profileMaterialField = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:surface-profile")));
		_naturalRockField = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:natural-cap-rock")));
		_naturalToneField = new Noise2D(unchecked(worldSeed ^ Rng.StableHash("atlas:natural-cap-tone")));
		if (existingGrid != null && (existingGrid.Size != data.Width ||
		    existingGrid.OriginX != data.OriginX || existingGrid.OriginZ != data.OriginZ))
			throw new InvalidOperationException(
				"wrapped production grid does not match its window origin and size");
		Grid = existingGrid ?? new VoxelGrid(data.Width,
			Math.Max(data.WorldHeight, AuthoredRuntimeHeight), worldSeed,
			data.OriginX, data.OriginZ);
		if (existingGrid == null) MaterialiseColumns();
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
			byte deep = ResolveSurface(surfaces.Cliff);
			AtlasTerrainSurface surface = (AtlasTerrainSurface)_data.Surface[index];
			if (surface == AtlasTerrainSurface.Underwater)
			{
				cap = ResolveSurface(surfaces.Underwater);
				sub = cap;
			}
			else if (surface == AtlasTerrainSurface.Shore)
			{
				// Compiler 17 admits only a low bank attached to real water. Carry its
				// substrate through the short face; the unconditional cliff-deep layer
				// made every harmless river lip repeat the same three-colour wall.
				cap = ResolveSurface(surfaces.Shore);
				sub = ResolveSurface(surfaces.Substrate);
				deep = sub;
			}
			else
			{
				// A cliff class describes the exposed vertical body, not a different
				// horizontal biome. Keep grass, snow or soil on the top block and let
				// Deep own the face below it; replacing the cap as well turned whole
				// highland shelves into pale masonry slabs.
				cap = ResolveSurface(surfaces.Cap);
				sub = ResolveSurface(surfaces.Substrate);
				cap = VaryNaturalCap(cap, surfaces.Cap, x + _data.OriginX, z + _data.OriginZ);
			}
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

	/// <summary>
	/// Resolve the authored biome detail vocabulary at a local runtime column.
	/// Consumers use this instead of reading the categorical profile bytes so
	/// ground marks follow the same broad transition field as the block caps.
	/// </summary>
	public string GroundDetailSetAt(int localX, int localZ)
	{
		if (localX < 0 || localZ < 0 || localX >= _data.Width || localZ >= _data.Depth)
			return "";
		int index = localZ * _data.Width + localX;
		return ProfileAt(index, localX, localZ).GroundDetailSetId;
	}

	/// <summary>
	/// Resolve the same broad categorical transition used by visible cap
	/// materials. Wilderness must not roll the blend again or a forest tree can
	/// appear on the meadow side of the rendered boundary.
	/// </summary>
	public BiomeBuildProfile BuildProfileAt(int localX, int localZ)
	{
		if (localX < 0 || localZ < 0 || localX >= _data.Width || localZ >= _data.Depth)
			throw new ArgumentOutOfRangeException(nameof(localX),
				$"atlas profile coordinate {localX},{localZ} leaves {_data.Width}x{_data.Depth} window");
		int index = localZ * _data.Width + localX;
		return ProfileAt(index, localX, localZ);
	}

	private byte VaryNaturalCap(byte cap, string capId, int globalX, int globalZ)
	{
		// These are world fields rather than per-column rolls: material islands must
		// continue through a sector apron and remain readable after the shader's fine
		// surface tooth has faded. The shorter tone field sits inside the broad rock
		// field without turning a shelf into block-by-block speckle. Snow also samples
		// that rock field in wind-aligned coordinates: the long/short axes make exposed
		// lanes tens of metres wide, while avoiding the hundred-metre blank zones that
		// one isotropic threshold left in the first mountain review.
		float rock = _naturalRockField.Fbm01(globalX / 122f + 11.7f, globalZ / 122f - 43.9f, 3);
		float tone = _naturalToneField.Fbm01(globalX / 87f - 29.3f, globalZ / 87f + 17.1f, 3);

		if (capId == "snow-pale")
		{
			float windAlong = (globalX * 0.87f + globalZ * 0.49f) / 180f;
			float windAcross = (-globalX * 0.49f + globalZ * 0.87f) / 54f;
			float scour = _naturalRockField.Fbm01(windAlong + 71.2f,
				windAcross - 33.7f, 3);

			// Alpine shelves remain predominantly snow. A broad exposure core can be
			// opened by either massif-scale rock or a narrower wind scour; their overlap
			// produces long irregular stone/scree patches rather than dots. The fringe is
			// deliberately scree-only so the transition reads as loose frost-broken rock.
			bool exposedCore = rock > 0.67f || (scour > 0.70f && rock > 0.37f);
			if (exposedCore) return tone < 0.51f ? Palette.SCREE : Palette.STONE_PALE;
			bool exposedFringe = (rock > 0.58f && tone < 0.42f) ||
				(scour > 0.65f && rock > 0.40f && tone < 0.47f);
			if (exposedFringe) return Palette.SCREE;
			return Palette.SNOW;
		}

		if (capId is "highland-grass" or "dry-highland-grass")
		{
			// A dry scarp exposes more of its stony substrate than the grassier
			// highland, but both keep grass as the connective material between broad
			// stone and scree regions.
			float bareThreshold = capId == "dry-highland-grass" ? 0.53f : 0.59f;
			if (rock > bareThreshold)
				return tone < 0.52f ? Palette.SCREE : Palette.STONE_PALE;
			if (rock > bareThreshold - 0.08f && tone < 0.32f)
				return Palette.SCREE;
			return GrassTone(tone, stoneSubstrate: true);
		}

		if (!Palette.IsGrassSurface(cap)) return cap;

		// Meadows and woods retain their living cap. Sparse broad patches merely
		// reveal a stone-bearing turf vocabulary; they do not become bare quarry.
		// Forests are slightly more protected, while bloom and river meadows still
		// receive enough variation to survive the far-view pattern fade.
		float stoneThreshold = capId == "forest-sage" ? 0.80f : 0.75f;
		bool stone = Palette.HasStoneSubstrate(cap) || rock > stoneThreshold;
		return GrassTone(tone, stone);
	}

	private static byte GrassTone(float tone, bool stoneSubstrate)
	{
		if (tone < 0.34f) return stoneSubstrate ? Palette.GRASS_DEEP_STONE : Palette.GRASS_DEEP;
		if (tone > 0.68f) return stoneSubstrate ? Palette.GRASS_LIGHT_STONE : Palette.GRASS_LIGHT;
		return stoneSubstrate ? Palette.GRASS_STONE : Palette.GRASS;
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
	public Vector3 FindReviewFocus(int streamMargin)
	{
		int centre = _data.Apron + _data.CoreSize / 2;
		int min = Math.Max(_data.Apron + 96, streamMargin);
		int max = Math.Min(_data.Apron + _data.CoreSize - 97, _data.Width - streamMargin - 1);
		if (min > max) throw new InvalidOperationException(
			$"stream margin {streamMargin} leaves no reviewable terrain inside {_data.Width} cells");
		float bestScore = float.MinValue;
		int bestX = centre, bestZ = centre;
		for (int z = min; z <= max; z += 6)
		for (int x = min; x <= max; x += 6)
		{
			int index = z * _data.Width + x;
			if (_data.Land[index] == 0) continue;
			int low = _data.Height[index], high = low;
			bool waterNearby = false;
			int landSamples = 0, samples = 0;
			for (int dz = -18; dz <= 18; dz += 6)
			for (int dx = -18; dx <= 18; dx += 6)
			{
				int sample = (z + dz) * _data.Width + x + dx;
				low = Math.Min(low, _data.Height[sample]);
				high = Math.Max(high, _data.Height[sample]);
				waterNearby |= _data.WaterSurface[sample] > 0;
				if (_data.Land[sample] != 0) landSamples++;
				samples++;
			}
			float centreDistance = new Vector2(x - centre, z - centre).Length();
			float landShare = landSamples / (float)Math.Max(1, samples);
			// Prefer a readable edge inside a landscape. Unbounded slope weighting
			// selected a sixty-block shoreline wall with most of the frame underwater;
			// the fixed capture then judged neither bank nor biome. Capping the local
			// signals still finds cliffs while a land-share penalty keeps their context.
			float score = (waterNearby ? 12f : 0f) + Math.Min(high - low, 28) * 1.35f +
				Math.Min(_data.Slope[index], (byte)16) * 3.25f +
				_data.ProfileBlend[index] / 48f - centreDistance * 0.018f;
			if (landShare < 0.68f) score -= (0.68f - landShare) * 150f;
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

	/// <summary>
	/// Resolve the current materialised atlas water column in permanent global
	/// coordinates. Grid height is intentional here: a reference site's pier or
	/// bridge support can replace the compiled bed after the sector was loaded.
	/// The +0.35 matches BuildWater's visible plane exactly.
	/// </summary>
	public bool TryWaterColumnAtGlobal(int globalX, int globalZ,
		out float bedY, out float surfaceY)
	{
		int x = globalX - _data.OriginX;
		int z = globalZ - _data.OriginZ;
		if (x < 0 || z < 0 || x >= _data.Width || z >= _data.Depth)
		{
			bedY = 0f;
			surfaceY = 0f;
			return false;
		}
		int index = z * _data.Width + x;
		ushort water = _data.WaterSurface[index];
		if (water == 0)
		{
			bedY = 0f;
			surfaceY = 0f;
			return false;
		}
		bedY = Grid.HeightAt(x, z);
		surfaceY = water + 0.35f;
		return true;
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

		// Connected atlas water descends by one voxel at a time. Horizontal tops
		// alone leave the wet bed's red/purple cliff face exposed between those
		// planes, making every harmless elevation contour look like a broken dam.
		// Close only wet-to-wet steps with a two-sided water curtain; shores remain
		// open so terrain still owns the bank silhouette.
		for (int z = 0; z < _data.Depth; z++)
		for (int x = 0; x < _data.Width; x++)
		{
			int cell = z * _data.Width + x;
			ushort surface = _data.WaterSurface[cell];
			if (surface == 0) continue;
			if (x + 1 < _data.Width)
			{
				ushort east = _data.WaterSurface[cell + 1];
				if (east > 0 && east != surface)
				{
					float low = Math.Min(surface, east) + 0.35f;
					float high = Math.Max(surface, east) + 0.35f;
					AddCurtain(new Vector3(x + 1, low, z),
						new Vector3(x + 1, high, z),
						new Vector3(x + 1, high, z + 1),
						new Vector3(x + 1, low, z + 1), Vector3.Right);
				}
			}
			if (z + 1 < _data.Depth)
			{
				ushort south = _data.WaterSurface[cell + _data.Width];
				if (south > 0 && south != surface)
				{
					float low = Math.Min(surface, south) + 0.35f;
					float high = Math.Max(surface, south) + 0.35f;
					AddCurtain(new Vector3(x, low, z + 1),
						new Vector3(x + 1, low, z + 1),
						new Vector3(x + 1, high, z + 1),
						new Vector3(x, high, z + 1), Vector3.Back);
				}
			}
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

		void AddCurtain(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
		{
			int first = vertices.Count;
			vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
			for (int i = 0; i < 4; i++) normals.Add(normal);
			indices.Add(first); indices.Add(first + 1); indices.Add(first + 2);
			indices.Add(first); indices.Add(first + 2); indices.Add(first + 3);
			indices.Add(first + 2); indices.Add(first + 1); indices.Add(first);
			indices.Add(first + 3); indices.Add(first + 2); indices.Add(first);
		}
	}
}
