using System;
using Godot;
using Petalfell.Items;

namespace Petalfell.Render;

/// <summary>
/// A small reusable voxel flame and its real local light. The geometry supplies
/// the readable warm source while the OmniLight3D illuminates terrain,
/// characters and props around it. Torch and campfire visuals use the same
/// language instead of maintaining unrelated fire effects.
/// </summary>
public partial class FireGlow : Node3D
{
	private OmniLight3D _light;
	private Node3D _flame;
	private float _baseEnergy;
	private float _phase;
	private float _visualScale;

	public void Setup(HeldLightProfile profile, float visualScale = 1f)
	{
		profile ??= new HeldLightProfile();
		_visualScale = Math.Max(0.1f, visualScale);
		_baseEnergy = profile.Energy;
		_phase = (float)(GetInstanceId() % 97) * 0.37f;

		_flame = new Node3D { Name = "Flame" };
		AddChild(_flame);
		AddFlameBox(new Vector3(0.22f, 0.42f, 0.22f),
			new Vector3(0f, 0.08f, 0f), new Color(1.00f, 0.34f, 0.10f), 2.5f);
		AddFlameBox(new Vector3(0.13f, 0.31f, 0.13f),
			new Vector3(0.035f, 0.18f, 0.025f), new Color(1.00f, 0.82f, 0.32f), 3.2f);
		_flame.Scale = Vector3.One * _visualScale;

		_light = new OmniLight3D
		{
			Name = "WarmLight",
			Position = new Vector3(0f, 0.10f * _visualScale, 0f),
			LightColor = profile.Color,
			LightEnergy = profile.Energy,
			LightSpecular = 0f,
			LightSize = profile.SourceRadius,
			OmniRange = profile.Range,
			OmniAttenuation = 1.18f,
			ShadowEnabled = profile.CastShadows,
			ShadowBlur = profile.ShadowBlur,
			ShadowOpacity = profile.ShadowOpacity,
			LightVolumetricFogEnergy = 0f,
			LightBakeMode = Light3D.BakeMode.Dynamic,
		};
		AddChild(_light);
	}

	private void AddFlameBox(Vector3 size, Vector3 at, Color color, float emission)
	{
		var material = new ShaderMaterial
		{
			Shader = GD.Load<Shader>("res://shaders/character.gdshader"),
		};
		material.SetShaderParameter("albedo", color.SrgbToLinear());
		material.SetShaderParameter("emission_amount", emission);
		_flame.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			Position = at,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	public override void _Process(double delta)
	{
		_phase += (float)delta;
		float slow = Mathf.Sin(_phase * 7.1f);
		float quick = Mathf.Sin(_phase * 13.7f + 1.8f);
		float flicker = 0.93f + slow * 0.045f + quick * 0.025f;
		if (_light != null) _light.LightEnergy = _baseEnergy * flicker;
		if (_flame != null)
		{
			_flame.Scale = new Vector3(
				_visualScale * (1f - quick * 0.025f),
				_visualScale * (1f + slow * 0.075f),
				_visualScale * (1f + quick * 0.025f));
			_flame.Rotation = new Vector3(0f, slow * 0.10f, quick * 0.035f);
		}
	}
}
