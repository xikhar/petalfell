using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;
using Petalfell.Items;
using Petalfell.Player;
using Petalfell.World;

namespace Petalfell.Gameplay;

/// <summary>
/// Shore fishing as a compact, explicit state machine.
///
/// The visible lake fish remain ambient fauna. A bite rolls an inventory item
/// independently, so streaming or frightening a decorative fish never changes
/// the player's fishing odds.
/// </summary>
public partial class FishingSystem : Node3D, IInteractionProvider
{
	private enum Phase { Idle, Charging, Casting, Waiting, Bite, Reeling, Catching }

	private GlobalInventory _inventory;
	private Controller _player;
	private Character _character;
	private Terrain _terrain;
	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private Rng _rng;

	private Phase _phase;
	private ItemHand _hand;
	private FishingProfile _profile;
	private float _clock;
	private float _biteAt;
	private float _castPoseStart;
	private Vector3 _castStart;
	private Vector3 _castTarget;
	private Vector3 _reelStart;
	private Vector3 _lurePosition;
	private ItemDefinition _caughtFish;
	private bool _catchShown;

	private ImmediateMesh _lineMesh;
	private MeshInstance3D _line;
	private StandardMaterial3D _lineMaterial;
	private Node3D _bobber;
	private Node3D _hookedFish;

	public bool IsActive => _phase != Phase.Idle;
	public event Action<ItemDefinition> FishCaught;
	public event Action<string> NoticeRequested;

	public void Setup(GlobalInventory inventory, Controller player, Character character,
		Terrain terrain, ShaderMaterial inkLight, ShaderMaterial inkDark, int seed)
	{
		_inventory = inventory;
		_player = player;
		_character = character;
		_terrain = terrain;
		_inkLight = inkLight;
		_inkDark = inkDark;
		_rng = new Rng(seed ^ unchecked((int)0xf1571a6b));
		BuildVisuals();
	}

	private void BuildVisuals()
	{
		_lineMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.88f, 0.90f, 0.94f, 0.76f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			DisableReceiveShadows = true,
		};
		_lineMesh = new ImmediateMesh();
		_line = new MeshInstance3D
		{
			Name = "FishingLine",
			Mesh = _lineMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		AddChild(_line);

		_bobber = ItemVisuals.BuildFishingBobber(_inkLight, _inkDark);
		_bobber.Visible = false;
		AddChild(_bobber);
	}

	/// <returns>True when fishing owns this event.</returns>
	public bool HandleInput(InputEvent inputEvent)
	{
		if (inputEvent is not InputEventKey key || key.Echo) return false;

		if (IsActive)
		{
			bool leftAction = inputEvent.IsActionPressed("throw_left") ||
				inputEvent.IsActionReleased("throw_left");
			bool rightAction = inputEvent.IsActionPressed("throw_right") ||
				inputEvent.IsActionReleased("throw_right");
			if (leftAction || rightAction)
			{
				bool activeHand = (_hand == ItemHand.Left && leftAction) ||
					(_hand == ItemHand.Right && rightAction);
				if (activeHand)
				{
					if (_phase == Phase.Charging && !key.Pressed) ReleaseCast();
					else if (_phase != Phase.Charging && key.Pressed) BeginReel();
				}
				return true;
			}

			if (inputEvent.IsActionPressed("interact"))
			{
				if (key.Pressed && _phase == Phase.Bite) Strike();
				return true;
			}
			return false;
		}

		if (!key.Pressed) return false;
		if (inputEvent.IsActionPressed("throw_left") && HasRod(ItemHand.Left))
		{
			TryBeginCharge(ItemHand.Left);
			return true;
		}
		if (inputEvent.IsActionPressed("throw_right") && HasRod(ItemHand.Right))
		{
			TryBeginCharge(ItemHand.Right);
			return true;
		}
		return false;
	}

