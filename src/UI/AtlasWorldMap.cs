using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Petalfell.World;

namespace Petalfell.UI;

/// <summary>
/// Cartographic surface for the rectangular production atlas.
///
/// This deliberately does not share the legacy <see cref="WorldMap"/> terrain
/// dependency. The old map reads one complete square Terrain array; the atlas
/// map reads compact registered L0/L1 images and permanent L2 topology, so it
/// remains useful while runtime terrain is only a moving sector window.
/// </summary>
public partial class AtlasWorldMap : CanvasLayer
{
	private WorldAtlasDefinition _atlas;
	private string _derivedDirectory;
	private string _sourceFingerprint;
	private Canvas _canvas;
	private ImageTexture _texture;
	private Vector3 _player;
	private bool _open;

	public bool IsOpen => _open;

	/// <summary>
	/// A map click is expressed in permanent atlas coordinates. The runtime owns
	/// whether that address is currently materialised and safe to enter.
	/// </summary>
	public event Action<Vector3> TeleportRequested;

	public void Setup(WorldAtlasDefinition atlas, string derivedDirectory,
		string sourceFingerprint)
	{
		_atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
		_derivedDirectory = derivedDirectory?.TrimEnd('/') ?? "";
		_sourceFingerprint = sourceFingerprint ?? "";
		Layer = 240;
		Visible = false;

		var dim = new ColorRect { Color = new Color(0.10f, 0.09f, 0.14f, 0.86f) };
		dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		dim.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(dim);

		_canvas = new Canvas
		{
			Atlas = _atlas,
			Teleport = world => TeleportRequested?.Invoke(world),
		};
		_canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_canvas);

		var hint = new Label
		{
			Text = "M  close     scroll  zoom     drag  pan     Shift-click  travel anywhere on the atlas",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		hint.AnchorLeft = 0.5f;
		hint.AnchorRight = 0.5f;
		hint.OffsetLeft = -360f;
		hint.OffsetTop = -46f;
		hint.OffsetRight = 360f;
		hint.HorizontalAlignment = HorizontalAlignment.Center;
		hint.Modulate = new Color(1f, 1f, 1f, .58f);
		AddChild(hint);
	}

	public void SetPlayer(Vector3 globalAtlasPosition) => _player = globalAtlasPosition;

