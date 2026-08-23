using System;
using Godot;

namespace Petalfell.Render;

/// <summary>
/// Camera-independent crossed planes carrying the procedural campfire shader.
/// Lighting is intentionally absent: the owning campfire supplies its physical
/// light separately, while this node owns only flame, emission and heat haze.
/// </summary>
public partial class CampfireFlame : Node3D
{
	private const float Width = 1.72f;
	private const float Height = 2.25f;
	private MeshInstance3D _visual;

	/// <summary>Create the visual once and set its uniform world scale.</summary>
	public void Setup(float scale = 1f)
	{
		float safeScale = Math.Max(scale, 0.10f);
		if (_visual == null)
		{
			var material = new ShaderMaterial
			{
				Shader = GD.Load<Shader>("res://shaders/fire.gdshader"),
				// Terrain ink uses priorities one through three. Drawing the flame at
				// zero leaves those clean authored strokes above the soft volume.
				RenderPriority = 0,
			};

			_visual = new MeshInstance3D
			{
				Name = "CrossedFlame",
				Mesh = CrossedPlanes(),
				MaterialOverride = material,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(_visual);
		}

		Scale = Vector3.One * safeScale;
	}

	private static ArrayMesh CrossedPlanes()
	{
		float half = Width * 0.5f;
		var vertices = new[]
		{
			// Plane across X.
			new Vector3(-half, 0f, 0f),
			new Vector3( half, 0f, 0f),
			new Vector3( half, Height, 0f),
			new Vector3(-half, Height, 0f),
			// Plane across Z.
			new Vector3(0f, 0f, -half),
			new Vector3(0f, 0f,  half),
			new Vector3(0f, Height,  half),
			new Vector3(0f, Height, -half),
		};
		var uv = new[]
		{
			new Vector2(0f, 1f), new Vector2(1f, 1f),
			new Vector2(1f, 0f), new Vector2(0f, 0f),
			new Vector2(0f, 1f), new Vector2(1f, 1f),
			new Vector2(1f, 0f), new Vector2(0f, 0f),
		};
		var colors = new[]
		{
			new Color(0f, 0f, 0f), new Color(0f, 0f, 0f),
			new Color(0f, 0f, 0f), new Color(0f, 0f, 0f),
			new Color(0.63f, 0f, 0f), new Color(0.63f, 0f, 0f),
			new Color(0.63f, 0f, 0f), new Color(0.63f, 0f, 0f),
		};
		var indices = new[]
		{
			0, 1, 2, 0, 2, 3,
			4, 5, 6, 4, 6, 7,
		};

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.TexUV] = uv;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}
}
