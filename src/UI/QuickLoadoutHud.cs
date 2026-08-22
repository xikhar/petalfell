using Godot;
using Petalfell.Items;

namespace Petalfell.UI;

/// <summary>
/// The only persistent inventory UI: four quiet quick slots. Full storage can
/// later live in a separate inventory screen without changing this component.
/// </summary>
public partial class QuickLoadoutHud : CanvasLayer
{
	private readonly PanelContainer[] _panels = new PanelContainer[GlobalInventory.LoadoutCapacity];
	private readonly Label[] _names = new Label[GlobalInventory.LoadoutCapacity];
	private readonly Label[] _hands = new Label[GlobalInventory.LoadoutCapacity];
	private GlobalInventory _inventory;

	public void Setup(GlobalInventory inventory) => _inventory = inventory;

	public override void _Ready()
	{
		Layer = 220;
		var root = new Control
		{
			Name = "QuickLoadoutRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			AnchorTop = 1f,
			AnchorBottom = 1f,
			OffsetLeft = -212f,
			OffsetRight = 212f,
			OffsetTop = -76f,
			OffsetBottom = -18f,
		};
		AddChild(root);

		var row = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		row.AddThemeConstantOverride("separation", 8);
		root.AddChild(row);

		for (int i = 0; i < GlobalInventory.LoadoutCapacity; i++)
		{
			var panel = new PanelContainer
			{
				CustomMinimumSize = new Vector2(100f, 58f),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			_panels[i] = panel;
			row.AddChild(panel);

			var margin = new MarginContainer();
			margin.AddThemeConstantOverride("margin_left", 9);
			margin.AddThemeConstantOverride("margin_top", 6);
			margin.AddThemeConstantOverride("margin_right", 9);
			margin.AddThemeConstantOverride("margin_bottom", 5);
			panel.AddChild(margin);

			var stack = new VBoxContainer();
			stack.AddThemeConstantOverride("separation", 0);
			margin.AddChild(stack);

			var header = new HBoxContainer();
			stack.AddChild(header);
			var number = new Label { Text = (i + 1).ToString() };
			number.AddThemeFontSizeOverride("font_size", 11);
			number.AddThemeColorOverride("font_color", new Color(0.25f, 0.25f, 0.30f, 0.58f));
			header.AddChild(number);
			_hands[i] = new Label
			{
				Text = "",
				HorizontalAlignment = HorizontalAlignment.Right,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			_hands[i].AddThemeFontSizeOverride("font_size", 11);
			header.AddChild(_hands[i]);

			_names[i] = new Label
			{
				Text = "—",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			_names[i].AddThemeFontSizeOverride("font_size", 14);
			_names[i].AddThemeColorOverride("font_color", new Color(0.18f, 0.18f, 0.23f, 0.92f));
			stack.AddChild(_names[i]);
		}

		if (_inventory != null) _inventory.Changed += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		if (_inventory != null) _inventory.Changed -= Refresh;
	}

	private void Refresh()
	{
		if (_inventory == null || _panels[0] == null) return;
		for (int i = 0; i < GlobalInventory.LoadoutCapacity; i++)
		{
			var item = _inventory.LoadoutItem(i);
			int quantity = item == null ? 0 : _inventory.Count(item.Id);
			_names[i].Text = item == null ? "—" : $"{item.Name}  {quantity}";

			bool left = _inventory.LeftLoadout == i;
			bool right = _inventory.RightLoadout == i;
			_hands[i].Text = left && right ? "L  R" : left ? "L" : right ? "R" : "";
			_hands[i].AddThemeColorOverride("font_color",
				left ? new Color(0.48f, 0.35f, 0.49f, 0.92f)
				     : new Color(0.27f, 0.42f, 0.48f, 0.92f));

			Color border = left && right
				? new Color(0.52f, 0.43f, 0.58f, 0.78f)
				: left
					? new Color(0.61f, 0.47f, 0.59f, 0.72f)
					: right
						? new Color(0.42f, 0.59f, 0.65f, 0.72f)
						: new Color(1f, 1f, 1f, 0.42f);
			_panels[i].AddThemeStyleboxOverride("panel", MakePanel(border,
				item == null || quantity == 0 ? 0.34f : 0.70f));
		}
	}

	private static StyleBoxFlat MakePanel(Color border, float opacity)
	{
		return new StyleBoxFlat
		{
			BgColor = new Color(0.98f, 0.98f, 1f, opacity),
			BorderColor = border,
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 9,
			CornerRadiusTopRight = 9,
			CornerRadiusBottomLeft = 9,
			CornerRadiusBottomRight = 9,
		};
	}
}

