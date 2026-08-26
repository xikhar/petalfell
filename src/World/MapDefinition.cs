using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Petalfell.Core;

namespace Petalfell.World;

/// <summary>
/// Content-owned macro plan for one map. Coordinates and radii are normalized
/// to the map's 0..1 footprint so the same definition can be previewed at a
/// smaller development size without changing its composition.
/// </summary>
public sealed class MapDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public int DefaultSeed { get; set; }
	public int DefaultWorldSize { get; set; } = 768;
	public MapBoundary Boundary { get; set; } = new();
	public NaturalInfillRules NaturalInfill { get; set; } = new();
	public List<ElevationZone> ElevationZones { get; set; } = new();
	public List<BiomeZone> BiomeZones { get; set; } = new();
	public List<LakeMarker> Lakes { get; set; } = new();
	public List<WaterwayMarker> Waterways { get; set; } = new();
	public List<SpawnMarker> Spawns { get; set; } = new();
	public List<SettlementMarker> Settlements { get; set; } = new();
	public List<RoadMarker> Roads { get; set; } = new();
	public List<LandmarkMarker> Landmarks { get; set; } = new();

	public static MapDefinition Load(string resourcePath)
	{
		using var file = Godot.FileAccess.Open(resourcePath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException($"Could not open map definition '{resourcePath}': {Godot.FileAccess.GetOpenError()}");

		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
		};
		options.Converters.Add(new JsonStringEnumConverter());
		var map = JsonSerializer.Deserialize<MapDefinition>(file.GetAsText(), options)
		          ?? throw new InvalidOperationException($"Map definition '{resourcePath}' was empty.");
		map.Validate(resourcePath);
		return map;
	}

	public float BoundaryDistance(float nx, float nz)
	{
		float dx = (nx - Boundary.Centre.X) / Boundary.RadiusX;
		float dz = (nz - Boundary.Centre.Z) / Boundary.RadiusZ;
		return MathF.Sqrt(dx * dx + dz * dz);
	}

	/// <summary>Influence of an elliptical authored zone, 1 in its core and 0 outside.</summary>
	public static float Influence(MapPoint centre, float radiusX, float radiusZ,
		float inner, float nx, float nz)
	{
		float dx = (nx - centre.X) / MathF.Max(radiusX, 0.0001f);
		float dz = (nz - centre.Z) / MathF.Max(radiusZ, 0.0001f);
		float d = MathF.Sqrt(dx * dx + dz * dz);
		return 1f - Rng.Smoothstep(Rng.Clamp(inner, 0f, 0.99f), 1f, d);
	}

	/// <summary>
	/// Authored built-space reservation used by procedural boulders and flora.
	/// Ground still exists here; later road/settlement stages own its dressing.
	/// </summary>
	public bool ReservesNaturalDetail(float nx, float nz, float padding = 0f)
	{
		foreach (var spawn in Spawns)
			if (Distance(nx, nz, spawn.Centre) <= 0.010f + padding) return true;
		foreach (var settlement in Settlements)
			if (Distance(nx, nz, settlement.Centre) <= settlement.Radius + padding) return true;
		foreach (var landmark in Landmarks)
			if (Distance(nx, nz, landmark.Centre) <= landmark.Radius + padding) return true;
		foreach (var road in Roads)
		for (int i = 0; i + 1 < road.Points.Count; i++)
			if (DistanceToSegment(nx, nz, road.Points[i], road.Points[i + 1]) <= road.Clearance + padding)
				return true;
		return false;
	}

	private static float Distance(float x, float z, MapPoint p)
	{
		float dx = x - p.X, dz = z - p.Z;
		return MathF.Sqrt(dx * dx + dz * dz);
	}

	private static float DistanceToSegment(float x, float z, MapPoint a, MapPoint b)
	{
		float vx = b.X - a.X, vz = b.Z - a.Z;
		float wx = x - a.X, wz = z - a.Z;
		float vv = vx * vx + vz * vz;
		float t = vv <= 0.0000001f ? 0f : Rng.Clamp((wx * vx + wz * vz) / vv, 0f, 1f);
		float dx = x - (a.X + vx * t), dz = z - (a.Z + vz * t);
		return MathF.Sqrt(dx * dx + dz * dz);
	}

	private void Validate(string path)
	{
		if (string.IsNullOrWhiteSpace(Id)) throw Invalid(path, "id is required");
		if (DefaultWorldSize < 192) throw Invalid(path, "defaultWorldSize must be at least 192");
		if (!Boundary.Centre.Normalized) throw Invalid(path, "boundary centre lies outside normalized map coordinates");
		if (Boundary.RadiusX <= 0f || Boundary.RadiusZ <= 0f)
			throw Invalid(path, "boundary radii must be positive");

		var ids = new HashSet<string>(StringComparer.Ordinal);
		void Feature(string id, MapPoint p, string kind)
		{
			if (string.IsNullOrWhiteSpace(id)) throw Invalid(path, $"{kind} id is required");
			if (!ids.Add(id)) throw Invalid(path, $"duplicate feature id '{id}'");
			if (!p.Normalized) throw Invalid(path, $"{kind} '{id}' lies outside normalized map coordinates");
		}

		foreach (var z in ElevationZones)
		{
			Feature(z.Id, z.Centre, "elevation zone");
			if (z.RadiusX <= 0f || z.RadiusZ <= 0f) throw Invalid(path, $"elevation zone '{z.Id}' has an invalid radius");
		}
		foreach (var z in BiomeZones)
		{
			Feature(z.Id, z.Centre, "biome zone");
			if (z.RadiusX <= 0f || z.RadiusZ <= 0f) throw Invalid(path, $"biome zone '{z.Id}' has an invalid radius");
		}
		foreach (var lake in Lakes)
		{
			Feature(lake.Id, lake.Centre, "lake");
			if (lake.Radius <= 0f) throw Invalid(path, $"lake '{lake.Id}' has an invalid radius");
		}
		foreach (var waterway in Waterways)
		{
			Feature(waterway.Id, waterway.Points.Count > 0 ? waterway.Points[0] : new MapPoint(), "waterway");
			if (waterway.Points.Count < 2) throw Invalid(path, $"waterway '{waterway.Id}' needs at least two points");
			foreach (var p in waterway.Points) if (!p.Normalized) throw Invalid(path, $"waterway '{waterway.Id}' has an invalid point");
		}
		foreach (var spawn in Spawns) Feature(spawn.Id, spawn.Centre, "spawn");
		foreach (var settlement in Settlements)
		{
			Feature(settlement.Id, settlement.Centre, "settlement");
			if (settlement.Radius <= 0f) throw Invalid(path, $"settlement '{settlement.Id}' has an invalid radius");
		}
		foreach (var road in Roads)
		{
			Feature(road.Id, road.Points.Count > 0 ? road.Points[0] : new MapPoint(), "road");
			if (road.Points.Count < 2) throw Invalid(path, $"road '{road.Id}' needs at least two points");
			if (road.Clearance < 0f) throw Invalid(path, $"road '{road.Id}' has an invalid clearance");
			foreach (var p in road.Points) if (!p.Normalized) throw Invalid(path, $"road '{road.Id}' has an invalid point");
		}
		foreach (var landmark in Landmarks)
		{
			Feature(landmark.Id, landmark.Centre, "landmark");
			if (landmark.Radius <= 0f) throw Invalid(path, $"landmark '{landmark.Id}' has an invalid radius");
		}
	}

	private static InvalidOperationException Invalid(string path, string detail) =>
		new($"Invalid map definition '{path}': {detail}.");
}