	public void Toggle()
	{
		_open = !_open;
		Visible = _open;
		if (!_open) return;
		if (_texture == null)
		{
			Image image = RenderBackground(_atlas, _derivedDirectory,
				_sourceFingerprint, out string source);
			_texture = ImageTexture.CreateFromImage(image);
			_canvas.Texture = _texture;
			_canvas.Reset();
			GD.Print($"[atlas-map] background {image.GetWidth()}x{image.GetHeight()} from {source}");
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

	/// <summary>
	/// Prefer a complete, current compiler composite, but never make the map
	/// depend on that disposable cache. The registered land/region/water/elevation
	/// images are sufficient to reconstruct a truthful cartographic background.
	/// </summary>
	public static Image RenderBackground(WorldAtlasDefinition atlas,
		string derivedDirectory, string sourceFingerprint, out string source)
	{
		if (atlas == null) throw new ArgumentNullException(nameof(atlas));
		derivedDirectory = derivedDirectory?.TrimEnd('/') ?? "";
		if (TryLoadCurrentComposite(atlas, derivedDirectory, sourceFingerprint,
			out Image profile, out Image height, out string compositePath,
			out string unavailable))
		{
			source = $"current derived composite '{compositePath}'";
			return ShadeBackground(profile, height, null, null);
		}

		Image land = LoadLayer(atlas, AtlasLayerKind.Land);
		Image region = LoadLayer(atlas, AtlasLayerKind.Region);
		Image water = LoadLayer(atlas, AtlasLayerKind.Water);
		Image elevation = LoadLayer(atlas, AtlasLayerKind.Elevation);
		Image basis = region ?? land ?? water ?? elevation;
		if (basis == null)
			throw new InvalidOperationException(
				"production atlas map has neither a current derived composite nor a readable registered source layer");
		ValidateLayerSize(atlas, basis, "map basis");
		foreach ((Image image, string label) in new[]
		         {
			         (land, "land"), (region, "region"), (water, "water"),
			         (elevation, "elevation"),
		         })
			if (image != null) ValidateMatchingSize(basis, image, label);

		source = $"registered atlas layers (derived unavailable: {unavailable})";
		return ShadeBackground(region, elevation, land, water);
	}

	private static bool TryLoadCurrentComposite(WorldAtlasDefinition atlas,
		string derivedDirectory, string sourceFingerprint, out Image profile,
		out Image height, out string profilePath, out string unavailable)
	{
		profile = null;
		height = null;
		profilePath = "";
		if (string.IsNullOrWhiteSpace(derivedDirectory))
		{
			unavailable = "no derived directory";
			return false;
		}
		string manifestPath = $"{derivedDirectory}/atlas-manifest.json";
		if (!Godot.FileAccess.FileExists(manifestPath))
		{
			unavailable = "batch manifest missing";
			return false;
		}

		try
		{
			using var manifestFile = Godot.FileAccess.Open(manifestPath,
				Godot.FileAccess.ModeFlags.Read);
			if (manifestFile == null)
			{
				unavailable = $"cannot open batch manifest ({Godot.FileAccess.GetOpenError()})";
				return false;
			}
			using JsonDocument manifest = JsonDocument.Parse(manifestFile.GetAsText());
			JsonElement root = manifest.RootElement;
			if (root.GetProperty("compilerVersion").GetInt32() != AtlasSectorCompiler.CompilerVersion ||
			    root.GetProperty("atlasId").GetString() != atlas.Id ||
			    root.GetProperty("width").GetInt32() != atlas.Width ||
			    root.GetProperty("depth").GetInt32() != atlas.Depth ||
			    (!string.IsNullOrEmpty(sourceFingerprint) &&
			     root.GetProperty("sourceFingerprint").GetString() != sourceFingerprint))
			{
				unavailable = "batch manifest is stale";
				return false;
			}

			string profileFile = null;
			string heightFile = null;
			foreach (JsonElement composite in root.GetProperty("composites").EnumerateArray())
			{
				string kind = composite.GetProperty("kind").GetString();
				if (kind == "profile") profileFile = composite.GetProperty("file").GetString();
				else if (kind == "height") heightFile = composite.GetProperty("file").GetString();
			}
			if (string.IsNullOrWhiteSpace(profileFile))
			{
				unavailable = "batch manifest has no profile composite";
				return false;
			}

			profilePath = $"{derivedDirectory}/{profileFile}";
			profile = LoadPng(profilePath);
			if (profile == null)
			{
				unavailable = $"profile composite '{profileFile}' missing";
				return false;
			}
			if (!string.IsNullOrWhiteSpace(heightFile))
			{
				string path = $"{derivedDirectory}/{heightFile}";
				height = LoadPng(path);
				if (height != null) ValidateMatchingSize(profile, height, "derived height");
			}
			unavailable = "";
			return true;
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
		{
			profile = null;
			height = null;
			unavailable = $"invalid batch manifest: {ex.Message}";
			return false;
		}
	}

	private static Image LoadLayer(WorldAtlasDefinition atlas, AtlasLayerKind kind)
	{
		AtlasSourceLayer layer = atlas.SourceLayers.FirstOrDefault(candidate =>
			candidate.Kind == kind && candidate.Status != AtlasLayerStatus.Planned);
		return layer == null ? null : LoadPng(layer.Path);
	}

	private static Image LoadPng(string resourcePath)
	{
		if (string.IsNullOrWhiteSpace(resourcePath) ||
		    !Godot.FileAccess.FileExists(resourcePath)) return null;
		using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
		if (file == null) return null;
		var image = new Image();
		Error error = image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength()));
		return error == Error.Ok && !image.IsEmpty() ? image : null;
	}

	private static void ValidateLayerSize(WorldAtlasDefinition atlas, Image image, string label)
	{
		int width = atlas.Width / atlas.BlocksPerPixel;
		int depth = atlas.Depth / atlas.BlocksPerPixel;
		if (image.GetWidth() != width || image.GetHeight() != depth)
			throw new InvalidOperationException(
				$"atlas {label} is {image.GetWidth()}x{image.GetHeight()}, expected {width}x{depth}");
	}

	private static void ValidateMatchingSize(Image basis, Image image, string label)
	{
		if (basis.GetWidth() != image.GetWidth() || basis.GetHeight() != image.GetHeight())
			throw new InvalidOperationException(
				$"atlas {label} is {image.GetWidth()}x{image.GetHeight()}, expected " +
				$"{basis.GetWidth()}x{basis.GetHeight()}");
	}

	private static Image ShadeBackground(Image colour, Image elevation,
		Image land, Image water)
	{
		Image basis = colour ?? land ?? water ?? elevation;
		int width = basis.GetWidth();
		int depth = basis.GetHeight();
		byte[] pixels = new byte[width * depth * 3];
		var ocean = new Color(.42f, .57f, .76f);
		var channel = new Color(.48f, .65f, .82f);
		var neutralLand = new Color(.72f, .72f, .62f);

		for (int z = 0; z < depth; z++)
		for (int x = 0; x < width; x++)
		{
			bool isLand = land == null || land.GetPixel(x, z).R >= .5f;
			float hydrology = water?.GetPixel(x, z).R ?? 0f;
			Color c = colour?.GetPixel(x, z) ?? neutralLand;
			if (!isLand) c = ocean;
			else if (hydrology >= 240f / 255f) c = c.Lerp(channel, .84f);
			else if (hydrology > .05f) c = c.Lerp(channel, hydrology * .18f);

			if (elevation != null && isLand)
			{
				float h = elevation.GetPixel(x, z).R;
				float hx = elevation.GetPixel(Math.Min(x + 1, width - 1), z).R;
				float hz = elevation.GetPixel(x, Math.Min(z + 1, depth - 1)).R;
				float relief = Math.Clamp((h - hx) * 7.5f + (h - hz) * 5.0f,
					-.18f, .18f);
				c = relief >= 0f ? c.Lightened(relief) : c.Darkened(-relief);
				if (((int)MathF.Floor(h * 32f) & 1) == 0) c = c.Darkened(.025f);
			}

			int target = (z * width + x) * 3;
			pixels[target] = ToByte(c.R);
			pixels[target + 1] = ToByte(c.G);
			pixels[target + 2] = ToByte(c.B);
		}
		return Image.CreateFromData(width, depth, false, Image.Format.Rgb8, pixels);
	}

	private static byte ToByte(float value) =>
		(byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

	private static Color SiteColour(SiteTier tier) => tier switch
	{
		SiteTier.GreatWork => new Color(1.00f, .88f, .54f),
		SiteTier.District => new Color(1.00f, .72f, .64f),
		SiteTier.Precinct => new Color(.84f, .70f, .94f),
		_ => new Color(.64f, .86f, .82f),
	};

	private sealed partial class Canvas : Control
	{
		private const string Reference1DomainId = "shallows-gateway-domain";
		public WorldAtlasDefinition Atlas;
		public Texture2D Texture;
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

		private Rect2 MapRect()
		{
			Vector2 available = Size * .88f;
			float scale = MathF.Min(available.X / Atlas.Width,
				available.Y / Atlas.Depth) * _zoom;
			Vector2 mapSize = new(Atlas.Width * scale, Atlas.Depth * scale);
			Vector2 centre = Size * .5f + _pan;
			return new Rect2(centre - mapSize * .5f, mapSize);
		}

		private Vector2 ToMap(float worldX, float worldZ)
		{
			Rect2 rect = MapRect();
			return rect.Position + new Vector2(worldX / Atlas.Width * rect.Size.X,
				worldZ / Atlas.Depth * rect.Size.Y);
		}

		public override void _GuiInput(InputEvent input)
		{
			if (input is InputEventMouseButton mouse)
			{
				if (mouse.ButtonIndex == MouseButton.WheelUp && mouse.Pressed)
					_zoom = MathF.Min(_zoom * 1.18f, 8f);
				else if (mouse.ButtonIndex == MouseButton.WheelDown && mouse.Pressed)
				{
					_zoom = MathF.Max(_zoom / 1.18f, 1f);
					if (_zoom <= 1.001f) _pan = Vector2.Zero;
				}
				else if (mouse.ButtonIndex == MouseButton.Left)
				{
					if (mouse.Pressed && mouse.ShiftPressed)
					{
						Rect2 rect = MapRect();
						if (rect.HasPoint(mouse.Position))
						{
							float nx = (mouse.Position.X - rect.Position.X) / rect.Size.X;
							float nz = (mouse.Position.Y - rect.Position.Y) / rect.Size.Y;
							float globalX = Math.Clamp(nx * Atlas.Width, 0f,
								Atlas.Width - .001f);
							float globalZ = Math.Clamp(nz * Atlas.Depth, 0f,
								Atlas.Depth - .001f);
							GD.Print($"[atlas-map] Shift-click global {globalX:0.00},{globalZ:0.00}");
							Teleport?.Invoke(new Vector3(globalX, 0f, globalZ));
						}
					}
					else _dragging = mouse.Pressed;
				}
				QueueRedraw();
				AcceptEvent();
			}
			else if (input is InputEventMouseMotion motion && _dragging)
			{
				_pan += motion.Relative;
				QueueRedraw();
				AcceptEvent();
			}
		}

		public override void _Draw()
		{
			if (Texture == null || Atlas == null) return;
			Rect2 rect = MapRect();
			DrawRect(rect.Grow(3f), new Color(.16f, .14f, .20f));
			DrawTextureRect(Texture, rect, false);

			CanonicalWorldDefinition topology = Atlas.Topology;
			if (topology != null)
			{
				foreach (CanonicalRoute route in topology.Routes)
				{
					Vector2[] points = route.Points.Select(point => ToMap(point.X, point.Z)).ToArray();
					if (points.Length >= 2)
						DrawPolyline(points, new Color(.96f, .91f, .90f, .72f),
							Math.Clamp(route.Width * rect.Size.X / Atlas.Width, 1f, 3f), true);
				}
				foreach (CanonicalDomain domain in topology.Domains) DrawDomain(domain);
				foreach (CanonicalSite site in topology.Sites) DrawSite(site);
			}

			Vector2 player = ToMap(Player.X, Player.Z);
			DrawCircle(player, 7.5f, new Color(.10f, .09f, .13f, .92f));
			DrawCircle(player, 5.2f, new Color(.42f, .72f, .78f));
			DrawCircle(player, 2.2f, Colors.White);
		}

		private void DrawDomain(CanonicalDomain domain)
		{
			Vector2[] points = domain.Boundary.Select(point => ToMap(point.X, point.Z)).ToArray();
			if (points.Length < 3) return;
			DrawColoredPolygon(points, new Color(.72f, .56f, .88f, .08f));
			for (int i = 0; i < points.Length; i++)
				DrawDashedLine(points[i], points[(i + 1) % points.Length],
					new Color(.78f, .64f, .90f, .86f), 2f, 7f);
			Vector2 labelAt = points.Aggregate(Vector2.Zero, (sum, point) => sum + point) /
				points.Length + new Vector2(8f, -12f);
			string label = domain.Id == Reference1DomainId
				? $"{domain.DisplayName} · Reference 1 district"
				: domain.DisplayName;
			DrawLabel(labelAt, label, new Color(.91f, .78f, 1f), 14);
		}

		private void DrawSite(CanonicalSite site)
		{
			Vector2 centre = ToMap(site.Centre.X, site.Centre.Z);
			float halfX = site.ExtentX * .5f * MapRect().Size.X / Atlas.Width;
			float halfZ = site.ExtentZ * .5f * MapRect().Size.Y / Atlas.Depth;
			float angle = Mathf.DegToRad(site.OrientationDegrees);
			Vector2 Rotate(Vector2 point) => new(
				point.X * MathF.Cos(angle) - point.Y * MathF.Sin(angle),
				point.X * MathF.Sin(angle) + point.Y * MathF.Cos(angle));
			Vector2[] corners =
			{
				centre + Rotate(new Vector2(-halfX, -halfZ)),
				centre + Rotate(new Vector2(halfX, -halfZ)),
				centre + Rotate(new Vector2(halfX, halfZ)),
				centre + Rotate(new Vector2(-halfX, halfZ)),
			};
			Color colour = SiteColour(site.Tier);
			for (int i = 0; i < corners.Length; i++)
				DrawLine(corners[i], corners[(i + 1) % corners.Length], colour, 2f);
			DrawCircle(centre, site.Tier is SiteTier.District or SiteTier.GreatWork ? 5f : 3.5f,
				colour);
			DrawLabel(centre + new Vector2(8f, -7f), site.DisplayName, colour, 13);
		}

		private void DrawLabel(Vector2 at, string text, Color colour, int size)
		{
			Font font = ThemeDB.FallbackFont;
			DrawString(font, at + Vector2.One, text, HorizontalAlignment.Left, -1, size,
				new Color(.10f, .09f, .13f, .94f));
			DrawString(font, at, text, HorizontalAlignment.Left, -1, size, colour);
		}
	}
}
