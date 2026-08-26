using Godot;
using Petalfell.Items;

namespace Petalfell.UI;

/// <summary>
/// A deliberately small, translucent view over the global inventory. It owns no
/// item state: every equip/loadout mutation is delegated to GlobalInventory so
/// save data and gameplay never depend on a particular arrangement of controls.
/// </summary>
public partial class InventoryView : CanvasLayer
{
	private GlobalInventory _inventory;
	private Control _root;
	private InventoryCanvas _canvas;
	private bool _open;

	public bool IsOpen => _open;

	public void Setup(GlobalInventory inventory) => _inventory = inventory;

	public override void _Ready()
	{
		Layer = 260;

		_root = new Control
		{
			Name = "InventoryRoot",
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var dim = new ColorRect
		{
			Color = new Color(0.11f, 0.10f, 0.16f, 0.25f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(dim);

		var panel = new PanelContainer
		{
			Name = "InventoryGlass",
			MouseFilter = Control.MouseFilterEnum.Stop,
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			AnchorTop = 0.5f,
			AnchorBottom = 0.5f,
			OffsetLeft = -340f,
			OffsetRight = 340f,
			OffsetTop = -275f,
			OffsetBottom = 275f,
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.96f, 0.96f, 1.0f, 0.16f),
			BorderColor = new Color(1f, 1f, 1f, 0.42f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 18,
			CornerRadiusTopRight = 18,
			CornerRadiusBottomLeft = 18,
			CornerRadiusBottomRight = 18,
			ShadowColor = new Color(0.14f, 0.11f, 0.20f, 0.24f),
			ShadowSize = 18,
		});
		_root.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 26);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_right", 26);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		panel.AddChild(margin);

		_canvas = new InventoryCanvas
		{
			Name = "InventoryContents",
			CustomMinimumSize = new Vector2(628f, 508f),
			MouseFilter = Control.MouseFilterEnum.Stop,
			FocusMode = Control.FocusModeEnum.None,
		};
		_canvas.Setup(_inventory);
		margin.AddChild(_canvas);

		Visible = false;
	}

	public void Toggle()
	{
		_open = !_open;
		Visible = _open;
		if (_open)
		{
			_canvas?.QueueRedraw();
		}
	}

	public void Close()
	{
		if (!_open) return;
		_open = false;
		Visible = false;
	}

	/// <summary>
	/// Main calls this before ordinary game input. Returning true means the modal
	/// inventory owns the event and it must not reach movement, items, or the map.
	/// </summary>
	public bool HandleInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventKey key && !key.Echo)
		{
			if (key.Pressed && inputEvent.IsActionPressed("inventory"))
			{
				Toggle();
				return true;
			}
			if (!_open) return false;
			if (!key.Pressed) return true;

			if (IsKey(key, Key.Escape))
			{
				Close();
				return true;
			}
			if (IsKey(key, Key.Z))
			{
				_canvas?.EquipSelected(ItemHand.Left);
				return true;
			}
			if (IsKey(key, Key.X))
			{
				_canvas?.EquipSelected(ItemHand.Right);
				return true;
			}

			int quick = QuickIndex(key);
			if (quick >= 0)
			{
				_canvas?.AssignSelectedToLoadout(quick);
				return true;
			}
			return true;
		}

