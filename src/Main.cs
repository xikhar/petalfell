using System;
using System.Collections.Generic;
using Godot;
using Petalfell.Core;
using Petalfell.Gameplay;
using Petalfell.Items;
using Petalfell.Player;
using Petalfell.Render;
using Petalfell.Skills;
using Petalfell.UI;
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
	[Export] public int WorldSize = 3456;
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
	private MovementPuffs _movementPuffs;
	private GlobalInventory _inventory;
	private WorldItemSystem _worldItems;
	private ItemGameplay _itemGameplay;
	private CampfireSystem _campfires;
	private SkillSystem _skills;
	private InteractionLayer _interactions;
	private InventoryView _inventoryView;
	private SkillSelectorView _skillSelector;
	private AmbientDrift _ambientDrift;
	private UI.WorldMap _worldMap;
	private Fauna _fauna;
	private DayCycle _day;
	private Vector3? _focusOverride;
	private ShaderMaterial _inkLight, _inkDark, _waterMat;
	private Tools.DeveloperMenu _developerMenu;

	public override void _Ready()
	{
		// Before any material is built: a shader naming a global uniform that has
		// not been registered fails to compile.
		DayCycle.RegisterGlobals();
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
		// After the bridges: a house may not stand where a crossing lands, and
		// Fits() tests that by looking for columns already built above ground.
		Settlements.Build(Terrain, Terrain.Sites, Seed);
		Landmarks.Build(Terrain, Terrain.Marks);
		var tTown = Time.GetTicksMsec();
		Vegetation.Populate(Terrain, Seed);
		var t3 = Time.GetTicksMsec();
		GD.Print($"[petalfell] map {Map.Id}  plan {t1 - t0}ms  terrain {t2 - t1}ms  props {tProps - t2}ms  " +
		         $"towns {tTown - tProps}ms  flora {t3 - tTown}ms  " +
		         $"regions {Plan.Regions.Count}  rivers {Plan.Rivers.Count}  " +
		         $"bridges {Props.Bridges.Count} ({Props.RoadDecks} for roads, {Terrain.Roads.Crossings.Count} crossings)  " +
		         $"roads {Terrain.Roads.Segments.Count} (+{Terrain.Roads.Unreachable} unreachable)  " +
		         $"settlements {Terrain.Sites.Count}  " +
		         $"buildings {Settlements.LastBuildingCount}  landmarks {Terrain.Marks.Count}");
		{
			var st = new int[4];
			foreach (var site in Terrain.Sites) st[(int)site.State]++;
			GD.Print($"[remnants] holdout {st[0]}  remnant {st[1]}  ruin {st[2]}  monument {st[3]}");
		}
		GD.Print($"[terrain] {Terrain.Timings}");
		ReportAbandonment();
		ReportTerrain();

		GroundDetail.Seed = Seed;

		BuildMaterials();

		AddChild(Atmosphere.Build());
		var sun = Atmosphere.Sun();
		var fill = Atmosphere.Fill();
		AddChild(sun);
		AddChild(fill);

		_streamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
		AddChild(_streamer);
		_streamer.Setup(Terrain, MakeVoxelMaterial(), _inkLight, _inkDark,
			MakeDetailMaterial(), MakeWaterDetailMaterial());

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
		Player.ResetPhysicsInterpolation();

		_character = new Character { Name = "Traveller" };
		Player.AddChild(_character);
		_character.Setup(_inkLight, _inkDark);

		_nav = new Navigation(Terrain);

		_dog = new Dog { Name = "Dog", Position = spawn + new Vector3(2.2f, 0, 1.4f) };
		AddChild(_dog);
		_dog.Setup(_nav, Player, _inkLight, _inkDark, Seed);

		_inventory = GetNode<GlobalInventory>("/root/GlobalInventory");
		_worldItems = new WorldItemSystem { Name = "WorldItems" };
		AddChild(_worldItems);
		_worldItems.Setup(_inkLight, _inkDark, Player);

		_itemGameplay = new ItemGameplay { Name = "ItemGameplay" };
		AddChild(_itemGameplay);
		_itemGameplay.Setup(_inventory, _worldItems, Player, _character, _dog);

		_campfires = new CampfireSystem { Name = "Campfires" };
		_campfires.Setup(Terrain, _inkLight, _inkDark);
		AddChild(_campfires);

		_interactions = new InteractionLayer { Name = "Interactions" };
		_interactions.Setup(Player);
		AddChild(_interactions);
		_interactions.Register(_itemGameplay);

		_skills = new SkillSystem { Name = "Skills" };
		_skills.Setup(_inventory, _campfires, Player, _dog, _nav);
		_skills.NoticeRequested += _interactions.ShowNotice;
		AddChild(_skills);
		_interactions.Register(_skills);

		var quickLoadout = new QuickLoadoutHud { Name = "QuickLoadout" };
		quickLoadout.Setup(_inventory);
		AddChild(quickLoadout);

		_inventoryView = new InventoryView { Name = "Inventory" };
		_inventoryView.Setup(_inventory);
		AddChild(_inventoryView);

		_skillSelector = new SkillSelectorView { Name = "SkillSelector" };
		_skillSelector.Setup(_skills);
		AddChild(_skillSelector);

		_pulse = new ClickPulse { Name = "ClickPulse" };
		AddChild(_pulse);

		_movementPuffs = new MovementPuffs { Name = "MovementPuffs" };
		AddChild(_movementPuffs);

		Rig = new CameraRig { Name = "Camera", Current = true };
		AddChild(Rig);
		Rig.Follow(spawn, Vector3.Zero, 1.0);

		// After the rig: it mirrors that camera, so it needs the real one.
		var reflection = new PlanarReflection { Name = "LakeReflection" };
		reflection.Setup(Rig, _waterMat, Palette.WaterLevel);
		AddChild(reflection);

		// Kept separate from game-facing UI. This is a disposable live-tuning
		// surface, toggled with tilde, and owns no gameplay or menu state.
		// The clock is built BEFORE the developer overlay, which takes it as a
		// constructor dependency. Built after, Setup received a null and quietly
		// skipped every time-of-day control — the overlay builds its whole panel
		// in _Ready, so there is no second chance to add them later.
		_day = new DayCycle { Name = "DayCycle" };
		AddChild(_day);
		_day.Setup(Atmosphere.LastEnvironment, sun, fill, Atmosphere.LastSky, _waterMat);

		_developerMenu = new Tools.DeveloperMenu { Name = "DeveloperSettings" };
		_developerMenu.Setup(_inkLight, _inkDark, Rig, _day);
		AddChild(_developerMenu);

		_fauna = new Fauna { Name = "Fauna" };
		AddChild(_fauna);
		_fauna.Setup(Terrain, _inkLight, _inkDark, Seed);

		_worldMap = new UI.WorldMap { Name = "WorldMap" };
		AddChild(_worldMap);
		_worldMap.Setup(Terrain);
		_worldMap.TeleportRequested += TeleportTo;

		_ambientDrift = new AmbientDrift { Name = "AmbientDrift" };
		_ambientDrift.Setup(Terrain, spawn);
		AddChild(_ambientDrift);
		AddChild(BuildGrade());

		GD.Print($"[petalfell] spawn {spawn}  chunks {_streamer.LoadedCount}");

		var (shotDir, only) = Tools.Capture.ParseArgs();
		if (shotDir != null) _ = RunCapture(shotDir, only, spawn);
	}

	/// <summary>
	/// How the retreat fell across the map. Printed because every decaying system
	/// reads this field, so if its distribution is wrong everything downstream is
	/// wrong in the same direction and it is far cheaper to see it here.
	/// </summary>
	private void ReportAbandonment()
	{
		var band = new int[5];
		float sum = 0f;
		foreach (var r in Plan.Regions)
		{
			sum += r.Abandonment;
			band[Math.Clamp((int)(r.Abandonment * 5f), 0, 4)]++;
		}
		var sb = new System.Text.StringBuilder();
		string[] names = { "held", "recent", "old", "older", "long-gone" };
		for (int i = 0; i < 5; i++)
			sb.Append($"{names[i]}:{band[i] * 100f / Math.Max(1, Plan.Regions.Count):F0}%  ");
		GD.Print($"[retreat] mean {sum / Math.Max(1, Plan.Regions.Count):F2}  {sb}");
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
		foreach (byte b in Terrain.Grid.Placed)
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
		GD.Print($"[flora] leaf blocks {leaves}  trunk blocks {trunks}  trees {Vegetation.LastTreeCount}  " +
		         $"placed blocks {Terrain.Grid.PlacedCount}");

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
				// Road decks are appended after the river ones, so the last bridge
				// is a crossing that actually carries a route — which is the one
				// worth reviewing.
				3 => Props.Bridges.Count > 0
					? new Vector3(Props.Bridges[^1].X + 0.5f, Props.Bridges[^1].DeckY + 1f,
						Props.Bridges[^1].Z + 0.5f)
					: spawn,
				4 => FindTreeFeature(spawn),
				5 => FindBiomeFeature(World.Biome.Sakura),
				7 => FindSettlementFeature(RemnantState.Ruin),
				8 => FindSettlementFeature(RemnantState.Holdout),
				9 => FindSettlementFeature(RemnantState.Monument),
				10 => FindLandmark(LandmarkForm.Watchtower),
				11 => FindLandmark(LandmarkForm.StandingStones),
				12 => FindLandmark(LandmarkForm.Farmstead),
				6 => Plan.Lakes.Count > 0
					? new Vector3(Plan.Lakes[0].Cx, Terrain.Sea, Plan.Lakes[0].Cz)
					: FindRiverFeature(),
				_ => spawn,
			};
			focus += new Vector3(0, 1.6f, 0);

			if (shot.Time >= 0f && _day != null)
			{
				_day.TimeOfDay = shot.Time;
				_day.Paused = true;
			}

			// The drift follows the player, and the player is not where a review
			// shot is framed. Without re-seating it, every captured frame is
			// judged with no petals, motes or leaves in the air at all — the
			// exact things being tuned.
			_ambientDrift.Setup(Terrain, focus);
			_movementPuffs?.SetProcess(false);
			// Wildlife is streamed around the traveller, and the traveller is not
			// where a review shot is framed. Without re-anchoring it, every capture
			// of a meadow is a capture of an empty meadow.
			_focusOverride = focus;

			_streamer.UpdateAround(focus, prime: true);
			for (int i = 0; i < 6; i++)
			{
				_streamer.UpdateAround(focus);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			Tools.Capture.Place(Rig, shot, focus);
			// Airborne drift does NOT appear in captures. GPU particles are
			// simulated by a compute pass that the offscreen capture path never
			// runs, so a review screenshot always shows still air no matter how
			// long it settles — verified against the live game, where the motes
			// and petals are present. Judge the drift by playing, not by these.
			for (int i = 0; i < 24; i++)
			{
				Tools.Capture.Place(Rig, shot, focus);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}

			await RenderingServer.Singleton.ToSignal(
				RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			Tools.Capture.Save(GetViewport(), dir, shot.Name);
			GD.Print($"[capture] {shot.Name} fauna {_fauna.LiveCount}");
		}
		// The live map, shot through the real UI rather than exported from the
		// renderer behind it. The export proves the cartography; only this proves
		// the panel, the framing and the markers actually come up on screen.
		if (only == null || only.Contains("worldmap"))
		{
			_worldMap.SetPlayer(spawn);
			_worldMap.Toggle();
			for (int i = 0; i < 4; i++)
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await RenderingServer.Singleton.ToSignal(
				RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			Tools.Capture.Save(GetViewport(), dir, "worldmap");
			_worldMap.Toggle();
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
		var img = UI.WorldMap.Render(Terrain, markers: true);
		DirAccess.MakeDirRecursiveAbsolute(dir);
		img.SavePng($"{dir}/map.png");
		GD.Print($"[capture] {dir}/map.png");
	}

	/// <summary>
	/// Put the traveller down near a point on the map, on ground they can stand on.
	///
	/// FindSpawn does the search — it already knows what a safe surface is, and
	/// reusing it means a teleport cannot land somewhere the game would never have
	/// spawned anyone. The chunks around the destination are primed before the
	/// move, because arriving inside unstreamed world drops the player through it.
	/// </summary>
	private void TeleportTo(Vector3 world)
	{
		var wanted = new MapPoint
		{
			X = Mathf.Clamp(world.X / WorldSize, 0f, 1f),
			Z = Mathf.Clamp(world.Z / WorldSize, 0f, 1f),
		};
		var (sx, sz) = Terrain.FindSpawn(wanted);
		var landing = new Vector3(sx + 0.5f, Terrain.Level[sz * WorldSize + sx] + 0.2f, sz + 0.5f);

		_streamer.UpdateAround(landing, prime: true);
		Player.Velocity = Vector3.Zero;
		Player.Route = null;
		Player.GlobalPosition = landing;
		Player.ResetPhysicsInterpolation();
		_dog.GlobalPosition = landing + new Vector3(2.2f, 0, 1.4f);
		_ambientDrift?.Setup(Terrain, landing);
		_worldMap.SetPlayer(landing);
		Rig.Follow(landing, Vector3.Zero, 1.0);
		if (_worldMap.IsOpen) _worldMap.Toggle();
		GD.Print($"[teleport] {landing}");
	}

	/// <summary>Somewhere to stand and look at one of the generated landmarks.</summary>
	private Vector3 FindLandmark(LandmarkForm want)
	{
		foreach (var m in Terrain.Marks)
			if (m.Form == want) return new Vector3(m.X + 0.5f, m.Level, m.Z + 0.5f);
		return new Vector3(WorldSize * 0.5f, Terrain.Sea, WorldSize * 0.5f);
	}

	/// <summary>The largest remnant of a given state, for a shot of one.</summary>
	private Vector3 FindSettlementFeature(RemnantState want = RemnantState.Ruin)
	{
		SettlementSite best = null;
		foreach (var site in Terrain.Sites)
			if (site.State == want && site.Buildings.Count > 0 &&
				(best == null || site.Buildings.Count > best.Buildings.Count)) best = site;
		// Monuments have no buildings at all, so fall back to any site of the kind.
		if (best == null)
			foreach (var site in Terrain.Sites)
				if (site.State == want) { best = site; break; }
		if (best == null) return new Vector3(WorldSize * 0.5f, Terrain.Sea, WorldSize * 0.5f);
		// Frame a HOUSE, not the middle of the site. The centre of a settlement is
		// its square, and a review shot of a square is a review shot of paving.
		return new Vector3(best.X + 0.5f, best.Level, best.Z + 0.5f);
	}

	/// <summary>
	/// The centre of the largest stand of a given province, for review shots
	/// that have to be judged somewhere representative rather than wherever the
	/// chapter happened to put the spawn.
	/// </summary>
	private Vector3 FindBiomeFeature(World.Biome want)
	{
		var best = default(Vector3);
		float bestScore = -1f;
		foreach (var region in Plan.Regions)
		{
			if (region.Biome != want) continue;
			int x = Mathf.Clamp((int)region.Cx, 8, WorldSize - 9);
			int z = Mathf.Clamp((int)region.Cz, 8, WorldSize - 9);
			int h = Terrain.Level[z * WorldSize + x];
			if (h <= Terrain.Sea + 1) continue;
			// Prefer a stand with neighbours of its own kind: one isolated region
			// is a patch, several together is a province you can photograph.
			float score = 0f;
			foreach (int n in region.Neighbours)
				if (Plan.Regions[n].Biome == want) score += 1f;
			if (score <= bestScore) continue;
			bestScore = score;
			best = new Vector3(x + 0.5f, h, z + 0.5f);
		}
		return bestScore < 0f ? new Vector3(WorldSize * 0.5f, Terrain.Sea + 6, WorldSize * 0.5f) : best;
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
		if (Player == null) return;
		// Physics moves the controller at a fixed cadence while rendering may run
		// much faster. Follow the same interpolated transform Godot presents for
		// the character; otherwise the small model jumps several screen pixels per
		// physics tick and persistence makes adjacent positions look like a ghost.
		var p = Player.GetGlobalTransformInterpolated().Origin;
		_movementPuffs?.Advance(p, Player.Velocity, Player.IsOnFloor(), Player.Swimming);
		_ambientDrift?.Advance(p, delta, _day?.NightAmount ?? 0f);
		_fauna?.Advance(_focusOverride ?? p, delta);
		_worldMap?.SetPlayer(p);
		SyncGameplayInput();
		if (_capturing) return;

		_streamer.UpdateAround(p);
		Rig.Follow(p, Player.Velocity, delta);
		_character.Animate(Player.Velocity, Player.Facing,
			Player.IsOnFloor(), Player.Swimming, Player.Sitting, delta);
	}

	public override void _UnhandledInput(InputEvent e)
	{
		// T owns the skills surface even if another reading surface is currently
		// open. Only one modal may own movement and mouse input at a time.
		if (_skillSelector?.HandleInput(e) == true)
		{
			if (_skillSelector.IsOpen)
			{
				_inventoryView?.Close();
				if (_worldMap?.IsOpen == true) _worldMap.Toggle();
			}
			SyncGameplayInput();
			GetViewport().SetInputAsHandled();
			return;
		}
		if (_inventoryView?.HandleInput(e) == true)
		{
			// Map and inventory are both full-screen reading surfaces. Opening one
			// closes the other rather than leaving two input owners stacked.
			if (_inventoryView.IsOpen && _worldMap?.IsOpen == true) _worldMap.Toggle();
			SyncGameplayInput();
			GetViewport().SetInputAsHandled();
			return;
		}
		if (e is InputEventKey mk && mk.Pressed && !mk.Echo && mk.PhysicalKeycode == Key.M)
		{
			_worldMap?.Toggle();
			_inventoryView?.Close();
			_skillSelector?.Close();
			SyncGameplayInput();
			GetViewport().SetInputAsHandled();
			return;
		}
		// While the map is up it owns the mouse: scrolling zooms the map, not the
		// rig, and a click pans rather than sending the traveller somewhere they
		// cannot be seen to walk to.
		if (_worldMap != null && _worldMap.IsOpen) return;

		if (_interactions?.HandleInput(e) == true)
		{
			GetViewport().SetInputAsHandled();
			return;
		}
		if (_itemGameplay?.HandleInput(e) == true)
		{
			GetViewport().SetInputAsHandled();
			return;
		}
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

	private void SyncGameplayInput()
	{
		if (Player == null) return;
		bool blocked = (_inventoryView?.IsOpen ?? false) ||
			(_skillSelector?.IsOpen ?? false) || (_worldMap?.IsOpen ?? false);
		Player.InputEnabled = !blocked;
		_interactions?.SetSuppressed(blocked);
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

	private ShaderMaterial MakeWaterDetailMaterial() =>
		new() { Shader = GD.Load<Shader>("res://shaders/waterdetail.gdshader") };

	private ShaderMaterial MakeVoxelMaterial()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/voxel.gdshader") };
		mat.SetShaderParameter("sun_dir", Palette.SunDir);
		mat.SetShaderParameter("plane_y", Palette.WaterLevel);
		return mat;
	}

	private MeshInstance3D BuildWater()
	{
		var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader") };
		_waterMat = mat;
		mat.SetShaderParameter("shoal", Palette.WaterShoal);
		mat.SetShaderParameter("shallow", Palette.WaterShallow);
		mat.SetShaderParameter("deep", Palette.WaterDeep);
		mat.SetShaderParameter("warm", Palette.WaterWarm);
		mat.SetShaderParameter("sheen", Palette.WaterSheen);
		// The mirrored pass is held partway toward this gradient where it sees
		// through the world's shell, so it has to be the same sky the player is
		// standing under.
		mat.SetShaderParameter("sky_low", Palette.SkyHorizon);
		mat.SetShaderParameter("sky_high", Palette.SkyZenith);
		mat.SetShaderParameter("sun_colour", Palette.SunColor);
		mat.SetShaderParameter("sun_dir", Palette.SunDir);
		mat.SetShaderParameter("plane_y", Palette.WaterLevel);

		return new MeshInstance3D
		{
			Name = "Water",
			// Subdivided, despite being flat. The shader reconstructs its own
			// surface position analytically, but the ENGINE still interpolates
			// VERTEX across the mesh for fog, and across a thousand-unit plane
			// drawn as two triangles that interpolation carries enough error to
			// paint broad triangular washes of haze over the whole lake. Roughly
			// eight-unit quads cost nothing and remove it.
			Mesh = new PlaneMesh
			{
				Size = new Vector2(WorldSize * 1.4f, WorldSize * 1.4f),
				SubdivideWidth = 128,
				SubdivideDepth = 128,
			},
			MaterialOverride = mat,
			Position = new Vector3(WorldSize * 0.5f, Palette.WaterLevel, WorldSize * 0.5f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			// Kept off the layer the reflection pass draws, or the lake would be
			// rendered into the texture the lake reads from.
			Layers = PlanarReflection.WaterLayer,
		};
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
		Bind("loadout_1", Key.Key1);
		Bind("loadout_2", Key.Key2);
		Bind("loadout_3", Key.Key3);
		Bind("loadout_4", Key.Key4);
		Bind("cycle_left_hand", Key.Z);
		Bind("cycle_right_hand", Key.X);
		Bind("throw_left", Key.F);
		Bind("throw_right", Key.G);
		Bind("interact", Key.R);
		Bind("dog_fetch", Key.U);
		Bind("world_map", Key.M);
		Bind("inventory", Key.Tab);
		Bind("skill_selector", Key.T);
	}
}
