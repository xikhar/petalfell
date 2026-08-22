using System;
using Godot;
using Petalfell.Core;
using Petalfell.Render;

namespace Petalfell.Tools;

/// <summary>
/// Temporary, standalone developer controls. This deliberately owns no game UI
/// state: it is a diagnostic overlay that can be replaced or removed without
/// affecting menus, HUD, input prompts, or presentation code.
/// </summary>
public partial class DeveloperMenu : CanvasLayer
{
	private sealed class SliderRow
	{
		public HSlider Slider;
		public Label ValueLabel;
		public Func<double, string> Format;

		public void SetWithoutSignal(double value)
		{
			Slider.SetValueNoSignal(value);
			ValueLabel.Text = Format(value);
		}
	}

	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private CameraRig _camera;
	private Control _root;
	private SliderRow _minZoom;
	private SliderRow _maxZoom;

	public void Setup(ShaderMaterial inkLight, ShaderMaterial inkDark, CameraRig camera)
	{
		_inkLight = inkLight;
		_inkDark = inkDark;
		_camera = camera;
	}

	public override void _Ready()
	{
		Layer = 1000;
		ProcessMode = ProcessModeEnum.Always;

		_root = new Control
		{
			Name = "DeveloperSettingsRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);

		var panel = new PanelContainer
		{
			Name = "DeveloperSettingsPanel",
			CustomMinimumSize = new Vector2(330f, 0f),
			OffsetLeft = 18f,
			OffsetTop = 18f,
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.055f, 0.06f, 0.075f, 0.94f),
			BorderColor = new Color(1f, 1f, 1f, 0.14f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 7,
			CornerRadiusTopRight = 7,
			CornerRadiusBottomLeft = 7,
			CornerRadiusBottomRight = 7,
		});
		_root.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 16);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_right", 16);
		margin.AddThemeConstantOverride("margin_bottom", 16);
		panel.AddChild(margin);

		var content = new VBoxContainer();
		content.AddThemeConstantOverride("separation", 12);
		margin.AddChild(content);

		var title = new Label { Text = "Developer settings" };
		title.AddThemeFontSizeOverride("font_size", 17);
		title.AddThemeColorOverride("font_color", new Color(0.94f, 0.95f, 1f));
		content.AddChild(title);

		AddSlider(content, "Outline width", 0.5, 6.0, 0.05, Palette.InkWidth,
			value => $"{value:0.00} px", SetOutlineWidth);

		_minZoom = AddSlider(content, "Minimum zoom", 20.0, 180.0, 1.0,
			_camera.MinDistance, value => $"{value:0}", SetMinimumZoom);
		_maxZoom = AddSlider(content, "Maximum zoom", 24.0, 240.0, 1.0,
			_camera.MaxDistance, value => $"{value:0}", SetMaximumZoom);

		_root.Visible = false;
	}

	public override void _Input(InputEvent inputEvent)
	{
		if (inputEvent is not InputEventKey key || !key.Pressed || key.Echo) return;
		if (key.PhysicalKeycode != Key.Quoteleft &&
			key.Keycode != Key.Quoteleft && key.Keycode != Key.Asciitilde) return;

		_root.Visible = !_root.Visible;
		GetViewport().SetInputAsHandled();
	}

	private SliderRow AddSlider(VBoxContainer parent, string title,
		double min, double max, double step, double current,
		Func<double, string> format, Action<double> changed)
	{
		var group = new VBoxContainer();
		group.AddThemeConstantOverride("separation", 4);
		parent.AddChild(group);

		var header = new HBoxContainer();
		group.AddChild(header);

		var name = new Label
		{
			Text = title,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		name.AddThemeColorOverride("font_color", new Color(0.80f, 0.82f, 0.88f));
		header.AddChild(name);

		var valueLabel = new Label
		{
			Text = format(current),
			HorizontalAlignment = HorizontalAlignment.Right,
			CustomMinimumSize = new Vector2(64f, 0f),
		};
		valueLabel.AddThemeColorOverride("font_color", new Color(0.94f, 0.95f, 1f));
		header.AddChild(valueLabel);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = current,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0f, 18f),
		};
		group.AddChild(slider);

		var row = new SliderRow { Slider = slider, ValueLabel = valueLabel, Format = format };
		slider.ValueChanged += value =>
		{
			valueLabel.Text = format(value);
			changed(value);
		};
		return row;
	}

	private void SetOutlineWidth(double value)
	{
		float width = (float)value;
		_inkLight.SetShaderParameter("core_width", width);
		_inkDark.SetShaderParameter("core_width", width);
	}

	private void SetMinimumZoom(double value)
	{
		if (value > _maxZoom.Slider.Value) _maxZoom.SetWithoutSignal(value);
		ApplyZoomLimits();
	}

	private void SetMaximumZoom(double value)
	{
		if (value < _minZoom.Slider.Value) _minZoom.SetWithoutSignal(value);
		ApplyZoomLimits();
	}

	private void ApplyZoomLimits()
	{
		_camera.SetZoomLimits((float)_minZoom.Slider.Value, (float)_maxZoom.Slider.Value);
	}
}
