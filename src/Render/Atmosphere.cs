using Godot;
using Petalfell.Core;

namespace Petalfell.Render;

/// <summary>
/// Lighting, sky and air.
///
/// Distance dissolving is the single effect that does most of the "this is a
/// miniature" work in the reference images: foreground crisp, midground soft,
/// background nearly sky. Fog colour, the sky's horizon band and the glow
/// threshold all have to agree, or the seam shows as a ring around the world.
///
/// Shadows are high quality and slightly darker than a washed-out pastel would
/// suggest — important characters and structures have to stay grounded at every
/// intended camera distance.
/// </summary>
public static class Atmosphere
{
	public static WorldEnvironment Build()
	{
		var env = new Godot.Environment();

		var sky = new Sky();
		var skyMat = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/sky.gdshader"),
		};
		skyMat.SetShaderParameter("zenith", Palette.SkyZenith);
		skyMat.SetShaderParameter("horizon", Palette.SkyHorizon);
		skyMat.SetShaderParameter("ground", Palette.SkyGround);
		skyMat.SetShaderParameter("sun_tint", Palette.SunTint);
		skyMat.SetShaderParameter("sun_dir", Palette.SunDir);
		sky.SkyMaterial = skyMat;

		env.BackgroundMode = Godot.Environment.BGMode.Sky;
		env.Sky = sky;
		env.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
		env.AmbientLightSkyContribution = 1.0f;
		// The reference's intensities are three.js units and do not survive the
		// trip: sun 2.2 plus a full-strength sky ambient pushes a pastel palette
		// straight through the ACES shoulder and the whole world comes out
		// white. Everything below is retuned for Godot's scale, with the
		// palette itself left alone — the colour is right, the exposure was not.
		env.AmbientLightEnergy = 0.50f;

		// Aerial perspective. The near plane matters as much as the density: a
		// haze that starts at the camera puts fog on the block in front of you,
		// which is exactly where the world should still be crisp.
		env.FogEnabled = true;
		env.FogMode = Godot.Environment.FogModeEnum.Depth;
		env.FogLightColor = Palette.SkyHaze;
		env.FogLightEnergy = 1.0f;
		env.FogSunScatter = 0.12f;
		env.FogDepthBegin = 96f;
		env.FogDepthEnd = 285f;
		env.FogDepthCurve = 1.35f;
		env.FogDensity = 1.0f;
		// A second, whiter medium pooling in the valleys and around the rim.
		env.FogHeightDensity = 0.006f;
		env.FogHeight = Palette.WaterLevel + 6f;

		// Only genuine emitters bloom. A low threshold catches most of a pastel
		// palette outright and the whole frame goes soft.
		env.GlowEnabled = true;
		env.GlowIntensity = 0.55f;
		env.GlowStrength = 0.95f;
		env.GlowBloom = 0.05f;
		env.GlowHdrThreshold = 1.15f;
		env.GlowHdrScale = 2.0f;
		env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;

		env.TonemapMode = Godot.Environment.ToneMapper.Aces;
		env.TonemapExposure = Palette.GradeExposure;
		env.TonemapWhite = 6.0f;

		// Large-scale contact shading. The mesher bakes AO into block crevices;
		// what it cannot see is a canopy hanging over grass or the base of a
		// cliff.
		env.SsaoEnabled = true;
		env.SsaoRadius = 2.6f;
		env.SsaoIntensity = 0.65f;
		env.SsaoPower = 1.4f;
		env.SsaoLightAffect = 0.25f;
		env.SsaoHorizon = 0.10f;

		env.AdjustmentEnabled = false;   // the canvas grade owns display space

		return new WorldEnvironment { Environment = env, Name = "Atmosphere" };
	}

	public static DirectionalLight3D Sun()
	{
		var sun = new DirectionalLight3D
		{
			Name = "Sun",
			LightColor = Palette.SunColor,
			LightEnergy = 0.55f,
			// Slightly darker than a washed-out pastel, without becoming harsh.
			ShadowEnabled = true,
			ShadowBias = 0.035f,
			ShadowNormalBias = 1.4f,
			ShadowBlur = 1.9f,
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
			DirectionalShadowMaxDistance = 260f,
			DirectionalShadowSplit1 = 0.06f,
			DirectionalShadowSplit2 = 0.16f,
			DirectionalShadowSplit3 = 0.42f,
		};
		sun.LookAtFromPosition(Palette.SunDir * 100f, Vector3.Zero, Vector3.Up);
		return sun;
	}

	/// <summary>
	/// A camera-side fill so shadowed faces never go muddy. The key is barely
	/// tinted — the pink in this world comes from the surfaces, not the lamp.
	/// </summary>
	public static DirectionalLight3D Fill()
	{
		var fill = new DirectionalLight3D
		{
			Name = "Fill",
			LightColor = Palette.FillColor,
			LightEnergy = 0.10f,
			ShadowEnabled = false,
			LightSpecular = 0f,
		};
		fill.LookAtFromPosition(new Vector3(0.62f, 0.45f, 0.65f) * 100f, Vector3.Zero, Vector3.Up);
		return fill;
	}
}
