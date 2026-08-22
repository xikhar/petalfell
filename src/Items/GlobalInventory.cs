using System;
using Godot;

namespace Petalfell.Items;

/// <summary>A single storage cell in the global inventory.</summary>
public sealed class InventorySlot
{
	public string ItemId { get; internal set; }
	public int Quantity { get; internal set; }
	public bool Empty => ItemId == null || Quantity <= 0;
	public ItemDefinition Item => ItemCatalog.Get(ItemId);
}

/// <summary>
/// Persistent game-level inventory state. Storage is deliberately distinct
/// from the four-item quick loadout and from the two hands: collecting an item,
/// assigning it for quick access, and holding it are three separate actions.
/// </summary>
public partial class GlobalInventory : Node
{
	public const int Capacity = 24;
	public const int LoadoutCapacity = 4;

	public static GlobalInventory Instance { get; private set; }
	public InventorySlot[] Slots { get; } = new InventorySlot[Capacity];

	private readonly string[] _loadout = new string[LoadoutCapacity];
	private int _leftLoadout = -1;
	private int _rightLoadout = -1;

	public event Action Changed;

	public int LeftLoadout => _leftLoadout;
	public int RightLoadout => _rightLoadout;

	public GlobalInventory()
	{
		for (int i = 0; i < Slots.Length; i++) Slots[i] = new InventorySlot();
	}

	public override void _EnterTree() => Instance = this;

	public override void _Ready()
	{
		// Chapter-one bootstrap item. Later this comes from a new-game loadout or
		// save data; the inventory code itself remains unchanged.
		if (Count(ItemCatalog.Stick.Id) == 0)
		{
			TryAdd(ItemCatalog.Stick.Id, 1);
			AssignLoadout(0, ItemCatalog.Stick.Id);
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public string LoadoutItemId(int index) =>
		index >= 0 && index < LoadoutCapacity ? _loadout[index] : null;

	public ItemDefinition LoadoutItem(int index) => ItemCatalog.Get(LoadoutItemId(index));

	public int Count(string itemId)
	{
		if (itemId == null) return 0;
		int total = 0;
		foreach (var slot in Slots)
			if (!slot.Empty && slot.ItemId == itemId) total += slot.Quantity;
		return total;
	}

	public bool TryAdd(string itemId, int quantity = 1)
	{
		var item = ItemCatalog.Get(itemId);
		if (item == null || quantity <= 0) return false;

		int available = 0;
		foreach (var slot in Slots)
		{
			if (slot.Empty) available += item.MaxStack;
			else if (slot.ItemId == itemId) available += item.MaxStack - slot.Quantity;
		}
		if (available < quantity) return false;

		int remaining = quantity;
		foreach (var slot in Slots)
		{
			if (slot.ItemId != itemId || slot.Quantity >= item.MaxStack) continue;
			int add = Math.Min(remaining, item.MaxStack - slot.Quantity);
			slot.Quantity += add;
			remaining -= add;
			if (remaining == 0) break;
		}
		foreach (var slot in Slots)
		{
			if (!slot.Empty) continue;
			int add = Math.Min(remaining, item.MaxStack);
			slot.ItemId = itemId;
			slot.Quantity = add;
			remaining -= add;
			if (remaining == 0) break;
		}

		Changed?.Invoke();
		return true;
	}

	public bool TryRemove(string itemId, int quantity = 1)
	{
		if (quantity <= 0 || Count(itemId) < quantity) return false;
		int remaining = quantity;
		for (int i = Slots.Length - 1; i >= 0 && remaining > 0; i--)
		{
			var slot = Slots[i];
			if (slot.Empty || slot.ItemId != itemId) continue;
			int remove = Math.Min(remaining, slot.Quantity);
			slot.Quantity -= remove;
			remaining -= remove;
			if (slot.Quantity == 0) slot.ItemId = null;
		}
		Changed?.Invoke();
		return true;
	}

	public bool AssignLoadout(int index, string itemId)
	{
		if (index < 0 || index >= LoadoutCapacity) return false;
		var item = ItemCatalog.Get(itemId);
		if (item == null || !item.Equipable) return false;
		_loadout[index] = itemId;
		Changed?.Invoke();
		return true;
	}

	public bool SelectForHand(int index, ItemHand hand)
	{
		if (index < 0 || index >= LoadoutCapacity) return false;
		var item = LoadoutItem(index);
		if (item == null || !item.Equipable || Count(item.Id) == 0) return false;

		ref int selected = ref (hand == ItemHand.Left ? ref _leftLoadout : ref _rightLoadout);
		ref int other = ref (hand == ItemHand.Left ? ref _rightLoadout : ref _leftLoadout);
		if (selected == index)
		{
			selected = -1;
			Changed?.Invoke();
			return true;
		}

		// One physical copy cannot appear in both hands. Two copies may.
		if (other >= 0 && LoadoutItemId(other) == item.Id && Count(item.Id) < 2)
			other = -1;
		selected = index;
		Changed?.Invoke();
		return true;
	}

	public ItemDefinition HeldItem(ItemHand hand)
	{
		int selected = hand == ItemHand.Left ? _leftLoadout : _rightLoadout;
		var item = LoadoutItem(selected);
		return item != null && Count(item.Id) > 0 ? item : null;
	}
}
