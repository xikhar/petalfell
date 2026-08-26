using Godot;
using Petalfell.Core;
using Petalfell.Render;

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
		else if (item == ItemCatalog.Torch)
			BuildTorch(root, item, inkLight, inkDark);
		else if (item == ItemCatalog.FishingRod)
			BuildFishingRod(root, item, inkLight, inkDark);
		else if (item == ItemCatalog.SilverMinnow || item == ItemCatalog.Rosefin ||
			item == ItemCatalog.MoonCarp)
			BuildFish(root, item, inkLight, inkDark);
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

	private static void BuildTorch(Node3D root, ItemDefinition item,
		ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		// A single outlined handle keeps the held silhouette clean. The pale binding
		// and glowing voxel flame are colour details, not additional ink ladders.
		AddBox(root, new Vector3(0.19f, 1.24f, 0.19f), new Vector3(0f, 0.03f, 0f),
			Vector3.Zero, item.Color, inkLight, inkDark,
			outlined: true, lightOutline: false);
		AddBox(root, new Vector3(0.34f, 0.24f, 0.34f), new Vector3(0f, 0.56f, 0f),
			Vector3.Zero, new Color(0.72f, 0.61f, 0.48f).SrgbToLinear(), inkLight, inkDark,
			outlined: false);

		var fire = new FireGlow
		{
			Name = "TorchFlame",
			Position = new Vector3(0f, 0.79f, 0f),
		};
		root.AddChild(fire);
		fire.Setup(item.Light, visualScale: 0.72f);
	}

	private static void BuildFishingRod(Node3D root, ItemDefinition item,
		ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		// The handle stays rigid while three short shaft joints carry a restrained
		// bend during casting and bites. The joints are named so Character can pose
		// the equipped instance without knowing how its boxes were constructed.
		AddBox(root, new Vector3(0.18f, 1.60f, 0.18f), new Vector3(0f, -0.14f, 0f),
			Vector3.Zero, item.Color.Darkened(0.08f), inkLight, inkDark,
			outlined: true, lightOutline: false);
		Node3D parent = root;
		for (int i = 0; i < 3; i++)
		{
			var joint = new Node3D
			{
				Name = $"RodJoint{i}",
				Position = new Vector3(0f, i == 0 ? -0.93f : -0.76f, 0f),
			};
			parent.AddChild(joint);
			float thickness = 0.125f - i * 0.022f;
			AddBox(joint, new Vector3(thickness, 0.78f, thickness),
				new Vector3(0f, -0.39f, 0f), Vector3.Zero,
				item.Color.Lightened(0.05f + i * 0.035f), inkLight, inkDark,
				outlined: i == 0, lightOutline: false);
			parent = joint;
		}

		var tip = new Marker3D
		{
			Name = "RodTip",
			Position = new Vector3(0f, -0.80f, 0f),
		};
		parent.AddChild(tip);

		// A compact reel beneath the grip, kept unoutlined so it reads as one detail
		// instead of a dark knot at normal camera distance.
		AddBox(root, new Vector3(0.34f, 0.28f, 0.20f), new Vector3(0.18f, -0.34f, 0f),
			new Vector3(0f, 0f, 0.18f), new Color(0.72f, 0.68f, 0.62f).SrgbToLinear(),
			inkLight, inkDark, outlined: false);
	}

	private static void BuildFish(Node3D root, ItemDefinition item,
		ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		AddBox(root, new Vector3(0.46f, 0.34f, 0.92f), Vector3.Zero,
			Vector3.Zero, item.Color, inkLight, inkDark,
			outlined: true, lightOutline: item.LightOutline);
		AddBox(root, new Vector3(0.12f, 0.48f, 0.42f), new Vector3(0f, 0f, -0.58f),
			new Vector3(0f, 0f, Mathf.Pi * 0.25f), item.Color.Darkened(0.08f),
			inkLight, inkDark, outlined: false);
		AddBox(root, new Vector3(0.16f, 0.10f, 0.28f), new Vector3(0f, 0.23f, 0.05f),
			new Vector3(0.24f, 0f, 0f), item.Color.Lightened(0.08f),
			inkLight, inkDark, outlined: false);
	}

	public static Node3D BuildFishingBobber(ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		var root = new Node3D { Name = "FishingBobber" };
		AddBox(root, new Vector3(0.18f, 0.25f, 0.18f), new Vector3(0f, -0.08f, 0f),
			Vector3.Zero, new Color(0.94f, 0.91f, 0.82f).SrgbToLinear(), inkLight, inkDark,
			outlined: true, lightOutline: true);
		AddBox(root, new Vector3(0.20f, 0.18f, 0.20f), new Vector3(0f, 0.13f, 0f),
			Vector3.Zero, new Color(0.88f, 0.35f, 0.38f).SrgbToLinear(), inkLight, inkDark,
			outlined: false);
		return root;
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