		return _open;
	}

	private static bool IsKey(InputEventKey input, Key key) =>
		input.PhysicalKeycode == key || input.Keycode == key;

	private static int QuickIndex(InputEventKey key)
	{
		if (IsKey(key, Key.Key1)) return 0;
		if (IsKey(key, Key.Key2)) return 1;
		if (IsKey(key, Key.Key3)) return 2;
		if (IsKey(key, Key.Key4)) return 3;
		return -1;
	}

	private partial class InventoryCanvas : Control
	{
		private const int Columns = 6;
		private const int Rows = 4;
		private const float SlotSize = 66f;
		private const float SlotGap = 10f;
		private const float GridTop = 58f;
		private const float QuickSize = 58f;
		private const float QuickGap = 12f;

		private static readonly Color Text = new(1f, 1f, 1f, 0.91f);
		private static readonly Color Muted = new(1f, 1f, 1f, 0.54f);
		private static readonly Color SlotFill = new(0.96f, 0.96f, 1f, 0.10f);
		private static readonly Color SlotEdge = new(1f, 1f, 1f, 0.25f);
		private static readonly Color SelectedFill = new(1f, 1f, 1f, 0.20f);
		private static readonly Color SelectedEdge = new(1f, 1f, 1f, 0.82f);

		private GlobalInventory _inventory;
		private int _selected = -1;

		public void Setup(GlobalInventory inventory) => _inventory = inventory;

		public override void _Ready()
		{
			if (_inventory != null) _inventory.Changed += InventoryChanged;
			QueueRedraw();
		}

		public override void _ExitTree()
		{
			if (_inventory != null) _inventory.Changed -= InventoryChanged;
		}

		private void InventoryChanged() => QueueRedraw();

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawString(font, new Vector2(2f, 25f), "Inventory",
				HorizontalAlignment.Left, -1f, 21, Text);

			var selectedItem = SelectedItem();
			if (selectedItem != null)
				DrawString(font, new Vector2(Size.X - 210f, 25f), selectedItem.Name,
					HorizontalAlignment.Right, 208f, 16, Muted);

			for (int i = 0; i < GlobalInventory.Capacity; i++)
				DrawInventorySlot(i, InventoryRect(i), font);

			float quickY = GridTop + Rows * SlotSize + (Rows - 1) * SlotGap + 37f;
			DrawString(font, new Vector2(2f, quickY), "Quick loadout",
				HorizontalAlignment.Left, -1f, 14, Muted);
			for (int i = 0; i < GlobalInventory.LoadoutCapacity; i++)
				DrawQuickSlot(i, QuickRect(i), font);

			DrawString(font, new Vector2(2f, Size.Y - 5f),
				"click  right hand     shift-click  left hand     1–4  quick slot",
				HorizontalAlignment.Left, Size.X - 4f, 13, Muted);
		}

		private void DrawInventorySlot(int index, Rect2 rect, Font font)
		{
			bool selected = index == _selected;
			DrawRect(rect, selected ? SelectedFill : SlotFill, filled: true);
			DrawRect(rect, selected ? SelectedEdge : SlotEdge,
				filled: false, width: selected ? 2f : 1f, antialiased: true);

			if (_inventory == null || index >= _inventory.Slots.Length) return;
			var slot = _inventory.Slots[index];
			if (slot.Empty || slot.Item == null) return;

			ItemIconRenderer.Draw(this, slot.Item, rect.GetCenter(), 0.92f);
			if (slot.Quantity > 1)
				DrawString(font, new Vector2(rect.Position.X + 5f, rect.End.Y - 7f),
					slot.Quantity.ToString(), HorizontalAlignment.Right, rect.Size.X - 10f,
					14, Text);
		}

		private void DrawQuickSlot(int index, Rect2 rect, Font font)
		{
			var item = _inventory?.LoadoutItem(index);
			int count = item == null ? 0 : _inventory.Count(item.Id);
			bool heldLeft = _inventory != null && _inventory.LeftLoadout == index;
			bool heldRight = _inventory != null && _inventory.RightLoadout == index;
			bool held = heldLeft || heldRight;

			DrawRect(rect, held ? SelectedFill : SlotFill, filled: true);
			DrawRect(rect, held ? SelectedEdge : SlotEdge,
				filled: false, width: held ? 2f : 1f, antialiased: true);
			DrawString(font, rect.Position + new Vector2(6f, 16f), (index + 1).ToString(),
				HorizontalAlignment.Left, -1f, 12, Muted);

			if (item != null)
			{
				ItemIconRenderer.Draw(this, item, rect.GetCenter() + Vector2.Down * 2f,
					0.76f, count > 0 ? 1f : 0.32f);
				if (count > 1)
					DrawString(font, new Vector2(rect.Position.X + 5f, rect.End.Y - 6f),
						count.ToString(), HorizontalAlignment.Right, rect.Size.X - 10f,
						12, Text);
			}

			string hands = heldLeft && heldRight ? "L R" : heldLeft ? "L" : heldRight ? "R" : "";
			if (hands.Length > 0)
				DrawString(font, rect.Position + new Vector2(0f, 16f), hands,
					HorizontalAlignment.Right, rect.Size.X - 6f, 12, Text);
		}

		public override void _GuiInput(InputEvent inputEvent)
		{
			if (inputEvent is not InputEventMouseButton mouse ||
				mouse.ButtonIndex != MouseButton.Left || !mouse.Pressed) return;

			for (int i = 0; i < GlobalInventory.Capacity; i++)
			{
				if (!InventoryRect(i).HasPoint(mouse.Position)) continue;
				_selected = i;
				var item = SelectedItem();
				if (item?.Equipable == true)
					_inventory.EquipItem(item.Id,
						mouse.ShiftPressed ? ItemHand.Left : ItemHand.Right);
				QueueRedraw();
				AcceptEvent();
				return;
			}

			for (int i = 0; i < GlobalInventory.LoadoutCapacity; i++)
			{
				if (!QuickRect(i).HasPoint(mouse.Position)) continue;
				_inventory?.SelectForHand(i,
					mouse.ShiftPressed ? ItemHand.Left : ItemHand.Right);
				QueueRedraw();
				AcceptEvent();
				return;
			}
		}

		public void EquipSelected(ItemHand hand)
		{
			var item = SelectedItem();
			if (item?.Equipable == true) _inventory?.EquipItem(item.Id, hand);
		}

		public void AssignSelectedToLoadout(int index)
		{
			var item = SelectedItem();
			if (item?.Equipable == true) _inventory?.AssignLoadout(index, item.Id);
		}

		private ItemDefinition SelectedItem()
		{
			if (_inventory == null || _selected < 0 || _selected >= _inventory.Slots.Length)
				return null;
			var slot = _inventory.Slots[_selected];
			return slot.Empty ? null : slot.Item;
		}

		private Rect2 InventoryRect(int index)
		{
			float width = Columns * SlotSize + (Columns - 1) * SlotGap;
			float left = (Size.X - width) * 0.5f;
			int column = index % Columns;
			int row = index / Columns;
			return new Rect2(left + column * (SlotSize + SlotGap),
				GridTop + row * (SlotSize + SlotGap), SlotSize, SlotSize);
		}

		private Rect2 QuickRect(int index)
		{
			float width = GlobalInventory.LoadoutCapacity * QuickSize +
				(GlobalInventory.LoadoutCapacity - 1) * QuickGap;
			float left = (Size.X - width) * 0.5f;
			float top = GridTop + Rows * SlotSize + (Rows - 1) * SlotGap + 49f;
			return new Rect2(left + index * (QuickSize + QuickGap), top, QuickSize, QuickSize);
		}
	}
}