	public void GatherInteractions(Vector3 playerPosition, List<ContextAction> actions)
	{
		if (IsActive)
		{
			string action = _hand == ItemHand.Left ? "throw_left" : "throw_right";
			string key = _hand == ItemHand.Left ? "F" : "G";
			string label = _phase == Phase.Charging ? "Release to cast" : "Reel in";
			actions.Add(new ContextAction(action, key, label, 92, 0f,
				() =>
				{
					if (_phase == Phase.Charging) ReleaseCast();
					else BeginReel();
					return InteractionResult.Done();
				}));
			if (_phase == Phase.Bite)
				actions.Add(new ContextAction("interact", "R", "Pull", 120, 0f,
					() => { Strike(); return InteractionResult.Done(); }));
			return;
		}

		for (int i = 0; i < 2; i++)
		{
			var hand = i == 0 ? ItemHand.Left : ItemHand.Right;
			if (!HasRod(hand) || !TryFindWater(_inventory.HeldItem(hand).Fishing, out _)) continue;
			string action = hand == ItemHand.Left ? "throw_left" : "throw_right";
			string key = hand == ItemHand.Left ? "F" : "G";
			actions.Add(new ContextAction(action, key, "Fish", 48, 0f,
				() => TryBeginCharge(hand)
					? InteractionResult.Done()
					: InteractionResult.Failed("Face nearby open water")));
		}
	}

	private bool HasRod(ItemHand hand) => _inventory?.HeldItem(hand)?.Fishing != null;

	private bool TryBeginCharge(ItemHand hand)
	{
		var item = _inventory?.HeldItem(hand);
		var profile = item?.Fishing;
		if (profile == null) return false;
		if (_player == null || _player.Swimming || _player.Sitting || !_player.IsOnFloor())
		{
			NoticeRequested?.Invoke("Stand on the shore to fish");
			return false;
		}
		if (!TryFindWater(profile, out Vector3 target))
		{
			NoticeRequested?.Invoke("Face nearby open water");
			return false;
		}

		_hand = hand;
		_profile = profile;
		_phase = Phase.Charging;
		_clock = 0f;
		_castPoseStart = 0f;
		_castTarget = target;
		_castStart = _character.FishingRodTipWorldPosition(hand);
		_lurePosition = _castStart;
		_caughtFish = null;
		_catchShown = false;

		var face = new Vector3(target.X - _player.GlobalPosition.X, 0f,
			target.Z - _player.GlobalPosition.Z);
		if (face.LengthSquared() > 0.001f) _player.Facing = face.Normalized();
		_player.StopTravel();
		_player.InputEnabled = false;
		_player.Velocity = new Vector3(0f, Mathf.Min(_player.Velocity.Y, 0f), 0f);
		_character.SetFishingPose(_hand, true, 0f, 0f, 0f);
		return true;
	}

	private void ReleaseCast()
	{
		if (_phase != Phase.Charging || _profile == null) return;
		float charge = Mathf.Clamp(_clock / _profile.ChargeSeconds, 0f, 1f);
		_castPoseStart = 0.36f * Ease(charge);
		float requestedDistance = Mathf.Lerp(_profile.MinimumCastDistance,
			_profile.MaximumCastDistance, Ease(charge));
		if (!TryFindWater(_profile, out Vector3 target, requestedDistance))
		{
			NoticeRequested?.Invoke("Face nearby open water");
			EndFishing();
			return;
		}

		_phase = Phase.Casting;
		_clock = 0f;
		_castTarget = target;
		_castStart = _character.FishingRodTipWorldPosition(_hand);
		_lurePosition = _castStart;
	}

	private bool TryFindWater(FishingProfile profile, out Vector3 target,
		float requestedDistance = float.PositiveInfinity)
	{
		target = Vector3.Zero;
		if (_terrain == null || _player == null || profile == null) return false;

		int px = Mathf.FloorToInt(_player.GlobalPosition.X);
		int pz = Mathf.FloorToInt(_player.GlobalPosition.Z);
		if (!Inside(px, pz) || _terrain.Land[pz * _terrain.Size + px] == 0) return false;

		var forward = new Vector3(_player.Facing.X, 0f, _player.Facing.Z);
		if (forward.LengthSquared() < 0.01f) return false;
		forward = forward.Normalized();
		var side = new Vector3(-forward.Z, 0f, forward.X);
		float[] lanes = { 0f, -0.55f, 0.55f };
		float desired = float.IsPositiveInfinity(requestedDistance)
			? profile.MaximumCastDistance
			: Mathf.Clamp(requestedDistance, profile.MinimumCastDistance,
				profile.MaximumCastDistance);

		foreach (float lane in lanes)
		{
			Vector3 best = Vector3.Zero;
			bool found = false;
			float bestError = float.PositiveInfinity;
			for (float d = profile.MinimumCastDistance; d <= profile.MaximumCastDistance; d += 0.4f)
			{
				var sample = _player.GlobalPosition + forward * d + side * lane;
				int x = Mathf.FloorToInt(sample.X);
				int z = Mathf.FloorToInt(sample.Z);
				if (!FishableWater(x, z))
				{
					if (found) break;
					continue;
				}
				found = true;
				float error = Mathf.Abs(d - desired);
				if (error < bestError)
				{
					bestError = error;
					best = new Vector3(x + 0.5f, Palette.WaterLevel + 0.04f, z + 0.5f);
				}
			}
			if (!found) continue;
			target = best;
			return true;
		}
		return false;
	}

