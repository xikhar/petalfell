using System;
using Godot;

namespace Petalfell.Render;

/// <summary>
/// The camera rig.
///
/// A long-lens near-isometric view: a narrow FOV placed far back gives the
/// flattened, model-railway parallax of the reference images while keeping
/// perspective's depth cues. It trails the player with critically-damped
/// springs so movement feels weighty but never sloppy, and leads slightly in
/// the direction of travel so you can see where you are going.
///
/// How chunky the voxels read is decided here, not in the mesh. A block's size
/// on screen is (block size / camera distance): at 108 units with a 21-degree
/// lens the frame held ~69 blocks and the world dissolved into fine mosaic. The
/// references hold roughly 25 blocks across — big, confident cubes you can
/// count — which means about 50 units out.
/// </summary>
public partial class CameraRig : Camera3D
{
	public float Yaw = Mathf.Pi * 0.25f;
	public float TargetYaw = Mathf.Pi * 0.25f;
	public float Pitch = Mathf.DegToRad(33.5f);
	public float Distance = 75f;
	public float TargetDistance = 75f;
	public float MinDistance = 50f;
	public float MaxDistance = 120f;
	/// <summary>World-space camera-distance units travelled per second by K auto-zoom.</summary>
	public float AutoZoomSpeed = 12f;
	public bool AutoZooming { get; private set; }

	private Vector3 _focus = new(0, 12, 0);
	private Vector3 _smoothFocus = new(0, 12, 0);
	private Vector3 _smoothLead = Vector3.Zero;

	public override void _Ready()
	{
		// Main feeds this camera the player's already-interpolated render transform
		// every frame. Automatic interpolation here would interpolate that result a
		// second time, leaving the camera one transform behind the character.
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		Projection = ProjectionType.Perspective;
		Fov = 21f;
		Near = 1f;
		Far = 620f;
	}

	public void Rotate45(int dir) => TargetYaw += dir * Mathf.Pi * 0.25f;

	public void Zoom(float delta)
	{
		// The wheel is direct author/player intent and must immediately take camera
		// ownership back from a cinematic move.
		if (AutoZooming) TargetDistance = Distance;
		AutoZooming = false;
		TargetDistance = Mathf.Clamp(TargetDistance + delta, MinDistance, MaxDistance);
	}

	/// <summary>Begin a constant-speed dolly to the current widest zoom limit.</summary>
	public void StartAutoZoomToMaximum()
	{
		TargetDistance = MaxDistance;
		AutoZooming = Distance < MaxDistance - 0.001f;
	}

	public void SetZoomLimits(float minimum, float maximum)
	{
		MinDistance = Mathf.Max(1f, minimum);
		MaxDistance = Mathf.Max(MinDistance, maximum);
		TargetDistance = Mathf.Clamp(TargetDistance, MinDistance, MaxDistance);
		Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
		if (AutoZooming) TargetDistance = MaxDistance;
	}

	/// <summary>Critically damped approach — no overshoot, no lag spike on a hard turn.</summary>
	private static float Damp(float current, float target, float lambda, float dt) =>
		Mathf.Lerp(current, target, 1f - Mathf.Exp(-lambda * dt));

	private static Vector3 Damp(Vector3 current, Vector3 target, float lambda, float dt) =>
		current.Lerp(target, 1f - Mathf.Exp(-lambda * dt));

	public void Follow(Vector3 target, Vector3 velocity, double delta)
	{
		float dt = (float)delta;

		Yaw = Damp(Yaw, TargetYaw, 7f, dt);
		if (AutoZooming)
		{
			// MoveToward makes the visible camera distance linear in time. Driving only
			// TargetDistance would still pass through the ordinary exponential spring
			// and produce a fast start with a long asymptotic tail.
			Distance = Mathf.MoveToward(Distance, MaxDistance,
				Mathf.Max(0.01f, AutoZoomSpeed) * dt);
			TargetDistance = Distance;
			if (Distance >= MaxDistance - 0.001f)
			{
				Distance = MaxDistance;
				TargetDistance = MaxDistance;
				AutoZooming = false;
			}
		}
		else Distance = Damp(Distance, TargetDistance, 6f, dt);

		// Frame a little above the feet: the traveller is short against the
		// terraces and centring on them puts the horizon in the wrong place.
		_focus = target + new Vector3(0, 1.6f, 0);
		_smoothFocus = Damp(_smoothFocus, _focus, 9f, dt);

		var flat = new Vector3(velocity.X, 0, velocity.Z);
		Vector3 lead = flat.LengthSquared() > 0.5f ? flat.Normalized() * Mathf.Min(flat.Length() * 0.32f, 4.5f)
		                                           : Vector3.Zero;
		_smoothLead = Damp(_smoothLead, lead, 3.2f, dt);

		var focus = _smoothFocus + _smoothLead;
		var offset = new Vector3(
			Mathf.Sin(Yaw) * Mathf.Cos(Pitch),
			Mathf.Sin(Pitch),
			Mathf.Cos(Yaw) * Mathf.Cos(Pitch)) * Distance;

		GlobalPosition = focus + offset;
		LookAt(focus, Vector3.Up);
	}
}
