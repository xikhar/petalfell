using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;
using Petalfell.Player;
using Petalfell.Render;
using Petalfell.World;

namespace Petalfell;

/// <summary>
/// Assembly order for the whole game. Owned deliberately in one place: a
/// subsystem that builds itself from private assumptions silently overrides one
/// that reads the plan, and does so without any error to notice.
/// </summary>
public partial class Main : Node3D
{
	[Export] public int Seed = 20260820;
	[Export] public int WorldSize = 768;
	[Export] public int StreamRadius = 8;
	[Export(PropertyHint.File, "*.json")] public string MapDefinitionPath = "res://content/chapter_01/map.json";

	public Terrain Terrain { get; private set; }
	public MapDefinition Map { get; private set; }
	public Planner Plan { get; private set; }
	public BuiltProps Props { get; private set; }
	public Controller Player { get; private set; }
	public CameraRig Rig { get; private set; }

	private ChunkStreamer _streamer;
	private Character _character;
	private Dog _dog;
	private Navigation _nav;
	private ClickPulse _pulse;
	private ShaderMaterial _inkLight, _inkDark;
	private Tools.DeveloperMenu _developerMenu;

	public override void _Ready()
	{
		SetupInput();
		Map = MapDefinition.Load(MapDefinitionPath);
		if (Seed == 0) Seed = Map.DefaultSeed;
		if (WorldSize <= 0) WorldSize = Map.DefaultWorldSize;

		var t0 = Time.GetTicksMsec();
		Plan = new Planner(Seed, WorldSize, Map);
		var t1 = Time.GetTicksMsec();
		Terrain = new Terrain(Seed, WorldSize, Plan);
		var t2 = Time.GetTicksMsec();
		Props = BuiltProps.Build(Terrain, Seed);
		var tProps = Time.GetTicksMsec();
		Vegetation.Populate(Terrain, Seed);
		var t3 = Time.GetTicksMsec();
		GD.Print($"[petalfell] map {Map.Id}  plan {t1 - t0}ms  terrain {t2 - t1}ms  props {tProps - t2}ms  flora {t3 - tProps}ms  " +
		         $"regions {Plan.Regions.Count}  rivers {Plan.Rivers.Count}  bridges {Props.Bridges.Count}");
		ReportTerrain();

		GroundDetail.Seed = Seed;

		BuildMaterials();

		AddChild(Atmosphere.Build());
		AddChild(Atmosphere.Sun());
		AddChild(Atmosphere.Fill());

		_streamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
		AddChild(_streamer);
		_streamer.Setup(Terrain, MakeVoxelMaterial(), _inkLight, _inkDark, MakeDetailMaterial());

		AddChild(BuildWater());

		MapPoint start = Map.Spawns.Count > 0 ? Map.Spawns[0].Centre : null;
		var (sx, sz) = Terrain.FindSpawn(start);
		// Level is the surface plane (first empty voxel), so 0.2 places the
		// capsule just above its authored starting ground.
		Vector3 spawn = new(sx + 0.5f, Terrain.Level[sz * WorldSize + sx] + 0.2f, sz + 0.5f);

		// Ground under the player has to exist before the player does, or the
		// first frame drops them through the world.
		_streamer.UpdateAround(spawn, prime: true);

		Player = new Controller { Name = "Player", Position = spawn };
		AddChild(Player);
		Player.Setup(Terrain);

		_character = new Character { Name = "Traveller" };
		Player.AddChild(_character);
		_character.Setup(_inkLight, _inkDark);

		_nav = new Navigation(Terrain);

		_dog = new Dog { Name = "Dog", Position = spawn + new Vector3(2.2f, 0, 1.4f) };
		AddChild(_dog);
		_dog.Setup(_nav, Player, _inkLight, _inkDark, Seed);

		_pulse = new ClickPulse { Name = "ClickPulse" };
		AddChild(_pulse);

		Rig = new CameraRig { Name = "Camera", Current = true };
		AddChild(Rig);
		Rig.Follow(spawn, Vector3.Zero, 1.0);

		// Kept separate from game-facing UI. This is a disposable live-tuning
		// surface, toggled with tilde, and owns no gameplay or menu state.
		_developerMenu = new Tools.DeveloperMenu { Name = "DeveloperSettings" };
		_developerMenu.Setup(_inkLight, _inkDark, Rig);
		AddChild(_developerMenu);

		AddChild(BuildPetals(spawn));
		AddChild(BuildGrade());

		GD.Print($"[petalfell] spawn {spawn}  chunks {_streamer.LoadedCount}");

		var (shotDir, only) = Tools.Capture.ParseArgs();
		if (shotDir != null) _ = RunCapture(shotDir, only, spawn);
	}

