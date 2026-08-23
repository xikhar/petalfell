using System;
using Godot;
using Petalfell.Core;
using Petalfell.Items;
using Petalfell.Render;

namespace Petalfell.World;

/// <summary>
/// One compact, runtime campfire. It is deliberately independent of terrain
/// chunks: unloading and rebuilding the ground around it must not destroy a
/// player-placed object.
/// </summary>
public partial class Campfire : Node3D
{
	public const float CollisionRadius = 1.12f;

	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private Rng _rng;
	private OmniLight3D _fireLight;
	private float _lightPhase;
	private bool _built;

	/// <summary>Build the visual, collision, flame, light and smoke once.</summary>
	public void Setup(ShaderMaterial inkLight, ShaderMaterial inkDark, int visualSeed)
	{
		if (_built) return;
		_built = true;
		_inkLight = inkLight;
		_inkDark = inkDark;
		_rng = new Rng(visualSeed);
		_lightPhase = _rng.Range(0f, Mathf.Tau);

		BuildHearth();
		BuildFire();
		BuildCollision();
	}

	private void BuildHearth()
	{
		var stone = Tone(0xb8afc3);
		var stoneShade = Tone(0x9f94ad);
		var wood = Tone(0x8f5c4d);
		var woodDeep = Tone(0x684553);
		var ember = Tone(0x4f3948);

		// A loose octagonal stone ring. Pale ink keeps the small separate pieces
		// readable without turning the hearth into a knot of dark internal lines.
		for (int i = 0; i < 8; i++)
		{
			float angle = i / 8f * Mathf.Tau + _rng.Range(-0.045f, 0.045f);
			float radius = _rng.Range(0.84f, 0.91f);
			float height = _rng.Range(0.23f, 0.30f);
			var at = new Vector3(Mathf.Cos(angle) * radius, height * 0.5f,
				Mathf.Sin(angle) * radius);
			var rock = Box(new Vector3(_rng.Range(0.46f, 0.56f), height,
				_rng.Range(0.36f, 0.44f)), at,
				i % 3 == 0 ? stoneShade : stone, outlined: true, lightOutline: true);
			rock.Rotation = new Vector3(_rng.Range(-0.035f, 0.035f),
				-angle + _rng.Range(-0.10f, 0.10f), _rng.Range(-0.025f, 0.025f));
		}

		// Two broad crossed courses. Small seeded offsets keep every built fire
		// handmade-looking while remaining deterministic across save/load later.
		for (int course = 0; course < 2; course++)
		for (int side = -1; side <= 1; side += 2)
		{
			float length = _rng.Range(course == 0 ? 1.50f : 1.38f,
				course == 0 ? 1.74f : 1.62f);
			float thickness = _rng.Range(0.29f, 0.36f);
			float across = side * _rng.Range(0.20f, 0.27f);
			var at = course == 0
				? new Vector3(_rng.Range(-0.05f, 0.05f), 0.26f, across)
				: new Vector3(across, 0.51f, _rng.Range(-0.05f, 0.05f));
			var log = Box(new Vector3(length, thickness, thickness), at,
				(course + side + 3) % 3 == 0 ? woodDeep : wood, outlined: true);
			float baseYaw = course == 0 ? 0f : Mathf.Pi * 0.5f;
			log.Rotation = new Vector3(_rng.Range(-0.035f, 0.035f),
				baseYaw + _rng.Range(-0.16f, 0.16f), _rng.Range(-0.035f, 0.035f));
		}

		// A few unoutlined coals give the flame a dark visual root without adding
		// tiny overlapping strokes at the exact centre of the fire.
		Box(new Vector3(0.42f, 0.11f, 0.36f), new Vector3(-0.20f, 0.49f, 0.02f),
			ember, outlined: false);
		Box(new Vector3(0.35f, 0.10f, 0.42f), new Vector3(0.19f, 0.50f, -0.05f),
			ember, outlined: false);
	}

