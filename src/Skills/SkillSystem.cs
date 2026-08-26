using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Gameplay;
using Petalfell.Items;
using Petalfell.Player;
using Petalfell.World;

namespace Petalfell.Skills;

/// <summary>Stable skill data, ready to move into chapter content later.</summary>
public sealed class SkillDefinition
{
	public string Id { get; }
	public string Name { get; }
	public string ActionName { get; }
	public string KeyLabel { get; }

	public SkillDefinition(string id, string name, string actionName, string keyLabel)
	{
		Id = id;
		Name = name;
		ActionName = actionName;
		KeyLabel = keyLabel;
	}
}

public static class SkillCatalog
{
	public static readonly SkillDefinition BuildCampfire = new(
		"build_campfire", "Build campfire", "skill_selector", "T");

	public static readonly IReadOnlyList<SkillDefinition> All =
		Array.AsReadOnly(new[] { BuildCampfire });
}

/// <summary>
/// Player skills are separate from items: a skill may consume inventory
/// materials and create world state, but it is neither an equipped object nor
/// a special case inside the inventory. The first skill builds a campfire.
/// </summary>
public partial class SkillSystem : Node, IInteractionProvider
{
	public const int CampfireWoodCost = 5;
	private const float SitRadius = 5.25f;

	private GlobalInventory _inventory;
	private CampfireSystem _campfires;
	private Controller _player;
	private Dog _dog;
	private Navigation _navigation;
	private Campfire _restingAt;
	private Campfire _pendingRestAt;
	private Vector3 _pendingSeat;
	private List<Vector3> _pendingRoute;

	public event Action<string> NoticeRequested;

	/// <summary>The learned skills shown by the selector, in stable UI order.</summary>
	public IReadOnlyList<SkillDefinition> AvailableSkills => SkillCatalog.All;

	/// <summary>Live material count used by the selector's affordability state.</summary>
	public int WoodCount => _inventory?.Count(ItemCatalog.Wood.Id) ?? 0;

	public void Setup(GlobalInventory inventory, CampfireSystem campfires,
		Controller player, Dog dog, Navigation navigation)
	{
		_inventory = inventory;
		_campfires = campfires;
		_player = player;
		_dog = dog;
		_navigation = navigation;
	}

	public override void _Process(double delta)
	{
		if (_pendingRestAt != null)
		{
			bool invalid = !GodotObject.IsInstanceValid(_pendingRestAt) ||
				_pendingRestAt.IsQueuedForDeletion() || _player.Swimming;
			if (invalid)
			{
				ClearPendingRest();
			}
			else
			{
				var remaining = _player.GlobalPosition - _pendingSeat;
				remaining.Y = 0f;
				// Controller route waypoints deliberately have a broad arrival radius.
				// This final short settle is collision-safe because TryFindSeat already
				// validated the complete capsule footprint.
				if (remaining.LengthSquared() <= 1.15f * 1.15f)
					CompleteRest(_pendingRestAt, _pendingSeat);
				else if (_player.Route == null ||
					!ReferenceEquals(_player.Route, _pendingRoute))
					ClearPendingRest();
			}
		}

		if (_restingAt != null && (!_player.Sitting ||
			!GodotObject.IsInstanceValid(_restingAt)))
		{
			_restingAt = null;
			_dog.EndCampfireSit();
		}
	}

	/// <summary>Execute a selected skill by stable ID.</summary>
	public bool Activate(string skillId)
	{
		InteractionResult result;
		if (skillId == SkillCatalog.BuildCampfire.Id) result = BuildCampfire();
		else result = InteractionResult.Failed("That skill is not available");
		if (!string.IsNullOrWhiteSpace(result.Message)) NoticeRequested?.Invoke(result.Message);
		return result.Succeeded;
	}

	public void GatherInteractions(Vector3 playerPosition, List<ContextAction> actions)
	{
		var fire = _campfires.Nearest(playerPosition, SitRadius);
		if (fire != null)
		{
			float distance = playerPosition.DistanceTo(fire.GlobalPosition);
			bool standingUp = _player.Sitting && fire == _restingAt;
			actions.Add(new ContextAction("interact", "R",
				standingUp ? "Stand up" : "Sit down",
				priority: 80, distance,
				() => standingUp ? EndRest() : BeginRest(fire)));
		}

	}

	private InteractionResult BuildCampfire()
	{
		if (_player.Sitting) return InteractionResult.Failed("Stand up before building");
		if (new Vector2(_player.Velocity.X, _player.Velocity.Z).LengthSquared() > 0.20f)
			return InteractionResult.Failed("Stand still to build");
		int wood = _inventory.Count(ItemCatalog.Wood.Id);
		if (wood < CampfireWoodCost)
			return InteractionResult.Failed($"Need {CampfireWoodCost - wood} more wood");
		if (!_campfires.CanPlace(_player.GlobalPosition, _player.Facing,
			out Vector3 placement, out string reason))
			return InteractionResult.Failed(reason ?? "Find a flat clear area");

		// Validate before paying, then refund if world creation unexpectedly fails.
		if (!_inventory.TryRemove(ItemCatalog.Wood.Id, CampfireWoodCost))
			return InteractionResult.Failed("Not enough wood");
		var fire = _campfires.Spawn(placement);
		if (fire == null)
		{
			_inventory.TryAdd(ItemCatalog.Wood.Id, CampfireWoodCost);
			return InteractionResult.Failed("Could not build here");
		}
		return InteractionResult.Done("Campfire built");
	}

	private InteractionResult BeginRest(Campfire fire)
	{
		if (fire == null || !GodotObject.IsInstanceValid(fire))
			return InteractionResult.Failed("The fire is no longer there");

		if (!_campfires.TryFindSeat(fire, _player.GlobalPosition, out Vector3 seat))
			return InteractionResult.Failed("No clear place to sit around the fire");

		var remaining = _player.GlobalPosition - seat;
		remaining.Y = 0f;
		if (remaining.LengthSquared() <= 1.15f * 1.15f)
		{
			CompleteRest(fire, seat);
			return InteractionResult.Done();
		}

		var route = _navigation?.FindPath(_player.GlobalPosition, seat);
		if (route == null || route.Count == 0)
			return InteractionResult.Failed("No clear path to a seat around the fire");
		_pendingRestAt = fire;
		_pendingSeat = seat;
		_pendingRoute = route;
		_player.SetRoute(route);
		return InteractionResult.Done();
	}

	private void CompleteRest(Campfire fire, Vector3 seat)
	{
		ClearPendingRest();
		_player.BeginSit(seat, fire.GlobalPosition);
		_dog.BeginCampfireSit(fire.GlobalPosition, seat);
		_restingAt = fire;
	}

	private void ClearPendingRest()
	{
		_pendingRestAt = null;
		_pendingRoute = null;
		_pendingSeat = Vector3.Zero;
	}

	private InteractionResult EndRest()
	{
		ClearPendingRest();
		_player.EndSit();
		_dog.EndCampfireSit();
		_restingAt = null;
		return InteractionResult.Done();
	}
}
