using Godot;
using Petalfell.Core;
using Petalfell.Render;

namespace Petalfell.World.Sites;

/// <summary>
/// Places the author's Meshy reconstructions over the conservative voxel
/// support/collision core. Each imported GLB remains a site-specific transcription;
/// this class only normalises scale, pivot and permanent atlas placement.
/// </summary>
public static class Reference12SculptureDetail
{
	public const string SiteId = "fallen-colossus";
	private const string HeadPath = "res://assets/sites/fallen-colossus-head.glb";
	private const string LegsPath = "res://assets/sites/fallen-colossus-legs.glb";

	public static Node3D Build(AtlasSectorWindow window, ReferenceSiteDefinition site,
		ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		if (window == null || site?.SiteId != SiteId) return null;
		var root = new Node3D { Name = "FallenColossusMeshyAssets" };

		int localX = site.Origin.X - window.Data.OriginX;
		int localZ = site.Origin.Z - window.Data.OriginZ;
		// The voxel blueprint's centre is the Y=44 statue dais. Recovering its
		// translation keeps the external meshes attached to both compiled review
		// terrain and the fast map-guided production terrain.
		int verticalOffset = window.Grid.Top[localZ * window.Grid.Size + localX] - 44;
		root.Position = new Vector3(localX, verticalOffset, localZ);
		root.Rotation = new Vector3(0f, Mathf.DegToRad(site.AxisDegrees), 0f);

		// Meshy normalises each generation independently. The measured scales below
		// restore the player/reference relation: the head keeps its image-facing
		// diagonal, while the author's later correction makes the forward-facing legs
		// 1.5 times their first imported review scale without moving their footing.
		ShaderMaterial stone = WorldMaterials.CreateSculptureStone(
			Palette.Get(Palette.STONE_PALE).Top, 0.78f, 0.10f);
		stone.NextPass = WorldMaterials.CreateSculptureOutline();
		root.AddChild(Place(HeadPath, "FallenHead", new Vector3(25f, 40f, 19f),
			13f, -45f, stone));
		root.AddChild(Place(LegsPath, "TrunklessLegs", new Vector3(0f, 44f, 0f),
			19.5f, 0f, stone));
		root.AddChild(BuildCollision());
		return root;
	}

	private static StaticBody3D BuildCollision()
	{
		var body = new StaticBody3D { Name = "FallenColossusCollision" };
		AddBox(body, "LeftLegCollision", new Vector3(-7.5f, 59f, 0f),
			new Vector3(12f, 30f, 15f), 0f);
		AddBox(body, "RightLegCollision", new Vector3(7.5f, 59f, 0f),
			new Vector3(12f, 30f, 15f), 0f);
		AddBox(body, "HeadCollision", new Vector3(25f, 48f, 19f),
			new Vector3(23f, 16f, 21f), -45f);
		return body;
	}

	private static void AddBox(StaticBody3D body, string name, Vector3 position,
		Vector3 size, float yawDegrees)
	{
		body.AddChild(new CollisionShape3D
		{
			Name = name,
			Position = position,
			Rotation = new Vector3(0f, Mathf.DegToRad(yawDegrees), 0f),
			Shape = new BoxShape3D { Size = size }
		});
	}

	private static Node3D Place(string path, string name, Vector3 position,
		float scale, float yawDegrees, Material material)
	{
		PackedScene scene = GD.Load<PackedScene>(path) ??
			throw new System.InvalidOperationException(
				$"Reference 12 Meshy asset '{path}' was not imported");
		Node3D model = scene.Instantiate<Node3D>();
		model.Name = name;
		model.Position = position;
		model.Scale = Vector3.One * scale;
		model.Rotation = new Vector3(0f, Mathf.DegToRad(yawDegrees), 0f);
		foreach (Node child in model.FindChildren("*", "MeshInstance3D", true, false))
			if (child is MeshInstance3D mesh)
			{
				mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
				mesh.MaterialOverride = material;
			}
		return model;
	}
}
