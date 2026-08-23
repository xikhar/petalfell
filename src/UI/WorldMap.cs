using System;
using Godot;
using Petalfell.Core;
using Petalfell.World;

namespace Petalfell.UI;

/// <summary>
/// The world map.
///
/// Everything on it is read back out of the finished world rather than kept as
/// a parallel description of it: the coastline is the columns that came out
/// below the waterline, the roads are the road mask, the provinces are the cell
/// biome field. So the map cannot drift out of step with the place it depicts —
/// if a road is drawn here it is because there is trodden ground there, and a
/// map that lies about the terrain is worse than no map at all.
///
/// Rendered once, on first open, at one pixel per block. A 768-block chapter is
/// a 590k-pixel image and takes a moment to build, which is unacceptable during
/// the boot the player is waiting on and unnoticeable behind the keypress that
/// asks for it.
/// </summary>
public partial class WorldMap : CanvasLayer
{
	private Terrain _terrain;
	private Canvas _canvas;
	private ImageTexture _texture;
	private Vector3 _player;
	private bool _open;

	public bool IsOpen => _open;

	public void Setup(Terrain terrain)
	{
		_terrain = terrain;
		// Above the loadout HUD, which sits at 220 and would otherwise show
		// through the map's own dimming.
		Layer = 240;
		Visible = false;

		var dim = new ColorRect { Color = new Color(0.10f, 0.09f, 0.14f, 0.86f) };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		dim.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(dim);

		_canvas = new Canvas { Owner_ = this };
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_canvas);

		var hint = new Label
		{
			Text = "M  close     scroll  zoom     drag  pan",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		hint.AnchorLeft = 0.5f; hint.AnchorRight = 0.5f;
		hint.OffsetLeft = -160; hint.OffsetTop = -46; hint.OffsetRight = 160;
		hint.HorizontalAlignment = HorizontalAlignment.Center;
		hint.Modulate = new Color(1, 1, 1, 0.55f);
		AddChild(hint);
	}

	public void SetPlayer(Vector3 position) => _player = position;

	public void Toggle()
	{
		_open = !_open;
		Visible = _open;
		if (!_open) return;
		if (_texture == null)
		{
			var image = Render(_terrain);
			_texture = ImageTexture.CreateFromImage(image);
			_canvas.Texture = _texture;
			_canvas.Terrain = _terrain;
			_canvas.Reset();
		}
		_canvas.Player = _player;
		_canvas.QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (!_open) return;
		_canvas.Player = _player;
		_canvas.QueueRedraw();
	}

	/* ================================================================
	 * Cartography
	 * ================================================================ */

	private static Color BiomeColour(Biome b) => b switch
	{
		Biome.Meadow => new Color(0.79f, 0.83f, 0.56f),
		Biome.Plains => new Color(0.85f, 0.84f, 0.60f),
		Biome.Forest => new Color(0.62f, 0.72f, 0.55f),
		Biome.Sakura => new Color(0.89f, 0.74f, 0.80f),
		Biome.Highland => new Color(0.76f, 0.73f, 0.82f),
		Biome.SnowyHills => new Color(0.93f, 0.93f, 0.97f),
		Biome.Shore => new Color(0.91f, 0.87f, 0.75f),
		Biome.Wetland => new Color(0.67f, 0.72f, 0.62f),
		_ => new Color(0.8f, 0.8f, 0.8f),
	};

	/// <summary>
	/// Draw the world. Pass markers for a standalone export — the live map draws
	/// its own on top, at a size that survives zooming.
	/// </summary>
	public static Image Render(Terrain t, bool markers = false)
	{
		int S = t.Size;
		var img = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);

		var shallow = new Color(0.62f, 0.68f, 0.90f);
		var deep = new Color(0.24f, 0.28f, 0.62f);
		var majorRoad = new Color(0.97f, 0.94f, 0.98f);
		var localRoad = new Color(0.90f, 0.86f, 0.92f);
		var trailRoad = new Color(0.76f, 0.56f, 0.48f);

		for (int z = 0; z < S; z++)
		for (int x = 0; x < S; x++)
		{
			int i = z * S + x;
			int h = t.Level[i];
			Color c;

			if (t.Land[i] == 0)
			{
				c = shallow.Lerp(deep, Mathf.Clamp((Terrain.Sea - h) / 20f, 0f, 1f));
			}
			else
			{
				c = BiomeColour(t.Plan.RegionAt(x, z).Biome);

				// Relief shading off the height gradient. Without it a map of a
				// terraced world is flat colour, and the terraces are the single
				// most useful thing on it — they are what the player has to walk
				// around.
				int hx = t.Level[i + (x + 1 < S ? 1 : 0)];
				int hz = t.Level[i + (z + 1 < S ? S : 0)];
				float slope = (h - hx) * 0.6f + (h - hz) * 0.4f;
				float shade = Mathf.Clamp(0.5f + slope * 0.22f, 0f, 1f);
				c = c.Lerp(shade > 0.5f ? Colors.White : Colors.Black,
					Mathf.Abs(shade - 0.5f) * 0.62f);

				// A faint banding by terrace, so height is readable in flat country
				// where there is no gradient to shade.
				int terrace = (h - Terrain.Base) / Terrain.Step;
				if (terrace % 2 == 0) c = c.Darkened(0.045f);
			}

			if (t.Roads != null && t.Roads.Mask[i] != 0 && t.Land[i] != 0)
				c = t.Roads.Mask[i] switch
				{
					(byte)RoadClass.Major + 1 => majorRoad,
					(byte)RoadClass.Local + 1 => localRoad,
					_ => trailRoad,
				};

			img.SetPixel(x, z, c);
		}

