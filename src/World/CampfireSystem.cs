using System;
using System.Collections.Generic;
using Godot;

namespace Petalfell.World;

/// <summary>
/// Owns player-built campfires and validates their terrain footprint. Campfires
/// are runtime world objects rather than voxel edits, so they survive terrain
/// chunk unloads and never require a synchronous chunk remesh when placed.
/// </summary>
public partial class CampfireSystem : Node3D
{
	public const float PlacementDistance = 3.0f;
	public const float MinimumSpacing = 6.0f;

	private readonly List<Campfire> _fires = new();
	private Terrain _terrain;
	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private int _sequence;

	public IReadOnlyList<Campfire> Fires
	{
		get
		{
			Prune();
			return _fires;
		}
	}

	public void Setup(Terrain terrain, ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		_terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
		_inkLight = inkLight ?? throw new ArgumentNullException(nameof(inkLight));
		_inkDark = inkDark ?? throw new ArgumentNullException(nameof(inkDark));
	}

	/// <summary>
	/// Find the block-centred point a short distance in front of the player and
	/// verify a dry, unobstructed, exactly level 3x3 terrain footprint there.
	/// </summary>
	public bool CanPlace(Vector3 playerPosition, Vector3 facing,
		out Vector3 placement, out string reason)
	{
		placement = Vector3.Zero;
		reason = null;
		if (_terrain == null)
		{
			reason = "Campfire placement is not ready.";
			return false;
		}

		var direction = new Vector3(facing.X, 0f, facing.Z);
		if (direction.LengthSquared() < 0.001f) direction = Vector3.Forward;
		direction = direction.Normalized();
		var wanted = playerPosition + direction * PlacementDistance;
		int cx = Mathf.FloorToInt(wanted.X);
		int cz = Mathf.FloorToInt(wanted.Z);

		// One-cell footprint plus one safety cell for all terrain array reads.
		if (cx < 2 || cz < 2 || cx >= _terrain.Size - 2 || cz >= _terrain.Size - 2)
		{
			reason = "Move away from the edge of the world.";
			return false;
		}

		int centre = cz * _terrain.Size + cx;
		int ground = _terrain.Level[centre];
		placement = new Vector3(cx + 0.5f, ground, cz + 0.5f);

		for (int dz = -1; dz <= 1; dz++)
		for (int dx = -1; dx <= 1; dx++)
		{
			int x = cx + dx;
			int z = cz + dz;
			int i = z * _terrain.Size + x;

			if (_terrain.Land[i] == 0 || _terrain.Level[i] <= Terrain.Sea ||
				_terrain.Wet[i] != 0)
			{
				reason = "Campfires need dry ground.";
				return false;
			}
			if (_terrain.Level[i] != ground || _terrain.StairMask[i] != 0)
			{
				reason = "Find a flat 3 x 3 patch of ground.";
				return false;
			}

			// Heights catches generated trees, buildings and props cheaply. The
			// direct block checks also catch sparse AIR/solid edits whose cached
			// height may intentionally describe the original terrain column.
			if (_terrain.Grid.Heights[i] > ground)
			{
				reason = "Clear the space before building a campfire.";
				return false;
			}
			int top = Math.Min(ground + 2, _terrain.Grid.Height - 1);
			for (int y = ground; y <= top; y++)
			{
				if (!_terrain.Grid.SolidAt(x, y, z)) continue;
				reason = "Clear the space before building a campfire.";
				return false;
			}
		}

		Prune();
		float minimum2 = MinimumSpacing * MinimumSpacing;
		foreach (var fire in _fires)
		{
			var delta = fire.GlobalPosition - placement;
			delta.Y = 0f;
			if (delta.LengthSquared() >= minimum2) continue;
			reason = "This is too close to another campfire.";
			return false;
		}

		return true;
	}

	/// <summary>Instantiate a campfire at a position already accepted by CanPlace.</summary>
	public Campfire Spawn(Vector3 position)
	{
		if (_terrain == null || _inkLight == null || _inkDark == null)
			throw new InvalidOperationException("Call CampfireSystem.Setup before Spawn.");

		int sequence = ++_sequence;
		var fire = new Campfire
		{
			Name = $"Campfire_{sequence}",
		};
		int visualSeed = unchecked(sequence * 92821 ^
			Mathf.FloorToInt(position.X) * 73856093 ^
			Mathf.FloorToInt(position.Z) * 19349663);
		fire.Setup(_inkLight, _inkDark, visualSeed);
		AddChild(fire);
		fire.GlobalPosition = position;
		_fires.Add(fire);
		return fire;
	}

