using System;
using Godot;
using Petalfell.Core;

namespace Petalfell.Render;

/// <summary>
/// The clock, and everything the sky does because of it.
///
/// One node owns the whole atmosphere at runtime — sun, moon, ambient, fog,
/// glow, sky and the shader globals — because every one of those has to agree
/// about what time it is. Split across several owners they drift, and the seam
/// shows immediately: a warm sun under a midnight sky, or fog that stays noon
/// pale while the light goes out.
///
/// THE KEY LIGHT IS ONE LIGHT. It is the sun while the sun is up and the moon
/// after it sets, swapping direction to the opposite side of the sky. Two
/// directional lights fighting over the same shadow map is a real cost for a
/// benefit nobody can see at this camera distance, and a single key means the
/// shadows never double or pop at the moment of the handover.
///
/// Shader values travel as GLOBAL uniforms rather than per-material parameters.
/// The character shader alone builds a fresh material for every box of every
/// figure in the world, so a cycle that pushed the sun direction to each one
/// would spend its whole frame chasing references that come and go with
/// streaming. Registered once, set once a frame, read by everything.
/// </summary>
public partial class DayCycle : Node
{
	public const string SunDirParam = "pf_sun_dir";
	public const string NightParam = "pf_night";
	public const string SunColourParam = "pf_sun_colour";

	/// <summary>0 midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset.</summary>
	public float TimeOfDay = 0.34f;
	/// <summary>Real seconds for one full cycle.</summary>
	public float DayLength = 900f;
	public bool Paused;
	/// <summary>Authored darkness blend: 0 in daylight, 1 at full night.</summary>
	public float NightAmount { get; private set; }

	private Godot.Environment _env;
	private DirectionalLight3D _key;
	private DirectionalLight3D _fill;
	private ShaderMaterial _sky;
	private ShaderMaterial _water;

	/// <summary>
	/// Register the globals before anything that reads them is compiled.
	///
	/// A shader naming a global uniform that does not exist yet fails to compile,
	/// and the failure surfaces as an untextured object rather than as an error
	/// anyone would connect to this — so this runs first thing at boot, before a
	/// single material is built.
	/// </summary>
	private static bool _registered;

	public static void RegisterGlobals()
	{
		// Guarded by a flag rather than by asking the server what already exists:
		// GlobalShaderParameterGetList is editor-only and logs a severe-performance
		// error when called from a running game.
		if (_registered) return;
		_registered = true;

		static void Add(string name, RenderingServer.GlobalShaderParameterType type, Variant value)
			=> RenderingServer.GlobalShaderParameterAdd(name, type, value);

		Add(SunDirParam, RenderingServer.GlobalShaderParameterType.Vec3, Palette.SunDir);
		Add(NightParam, RenderingServer.GlobalShaderParameterType.Float, 0f);
		Add(SunColourParam, RenderingServer.GlobalShaderParameterType.Vec3,
			new Vector3(Palette.SunColor.R, Palette.SunColor.G, Palette.SunColor.B));
	}

	public void Setup(Godot.Environment env, DirectionalLight3D key, DirectionalLight3D fill,
		ShaderMaterial sky, ShaderMaterial water)
	{
		_env = env;
		_key = key;
		_fill = fill;
		_sky = sky;
		_water = water;
		ProcessPriority = -50;   // before anything reads the light this frame
		_applied = -1f;
		Apply();
	}

	/// <summary>
	/// Smallest change in time worth re-applying.
	///
	/// Every Apply() moves the key light, which invalidates all four shadow
	/// cascades, and rewrites the sky, which re-bakes the ambient radiance. Doing
	/// that at frame rate is enormously expensive and completely pointless: over
	/// a fifteen-minute day the sun travels a quarter of a degree between these
	/// steps, which nothing on screen can resolve. This is the difference between
	/// a day cycle that costs almost nothing and one that halves the frame rate.
	/// </summary>
	private const float Quantum = 1f / 2048f;

	private float _applied = -1f;

	public override void _Process(double delta)
	{
		if (!Paused && DayLength > 0.01f)
			TimeOfDay = Mathf.PosMod(TimeOfDay + (float)delta / DayLength, 1f);

		float step = Mathf.Floor(TimeOfDay / Quantum);
		if (Mathf.IsEqualApprox(step, _applied)) return;
		_applied = step;
		Apply();
	}

