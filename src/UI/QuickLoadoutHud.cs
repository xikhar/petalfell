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
			DrawItem(left, leftItem);
			DrawItem(right, rightItem);
		}

		private void DrawSocket(Vector2 centre, bool occupied)
		{
			var fill = new Color(0.96f, 0.96f, 1f, occupied ? 0.17f : 0.09f);
			var edge = new Color(0.98f, 0.98f, 1f, occupied ? 0.72f : 0.48f);
			DrawCircle(centre, Radius, fill, filled: true, width: -1f, antialiased: true);
			DrawArc(centre, Radius, 0f, Mathf.Tau, 64, edge, 1.65f, antialiased: true);
		}

		private void DrawItem(Vector2 centre, ItemDefinition item)
		{
			if (item == ItemCatalog.Stick) DrawStick(centre);
		}

		private void DrawStick(Vector2 centre)
		{
			// A tiny vector version of the blocky world stick. Drawing it here keeps
			// the HUD resolution-independent and avoids a one-item texture atlas.
			var axis = new Vector2(0.68f, -0.73f);
			var a = centre - axis * 17f;
			var b = centre + axis * 17f;
			var outline = new Color(0.29f, 0.22f, 0.27f, 0.86f);
			var wood = new Color(0.62f, 0.40f, 0.27f, 0.98f);
			var highlight = new Color(0.76f, 0.53f, 0.35f, 0.82f);

			DrawLine(a, b, outline, 12f, antialiased: true);
			DrawLine(a, b, wood, 8f, antialiased: true);
			DrawLine(a + new Vector2(1f, -1f), b + new Vector2(1f, -1f),
				highlight, 2f, antialiased: true);
			DrawCircle(a, 4f, wood, filled: true, width: -1f, antialiased: true);
			DrawCircle(b, 4f, wood, filled: true, width: -1f, antialiased: true);

			var branchRoot = centre + axis * 5f;
			var branchEnd = branchRoot + new Vector2(8f, 2f);
			DrawLine(branchRoot, branchEnd, outline, 7f, antialiased: true);
			DrawLine(branchRoot, branchEnd, wood, 4f, antialiased: true);
		}
	}
}