public sealed class MapBoundary
{
	public MapPoint Centre { get; set; } = new() { X = 0.5f, Z = 0.5f };
	public float RadiusX { get; set; } = 0.45f;
	public float RadiusZ { get; set; } = 0.43f;
	public float Noise { get; set; } = 0.026f;
}

/// <summary>
/// Counts for optional macro features that may be generated in addition to the
/// fixed map plan. Chapter maps normally author their major water explicitly;
/// zero is a meaningful value, not a request for an automatic default.
/// </summary>
public sealed class NaturalInfillRules
{
	public int AdditionalMajorLakes { get; set; }
	public int AdditionalRiverTrunks { get; set; }
	public int AdditionalTributaries { get; set; }
}

public sealed class MapPoint
{
	public float X { get; set; }
	public float Z { get; set; }
	[JsonIgnore] public bool Normalized => X >= 0f && X <= 1f && Z >= 0f && Z <= 1f;
}

public sealed class ElevationZone
{
	public string Id { get; set; } = "";
	public MapPoint Centre { get; set; } = new();
	public float RadiusX { get; set; }
	public float RadiusZ { get; set; }
	public float Inner { get; set; } = 0.35f;
	public float Target { get; set; } = 0.5f;
	public float Strength { get; set; } = 0.75f;
}

public sealed class BiomeZone
{
	public string Id { get; set; } = "";
	public Biome Biome { get; set; }
	public MapPoint Centre { get; set; } = new();
	public float RadiusX { get; set; }
	public float RadiusZ { get; set; }
	public float Inner { get; set; } = 0.35f;
	public float Strength { get; set; } = 1f;
}

public sealed class LakeMarker
{
	public string Id { get; set; } = "";
	public MapPoint Centre { get; set; } = new();
	public float Radius { get; set; }
}

public sealed class WaterwayMarker
{
	public string Id { get; set; } = "";
	public int Order { get; set; }
	public bool Outflow { get; set; }
	public bool Tributary { get; set; }
	public List<MapPoint> Points { get; set; } = new();
}

public sealed class SpawnMarker
{
	public string Id { get; set; } = "";
	public MapPoint Centre { get; set; } = new();
}

public enum SettlementScale { Village, Town, City }

public sealed class SettlementMarker
{
	public string Id { get; set; } = "";
	public SettlementScale Scale { get; set; }
	public MapPoint Centre { get; set; } = new();
	public float Radius { get; set; }
}

public enum RoadKind { Major, Local, Trail, Abandoned, Street }

public sealed class RoadMarker
{
	public string Id { get; set; } = "";
	public RoadKind Kind { get; set; }
	public float Clearance { get; set; } = 0.006f;
	public List<MapPoint> Points { get; set; } = new();
}

public enum LandmarkKind { Overlook, Ruin, Shrine, Crossing, AbandonedBuilding, NaturalFormation }

public sealed class LandmarkMarker
{
	public string Id { get; set; } = "";
	public LandmarkKind Kind { get; set; }
	public MapPoint Centre { get; set; } = new();
	public float Radius { get; set; }
}
