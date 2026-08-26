using Godot;
using Petalfell.Items;

namespace Petalfell.UI;

/// <summary>
/// Four translucent sockets in the lower-left corner. Left and right mirror
/// the traveller's hands; top and bottom are intentionally reserved for the
/// future consumable layer rather than pretending to be inventory slots now.
/// </summary>
public partial class QuickLoadoutHud : CanvasLayer
{
	private GlobalInventory _inventory;

	public void Setup(GlobalInventory inventory) => _inventory = inventory;

	public override void _Ready()
	{
		Layer = 220;
		var display = new LoadoutCross
		{
			Name = "HandLoadout",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			AnchorLeft = 0f,
			AnchorRight = 0f,
			AnchorTop = 1f,
			AnchorBottom = 1f,
			OffsetLeft = 24f,
			OffsetRight = 188f,
			OffsetTop = -192f,
			OffsetBottom = -28f,
		};
		display.Setup(_inventory);
		AddChild(display);
	}

	private partial class LoadoutCross : Control
	{
		private const float Radius = 30f;
		private const float Offset = 50f;
		private static readonly Vector2 Centre = new(82f, 82f);

		private GlobalInventory _inventory;

		public void Setup(GlobalInventory inventory) => _inventory = inventory;

		public override void _Ready()
		{
			if (_inventory != null) _inventory.Changed += QueueRedraw;
			QueueRedraw();
		}

		public override void _ExitTree()
		{
			if (_inventory != null) _inventory.Changed -= QueueRedraw;
		}

		public override void _Draw()
		{
			var left = Centre + Vector2.Left * Offset;
			var right = Centre + Vector2.Right * Offset;
			var top = Centre + Vector2.Up * Offset;
			var bottom = Centre + Vector2.Down * Offset;

			var leftItem = _inventory?.HeldItem(ItemHand.Left);
			var rightItem = _inventory?.HeldItem(ItemHand.Right);
			DrawSocket(top, occupied: false);
			DrawSocket(bottom, occupied: false);
			DrawSocket(left, leftItem != null);
			DrawSocket(right, rightItem != null);
			DrawItem(left, leftItem, ItemHand.Left);
			DrawItem(right, rightItem, ItemHand.Right);
		}

		private void DrawSocket(Vector2 centre, bool occupied)
		{
			var fill = new Color(0.96f, 0.96f, 1f, occupied ? 0.17f : 0.09f);
			var edge = new Color(0.98f, 0.98f, 1f, occupied ? 0.72f : 0.48f);
			DrawCircle(centre, Radius, fill, filled: true, width: -1f, antialiased: true);
			DrawArc(centre, Radius, 0f, Mathf.Tau, 64, edge, 1.65f, antialiased: true);
		}

		private void DrawItem(Vector2 centre, ItemDefinition item, ItemHand hand)
		{
			if (item == null) return;
			ItemIconRenderer.Draw(this, item, centre);

			int count = _inventory?.Count(item.Id) ?? 0;
			if (count <= 1) return;
			var font = ThemeDB.FallbackFont;
			var origin = centre + new Vector2(-Radius + 5f, Radius - 6f);
			DrawString(font, origin, count.ToString(), HorizontalAlignment.Right,
				Radius * 2f - 10f, 13, new Color(1f, 1f, 1f, 0.90f));
		}
	}
}
