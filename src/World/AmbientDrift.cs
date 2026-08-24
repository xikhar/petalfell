using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Sparse airborne life around the traveller.
///
/// The pieces are a small CPU pool but render through two MultiMeshes, so they
/// retain real world positions and terrain-aware landing without turning into
/// a collection of nodes or draw calls. Moving the player never moves an
/// existing leaf: it finishes its own fall, rests briefly on its own patch of
/// ground, fades, and is only then recycled near the current play area.
/// </summary>
public partial class AmbientDrift : Node3D
{
	private enum SurfaceRule { Grass, Land, Wetland }

	private sealed class LayerSpec
	{
		public int Weight;
		public float Width, Length;
		public float SpeedMin, SpeedMax;
		public Color[] Colors;
	}

	private sealed class Profile
	{
		public SurfaceRule Surface;
		public LayerSpec[] Layers;
	}

	private sealed class FallingPiece
	{
		public Vector3 Position;
		public Vector3 Velocity;
		public Color Color;
		public float Width, Length;
		public float GroundY;
		public float Age;
		public float Alpha;
		public float Rest;
		public float Spin;
		public Vector3 Angles;
		public Vector3 SpinAxis;
		public bool Landed;
	}

	private sealed class Firefly
	{
		public Vector3 Position;
		public Vector2 Drift;
		public Color Color;
		public float GroundY;
		public float Hover;
		public float Phase;
		public float PhaseSpeed;
		public float Size;
		public float Fade;
		public float Age;
		public float Lifetime;
	}

	private static LayerSpec Layer(int weight, float width, float length,
		float speedMin, float speedMax, Color[] colors) => new()
	{
		Weight = weight,
		Width = width,
		Length = length,
		SpeedMin = speedMin,
		SpeedMax = speedMax,
		Colors = colors,
	};

