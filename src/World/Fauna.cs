using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

public enum Species : byte { Deer, Rabbit, Goat, Bird, Butterfly, Fish }

/// <summary>
/// Ambient wildlife.
///
/// Streamed against the player the way chunks are, and for the same reason: a
/// world this size cannot afford to simulate creatures nobody is looking at,
/// and it does not need to — an animal the player has never seen has no history
/// worth preserving. A fixed population is kept alive inside a ring around the
/// traveller, culled when they walk away and re-seeded ahead of them.
///
/// Which animal appears is decided by the ground it would be standing on, so
/// fauna reinforces the provinces rather than floating free of them: deer in
/// the woods, rabbits in the meadows, goats on the high stone, fish in the
/// water. That is the whole design brief — a creature is one more way to tell
/// where you are without being told.
///
/// Every body is the same four-box plan at different proportions rather than
/// six hand-built models. At this camera distance a creature is thirty pixels
/// tall, and its silhouette and gait carry all of the recognition; modelling
/// each one separately would be a great deal of code spent below the threshold
/// where anyone could tell.
/// </summary>
public partial class Fauna : Node3D
{
	private const int Population = 16;
	private const float SpawnNear = 26f;
	private const float SpawnFar = 74f;
	private const float Cull = 96f;

	private readonly List<Critter> _live = new();
	private Terrain _terrain;
	private ShaderMaterial _inkLight, _inkDark;
	private Rng _rng;
	private double _retry;

	public void Setup(Terrain terrain, ShaderMaterial inkLight, ShaderMaterial inkDark, int seed)
	{
		_terrain = terrain;
		_inkLight = inkLight;
		_inkDark = inkDark;
		_rng = new Rng(seed ^ 0x5EA7);
	}

	public int LiveCount => _live.Count;

	public void Advance(Vector3 player, double delta)
	{
		if (_terrain == null) return;

		for (int i = _live.Count - 1; i >= 0; i--)
		{
			var c = _live[i];
			if (!IsInstanceValid(c)) { _live.RemoveAt(i); continue; }
			float d = new Vector2(c.GlobalPosition.X - player.X, c.GlobalPosition.Z - player.Z).Length();
			if (d > Cull)
			{
				_live.RemoveAt(i);
				c.QueueFree();
				continue;
			}
			c.Advance(player, delta);
		}

		// Spawning is rate limited, but not to one at a time. Crossing a shoreline
		// can invalidate half the population at once, and at one animal per
		// quarter second the meadow the player is walking into stays empty for the
		// four seconds they are looking at it.
		_retry -= delta;
		if (_live.Count >= Population || _retry > 0.0) return;
		_retry = 0.12;
		for (int i = 0; i < 3 && _live.Count < Population; i++) TrySpawn(player);
	}

	private void TrySpawn(Vector3 player)
	{
		int S = _terrain.Size;
		for (int attempt = 0; attempt < 12; attempt++)
		{
			float ang = _rng.Next() * MathF.Tau;
			float rad = _rng.Range(SpawnNear, SpawnFar);
			int x = (int)(player.X + MathF.Cos(ang) * rad);
			int z = (int)(player.Z + MathF.Sin(ang) * rad);
			if (x < 4 || z < 4 || x >= S - 4 || z >= S - 4) continue;

			int i = z * S + x;
			bool water = _terrain.Land[i] == 0;
			int level = _terrain.Level[i];
			var biome = _terrain.Plan.RegionAt(x, z).Biome;

			var species = Choose(biome, water, _terrain.Roads != null && _terrain.Roads.Clear[i] != 0);
			if (species == null) continue;

			// Land animals will not stand on a road, a stair or a cliff lip.
			if (!water)
			{
				if (_terrain.StairMask[i] != 0) continue;
				if (TerrainShape.DropBelow(_terrain.Level, S, x, z) > Terrain.Step) continue;
				if (_terrain.Grid.Heights[i] > level) continue;
			}

			var kind = species.Value;
			float y = kind == Species.Fish
				? Palette.WaterLevel - _rng.Range(0.6f, 1.6f)
				: kind is Species.Bird or Species.Butterfly
					? level + _rng.Range(kind == Species.Bird ? 6f : 1.2f, kind == Species.Bird ? 13f : 2.6f)
					: level;

			var critter = new Critter();
			AddChild(critter);
			critter.Position = new Vector3(x + 0.5f, y, z + 0.5f);
			critter.Setup(kind, _terrain, _inkLight, _inkDark,
				unchecked((int)(_rng.Next() * int.MaxValue)));
			_live.Add(critter);
			return;
		}
	}

