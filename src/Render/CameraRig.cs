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
	public float Distance = 50f;
	public float TargetDistance = 50f;
	public float MinDistance = 50f;
	public float MaxDistance = 120f;

	private Vector3 _focus = new(0, 12, 0);
	private Vector3 _smoothFocus = new(0, 12, 0);
	private Vector3 _smoothLead = Vector3.Zero;

	public override void _Ready()
	{
		Projection = ProjectionType.Perspective;
		Fov = 21f;
		Near = 1f;
		Far = 620f;
	}

	public void Rotate45(int dir) => TargetYaw += dir * Mathf.Pi * 0.25f;

	public void Zoom(float delta)
	{
		TargetDistance = Mathf.Clamp(TargetDistance + delta, MinDistance, MaxDistance);
	}

	public void SetZoomLimits(float minimum, float maximum)
	{
		MinDistance = Mathf.Max(1f, minimum);
		MaxDistance = Mathf.Max(MinDistance, maximum);
		TargetDistance = Mathf.Clamp(TargetDistance, MinDistance, MaxDistance);
		Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
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
		Distance = Damp(Distance, TargetDistance, 6f, dt);

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