	/// <summary>
	/// Terrain diagnostics. A generator can only be art-directed if you can see
	/// what it actually produced rather than what you assume it produced —
	/// "the world looks flat" needs to become a number before it can be fixed.
	/// </summary>
	private void ReportTerrain()
	{
		int n = WorldSize * WorldSize;
		int min = int.MaxValue, max = int.MinValue, water = 0;
		var hist = new Dictionary<int, int>();
		for (int i = 0; i < n; i++)
		{
			int h = Terrain.Level[i];
			min = Math.Min(min, h);
			max = Math.Max(max, h);
			if (h <= Terrain.Sea) water++;
			int terrace = (h - Terrain.Base) / Terrain.Step;
			hist.TryGetValue(terrace, out int c);
			hist[terrace] = c + 1;
		}
		var keys = new List<int>(hist.Keys);
		keys.Sort();
		var sb = new System.Text.StringBuilder();
		foreach (int k in keys)
		{
			float pct = hist[k] * 100f / n;
			if (pct >= 0.4f) sb.Append($"{k}:{pct:F1}%  ");
		}
		GD.Print($"[terrain] height {min}..{max}  water {water * 100f / n:F1}%  terraces {sb}");

		int detailEdges = 0, terraceEdges = 0, tallEdges = 0;
		for (int z = 0; z < WorldSize; z++)
		for (int x = 0; x < WorldSize; x++)
		{
			int i = z * WorldSize + x;
			if (Terrain.Land[i] == 0) continue;
			void Count(int j)
			{
				if (Terrain.Land[j] == 0) return;
				int d = Math.Abs(Terrain.Level[i] - Terrain.Level[j]);
				if (d == 1) detailEdges++;
				else if (d == Terrain.Step) terraceEdges++;
				else if (d > Terrain.Step) tallEdges++;
			}
			if (x + 1 < WorldSize) Count(i + 1);
			if (z + 1 < WorldSize) Count(i + WorldSize);
		}
		GD.Print($"[ledges] one-block {detailEdges}  standard-{Terrain.Step} {terraceEdges}  tall-cliff {tallEdges}");

		// What the player actually stands on, and what the canopy pass wrote.
		var caps = new Dictionary<byte, int>();
		int leaves = 0, trunks = 0;
		for (int z = 0; z < WorldSize; z++)
		for (int x = 0; x < WorldSize; x++)
		{
			int h = Terrain.Level[z * WorldSize + x];
			byte cap = Terrain.Grid.At(x, h - 1, z);
			caps.TryGetValue(cap, out int c);
			caps[cap] = c + 1;
		}
		foreach (byte b in Terrain.Grid.Blocks)
		{
			if (b >= Palette.LEAF_PINK && b <= Palette.LEAF_ROSE) leaves++;
			else if (b == Palette.TRUNK || b == Palette.TRUNK_PALE || b == Palette.TRUNK_ROSE) trunks++;
		}
		var cs = new System.Text.StringBuilder();
		foreach (var kv in caps)
		{
			float pct = kv.Value * 100f / n;
			if (pct >= 0.5f) cs.Append($"{BlockName(kv.Key)}:{pct:F1}%  ");
		}
		GD.Print($"[surface] {cs}");

		// Which provinces the plan actually produced. The surface histogram
		// above only says what got built; this says whether the biome that was
		// supposed to build it exists at all.
		var biomes = new Dictionary<Biome, int>();
		foreach (var r in Plan.Regions)
		{
			biomes.TryGetValue(r.Biome, out int bc);
			biomes[r.Biome] = bc + 1;
		}
		var bs = new System.Text.StringBuilder();
		foreach (var kv in biomes) bs.Append($"{kv.Key}:{kv.Value * 100f / Plan.Regions.Count:F0}%  ");
		GD.Print($"[biomes] {bs}");
		GD.Print($"[flora] leaf blocks {leaves}  trunk blocks {trunks}  trees {Vegetation.LastTreeCount}");

		// The surface pass reads a noise field to pick grass tones; if that
		// field never crosses its thresholds the world comes out one colour.
		var probe = new Noise2D(Seed + 6);
		float lo = 1f, hi = 0f, mean = 0f;
		int samples = 0;
		for (int z = 0; z < WorldSize; z += 7)
		for (int x = 0; x < WorldSize; x += 7)
		{
			float v = probe.Fbm01(x * 0.021f, z * 0.021f, 3);
			lo = Math.Min(lo, v); hi = Math.Max(hi, v); mean += v; samples++;
		}
		GD.Print($"[noise] tone field {lo:F3}..{hi:F3} mean {mean / samples:F3}");
	}