		if (markers)
			foreach (var site in t.Sites)
			{
				int r = site.Kind switch
				{
					SettlementKind.Town => 5,
					SettlementKind.Village => 4,
					_ => 3,
				};
				var fill = site.Kind == SettlementKind.Town ? new Color(0.99f, 0.80f, 0.36f)
					: site.Kind == SettlementKind.Village ? new Color(0.97f, 0.66f, 0.60f)
					: new Color(0.80f, 0.76f, 0.82f);
				for (int dz = -r; dz <= r; dz++)
				for (int dx = -r; dx <= r; dx++)
				{
					int x = site.X + dx, z = site.Z + dz;
					if (x < 0 || z < 0 || x >= S || z >= S) continue;
					float d = MathF.Sqrt(dx * dx + dz * dz);
					if (d > r) continue;
					img.SetPixel(x, z, d > r - 1.6f ? new Color(0.16f, 0.13f, 0.18f) : fill);
				}
			}
		return img;
	}

	/* ================================================================
	 * The interactive surface
	 * ================================================================ */
	private sealed partial class Canvas : Control
	{
		public Node Owner_;
		public Texture2D Texture;
		public Terrain Terrain;
		public Vector3 Player;

		private float _zoom = 1f;
		private Vector2 _pan;
		private bool _dragging;

		public Canvas() => MouseFilter = MouseFilterEnum.Stop;

		public void Reset()
		{
			_zoom = 1f;
			_pan = Vector2.Zero;
		}

		/// <summary>The rectangle the map image occupies, in this control's space.</summary>
		private Rect2 MapRect()
		{
			Vector2 size = Size;
			float fit = Mathf.Min(size.X, size.Y) * 0.88f * _zoom;
			var centre = size * 0.5f + _pan;
			return new Rect2(centre - new Vector2(fit, fit) * 0.5f, new Vector2(fit, fit));
		}

		private Vector2 ToMap(float worldX, float worldZ)
		{
			var r = MapRect();
			int s = Terrain?.Size ?? 1;
			return r.Position + new Vector2(worldX / s * r.Size.X, worldZ / s * r.Size.Y);
		}

		public override void _GuiInput(InputEvent e)
		{
			if (e is InputEventMouseButton mb)
			{
				if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
					_zoom = Mathf.Min(_zoom * 1.18f, 8f);
				else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
				{
					_zoom = Mathf.Max(_zoom / 1.18f, 1f);
					if (_zoom <= 1.001f) _pan = Vector2.Zero;
				}
				else if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
				QueueRedraw();
			}
			else if (e is InputEventMouseMotion mm && _dragging)
			{
				_pan += mm.Relative;
				QueueRedraw();
			}
		}

		public override void _Draw()
		{
			if (Texture == null || Terrain == null) return;
			var r = MapRect();

			DrawRect(r.Grow(3f), new Color(0.16f, 0.14f, 0.20f));
			DrawTextureRect(Texture, r, false);

			// Settlements. Size carries rank, because on a map of this scale a
			// town and a hamlet are both one dot unless something says otherwise.
			foreach (var site in Terrain.Sites)
			{
				var p = ToMap(site.X, site.Z);
				float rad = site.Kind switch
				{
					SettlementKind.Town => 6.5f,
					SettlementKind.Village => 5f,
					_ => 3.5f,
				};
				DrawCircle(p, rad + 1.6f, new Color(0.16f, 0.13f, 0.18f, 0.85f));
				DrawCircle(p, rad, site.Kind == SettlementKind.Town
					? new Color(0.99f, 0.86f, 0.55f)
					: site.Kind == SettlementKind.Village
						? new Color(0.97f, 0.78f, 0.72f)
						: new Color(0.86f, 0.82f, 0.86f));
			}

			// The player, last and on top of everything.
			var me = ToMap(Player.X, Player.Z);
			DrawCircle(me, 7.5f, new Color(0.10f, 0.09f, 0.13f, 0.9f));
			DrawCircle(me, 5.2f, new Color(0.42f, 0.72f, 0.78f));
			DrawCircle(me, 2.2f, Colors.White);
		}
	}
}