	private void BuildFire()
	{
		var flame = new CampfireFlame
		{
			Name = "Flame",
			Position = new Vector3(0f, 0.48f, 0f),
		};
		flame.Setup(0.86f);
		AddChild(flame);

		// The luminous source sits above the logs and behaves as a small spherical
		// area light. A wide PCSS penumbra and low opacity remove the radial hard
		// wedges produced when a point source lived inside the crossed wood.
		_fireLight = new OmniLight3D
		{
			Name = "WarmLight",
			Position = new Vector3(0f, 1.20f, 0f),
			LightColor = new Color(1.0f, 0.49f, 0.24f),
			LightEnergy = 5.35f,
			LightSpecular = 0f,
			LightSize = 0.48f,
			OmniRange = 11.5f,
			OmniAttenuation = 1.20f,
			OmniShadowMode = OmniLight3D.ShadowMode.Cube,
			ShadowEnabled = true,
			ShadowBlur = 1.35f,
			ShadowOpacity = 0.18f,
			LightVolumetricFogEnergy = 0f,
			LightBakeMode = Light3D.BakeMode.Dynamic,
		};
		AddChild(_fireLight);

		var smoke = new GpuParticles3D
		{
			Name = "Smoke",
			Position = new Vector3(0f, 0.92f, 0f),
			Amount = 9,
			Lifetime = 2.8f,
			Preprocess = 2.8f,
			Randomness = 0.82f,
			FixedFps = 24,
			Interpolate = true,
			FractDelta = true,
			LocalCoords = true,
			Emitting = true,
			ProcessMaterial = SmokeProcess(),
			DrawPass1 = SmokeMesh(),
			VisibilityAabb = new Aabb(new Vector3(-1.6f, -0.5f, -1.6f),
				new Vector3(3.2f, 5.8f, 3.2f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(smoke);
	}

	public override void _Process(double delta)
	{
		if (_fireLight == null) return;
		_lightPhase += (float)delta;
		float slow = Mathf.Sin(_lightPhase * 6.3f);
		float quick = Mathf.Sin(_lightPhase * 11.7f + 1.4f);
		_fireLight.LightEnergy = 5.35f * (0.94f + slow * 0.035f + quick * 0.018f);
		_fireLight.Position = new Vector3(slow * 0.025f,
			1.20f + quick * 0.018f, quick * 0.020f);
	}

	private void BuildCollision()
	{
		var body = new StaticBody3D { Name = "HearthCollision" };
		body.AddChild(new CollisionShape3D
		{
			Position = new Vector3(0f, 0.25f, 0f),
			Shape = new CylinderShape3D
			{
				Radius = CollisionRadius,
				Height = 0.50f,
			},
		});
		AddChild(body);
	}

	private MeshInstance3D Box(Vector3 size, Vector3 at, Color color,
		bool outlined, bool lightOutline = false)
	{
		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/character.gdshader"),
		};
		material.SetShaderParameter("albedo", color);

		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			Position = at,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		AddChild(mesh);

		if (!outlined) return mesh;
		var ink = new MeshInstance3D
		{
			Mesh = InkBuilder.Box(size.X, size.Y, size.Z, lightOutline),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			CustomAabb = new Aabb(-size, size * 2f),
		};
		ink.SetSurfaceOverrideMaterial(0, lightOutline ? _inkLight : _inkDark);
		mesh.AddChild(ink);
		return mesh;
	}

	private static Color Tone(uint hex)
	{
		var srgb = new Color(((hex >> 16) & 255) / 255f,
			((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
		return srgb.SrgbToLinear();
	}

	private static ParticleProcessMaterial SmokeProcess()
	{
		var scale = new Curve { MinValue = 0f, MaxValue = 2f };
		scale.AddPoint(new Vector2(0f, 0.42f));
		scale.AddPoint(new Vector2(0.35f, 0.88f));
		scale.AddPoint(new Vector2(1f, 1.48f));

		return new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(0.14f, 0.05f, 0.14f),
			Direction = new Vector3(0.12f, 1f, 0.06f).Normalized(),
			Spread = 13f,
			Gravity = new Vector3(0.055f, 0.08f, 0.025f),
			InitialVelocityMin = 0.42f,
			InitialVelocityMax = 0.78f,
			AngularVelocityMin = -70f,
			AngularVelocityMax = 70f,
			ScaleMin = 0.72f,
			ScaleMax = 1.12f,
			ScaleCurve = new CurveTexture { Curve = scale },
			DampingMin = 0.02f,
			DampingMax = 0.08f,
			ColorInitialRamp = new GradientTexture1D
			{
				Width = 24,
				Gradient = new Gradient
				{
					Offsets = new[] { 0f, 0.55f, 1f },
					Colors = new[]
					{
						new Color(0.43f, 0.38f, 0.48f),
						new Color(0.58f, 0.52f, 0.62f),
						new Color(0.70f, 0.66f, 0.73f),
					},
				},
			},
			ColorRamp = new GradientTexture1D
			{
				Width = 32,
				Gradient = new Gradient
				{
					Offsets = new[] { 0f, 0.16f, 0.66f, 1f },
					Colors = new[]
					{
						new Color(1f, 1f, 1f, 0f),
						new Color(1f, 1f, 1f, 0.24f),
						new Color(1f, 1f, 1f, 0.13f),
						new Color(1f, 1f, 1f, 0f),
					},
				},
			},
		};
	}

	private static BoxMesh SmokeMesh(float size = 0.16f)
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			DisableReceiveShadows = true,
		};
		return new BoxMesh
		{
			Size = new Vector3(size, size * 0.82f, size),
			Material = material,
		};
	}
}
