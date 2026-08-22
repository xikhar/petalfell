using Godot;

namespace Petalfell.Render;

/// <summary>
/// Planar reflection for the lake.
///
/// The world is rendered a second time from the camera's mirror image below the
/// surface. Because mirroring is an isometry that fixes the water plane, a point
/// ON the plane lands at the SAME screen position in both views — which is what
/// makes this cheap to use: the water shader looks the reflection up at its own
/// SCREEN_UV, with no projection maths of its own, and every reflection is
/// anchored under the thing casting it.
///
/// Two details make it work rather than nearly work:
///
/// The mirrored basis has a NEGATIVE determinant — a mirror swaps handedness —
/// and Godot culls on screen-space winding, so rendering through it would show
/// the world inside out. Negating the camera's X axis puts the determinant back
/// to positive at the cost of a horizontally flipped image, which the shader
/// undoes when it samples. Correct winding, correct reflection.
///
/// The water itself is excluded by layer, or the reflection pass would draw the
/// lake into the texture the lake reads from.
///
/// WHAT THIS CANNOT DO, since it cost a good deal of time to establish: a
/// reflected ray leaves the water at the same angle the view ray arrived, so at
/// the rig's thirty-three degree pitch every one of them clears a bank of height
/// h within about one and a half h of the shore and sees nothing but sky after
/// that. Reflections are therefore SHORT, and in the middle of open water there
/// are none at all — correctly. Two ways round it were tried and both are dead
/// ends worth not repeating: bending the lookup inside the mirrored frame finds
/// no grazing content because a twenty-one degree lens spans twenty-one degrees
/// of elevation, and a wide level probe from the middle of the lake comes back
/// blank because everything it can see across the water is past the fog. The
/// shader stretches what this pass produces instead.
///
/// Half resolution. A reflection off a rippled surface is smeared by the ripple
/// before anyone sees it, and this is a whole extra scene render per frame.
/// </summary>
public partial class PlanarReflection : Node3D
{
	/// <summary>Visual layer reserved for the water surface and the things floating on it.</summary>
	public const uint WaterLayer = 1u << 19;

	private const uint AllLayers = 0xFFFFF;

	private SubViewport _viewport;
	private Camera3D _camera;
	private Camera3D _main;
	private ShaderMaterial _water;
	private float _planeY;
	private Vector2I _size = Vector2I.Zero;

	/// <summary>Fraction of the main viewport's resolution to render at.</summary>
	public float Resolution = 0.5f;

	public void Setup(Camera3D main, ShaderMaterial water, float planeY)
	{
		_main = main;
		_water = water;
		_planeY = planeY;
	}

	public override void _Ready()
	{
		// After Main has placed the rig for this frame. Reflecting last frame's
		// camera puts the reflection a frame behind the world it reflects, which
		// is visible as a lag in the banks whenever the camera turns.
		ProcessPriority = 100;

		_viewport = new SubViewport
		{
			Name = "ReflectionViewport",
			// No own world: this has to be the SAME world, or the reflection shows
			// an empty scene under the correct sky.
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			RenderTargetClearMode = SubViewport.ClearMode.Always,
			HandleInputLocally = false,
			Msaa3D = Viewport.Msaa.Disabled,
			Size = new Vector2I(2, 2),
		};
		AddChild(_viewport);

		_camera = new Camera3D
		{
			Name = "ReflectionCamera",
			Current = true,
			Projection = Camera3D.ProjectionType.Perspective,
			// Everything except the water plane and what floats on it.
			CullMask = AllLayers & ~WaterLayer,
		};
		_viewport.AddChild(_camera);

		_water?.SetShaderParameter("reflect_tex", _viewport.GetTexture());
	}

	public override void _Process(double delta)
	{
		if (_main == null || _camera == null) return;

		var wanted = (Vector2I)(GetViewport().GetVisibleRect().Size * Resolution);
		wanted = new Vector2I(Mathf.Max(wanted.X, 8), Mathf.Max(wanted.Y, 8));
		if (wanted != _size)
		{
			_size = wanted;
			_viewport.Size = wanted;
		}

		// Match the lens, or the reflection is a different photograph of the same
		// world and nothing lines up along the shoreline.
		_camera.Fov = _main.Fov;
		_camera.Near = _main.Near;
		_camera.Far = _main.Far;
		_camera.KeepAspect = _main.KeepAspect;

		Transform3D t = _main.GlobalTransform;
		Basis b = t.Basis;

		// Reflect a direction through the horizontal plane.
		static Vector3 Mirror(Vector3 v) => new(v.X, -v.Y, v.Z);

		// X negated: see the note above about winding.
		_camera.GlobalTransform = new Transform3D(
			new Basis(-Mirror(b.X), Mirror(b.Y), Mirror(b.Z)),
			new Vector3(t.Origin.X, 2f * _planeY - t.Origin.Y, t.Origin.Z));
	}
}
