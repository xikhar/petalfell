using System;
using Godot;
using Petalfell.Core;
using Petalfell.World;

namespace Petalfell.Player;

/// <summary>
/// Movement and collision.
///
/// Game feel targets: instant response, a little inertia, generous forgiveness.
/// Concretely — high ground acceleration with a lower air control value, coyote
/// time, jump buffering, variable jump height, and step-up so terrace lips
/// never interrupt a run.
///
/// The step split matters more than it looks. A kerb is not a jump: launching a
/// ballistic arc over every one-block lip makes ordinary walking bouncy and
/// turns a gentle beach into a staircase of hops. Below StepSmall the body is
/// simply placed and the drawn position eases up to meet it, which is what
/// taking a small step actually looks like.
/// </summary>
public partial class Controller : CharacterBody3D
{
	public const float Gravity = 58f;
	public const float MaxSpeed = 13.5f;
	public const float Accel = 118f;
	public const float AirAccel = 46f;
	public const float Friction = 14.5f;
	public const float AirDrag = 1.4f;
	public const float JumpVel = 20.5f;
	public const float Coyote = 0.12f;
	public const float Buffer = 0.14f;
	public const float StepHeight = 3.30f;
	public const float StepSmall = 1.25f;
	public const float Terminal = -70f;

	/// <summary>How deep the water must be before you swim rather than wade.</summary>
	public const float SwimDepth = 1.25f;
	public const float SwimSpeed = 7.0f;
	public const float Buoyancy = 26f;
	public const float WaterDrag = 4.2f;

	public bool Swimming { get; private set; }
	/// <summary>How far the body was placed upward this frame by a step, for the drawn figure to ease out.</summary>
	public float StepLift { get; private set; }
	public Vector3 Facing = Vector3.Forward;

	private float _coyote;
	private float _buffer;
	private Terrain _terrain;

	/// <summary>World-space destination from click-to-move, or null.</summary>
	public System.Collections.Generic.List<Vector3> Route;
	private int _routeIndex;

	public void Setup(Terrain terrain)
	{
		_terrain = terrain;
		// The traveller is a slim capsule: wide enough not to slip through a
		// diagonal gap between trunks, narrow enough to walk a stair cut three
		// columns wide.
		var shape = new CollisionShape3D
		{
			Shape = new CapsuleShape3D { Radius = 0.38f, Height = 1.75f },
			Position = new Vector3(0, 0.875f, 0),
		};
		AddChild(shape);
		FloorMaxAngle = Mathf.DegToRad(50f);
		FloorSnapLength = 0.6f;
		UpDirection = Vector3.Up;
		MotionMode = MotionModeEnum.Grounded;
		SlideOnCeiling = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		StepLift = 0f;

		Vector3 wish = ReadInput();
		UpdateSwimming();

		var vel = Velocity;

		if (Swimming) SwimStep(ref vel, wish, dt);
		else GroundStep(ref vel, wish, dt);

		Velocity = vel;
		var before = GlobalPosition;
		MoveAndSlide();

		if (!Swimming) TryStepUp(before, wish, dt);

		var flat = new Vector3(Velocity.X, 0, Velocity.Z);
		if (flat.LengthSquared() > 0.35f) Facing = flat.Normalized();
	}

	private Vector3 ReadInput()
	{
		var wish = Vector3.Zero;
		float f = Input.GetActionStrength("move_forward") - Input.GetActionStrength("move_back");
		float r = Input.GetActionStrength("move_right") - Input.GetActionStrength("move_left");

		if (f != 0f || r != 0f)
		{
			// Movement is camera-relative: the world can be orbited in 45-degree
			// steps and "forward" has to keep meaning "away from the viewer".
			var cam = GetViewport().GetCamera3D();
			if (cam != null)
			{
				// A camera looks down its own -Z, so the basis Z column points
				// back at the viewer. Building "right" from that column without
				// negating it first yields left, and A/D come out swapped.
				var back = cam.GlobalTransform.Basis.Z;
				var fwd = new Vector3(-back.X, 0, -back.Z).Normalized();
				var right = fwd.Cross(Vector3.Up);
				wish = fwd * f + right * r;
			}
			else wish = new Vector3(r, 0, -f);

			if (wish.LengthSquared() > 1f) wish = wish.Normalized();
			Route = null;   // any movement input cancels a click destination
		}
		else if (Route != null)
		{
			wish = FollowRoute();
		}

		if (Input.IsActionJustPressed("jump")) _buffer = Buffer;
		return wish;
	}

	private Vector3 FollowRoute()
	{
		if (Route == null || _routeIndex >= Route.Count) { Route = null; return Vector3.Zero; }
		var target = Route[_routeIndex];
		var here = GlobalPosition;
		var d = new Vector3(target.X - here.X, 0, target.Z - here.Z);
		if (d.LengthSquared() < 1.1f)
		{
			_routeIndex++;
			if (_routeIndex >= Route.Count) { Route = null; return Vector3.Zero; }
			return FollowRoute();
		}
		return d.Normalized();
	}

