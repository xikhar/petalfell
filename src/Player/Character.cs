using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;

namespace Petalfell.Player;

/// <summary>
/// The traveller.
///
/// A small voxel figure built from boxes, animated entirely procedurally: a
/// walk cycle driven by a single phase accumulator, squash-and-stretch on jump
/// and land, a cloak that trails with simple lag, and a breathing idle. No
/// skeletons, no clips — it is all curves, which keeps the motion snappy and
/// easy to tune.
///
/// Every hue in this world is warm or violet: sage grass, terracotta soil,
/// dusty-rose blossom, lilac stone. So the traveller is deliberately the one
/// COOL thing in frame. Muted petrol appears nowhere in the terrain, which
/// means the figure separates from whatever they happen to be standing against
/// — grass, cliff or blossom — without needing an outline to do it.
///
/// Dimensions are the reference project's, voxel for voxel. The drawn figure is
/// about 3.65 units — roughly twice the height of the 1.75-unit collision
/// capsule it rides on. That mismatch is deliberate and load-bearing: scaled to
/// its own capsule the traveller is a speck against the broad terrain shelves, and
/// the whole sense of scale collapses.
/// </summary>
public partial class Character : Node3D
{
	/// <summary>World units per character voxel.</summary>
	private const float S = 0.30f;