	private Species? Choose(Biome biome, bool water, bool nearRoad)
	{
		if (water) return _rng.Chance(0.75f) ? Species.Fish : null;
		// Creatures give a road a wide berth. It is where people are.
		if (nearRoad && _rng.Chance(0.7f)) return null;

		return biome switch
		{
			Biome.Forest => _rng.Chance(0.55f) ? Species.Deer
				: _rng.Chance(0.6f) ? Species.Bird : Species.Rabbit,
			Biome.Meadow => _rng.Chance(0.4f) ? Species.Rabbit
				: _rng.Chance(0.5f) ? Species.Butterfly : Species.Deer,
			Biome.Sakura => _rng.Chance(0.6f) ? Species.Butterfly : Species.Bird,
			Biome.Plains => _rng.Chance(0.5f) ? Species.Rabbit : Species.Bird,
			Biome.Highland => _rng.Chance(0.6f) ? Species.Goat : Species.Bird,
			Biome.SnowyHills => _rng.Chance(0.7f) ? Species.Goat : null,
			Biome.Shore => _rng.Chance(0.7f) ? Species.Bird : null,
			Biome.Wetland => _rng.Chance(0.5f) ? Species.Bird : Species.Butterfly,
			_ => Species.Bird,
		};
	}
}

/// <summary>One animal: a shared body plan, its own proportions, its own gait.</summary>
public partial class Critter : Node3D
{
	/// <summary>World units per creature voxel. Matches the traveller's scale.</summary>
	private const float S = 0.30f;

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

	private Species _kind;
	private Terrain _terrain;
	private ShaderMaterial _inkLight, _inkDark;
	private Rng _rng;

	private Node3D _body, _head;
	private readonly List<Node3D> _limbs = new();
	private float _phase;
	private float _yaw;
	private Vector3 _heading = Vector3.Forward;
	private float _speed;
	private double _decide;
	private float _startle;
	private float _spookCooldown;
	/// <summary>Vertical state. Land animals leave the ground to change terrace.</summary>
	private float _vy;
	private bool _airborne;
	private float _groundY;

	public void Setup(Species kind, Terrain terrain, ShaderMaterial inkLight,
		ShaderMaterial inkDark, int seed)
	{
		_kind = kind;
		_terrain = terrain;
		_inkLight = inkLight;
		_inkDark = inkDark;
		_rng = new Rng(seed);
		_phase = _rng.Next() * 6f;
		_yaw = _rng.Next() * Mathf.Tau;

		// Seed the ground height from the terrain, not from the first successful
		// Legal() call.
		//
		// This was a chicken and egg that dropped every land animal through the
		// floor. Legal() decides whether a square is reachable by comparing it to
		// _groundY, and _groundY was only ever assigned when Legal() succeeded —
		// so on a creature that started at zero, every test read "that ledge is
		// twenty-six blocks up, unreachable", nothing ever set the field, and
		// Vertical() saw a twenty-six block drop below it and started falling.
		// They appeared for a second and sank.
		int S = terrain.Size;
		int gx = Mathf.Clamp((int)GlobalPosition.X, 0, S - 1);
		int gz = Mathf.Clamp((int)GlobalPosition.Z, 0, S - 1);
		_groundY = terrain.Level[gz * S + gx];

		Build();
	}

