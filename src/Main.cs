using Godot;
using Petalfell.Render;

namespace Petalfell;

/// <summary>
/// Production entry point. World authoring commands exit before runtime setup;
/// ordinary play always enters the moving, map-guided production window.
/// </summary>
public partial class Main : Node3D
{
	[Export(PropertyHint.File, "*.json")]
	public string MapDefinitionPath = "res://content/chapter_01/map.json";

	public override void _Ready()
	{
		if (Tools.WorldAuthoring.TryRun(this, MapDefinitionPath)) return;

		// Shader globals must exist before the runtime creates any materials.
		DayCycle.RegisterGlobals();
		SetupInput();
		if (!Tools.AtlasSectorReview.TryRun(this, MapDefinitionPath,
		    "bloom-grove-court"))
			throw new System.InvalidOperationException(
				"no production runtime or authoring review was selected");
	}

	private static void SetupInput()
	{
		// Defined in code so packaged controls cannot drift from their readers.
		void Bind(string action, params Key[] keys)
		{
			if (InputMap.HasAction(action)) InputMap.EraseAction(action);
			InputMap.AddAction(action);
			foreach (Key key in keys)
				InputMap.ActionAddEvent(action,
					new InputEventKey { PhysicalKeycode = key });
		}

		Bind("move_forward", Key.W, Key.Up);
		Bind("move_back", Key.S, Key.Down);
		Bind("move_left", Key.A, Key.Left);
		Bind("move_right", Key.D, Key.Right);
		Bind("slow_walk", Key.Shift);
		Bind("jump", Key.Space);
		Bind("loadout_1", Key.Key1);
		Bind("loadout_2", Key.Key2);
		Bind("loadout_3", Key.Key3);
		Bind("loadout_4", Key.Key4);
		Bind("cycle_left_hand", Key.Z);
		Bind("cycle_right_hand", Key.X);
		Bind("throw_left", Key.F);
		Bind("throw_right", Key.G);
		Bind("interact", Key.R);
		Bind("dog_fetch", Key.U);
		Bind("world_map", Key.M);
		Bind("inventory", Key.Tab);
		Bind("skill_selector", Key.T);
	}
}