	/// <summary>
	/// An authored colour plus its ink class. The linear value is what the
	/// shader gets; the pale/dark decision is made on the original sRGB, where
	/// the 0.61 threshold was tuned.
	/// </summary>
	private readonly struct Tone
	{
		public readonly Color Linear;
		public readonly bool Pale;

		public Tone(uint hex)
		{
			var s = new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f);
			Linear = s.SrgbToLinear();
			Pale = s.R * 0.2126f + s.G * 0.7152f + s.B * 0.0722f >= Palette.LightFaceLuma;
		}
	}

	private static readonly Tone Skin = new(0xf3d2b8);
	private static readonly Tone Hood = new(0x8fb6ba);
	private static readonly Tone HoodDeep = new(0x74a0a6);
	private static readonly Tone Tunic = new(0xf7e9cf);
	private static readonly Tone Scarf = new(0xefb173);
	private static readonly Tone Satchel = new(0xd6a374);
	private static readonly Tone Leather = new(0x8a6660);
	private static readonly Tone Trouser = new(0x7e93a6);
	private static readonly Tone Boots = new(0x7c5f5c);

	private Node3D _body;      // everything that bobs
	private Node3D _legL, _legR, _armL, _armR, _head;
	private readonly List<Node3D> _cloak = new();
	private readonly List<float> _cloakLag = new();
	private readonly List<Node3D> _scarfTail = new();
	private readonly List<float> _scarfLag = new();

	private float _phase;
	private float _bob;

	private ShaderMaterial _inkLight, _inkDark;

	public void Setup(ShaderMaterial inkLight, ShaderMaterial inkDark)
	{
		// Translation still inherits the interpolated Player transform. Local body,
		// limb and outline animation is authored every render frame, so interpolating
		// it again retains a second nearby pose on fast motion.
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		_inkLight = inkLight;
		_inkDark = inkDark;

		_body = new Node3D { Name = "Body" };
		AddChild(_body);

		// Torso, belted, with the satchel strap crossing it.
		Part(_body, 4f, 5f, 2.6f, Tunic, 0f, 5.5f, 0f);
		Part(_body, 4.2f, 0.9f, 2.8f, Leather, 0f, 3.9f, 0f, outlined: false);

		// A rotated box is the cheapest diagonal available, and the diagonal is
		// what stops the chest reading as a plain rectangle from the front.
		var strap = Part(_body, 5.4f, 0.7f, 0.45f, Leather, 0f, 6.2f, 1.35f, outlined: false);
		strap.RotateZ(-0.58f);

		// Worn on one hip only. The asymmetry is doing most of the work of
		// making this figure recognisable in silhouette from any angle.
		Part(_body, 2.2f, 2.4f, 1.3f, Satchel, 2.7f, 4.5f, 0.2f, outlined: false);
		Part(_body, 2.3f, 0.5f, 1.4f, Leather, 2.7f, 5.6f, 0.2f, outlined: false);

		// Collar, sitting BELOW the chin rather than around the neck. At the neck
		// the head covered it completely from the front, which wasted the one
		// saturated colour on the model on a band only visible from above.
		Part(_body, 4.6f, 1.3f, 3.2f, Scarf, 0f, 6.4f, 0f, outlined: false);

		// Head, inside an open hood. Five slabs rather than one shell, so the
		// face is genuinely framed by an opening instead of being a cube with a
		// lighter square painted on the front; the rear peak gives the profile
		// its point. Only the crown carries ink — outlining every slab creates a
		// stack of tiny nested boxes at play zoom.
		_head = Pivot(_body, new Vector3(0, 8.6f * S, 0));
		Part(_head, 4.0f, 3.8f, 3.8f, Skin, 0f, 0f, 0f, outlined: false);
		Part(_head, 4.9f, 1.2f, 4.6f, Hood, 0f, 2.3f, -0.1f);
		Part(_head, 4.9f, 4.1f, 1.0f, Hood, 0f, 0.1f, -1.85f, outlined: false);
		Part(_head, 0.75f, 3.8f, 4.2f, Hood, -2.1f, 0f, -0.25f, outlined: false);
		Part(_head, 0.75f, 3.8f, 4.2f, Hood, 2.1f, 0f, -0.25f, outlined: false);
		Part(_head, 2.2f, 1.5f, 2.2f, HoodDeep, 0f, 3.0f, -1.5f, outlined: false);

		// Arms — cloak sleeves, so they belong to the hood rather than the tunic.
		_armL = Arm(-1);
		_armR = Arm(1);

		_legL = Leg(-1);
		_legR = Leg(1);

		// Separate articulated trails, matching the current reference. Their
		// overlapping panels are colour shapes inside the character silhouette;
		// outlining every panel produces a dark ladder at gameplay distance.
		Trail(new Vector3(0, 7.9f * S, -1.75f * S), 3, HoodDeep,
			3.6f, 2.2f, 0.55f, 0.45f, -2.0f, _cloak, _cloakLag);
		Trail(new Vector3(1.5f * S, 7.9f * S, -1.1f * S), 2, Scarf,
			1.5f, 2.3f, 0.6f, 0.25f, -2.1f, _scarfTail, _scarfLag);
	}

	private void Trail(Vector3 origin, int count, Tone tone,
		float w, float h, float d, float taper, float drop,
		List<Node3D> segments, List<float> lag)
	{
		Node3D parent = Pivot(_body, origin);
		for (int i = 0; i < count; i++)
		{
			var seg = Pivot(parent, new Vector3(0, i == 0 ? 0f : drop * S, 0f));
			Part(seg, w - i * taper, h, d, tone, 0f, h * -0.5f, 0f, outlined: false);
			segments.Add(seg);
			lag.Add(0f);
			parent = seg;
		}
	}

	private Node3D Arm(int side)
	{
		var pivot = Pivot(_body, new Vector3(side * 2.7f * S, 7.6f * S, 0f));
		Part(pivot, 1.6f, 3.6f, 1.6f, Hood, 0f, -1.5f, 0f);
		Part(pivot, 1.5f, 1.3f, 1.5f, Tunic, 0f, -3.9f, 0f, outlined: false);
		return pivot;
	}

	private Node3D Leg(int side)
	{
		var pivot = Pivot(_body, new Vector3(side * 1.1f * S, 3.2f * S, 0f));
		Part(pivot, 1.7f, 3.4f, 1.8f, Trouser, 0f, -1.5f, 0f);
		// The trouser already provides the leg silhouette. A second boxed stroke
		// around the boot reads as an overlapping internal grid at small scale.
		Part(pivot, 1.9f, 1.1f, 2.4f, Boots, 0f, -3.3f, 0.25f, outlined: false);
		return pivot;
	}

	private Node3D Pivot(Node3D parent, Vector3 at)
	{
		var n = new Node3D { Position = at };
		parent.AddChild(n);
		return n;
	}

	/// <summary>
	/// One part, positioned in character voxels. `outlined` is the plan's §15.3
	/// character exception: the exterior silhouette stays inked, but trim that
	/// sits flush against a larger part gets no edges of its own. At the default
	/// gameplay distance those internal lines pile into a scribble and the figure
	/// stops reading as one shape.
	/// </summary>
	private MeshInstance3D Part(Node3D parent, float w, float h, float d, Tone tone,
		float x, float y, float z, bool outlined = true)
	{
		float wx = w * S, wy = h * S, wz = d * S;

		var mesh = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(wx, wy, wz) },
			Position = new Vector3(x * S, y * S, z * S),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		};
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/character.gdshader") };
		mat.SetShaderParameter("albedo", tone.Linear);
		mat.SetShaderParameter("sun_dir", Palette.SunDir);
		mesh.MaterialOverride = mat;
		parent.AddChild(mesh);

		if (!outlined) return mesh;

		// A child, so every bit of the animation — the walk cycle, the squash,
		// the cloak lag — carries the outline along without any of it restated.
		var ink = new MeshInstance3D
		{
			Mesh = InkBuilder.Box(wx, wy, wz, tone.Pale),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			CustomAabb = new Aabb(new Vector3(-wx, -wy, -wz), new Vector3(wx * 2, wy * 2, wz * 2)),
		};
		ink.SetSurfaceOverrideMaterial(0, tone.Pale ? _inkLight : _inkDark);
		mesh.AddChild(ink);
		return mesh;
	}

	/// <summary>
	/// One phase accumulator drives everything. Speed sets the cadence, so a slow
	/// walk and a run are the same curve at different rates rather than two clips
	/// that have to be blended.
	/// </summary>
	public void Animate(Vector3 velocity, Vector3 facing,
		bool grounded, bool swimming, double delta)
	{
		float dt = (float)delta;
		var flat = new Vector3(velocity.X, 0, velocity.Z);
		float speed = flat.Length();

		var look = new Vector3(facing.X, 0, facing.Z);
		if (look.LengthSquared() > 0.0001f)
		{
			float yaw = Mathf.Atan2(look.X, look.Z);
			Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-14f * dt)), 0);
		}

		float cadence = Mathf.Clamp(speed / Controller.MaxSpeed, 0f, 1f);
		_phase += dt * (4.5f + cadence * 9.5f) * (cadence > 0.02f ? 1f : 0f);

		float swing = Mathf.Sin(_phase) * cadence * 0.85f;
		float counter = Mathf.Sin(_phase + Mathf.Pi) * cadence * 0.62f;

		if (swimming)
		{
			// Arms do the work, legs trail. A walk cycle in water reads as
			// someone marching along the bottom.
			_armL.Rotation = new Vector3(Mathf.Sin(_phase * 1.4f) * 1.15f - 0.6f, 0, 0);
			_armR.Rotation = new Vector3(Mathf.Sin(_phase * 1.4f + Mathf.Pi) * 1.15f - 0.6f, 0, 0);
			_legL.Rotation = new Vector3(swing * 0.35f - 0.5f, 0, 0);
			_legR.Rotation = new Vector3(counter * 0.35f - 0.5f, 0, 0);
		}
		else if (!grounded)
		{
			// Tuck on the way up, reach on the way down.
			float t = Mathf.Clamp(velocity.Y / Controller.JumpVel, -1f, 1f);
			_legL.Rotation = new Vector3(-0.5f - t * 0.35f, 0, 0);
			_legR.Rotation = new Vector3(-0.2f + t * 0.25f, 0, 0);
			_armL.Rotation = new Vector3(-1.1f - t * 0.4f, 0, 0);
			_armR.Rotation = new Vector3(-1.1f - t * 0.4f, 0, 0);
		}
		else
		{
			_legL.Rotation = new Vector3(swing, 0, 0);
			_legR.Rotation = new Vector3(counter, 0, 0);
			_armL.Rotation = new Vector3(counter * 0.8f, 0, 0);
			_armR.Rotation = new Vector3(swing * 0.8f, 0, 0);
		}

		// Breathing idle plus a two-per-stride bob.
		float idle = Mathf.Sin(_phase * 0.35f + 1.3f) * 0.012f;
		_bob = Mathf.Lerp(_bob, Mathf.Abs(Mathf.Sin(_phase)) * cadence * 0.10f + idle,
			1f - Mathf.Exp(-18f * dt));

		_body.Position = new Vector3(0, _bob, 0);

		// Keep the voxel proportions rigid in the air. Jump height and limb posing
		// provide the motion; scaling the whole body made the player visibly stretch.
		_body.Scale = Vector3.One;

		_head.Rotation = new Vector3(Mathf.Sin(_phase * 0.5f) * 0.04f,
			Mathf.Sin(_phase * 0.31f) * 0.09f, 0);

		// Each cloak segment chases the one above it, so the trail bends rather
		// than swinging as one rigid slab.
		float target = 0.16f + cadence * 0.55f + (grounded ? 0f : 0.5f);
		for (int i = 0; i < _cloak.Count; i++)
		{
			float want = target + Mathf.Sin(_phase * 0.5f + i) * cadence * 0.06f;
			_cloakLag[i] = Mathf.Lerp(_cloakLag[i], want, 1f - Mathf.Exp(-(9f - i * 2f) * dt));
			_cloak[i].Rotation = new Vector3(_cloakLag[i], 0,
				Mathf.Sin(_phase * 0.7f + i * 1.1f) * 0.05f * (0.4f + cadence));
		}

		// The scarf is lighter cloth: it reacts faster and sweeps farther than
		// the cape, preserving the model's one warm accent while it moves.
		for (int i = 0; i < _scarfTail.Count; i++)
		{
			float want = 0.10f + cadence * 0.95f + (grounded ? 0f : 0.75f)
				+ Mathf.Sin(_phase * 0.9f + i * 1.4f) * cadence * 0.16f;
			_scarfLag[i] = Mathf.Lerp(_scarfLag[i], want,
				1f - Mathf.Exp(-(13f - i * 3f) * dt));
			_scarfTail[i].Rotation = new Vector3(_scarfLag[i], 0,
				Mathf.Sin(_phase * 1.5f + i * 1.7f) * 0.13f * (0.5f + cadence)
				- cadence * 0.10f);
		}
	}
}