	/* ----------------------------------------------------------------
	 * Bodies
	 * ---------------------------------------------------------------- */
	private void Build()
	{
		_body = new Node3D();
		AddChild(_body);

		switch (_kind)
		{
			case Species.Deer: Quadruped(new Tone(0xc59a86), new Tone(0xf1e2d6), 3.0f, 2.6f, 5.4f, 3.1f, 0.9f); break;
			case Species.Goat: Quadruped(new Tone(0xe8e2ea), new Tone(0xb9aec0), 2.7f, 2.4f, 4.4f, 2.4f, 0.8f); break;
			case Species.Rabbit: Quadruped(new Tone(0xe3d5cf), new Tone(0xf4ece6), 1.9f, 1.7f, 2.7f, 1.1f, 0.6f); break;
			case Species.Bird: Flyer(new Tone(0xdfe6f2), new Tone(0xb9c2d8), 0.95f); break;
			case Species.Butterfly: Flyer(new Tone(0xf8ccda), new Tone(0xdccef1), 0.34f); break;
			case Species.Fish: Swimmer(new Tone(0xa9c2d8)); break;
		}
	}

	private void Quadruped(Tone coat, Tone belly, float w, float h, float d, float legY, float headScale)
	{
		float legLen = legY;
		Box(_body, w, h, d, coat, new Vector3(0, (legLen + h * 0.5f) * S, 0));
		Box(_body, w * 0.86f, h * 0.34f, d * 0.9f, belly,
			new Vector3(0, (legLen + h * 0.18f) * S, 0), outlined: false);

		// Nose toward +Z.
		//
		// The whole menagerie was walking backwards. Yaw is derived with
		// Atan2(heading.x, heading.z), which turns +Z to face the heading — the
		// convention Character.cs is built to — but these bodies had the head at
		// NEGATIVE Z, so every animal presented its tail to wherever it was going.
		// Fixed in the model rather than by adding a half-turn to the yaw, so the
		// project keeps one facing convention instead of two.
		_head = Pivot(_body, new Vector3(0, (legLen + h * 0.85f) * S, d * 0.42f * S));
		Box(_head, w * 0.72f * headScale, h * 0.72f * headScale, w * 0.8f * headScale,
			coat, new Vector3(0, 0, w * 0.3f * headScale * S));
		if (_kind == Species.Rabbit)
		{
			// Ears. The one piece of species-specific modelling in here, because a
			// rabbit without them is just a small deer.
			Box(_head, 0.4f, 1.7f, 0.4f, coat, new Vector3(-0.45f * S, 1.2f * S, 0f), outlined: false);
			Box(_head, 0.4f, 1.7f, 0.4f, coat, new Vector3(0.45f * S, 1.2f * S, 0f), outlined: false);
		}
		else if (_kind == Species.Goat)
		{
			Box(_head, 0.35f, 0.9f, 0.35f, new Tone(0x8a6270), new Vector3(-0.4f * S, 0.9f * S, 0f), outlined: false);
			Box(_head, 0.35f, 0.9f, 0.35f, new Tone(0x8a6270), new Vector3(0.4f * S, 0.9f * S, 0f), outlined: false);
		}

		float lx = w * 0.32f, lz = d * 0.32f;
		for (int i = 0; i < 4; i++)
		{
			float sx = (i & 1) == 0 ? -lx : lx;
			float sz = i < 2 ? -lz : lz;
			var pivot = Pivot(_body, new Vector3(sx * S, legLen * S, sz * S));
			Box(pivot, 0.8f, legLen, 0.8f, coat, new Vector3(0, -legLen * 0.5f * S, 0), outlined: false);
			_limbs.Add(pivot);
		}
	}

