using Godot;
using Petalfell.Items;

namespace Petalfell.UI;

/// <summary>
/// Resolution-independent inventory iconography shared by the quiet in-world HUD
/// and the full inventory. Items keep their visual identity without introducing a
/// texture atlas before the production art pipeline exists.
/// </summary>
public static class ItemIconRenderer
{
	private static Color WithAlpha(Color color, float opacity) =>
		new(color.R, color.G, color.B, color.A * opacity);

	public static void Draw(CanvasItem canvas, ItemDefinition item, Vector2 centre,
		float scale = 1f, float opacity = 1f)
	{
		if (canvas == null || item == null || opacity <= 0f) return;

		switch (item.Id)
		{
			case "stick":
				DrawStick(canvas, centre, scale, opacity);
				break;
			case "wood":
				DrawWood(canvas, centre, scale, opacity);
				break;
			case "torch":
				DrawTorch(canvas, centre, scale, opacity);
				break;
			default:
				DrawGeneric(canvas, item, centre, scale, opacity);
				break;
		}
	}

	private static void DrawStick(CanvasItem canvas, Vector2 centre, float scale, float opacity)
	{
		var axis = new Vector2(0.68f, -0.73f);
		var a = centre - axis * (17f * scale);
		var b = centre + axis * (17f * scale);
		var outline = WithAlpha(new Color(0.29f, 0.22f, 0.27f, 0.86f), opacity);
		var wood = WithAlpha(new Color(0.62f, 0.40f, 0.27f, 0.98f), opacity);
		var highlight = WithAlpha(new Color(0.76f, 0.53f, 0.35f, 0.82f), opacity);

		canvas.DrawLine(a, b, outline, 12f * scale, antialiased: true);
		canvas.DrawLine(a, b, wood, 8f * scale, antialiased: true);
		canvas.DrawLine(a + new Vector2(scale, -scale), b + new Vector2(scale, -scale),
			highlight, 2f * scale, antialiased: true);
		canvas.DrawCircle(a, 4f * scale, wood, filled: true, width: -1f, antialiased: true);
		canvas.DrawCircle(b, 4f * scale, wood, filled: true, width: -1f, antialiased: true);

		var branchRoot = centre + axis * (5f * scale);
		var branchEnd = branchRoot + new Vector2(8f, 2f) * scale;
		canvas.DrawLine(branchRoot, branchEnd, outline, 7f * scale, antialiased: true);
		canvas.DrawLine(branchRoot, branchEnd, wood, 4f * scale, antialiased: true);
	}

	private static void DrawWood(CanvasItem canvas, Vector2 centre, float scale, float opacity)
	{
		var outline = WithAlpha(new Color(0.28f, 0.22f, 0.27f, 0.88f), opacity);
		var wood = WithAlpha(new Color(0.66f, 0.43f, 0.28f, 0.98f), opacity);
		var light = WithAlpha(new Color(0.80f, 0.60f, 0.39f, 0.94f), opacity);
		float half = 14f * scale;
		float width = 10f * scale;

		for (int row = -1; row <= 1; row++)
		{
			float y = centre.Y + row * 9f * scale;
			var a = new Vector2(centre.X - half, y + 2f * scale);
			var b = new Vector2(centre.X + half, y - 2f * scale);
			canvas.DrawLine(a, b, outline, width + 4f * scale, antialiased: true);
			canvas.DrawLine(a, b, wood, width, antialiased: true);
			canvas.DrawLine(a + Vector2.Up * (2f * scale), b + Vector2.Up * (2f * scale),
				light, 1.7f * scale, antialiased: true);
			canvas.DrawCircle(a, width * 0.34f, light, filled: true, width: -1f, antialiased: true);
		}
	}

	private static void DrawTorch(CanvasItem canvas, Vector2 centre, float scale, float opacity)
	{
		var outline = WithAlpha(new Color(0.27f, 0.21f, 0.27f, 0.90f), opacity);
		var shaft = WithAlpha(new Color(0.54f, 0.32f, 0.22f, 0.98f), opacity);
		var wrap = WithAlpha(new Color(0.80f, 0.68f, 0.51f, 0.94f), opacity);
		var ember = WithAlpha(new Color(1.00f, 0.48f, 0.20f, 0.98f), opacity);
		var flame = WithAlpha(new Color(1.00f, 0.80f, 0.38f, 0.98f), opacity);

		var bottom = centre + Vector2.Down * (17f * scale);
		var top = centre + Vector2.Up * (8f * scale);
		canvas.DrawLine(bottom, top, outline, 11f * scale, antialiased: true);
		canvas.DrawLine(bottom, top, shaft, 7f * scale, antialiased: true);
		canvas.DrawLine(top + Vector2.Down * (3f * scale), top + Vector2.Up * (3f * scale),
			wrap, 10f * scale, antialiased: true);

		var fire = top + Vector2.Up * (9f * scale);
		canvas.DrawCircle(fire + Vector2.Down * (2f * scale), 9f * scale,
			ember, filled: true, width: -1f, antialiased: true);
		canvas.DrawCircle(fire + Vector2.Up * (2f * scale), 6f * scale,
			flame, filled: true, width: -1f, antialiased: true);
		canvas.DrawLine(fire + Vector2.Down * (2f * scale), fire + Vector2.Up * (11f * scale),
			flame, 5f * scale, antialiased: true);
	}

	private static void DrawGeneric(CanvasItem canvas, ItemDefinition item, Vector2 centre,
		float scale, float opacity)
	{
		var baseColor = WithAlpha(item.Color, opacity);
		var edge = WithAlpha(new Color(0.30f, 0.27f, 0.34f, 0.78f), opacity);
		canvas.DrawCircle(centre, 14f * scale, edge,
			filled: true, width: -1f, antialiased: true);
		canvas.DrawCircle(centre, 10f * scale, baseColor,
			filled: true, width: -1f, antialiased: true);
	}
}
