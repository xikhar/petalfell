using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Sparse, local airborne detail continuously carried down-screen by wind.
///
/// This is deliberately not weather: the overhead stream follows the player
/// and uses small tapered meshes rather than screen-filling quads. Biome
/// profiles are data, so later chapters can replace what each landscape carries
/// without changing the particle lifecycle.
/// </summary>
public partial class AmbientDrift : Node3D
{
	private enum SurfaceRule { Grass, Land, Wetland }

	private sealed class LayerSpec
	{
		public string Name;
		public int Amount;
		public float Lifetime;
		public float Width, Length;
		public float SpeedMin, SpeedMax;
		public Vector3 Direction, Gravity;
		public float Spread;
		public Color[] Colors;
	}

	private sealed class Profile
	{
		public SurfaceRule Surface;
		public LayerSpec[] Layers;
	}

	private static LayerSpec Layer(string name, int amount, float lifetime,
		float width, float length, float speedMin, float speedMax,
		Vector3 direction, Vector3 gravity, float spread, Color[] colors) => new()
	{
		Name = name, Amount = amount, Lifetime = lifetime,
		Width = width, Length = length, SpeedMin = speedMin, SpeedMax = speedMax,
		Direction = direction, Gravity = gravity, Spread = spread, Colors = colors,
	};

