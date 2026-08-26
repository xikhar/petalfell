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
			case "fishing_rod":
				DrawFishingRod(canvas, centre, scale, opacity);
				break;
			case "silver_minnow":
			case "rosefin":
			case "moon_carp":
				DrawFish(canvas, item, centre, scale, opacity);
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

	private static void DrawFishingRod(CanvasItem canvas, Vector2 centre,
		float scale, float opacity)
	{
		var edge = WithAlpha(new Color(0.27f, 0.23f, 0.28f, 0.88f), opacity);
		var wood = WithAlpha(new Color(0.56f, 0.40f, 0.29f, 0.96f), opacity);
		var line = WithAlpha(new Color(0.88f, 0.89f, 0.92f, 0.72f), opacity);
		var reel = WithAlpha(new Color(0.72f, 0.68f, 0.62f, 0.95f), opacity);
		var a = centre + new Vector2(-15f, 19f) * scale;
		var b = centre + new Vector2(11f, -20f) * scale;
		var mid = centre + new Vector2(4f, -2f) * scale;
		canvas.DrawLine(a, mid, edge, 7f * scale, antialiased: true);
		canvas.DrawLine(mid, b, edge, 5f * scale, antialiased: true);
		canvas.DrawLine(a, mid, wood, 4f * scale, antialiased: true);
		canvas.DrawLine(mid, b, wood.Lightened(0.12f), 2.4f * scale, antialiased: true);
		canvas.DrawCircle(a + new Vector2(7f, -6f) * scale, 5f * scale,
			reel, filled: true, width: -1f, antialiased: true);
		canvas.DrawLine(b, b + new Vector2(2f, 13f) * scale,
			line, 1.2f * scale, antialiased: true);
	}

	private static void DrawFish(CanvasItem canvas, ItemDefinition item, Vector2 centre,
		float scale, float opacity)
	{
		var edge = WithAlpha(new Color(0.28f, 0.25f, 0.32f, 0.82f), opacity);
		var body = WithAlpha(item.Color.Lightened(0.12f), opacity);
		var fin = WithAlpha(item.Color.Darkened(0.08f), opacity);
		Vector2[] silhouette =
		{
			centre + new Vector2(-17f, 0f) * scale,
			centre + new Vector2(-9f, -10f) * scale,
			centre + new Vector2(8f, -9f) * scale,
			centre + new Vector2(17f, 0f) * scale,
			centre + new Vector2(8f, 9f) * scale,
			centre + new Vector2(-9f, 10f) * scale,
		};
		canvas.DrawColoredPolygon(silhouette, edge);
		Vector2[] inner =
		{
			centre + new Vector2(-12f, 0f) * scale,
			centre + new Vector2(-6f, -7f) * scale,
			centre + new Vector2(8f, -6f) * scale,
			centre + new Vector2(13f, 0f) * scale,
			centre + new Vector2(8f, 6f) * scale,
			centre + new Vector2(-6f, 7f) * scale,
		};
		canvas.DrawColoredPolygon(inner, body);
		Vector2[] tail =
		{
			centre + new Vector2(-11f, 0f) * scale,
			centre + new Vector2(-22f, -10f) * scale,
			centre + new Vector2(-21f, 10f) * scale,
		};
		canvas.DrawColoredPolygon(tail, fin);
		canvas.DrawCircle(centre + new Vector2(8f, -2f) * scale, 1.8f * scale,
			WithAlpha(new Color(0.22f, 0.20f, 0.27f), opacity),
			filled: true, width: -1f, antialiased: true);
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
