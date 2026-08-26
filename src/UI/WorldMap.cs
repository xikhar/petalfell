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

	/// <summary>
	/// Shift-click on the map: put the traveller down there.
	///
	/// A development convenience and deliberately not hidden behind the developer
	/// menu, because on a continent this size the alternative to it is walking for
	/// twenty minutes to look at one ruin. The map already knows where everything
	/// is, so it is the natural place to ask to be somewhere.
	/// </summary>
	public event Action<Vector3> TeleportRequested;

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
		_canvas.Teleport = world => TeleportRequested?.Invoke(world);
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

		// Built as a raw byte buffer, not with SetPixel.
		//
		// At 3456 across that is eleven point nine MILLION calls through a bound
		// method, on the keypress the player is waiting behind. Writing three
		// bytes per pixel into an array and handing the whole thing over once is
		// the same picture for a fraction of the cost.
		//
		// Downsampled as well. A map wider than the screen it is drawn on cannot
		// show its extra pixels, and a continent this size does not need one pixel
		// per block to be legible — the terraces are what matter and they are six
		// blocks wide.
		int step = Math.Max(1, S / 1536);
		int W = S / step;
		var buf = new byte[W * W * 3];

		var shallow = new Color(0.62f, 0.68f, 0.90f);
		var deep = new Color(0.24f, 0.28f, 0.62f);
		var majorRoad = new Color(0.97f, 0.94f, 0.98f);
		var localRoad = new Color(0.90f, 0.86f, 0.92f);
		var trailRoad = new Color(0.76f, 0.56f, 0.48f);

		for (int oz = 0; oz < W; oz++)
		for (int ox = 0; ox < W; ox++)
		{
			int x = ox * step, z = oz * step;
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

			// Roads are thinner than the sampling step, so a straight lookup drops
			// most of the network. Take any road within the cell this pixel stands
			// for — a map that loses the roads loses the only thing on it the
			// player navigates by.
			if (t.Roads != null && t.Land[i] != 0)
			{
				byte road = 0;
				for (int dz = 0; dz < step && road == 0; dz++)
				for (int dx = 0; dx < step; dx++)
				{
					int xx = x + dx, zz = z + dz;
					if (xx >= S || zz >= S) continue;
					byte m = t.Roads.Mask[zz * S + xx];
					if (m != 0 && (road == 0 || m < road)) road = m;
				}
				if (road != 0)
					c = road switch
					{
						(byte)RoadClass.Major + 1 => majorRoad,
						(byte)RoadClass.Local + 1 => localRoad,
						_ => trailRoad,
					};
			}

			int o = (oz * W + ox) * 3;
			buf[o] = (byte)(Mathf.Clamp(c.R, 0f, 1f) * 255f);
			buf[o + 1] = (byte)(Mathf.Clamp(c.G, 0f, 1f) * 255f);
			buf[o + 2] = (byte)(Mathf.Clamp(c.B, 0f, 1f) * 255f);
		}

		var img = Image.CreateFromData(W, W, false, Image.Format.Rgb8, buf);

		if (markers)
		{
			void Dot(int wx, int wz, int r, Color fill)
			{
				int px = wx / step, pz = wz / step;
				for (int dz = -r; dz <= r; dz++)
				for (int dx = -r; dx <= r; dx++)
				{
					int x = px + dx, z = pz + dz;
					if (x < 0 || z < 0 || x >= W || z >= W) continue;
					if (dx * dx + dz * dz > r * r) continue;
					img.SetPixel(x, z, dx * dx + dz * dz > (r - 1.4f) * (r - 1.4f)
						? new Color(0.16f, 0.13f, 0.18f) : fill);
				}
			}

			// Landmarks first, so a remnant is never hidden behind a cairn.
			foreach (var m in t.Marks)
			{
				if (m.Form == LandmarkForm.Cairn) continue;
				Dot(m.X, m.Z, 2, LandmarkColour(m.Form));
			}
			foreach (var site in t.Sites)
				Dot(site.X, site.Z, site.Kind == SettlementKind.Town ? 5
					: site.Kind == SettlementKind.Village ? 4 : 3, StateColour(site.State));

			// The review fixture, biggest of all and in the fixture green
			// nothing else on the map uses.
			if (Sanctum.Built) Dot(Sanctum.SiteX, Sanctum.SiteZ, 6, FixtureColour);
		}
		return img;
	}

	/// <summary>
	/// The one colour reserved for development fixtures — a minty green that no
	/// biome, road, state or landmark marker comes near, because the entire job
	/// of these markers is to be found in seconds from anywhere.
	/// </summary>
	private static readonly Color FixtureColour = new(0.45f, 0.96f, 0.70f);

	/// <summary>
	/// Colour by STATE, not by size. On a map of an emptied continent the useful
	/// question is not how big a place was but whether anybody is there — so the
	/// one gold dot on the map is a holdout, and finding it is the point.
	/// </summary>
	private static Color StateColour(RemnantState s) => s switch
	{
		RemnantState.Holdout => new Color(1.00f, 0.84f, 0.36f),
		RemnantState.Remnant => new Color(0.93f, 0.72f, 0.66f),
		RemnantState.Ruin => new Color(0.74f, 0.66f, 0.70f),
		_ => new Color(0.62f, 0.60f, 0.66f),
	};

	private static Color LandmarkColour(LandmarkForm f) => f switch
	{
		LandmarkForm.Watchtower => new Color(0.86f, 0.86f, 0.94f),
		LandmarkForm.StandingStones => new Color(0.72f, 0.68f, 0.84f),
		LandmarkForm.Shrine => new Color(0.72f, 0.88f, 0.92f),
		LandmarkForm.Farmstead => new Color(0.88f, 0.82f, 0.62f),
		_ => new Color(0.78f, 0.76f, 0.78f),
	};

	/* ================================================================
	 * The interactive surface
	 * ================================================================ */
	private sealed partial class Canvas : Control
	{
		public Node Owner_;
		public Texture2D Texture;
		public Terrain Terrain;
		public Vector3 Player;
		public Action<Vector3> Teleport;

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
				else if (mb.ButtonIndex == MouseButton.Left)
				{
					// Shift-click asks to be moved. Plain click still pans, so the
					// two never fight over the same gesture.
					if (mb.Pressed && mb.ShiftPressed && Terrain != null)
					{
						var r = MapRect();
						if (r.HasPoint(mb.Position))
						{
							float nx = (mb.Position.X - r.Position.X) / r.Size.X;
							float nz = (mb.Position.Y - r.Position.Y) / r.Size.Y;
							Teleport?.Invoke(new Vector3(nx * Terrain.Size, 0f, nz * Terrain.Size));
						}
					}
					else _dragging = mb.Pressed;
				}
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

			// The review fixture: a labelled diamond, so it cannot be mistaken
			// for content or missed at any zoom. Shift-click it to be standing
			// in it.
			if (Sanctum.Built) Fixture(Sanctum.SiteX, Sanctum.SiteZ, "sanctum");

			// The player, last and on top of everything.
			var me = ToMap(Player.X, Player.Z);
			DrawCircle(me, 7.5f, new Color(0.10f, 0.09f, 0.13f, 0.9f));
			DrawCircle(me, 5.2f, new Color(0.42f, 0.72f, 0.78f));
			DrawCircle(me, 2.2f, Colors.White);
		}

		private void Fixture(int x, int z, string label)
		{
			var p = ToMap(x, z);
			Vector2[] Diamond(float r) => new[]
			{
				p + new Vector2(0, -r), p + new Vector2(r, 0),
				p + new Vector2(0, r), p + new Vector2(-r, 0),
			};
			DrawColoredPolygon(Diamond(9f), new Color(0.16f, 0.13f, 0.18f));
			DrawColoredPolygon(Diamond(6.5f), FixtureColour);

			var font = ThemeDB.FallbackFont;
			var at = p + new Vector2(12f, 5f);
			// A dark copy one pixel under the label keeps it readable over
			// snowfield and water alike.
			DrawString(font, at + new Vector2(1, 1), label,
				HorizontalAlignment.Left, -1, 14, new Color(0.10f, 0.09f, 0.13f));
			DrawString(font, at, label,
				HorizontalAlignment.Left, -1, 14, FixtureColour);
		}
	}
}