	/// <summary>Blend the keyframe table at the current time, wrapping midnight.</summary>
	private static Palette.SkyState Sample(float t, out float mix, out Palette.SkyState next)
	{
		var keys = Palette.Day;
		int i = keys.Length - 1;
		for (int k = 0; k < keys.Length; k++)
			if (keys[k].At <= t) i = k;

		int j = (i + 1) % keys.Length;
		float a = keys[i].At;
		float b = keys[j].At;
		// The last key wraps forward past midnight into the first.
		float span = b > a ? b - a : b + 1f - a;
		float into = t >= a ? t - a : t + 1f - a;
		mix = span < 0.0001f ? 0f : Mathf.Clamp(into / span, 0f, 1f);
		next = keys[j];
		return keys[i];
	}

	private void Apply()
	{
		var from = Sample(TimeOfDay, out float k, out var to);

		Color Blend(Color x, Color y) => x.Lerp(y, k);
		float Lerp(float x, float y) => Mathf.Lerp(x, y, k);

		// Where the sun is.
		//
		// The arc is built around the authored noon direction rather than a
		// vertical: Palette.SunDir is the angle the whole look was tuned at, so
		// making it the apex means midday still matches every capture ever taken
		// of this project, and dawn and dusk fall out of the same circle.
		float ang = (TimeOfDay - 0.25f) * Mathf.Tau;
		Vector3 high = Palette.SunDir;
		Vector3 east = high.Cross(Vector3.Up).Normalized();
		Vector3 sun = (east * Mathf.Cos(ang) + high * Mathf.Sin(ang)).Normalized();

		// One key light: the sun by day, the moon after it goes down.
		bool daylight = sun.Y > 0f;
		Vector3 keyDir = daylight ? sun : -sun;

		float night = Lerp(from.Night, to.Night);
		NightAmount = night;
		var sunColour = Blend(from.Sun, to.Sun);
		float energy = Lerp(from.SunEnergy, to.SunEnergy);

		if (_key != null)
		{
			_key.LightColor = sunColour;
			_key.LightEnergy = energy;
			_key.ShadowOpacity = Lerp(from.ShadowOpacity, to.ShadowOpacity);
			// A light exactly on the horizon casts shadows the length of the world
			// and the cascade cannot hold them, so the key is never allowed all
			// the way down. It goes dim instead, which is what sunset looks like.
			var aimed = keyDir;
			if (aimed.Y < 0.10f) aimed = new Vector3(aimed.X, 0.10f, aimed.Z).Normalized();
			_key.LookAtFromPosition(aimed * 120f, Vector3.Zero, Vector3.Up);
		}

		if (_fill != null)
			_fill.LightEnergy = Mathf.Lerp(0.10f, 0.05f, night);

		if (_env != null)
		{
			_env.AmbientLightColor = Blend(from.Ambient, to.Ambient);
			_env.AmbientLightEnergy = Lerp(from.AmbientEnergy, to.AmbientEnergy);
			_env.AmbientLightSkyContribution = Lerp(from.SkyMix, to.SkyMix);
			var fog = Blend(from.Fog, to.Fog);
			_env.FogLightColor = fog;
			// Lanterns and lit windows have to clear the glow threshold at night
			// and must not at noon, or every pale surface in a pale world blooms.
			_env.GlowHdrThreshold = Lerp(from.GlowThreshold, to.GlowThreshold);
			_env.GlowIntensity = Mathf.Lerp(0.55f, 0.85f, night);
		}

		if (_sky != null)
		{
			_sky.SetShaderParameter("zenith", Blend(from.Zenith, to.Zenith));
			_sky.SetShaderParameter("horizon", Blend(from.Horizon, to.Horizon));
			_sky.SetShaderParameter("ground", Blend(from.Ground, to.Ground));
			_sky.SetShaderParameter("sun_tint", sunColour);
			_sky.SetShaderParameter("sun_dir", sun);
			_sky.SetShaderParameter("night", night);
			// The moon is opposite the sun and only drawn once it is up.
			_sky.SetShaderParameter("moon_dir", -sun);
		}

		if (_water != null)
		{
			// The lake reflects the sky it is actually under.
			_water.SetShaderParameter("sky_low", Blend(from.Horizon, to.Horizon));
			_water.SetShaderParameter("sky_high", Blend(from.Zenith, to.Zenith));
			_water.SetShaderParameter("sun_colour", sunColour);
			_water.SetShaderParameter("sun_dir", keyDir);
		}

		RenderingServer.GlobalShaderParameterSet(SunDirParam, keyDir);
		RenderingServer.GlobalShaderParameterSet(NightParam, night);
		RenderingServer.GlobalShaderParameterSet(SunColourParam,
			new Vector3(sunColour.R, sunColour.G, sunColour.B));
	}

	/// <summary>Clock reading, for the developer overlay.</summary>
	public string Clock()
	{
		float hours = TimeOfDay * 24f;
		int h = Mathf.FloorToInt(hours);
		int m = Mathf.FloorToInt((hours - h) * 60f);
		return $"{h:00}:{m:00}";
	}
}
