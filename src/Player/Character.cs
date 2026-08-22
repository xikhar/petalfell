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
	/// <summary>Centre of mass used to pitch the swimmer without swinging them around their feet.</summary>
	private const float SwimPivotHeight = 5.3f * S;

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

	private static readonly Tone Skin = new(0xf7ddca);
	private static readonly Tone Hood = new(0xa2c5c8);
	private static readonly Tone HoodDeep = new(0x7aa2a8);
	private static readonly Tone Tunic = new(0xfbf1dd);
	private static readonly Tone Scarf = new(0xf4c58f);
	private static readonly Tone Satchel = new(0xe2b98d);
	private static readonly Tone Leather = new(0xa17c75);
	private static readonly Tone Trouser = new(0x889dae);
	private static readonly Tone Boots = new(0x92736e);

	private Node3D _swimPivot; // pitches the complete figure around its torso
	private Node3D _body;      // everything that bobs
	private Node3D _legL, _legR, _armL, _armR, _head;
	private readonly List<Node3D> _cloak = new();
	private readonly List<float> _cloakLag = new();
	private readonly List<Node3D> _scarfTail = new();
	private readonly List<float> _scarfLag = new();

	private float _phase;
	private float _swimPhase;
	private float _swimBlend;
	private float _airBlend;
	private float _airLead = 1f;
	private bool _wasAirborne;
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

		// Keep the visual origin at the feet for ordinary movement, but put the
		// swimming rotation around the torso. Rotating the old body node directly
		// swung the head almost a character-length into a bank.
		_swimPivot = new Node3D
		{
			Name = "SwimPivot",
			Position = new Vector3(0f, SwimPivotHeight, 0f),
		};
		AddChild(_swimPivot);

		_body = new Node3D
		{
			Name = "Body",
			Position = new Vector3(0f, -SwimPivotHeight, 0f),
		};
		_swimPivot.AddChild(_body);

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

		// Water thresholds are intentionally binary in the controller; the pose is
		// not. Enter promptly so the figure does not fall upright through the lake,
		// then return to standing a little more slowly while climbing onto a bank.
		float swimResponse = swimming ? 9.0f : 5.5f;
		_swimBlend = Mathf.Lerp(_swimBlend, swimming ? 1f : 0f,
			1f - Mathf.Exp(-swimResponse * dt));
		if (!swimming && _swimBlend < 0.001f) _swimBlend = 0f;
		float swim = _swimBlend;

		// Alternate the leading side on each takeoff. This gives jumps a readable
		// stride rather than sending all four limbs forward in the same pose.
		bool airborne = !grounded && !swimming;
		if (airborne && !_wasAirborne) _airLead = -_airLead;
		_wasAirborne = airborne;
		_airBlend = Mathf.Lerp(_airBlend, airborne ? 1f : 0f,
			1f - Mathf.Exp(-(airborne ? 14f : 18f) * dt));
		if (!airborne && _airBlend < 0.001f) _airBlend = 0f;
		float air = _airBlend;

		var look = new Vector3(facing.X, 0, facing.Z);
		if (look.LengthSquared() > 0.0001f)
		{
			float yaw = Mathf.Atan2(look.X, look.Z);
			Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, 1f - Mathf.Exp(-14f * dt)), 0);
		}

		float cadence = Mathf.Clamp(speed / Controller.MaxSpeed, 0f, 1f);
		_phase += dt * (4.5f + cadence * 9.5f) * (cadence > 0.02f ? 1f : 0f);

		// Unlike the walk clock, this never stops in water. A stationary swimmer
		// needs a slow scull and flutter rather than a frozen mid-stroke pose.
		float swimMotion = Ease01(Mathf.Clamp(speed / Controller.SwimSpeed, 0f, 1f));
		if (swimming || swim > 0f)
			_swimPhase += dt * Mathf.Lerp(1.75f, 4.85f, swimMotion);

		float swing = Mathf.Sin(_phase) * cadence * 0.85f;
		float counter = Mathf.Sin(_phase + Mathf.Pi) * cadence * 0.62f;
		var groundLegL = new Vector3(swing, 0f, 0f);
		var groundLegR = new Vector3(counter, 0f, 0f);
		var groundArmL = new Vector3(counter * 0.8f, 0f, 0f);
		var groundArmR = new Vector3(swing * 0.8f, 0f, 0f);

		// The legs are the jump's power source: both extend behind the body during
		// takeoff, relax slightly near the apex, then lengthen while remaining just
		// behind the torso for landing. Vertical velocity supplies a continuous phase without a
		// separate animation clock that can drift away from the real arc.
		float vertical = Mathf.Clamp(velocity.Y / Controller.JumpVel, -1f, 1f);
		float rising = Mathf.Max(vertical, 0f);
		float falling = Mathf.Max(-vertical, 0f);
		float apex = 1f - Mathf.Abs(vertical);
		float legDrive = Ease01(rising);
		float legTuck = Ease01(apex);
		float legReach = Ease01(falling);
		float poweredLegPitch = 0.10f + legDrive * 0.26f
			- legTuck * 0.08f + legReach * 0.05f;
		float legOffset = _airLead * (0.035f + apex * 0.035f);
		float forwardArmPitch = -1.10f - rising * 0.20f + falling * 0.66f;
		float backArmPitch = 0.28f - apex * 0.42f - falling * 0.50f;
		float airArmSpread = 0.07f + apex * 0.25f + falling * 0.18f;
		float airLegSpread = 0.025f + apex * 0.09f + falling * 0.035f;

		var airLegL = new Vector3(poweredLegPitch + legOffset, 0f, -airLegSpread);
		var airLegR = new Vector3(poweredLegPitch - legOffset, 0f, airLegSpread);
		// Arms retain a small alternating lead so consecutive jumps do not become
		// perfectly mirrored, while the coordinated legs carry the actual force.
		bool leftArmLeads = _airLead < 0f;
		var airArmL = new Vector3(leftArmLeads ? backArmPitch : forwardArmPitch,
			0f, -airArmSpread);
		var airArmR = new Vector3(leftArmLeads ? forwardArmPitch : backArmPitch,
			0f, airArmSpread);

		var landArmL = groundArmL.Lerp(airArmL, air);
		var landArmR = groundArmR.Lerp(airArmR, air);
		var landLegL = groundLegL.Lerp(airLegL, air);
		var landLegR = groundLegR.Lerp(airLegR, air);

		// The arms reach together and widen through a broad pull. Legs use a faster
		// alternating flutter, so forward swimming stays energetic and never turns
		// into either a submerged walk or a symmetrical frog pose.
		float pull = Ease01(0.5f - 0.5f * Mathf.Cos(_swimPhase));
		float pullWidth = Mathf.Sin(pull * Mathf.Pi);
		float movingArmPitch = Mathf.Lerp(-1.48f, -0.34f, pull);
		float movingArmSpread = 0.08f + pullWidth * 0.52f;
		float idleArmPitch = -0.78f + Mathf.Sin(_swimPhase) * 0.13f;
		float idleArmSpread = 0.24f + Mathf.Cos(_swimPhase) * 0.09f;
		float swimArmPitch = Mathf.Lerp(idleArmPitch, movingArmPitch, swimMotion);
		float swimArmSpread = Mathf.Lerp(idleArmSpread, movingArmSpread, swimMotion);
		var swimArmL = new Vector3(swimArmPitch, 0f, -swimArmSpread);
		var swimArmR = new Vector3(swimArmPitch, 0f, swimArmSpread);

		float flutter = Mathf.Sin(_swimPhase * 2.05f);
		float flutterAmplitude = Mathf.Lerp(0.15f, 0.48f, swimMotion);
		float swimLegBase = Mathf.Lerp(-0.22f, -0.12f, swimMotion);
		float swimLegSpread = 0.055f + Mathf.Abs(flutter) * 0.055f;
		var swimLegL = new Vector3(swimLegBase + flutter * flutterAmplitude,
			0f, -swimLegSpread);
		var swimLegR = new Vector3(swimLegBase - flutter * flutterAmplitude,
			0f, swimLegSpread);

		_armL.Rotation = landArmL.Lerp(swimArmL, swim);
		_armR.Rotation = landArmR.Lerp(swimArmR, swim);
		_legL.Rotation = landLegL.Lerp(swimLegL, swim);
		_legR.Rotation = landLegR.Lerp(swimLegR, swim);

		// Breathing idle plus a two-per-stride bob.
		float idle = Mathf.Sin(_phase * 0.35f + 1.3f) * 0.012f;
		_bob = Mathf.Lerp(_bob, Mathf.Abs(Mathf.Sin(_phase)) * cadence * 0.10f + idle,
			1f - Mathf.Exp(-18f * dt));

		float swimBob = Mathf.Sin(_swimPhase) * Mathf.Lerp(0.035f, 0.065f, swimMotion);
		float nonSwimBob = Mathf.Lerp(_bob, 0f, air);
		float visibleBob = Mathf.Lerp(nonSwimBob, swimBob, swim);
		_swimPivot.Position = new Vector3(0f, SwimPivotHeight + visibleBob, 0f);

		// Idle swimmers stay angled enough to read as floating; forward movement
		// brings the figure close to prone. The true torso pivot keeps that rotation
		// centred inside the collision body throughout both transitions.
		float swimPitch = Mathf.Lerp(0.62f, 1.02f, swimMotion)
			+ Mathf.Sin(_swimPhase) * 0.025f;
		float airPitch = (0.055f + rising * 0.09f - falling * 0.04f) * air;
		float airRoll = _airLead * (rising * 0.055f + apex * 0.095f) * air;
		var landBodyRotation = new Vector3(airPitch, 0f, airRoll);
		_swimPivot.Rotation = landBodyRotation.Lerp(new Vector3(swimPitch, 0f, 0f), swim);
		_body.Position = new Vector3(0f, -SwimPivotHeight, 0f);

		// Keep the voxel proportions rigid in the air. Jump height and limb posing
		// provide the motion; scaling the whole body made the player visibly stretch.
		_body.Scale = Vector3.One;

		var landHead = new Vector3(Mathf.Sin(_phase * 0.5f) * 0.04f,
			Mathf.Sin(_phase * 0.31f) * 0.09f, 0);
		// Counter-pitch so the hood and face remain above the surface and continue
		// looking ahead instead of pointing down into the lake.
		var airHead = new Vector3(-airPitch * 0.72f,
			_airLead * (rising + apex) * 0.065f, -airRoll * 0.65f);
		var activeLandHead = landHead.Lerp(airHead, air);
		var swimHead = new Vector3(-swimPitch * 0.58f + Mathf.Sin(_swimPhase) * 0.035f,
			Mathf.Sin(_swimPhase * 0.5f) * 0.055f, 0f);
		_head.Rotation = activeLandHead.Lerp(swimHead, swim);

		// Keep the cape on its ordinary trailing animation in water. Swimming is
		// treated as supported rather than airborne, so it does not receive the
		// extra jump/fall lift merely because the physics body is off the floor.
		float cloakTarget = 0.16f + cadence * 0.55f
			+ (grounded || swimming ? 0f : 0.5f);
		for (int i = 0; i < _cloak.Count; i++)
		{
			float want = cloakTarget
				+ Mathf.Sin(_phase * 0.5f + i) * cadence * 0.06f;
			_cloakLag[i] = Mathf.Lerp(_cloakLag[i], want, 1f - Mathf.Exp(-(9f - i * 2f) * dt));
			_cloak[i].Rotation = new Vector3(_cloakLag[i], 0,
				Mathf.Sin(_phase * 0.7f + i * 1.1f) * 0.05f * (0.4f + cadence));
		}

		// The scarf is lighter cloth: it reacts faster and sweeps farther than
		// the cape, preserving the model's one warm accent while it moves.
		for (int i = 0; i < _scarfTail.Count; i++)
		{
			float landWant = 0.10f + cadence * 0.95f + (grounded ? 0f : 0.75f)
				+ Mathf.Sin(_phase * 0.9f + i * 1.4f) * cadence * 0.16f;
			float waterWant = 1.24f + swimMotion * 0.25f
				+ Mathf.Sin(_swimPhase * 0.92f + i * 1.35f) * 0.16f;
			float want = Mathf.Lerp(landWant, waterWant, swim);
			_scarfLag[i] = Mathf.Lerp(_scarfLag[i], want,
				1f - Mathf.Exp(-(13f - i * 3f) * dt));
			_scarfTail[i].Rotation = new Vector3(_scarfLag[i], 0,
				Mathf.Lerp(
					Mathf.Sin(_phase * 1.5f + i * 1.7f) * 0.13f * (0.5f + cadence)
						- cadence * 0.10f,
					Mathf.Sin(_swimPhase * 1.15f + i * 1.7f) * 0.18f - 0.10f,
					swim));
		}
	}

	private static float Ease01(float value)
	{
		float t = Mathf.Clamp(value, 0f, 1f);
		return t * t * (3f - 2f * t);
	}
}
