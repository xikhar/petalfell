using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;
using Petalfell.Items;
using Petalfell.World;

namespace Petalfell.Player;

/// <summary>
/// The dog.
///
/// A companion rather than a follower. Left alone it mills about wherever you
/// happen to be — trotting a few paces, stopping to sniff, sitting down and
/// watching you — and it only actually *travels* when you get far enough away
/// that staying put would look like it had forgotten about you.
///
/// It has no physics body. It rides the terrain heightfield directly: there is
/// nothing to fall through and nothing to collide with. A dog does not need
/// gravity, and giving it a Controller would mean a second body to depenetrate,
/// tune and debug for an animal whose whole job is to look happy near you.
/// </summary>
public partial class Dog : Node3D
{
	/// <summary>World units per dog voxel. A dog reads as roughly a third of a person's height.</summary>
	private const float S = 0.205f;

	// Distances in blocks. Leash is the one that matters: inside it the dog
	// potters, outside it the dog comes to you.
	private const float Leash = 9.0f;
	private const float Heel = 3.0f;
	private const float WanderR = 6.5f;
	private const float Warp = 72f;
	/// <summary>Hard minimum gap, enforced as a positional clamp rather than a steering force.</summary>
	private const float Personal = 1.15f;
	private const float Speed = 11.0f;

	/// <summary>
	/// An authored colour plus its ink class. The linear value is what the
	/// shader gets; the pale/dark decision is made on the original sRGB, where
	/// the 0.61 threshold was tuned. Keeping the pair together stops the two
	/// from being derived in different spaces.
	/// </summary>
	private readonly struct Tone
	{
		public readonly Color Linear;
		public readonly bool Pale;

		public Tone(uint hex)
		{
			var s = new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
			Linear = s.SrgbToLinear();
			Pale = s.R * 0.2126f + s.G * 0.7152f + s.B * 0.0722f >= Palette.LightFaceLuma;
		}
	}

	private static readonly Tone Coat = new(0xe8d9c8);
	private static readonly Tone CoatDeep = new(0xd6c2ae);
	private static readonly Tone Muzzle = new(0xf6ecdf);
	private static readonly Tone Nose = new(0x7a5c60);
	private static readonly Tone Collar = new(0xefb173);


	private Node3D _body, _head, _tail, _legFL, _legFR, _legBL, _legBR, _mouth;
	private Navigation _nav;
	private Node3D _player;
	private Rng _rng;

	private Vector3 _vel;
	private Vector3 _goal;
	private float _phase;
	private float _think;
	private float _sit;
	private bool _sitting;
	private bool _jumping;
	private Vector3 _jumpStart, _jumpEnd;
	private float _jumpClock, _jumpDuration, _jumpArc;
	private List<Vector3> _route;
	private int _routeIndex;
	private ShaderMaterial _inkLight, _inkDark;
	private WorldItem _fetchTarget;
	private bool _carryingFetch;
	private bool _campfireSitRequested;
	private bool _campfireSeated;
	private Vector3 _campfirePosition;
	private Vector3 _campfireSeat;

