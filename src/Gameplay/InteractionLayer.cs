using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Player;

namespace Petalfell.Gameplay;

/// <summary>The result of one contextual action.</summary>
public readonly struct InteractionResult
{
	public readonly bool Succeeded;
	public readonly string Message;

	public InteractionResult(bool succeeded, string message = null)
	{
		Succeeded = succeeded;
		Message = message;
	}

	public static InteractionResult Done(string message = null) => new(true, message);
	public static InteractionResult Failed(string message) => new(false, message);
}

/// <summary>
/// One action currently available to the player. Providers create these from
/// live world state; the central layer resolves priority, draws the hints and
/// owns input dispatch, so every future door, artifact, NPC or workstation uses
/// the same interaction language.
/// </summary>
public sealed class ContextAction
{
	public string ActionName { get; }
	public string KeyLabel { get; }
	public string Label { get; }
	public int Priority { get; }
	public float Distance { get; }
	public Func<InteractionResult> Execute { get; }

	public ContextAction(string actionName, string keyLabel, string label,
		int priority, float distance, Func<InteractionResult> execute)
	{
		ActionName = actionName;
		KeyLabel = keyLabel;
		Label = label;
		Priority = priority;
		Distance = distance;
		Execute = execute;
	}
}

public interface IInteractionProvider
{
	void GatherInteractions(Vector3 playerPosition, List<ContextAction> actions);
}

/// <summary>
/// Bottom-centre contextual UI and the sole dispatcher for proximity actions.
/// It deliberately does not know what a campfire, item or NPC is.
/// </summary>
public partial class InteractionLayer : CanvasLayer
{
	private readonly List<IInteractionProvider> _providers = new();
	private readonly List<ContextAction> _actions = new();
	private Controller _player;
	private PromptView _view;
	private bool _suppressed;

	public void Setup(Controller player) => _player = player;

	public void Register(IInteractionProvider provider)
	{
		if (provider != null && !_providers.Contains(provider)) _providers.Add(provider);
	}

	public void Unregister(IInteractionProvider provider) => _providers.Remove(provider);

	public override void _Ready()
	{
		Layer = 230;
		_view = new PromptView();
		AddChild(_view);
	}

	public void SetSuppressed(bool suppressed)
	{
		_suppressed = suppressed;
		if (suppressed)
		{
			_actions.Clear();
			_view?.SetActions(_actions);
		}
	}

	public void ShowNotice(string text) => _view?.ShowNotice(text);

	public override void _Process(double delta)
	{
		if (_suppressed || _player == null)
		{
			_view?.SetActions(Array.Empty<ContextAction>());
			return;
		}
		Gather();
		_view?.SetActions(_actions);
	}

	public bool HandleInput(InputEvent inputEvent)
	{
		if (_suppressed || _player == null || inputEvent is not InputEventKey key ||
			!key.Pressed || key.Echo)
			return false;

		Gather();
		foreach (var action in _actions)
		{
			if (!inputEvent.IsActionPressed(action.ActionName)) continue;
			var result = action.Execute?.Invoke() ?? InteractionResult.Failed(null);
			if (!string.IsNullOrWhiteSpace(result.Message)) ShowNotice(result.Message);
			return true;
		}
		return false;
	}

	private void Gather()
	{
		_actions.Clear();
		Vector3 at = _player.GlobalPosition;
		foreach (var provider in _providers) provider.GatherInteractions(at, _actions);
		_actions.Sort((a, b) =>
		{
			int priority = b.Priority.CompareTo(a.Priority);
			return priority != 0 ? priority : a.Distance.CompareTo(b.Distance);
		});

		// Only the best target for a given button can actually receive that button.
		// Removing duplicate hints keeps two nearby sticks from drawing two R rows.
		for (int i = 0; i < _actions.Count;)
		{
			bool duplicate = false;
			for (int earlier = 0; earlier < i; earlier++)
				if (_actions[earlier].ActionName == _actions[i].ActionName)
				{
					duplicate = true;
					break;
				}
			if (duplicate) _actions.RemoveAt(i);
			else i++;
		}
	}

	private partial class PromptView : Control
	{
		private readonly HBoxContainer[] _rows = new HBoxContainer[3];
		private readonly Label[] _keys = new Label[3];
		private readonly Label[] _labels = new Label[3];
		private Label _notice;
		private float _noticeTime;

		public override void _Ready()
		{
			MouseFilter = MouseFilterEnum.Ignore;
			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

			var stack = new VBoxContainer
			{
				MouseFilter = MouseFilterEnum.Ignore,
				AnchorLeft = 0.5f,
				AnchorRight = 0.5f,
				AnchorTop = 1f,
				AnchorBottom = 1f,
				OffsetLeft = -260f,
				OffsetRight = 260f,
				OffsetTop = -150f,
				OffsetBottom = -26f,
				Alignment = BoxContainer.AlignmentMode.End,
			};
			stack.AddThemeConstantOverride("separation", 7);
			AddChild(stack);

			_notice = new Label
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				MouseFilter = MouseFilterEnum.Ignore,
				Visible = false,
			};
			_notice.AddThemeFontSizeOverride("font_size", 16);
			_notice.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.82f));
			stack.AddChild(_notice);

			for (int i = 0; i < _rows.Length; i++)
			{
				var row = new HBoxContainer
				{
					Alignment = BoxContainer.AlignmentMode.Center,
					MouseFilter = MouseFilterEnum.Ignore,
					Visible = false,
				};
				row.AddThemeConstantOverride("separation", 9);
				stack.AddChild(row);
				_rows[i] = row;

				var key = new Label
				{
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					CustomMinimumSize = new Vector2(31f, 28f),
					MouseFilter = MouseFilterEnum.Ignore,
				};
				key.AddThemeFontSizeOverride("font_size", 15);
				key.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.90f));
				key.AddThemeStyleboxOverride("normal", new StyleBoxFlat
				{
					BgColor = new Color(1f, 1f, 1f, 0.11f),
					BorderColor = new Color(1f, 1f, 1f, 0.30f),
					BorderWidthLeft = 1,
					BorderWidthTop = 1,
					BorderWidthRight = 1,
					BorderWidthBottom = 1,
					CornerRadiusTopLeft = 7,
					CornerRadiusTopRight = 7,
					CornerRadiusBottomLeft = 7,
					CornerRadiusBottomRight = 7,
				});
				row.AddChild(key);
				_keys[i] = key;

				var label = new Label { MouseFilter = MouseFilterEnum.Ignore };
				label.AddThemeFontSizeOverride("font_size", 17);
				label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.76f));
				row.AddChild(label);
				_labels[i] = label;
			}
		}

		public void SetActions(IReadOnlyList<ContextAction> actions)
		{
			for (int i = 0; i < _rows.Length; i++)
			{
				bool shown = actions != null && i < actions.Count;
				_rows[i].Visible = shown;
				if (!shown) continue;
				_keys[i].Text = actions[i].KeyLabel;
				_labels[i].Text = actions[i].Label;
			}
		}

		public void ShowNotice(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return;
			_notice.Text = text;
			_notice.Modulate = Colors.White;
			_notice.Visible = true;
			_noticeTime = 2.25f;
		}

		public override void _Process(double delta)
		{
			if (_noticeTime <= 0f) return;
			_noticeTime = Mathf.Max(0f, _noticeTime - (float)delta);
			_notice.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_noticeTime * 1.5f, 0f, 1f));
			if (_noticeTime <= 0f) _notice.Visible = false;
		}
	}
}