	private void Flyer(Tone body, Tone wing, float scale)
	{
		Box(_body, 1.1f * scale, 1.0f * scale, 2.0f * scale, body, Vector3.Zero);
		for (int i = 0; i < 2; i++)
		{
			var pivot = Pivot(_body, new Vector3((i == 0 ? -0.5f : 0.5f) * scale * S, 0.2f * scale * S, 0));
			Box(pivot, 2.4f * scale, 0.22f * scale, 1.5f * scale, wing,
				new Vector3((i == 0 ? -1.2f : 1.2f) * scale * S, 0, 0), outlined: false);
			_limbs.Add(pivot);
		}
	}

	private void Swimmer(Tone tone)
	{
		Box(_body, 0.75f, 1.0f, 2.2f, tone, Vector3.Zero);
		// Tail behind, which is now -Z. See the note in Quadruped.
		var tail = Pivot(_body, new Vector3(0, 0, -1.1f * S));
		Box(tail, 0.2f, 1.1f, 1.0f, tone, new Vector3(0, 0, -0.5f * S), outlined: false);
		_limbs.Add(tail);
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

		var ink = new MeshInstance3D
		{
			Mesh = InkBuilder.Box(wx, wy, wz, tone.Pale),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			CustomAabb = new Aabb(new Vector3(-wx, -wy, -wz), new Vector3(wx * 2, wy * 2, wz * 2)),
		};
		ink.SetSurfaceOverrideMaterial(0, tone.Pale ? _inkLight : _inkDark);
		mesh.AddChild(ink);
	}

	/* ----------------------------------------------------------------
	 * Behaviour
	 * ---------------------------------------------------------------- */
	private float Cruise => _kind switch
	{
		Species.Deer => 3.4f,
		Species.Goat => 2.2f,
		Species.Rabbit => 3.0f,
		Species.Bird => 7.5f,
		Species.Butterfly => 1.8f,
		Species.Fish => 2.6f,
		_ => 2f,
	};

	public void Advance(Vector3 player, double delta)
	{
		float dt = (float)delta;
		var here = GlobalPosition;

		// Flight, at TOUCHING distance and once.
		//
		// The first version bolted at seven to thirteen units and re-triggered
		// every frame the player stayed inside that ring, so a meadow emptied
		// ahead of you and nothing ever settled — which is the opposite of a world
		// that is supposed to feel alive and indifferent. An animal should let you
		// walk up to it, move off a little when you are almost on top of it, and
		// then go back to what it was doing.
		float near = new Vector2(player.X - here.X, player.Z - here.Z).Length();
		float radius = _kind is Species.Bird or Species.Butterfly ? 3.4f : 2.6f;
		_spookCooldown = Mathf.Max(0f, _spookCooldown - dt);
		if (near < radius && _spookCooldown <= 0f)
		{
			_startle = 1f;
			// A refractory period, so standing next to something does not hold it
			// in a permanent panic.
			_spookCooldown = 7f;
			var away = new Vector3(here.X - player.X, 0, here.Z - player.Z);
			if (away.LengthSquared() > 0.01f) _heading = away.Normalized();
			_decide = 0.9f;
		}
		// Short. A second and a half of trotting is a few units of ground, which
		// is "moved off a bit" rather than "fled the province".
		_startle = Mathf.Max(0f, _startle - dt * 1.5f);

		_decide -= delta;
		if (_decide <= 0.0)
		{
			_decide = _rng.Range(1.6f, 4.5f);
			// Idling is as much of the behaviour as moving. A meadow where every
			// rabbit is permanently in transit looks like a screensaver.
			bool rest = _kind != Species.Bird && _rng.Chance(0.35f);
			_speed = rest ? 0f : Cruise * _rng.Range(0.55f, 1.05f);
			float turn = _rng.Bell() * 1.5f;
			_heading = _heading.Rotated(Vector3.Up, turn);
		}

		float speed = _speed + _startle * Cruise * 1.4f;
		if (speed > 0.01f)
		{
			var step = _heading * speed * dt;
			var want = here + step;
			if (!Legal(want, out float ground))
			{
				// Turn rather than stop. A creature stuck against a cliff jittering
				// in place is more distracting than one that simply walks away.
				_heading = _heading.Rotated(Vector3.Up, 2.2f);
			}
			else
			{
				want.Y = here.Y;
				GlobalPosition = want;
				_groundY = ground;
			}

			_yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(_heading.X, _heading.Z), 1f - Mathf.Exp(-7f * dt));
		}
		else if (Legal(here, out float standing)) _groundY = standing;

