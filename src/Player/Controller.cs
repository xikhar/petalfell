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
/// Traversable ledges are detected before launching a short automatic jump.
/// The controller follows a real collision-tested arc instead of being placed
/// on top of the ledge and asking the visual model to hide the teleport.
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
	// Keep the traversal probe coupled to the world's canonical terrace height.
	public const float StepHeight = Terrain.Step + 0.30f;
	public const float Terminal = -70f;

	/// <summary>How deep the water must be before you swim rather than wade.</summary>
	public const float SwimDepth = 1.25f;
	public const float SwimSpeed = 7.0f;
	public const float Buoyancy = 26f;
	public const float WaterDrag = 4.2f;

	/// <summary>
	/// Gates new manual and route-following intent without disabling the physics
	/// body. Gravity, floor snapping, buoyancy, and collision resolution continue
	/// to run while input is disabled.
	/// </summary>
	public bool InputEnabled { get; set; } = true;
	public bool Swimming { get; private set; }
	public bool Sitting { get; private set; }
	public Vector3 Facing = Vector3.Forward;

	private float _coyote;
	private float _buffer;
	private bool _autoJumping;
	private Vector3 _autoJumpFlat;
	private float _autoJumpAge;
	private Vector3 _sitFacing = Vector3.Forward;
	/// <summary>An automatic hop cannot physically last longer than this.</summary>
	private const float AutoJumpMax = 0.9f;
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

		UpdateSwimming();
		// A seated pose is a grounded interaction. If the ground becomes water,
		// release it before the normal swim step so buoyancy is never suppressed.
		if (Sitting && Swimming) EndSit();
		Vector3 wish = ReadInput();

		var vel = Velocity;
		if (Sitting)
		{
			// Keep collision/floor physics alive while removing every source of
			// horizontal travel. BeginSit already clears route and jump state.
			vel.X = 0f;
			vel.Z = 0f;
			GroundStep(ref vel, Vector3.Zero, dt);
			vel.X = 0f;
			vel.Z = 0f;
			Velocity = vel;
			MoveAndSlide();
			Facing = _sitFacing;
			return;
		}

		if (Swimming) SwimStep(ref vel, wish, dt);
		else GroundStep(ref vel, wish, dt);

		Velocity = vel;
		MoveAndSlide();

		if (!Swimming) TryAutoJump(wish, dt);

		// Collision response may remove the component pointing into a wall and
		// leave a one-frame sideways slide. That is correct for the physics body,
		// but it must not turn the visible traveller toward the ledge immediately
		// before an auto jump. Facing follows intent (or the locked jump heading),
		// falling back to resolved velocity only while coasting without input.
		Vector3 look = _autoJumping ? _autoJumpFlat : wish;
		if (look.LengthSquared() < 0.0001f)
			look = new Vector3(Velocity.X, 0, Velocity.Z);
		if (look.LengthSquared() > 0.0001f) Facing = look.Normalized();
	}

	private Vector3 ReadInput()
	{
		if (!InputEnabled || Sitting)
		{
			_buffer = 0f;
			return Vector3.Zero;
		}

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
		if (Sitting)
		{
			Route = null;
			_routeIndex = 0;
			return;
		}
		Route = route;
		_routeIndex = 0;
	}

	/// <summary>
	/// Place the controller on an authored seat and hold it facing the interaction.
	/// The caller is expected to approach the point through navigation first; the
	/// final assignment removes tiny path-arrival offsets without freezing physics.
	/// </summary>
	public void BeginSit(Vector3 position, Vector3 faceToward)
	{
		if (Swimming) return;

		Sitting = true;
		Route = null;
		_routeIndex = 0;
		_autoJumping = false;
		_autoJumpFlat = Vector3.Zero;
		_autoJumpAge = 0f;
		_buffer = 0f;
		_coyote = 0f;

		GlobalPosition = position;
		ResetPhysicsInterpolation();
		Velocity = new Vector3(0f, Mathf.Min(Velocity.Y, 0f), 0f);

		var toward = new Vector3(faceToward.X - position.X, 0f,
			faceToward.Z - position.Z);
		if (toward.LengthSquared() > 0.0001f) _sitFacing = toward.Normalized();
		else if (Facing.LengthSquared() > 0.0001f) _sitFacing = Facing.Normalized();
		Facing = _sitFacing;
	}

	/// <summary>Release the grounded interaction without changing the input gate.</summary>
	public void EndSit()
	{
		Sitting = false;
		_buffer = 0f;
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
		if (grounded && vel.Y <= 0.1f)
		{
			_autoJumping = false;
			_autoJumpFlat = Vector3.Zero;
			_autoJumpAge = 0f;
		}
		_coyote = grounded ? Coyote : Mathf.Max(0f, _coyote - dt);
		_buffer = Mathf.Max(0f, _buffer - dt);

		float accel = grounded ? Accel : AirAccel;
		var flat = new Vector3(vel.X, 0, vel.Z);

		if (_autoJumping)
		{
			_autoJumpAge += dt;

			// Two ways out, on top of landing.
			//
			// While this flag is set the traveller's heading is FORCED to the
			// stored vector and player input is discarded entirely — which is
			// correct for the third of a second an arc lasts and catastrophic if
			// the flag ever fails to clear. It did: the reset needs grounded AND
			// a downward velocity, gravity used to be applied only when NOT
			// grounded, so a body that ended up standing on the floor with upward
			// velocity had nothing to bring it down, never met the reset, and
			// walked off in a straight line ignoring the controls forever. The
			// gravity rule below is the actual repair; these two are the guarantee.
			if (_autoJumpAge > AutoJumpMax)
			{
				GD.Print("[controller] auto-jump outlived its arc and was cancelled");
				_autoJumping = false;
				_autoJumpFlat = Vector3.Zero;
			}
			// A deliberate shove in the opposite direction always wins. Whatever
			// else is wrong, the player must be able to stop.
			else if (wish.LengthSquared() > 0.0001f && _autoJumpFlat.LengthSquared() > 0.0001f &&
				wish.Normalized().Dot(_autoJumpFlat.Normalized()) < -0.1f)
			{
				_autoJumping = false;
				_autoJumpFlat = Vector3.Zero;
			}
		}

		if (_autoJumping)
		{
			// Preserve one crossing vector for the entire automatic arc. Input and
			// path-following may change their desired direction near a waypoint, but
			// steering halfway through a ledge jump produces a visibly curved hop.
			flat = _autoJumpFlat;
		}
		else if (wish.LengthSquared() > 0.0001f)
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
			_autoJumping = false;
			_autoJumpFlat = Vector3.Zero;
			_buffer = 0f;
			_coyote = 0f;
		}
		else if (!grounded || vel.Y > 0f)
		{
			// Gravity applies to anything RISING, not merely to anything airborne.
			//
			// The old test was `!grounded` alone, and it left a hole the whole
			// controller could fall through. An automatic hop sets an upward
			// velocity while IsOnFloor() is still true, and if that upward move is
			// then blocked — a bridge deck, a roof, a surviving course of wall, a
			// canopy grown into a doorway, all of which this world now has a great
			// deal more of than it used to — the body stays on the floor with
			// positive Y. Grounded, so no gravity; rising, so the auto-jump reset
			// never fires. Nothing in the loop could break the tie, and the
			// traveller walked in one direction with the controls dead.
			if (vel.Y > 0f && !_autoJumping && !Input.IsActionPressed("jump"))
				vel.Y -= Gravity * 1.7f * dt;
			vel.Y = Mathf.Max(Terminal, vel.Y - Gravity * dt);
		}
		else if (vel.Y < 0f) vel.Y = -2f;
	}

	private void SwimStep(ref Vector3 vel, Vector3 wish, float dt)
	{
		_autoJumping = false;
		_autoJumpFlat = Vector3.Zero;
		_autoJumpAge = 0f;
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
	/// A terrace lip should never stop a run. Probe for both clearance and a real
	/// landing surface, then launch a short physical arc high enough to clear it.
	/// Nothing changes position here: MoveAndSlide owns the entire traversal.
	/// </summary>
	private void TryAutoJump(Vector3 wish, float dt)
	{
		if (!IsOnWall() || wish.LengthSquared() < 0.0001f) return;
		if (!IsOnFloor() || _autoJumping) return;

		// Preserve the exact incoming heading. The wall normal is used only to
		// measure whether that straight line can cross the ledge and how much speed
		// it needs; it must never rotate the jump toward the platform.
		var wallNormal = GetWallNormal();
		var inward = new Vector3(-wallNormal.X, 0f, -wallNormal.Z).Normalized();
		var approach = wish.Normalized();
		if (inward.LengthSquared() < 0.5f) inward = approach;
		float crossingDot = approach.Dot(inward);
		// Grazing along a wall is not an attempt to climb it.
		if (crossingDot < 0.18f) return;

		float currentSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
		float crossingSpeed = Mathf.Max(currentSpeed, MaxSpeed * 0.72f);
		crossingSpeed = Mathf.Max(crossingSpeed, MaxSpeed * 0.50f / crossingDot);
		crossingSpeed = Mathf.Min(crossingSpeed, MaxSpeed * 1.20f);
		var crossingVelocity = approach * crossingSpeed;

		var motion = crossingVelocity.Normalized() * MaxSpeed * dt * 1.6f;
		motion.Y = 0f;

		// Half-block probes find both one-block details and the canonical two-block
		// terrace without guessing the obstacle height from a wall normal.
		for (float lift = 0.5f; lift <= StepHeight + 0.01f; lift += 0.5f)
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
			if (rise <= 0.05f || rise > StepHeight + 0.05f) continue;

			// Solve v² = 2gh for an apex slightly above the landing. Auto jumps
			// ignore manual short-hop input while rising, otherwise the absence of a
			// held jump key would cut the arc before it cleared the ledge.
			float apex = rise + 0.38f;
			float launchY = MathF.Sqrt(2f * Gravity * apex);
			_autoJumpFlat = crossingVelocity;
			Velocity = new Vector3(_autoJumpFlat.X, launchY, _autoJumpFlat.Z);
			_autoJumping = true;
			_autoJumpAge = 0f;
			_coyote = 0f;
			_buffer = 0f;
			return;
		}
	}
}
