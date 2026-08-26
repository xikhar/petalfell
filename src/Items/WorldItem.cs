using Godot;
using Petalfell.Player;

namespace Petalfell.Items;

/// <summary>A collectible physical item currently present in the world.</summary>
public partial class WorldItem : RigidBody3D
{
	private const uint WorldItemLayer = 1u << 2;
	private Node _worldParent;

	public ItemDefinition Item { get; private set; }
	public ItemHand OriginHand { get; private set; }
	public long ThrowSequence { get; private set; }
	public bool IsCarried { get; private set; }
	public bool CanPickUp => !IsCarried && Item != null;

	public void Setup(ItemDefinition item, ItemHand originHand, long sequence,
		ShaderMaterial inkLight, ShaderMaterial inkDark, Controller thrower)
	{
		Item = item;
		OriginHand = originHand;
		ThrowSequence = sequence;
		Mass = item.Throw?.Mass ?? 0.25f;
		CollisionLayer = WorldItemLayer;
		CollisionMask = 1u;
		LinearDamp = 0.18f;
		AngularDamp = 0.32f;
		CanSleep = true;
		_worldParent = GetParent();

		AddChild(ItemVisuals.Build(item, inkLight, inkDark));
		AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(0.30f, 1.34f, 0.30f) },
		});
		if (thrower != null) AddCollisionExceptionWith(thrower);
	}

	public bool BeginCarry(Node3D anchor)
	{
		if (!CanPickUp || anchor == null) return false;
		IsCarried = true;
		Freeze = true;
		Sleeping = true;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = Vector3.Zero;
		CollisionLayer = 0;
		CollisionMask = 0;
		Reparent(anchor, keepGlobalTransform: false);
		Position = new Vector3(0f, -0.04f, 0.31f);
		Rotation = new Vector3(0f, 0f, Mathf.Pi * 0.5f);
		return true;
	}

	public void Drop(Vector3 position)
	{
		if (_worldParent == null || !GodotObject.IsInstanceValid(_worldParent)) return;
		Reparent(_worldParent, keepGlobalTransform: false);
		GlobalPosition = position + Vector3.Up * 0.75f;
		Rotation = new Vector3(0.18f, 0.3f, Mathf.Pi * 0.5f);
		CollisionLayer = WorldItemLayer;
		CollisionMask = 1u;
		IsCarried = false;
		Freeze = false;
		Sleeping = false;
		LinearVelocity = Vector3.Zero;
		AngularVelocity = new Vector3(0f, 1.2f, 0f);
	}
}