		Vertical(dt);
		Rotation = new Vector3(0, _yaw, 0);

		// One phase accumulator drives the gait, exactly as the traveller's does:
		// a walk and a bolt are the same curve at different rates.
		float cadence = Mathf.Clamp(speed / Cruise, 0f, 1.6f);
		_phase += dt * (_kind switch
		{
			Species.Butterfly => 17f,
			Species.Bird => 11f + cadence * 6f,
			Species.Fish => 5f + cadence * 4f,
			_ => 2.5f + cadence * 9f,
		});

		Gait(cadence, dt);
	}

	/// <summary>
	/// Height, and how a creature changes terrace.
	///
	/// Snapping straight to whatever the ground turned out to be is what the
	/// first version did, and on a world built entirely out of two-block terraces
	/// that means every animal teleports up and down all day. A terrace is a step
	/// a creature has to LEAVE THE GROUND to take, so it does: an upward change
	/// launches a hop with enough speed to clear it, a downward one just walks off
	/// the edge, and gravity handles both. The gait tucks the legs while the feet
	/// are off the ground, which is the whole reason the hop reads as a hop rather
	/// than as a smoothed slide.
	///
	/// Swimmers and flyers never touch this: they have no feet on anything.
	/// </summary>
	private void Vertical(float dt)
	{
		var at = GlobalPosition;

		if (_kind is Species.Fish or Species.Bird or Species.Butterfly)
		{
			float want = _kind switch
			{
				Species.Fish => Palette.WaterLevel - 1.1f,
				Species.Bird => _groundY + 10f,
				_ => _groundY + 1.9f,
			};
			float bob = _kind switch
			{
				Species.Bird => Mathf.Sin(_phase * 0.7f) * 0.10f,
				Species.Butterfly => Mathf.Sin(_phase * 0.5f) * 0.16f,
				_ => 0f,
			};
			float rate = _kind == Species.Fish ? 2f : 1.4f;
			GlobalPosition = new Vector3(at.X,
				Mathf.Lerp(at.Y, want + bob, 1f - Mathf.Exp(-rate * dt)), at.Z);
			return;
		}

		const float Gravity = 34f;
		float foot = at.Y;

		// A creature that has somehow ended up well under the terrain is put back
		// on it rather than left to fall for ever. Nothing should reach this, but
		// an animal quietly sinking out of the world is both the worst-looking
		// possible failure and the hardest to notice in code.
		if (foot < _groundY - 24f)
		{
			GlobalPosition = new Vector3(at.X, _groundY, at.Z);
			_vy = 0f;
			_airborne = false;
			return;
		}

		if (!_airborne)
		{
			float rise = _groundY - foot;
			if (rise > 0.35f)
			{
				// Enough to clear the step with a little over, which is what makes
				// the arc visible rather than a scramble.
				_vy = Mathf.Sqrt(2f * Gravity * (rise + 0.45f));
				_airborne = true;
			}
			else if (rise < -0.35f)
			{
				// Walked off a lip. No push, just let go.
				_vy = 0f;
				_airborne = true;
			}
			else
			{
				GlobalPosition = new Vector3(at.X, _groundY, at.Z);
				return;
			}
		}

		_vy -= Gravity * dt;
		float y = foot + _vy * dt;
		if (_vy <= 0f && y <= _groundY)
		{
			y = _groundY;
			_vy = 0f;
			_airborne = false;
		}
		GlobalPosition = new Vector3(at.X, y, at.Z);
	}

	private void Gait(float cadence, float dt)
	{
		switch (_kind)
		{
			case Species.Bird:
			case Species.Butterfly:
			{
				// Wings beat whether or not the creature is going anywhere: it is
				// holding itself up either way.
				float beat = Mathf.Sin(_phase) * (_kind == Species.Butterfly ? 1.15f : 0.72f);
				if (_limbs.Count >= 2)
				{
					_limbs[0].Rotation = new Vector3(0, 0, -beat);
					_limbs[1].Rotation = new Vector3(0, 0, beat);
				}
				_body.Position = new Vector3(0, Mathf.Sin(_phase * 2f) * 0.02f, 0);
				break;
			}
			case Species.Fish:
			{
				if (_limbs.Count >= 1)
					_limbs[0].Rotation = new Vector3(0, Mathf.Sin(_phase) * 0.55f, 0);
				break;
			}
			default:
			{
				// Off the ground: legs tucked, front reaching. Without this the
				// animal runs on air through the whole arc and the hop reads as a
				// glitch rather than as a jump.
				if (_airborne)
				{
					float t = Mathf.Clamp(_vy / 6f, -1f, 1f);
					for (int i = 0; i < _limbs.Count; i++)
					{
						bool front = i < 2;
						_limbs[i].Rotation = new Vector3(front ? 0.85f + t * 0.4f : -0.7f - t * 0.3f, 0, 0);
					}
					_body.Position = new Vector3(0, 0, 0);
					if (_head != null) _head.Rotation = new Vector3(-t * 0.25f, 0, 0);
					break;
				}

				// Diagonal pairs, which is what a quadruped actually does and reads
				// correctly even at this size.
				float swing = Mathf.Sin(_phase) * cadence * 0.75f;
				for (int i = 0; i < _limbs.Count; i++)
				{
					bool lead = i == 0 || i == 3;
					_limbs[i].Rotation = new Vector3(lead ? swing : -swing, 0, 0);
				}
				// Rabbits hop rather than walk: the body rises with the stride
				// instead of staying level over it.
				float bob = _kind == Species.Rabbit
					? Mathf.Abs(Mathf.Sin(_phase)) * cadence * 0.22f
					: Mathf.Abs(Mathf.Sin(_phase * 2f)) * cadence * 0.05f;
				_body.Position = new Vector3(0, bob, 0);
				if (_head != null)
					_head.Rotation = new Vector3(Mathf.Sin(_phase * 0.5f) * 0.06f * cadence, 0, 0);
				break;
			}
		}
	}

	/// <summary>Is this somewhere the creature can be, and how high is the ground?</summary>
	private bool Legal(Vector3 at, out float ground)
	{
		int S = _terrain.Size;
		int x = (int)MathF.Floor(at.X), z = (int)MathF.Floor(at.Z);
		ground = at.Y;
		if (x < 2 || z < 2 || x >= S - 2 || z >= S - 2) return false;

		int i = z * S + x;
		bool water = _terrain.Land[i] == 0;
		if (_kind == Species.Fish) return water;

		if (_kind is Species.Bird or Species.Butterfly)
		{
			ground = MathF.Max(_terrain.Level[i], Palette.WaterLevel);
			return true;
		}

		if (water) return false;
		int level = _terrain.Level[i];
		// Measured against the ground the creature is walking on, not against its
		// current height. Mid-hop those differ by most of a terrace, and testing
		// the live Y made a creature reject the very ledge it was jumping onto.
		if (MathF.Abs(level - _groundY) > Terrain.Step + 0.5f) return false;
		if (_terrain.Grid.Heights[i] > level) return false;
		ground = level;
		return true;
	}
}
