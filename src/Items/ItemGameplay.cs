using Godot;
using Petalfell.Player;

namespace Petalfell.Items;

/// <summary>
/// Connects inventory state to the current traveller, world items, and dog.
/// Item definitions own capabilities; this node only routes player intent.
/// </summary>
public partial class ItemGameplay : Node
{
	private GlobalInventory _inventory;
	private WorldItemSystem _worldItems;
	private Controller _player;
	private Character _character;
	private Dog _dog;

	private float _leftCharge = -1f;
	private float _rightCharge = -1f;

	public void Setup(GlobalInventory inventory, WorldItemSystem worldItems,
		Controller player, Character character, Dog dog)
	{
		_inventory = inventory;
		_worldItems = worldItems;
		_player = player;
		_character = character;
		_dog = dog;
		_inventory.Changed += RefreshHands;
		RefreshHands();
	}

	public override void _ExitTree()
	{
		if (_inventory != null) _inventory.Changed -= RefreshHands;
	}

	public override void _Process(double delta)
	{
		AdvanceCharge(ItemHand.Left, ref _leftCharge, (float)delta);
		AdvanceCharge(ItemHand.Right, ref _rightCharge, (float)delta);
	}

	/// <returns>True when this gameplay layer consumed the event.</returns>
	public bool HandleInput(InputEvent inputEvent)
	{
		if (inputEvent is not InputEventKey key || key.Echo) return false;

		if (key.Pressed)
		{
			for (int i = 0; i < GlobalInventory.LoadoutCapacity; i++)
			{
				if (!inputEvent.IsActionPressed($"loadout_{i + 1}")) continue;
				_inventory.SelectForHand(i, key.ShiftPressed ? ItemHand.Left : ItemHand.Right);
				return true;
			}

			if (inputEvent.IsActionPressed("interact"))
			{
				_worldItems.TryPickUpNearest(_player.GlobalPosition, _inventory);
				return true;
			}
			if (inputEvent.IsActionPressed("dog_fetch"))
			{
				var stick = _worldItems.LatestFetchable(ItemCatalog.Stick.Id);
				if (stick != null) _dog.Fetch(stick);
				return true;
			}
			if (inputEvent.IsActionPressed("throw_left"))
				return BeginCharge(ItemHand.Left, ref _leftCharge);
			if (inputEvent.IsActionPressed("throw_right"))
				return BeginCharge(ItemHand.Right, ref _rightCharge);
		}
		else
		{
			if (inputEvent.IsActionReleased("throw_left"))
				return ReleaseThrow(ItemHand.Left, ref _leftCharge);
			if (inputEvent.IsActionReleased("throw_right"))
				return ReleaseThrow(ItemHand.Right, ref _rightCharge);
		}
		return false;
	}

	private bool BeginCharge(ItemHand hand, ref float clock)
	{
		var item = _inventory.HeldItem(hand);
		if (item?.Throw == null) return false;
		clock = 0f;
		_character.SetThrowCharge(hand, 0f);
		return true;
	}

	private void AdvanceCharge(ItemHand hand, ref float clock, float delta)
	{
		if (clock < 0f) return;
		var item = _inventory.HeldItem(hand);
		if (item?.Throw == null)
		{
			clock = -1f;
			_character.SetThrowCharge(hand, 0f);
			return;
		}
		clock = Mathf.Min(clock + delta, item.Throw.ChargeSeconds);
		_character.SetThrowCharge(hand, clock / item.Throw.ChargeSeconds);
	}

	private bool ReleaseThrow(ItemHand hand, ref float clock)
	{
		if (clock < 0f) return false;
		var item = _inventory.HeldItem(hand);
		var profile = item?.Throw;
		float charge = profile == null ? 0f : Mathf.Clamp(clock / profile.ChargeSeconds, 0f, 1f);
		clock = -1f;
		_character.SetThrowCharge(hand, 0f);
		if (item == null || profile == null) return true;

		Vector3 origin = _character.HandWorldPosition(hand);
		Vector3 direction = new(_player.Facing.X, 0f, _player.Facing.Z);
		if (direction.LengthSquared() < 0.001f) direction = Vector3.Forward;
		direction = direction.Normalized();
		float shaped = charge * charge * (3f - 2f * charge);
		float speed = Mathf.Lerp(profile.MinimumSpeed, profile.MaximumSpeed, shaped);
		float lift = Mathf.Lerp(profile.MinimumLift, profile.MaximumLift, shaped);

		if (!_inventory.TryRemove(item.Id, 1)) return true;
		_worldItems.Throw(item, hand, origin + direction * 0.42f + Vector3.Up * 0.12f,
			direction * speed + Vector3.Up * lift + _player.Velocity * 0.28f);
		return true;
	}

	private void RefreshHands()
	{
		_character?.SetHeldItem(ItemHand.Left, _inventory.HeldItem(ItemHand.Left));
		_character?.SetHeldItem(ItemHand.Right, _inventory.HeldItem(ItemHand.Right));
	}
}

