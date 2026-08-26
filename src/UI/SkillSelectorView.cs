using System.Collections.Generic;
using Godot;
using Petalfell.Skills;

namespace Petalfell.UI;

/// <summary>
/// A compact, transient chooser for learned actions. Skill state and execution
/// stay in SkillSystem; this layer only presents what is currently available.
/// </summary>
public partial class SkillSelectorView : CanvasLayer
{
	private SkillSystem _skills;
	private Control _root;
	private SkillCanvas _canvas;
	private bool _open;

	public bool IsOpen => _open;

	public void Setup(SkillSystem skills) => _skills = skills;

	public override void _Ready()
	{
		Layer = 265;

		_root = new Control
		{
			Name = "SkillSelectorRoot",
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var dim = new ColorRect
		{
			Color = new Color(0.11f, 0.10f, 0.16f, 0.20f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(dim);

		var panel = new PanelContainer
		{
			Name = "SkillSelectorGlass",
			MouseFilter = Control.MouseFilterEnum.Stop,
			AnchorLeft = 0.5f,
			AnchorRight = 0.5f,
			AnchorTop = 0.5f,
			AnchorBottom = 0.5f,
			OffsetLeft = -220f,
			OffsetRight = 220f,
			OffsetTop = -142f,
			OffsetBottom = 142f,
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
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		panel.AddChild(margin);

		_canvas = new SkillCanvas
		{
			Name = "SkillSelectorContents",
			CustomMinimumSize = new Vector2(392f, 246f),
			MouseFilter = Control.MouseFilterEnum.Stop,
			FocusMode = Control.FocusModeEnum.None,
		};
		_canvas.Setup(_skills, ActivateSelected);
		margin.AddChild(_canvas);

		Visible = false;
	}

	public void Toggle()
	{
		_open = !_open;
		Visible = _open;
		if (_open) _canvas?.Refresh();
	}

	public void Close()
	{
		if (!_open) return;
		_open = false;
		Visible = false;
	}

	/// <summary>Returns true whenever this modal owns the event.</summary>
	public bool HandleInput(InputEvent inputEvent)
	{
		if (inputEvent is not InputEventKey key || key.Echo) return _open;

		if (key.Pressed && inputEvent.IsActionPressed("skill_selector"))
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
		if (IsKey(key, Key.Enter) || inputEvent.IsActionPressed("ui_accept"))
		{
			ActivateSelected();
			return true;
		}
		if (IsKey(key, Key.Left) || IsKey(key, Key.Up))
		{
			_canvas?.MoveSelection(-1);
			return true;
		}
		if (IsKey(key, Key.Right) || IsKey(key, Key.Down))
		{
			_canvas?.MoveSelection(1);
			return true;
		}
		return true;
	}

	private void ActivateSelected()
	{
		var skill = _canvas?.SelectedSkill;
		if (skill == null) return;
		_skills?.Activate(skill.Id);
		Close();
	}

	private static bool IsKey(InputEventKey input, Key key) =>
		input.PhysicalKeycode == key || input.Keycode == key;

	private partial class SkillCanvas : Control
	{
		private static readonly Color Text = new(1f, 1f, 1f, 0.91f);
		private static readonly Color Muted = new(1f, 1f, 1f, 0.54f);
		private static readonly Color TileFill = new(0.96f, 0.96f, 1f, 0.14f);
		private static readonly Color TileEdge = new(1f, 1f, 1f, 0.74f);
		private static readonly Color Unavailable = new(0.93f, 0.72f, 0.72f, 0.70f);

		private SkillSystem _skills;
		private System.Action _activate;
		private int _selected;

		public SkillDefinition SelectedSkill
		{
			get
			{
				IReadOnlyList<SkillDefinition> available = _skills?.AvailableSkills;
				return available != null && _selected >= 0 && _selected < available.Count
					? available[_selected] : null;
			}
		}

		public void Setup(SkillSystem skills, System.Action activate)
		{
			_skills = skills;
			_activate = activate;
		}

		public void Refresh()
		{
			int count = _skills?.AvailableSkills?.Count ?? 0;
			_selected = count == 0 ? -1 : Mathf.Clamp(_selected, 0, count - 1);
			QueueRedraw();
		}

		public void MoveSelection(int direction)
		{
			int count = _skills?.AvailableSkills?.Count ?? 0;
			if (count <= 0) return;
			_selected = Mathf.PosMod(_selected + direction, count);
			QueueRedraw();
		}

		public override void _Ready() => Refresh();

		public override void _Draw()
		{
			var font = ThemeDB.FallbackFont;
			DrawString(font, new Vector2(2f, 25f), "Skills",
				HorizontalAlignment.Left, -1f, 21, Text);

			var skill = SelectedSkill;
			if (skill == null)
			{
				DrawString(font, new Vector2(0f, 130f), "No skills available",
					HorizontalAlignment.Center, Size.X, 16, Muted);
				return;
			}

			var tile = TileRect();
			DrawRect(tile, TileFill, filled: true);
			DrawRect(tile, TileEdge, filled: false, width: 2f, antialiased: true);
			DrawCampfire(tile.Position + new Vector2(63f, tile.Size.Y * 0.5f));

			string displayName = skill.Id == "build_campfire" ? "Build Campfire" : skill.Name;
			DrawString(font, tile.Position + new Vector2(116f, 43f), displayName,
				HorizontalAlignment.Left, tile.Size.X - 132f, 18, Text);
			bool enoughWood = (_skills?.WoodCount ?? 0) >= SkillSystem.CampfireWoodCost;
			DrawString(font, tile.Position + new Vector2(116f, 72f),
				$"{SkillSystem.CampfireWoodCost} wood",
				HorizontalAlignment.Left, tile.Size.X - 132f, 14,
				enoughWood ? Muted : Unavailable);

			DrawString(font, new Vector2(2f, Size.Y - 6f), "Enter  use     Esc  close",
				HorizontalAlignment.Center, Size.X - 4f, 13, Muted);
		}

		public override void _GuiInput(InputEvent inputEvent)
		{
			if (inputEvent is not InputEventMouseButton mouse ||
				mouse.ButtonIndex != MouseButton.Left || !mouse.Pressed ||
				!TileRect().HasPoint(mouse.Position)) return;

			_selected = 0;
			_activate?.Invoke();
			AcceptEvent();
		}

		private Rect2 TileRect() => new(20f, 58f, Size.X - 40f, 116f);

		private void DrawCampfire(Vector2 centre)
		{
			var dark = new Color(0.27f, 0.22f, 0.28f, 0.88f);
			var stone = new Color(0.73f, 0.70f, 0.78f, 0.88f);
			var wood = new Color(0.58f, 0.36f, 0.24f, 0.98f);
			var ember = new Color(1.00f, 0.45f, 0.19f, 0.98f);
			var flame = new Color(1.00f, 0.80f, 0.38f, 0.98f);

			// A quiet ring of stones underneath two crossed logs.
			for (int i = 0; i < 8; i++)
			{
				float angle = i / 8f * Mathf.Tau;
				var p = centre + new Vector2(Mathf.Cos(angle) * 25f,
					Mathf.Sin(angle) * 9f + 16f);
				DrawCircle(p, 5.5f, dark, filled: true, width: -1f, antialiased: true);
				DrawCircle(p + Vector2.Up, 4f, stone,
					filled: true, width: -1f, antialiased: true);
			}

			DrawLine(centre + new Vector2(-18f, 18f), centre + new Vector2(18f, 7f),
				dark, 10f, antialiased: true);
			DrawLine(centre + new Vector2(-18f, 18f), centre + new Vector2(18f, 7f),
				wood, 6f, antialiased: true);
			DrawLine(centre + new Vector2(18f, 18f), centre + new Vector2(-18f, 7f),
				dark, 10f, antialiased: true);
			DrawLine(centre + new Vector2(18f, 18f), centre + new Vector2(-18f, 7f),
				wood, 6f, antialiased: true);

			var fire = centre + Vector2.Up * 4f;
			DrawCircle(fire, 15f, ember, filled: true, width: -1f, antialiased: true);
			DrawCircle(fire + Vector2.Up * 7f, 10f, flame,
				filled: true, width: -1f, antialiased: true);
			DrawLine(fire, fire + Vector2.Up * 24f, flame, 7f, antialiased: true);
		}
	}
}
