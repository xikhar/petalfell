using Godot;
using Petalfell.Core;

namespace Petalfell.Items;

/// <summary>Small procedural models shared by held and world item instances.</summary>
public static class ItemVisuals
{
	public static Node3D Build(ItemDefinition item,
		ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		var root = new Node3D { Name = item?.Name ?? "Item" };
		if (item == ItemCatalog.Stick)
			BuildStick(root, item.Color, item.LightOutline, inkLight, inkDark);
		return root;
	}

	private static void BuildStick(Node3D root, Color color, bool lightOutline,
		ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		// One strong outlined shaft and two unoutlined knots read as a stick at
		// gameplay zoom without becoming a ladder of tiny internal strokes.
		AddBox(root, new Vector3(0.17f, 1.36f, 0.17f), Vector3.Zero,
			new Vector3(0f, 0f, -0.12f), color, inkLight, inkDark,
			outlined: true, lightOutline: lightOutline);
		AddBox(root, new Vector3(0.13f, 0.42f, 0.13f), new Vector3(0.13f, 0.31f, 0f),
			new Vector3(0f, 0f, -0.68f), color.Lightened(0.08f),
			inkLight, inkDark, outlined: false);
		AddBox(root, new Vector3(0.19f, 0.18f, 0.19f), new Vector3(-0.035f, -0.37f, 0f),
			Vector3.Zero, color.Darkened(0.06f), inkLight, inkDark, outlined: false);
	}

	private static void AddBox(Node3D parent, Vector3 size, Vector3 position,
		Vector3 rotation, Color color, ShaderMaterial inkLight,
		ShaderMaterial inkDark, bool outlined, bool lightOutline = false)
	{
		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			Position = position,
			Rotation = rotation,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/character.gdshader"),
		};
		material.SetShaderParameter("albedo", color);
		material.SetShaderParameter("sun_dir", Palette.SunDir);
		mesh.MaterialOverride = material;
		parent.AddChild(mesh);

		if (!outlined) return;
		var ink = new MeshInstance3D
		{
			Mesh = InkBuilder.Box(size.X, size.Y, size.Z, lightOutline),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			CustomAabb = new Aabb(-size, size * 2f),
		};
		ink.SetSurfaceOverrideMaterial(0, lightOutline ? inkLight : inkDark);
		mesh.AddChild(ink);
	}
}
