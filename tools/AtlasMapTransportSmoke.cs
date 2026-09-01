using System;
using Godot;
using Petalfell.UI;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Focused UI-state regression. A successful runtime handoff calls the same
/// CompleteTransport method after collision and landing are installed; rejected
/// requests never call it and therefore leave the atlas open.
/// </summary>
public partial class AtlasMapTransportSmoke : Node
{
	public override void _Ready()
	{
		try
		{
			MapDefinition map = MapDefinition.Load("res://content/chapter_01/map.json");
			var atlasMap = new AtlasWorldMap();
			AddChild(atlasMap);
			atlasMap.Setup(map.CanonicalAtlas, "", "");
			atlasMap.Toggle();
			if (!atlasMap.IsOpen || !atlasMap.Visible)
				throw new InvalidOperationException("atlas did not open before transport");

			atlasMap.CompleteTransport(new Vector3(6400.5f, 31.2f, 7360.5f));
			if (atlasMap.IsOpen || atlasMap.Visible)
				throw new InvalidOperationException(
					"successful atlas transport did not close the map");

			GD.Print("[atlas-map-transport-smoke] successful transport closes the atlas");
			GetTree().Quit();
		}
		catch (Exception ex)
		{
			GD.PushError($"[atlas-map-transport-smoke] {ex.Message}");
			GetTree().Quit(1);
		}
	}
}
