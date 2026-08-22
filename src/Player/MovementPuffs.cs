using Godot;
using Petalfell.Core;

namespace Petalfell.Player;

/// <summary>
/// Small voxel dust released at the traveller's feet.
///
/// The walking stream and the two one-shot bursts share one visual language,
/// but remain separate emitters so jumping never changes the walking cadence.
/// Particles use global coordinates: once a cube leaves a foot it stays on that
/// patch of ground instead of being dragged along with the player.
/// </summary>
public partial class MovementPuffs : Node3D
{
	private GpuParticles3D _steps;
	private GpuParticles3D _jump;
	private GpuParticles3D _land;
	private bool _stateReady;
	private bool _wasGrounded;

	public override void _Ready()
	{
		// This node is positioned from the interpolated player transform every
		// render frame; physics interpolation here would add a second delay.
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

		_steps = BuildEmitter("StepPuffs", amount: 5, lifetime: 0.36f,
			minSpeed: 0.18f, maxSpeed: 0.38f, spread: 52f, oneShot: false,
			opacity: 0.36f, size: 0.78f, inheritedVelocity: 0.58f);
		_jump = BuildEmitter("JumpPuff", amount: 5, lifetime: 0.68f,
			minSpeed: 0.48f, maxSpeed: 0.92f, spread: 68f, oneShot: true,
			opacity: 0.62f, size: 1f, inheritedVelocity: 0f);
		_land = BuildEmitter("LandingPuff", amount: 9, lifetime: 0.78f,
			minSpeed: 0.52f, maxSpeed: 1.08f, spread: 76f, oneShot: true,
			opacity: 0.62f, size: 1f, inheritedVelocity: 0f);

		AddChild(_steps);
		AddChild(_jump);
		AddChild(_land);
	}

	public void Advance(Vector3 playerPosition, Vector3 velocity,
		bool grounded, bool swimming)
	{
		GlobalPosition = playerPosition + Vector3.Up * 0.08f;

		float speed = new Vector2(velocity.X, velocity.Z).Length();
		bool walking = grounded && !swimming && speed > 1.1f;
		_steps.Emitting = walking;
		_steps.AmountRatio = walking
			? Mathf.Clamp(speed / Controller.MaxSpeed, 0.35f, 1f)
			: 0f;

		if (!_stateReady)
		{
			_stateReady = true;
			_wasGrounded = grounded;
			return;
		}

		if (!swimming && _wasGrounded && !grounded && velocity.Y > 1f)
			Burst(_jump);
		else if (!swimming && !_wasGrounded && grounded)
			Burst(_land);

		_wasGrounded = grounded;
	}

	private static void Burst(GpuParticles3D emitter)
	{
		emitter.Emitting = true;
		emitter.Restart(keepSeed: false);
	}

	private static GpuParticles3D BuildEmitter(string name, int amount,
		float lifetime, float minSpeed, float maxSpeed, float spread, bool oneShot,
		float opacity, float size, float inheritedVelocity)
	{
		var process = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(0.42f, 0.035f, 0.34f),
			Direction = Vector3.Up,
			Spread = spread,
			Gravity = new Vector3(0f, -0.42f, 0f),
			InitialVelocityMin = minSpeed,
			InitialVelocityMax = maxSpeed,
			AngularVelocityMin = -150f,
			AngularVelocityMax = 150f,
			ScaleMin = 0.55f,
			ScaleMax = 1.10f,
			ScaleCurve = PuffScale(),
			DampingMin = 0.08f,
			DampingMax = 0.22f,
			InheritVelocityRatio = inheritedVelocity,
			ColorInitialRamp = PuffPalette(),
			ColorRamp = PuffFade(opacity),
		};

		return new GpuParticles3D
		{
			Name = name,
			Amount = amount,
			Lifetime = lifetime,
			OneShot = oneShot,
			Explosiveness = oneShot ? 1f : 0.15f,
			Randomness = 0.88f,
			FixedFps = 30,
			Interpolate = true,
			FractDelta = true,
			LocalCoords = false,
			Emitting = false,
			ProcessMaterial = process,
			DrawPass1 = PuffMesh(size),
			VisibilityAabb = new Aabb(new Vector3(-3f, -1f, -3f), new Vector3(6f, 4f, 6f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private static BoxMesh PuffMesh(float scale)
	{
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Colors.White,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		return new BoxMesh
		{
			Size = new Vector3(0.105f, 0.085f, 0.105f) * scale,
			Material = material,
		};
	}

	private static GradientTexture1D PuffPalette()
	{
		var colors = Palette.FootPuffColors;
		var offsets = new float[colors.Length];
		for (int i = 0; i < offsets.Length; i++)
			offsets[i] = offsets.Length == 1 ? 0f : i / (float)(offsets.Length - 1);
		return new GradientTexture1D
		{
			Width = 32,
			Gradient = new Gradient
			{
				Offsets = offsets,
				Colors = colors,
				InterpolationMode = Gradient.InterpolationModeEnum.Constant,
			},
		};
	}

	private static GradientTexture1D PuffFade(float opacity) => new()
	{
		Width = 32,
		Gradient = new Gradient
		{
			Offsets = new[] { 0f, 0.12f, 0.62f, 1f },
			Colors = new[]
			{
				new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, opacity),
				new Color(1f, 1f, 1f, opacity * 0.61f), new Color(1f, 1f, 1f, 0f),
			},
		},
	};

	private static CurveTexture PuffScale()
	{
		var curve = new Curve { MinValue = 0f, MaxValue = 2f };
		curve.AddPoint(new Vector2(0f, 0.55f));
		curve.AddPoint(new Vector2(0.30f, 1.0f));
		curve.AddPoint(new Vector2(1f, 1.45f));
		return new CurveTexture { Curve = curve };
	}
}
