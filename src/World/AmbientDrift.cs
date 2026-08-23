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
		public Vector3 EmissionExtents = new(26f, 9.5f, 26f);
		public bool Firefly;
		/// <summary>
		/// Motes rather than flecks: a square drawn additively and above the
		/// glow threshold, so the environment's bloom picks it up as a soft
		/// point of light. The reference frames are dusted with these and they
		/// are most of what makes the air feel lit rather than empty.
		/// </summary>
		public bool Glow;
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

	/// <summary>
	/// The drifting motes, shared by every province. Slow, near-weightless and
	/// sparse: a dozen or so in frame, never a snowfall.
	/// </summary>
	private static LayerSpec Motes() => new()
	{
		Name = "Motes", Amount = 30, Lifetime = 11f,
		Width = 0.085f, Length = 0.085f, SpeedMin = 0.05f, SpeedMax = 0.20f,
		Direction = new Vector3(0.2f, -1f, 0.1f), Gravity = new Vector3(0, -0.035f, 0),
		Spread = 40f, Colors = Palette.MoteColors, Glow = true,
	};

	/// <summary>
	/// A handful across the whole visible field, not a second particle shower.
	/// They live near the grass, drift almost weightlessly, and their authored
	/// colour is bright enough to bloom without adding point lights.
	/// </summary>
	private static LayerSpec Fireflies() => new()
	{
		Name = "Fireflies", Amount = 8, Lifetime = 12.5f,
		Width = 0.045f, Length = 0.045f, SpeedMin = 0.035f, SpeedMax = 0.13f,
		Direction = new Vector3(0.20f, 0.08f, 0.13f), Gravity = Vector3.Zero,
		Spread = 180f, Colors = Palette.FireflyColors, Glow = true,
		EmissionExtents = new Vector3(18f, 1.7f, 18f), Firefly = true,
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
				Layer("FlowerPetals", 20, 7.5f, 0.130f, 0.185f, 0.42f, 0.72f,
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
				Layer("BlossomPetals", 40, 8.5f, 0.170f, 0.235f, 0.38f, 0.70f,
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
	private GpuParticles3D _motes;
	private GpuParticles3D _fireflies;
	private float _fireflyFade;
	private Terrain _terrain;
	private Profile _profile;
	private Biome? _biome;
	private float _probeClock;
	private bool _surfaceAllowed;
	/// <summary>
	/// Centre of the drifting column, above the player.
	///
	/// Not a ceiling. Spawning a thin band eighteen units up and letting it fall
	/// puts every particle out of frame for its whole life: at these speeds a
	/// petal descends about five units in eight seconds, so it lives between
	/// twelve and eighteen units overhead and the play camera never sees it. The
	/// emission box below is tall enough to fill the visible column instead, so
	/// petals drift past the traveller rather than only over them.
	/// </summary>
	private const float HeightAbovePlayer = 9.0f;
	private const int SurfaceProbeRadius = 26;

	public void Setup(Terrain terrain, Vector3 at)
	{
		_terrain = terrain;
		Position = at + Vector3.Up * HeightAbovePlayer;
		_probeClock = 0f;
		Probe(at, force: true);
	}

	public void Advance(Vector3 playerPosition, double delta, float nightAmount)
	{
		GlobalPosition = playerPosition + Vector3.Up * HeightAbovePlayer;

		_probeClock -= (float)delta;
		if (_probeClock <= 0f)
		{
			_probeClock = 0.45f;
			Probe(playerPosition, force: false);
		}

		if (_emitters.Count == 0 || !_surfaceAllowed) SetActive(false);
		UpdateFireflies(nightAmount, (float)delta);
	}

	private void Probe(Vector3 position, bool force)
	{
		var biome = _terrain.Plan.RegionAt(position.X, position.Z).Biome;
		bool rebuilt = false;
		if (force || _biome != biome)
		{
			_biome = biome;
			Rebuild(Profiles.GetValueOrDefault(biome));
			rebuilt = true;
		}

		bool wasAllowed = _surfaceAllowed;
		_surfaceAllowed = _profile != null && SurfaceAllows(_profile.Surface, position);
		if (!_surfaceAllowed)
		{
			SetActive(false);
		}
		// A rebuild has to wake its own emitters. They are constructed switched
		// off, so activating only on a disallowed-to-allowed transition leaves
		// them dark for good whenever the province changes while the ground was
		// already suitable — which is every ordinary walk across a biome
		// boundary, and the air simply stops for the rest of the session.
		else if (!wasAllowed || rebuilt)
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
		_motes = null;
		if (_fireflies != null)
		{
			RemoveChild(_fireflies);
			_fireflies.QueueFree();
			_fireflies = null;
		}
		_profile = profile;
		_surfaceAllowed = false;

		if (profile == null) return;
		foreach (var layer in profile.Layers)
		{
			var emitter = BuildEmitter(layer);
			AddChild(emitter);
			_emitters.Add(emitter);
		}
		// Motes belong to the air, not to any one province, so they are appended
		// here rather than repeated in every profile table.
		_motes = BuildEmitter(Motes());
		AddChild(_motes);
		_emitters.Add(_motes);

		// AmbientDrift itself rides above the player so falling leaves can cross the
		// whole camera column. Fireflies instead hover just above the local ground.
		_fireflies = BuildEmitter(Fireflies());
		_fireflies.Position = Vector3.Down * (HeightAbovePlayer - 1.8f);
		_fireflies.Transparency = 1f;
		AddChild(_fireflies);
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
			EmissionBoxExtents = spec.EmissionExtents,
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
			ColorRamp = spec.Firefly ? FireflyFlicker() : LifetimeFade(),
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
			DrawPass1 = spec.Glow
				? spec.Firefly ? DotMesh(spec.Width) : MoteMesh(spec.Width)
				: FleckMesh(spec.Width, spec.Length),
			VisibilityAabb = new Aabb(new Vector3(-34f, -28f, -34f), new Vector3(68f, 36f, 68f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	/// <summary>
	/// A mote: a small square drawn additively with its colour pushed above 1.0
	/// so the environment's glow threshold catches it. Kept square and tiny —
	/// at any real size an additive quad stops reading as a point of light and
	/// starts reading as a smear.
	/// </summary>
	private static ArrayMesh MoteMesh(float size)
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
		};

		float h = size * 0.5f;
		var vertices = new[]
		{
			new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f),
			new Vector3(h, h, 0f), new Vector3(-h, h, 0f),
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

	/// <summary>A real round billboard, so a firefly stays a dot even in bloom.</summary>
	private static ArrayMesh DotMesh(float size)
	{
		const int segments = 10;
		float radius = size * 0.5f;
		var vertices = new Vector3[segments + 1];
		var normals = new Vector3[segments + 1];
		var indices = new int[segments * 3];
		vertices[0] = Vector3.Zero;
		for (int i = 0; i <= segments; i++)
		{
			if (i > 0)
			{
				float angle = (i - 1) * Mathf.Tau / segments;
				vertices[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
			}
			normals[i] = Vector3.Back;
		}
		for (int i = 0; i < segments; i++)
		{
			indices[i * 3] = 0;
			indices[i * 3 + 1] = i + 1;
			indices[i * 3 + 2] = (i + 1) % segments + 1;
		}

		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
		};
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, material);
		return mesh;
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

	private static GradientTexture1D FireflyFlicker()
	{
		// Uneven light pulses over each lifetime. Birth times are randomized by the
		// emitter, so the points never blink in unison.
		var gradient = new Gradient
		{
			Offsets = new[] { 0f, 0.08f, 0.23f, 0.37f, 0.56f, 0.70f, 0.88f, 1f },
			Colors = new[]
			{
				new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.92f),
				new Color(1f, 1f, 1f, 0.34f), new Color(1f, 1f, 1f, 1f),
				new Color(1f, 1f, 1f, 0.24f), new Color(1f, 1f, 1f, 0.86f),
				new Color(1f, 1f, 1f, 0.48f), new Color(1f, 1f, 1f, 0f),
			},
		};
		return new GradientTexture1D { Gradient = gradient, Width = 64 };
	}

	private void UpdateFireflies(float nightAmount, float delta)
	{
		if (_fireflies == null) return;

		// Fireflies wait until the scene reads as night, then ease in rather than
		// appearing as a synchronized switch at sunset.
		float target = Mathf.Clamp((nightAmount - 0.45f) / 0.35f, 0f, 1f);
		target = target * target * (3f - 2f * target);
		if (!_surfaceAllowed) target = 0f;
		if (_motes != null)
		{
			// Daylight dust and night fireflies trade places; retaining both would
			// turn eight deliberate points into a field of nearly forty lights.
			_motes.AmountRatio = 1f - target;
			_motes.Transparency = target;
		}

		_fireflyFade = Mathf.Lerp(_fireflyFade, target,
			1f - Mathf.Exp(-2.4f * Mathf.Max(delta, 0f)));
		bool shouldEmit = target > 0.01f;
		if (shouldEmit && !_fireflies.Emitting)
		{
			_fireflies.Emitting = true;
			_fireflies.Restart(keepSeed: false);
		}
		else if (!shouldEmit)
		{
			_fireflies.Emitting = false;
		}

		_fireflies.AmountRatio = target;
		_fireflies.Transparency = 1f - _fireflyFade;
		_fireflies.Visible = _fireflyFade > 0.005f;
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