	private bool FishableWater(int x, int z)
	{
		if (!Inside(x, z)) return false;
		int i = z * _terrain.Size + x;
		return _terrain.Land[i] == 0 && Palette.WaterLevel - _terrain.Level[i] >= 0.70f;
	}

	private bool Inside(int x, int z) => x >= 1 && z >= 1 &&
		x < _terrain.Size - 1 && z < _terrain.Size - 1;

	public void Advance(double delta)
	{
		if (!IsActive) return;
		float dt = Mathf.Min((float)delta, 0.05f);

		if (!HasRod(_hand) || _player.Swimming || _player.Sitting)
		{
			EndFishing();
			return;
		}

		_player.InputEnabled = false;
		_player.Route = null;
		_player.Velocity = new Vector3(0f, Mathf.Min(_player.Velocity.Y, 0f), 0f);
		_clock += dt;

		switch (_phase)
		{
			case Phase.Charging:
				AdvanceCharge();
				break;
			case Phase.Casting:
				AdvanceCast();
				break;
			case Phase.Waiting:
				AdvanceWaiting();
				break;
			case Phase.Bite:
				AdvanceBite();
				break;
			case Phase.Reeling:
				AdvanceReel();
				break;
			case Phase.Catching:
				AdvanceCatch();
				break;
		}

		UpdateVisuals();
	}

	private void AdvanceCharge()
	{
		float charge = Mathf.Clamp(_clock / _profile.ChargeSeconds, 0f, 1f);
		// The first part of the existing cast curve is the backswing. Holding the
		// action settles into its strongest point; releasing continues forward from
		// there rather than replaying the preparation.
		_character.SetFishingPose(_hand, true, 0.36f * Ease(charge), 0f, 0f);
		_lurePosition = _character.FishingRodTipWorldPosition(_hand);
	}

	private void AdvanceCast()
	{
		float t = Mathf.Clamp(_clock / _profile.CastSeconds, 0f, 1f);
		float eased = Ease(t);
		_lurePosition = _castStart.Lerp(_castTarget, eased)
			+ Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * 2.35f);
		_character.SetFishingPose(_hand, true,
			Mathf.Lerp(_castPoseStart, 1f, eased), 0f, 0f);
		if (t < 1f) return;

