using Godot;
using Petalfell.Items;

namespace Petalfell.UI;

/// <summary>
/// A caught fish is acknowledged as an image, not a modal or a sentence. The
/// icon rises a few pixels near the lower centre and disappears quickly enough
/// that repeated fishing never turns into UI management.
/// </summary>
public partial class FishingCatchHud : CanvasLayer
{
	private CatchIcon _icon;

	public override void _Ready()
	{
		Layer = 235;
		_icon = new CatchIcon
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_icon.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(_icon);
	}

	public void ShowCatch(ItemDefinition fish) => _icon?.Show(fish);

	private partial class CatchIcon : Control
	{
		private const float Lifetime = 1.45f;
		private ItemDefinition _fish;
		private float _remaining;

		public void Show(ItemDefinition fish)
		{
			if (fish == null) return;
			_fish = fish;
			_remaining = Lifetime;
			Visible = true;
			QueueRedraw();
		}

		public override void _Process(double delta)
		{
			if (_remaining <= 0f) return;
			_remaining = Mathf.Max(0f, _remaining - (float)delta);
			if (_remaining <= 0f)
			{
				Visible = false;
				_fish = null;
			}
			QueueRedraw();
		}

		public override void _Draw()
		{
			if (_fish == null || _remaining <= 0f) return;
			float age = Lifetime - _remaining;
			float enter = Mathf.Clamp(age / 0.13f, 0f, 1f);
			enter = enter * enter * (3f - 2f * enter);
			float exit = Mathf.Clamp(_remaining / 0.62f, 0f, 1f);
			exit = exit * exit * (3f - 2f * exit);
			float opacity = enter * exit;
			float scale = Mathf.Lerp(0.82f, 1.25f, enter) * (0.98f + exit * 0.02f);
			var centre = new Vector2(Size.X * 0.5f,
				Size.Y * 0.73f - Mathf.Clamp(age, 0f, 1f) * 14f);
			ItemIconRenderer.Draw(this, _fish, centre, scale, opacity);
		}
	}
}
