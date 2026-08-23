using System;
using System.Collections.Generic;
using Godot;

namespace Petalfell.Items;

/// <summary>Which hand currently owns an equipped one-handed item.</summary>
public enum ItemHand
{
	Left,
	Right,
}

/// <summary>
/// Optional throw behaviour. Its presence is the capability: ordinary items
/// do not become throwable merely because they can be equipped.
/// </summary>
public sealed class ThrowProfile
{
	public float ChargeSeconds { get; init; } = 1.25f;
	public float MinimumSpeed { get; init; } = 9f;
	public float MaximumSpeed { get; init; } = 28f;
	public float MinimumLift { get; init; } = 4.5f;
	public float MaximumLift { get; init; } = 8.5f;
	public float Mass { get; init; } = 0.24f;
}

/// <summary>
/// Optional local light carried by an item. Keeping illumination as item data
/// lets future lanterns, glowing artifacts and two-handed lights share the same
/// equipment path without teaching the character about individual item IDs.
/// </summary>
public sealed class HeldLightProfile
{
	public Color Color { get; init; } = new Color(1.0f, 0.56f, 0.28f);
	public float Energy { get; init; } = 4.8f;
	public float Range { get; init; } = 13.5f;
	public bool CastShadows { get; init; } = true;
	/// <summary>Physical source radius used by Godot's positional-light PCSS.</summary>
	public float SourceRadius { get; init; } = 0.22f;
	public float ShadowBlur { get; init; } = 1.25f;
	public float ShadowOpacity { get; init; } = 0.22f;
}

/// <summary>
/// Immutable, globally shared item data. Runtime inventory state stores only
/// the stable ID and quantity, keeping future saves independent of scene nodes.
/// </summary>
public sealed class ItemDefinition
{
	public string Id { get; }
	public string Name { get; }
	public int MaxStack { get; }
	public bool Equipable { get; }
	public bool RequiresBothHands { get; }
	public ThrowProfile Throw { get; }
	public HeldLightProfile Light { get; }
	public Color Color { get; }
	public bool LightOutline { get; }

	public ItemDefinition(string id, string name, uint color, int maxStack = 1,
		bool equipable = false, bool requiresBothHands = false,
		ThrowProfile throwProfile = null, HeldLightProfile lightProfile = null,
		bool? lightOutline = null)
	{
		Id = id;
		Name = name;
		var srgb = new Color(((color >> 16) & 255) / 255f,
			((color >> 8) & 255) / 255f, (color & 255) / 255f);
		Color = srgb.SrgbToLinear();
		LightOutline = lightOutline ??
			(srgb.R * 0.2126f + srgb.G * 0.7152f + srgb.B * 0.0722f >= Core.Palette.LightFaceLuma);
		MaxStack = Math.Max(1, maxStack);
		Equipable = equipable;
		RequiresBothHands = requiresBothHands;
		Throw = throwProfile;
		Light = lightProfile;
	}
}

/// <summary>One authoritative catalog for definitions used by saves and content.</summary>
public static class ItemCatalog
{
	public static readonly ItemDefinition Stick = new(
		"stick",
		"Stick",
		0x8f5c40,
		maxStack: 12,
		equipable: true,
		throwProfile: new ThrowProfile());

	public static readonly ItemDefinition Wood = new(
		"wood",
		"Wood",
		0xa86f48,
		maxStack: 99,
		lightOutline: false);

	public static readonly ItemDefinition Torch = new(
		"torch",
		"Torch",
		0x9a6542,
		maxStack: 4,
		equipable: true,
		// A handheld flame is a broad, unstable local source. A point-light shadow
		// map reads as a hard duplicate silhouette at this distance and its cubemap
		// seams can show up as rectangular patches on the ground, so the torch only
		// contributes diffuse illumination. World/sun shadows remain unaffected.
		lightProfile: new HeldLightProfile { CastShadows = false });

	private static readonly Dictionary<string, ItemDefinition> Definitions =
		new(StringComparer.Ordinal)
		{
			[Stick.Id] = Stick,
			[Wood.Id] = Wood,
			[Torch.Id] = Torch,
		};

	public static ItemDefinition Get(string id) =>
		id != null && Definitions.TryGetValue(id, out var item) ? item : null;
}