		_phase = Phase.Waiting;
		_clock = 0f;
		_biteAt = _rng.Range(_profile.MinimumBiteWait, _profile.MaximumBiteWait);
	}

	private void AdvanceWaiting()
	{
		_lurePosition = _castTarget + Vector3.Up *
			(0.035f + Mathf.Sin(_clock * 2.1f) * 0.025f);
		_character.SetFishingPose(_hand, true, 1f, 0f, 0f);
		if (_clock < _biteAt) return;
		_phase = Phase.Bite;
		_clock = 0f;
	}

	private void AdvanceBite()
	{
		float t = Mathf.Clamp(_clock / _profile.BiteWindow, 0f, 1f);
		float pulse = Mathf.Abs(Mathf.Sin(t * Mathf.Pi * 3.0f));
		float envelope = 1f - t * 0.35f;
		float tug = pulse * envelope;
		_lurePosition = _castTarget + new Vector3(
			Mathf.Sin(t * 19f) * 0.07f, -0.16f * tug,
			Mathf.Cos(t * 17f) * 0.07f);
		_character.SetFishingPose(_hand, true, 1f, tug, 0f);
		if (t < 1f) return;

		_phase = Phase.Waiting;
		_clock = 0f;
		_biteAt = _rng.Range(_profile.MinimumBiteWait, _profile.MaximumBiteWait);
	}

	private void Strike()
	{
		_caughtFish = RollFish();
		if (!_inventory.TryAdd(_caughtFish.Id, 1))
		{
			NoticeRequested?.Invoke("Inventory full");
			_caughtFish = null;
			BeginReel();
			return;
		}

		_phase = Phase.Catching;
		_clock = 0f;
		_reelStart = _lurePosition;
		_catchShown = false;
		RemoveHookedFish();
		_hookedFish = ItemVisuals.Build(_caughtFish, _inkLight, _inkDark);
		_hookedFish.Scale = Vector3.One * 0.82f;
		AddChild(_hookedFish);
	}

	private ItemDefinition RollFish()
	{
		float roll = _rng.Next();
		if (roll < 0.56f) return ItemCatalog.SilverMinnow;
		if (roll < 0.90f) return ItemCatalog.Rosefin;
		return ItemCatalog.MoonCarp;
	}

	private void AdvanceCatch()
	{
		float t = Mathf.Clamp(_clock / _profile.CatchSeconds, 0f, 1f);
		float eased = Ease(t);
		Vector3 tip = _character.FishingRodTipWorldPosition(_hand);
		_lurePosition = _reelStart.Lerp(tip + Vector3.Up * 0.15f, eased)
			+ Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * 1.75f);
		_character.SetFishingPose(_hand, true, 1f, 0f, Mathf.Sin(t * Mathf.Pi));
		if (!_catchShown && t >= 0.68f)
		{
			_catchShown = true;
			FishCaught?.Invoke(_caughtFish);
		}
		if (t >= 1f) EndFishing();
	}

	private void BeginReel()
	{
		if (!IsActive || _phase == Phase.Reeling) return;
		if (_phase == Phase.Catching) return;
		_phase = Phase.Reeling;
		_clock = 0f;
		_reelStart = _lurePosition;
	}

	private void AdvanceReel()
	{
		float t = Mathf.Clamp(_clock / _profile.ReelSeconds, 0f, 1f);
		Vector3 tip = _character.FishingRodTipWorldPosition(_hand);
		_lurePosition = _reelStart.Lerp(tip, Ease(t))
			+ Vector3.Up * (Mathf.Sin(t * Mathf.Pi) * 0.36f);
		// Lift both grips a little at mid-reel, then settle back to the forward
		// fishing pose before the normal one-handed carry animation takes over.
		_character.SetFishingPose(_hand, true, 1f, 0f,
			Mathf.Sin(t * Mathf.Pi) * 0.42f);
		if (t >= 1f) EndFishing();
	}

	public void Cancel(bool immediate = false)
	{
		if (!IsActive) return;
		if (immediate) EndFishing();
		else BeginReel();
	}

	private void EndFishing()
	{
		_phase = Phase.Idle;
		_clock = 0f;
		_profile = null;
		_character?.SetFishingPose(_hand, false, 0f, 0f, 0f);
		if (_line != null) _line.Visible = false;
		if (_bobber != null) _bobber.Visible = false;
		RemoveHookedFish();
		_caughtFish = null;
	}

	private void RemoveHookedFish()
	{
		if (_hookedFish != null && GodotObject.IsInstanceValid(_hookedFish))
			_hookedFish.QueueFree();
		_hookedFish = null;
	}

	private void UpdateVisuals()
	{
		if (!IsActive) return;
		if (_phase == Phase.Charging)
		{
			_line.Visible = false;
			_bobber.Visible = false;
			return;
		}
		Vector3 tip = _character.FishingRodTipWorldPosition(_hand);
		bool catching = _phase == Phase.Catching;
		_line.Visible = true;
		_bobber.Visible = !catching;
		_bobber.GlobalPosition = _lurePosition;
		_bobber.Rotation = new Vector3(0f, _clock * 0.7f,
			_phase == Phase.Bite ? Mathf.Sin(_clock * 19f) * 0.22f : 0f);

		if (_hookedFish != null && GodotObject.IsInstanceValid(_hookedFish))
		{
			_hookedFish.GlobalPosition = _lurePosition;
			_hookedFish.Rotation = new Vector3(0f, _clock * 5.2f,
				Mathf.Sin(_clock * 18f) * 0.35f);
		}

		float sag = _phase is Phase.Waiting or Phase.Bite ? 0.38f : 0.10f;
		Vector3 middle = (tip + _lurePosition) * 0.5f + Vector3.Down * sag;
		_lineMesh.ClearSurfaces();
		_lineMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial);
		_lineMesh.SurfaceAddVertex(tip);
		_lineMesh.SurfaceAddVertex(middle);
		_lineMesh.SurfaceAddVertex(middle);
		_lineMesh.SurfaceAddVertex(_lurePosition);
		_lineMesh.SurfaceEnd();
	}

	private static float Ease(float value)
	{
		float t = Mathf.Clamp(value, 0f, 1f);
		return t * t * (3f - 2f * t);
	}
}