	public Campfire Nearest(Vector3 position, float radius)
	{
		if (radius < 0f) return null;
		Prune();
		Campfire nearest = null;
		float best = radius * radius;
		foreach (var fire in _fires)
		{
			float distance = fire.GlobalPosition.DistanceSquaredTo(position);
			if (distance > best) continue;
			best = distance;
			nearest = fire;
		}
		return nearest;
	}

	/// <summary>
	/// Choose a dry, level player seat on the same side of the fire they approached
	/// from. The returned point is a cell centre so navigation and the authored
	/// final sitting transform agree exactly; nearby angular alternatives avoid a
	/// prop or terrace edge without ever falling back to the unsafe hearth rim.
	/// </summary>
	public bool TryFindSeat(Campfire fire, Vector3 preferredFrom, out Vector3 seat)
	{
		seat = Vector3.Zero;
		if (_terrain == null || fire == null || !GodotObject.IsInstanceValid(fire))
			return false;

		var preferred = preferredFrom - fire.GlobalPosition;
		preferred.Y = 0f;
		if (preferred.LengthSquared() < 0.01f) preferred = Vector3.Back;
		float start = Mathf.Atan2(preferred.Z, preferred.X);
		float[] radii = { 2.55f, 2.35f, 2.75f };
		int[] angularOrder = { 0, 1, -1, 2, -2, 3, -3, 4, -4, 5, -5, 6, -6, 7, -7, 8 };
		var visited = new HashSet<int>();

		foreach (float radius in radii)
		foreach (int offset in angularOrder)
		{
			float angle = start + offset * (Mathf.Tau / 16f);
			var wanted = fire.GlobalPosition + new Vector3(
				Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
			int x = Mathf.FloorToInt(wanted.X);
			int z = Mathf.FloorToInt(wanted.Z);
			if (x < 2 || z < 2 || x >= _terrain.Size - 2 || z >= _terrain.Size - 2)
				continue;
			int key = z * _terrain.Size + x;
			if (!visited.Add(key)) continue;

			var candidate = new Vector3(x + 0.5f, fire.GlobalPosition.Y, z + 0.5f);
			var fromFire = candidate - fire.GlobalPosition;
			fromFire.Y = 0f;
			// This is the invariant the old 1.34-unit snap violated: even the
			// nearest fallback leaves generous visible space around the stone ring.
			if (fromFire.LengthSquared() < 2.30f * 2.30f) continue;
			if (!SeatFootprintClear(candidate, Mathf.RoundToInt(fire.GlobalPosition.Y)))
				continue;

			seat = candidate;
			return true;
		}
		return false;
	}

	private bool SeatFootprintClear(Vector3 candidate, int expectedGround)
	{
		// Centre plus an octagonal capsule footprint. Sampling the actual radius
		// prevents a centre that is technically safe from hanging over a corner.
		for (int sample = 0; sample < 9; sample++)
		{
			float angle = (sample - 1) * Mathf.Tau / 8f;
			float radius = sample == 0 ? 0f : 0.43f;
			int x = Mathf.FloorToInt(candidate.X + Mathf.Cos(angle) * radius);
			int z = Mathf.FloorToInt(candidate.Z + Mathf.Sin(angle) * radius);
			if (x < 1 || z < 1 || x >= _terrain.Size - 1 || z >= _terrain.Size - 1)
				return false;
			int i = z * _terrain.Size + x;
			if (_terrain.Land[i] == 0 || _terrain.Wet[i] != 0 ||
				_terrain.StairMask[i] != 0 || _terrain.Level[i] != expectedGround ||
				_terrain.Grid.Heights[i] > expectedGround)
				return false;

			int top = Math.Min(expectedGround + 2, _terrain.Grid.Height - 1);
			for (int y = expectedGround; y <= top; y++)
				if (_terrain.Grid.SolidAt(x, y, z)) return false;
		}
		return true;
	}

	private void Prune()
	{
		for (int i = _fires.Count - 1; i >= 0; i--)
		{
			var fire = _fires[i];
			if (GodotObject.IsInstanceValid(fire) && !fire.IsQueuedForDeletion()) continue;
			_fires.RemoveAt(i);
		}
	}
}