	public void Setup(Navigation nav, Node3D player, ShaderMaterial inkLight, ShaderMaterial inkDark, int seed)
	{
		// The dog owns render-frame movement and its own jump interpolation.
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		_nav = nav;
		_player = player;
		_inkLight = inkLight;
		_inkDark = inkDark;
		_rng = new Rng(seed ^ 0xD06);

		_body = new Node3D();
		AddChild(_body);

		// Forward is +Z for the dog. The old dimensions put the long side across
		// X, making the torso look rotated ninety degrees relative to its head,
		// legs and movement direction.
		Box(_body, 2.2f, 2.3f, 4.2f, Coat, new Vector3(0, S * 2.6f, 0));
		Box(_body, 2.4f, 0.5f, 1.6f, Collar, new Vector3(0, S * 2.7f, S * 1.5f), outlined: false);

		_head = Pivot(_body, new Vector3(0, S * 3.4f, S * 2.0f));
		Box(_head, 2.0f, 1.9f, 1.9f, Coat, new Vector3(0, S * 0.5f, S * 0.3f));
		Box(_head, 1.1f, 0.9f, 1.0f, Muzzle, new Vector3(0, S * 0.15f, S * 1.4f), outlined: false);
		Box(_head, 0.4f, 0.35f, 0.3f, Nose, new Vector3(0, S * 0.3f, S * 1.95f), outlined: false);
		_mouth = Pivot(_head, new Vector3(0f, S * 0.02f, S * 1.72f));
		// Sit the ears on the head instead of embedding their lower halves in it.
		Box(_head, 0.5f, 0.7f, 0.6f, CoatDeep, new Vector3(-S * 0.75f, S * 1.8f, -S * 0.1f), outlined: false);
		Box(_head, 0.5f, 0.7f, 0.6f, CoatDeep, new Vector3(S * 0.75f, S * 1.8f, -S * 0.1f), outlined: false);

		_legFL = Pivot(_body, new Vector3(-S * 0.9f, S * 1.7f, S * 1.3f));
		Box(_legFL, 0.7f, 1.8f, 0.7f, CoatDeep, new Vector3(0, -S * 0.9f, 0));
		_legFR = Pivot(_body, new Vector3(S * 0.9f, S * 1.7f, S * 1.3f));
		Box(_legFR, 0.7f, 1.8f, 0.7f, CoatDeep, new Vector3(0, -S * 0.9f, 0));
		_legBL = Pivot(_body, new Vector3(-S * 0.9f, S * 1.7f, -S * 1.3f));
		Box(_legBL, 0.7f, 1.8f, 0.7f, CoatDeep, new Vector3(0, -S * 0.9f, 0));
		_legBR = Pivot(_body, new Vector3(S * 0.9f, S * 1.7f, -S * 1.3f));
		Box(_legBR, 0.7f, 1.8f, 0.7f, CoatDeep, new Vector3(0, -S * 0.9f, 0));

		_tail = Pivot(_body, new Vector3(0, S * 3.2f, -S * 2.0f));
		Box(_tail, 0.5f, 1.5f, 0.5f, Coat, new Vector3(0, S * 0.6f, 0));
	}

	/// <summary>Command the dog to retrieve one physical stick from the world.</summary>
	public bool Fetch(WorldItem item)
	{
		if (item == null || !GodotObject.IsInstanceValid(item) || !item.CanPickUp ||
			item.Item != ItemCatalog.Stick)
			return false;
		EndCampfireSit();
		_fetchTarget = item;
		_carryingFetch = false;
		_sitting = false;
		_sit = 0f;
		_route = null;
		_think = 0f;
		return true;
	}

	/// <summary>
	/// Ask the dog to settle across the fire from the player. The normal route and
	/// ledge-jump machinery owns the approach; only arrival turns into a persistent
	/// sit, so the command never teleports the dog across terrain.
	/// </summary>
	public void BeginCampfireSit(Vector3 firePosition, Vector3 playerPosition)
	{
		// Fetch and camp sitting are mutually exclusive commands. A carried object
		// is put down safely instead of being stranded under the dog's mouth node.
		if (_fetchTarget != null && GodotObject.IsInstanceValid(_fetchTarget) &&
			!_fetchTarget.IsQueuedForDeletion() && _carryingFetch)
		{
			var drop = GlobalPosition;
			if (_nav != null) drop.Y = GroundAt(drop.X, drop.Z);
			_fetchTarget.Drop(drop);
		}
		_fetchTarget = null;
		_carryingFetch = false;

		_campfirePosition = firePosition;
		var playerSide = new Vector3(playerPosition.X - firePosition.X, 0f,
			playerPosition.Z - firePosition.Z);
		if (playerSide.LengthSquared() < 0.01f)
			playerSide = new Vector3(GlobalPosition.X - firePosition.X, 0f,
				GlobalPosition.Z - firePosition.Z);
		if (playerSide.LengthSquared() < 0.01f) playerSide = Vector3.Back;

		// A little over two blocks leaves the flame readable between both figures
		// while remaining comfortably outside the player's personal-space clamp.
		var opposite = -playerSide.Normalized();
		_campfireSeat = firePosition + opposite * 2.15f;
		_campfireSeat.Y = _nav != null
			? GroundAt(_campfireSeat.X, _campfireSeat.Z)
			: GlobalPosition.Y;

		_campfireSitRequested = true;
		_campfireSeated = false;
		_sitting = false;
		_sit = 0f;
		_goal = _campfireSeat;
		_route = null;
		_routeIndex = 0;
		_think = 0f;
		if (_nav != null) PlanRoute(GlobalPosition, _campfireSeat);
	}