	public void SetRoute(System.Collections.Generic.List<Vector3> route)
	{
		Route = route;
		_routeIndex = 0;
	}

	private void UpdateSwimming()
	{
		// Keyed on the depth of the bed beneath you, never on your own height in
		// the water. Testing your own Y is a feedback loop: buoyancy lifts you
		// past the threshold, you stop swimming, gravity drops you back under.
		int x = Mathf.FloorToInt(GlobalPosition.X);
		int z = Mathf.FloorToInt(GlobalPosition.Z);
		float bed = _terrain != null && x >= 0 && z >= 0 && x < _terrain.Size && z < _terrain.Size
			? _terrain.Level[z * _terrain.Size + x] : 0f;
		float depth = Palette.WaterLevel - bed;
		Swimming = depth > SwimDepth && GlobalPosition.Y < Palette.WaterLevel + 0.35f;
	}

	private void GroundStep(ref Vector3 vel, Vector3 wish, float dt)
	{
		bool grounded = IsOnFloor();
		_coyote = grounded ? Coyote : Mathf.Max(0f, _coyote - dt);
		_buffer = Mathf.Max(0f, _buffer - dt);

		float accel = grounded ? Accel : AirAccel;
		var flat = new Vector3(vel.X, 0, vel.Z);

		if (wish.LengthSquared() > 0.0001f)
		{
			flat += wish * accel * dt;
			if (flat.Length() > MaxSpeed) flat = flat.Normalized() * MaxSpeed;
		}
		else
		{
			float drag = grounded ? Friction : AirDrag;
			float speed = flat.Length();
			float drop = speed * drag * dt;
			flat = speed > drop ? flat * ((speed - drop) / speed) : Vector3.Zero;
		}

		vel.X = flat.X;
		vel.Z = flat.Z;

		if (_buffer > 0f && _coyote > 0f)
		{
			vel.Y = JumpVel;
			_buffer = 0f;
			_coyote = 0f;
		}
		else if (!grounded)
		{
			// Variable jump height: releasing early cuts the arc short.
			if (vel.Y > 0f && !Input.IsActionPressed("jump")) vel.Y -= Gravity * 1.7f * dt;
			vel.Y = Mathf.Max(Terminal, vel.Y - Gravity * dt);
		}
		else if (vel.Y < 0f) vel.Y = -2f;
	}

	private void SwimStep(ref Vector3 vel, Vector3 wish, float dt)
	{
		// The traveller floats rather than sinks: water deeper than a stride is
		// common, and the surface is where the scene is.
		float target = Palette.WaterLevel - 0.85f;
		float error = target - GlobalPosition.Y;
		vel.Y += Mathf.Clamp(error, -1f, 1f) * Buoyancy * dt;
		vel.Y -= vel.Y * WaterDrag * dt;

		var flat = new Vector3(vel.X, 0, vel.Z);
		if (wish.LengthSquared() > 0.0001f)
		{
			flat += wish * SwimSpeed * 3.2f * dt;
			if (flat.Length() > SwimSpeed) flat = flat.Normalized() * SwimSpeed;
		}
		else flat -= flat * WaterDrag * dt;

		vel.X = flat.X;
		vel.Z = flat.Z;

		// Climbing out is a swim toward the bank plus a small assist, not a jump.
		if (Input.IsActionPressed("jump")) vel.Y = Mathf.Max(vel.Y, 6.5f);
	}

	/// <summary>
	/// A terrace lip should never stop a run. If the body is against a wall and
	/// there is clear space one step up, place it there rather than letting the
	/// slide solver grind along the face.
	/// </summary>
	private void TryStepUp(Vector3 before, Vector3 wish, float dt)
	{
		if (!IsOnWall() || wish.LengthSquared() < 0.0001f) return;
		if (!IsOnFloor() && Velocity.Y > 0.1f) return;

		var motion = wish * MaxSpeed * dt * 1.6f;
		motion.Y = 0f;

		for (float lift = 0.6f; lift <= StepHeight + 0.01f; lift += 0.6f)
		{
			var probe = GlobalTransform;
			probe.Origin += new Vector3(0, lift, 0);
			if (TestMove(probe, motion)) continue;

			probe.Origin += motion;
			// Settle back onto whatever is under the new footing.
			var drop = new Vector3(0, -(lift + 0.35f), 0);
			var result = new KinematicCollision3D();
			if (TestMove(probe, drop, result))
				probe.Origin += drop * result.GetTravel().Length() / Mathf.Max(drop.Length(), 0.001f);

			float rise = probe.Origin.Y - GlobalPosition.Y;
			if (rise > StepHeight + 0.05f) return;

			GlobalPosition = probe.Origin;
			// Small rises are placed, not jumped, and the drawn figure eases up
			// to meet the body over the next few frames.
			if (rise > 0.05f) StepLift = rise;
			return;
		}
	}
}