	private static readonly Dictionary<Biome, Profile> Profiles = new()
	{
		[Biome.Meadow] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer("Leaves", 42, 8.0f, 0.090f, 0.19f, 0.48f, 0.82f,
					new Vector3(0.36f, -1f, 0.16f), new Vector3(0, -0.34f, 0), 22f, Palette.AirLeafColors),
				Layer("FlowerPetals", 16, 7.5f, 0.075f, 0.14f, 0.42f, 0.72f,
					new Vector3(0.32f, -1f, 0.13f), new Vector3(0, -0.30f, 0), 25f, Palette.AirPetalColors),
			},
		},
		[Biome.Forest] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer("Leaves", 48, 8.5f, 0.095f, 0.20f, 0.42f, 0.76f,
					new Vector3(0.30f, -1f, 0.14f), new Vector3(0, -0.32f, 0), 21f, Palette.AirLeafColors),
			},
		},
		[Biome.Plains] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer("DryLeaves", 40, 7.5f, 0.080f, 0.17f, 0.62f, 1.00f,
					new Vector3(0.44f, -1f, 0.20f), new Vector3(0, -0.38f, 0), 20f, Palette.AirLeafColors),
			},
		},
		[Biome.Sakura] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer("Leaves", 24, 8.0f, 0.080f, 0.17f, 0.44f, 0.76f,
					new Vector3(0.32f, -1f, 0.15f), new Vector3(0, -0.30f, 0), 24f, Palette.AirLeafColors),
				Layer("BlossomPetals", 34, 8.5f, 0.078f, 0.14f, 0.38f, 0.70f,
					new Vector3(0.30f, -1f, 0.12f), new Vector3(0, -0.27f, 0), 28f, Palette.AirPetalColors),
			},
		},
		[Biome.Highland] = new Profile
		{
			Surface = SurfaceRule.Land,
			Layers = new[]
			{
				Layer("AlpineFlecks", 32, 7.0f, 0.065f, 0.14f, 0.68f, 1.08f,
					new Vector3(0.48f, -1f, 0.23f), new Vector3(0, -0.42f, 0), 18f, Palette.AirAlpineColors),
			},
		},
		[Biome.SnowyHills] = new Profile
		{
			Surface = SurfaceRule.Land,
			Layers = new[]
			{
				Layer("ColdFlecks", 36, 8.0f, 0.065f, 0.12f, 0.48f, 0.82f,
					new Vector3(0.34f, -1f, 0.20f), new Vector3(0, -0.28f, 0), 24f, Palette.AirAlpineColors),
			},
		},
		[Biome.Wetland] = new Profile
		{
			Surface = SurfaceRule.Wetland,
			Layers = new[]
			{
				Layer("ReedFluff", 28, 8.0f, 0.055f, 0.14f, 0.34f, 0.62f,
					new Vector3(0.24f, -1f, 0.11f), new Vector3(0, -0.24f, 0), 25f, Palette.AirReedColors),
			},
		},
		[Biome.Shore] = new Profile
		{
			Surface = SurfaceRule.Land,
			Layers = new[]
			{
				Layer("ShoreFlecks", 24, 7.5f, 0.052f, 0.13f, 0.42f, 0.72f,
					new Vector3(0.30f, -1f, 0.16f), new Vector3(0, -0.30f, 0), 22f, Palette.AirReedColors),
			},
		},
	};

	private readonly List<GpuParticles3D> _emitters = new(2);
	private Terrain _terrain;
	private Profile _profile;
	private Biome? _biome;
	private float _probeClock;
	private bool _surfaceAllowed;
	private const float HeightAbovePlayer = 18.0f;
	private const int SurfaceProbeRadius = 26;

	public void Setup(Terrain terrain, Vector3 at)
	{
		_terrain = terrain;
		Position = at + Vector3.Up * HeightAbovePlayer;
		_probeClock = 0f;
		Probe(at, force: true);
	}

	public void Advance(Vector3 playerPosition, double delta)
	{
		GlobalPosition = playerPosition + Vector3.Up * HeightAbovePlayer;

		_probeClock -= (float)delta;
		if (_probeClock <= 0f)
		{
			_probeClock = 0.45f;
			Probe(playerPosition, force: false);
		}

		if (_emitters.Count == 0 || !_surfaceAllowed) SetActive(false);
	}

	private void Probe(Vector3 position, bool force)
	{
		var biome = _terrain.Plan.RegionAt(position.X, position.Z).Biome;
		if (force || _biome != biome)
		{
			_biome = biome;
			Rebuild(Profiles.GetValueOrDefault(biome));
		}

		bool wasAllowed = _surfaceAllowed;
		_surfaceAllowed = _profile != null && SurfaceAllows(_profile.Surface, position);
		if (!_surfaceAllowed)
		{
			SetActive(false);
		}
		else if (!wasAllowed)
		{
			// Re-entering suitable ground explicitly wakes and pre-fills the steady
			// overhead stream. There is no burst phase and no empty calm phase.
			SetActive(true, restart: true);
		}
	}

	private bool SurfaceAllows(SurfaceRule rule, Vector3 position)
	{
		int x = Math.Clamp(Mathf.FloorToInt(position.X), 0, _terrain.Size - 1);
		int z = Math.Clamp(Mathf.FloorToInt(position.Z), 0, _terrain.Size - 1);
		int centre = z * _terrain.Size + x;
		if (_terrain.Land[centre] == 0 || _terrain.Level[centre] <= 0) return false;
		if (rule == SurfaceRule.Land) return true;

		// Inspect the emitter's footprint, not only the block under the player. A
		// village road is wider than the old three-block probe and was therefore
		// able to suppress every meadow particle even with grass in the shot.
		for (int dz = -SurfaceProbeRadius; dz <= SurfaceProbeRadius; dz++)
		for (int dx = -SurfaceProbeRadius; dx <= SurfaceProbeRadius; dx++)
		{
			int sx = Math.Clamp(x + dx, 0, _terrain.Size - 1);
			int sz = Math.Clamp(z + dz, 0, _terrain.Size - 1);
			int i = sz * _terrain.Size + sx;
			if (_terrain.Land[i] == 0 || _terrain.Level[i] <= 0) continue;
			byte cap = _terrain.Grid.At(sx, _terrain.Level[i] - 1, sz);
			bool grass = Palette.IsGrassSurface(cap) || cap is Palette.MOSS or Palette.BLOSSOM_DRIFT;
			if (rule == SurfaceRule.Grass && grass) return true;
			if (rule == SurfaceRule.Wetland && (grass || cap == Palette.MUD)) return true;
		}
		return false;
	}

	private void Rebuild(Profile profile)
	{
		SetActive(false);
		foreach (var emitter in _emitters)
		{
			RemoveChild(emitter);
			emitter.QueueFree();
		}
		_emitters.Clear();
		_profile = profile;
		_surfaceAllowed = false;

		if (profile == null) return;
		foreach (var layer in profile.Layers)
		{
			var emitter = BuildEmitter(layer);
			AddChild(emitter);
			_emitters.Add(emitter);
		}
		// Probe activates the rebuilt emitters once it has confirmed that their
		// surface family is present around the player.
	}

	private static GpuParticles3D BuildEmitter(LayerSpec spec)
	{
		var process = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			// Spawn from a thin band above the view. Global-space particles then fall
			// through it while the band follows the player, matching the old readable
			// motion without returning to large rectangular confetti.
			EmissionBoxExtents = new Vector3(26f, 1.4f, 26f),
			Direction = spec.Direction.Normalized(),
			Spread = spec.Spread,
			Gravity = spec.Gravity,
			InitialVelocityMin = spec.SpeedMin,
			InitialVelocityMax = spec.SpeedMax,
			AngularVelocityMin = -105f,
			AngularVelocityMax = 105f,
			ScaleMin = 0.78f,
			ScaleMax = 1.18f,
			DampingMin = 0.025f,
			DampingMax = 0.10f,
			ColorInitialRamp = PaletteTexture(spec.Colors),
			ColorRamp = LifetimeFade(),
		};

		return new GpuParticles3D
		{
			Name = spec.Name,
			Amount = spec.Amount,
			Lifetime = spec.Lifetime,
			// Pre-fill a complete lifetime on activation, avoiding several seconds
			// where only one or two newly born particles exist.
			Preprocess = spec.Lifetime,
			Randomness = 0.82f,
			FixedFps = 30,
			Interpolate = true,
			FractDelta = true,
			LocalCoords = false,
			Emitting = false,
			AmountRatio = 1f,
			ProcessMaterial = process,
			DrawPass1 = FleckMesh(spec.Width, spec.Length),
			VisibilityAabb = new Aabb(new Vector3(-34f, -28f, -34f), new Vector3(68f, 36f, 68f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private static ArrayMesh FleckMesh(float width, float length)
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		var vertices = new[]
		{
			new Vector3(0f, -length * 0.52f, 0f),
			new Vector3(width * 0.5f, -length * 0.06f, 0f),
			new Vector3(0f, length * 0.48f, 0f),
			new Vector3(-width * 0.5f, -length * 0.06f, 0f),
		};
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
		arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, material);
		return mesh;
	}

	private static GradientTexture1D PaletteTexture(Color[] colors)
	{
		var offsets = new float[colors.Length];
		for (int i = 0; i < offsets.Length; i++)
			offsets[i] = offsets.Length == 1 ? 0f : i / (float)offsets.Length;
		var gradient = new Gradient
		{
			Offsets = offsets,
			Colors = colors,
			InterpolationMode = Gradient.InterpolationModeEnum.Constant,
		};
		return new GradientTexture1D { Gradient = gradient, Width = 32 };
	}

	private static GradientTexture1D LifetimeFade()
	{
		var gradient = new Gradient
		{
			Offsets = new[] { 0f, 0.16f, 0.78f, 1f },
			Colors = new[]
			{
				new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.88f),
				new Color(1f, 1f, 1f, 0.88f), new Color(1f, 1f, 1f, 0f),
			},
		};
		return new GradientTexture1D { Gradient = gradient, Width = 32 };
	}

	private void SetActive(bool active, bool restart = false)
	{
		foreach (var emitter in _emitters)
		{
			emitter.Emitting = active;
			if (active && restart) emitter.Restart(keepSeed: false);
		}
	}

}