	private static readonly Dictionary<Biome, Profile> Profiles = new()
	{
		[Biome.Meadow] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer(3, 0.085f, 0.18f, 0.45f, 0.82f, Palette.AirLeafColors),
				Layer(1, 0.105f, 0.15f, 0.32f, 0.62f, Palette.AirPetalColors),
			},
		},
		[Biome.Forest] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer(5, 0.090f, 0.19f, 0.38f, 0.74f, Palette.AirLeafColors),
				Layer(1, 0.080f, 0.13f, 0.28f, 0.52f, Palette.AirPetalColors),
			},
		},
		[Biome.Plains] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer(1, 0.075f, 0.16f, 0.58f, 0.98f, Palette.AirLeafColors),
			},
		},
		[Biome.Sakura] = new Profile
		{
			Surface = SurfaceRule.Grass,
			Layers = new[]
			{
				Layer(1, 0.075f, 0.16f, 0.38f, 0.68f, Palette.AirLeafColors),
				Layer(3, 0.125f, 0.18f, 0.28f, 0.58f, Palette.AirPetalColors),
			},
		},
		[Biome.Highland] = new Profile
		{
			Surface = SurfaceRule.Land,
			Layers = new[]
			{
				Layer(1, 0.060f, 0.13f, 0.62f, 1.04f, Palette.AirAlpineColors),
			},
		},
		[Biome.SnowyHills] = new Profile
		{
			Surface = SurfaceRule.Land,
			Layers = new[]
			{
				Layer(1, 0.055f, 0.11f, 0.42f, 0.76f, Palette.AirAlpineColors),
			},
		},
		[Biome.Wetland] = new Profile
		{
			Surface = SurfaceRule.Wetland,
			Layers = new[]
			{
				Layer(1, 0.052f, 0.13f, 0.30f, 0.56f, Palette.AirReedColors),
			},
		},
		[Biome.Shore] = new Profile
		{
			Surface = SurfaceRule.Land,
			Layers = new[]
			{
				Layer(1, 0.050f, 0.12f, 0.38f, 0.66f, Palette.AirReedColors),
			},
		},
	};

	// Across a radius larger than the play frame, fourteen pieces reads as an
	// occasional leaf or petal rather than continuous weather.
	private const int FallingCount = 14;
	private const int FireflyCount = 7;
	private const float SpawnRadius = 28f;
	private const float RecycleRadius = 52f;

	private readonly List<FallingPiece> _falling = new(FallingCount);
	private readonly List<Firefly> _fireflies = new(FireflyCount);
	private Terrain _terrain;
	private Rng _rng;
	private Profile _profile;
	private Biome? _biome;
	private bool _surfaceAllowed;
	private float _probeClock;
	private float _dayFade;
	private float _nightFade;
	private MultiMesh _fallingMesh;
	private MultiMesh _fireflyMesh;

	public void Setup(Terrain terrain, Vector3 at)
	{
		_terrain = terrain;
		_rng ??= new Rng(unchecked(terrain.Size * 0x45d9f3b) ^ 0x71A1F17E);
		if (_fallingMesh == null) BuildRenderers();

		_biome = null;
		_probeClock = 0f;
		Probe(at, force: true);
		ResetPool(at);
	}

	public void Advance(Vector3 playerPosition, double delta, float nightAmount)
	{
		float dt = Mathf.Min((float)delta, 0.05f);
		_probeClock -= dt;
		if (_probeClock <= 0f)
		{
			_probeClock = 0.55f;
			Probe(playerPosition, force: false);
		}

		float dayTarget = _surfaceAllowed ? 1f - Rng.Smoothstep(0.24f, 0.58f, nightAmount) : 0f;
		float nightTarget = _surfaceAllowed ? Rng.Smoothstep(0.30f, 0.64f, nightAmount) : 0f;
		_dayFade = Mathf.Lerp(_dayFade, dayTarget, 1f - Mathf.Exp(-2.0f * dt));
		_nightFade = Mathf.Lerp(_nightFade, nightTarget, 1f - Mathf.Exp(-2.4f * dt));

		AdvanceFalling(playerPosition, dt);
		AdvanceFireflies(playerPosition, dt);
	}

	private void BuildRenderers()
	{
		var fleckMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
			Roughness = 1f,
		};
		_fallingMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new BoxMesh { Size = Vector3.One, Material = fleckMaterial },
			InstanceCount = FallingCount,
		};
		AddChild(new MultiMeshInstance3D
		{
			Name = "FallingLeaves",
			Multimesh = _fallingMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		var fireflyMaterial = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/firefly.gdshader"),
		};
		fireflyMaterial.SetShaderParameter("emission_strength", 2.2f);
		_fireflyMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = new QuadMesh
			{
				Size = Vector2.One,
				Material = fireflyMaterial,
			},
			InstanceCount = FireflyCount,
		};
		AddChild(new MultiMeshInstance3D
		{
			Name = "NightFireflies",
			Multimesh = _fireflyMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	private void ResetPool(Vector3 around)
	{
		_falling.Clear();
		for (int i = 0; i < FallingCount; i++)
		{
			var piece = new FallingPiece();
			_falling.Add(piece);
			Respawn(piece, around, initial: true);
		}

		_fireflies.Clear();
		for (int i = 0; i < FireflyCount; i++)
		{
			var firefly = new Firefly();
			_fireflies.Add(firefly);
			Respawn(firefly, around, initial: true);
		}
	}

	private void Probe(Vector3 position, bool force)
	{
		var biome = _terrain.Plan.RegionAt(position.X, position.Z).Biome;
		if (force || _biome != biome)
		{
			_biome = biome;
			_profile = Profiles.GetValueOrDefault(biome);
		}
		_surfaceAllowed = _profile != null && HasSurfaceNear(_profile.Surface, position);
	}

	private bool HasSurfaceNear(SurfaceRule rule, Vector3 position)
	{
		for (int dz = -18; dz <= 18; dz += 3)
		for (int dx = -18; dx <= 18; dx += 3)
			if (TryGround(rule, position.X + dx, position.Z + dz, out _)) return true;
		return false;
	}

	private bool TryGround(SurfaceRule rule, float wx, float wz, out float ground)
	{
		ground = 0f;
		int x = Mathf.FloorToInt(wx);
		int z = Mathf.FloorToInt(wz);
		if (x < 1 || z < 1 || x >= _terrain.Size - 1 || z >= _terrain.Size - 1) return false;
		int i = z * _terrain.Size + x;
		if (_terrain.Land[i] == 0 || _terrain.Level[i] <= 0) return false;

		byte cap = _terrain.Grid.At(x, _terrain.Level[i] - 1, z);
		bool grass = Palette.IsGrassSurface(cap) || cap is Palette.MOSS or Palette.BLOSSOM_DRIFT;
		if (rule == SurfaceRule.Grass && !grass) return false;
		if (rule == SurfaceRule.Wetland && !grass && cap != Palette.MUD) return false;
		ground = _terrain.Level[i] + 0.025f;
		return true;
	}

	private LayerSpec PickLayer()
	{
		if (_profile?.Layers == null || _profile.Layers.Length == 0) return null;
		int total = 0;
		foreach (var layer in _profile.Layers) total += layer.Weight;
		float roll = _rng.Next() * total;
		foreach (var layer in _profile.Layers)
		{
			roll -= layer.Weight;
			if (roll <= 0f) return layer;
		}
		return _profile.Layers[^1];
	}

	private bool Respawn(FallingPiece piece, Vector3 around, bool initial)
	{
		var layer = PickLayer();
		if (layer == null) { piece.Alpha = 0f; return false; }
		for (int attempt = 0; attempt < 18; attempt++)
		{
			float angle = _rng.Next() * MathF.Tau;
			float radius = MathF.Sqrt(_rng.Next()) * SpawnRadius;
			float x = around.X + MathF.Cos(angle) * radius;
			float z = around.Z + MathF.Sin(angle) * radius;
			if (!TryGround(_profile.Surface, x, z, out float ground)) continue;

			float height = initial ? _rng.Range(0.7f, 8.5f) : _rng.Range(6.5f, 9.5f);
			piece.Position = new Vector3(x, ground + height, z);
			piece.GroundY = ground;
			piece.Velocity = new Vector3(_rng.Range(0.04f, 0.18f),
				-_rng.Range(layer.SpeedMin, layer.SpeedMax), _rng.Range(-0.08f, 0.10f));
			piece.Color = _rng.Pick(layer.Colors);
			piece.Width = layer.Width * _rng.Range(0.82f, 1.16f);
			piece.Length = layer.Length * _rng.Range(0.80f, 1.20f);
			piece.Age = initial ? _rng.Range(0.4f, 2.0f) : 0f;
			piece.Alpha = initial ? _rng.Range(0.35f, 0.90f) : 0f;
			piece.Rest = _rng.Range(0.65f, 1.45f);
			piece.Spin = _rng.Range(0.7f, 2.4f) * (_rng.Chance(0.5f) ? -1f : 1f);
			piece.Angles = new Vector3(_rng.Next() * MathF.Tau, _rng.Next() * MathF.Tau, _rng.Next() * MathF.Tau);
			piece.SpinAxis = new Vector3(_rng.Range(0.25f, 0.85f), 1f, _rng.Range(0.2f, 0.7f)).Normalized();
			piece.Landed = false;
			return true;
		}
		piece.Alpha = 0f;
		return false;
	}

	private void AdvanceFalling(Vector3 player, float dt)
	{
		float recycleSq = RecycleRadius * RecycleRadius;
		for (int i = 0; i < _falling.Count; i++)
		{
			var piece = _falling[i];
			piece.Age += dt;
			var delta = new Vector2(piece.Position.X - player.X, piece.Position.Z - player.Z);
			if (delta.LengthSquared() > recycleSq)
				Respawn(piece, player, initial: false);

			if (!piece.Landed)
			{
				float sway = MathF.Sin(piece.Age * 1.7f + i * 2.13f) * 0.055f;
				piece.Position += (piece.Velocity + new Vector3(0f, 0f, sway)) * dt;
				// Re-sample below the drifting piece, not below its birth point. This is
				// what makes it settle onto the actual terrace it reaches.
				if (TryGround(_profile.Surface, piece.Position.X, piece.Position.Z, out float below) &&
					below <= piece.Position.Y + 0.04f)
					piece.GroundY = below;
				piece.Angles += piece.SpinAxis * piece.Spin * dt;
				piece.Alpha = Mathf.Min(1f, piece.Alpha + dt * 1.35f);
				if (piece.Position.Y <= piece.GroundY)
				{
					piece.Position = new Vector3(piece.Position.X, piece.GroundY, piece.Position.Z);
					piece.Angles = new Vector3(0f, piece.Angles.Y, 0f);
					piece.Landed = true;
				}
			}
			else
			{
				piece.Rest -= dt;
				if (piece.Rest <= 0f) piece.Alpha -= dt * 1.8f;
				if (piece.Alpha <= 0f) Respawn(piece, player, initial: false);
			}

			var scale = new Vector3(piece.Width, 0.018f, piece.Length);
			var basis = Basis.FromEuler(piece.Angles).ScaledLocal(scale);
			_fallingMesh.SetInstanceTransform(i, new Transform3D(basis, piece.Position));
			var color = piece.Color;
			color.A = Mathf.Clamp(piece.Alpha * _dayFade, 0f, 1f);
			_fallingMesh.SetInstanceColor(i, color);
		}
	}

	private bool Respawn(Firefly firefly, Vector3 around, bool initial)
	{
		if (_profile == null) { firefly.Fade = 0f; return false; }
		for (int attempt = 0; attempt < 18; attempt++)
		{
			float angle = _rng.Next() * MathF.Tau;
			float radius = MathF.Sqrt(_rng.Next()) * 20f;
			float x = around.X + MathF.Cos(angle) * radius;
			float z = around.Z + MathF.Sin(angle) * radius;
			if (!TryGround(_profile.Surface, x, z, out float ground)) continue;

			firefly.Hover = _rng.Range(0.65f, 2.5f);
			firefly.Position = new Vector3(x, ground + firefly.Hover, z);
			firefly.GroundY = ground;
			firefly.Drift = new Vector2(_rng.Range(-0.10f, 0.10f), _rng.Range(-0.10f, 0.10f));
			firefly.Color = _rng.Pick(Palette.FireflyColors);
			firefly.Phase = _rng.Next() * MathF.Tau;
			firefly.PhaseSpeed = _rng.Range(0.75f, 1.45f);
			firefly.Size = _rng.Range(0.060f, 0.082f);
			firefly.Fade = initial ? _rng.Range(0.45f, 1f) : 0f;
			firefly.Age = initial ? _rng.Range(0f, 8f) : 0f;
			firefly.Lifetime = _rng.Range(18f, 34f);
			return true;
		}
		firefly.Fade = 0f;
		return false;
	}

	private void AdvanceFireflies(Vector3 player, float dt)
	{
		const float recycleSq = 42f * 42f;
		var camera = GetViewport()?.GetCamera3D();
		var billboard = camera?.GlobalBasis.Orthonormalized() ?? Basis.Identity;
		for (int i = 0; i < _fireflies.Count; i++)
		{
			var firefly = _fireflies[i];
			firefly.Age += dt;
			firefly.Phase += firefly.PhaseSpeed * dt;
			var fromPlayer = new Vector2(firefly.Position.X - player.X, firefly.Position.Z - player.Z);
			if (fromPlayer.LengthSquared() > recycleSq || firefly.Age >= firefly.Lifetime)
				Respawn(firefly, player, initial: false);

			float curlX = MathF.Sin(firefly.Phase * 0.83f + i * 1.7f) * 0.065f;
			float curlZ = MathF.Cos(firefly.Phase * 0.69f + i * 2.3f) * 0.065f;
			firefly.Position += new Vector3(firefly.Drift.X + curlX, 0f, firefly.Drift.Y + curlZ) * dt;
			float wantedY = firefly.GroundY + firefly.Hover + MathF.Sin(firefly.Phase * 1.15f) * 0.24f;
			firefly.Position = new Vector3(firefly.Position.X,
				Mathf.Lerp(firefly.Position.Y, wantedY, 1f - Mathf.Exp(-2.1f * dt)), firefly.Position.Z);
			firefly.Fade = Mathf.Min(1f, firefly.Fade + dt * 0.75f);

			float wave = 0.5f + 0.34f * MathF.Sin(firefly.Phase * 2.2f)
				+ 0.16f * MathF.Sin(firefly.Phase * 5.1f + i);
			float pulse = 0.22f + 0.78f * Rng.Smoothstep(0.30f, 0.78f, wave);
			// The quad is wider than the visible core only so its faint radial source
			// survives bloom downsampling. The shader keeps the actual dot pin-small.
			float size = firefly.Size * 3.6f * (0.88f + pulse * 0.18f);
			_fireflyMesh.SetInstanceTransform(i, new Transform3D(
				billboard.ScaledLocal(new Vector3(size, size, 1f)), firefly.Position));
			var color = firefly.Color * (0.58f + pulse * 0.42f);
			color.A = Mathf.Clamp(_nightFade * firefly.Fade * (0.38f + pulse * 0.62f), 0f, 1f);
			_fireflyMesh.SetInstanceColor(i, color);
		}
	}
}
