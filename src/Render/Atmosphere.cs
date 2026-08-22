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
		// Ambient taken entirely from the sky is ambient that is entirely
		// lavender, and at the energy this look needs it repaints every surface
		// its own colour: measured against raw albedo, sage grass arrived beige.
		// Half the fill comes from a warm neutral so the greens and terracottas
		// survive the wash while the frame keeps its lilac air.
		env.AmbientLightColor = new Color(1.0f, 0.965f, 0.93f);
		env.AmbientLightSkyContribution = 0.45f;
		// The reference's intensities are three.js units and do not survive the
		// trip: sun 2.2 plus a full-strength sky ambient pushes a pastel palette
		// straight through the ACES shoulder and the whole world comes out
		// white. Everything below is retuned for Godot's scale, with the
		// palette itself left alone — the colour is right, the exposure was not.
		// The references are lit almost entirely by ambient. Cast shadows are
		// barely present; what describes the form is the baked occlusion, the
		// face-direction ramp and the haze. A strong key with dark shadows is
		// the single fastest way to lose the storybook read.
		// High enough that cast shadows stay soft and the form is carried by
		// occlusion and the face ramp, but no higher: ACES desaturates as it
		// approaches white, so a pastel palette pushed past the mid range comes
		// out grey. Bright and saturated means keeping the values in the middle,
		// not turning everything up.
		env.AmbientLightEnergy = 0.62f;

		// Aerial perspective. The near plane matters as much as the density: a
		// haze that starts at the camera puts fog on the block in front of you,
		// which is exactly where the world should still be crisp.
		env.FogEnabled = true;
		env.FogMode = Godot.Environment.FogModeEnum.Depth;
		env.FogLightColor = Palette.SkyHaze;
		env.FogLightEnergy = 1.0f;
		env.FogSunScatter = 0.12f;
		// Distance dissolving does most of the "this is a miniature" work. In the
		// references, ground a hundred units out is already more than half gone
		// and the far hills are barely a lilac silhouette. Anything subtler than
		// this reads as a diorama photographed in clear air.
		// The play camera sits 75 units back, so anything the player is looking
		// at is already 60-120 units away. A fog that begins at 26 puts the
		// whole frame inside it and the world loses its colour entirely; it has
		// to clear the subject first and then fall away hard.
		env.FogDepthBegin = 78f;
		env.FogDepthEnd = 430f;
		env.FogDepthCurve = 1.6f;
		env.FogDensity = 1.0f;
		// A second, whiter medium pooling in the valleys and around the rim.
		env.FogHeightDensity = 0.004f;
		env.FogHeight = Palette.WaterLevel + 10f;

		// Only genuine emitters bloom. A low threshold catches most of a pastel
		// palette outright and the whole frame goes soft.
		// The references carry a constant dust of small bright motes and a soft
		// halo around every pale surface. That wants a threshold low enough to
		// catch the top of the pastel range, not only true emitters — the palette
		// never reaches 1.0, so a threshold above it produces no bloom at all.
		env.GlowEnabled = true;
		env.GlowIntensity = 0.55f;
		env.GlowStrength = 1.05f;
		env.GlowBloom = 0.18f;
		env.GlowHdrThreshold = 0.82f;
		env.GlowHdrScale = 2.4f;
		env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;
		// Wide, soft halo rather than a tight one: the upper mip levels are what
		// give the reference its bloom-lit air instead of a rim on each edge.
		env.SetGlowLevel(4, 1.0f);
		env.SetGlowLevel(5, 0.8f);
		env.SetGlowLevel(6, 0.5f);

		env.TonemapMode = Godot.Environment.ToneMapper.Aces;
		env.TonemapExposure = 1.0f;
		env.TonemapWhite = 6.0f;

		// Large-scale contact shading. The mesher bakes AO into block crevices;
		// what it cannot see is a canopy hanging over grass or the base of a
		// cliff.
		env.SsaoEnabled = true;
		env.SsaoRadius = 3.2f;
		env.SsaoIntensity = 0.85f;
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
			LightEnergy = 0.45f,
			// Slightly darker than a washed-out pastel, without becoming harsh.
			ShadowEnabled = true,
			// The references barely have cast shadows: form is carried by the
			// occlusion and the face ramp, and a full-strength shadow under
			// every canopy reads as a hole punched in the meadow.
			ShadowOpacity = 0.42f,
			ShadowBias = 0.035f,
			ShadowNormalBias = 1.4f,
			ShadowBlur = 3.4f,
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
			LightEnergy = 0.14f,
			ShadowEnabled = false,
			LightSpecular = 0f,
		};
		fill.LookAtFromPosition(new Vector3(0.62f, 0.45f, 0.65f) * 100f, Vector3.Zero, Vector3.Up);
		return fill;
	}
}