	private static string BlockName(byte id) => id switch
	{
		Palette.GRASS => "grass", Palette.GRASS_LIGHT => "grassL", Palette.GRASS_DEEP => "grassD",
		Palette.GRASS_STONE => "grassS", Palette.GRASS_LIGHT_STONE => "grassLS",
		Palette.GRASS_DEEP_STONE => "grassDS", Palette.SOIL => "soil", Palette.SAND => "sand",
		Palette.STONE => "stone", Palette.STONE_PALE => "stoneP", Palette.PATH => "path",
		Palette.SNOW => "snow", Palette.MUD => "mud", Palette.MOSS => "moss", Palette.SCREE => "scree",
		_ => id.ToString(),
	};

	private bool _capturing;

	/// <summary>
	/// Drive the camera to each named viewpoint and write a PNG. The wait
	/// before each shot is not politeness: streaming, particles and the
	/// temporal AA all need a few frames to settle or the review loop is
	/// judging a half-built frame.
	/// </summary>
	private async System.Threading.Tasks.Task RunCapture(string dir, HashSet<string> only, Vector3 spawn)
	{
		_capturing = true;
		DumpHeightMap(dir);
		foreach (var shot in Tools.Capture.Shots)
		{
			if (only != null && !only.Contains(shot.Name)) continue;

			var focus = shot.Subject switch
			{
				1 => FindFeature(spawn, wantCliff: true),
				2 => FindRiverFeature(),
				3 => Props.Bridges.Count > 0
					? new Vector3(Props.Bridges[0].X + 0.5f, Props.Bridges[0].DeckY + 1f, Props.Bridges[0].Z + 0.5f)
					: spawn,
				4 => FindTreeFeature(spawn),
				_ => spawn,
			};
			focus += new Vector3(0, 1.6f, 0);

			_streamer.UpdateAround(focus, prime: true);
			for (int i = 0; i < 6; i++)
			{
				_streamer.UpdateAround(focus);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			Tools.Capture.Place(Rig, shot, focus);
			for (int i = 0; i < 8; i++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			await RenderingServer.Singleton.ToSignal(
				RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			Tools.Capture.Save(GetViewport(), dir, shot.Name);
		}
		GD.Print("[capture] done");
		GetTree().Quit();
	}

	/// <summary>
	/// A top-down map of the finished heightfield, one pixel per column.
	///
	/// Reading terrain structure out of a 21-degree lens at ground level is
	/// guesswork; this shows the terraces, the coastline, the river network and
	/// the lake at a glance, and it is how you tell "the generator is flat" from
	/// "the camera happens to be standing on a shelf".
	/// </summary>
	private void DumpHeightMap(string dir)
	{
		var img = Image.CreateEmpty(WorldSize, WorldSize, false, Image.Format.Rgb8);
		for (int z = 0; z < WorldSize; z++)
		for (int x = 0; x < WorldSize; x++)
		{
			int h = Terrain.Level[z * WorldSize + x];
			Color c;
			if (h <= Terrain.Sea)
			{
				float d = Mathf.Clamp((Terrain.Sea - h) / 22f, 0f, 1f);
				c = new Color(0.55f, 0.62f, 0.90f).Lerp(new Color(0.10f, 0.12f, 0.42f), d);
			}
			else
			{
				// Banded by terrace so shelf boundaries are unmissable.
				int terrace = (h - Terrain.Base) / Terrain.Step;
				float t = Mathf.Clamp(terrace / 12f, 0f, 1f);
				c = new Color(0.72f, 0.80f, 0.55f).Lerp(new Color(0.98f, 0.94f, 0.90f), t);
				if (terrace % 2 == 0) c = c.Darkened(0.10f);
			}
			img.SetPixel(x, z, c);
		}
		DirAccess.MakeDirRecursiveAbsolute(dir);
		img.SavePng($"{dir}/map.png");
		GD.Print($"[capture] {dir}/map.png");
	}

	/// <summary>Nearest tall cliff, or nearest shoreline, for the fixed review shots.</summary>
	private Vector3 FindFeature(Vector3 from, bool wantCliff)
	{
		int bx = Mathf.FloorToInt(from.X), bz = Mathf.FloorToInt(from.Z);
		float best = -1f;
		var found = from;
		for (int r = 4; r < 150; r += 3)
		for (int a = 0; a < 48; a++)
		{
			float ang = a / 48f * Mathf.Tau;
			int x = bx + (int)(Mathf.Cos(ang) * r);
			int z = bz + (int)(Mathf.Sin(ang) * r);
			if (x < 4 || z < 4 || x >= WorldSize - 4 || z >= WorldSize - 4) continue;
			int i = z * WorldSize + x;

			float score;
			if (wantCliff)
			{
				if (Terrain.Level[i] <= Terrain.Sea + 2) continue;
				score = World.TerrainShape.DropBelow(Terrain.Level, WorldSize, x, z) - r * 0.05f;
			}
			else
			{
				if (Terrain.Level[i] > Terrain.Sea) continue;
				score = 40f - r * 0.2f;
			}
			if (score > best) { best = score; found = new Vector3(x + 0.5f, Terrain.Level[i], z + 0.5f); }
		}
		return found;
	}

	private Vector3 FindRiverFeature()
	{
		if (Terrain.RiverPath.Count == 0)
			return new Vector3(Plan.LakeRegion.Cx, Palette.WaterLevel, Plan.LakeRegion.Cz);
		RiverNode best = Terrain.RiverPath[0];
		float scoreBest = float.MaxValue;
		bool foundVisibleWater = false;
		foreach (var node in Terrain.RiverPath)
		{
			int x = Rng.ClampI((int)MathF.Floor(node.X + 0.5f), 0, WorldSize - 1);
			int z = Rng.ClampI((int)MathF.Floor(node.Z + 0.5f), 0, WorldSize - 1);
			if (Terrain.Level[z * WorldSize + x] > Terrain.Sea) continue;
			float score = MathF.Abs(node.T - 0.55f) + node.Width * 0.006f;
			if (score < scoreBest) { scoreBest = score; best = node; foundVisibleWater = true; }
		}
		if (!foundVisibleWater)
			return new Vector3(Plan.LakeRegion.Cx, Palette.WaterLevel, Plan.LakeRegion.Cz);
		return new Vector3(best.X, Palette.WaterLevel, best.Z);
	}

	private Vector3 FindTreeFeature(Vector3 from)
	{
		int cx = Mathf.FloorToInt(from.X), cz = Mathf.FloorToInt(from.Z);
		float bestScore = float.MinValue;
		var found = from;
		for (int z = Math.Max(2, cz - 150); z <= Math.Min(WorldSize - 3, cz + 150); z += 2)
		for (int x = Math.Max(2, cx - 150); x <= Math.Min(WorldSize - 3, cx + 150); x += 2)
		{
			int top = Terrain.Grid.HeightAt(x, z);
			if (top <= Terrain.Level[z * WorldSize + x]) continue;
			bool trunk = false;
			for (int y = Terrain.Level[z * WorldSize + x]; y < top; y++)
			{
				byte id = Terrain.Grid.At(x, y, z);
				if (id == Palette.TRUNK || id == Palette.TRUNK_PALE || id == Palette.TRUNK_ROSE)
				{ trunk = true; break; }
			}
			if (!trunk) continue;
			float d = MathF.Sqrt((x - from.X) * (x - from.X) + (z - from.Z) * (z - from.Z));
			float score = top - d * 0.08f;
			if (score > bestScore)
			{
				bestScore = score;
				found = new Vector3(x + 0.5f, top - 1.5f, z + 0.5f);
			}
		}
		return found;
	}

	public override void _Process(double delta)
	{
		if (Player == null || _capturing) return;
		var p = Player.GlobalPosition;

		_streamer.UpdateAround(p);
		Rig.Follow(p, Player.Velocity, delta);
		_character.Animate(Player.Velocity, Player.IsOnFloor(), Player.Swimming, Player.StepLift, delta);

		// Petals follow the camera focus rather than the world, so the effect
		// is sparse punctuation everywhere instead of a weather system.
		if (_petals != null) _petals.GlobalPosition = new Vector3(p.X, p.Y + 26f, p.Z);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb && mb.Pressed)
		{
			switch (mb.ButtonIndex)
			{
				case MouseButton.WheelUp: Rig.Zoom(-6f); return;
				case MouseButton.WheelDown: Rig.Zoom(6f); return;
				case MouseButton.Left: ClickToMove(mb.Position); return;
			}
		}
		if (e is InputEventKey k && k.Pressed && !k.Echo)
		{
			if (k.Keycode == Key.Q) Rig.Rotate45(-1);
			if (k.Keycode == Key.E) Rig.Rotate45(1);
		}
	}

	/// <summary>
	/// A destination chosen directly from the visible world. Where the exact
	/// point is unsuitable the nearest reachable result is used, and where no
	/// reasonable path exists nothing happens rather than the traveller setting
	/// off in a hopeful direction.
	/// </summary>
	private void ClickToMove(Vector2 screen)
	{
		var from = Rig.ProjectRayOrigin(screen);
		var dir = Rig.ProjectRayNormal(screen);

		var space = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(from, from + dir * 900f);
		query.CollideWithAreas = false;
		var hit = space.IntersectRay(query);

		Vector3 point;
		if (hit.Count > 0) point = (Vector3)hit["position"];
		else
		{
			// Nothing solid under the cursor: fall back to the water plane, so
			// clicking open water still sends you swimming.
			float t = (Palette.WaterLevel - from.Y) / dir.Y;
			if (t <= 0f) return;
			point = from + dir * t;
		}

		var route = _nav.FindPath(Player.GlobalPosition, point);
		if (route == null) return;
		Player.SetRoute(route);
		_pulse.Fire(point);
	}

	/* ================================================================
	 * assembly
	 * ================================================================ */

	private void BuildMaterials()
	{
		var inkShader = GD.Load<Shader>("res://shaders/ink.gdshader");

		// Pale runs draw first and dark runs draw over them by default. Endpoint
		// topology in the shader makes the sole exception: at least two actually
		// pale incident edges mask dark coverage inside their shared vertex.
		ShaderMaterial Ink(int priority, int pass = 0)
		{
			var m = new ShaderMaterial { Shader = inkShader, RenderPriority = priority };
			m.SetShaderParameter("ink_dark", Palette.InkDark);
			m.SetShaderParameter("ink_light", Palette.InkLight);
			m.SetShaderParameter("core_width", Palette.InkWidth);
			m.SetShaderParameter("ink_pass", pass);
			m.SetShaderParameter("water_level", Palette.WaterLevel);
			return m;
		}

		_inkLight = Ink(1, pass: 0);
		_inkDark = Ink(2, pass: 2);
		_inkLight.NextPass = Ink(3, pass: 1);
	}

	private ShaderMaterial MakeDetailMaterial() =>
		new() { Shader = GD.Load<Shader>("res://shaders/detail.gdshader") };

	private ShaderMaterial MakeVoxelMaterial()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
		mat.SetShaderParameter("sun_dir", Palette.SunDir);
		return mat;
	}

	private MeshInstance3D BuildWater()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
		mat.SetShaderParameter("shoal", Palette.WaterShoal);
		mat.SetShaderParameter("shallow", Palette.WaterShallow);
		mat.SetShaderParameter("deep", Palette.WaterDeep);
		mat.SetShaderParameter("warm", Palette.WaterWarm);
		mat.SetShaderParameter("sheen", Palette.WaterSheen);

		return new MeshInstance3D
		{
			Name = "Water",
			Mesh = new PlaneMesh { Size = new Vector2(WorldSize * 1.4f, WorldSize * 1.4f) },
			MaterialOverride = mat,
			Position = new Vector3(WorldSize * 0.5f, Palette.WaterLevel, WorldSize * 0.5f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private GpuParticles3D _petals;

	/// <summary>
	/// Sparse punctuation: a few large tumbling petals. Not weather, not
	/// confetti — the reference frames hold maybe a dozen at a time.
	/// </summary>
	private GpuParticles3D BuildPetals(Vector3 at)
	{
		var mat = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(38f, 2f, 38f),
			Direction = new Vector3(0.3f, -1f, 0.15f),
			Spread = 22f,
			Gravity = new Vector3(0, -0.9f, 0),
			InitialVelocityMin = 0.6f,
			InitialVelocityMax = 1.8f,
			AngularVelocityMin = -160f,
			AngularVelocityMax = 160f,
			ScaleMin = 0.7f,
			ScaleMax = 1.5f,
			Damping = new Vector2(0.1f, 0.4f),
		};

		var quad = new QuadMesh { Size = new Vector2(0.34f, 0.22f) };
		var qmat = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = Palette.PetalColors[0],
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
			VertexColorUseAsAlbedo = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		quad.Material = qmat;

		_petals = new GpuParticles3D
		{
			Name = "Petals",
			Amount = 160,
			Lifetime = 9.0,
			ProcessMaterial = mat,
			DrawPass1 = quad,
			Position = at + new Vector3(0, 26f, 0),
			VisibilityAabb = new Aabb(new Vector3(-60, -60, -60), new Vector3(120, 120, 120)),
		};
		return _petals;
	}

	private CanvasLayer BuildGrade()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/grade.gdshader") };
		mat.SetShaderParameter("lift", Palette.GradeLift);
		mat.SetShaderParameter("gamma_", Palette.GradeGamma);
		mat.SetShaderParameter("gain", Palette.GradeGain);
		mat.SetShaderParameter("saturation", Palette.GradeSaturation);
		mat.SetShaderParameter("contrast", Palette.GradeContrast);
		mat.SetShaderParameter("vignette", Palette.GradeVignette);

		var rect = new ColorRect
		{
			Material = mat,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		var layer = new CanvasLayer { Name = "Grade", Layer = 100 };
		layer.AddChild(rect);
		return layer;
	}

	private static void SetupInput()
	{
		// Defined in code rather than in project.godot so the controls travel
		// with the build and cannot drift out of sync with what reads them.
		void Bind(string action, params Key[] keys)
		{
			if (InputMap.HasAction(action)) InputMap.EraseAction(action);
			InputMap.AddAction(action);
			foreach (var k in keys)
				InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
		}

		Bind("move_forward", Key.W, Key.Up);
		Bind("move_back", Key.S, Key.Down);
		Bind("move_left", Key.A, Key.Left);
		Bind("move_right", Key.D, Key.Right);
		Bind("jump", Key.Space);
	}
}
