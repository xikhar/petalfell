using Godot;
using Petalfell.Core;

namespace Petalfell.Render;

/// <summary>
/// Shared construction of the materials that define the finished world look.
/// Runtime sector review must use the same shaders and starting parameters as
/// ordinary play or it is only a second preview renderer with different truths.
/// </summary>
public static class WorldMaterials
{
	public readonly record struct InkSet(ShaderMaterial Light, ShaderMaterial Dark);

	public static InkSet CreateInk(float waterLevel, int priorityOffset = 0)
	{
		var shader = GD.Load<Shader>("res://shaders/ink.gdshader");
		ShaderMaterial Ink(int priority, int pass)
		{
			var material = new ShaderMaterial { Shader = shader, RenderPriority = priority };
			material.SetShaderParameter("ink_dark", Palette.InkDark);
			material.SetShaderParameter("ink_light", Palette.InkLight);
			material.SetShaderParameter("core_width", Palette.InkWidth);
			material.SetShaderParameter("ink_pass", pass);
			material.SetShaderParameter("water_level", waterLevel);
			return material;
		}

		var light = Ink(1 + priorityOffset, 0);
		var dark = Ink(2 + priorityOffset, 2);
		light.NextPass = Ink(3 + priorityOffset, 1);
		return new InkSet(light, dark);
	}

	public static ShaderMaterial CreateVoxel(float waterLevel)
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
		material.SetShaderParameter("sun_dir", Palette.SunDir);
		material.SetShaderParameter("plane_y", waterLevel);
		return material;
	}

	public static ShaderMaterial CreateDetail() =>
		new() { Shader = GD.Load<Shader>("res://shaders/detail.gdshader") };

	public static ShaderMaterial CreateWaterDetail() =>
		new() { Shader = GD.Load<Shader>("res://shaders/waterdetail.gdshader") };

	public static ShaderMaterial CreateWater(float waterLevel, bool surfaceFromMesh = false,
		bool reflectionAvailable = true)
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
		material.SetShaderParameter("shoal", Palette.WaterShoal);
		material.SetShaderParameter("shallow", Palette.WaterShallow);
		material.SetShaderParameter("deep", Palette.WaterDeep);
		material.SetShaderParameter("warm", Palette.WaterWarm);
		material.SetShaderParameter("sheen", Palette.WaterSheen);
		material.SetShaderParameter("sky_low", Palette.SkyHorizon);
		material.SetShaderParameter("sky_high", Palette.SkyZenith);
		material.SetShaderParameter("sun_colour", Palette.SunColor);
		material.SetShaderParameter("sun_dir", Palette.SunDir);
		material.SetShaderParameter("plane_y", waterLevel);
		material.SetShaderParameter("surface_from_mesh", surfaceFromMesh);
		// Atlas water differs only in where its surface position comes from. Its
		// multi-height tops and step curtains still use the legacy absorption,
		// refraction, caustics and moving-sheet response; overriding those values
		// here once collapsed every column into one opaque periwinkle stop.
		// A review window has no single reflection plane. Keep the real water's
		// sky response while the multi-plane reflection compositor remains future work.
		if (!reflectionAvailable) material.SetShaderParameter("reflect_mix", 0f);
		return material;
	}

	public static CanvasLayer CreateGrade()
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grade.gdshader") };
		material.SetShaderParameter("lift", Palette.GradeLift);
		material.SetShaderParameter("gamma_", Palette.GradeGamma);
		material.SetShaderParameter("gain", Palette.GradeGain);
		material.SetShaderParameter("saturation", Palette.GradeSaturation);
		material.SetShaderParameter("contrast", Palette.GradeContrast);
		material.SetShaderParameter("vignette", Palette.GradeVignette);

		var rect = new ColorRect
		{
			Material = material,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		var layer = new CanvasLayer { Name = "Grade", Layer = 100 };
		layer.AddChild(rect);
		return layer;
	}
}