	/// <summary>Release a campfire command and return control to ordinary dog AI.</summary>
	public void EndCampfireSit()
	{
		_campfireSitRequested = false;
		_campfireSeated = false;
		_sitting = false;
		_sit = 0f;
		_route = null;
		_routeIndex = 0;
		_goal = GlobalPosition;
		_vel = Vector3.Zero;
		_think = 0f;
	}

	private Node3D Pivot(Node3D parent, Vector3 at)
	{
		var n = new Node3D { Position = at };
		parent.AddChild(n);
		return n;
	}

	/// <summary>See Character.Box — the same silhouette-only rule applies.</summary>
	private void Box(Node3D parent, float w, float h, float d, Tone tone, Vector3 at,
		bool outlined = true)
	{
		float wx = w * S, wy = h * S, wz = d * S;
		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(wx, wy, wz) },
			Position = at,
		};
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/character.gdshader") };
		mat.SetShaderParameter("albedo", tone.Linear);
		mat.SetShaderParameter("sun_dir", Palette.SunDir);
		mesh.MaterialOverride = mat;
		parent.AddChild(mesh);

		if (!outlined) return;

		var inkMesh = InkBuilder.Box(wx, wy, wz, tone.Pale);
		var ink = new MeshInstance3D
		{
			Mesh = inkMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			CustomAabb = new Aabb(new Vector3(-wx, -wy, -wz), new Vector3(wx * 2, wy * 2, wz * 2)),
		};
		ink.SetSurfaceOverrideMaterial(0, tone.Pale ? _inkLight : _inkDark);
		mesh.AddChild(ink);
	}

	public override void _Process(double delta)
	{
		if (_player == null) return;
		float dt = (float)delta;

		var me = GlobalPosition;
		var you = _player.GlobalPosition;
		float away = new Vector2(you.X - me.X, you.Z - me.Z).Length();

		// Hopelessly far, or stuck the far side of something: reappear rather
		// than trail forlornly across the map.
		if (away > Warp)
		{
			var warp = you + new Vector3(_rng.Bell() * 2f, 0, _rng.Bell() * 2f);
			warp.Y = GroundAt(warp.X, warp.Z);
			GlobalPosition = warp;
			_vel = Vector3.Zero;
			_jumping = false;
			_route = null;
			return;
		}

		if (_jumping)
		{
			AdvanceJump(dt);
			Animate(dt);
			return;
		}

		bool campfireCommand = _campfireSitRequested;
		bool fetching = false;
		Vector3 fetchDestination = me;
		if (campfireCommand)
		{
			if (_route != null) AdvanceRouteTarget(me);
			UpdateCampfireCommand(me);
			if (_campfireSeated)
			{
				Animate(dt);
				return;
			}
		}
		else
		{
			fetching = UpdateFetchCommand(me, you, out fetchDestination);
			if (_route != null) AdvanceRouteTarget(me);
			else if (fetching)
			{
				_goal = fetchDestination;
				_sitting = false;
			}
			else
			{
				_think -= dt;
				if (_think <= 0f) Decide(you, away);
			}
		}

		Vector3 wish = Vector3.Zero;
		if (!_sitting)
		{
			var d = new Vector3(_goal.X - me.X, 0, _goal.Z - me.Z);
			if (d.LengthSquared() > 0.6f) wish = d.Normalized();
		}

		// Outside the leash the dog commits and comes to you; inside, it ambles.
		float pace = campfireCommand ? 0.68f
			: (fetching ? 0.88f : (away > Leash ? 1f : 0.42f));
		_vel = _vel.Lerp(wish * Speed * pace, 1f - Mathf.Exp(-9f * dt));

		var next = me + _vel * dt;

		// A soft push loses to a route that wants to go straight through you,
		// so the gap is a positional clamp instead.
		var gap = new Vector3(next.X - you.X, 0, next.Z - you.Z);
		if (gap.Length() < Personal && gap.LengthSquared() > 0.0001f)
			next = you + gap.Normalized() * Personal;

		float hereGround = GroundAt(me.X, me.Z);
		float nextGround = GroundAt(next.X, next.Z);
		if (FindBoundaryAhead(me, _vel, hereGround,
			out float boundaryGround, out float boundaryDistance))
		{
			float boundaryRise = boundaryGround - hereGround;
			if (Mathf.Abs(boundaryRise) <= Controller.StepHeight + 0.10f &&
				TryStartJump(me, _vel, hereGround, boundaryGround, boundaryDistance))
			{
				AdvanceJump(dt);
				Animate(dt);
				return;
			}

			// A rejected jump is an impassable boundary, not permission for the
			// ordinary height lerp to pull the dog through it. Stop on this side and
			// ask the shared navigation graph for a legal route around.
			_vel = Vector3.Zero;
			Vector3 routeTarget = campfireCommand ? _campfireSeat
				: (fetching ? fetchDestination : (away > Leash ? you : _goal));
			PlanRoute(me, routeTarget);
			GlobalPosition = new Vector3(me.X, hereGround, me.Z);
			Animate(dt);
			return;
		}

		next.Y = Mathf.Lerp(me.Y, nextGround, 1f - Mathf.Exp(-16f * dt));
		GlobalPosition = next;

		Animate(dt);
	}

	private void UpdateCampfireCommand(Vector3 me)
	{
		if (!_campfireSitRequested) return;
		_sitting = false;

		var remaining = new Vector2(_campfireSeat.X - me.X, _campfireSeat.Z - me.Z);
		if (remaining.LengthSquared() <= 0.48f)
		{
			// The route gets the dog here; this small final settle makes the authored
			// composition stable and keeps its gaze centred on the flame.
			GlobalPosition = _campfireSeat;
			_vel = Vector3.Zero;
			_goal = _campfireSeat;
			_route = null;
			_routeIndex = 0;
			_campfireSeated = true;
			_sitting = true;
			return;
		}

		// A completed navigation route may end just short of the authored seat.
		// Cover that final, level gap directly instead of falling back to wander AI.
		if (_route == null) _goal = _campfireSeat;
	}

	private bool UpdateFetchCommand(Vector3 me, Vector3 you, out Vector3 destination)
	{
		destination = me;
		if (_fetchTarget == null || !GodotObject.IsInstanceValid(_fetchTarget) ||
			_fetchTarget.IsQueuedForDeletion())
		{
			_fetchTarget = null;
			_carryingFetch = false;
			return false;
		}

		if (!_carryingFetch)
		{
			destination = _fetchTarget.GlobalPosition;
			float distance = new Vector2(destination.X - me.X, destination.Z - me.Z).Length();
			if (distance > 0.92f) return true;

			if (!_fetchTarget.BeginCarry(_mouth))
			{
				_fetchTarget = null;
				return false;
			}
			_carryingFetch = true;
			_route = null;
			destination = you;
			return true;
		}

		destination = you;
		float homeDistance = new Vector2(you.X - me.X, you.Z - me.Z).Length();
		if (homeDistance > 1.75f) return true;

		var awayFromDog = new Vector3(you.X - me.X, 0f, you.Z - me.Z);
		if (awayFromDog.LengthSquared() < 0.01f) awayFromDog = Vector3.Right;
		var drop = you + awayFromDog.Normalized() * 1.15f;
		drop.Y = GroundAt(drop.X, drop.Z);
		_fetchTarget.Drop(drop);
		_fetchTarget = null;
		_carryingFetch = false;
		_route = null;
		_goal = me;
		_vel = Vector3.Zero;
		_think = 0.45f;
		return false;
	}

	private void Decide(Vector3 you, float away)
	{
		_think = _rng.Range(0.6f, 2.2f);

		if (away > Leash)
		{
			// Choose a natural nearby position rather than the player's exact
			// spot: a pet standing inside you is not a pet.
			float ang = _rng.Next() * Mathf.Tau;
			float r = _rng.Range(Heel * 0.6f, Heel);
			_goal = you + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
			_sitting = false;
			_sit = 0f;
			return;
		}

		if (_sitting)
		{
			_sit -= _think;
			if (_sit <= 0f) _sitting = false;
			return;
		}

		if (_rng.Chance(0.22f))
		{
			_sitting = true;
			_sit = _rng.Range(1.2f, 3.4f);
			return;
		}

		float a = _rng.Next() * Mathf.Tau;
		float rad = _rng.Range(1.5f, WanderR);
		_goal = you + new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad);
	}

	private void PlanRoute(Vector3 from, Vector3 destination)
	{
		_route = _nav.FindPath(from, destination);
		_routeIndex = 0;
		if (_route == null || _route.Count == 0)
		{
			_route = null;
			_goal = from;
			_think = 0.35f;
			return;
		}
		AdvanceRouteTarget(from);
	}

	private void AdvanceRouteTarget(Vector3 from)
	{
		while (_route != null && _routeIndex < _route.Count)
		{
			var point = _route[_routeIndex];
			var delta = new Vector2(point.X - from.X, point.Z - from.Z);
			if (delta.LengthSquared() >= 0.72f) break;
			_routeIndex++;
		}

		if (_route == null || _routeIndex >= _route.Count)
		{
			_route = null;
			_think = 0f;
			return;
		}
		_goal = _route[_routeIndex];
	}

	private float GroundAt(float x, float z)
	{
		float g = _nav.GroundY(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
		// Paddling rather than walking the bed: the surface is where the scene is.
		return Mathf.Max(g, Palette.WaterLevel - 0.55f) == g ? g : Palette.WaterLevel - 0.35f;
	}

	private bool FindBoundaryAhead(Vector3 from, Vector3 velocity, float hereGround,
		out float boundaryGround, out float boundaryDistance)
	{
		boundaryGround = hereGround;
		boundaryDistance = 0f;
		var direction = new Vector3(velocity.X, 0f, velocity.Z).Normalized();
		if (direction.LengthSquared() < 0.5f) return false;

		// Begin looking beyond the next movement step. At the dog's running speed,
		// a one-frame test discovers the shelf only after its nose is already in it.
		for (float distance = 0.22f; distance <= 0.95f; distance += 0.09f)
		{
			var sample = from + direction * distance;
			float ground = GroundAt(sample.X, sample.Z);
			if (Mathf.Abs(ground - hereGround) < 0.20f) continue;
			boundaryGround = ground;
			boundaryDistance = distance;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Detect a one- or two-block boundary and choose a landing point beyond its
	/// face. The regular ground lerp is never allowed to absorb this height
	/// change: once accepted, the whole crossing belongs to the jump arc.
	/// </summary>
	private bool TryStartJump(Vector3 from, Vector3 velocity,
		float hereGround, float nextGround, float boundaryDistance)
	{
		float initialRise = nextGround - hereGround;
		if (Mathf.Abs(initialRise) < 0.20f ||
			Mathf.Abs(initialRise) > Controller.StepHeight + 0.10f)
			return false;

		var direction = new Vector3(velocity.X, 0f, velocity.Z).Normalized();
		if (direction.LengthSquared() < 0.5f) return false;

		// Move far enough beyond the cell boundary that the dog's torso lands on
		// the new shelf rather than balancing on its front edge.
		Vector3 landing = from;
		float targetGround = nextGround;
		bool found = false;
		float landingStart = boundaryDistance + 0.68f;
		for (float distance = landingStart; distance <= landingStart + 1.35f; distance += 0.15f)
		{
			var candidate = from + direction * distance;
			float ground = GroundAt(candidate.X, candidate.Z);
			if (Mathf.Abs(ground - nextGround) > 0.15f) continue;
			landing = candidate;
			targetGround = ground;
			found = true;
			break;
		}
		if (!found) return false;

		_jumpStart = from;
		_jumpStart.Y = hereGround;
		_jumpEnd = new Vector3(landing.X, targetGround, landing.Z);
		float distanceFlat = new Vector2(_jumpEnd.X - _jumpStart.X,
			_jumpEnd.Z - _jumpStart.Z).Length();
		_jumpDuration = Mathf.Clamp(distanceFlat / (Speed * 0.46f), 0.36f, 0.54f);
		_jumpArc = 0.62f + Mathf.Abs(targetGround - hereGround) * 0.62f;
		_jumpClock = 0f;
		_jumping = true;
		_sitting = false;
		_vel = (_jumpEnd - _jumpStart) / _jumpDuration;
		return true;
	}

	private void AdvanceJump(float dt)
	{
		_jumpClock = Mathf.Min(_jumpClock + dt, _jumpDuration);
		float t = _jumpClock / Mathf.Max(_jumpDuration, 0.001f);
		var position = _jumpStart.Lerp(_jumpEnd, t);
		position.Y += Mathf.Sin(t * Mathf.Pi) * _jumpArc;
		GlobalPosition = position;

		if (t < 1f) return;
		GlobalPosition = _jumpEnd;
		_jumping = false;
	}

	private void Animate(float dt)
	{
		float speed = new Vector2(_vel.X, _vel.Z).Length();
		float cadence = Mathf.Clamp(speed / Speed, 0f, 1f);
		_phase += dt * (6f + cadence * 14f) * (cadence > 0.03f ? 1f : 0.15f);

		if (_campfireSeated)
		{
			var toward = new Vector3(_campfirePosition.X - GlobalPosition.X, 0f,
				_campfirePosition.Z - GlobalPosition.Z);
			if (toward.LengthSquared() > 0.0001f)
			{
				float yaw = Mathf.Atan2(toward.X, toward.Z);
				Rotation = new Vector3(0,
					Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-9f * dt)), 0);
			}
		}
		else if (speed > 0.6f)
		{
			float yaw = Mathf.Atan2(_vel.X, _vel.Z);
			Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-11f * dt)), 0);
		}

		float swing = Mathf.Sin(_phase) * cadence * 0.95f;
		float counter = Mathf.Sin(_phase + Mathf.Pi) * cadence * 0.95f;

		if (_jumping)
		{
			// Compact readable tuck, held for the arc rather than blended through a
			// walking cycle while the dog is airborne.
			_body.Rotation = new Vector3(-0.08f, 0, 0);
			_legFL.Rotation = new Vector3(-0.72f, 0, 0);
			_legFR.Rotation = new Vector3(-0.72f, 0, 0);
			_legBL.Rotation = new Vector3(0.78f, 0, 0);
			_legBR.Rotation = new Vector3(0.78f, 0, 0);
		}
		else if (_sitting)
		{
			_body.Rotation = new Vector3(Mathf.Lerp(_body.Rotation.X, -0.42f, 1f - Mathf.Exp(-8f * dt)), 0, 0);
			_legBL.Rotation = new Vector3(1.15f, 0, 0);
			_legBR.Rotation = new Vector3(1.15f, 0, 0);
			_legFL.Rotation = new Vector3(0, 0, 0);
			_legFR.Rotation = new Vector3(0, 0, 0);
		}
		else
		{
			_body.Rotation = new Vector3(Mathf.Lerp(_body.Rotation.X, 0f, 1f - Mathf.Exp(-8f * dt)), 0, 0);
			_legFL.Rotation = new Vector3(swing, 0, 0);
			_legFR.Rotation = new Vector3(counter, 0, 0);
			_legBL.Rotation = new Vector3(counter, 0, 0);
			_legBR.Rotation = new Vector3(swing, 0, 0);
		}

		// The tail is the whole personality: fast and wide when moving, a slow
		// idle sweep when parked.
		float wag = Mathf.Sin(_phase * (2.2f + cadence * 2.5f)) * (0.35f + cadence * 0.55f);
		_tail.Rotation = new Vector3(-0.5f - cadence * 0.35f, wag, 0);
		_head.Rotation = new Vector3(Mathf.Sin(_phase * 0.4f) * 0.08f, Mathf.Sin(_phase * 0.27f) * 0.22f, 0);
		_body.Position = _jumping
			? new Vector3(0, 0.035f, 0)
			: new Vector3(0, Mathf.Abs(Mathf.Sin(_phase)) * cadence * 0.045f, 0);
	}
}
