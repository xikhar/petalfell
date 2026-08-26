using System.Collections.Generic;
using Godot;
using Petalfell.Player;

namespace Petalfell.Items;

/// <summary>Owns dropped/thrown item instances without coupling them to terrain chunks.</summary>
public partial class WorldItemSystem : Node3D
{
	private readonly List<WorldItem> _items = new();
	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private Controller _player;
	private long _throwSequence;

	public void Setup(ShaderMaterial inkLight, ShaderMaterial inkDark, Controller player)
	{
		_inkLight = inkLight;
		_inkDark = inkDark;
		_player = player;
	}

	public WorldItem Throw(ItemDefinition item, ItemHand hand, Vector3 origin,
		Vector3 velocity)
	{
		var worldItem = new WorldItem { Name = $"WorldItem_{item.Name}" };
		AddChild(worldItem);
		worldItem.Setup(item, hand, ++_throwSequence, _inkLight, _inkDark, _player);
		worldItem.GlobalPosition = origin;
		worldItem.LinearVelocity = velocity;
		worldItem.AngularVelocity = new Vector3(7.5f, 3.2f, 5.1f);
		_items.Add(worldItem);
		return worldItem;
	}

	public bool TryPickUpNearest(Vector3 position, GlobalInventory inventory,
		float radius = 3.0f)
	{
		Prune();
		WorldItem nearest = null;
		float best = radius * radius;
		foreach (var item in _items)
		{
			if (!item.CanPickUp) continue;
			float distance = item.GlobalPosition.DistanceSquaredTo(position);
			if (distance >= best) continue;
			best = distance;
			nearest = item;
		}
		if (nearest == null || !inventory.TryAdd(nearest.Item.Id, 1)) return false;

		_items.Remove(nearest);
		nearest.QueueFree();
		return true;
	}

	public WorldItem Nearest(Vector3 position, float radius = 3.0f)
	{
		Prune();
		WorldItem nearest = null;
		float best = radius * radius;
		foreach (var item in _items)
		{
			if (!item.CanPickUp) continue;
			float distance = item.GlobalPosition.DistanceSquaredTo(position);
			if (distance >= best) continue;
			best = distance;
			nearest = item;
		}
		return nearest;
	}

	public bool TryPickUp(WorldItem item, GlobalInventory inventory)
	{
		Prune();
		if (item == null || !item.CanPickUp || !_items.Contains(item) ||
			!inventory.TryAdd(item.Item.Id, 1))
			return false;
		_items.Remove(item);
		item.QueueFree();
		return true;
	}

	public WorldItem LatestFetchable(string itemId)
	{
		Prune();
		WorldItem latest = null;
		foreach (var item in _items)
		{
			if (!item.CanPickUp || item.Item.Id != itemId) continue;
			if (latest == null || item.ThrowSequence > latest.ThrowSequence) latest = item;
		}
		return latest;
	}

	private void Prune()
	{
		for (int i = _items.Count - 1; i >= 0; i--)
			if (!GodotObject.IsInstanceValid(_items[i]) || _items[i].IsQueuedForDeletion())
				_items.RemoveAt(i);
	}
}
