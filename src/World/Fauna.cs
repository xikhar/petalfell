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
			case Species.Bird: Flyer(new Tone(0xdfe6f2), new Tone(0xb9c2d8), 1.5f); break;
			case Species.Butterfly: Flyer(new Tone(0xf8ccda), new Tone(0xdccef1), 0.75f); break;
			case Species.Fish: Swimmer(new Tone(0xa9c2d8)); break;
		}
	}

	private void Quadruped(Tone coat, Tone belly, float w, float h, float d, float legY, float headScale)
	{
		float legLen = legY;
		Box(_body, w, h, d, coat, new Vector3(0, (legLen + h * 0.5f) * S, 0));
		Box(_body, w * 0.86f, h * 0.34f, d * 0.9f, belly,
			new Vector3(0, (legLen + h * 0.18f) * S, 0), outlined: false);

		_head = Pivot(_body, new Vector3(0, (legLen + h * 0.85f) * S, -d * 0.42f * S));
		Box(_head, w * 0.72f * headScale, h * 0.72f * headScale, w * 0.8f * headScale,
			coat, new Vector3(0, 0, -w * 0.3f * headScale * S));
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
		var tail = Pivot(_body, new Vector3(0, 0, 1.1f * S));
		Box(tail, 0.2f, 1.1f, 1.0f, tone, new Vector3(0, 0, 0.5f * S), outlined: false);
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

		// Flight. Anything the player walks up to should leave, because a wild
		// animal that lets you stand next to it stops reading as wild — and the
		// bolt is most of the character these creatures have.
		float near = new Vector2(player.X - here.X, player.Z - here.Z).Length();
		float radius = _kind is Species.Bird or Species.Butterfly ? 7f : 13f;
		if (near < radius)
		{
			_startle = 1f;
			var away = new Vector3(here.X - player.X, 0, here.Z - player.Z);
			if (away.LengthSquared() > 0.01f) _heading = away.Normalized();
			_decide = 1.4f;
		}
		_startle = Mathf.Max(0f, _startle - dt * 0.55f);

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
				want.Y = _kind switch
				{
					Species.Fish => Mathf.Lerp(here.Y, Palette.WaterLevel - 1.1f, 1f - Mathf.Exp(-2f * dt)),
					Species.Bird => Mathf.Lerp(here.Y, ground + 10f, 1f - Mathf.Exp(-1.2f * dt))
						+ Mathf.Sin(_phase * 0.7f) * 0.02f,
					Species.Butterfly => Mathf.Lerp(here.Y, ground + 1.9f, 1f - Mathf.Exp(-2f * dt))
						+ Mathf.Sin(_phase * 3.1f) * 0.03f,
					_ => ground,
				};
				GlobalPosition = want;
			}

			_yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(_heading.X, _heading.Z), 1f - Mathf.Exp(-7f * dt));
		}
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
		// A terrace step is climbable; a cliff is not.
		if (MathF.Abs(level - at.Y) > Terrain.Step + 0.5f) return false;
		if (_terrain.Grid.Heights[i] > level) return false;
		ground = level;
		return true;
	}
}
