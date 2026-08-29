using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Petalfell.Core;
using Petalfell.Player;
using Petalfell.Render;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Game-renderer review window for one production atlas sector or one authored
/// domain spanning several sectors. This is not a second terrain generator: it
/// reads disposable sector artifacts, materialises their columns, and sends
/// them through the ordinary chunk mesher, ink, atmosphere, water and grade.
/// </summary>
public partial class AtlasSectorReview : Node3D
{
	private const int InteractiveStreamRadius = 8;
	private const int CaptureStreamRadius = 12;
	// Play matches the ordinary game radius. 14 loaded ~600 chunks around the
	// traveller and meshing them on the 5ms budget made walking hitch.
	private const int DomainInteractiveStreamRadius = 8;
	private const int DomainCaptureStreamRadius = 24;
	private static readonly Capture.Shot[] SectorShots =
	{
		new("atlas_near", 86f, 45f, 31f),
		new("atlas_wide", 170f, 45f, 38f),
		new("atlas_reverse", 170f, 225f, 36f),
		new("atlas_far", 300f, 45f, 48f),
	};
	private static readonly Capture.Shot[] DomainShots =
	{
		// The late-morning key keeps the pale architecture high-key while giving its
		// stepped planes enough shadow length to remain legible at domain distance.
		// Pitch is low enough that the gate opening remains a face, not a roof.
		// Atlas water sits around 6540,6740 — ~280 south and 140 east of the gate.
		// Yaw 45 parked the camera east of that pool, so 24° near never saw it.
		// Yaw 23 aims along the gate–water vector. Near owns the approach shrine
		// (refs 6/7/8/10): 118 fills the frame with the shrine. Pitch 20 aimed
		// at the south face and left a fortress in the midground; 38 still put
		// the 124 at ~143, 4% into a 140–210 fog ramp. 42 pushes that mass to
		// ~159 so the same ramp hazes it. Play walking-distance stays 150.
		// 720/920 put the pool in the lower third.
		new("domain_near", 118f, 23f, 42f, time: 0.36f),
		// Wide/far keep the same yaw as near so the drowned pool sits on the
		// look-at axis. Yaw 45 / focus on the water put the gate behind the
		// lens. 21° at 500 cannot hold water+gate; 30° can. 720u made a
		// 22-block cliff ~1° of a 30° frame (atan(22/900)) and put the look-at
		// in the fog ramp, so the pool was the subject and the massif haze.
		// Look-at on the waterfront SW corner (plan −70,−72) held the E–W
		// cliff, the pool and the inland gate as two monuments 230 apart.
		// Refs 1/2 are one gate in a cliff with a processional through the
		// water; look-at on the drowned spur (plan −122,−168) makes that
		// opening the subject and the inland massif hinterland haze.
		new("domain_wide", 520f, 23f, 18f, time: 0.36f),
		new("domain_reverse", 520f, 203f, 18f, time: 0.36f),
		new("domain_far", 700f, 23f, 20f, time: 0.36f),
		// Midnight (0.00) is the stage-moon: deep blue ambient. Reference-5 is a
		// high-key lavender twilight, which is the 0.83 key already in Palette.Day.
		new("domain_night_near", 118f, 23f, 42f, time: 0.83f),
		new("domain_night_wide", 520f, 23f, 18f, time: 0.83f),
		new("domain_night_reverse", 520f, 203f, 18f, time: 0.83f),
		new("domain_night_far", 700f, 23f, 20f, time: 0.83f),
	};

	private string _mapPath;
	private int _sectorX;
	private int _sectorZ;
	private string _domainId;
	private CanonicalDomain _domain;
	private Vector2I? _requestedFocus;
	private string _shotDirectory;
	private HashSet<string> _only;
	private AtlasSectorWindow _window;
	private ChunkStreamer _streamer;
	private CameraRig _camera;
	private DayCycle _day;
	private DirectionalLight3D _key;
	private DirectionalLight3D _fill;
	private Godot.Environment _environment;
	private Vector3 _focusLocal;
	private Node3D _content;
	private Controller _player;
	private Character _character;
	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private float _interactiveYaw = 45f;
	private float _interactivePitch = 34f;
	private float _interactiveDistance = 150f;
	private bool _started;
	private bool _playable;
	private bool IsDomain => _domainId != null;
	private int StreamRadius => IsDomain
		? (_shotDirectory == null ? DomainInteractiveStreamRadius : DomainCaptureStreamRadius)
		: (_shotDirectory == null ? InteractiveStreamRadius : CaptureStreamRadius);
	private Capture.Shot[] ReviewShots => IsDomain ? DomainShots : SectorShots;

	public static bool TryRun(Node owner, string defaultMapPath)
	{
		string sector = null;
		string domain = null;
		string focus = null;
		string mapPath = defaultMapPath;
		bool legacyWorld = false;
		bool playAtlas = false;
		var args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--review-sector" && i + 1 < args.Length) sector = args[++i];
			else if (args[i].StartsWith("--review-sector=")) sector = args[i][16..];
			else if (args[i] == "--review-domain" && i + 1 < args.Length) domain = args[++i];
			else if (args[i].StartsWith("--review-domain=")) domain = args[i][16..];
			else if (args[i] == "--play-atlas-domain" && i + 1 < args.Length)
			{
				domain = args[++i];
				playAtlas = true;
			}
			else if (args[i].StartsWith("--play-atlas-domain="))
			{
				domain = args[i][20..];
				playAtlas = true;
			}
			else if (args[i] == "--play-atlas") playAtlas = true;
			else if (args[i] == "--legacy-world") legacyWorld = true;
			else if (args[i] == "--review-focus" && i + 1 < args.Length) focus = args[++i];
			else if (args[i].StartsWith("--review-focus=")) focus = args[i][15..];
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}
		(string shotDirectory, HashSet<string> only) = Capture.ParseArgs();
		if (sector != null && domain != null)
			throw new InvalidOperationException("choose either --review-sector or --review-domain, not both");
		if (legacyWorld && sector == null && domain == null && !playAtlas) return false;
		if (shotDirectory != null && sector == null && domain == null && !playAtlas) return false;
		if (sector == null && domain == null)
			domain = "shallows-gateway-domain";

		int sectorX = 0, sectorZ = 0;
		if (sector != null) (sectorX, sectorZ) = ParsePair(sector, "sector address");
		Vector2I? requestedFocus = null;
		if (focus != null)
		{
			(int x, int z) = ParsePair(focus, "review focus");
			requestedFocus = new Vector2I(x, z);
		}
		var review = new AtlasSectorReview
		{
			Name = domain == null ? "AtlasSectorReview" : "AtlasDomainReview",
			_mapPath = mapPath,
			_sectorX = sectorX,
			_sectorZ = sectorZ,
			_domainId = domain,
			_requestedFocus = requestedFocus,
			_shotDirectory = shotDirectory,
			_only = only,
		};
		owner.AddChild(review);
		return true;
	}

	private static (int x, int z) ParsePair(string value, string label)
	{
		string[] parts = value.Split(',');
		if (parts.Length != 2 ||
		    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
		    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
			throw new InvalidOperationException($"{label} '{value}' must be x,z");
		return (x, z);
	}

	public override void _Ready()
	{
		_ = Start();
	}

	private async System.Threading.Tasks.Task Start()
	{
		try
		{
			DayCycle.RegisterGlobals();
			MapDefinition map = MapDefinition.Load(_mapPath);
			if (map.CanonicalAtlas == null)
				throw new InvalidOperationException($"map '{_mapPath}' has no canonical production atlas");
			var compiler = new AtlasSectorCompiler(map.CanonicalAtlas, map.DefaultSeed, map.CanonicalAtlasPath);
			int pathSplit = map.CanonicalAtlasPath.LastIndexOf('/');
			string packagePath = pathSplit >= 0 ? map.CanonicalAtlasPath[..pathSplit] : "res://content";
			string sourceDescription;
			AtlasSectorData data;
			if (IsDomain)
			{
				if (map.CanonicalAtlas.Topology == null)
					throw new InvalidOperationException("production atlas has no registered authored topology");
				_domain = map.CanonicalAtlas.Topology.Domains.Find(d => d.Id == _domainId) ??
					throw new InvalidOperationException($"canonical domain '{_domainId}' does not exist");
				if (_domain.Plan == null)
					throw new InvalidOperationException($"canonical domain '{_domainId}' has no loaded L3 plan");
				(int minX, int minZ, int maxX, int maxZ) = SectorBounds(_domain, map.CanonicalAtlas);
				data = AtlasSectorMosaic.Compose(map.CanonicalAtlas, minX, minZ, maxX, maxZ,
					(sx, sz) => LoadOrRebuild(compiler, packagePath, sx, sz));
				sourceDescription = $"domain {_domainId} sectors {minX},{minZ}..{maxX},{maxZ}";
			}
			else
			{
				data = LoadOrRebuild(compiler, packagePath, _sectorX, _sectorZ);
				sourceDescription = $"sector {_sectorX},{_sectorZ}";
			}

			_window = new AtlasSectorWindow(data, map.CanonicalAtlas, map.DefaultSeed);
			DomainBlockoutStatistics? blockout = IsDomain
				? DomainPlanBlockout.Compile(_window, map.CanonicalAtlas.Topology, _domain)
				: null;
			AtlasDomainDressingStatistics? dressing = IsDomain
				? AtlasDomainDressing.Apply(_window, map.CanonicalAtlas,
					_domain.Plan, map.DefaultSeed)
				: AtlasDomainDressing.ApplyWilderness(_window, map.CanonicalAtlas, map.DefaultSeed);
			_content = new Node3D { Name = "AtlasWindow", Position = _window.GlobalOrigin };
			AddChild(_content);

			// The legacy world has one water plane and can hide submerged ink by a
			// uniform height. An atlas window has many surface heights; drawing its
			// same ink passes before water lets the real geometry occlude every bed.
			var ink = WorldMaterials.CreateInk(data.SeaLevel, priorityOffset: -6);
			_inkLight = ink.Light;
			_inkDark = ink.Dark;
			_streamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
			if (IsDomain && _shotDirectory == null) _streamer.FrameBudgetMs = 8.0;
			_content.AddChild(_streamer);
			GroundDetail.Seed = map.DefaultSeed;
			_streamer.Setup(_window.Grid, WorldMaterials.CreateVoxel(data.SeaLevel),
				ink.Light, ink.Dark, buildCollision: true,
				WorldMaterials.CreateDetail(), WorldMaterials.CreateWaterDetail());

			ShaderMaterial waterMaterial = WorldMaterials.CreateWater(data.SeaLevel,
				surfaceFromMesh: true, reflectionAvailable: false);
			var water = _window.BuildWater(waterMaterial);
			if (water != null) _content.AddChild(water);

			AddChild(Atmosphere.Build());
			_environment = Atmosphere.LastEnvironment;
			_key = Atmosphere.Sun();
			_fill = Atmosphere.Fill();
			AddChild(_key);
			AddChild(_fill);
			_day = new DayCycle { Name = "DayCycle", Paused = _shotDirectory != null };
			AddChild(_day);
			_day.Setup(_environment, _key, _fill, Atmosphere.LastSky,
				waterMaterial, map.DefaultSeed);
			AddChild(WorldMaterials.CreateGrade());

			Vector2I? canonicalFocus = IsDomain
				? new Vector2I(_domain.Plan.Origin.X, _domain.Plan.Origin.Z)
				: null;
			_focusLocal = _requestedFocus is Vector2I focus
				? _window.FocusAtGlobal(focus.X, focus.Y, StreamRadius)
				: canonicalFocus is Vector2I authored
					? _window.FocusAtGlobal(authored.X, authored.Y, StreamRadius)
					: _window.FindReviewFocus();
			// The information-rich point may sit near a sector edge. A one-sector
			// artifact only owns one apron there, so clamp the review anchor far
			// enough inside the local window that every requested chunk is real.
			if (_requestedFocus == null && !IsDomain)
				_focusLocal = _window.FocusAtGlobal(
					(int)(_focusLocal.X + _window.Data.OriginX),
					(int)(_focusLocal.Z + _window.Data.OriginZ), StreamRadius);
			_camera = new CameraRig { Name = "AtlasCamera", Current = true };
			AddChild(_camera);
			if (IsDomain)
			{
				_camera.Far = 1600f;
				_interactiveDistance = 300f;
				_camera.SetZoomLimits(50f, 280f);
			}
			SpawnTraveller();
			if (_shotDirectory == null)
			{
				_playable = true;
				_camera.TargetDistance = IsDomain ? 90f : 75f;
				_camera.Distance = _camera.TargetDistance;
				_camera.TargetYaw = Mathf.DegToRad(_interactiveYaw);
				_camera.Yaw = _camera.TargetYaw;
			}
			else PlaceInteractiveCamera();
			_streamer.UpdateAround(_player?.Position ?? _focusLocal, prime: true);
			AtlasSectorStatistics stats = data.CoreStatistics();
			Vector3 globalFocus = GlobalFocus();
			GD.Print($"[atlas-review] {sourceDescription} " +
			         $"window {data.Width}x{data.Depth} origin {data.OriginX},{data.OriginZ} " +
			         $"focus {globalFocus.X:0},{globalFocus.Z:0} chunks {_streamer.LoadedCount}");
			GD.Print($"[atlas-review] land {stats.LandCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
			         $"water {stats.WaterCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
			         $"height {stats.MinHeight}..{stats.MaxHeight} water {stats.MinWaterSurface}..{stats.MaxWaterSurface}");
			if (blockout is DomainBlockoutStatistics b)
				GD.Print($"[domain-blockout] {b.Platforms} platforms/{b.PlatformCells} cells  " +
				         $"caps {b.TerrainCapCells} terrain/{b.PavedCapCells} paved/" +
				         $"{b.ReclaimedCapCells} reclaimed  " +
				         $"cutouts {b.Cutouts}/{b.CutoutCells} cells  " +
				         $"{b.Stairs} stairs/{b.StairCells} cells  routes {b.RouteCells} cells  " +
				         $"walls {b.Walls} landmarks {b.Landmarks} placed {b.PlacedBlocks} blocks");
			if (dressing is AtlasDomainDressingStatistics d)
				GD.Print($"[atlas-dressing] {d.Trees} trees from {d.Candidates} globally anchored candidates");
			_started = true;

			if (_shotDirectory != null) await RunCapture();
			else GD.Print("[atlas-play] WASD walk  Space jump  Q/E orbit  mouse wheel zoom");
		}
		catch (Exception ex)
		{
			GD.PushError($"[atlas-review] {ex.Message}");
			GetTree().Quit(2);
		}
	}

	private static (int minX, int minZ, int maxX, int maxZ) SectorBounds(
		CanonicalDomain domain, WorldAtlasDefinition atlas)
	{
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		int minX = columns - 1, minZ = rows - 1, maxX = 0, maxZ = 0;
		foreach (BlockPoint point in domain.Boundary)
		{
			int sx = Math.Clamp(point.X / atlas.SectorSize, 0, columns - 1);
			int sz = Math.Clamp(point.Z / atlas.SectorSize, 0, rows - 1);
			minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
			minZ = Math.Min(minZ, sz); maxZ = Math.Max(maxZ, sz);
		}
		return (minX, minZ, maxX, maxZ);
	}

	private AtlasSectorData LoadOrRebuild(AtlasSectorCompiler compiler, string packagePath,
		int sectorX, int sectorZ)
	{
		string artifactPath = $"{packagePath}/derived/sector-{sectorX}-{sectorZ}.pfs";
		try
		{
			AtlasSectorData loaded = compiler.ReadArtifact(artifactPath);
			GD.Print($"[atlas-review] loaded {ProjectSettings.GlobalizePath(artifactPath)}");
			return loaded;
		}
		catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or EndOfStreamException)
		{
			GD.Print($"[atlas-review] rebuilding derived artifact: {ex.Message}");
			AtlasSectorData rebuilt = compiler.Compile(sectorX, sectorZ);
			compiler.WriteArtifact(rebuilt, artifactPath);
			return compiler.ReadArtifact(artifactPath);
		}
	}

	private Vector3 GlobalFocus() => _focusLocal + _content.Position;

	private void PlaceInteractiveCamera()
	{
		if (_camera == null) return;
		var shot = new Capture.Shot("interactive", _interactiveDistance, _interactiveYaw,
			_interactivePitch);
		Capture.Place(_camera, shot, GlobalFocus());
	}

	private async System.Threading.Tasks.Task RunCapture()
	{
		for (int i = 0; i < 12; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		foreach (Capture.Shot shot in ReviewShots)
		{
			if (_only != null && !_only.Contains(shot.Name)) continue;
			if (shot.Time >= 0f && _day != null)
			{
				_day.TimeOfDay = shot.Time;
				_day.Paused = true;
			}
			if (_key != null)
			{
				// The playable camera needs only the normal 260-block cascade. A
				// thousand-block atlas composition shot otherwise sits completely
				// outside the shadow map and cannot judge the reference silhouette.
				_key.DirectionalShadowMaxDistance = Math.Clamp(shot.Distance * 1.18f,
					260f, IsDomain ? 1200f : 420f);
			}
			if (_environment != null)
			{
				_environment.FogDepthBegin = Math.Max(160f, shot.Distance * .40f);
				_environment.FogDepthEnd = Math.Max(720f, shot.Distance * 2.4f);
				if (IsDomain && _day != null)
				{
					if (shot.Time >= 0.78f)
					{
						// 0.83 already carries lavender key/fog. Far night was
						// crushed to mean ~68 by the 900-unit fog start; push the
						// fog back so the massif still reads, and ease darkness
						// on the far cameras only. Near night used the 280-unit
						// far fog start, so the 124 sat in full darkness as a
						// second precinct (reference-5 is a hazy shrine).
						bool shrineNear = shot.Name.Contains("near", StringComparison.Ordinal);
						_day.SetNightDarkness(shot.Distance >= 800f ? 0.22f
							: shrineNear ? 0.28f : 0.34f);
						_day.TintReviewTwilight();
						_environment.AmbientLightEnergy = shrineNear ? 1.20f : 1.16f;
						_environment.FogLightEnergy = shrineNear ? 0.96f : 0.98f;
						if (_fill != null)
							_fill.LightEnergy = shrineNear ? 0.28f : 0.22f;
						if (shrineNear)
						{
							// Camera 118. Begin 122 sat on the shrine (ink-only
							// precinct). End 162 at begin 130 did the same to the
							// 124. Begin behind the dais; pitch (not a hard fog
							// wall) is what puts 124 onto the ramp.
							_environment.FogDepthBegin = 140f;
							_environment.FogDepthEnd = 210f;
						}
						else
						{
							// 0.68×distance put the look-at (the cliff) in the
							// fog ramp. Reference-5 keeps the gate solid.
							_environment.FogDepthBegin = shot.Distance * 1.12f;
							_environment.FogDepthEnd = Math.Max(1100f, shot.Distance * 3.2f);
						}
					}
					else
					{
						_day.SetNightDarkness(1.45f);
						// Time 0.36 keeps the long shadow angle; the peach
						// horizon at that key is what washed the massif pink.
						bool shrineNear = shot.Name.Contains("near", StringComparison.Ordinal);
						if (shrineNear)
							_day.TintReviewShrine();
						else
							_day.TintReviewMorning();
						_environment.AmbientLightEnergy = shrineNear ? 0.94f : 0.82f;
						_environment.FogLightEnergy = shrineNear ? 0.92f : 0.90f;
						if (_fill != null)
							_fill.LightEnergy = shrineNear ? 0.14f : 0.09f;
						if (shrineNear)
						{
							// Camera 118. Begin 122 sat on the shrine (ink-only).
							// End 162 at begin 130 inked the 124. Keep the dais
							// clear; 124 sits on the ramp once pitch is 42.
							_environment.FogDepthBegin = 140f;
							_environment.FogDepthEnd = 210f;
						}
						else
						{
							// 0.52×distance fogged the look-at. 1.12× kept the
							// look-at solid when it was the only subject, but a
							// district look-at leaves the inland gate ~150
							// behind (reference-1/2 keep both cliff and gate
							// solid). 1.40× holds that gate; the look-at at
							// 1.0× stays well before the ramp.
							_environment.FogDepthBegin = shot.Distance * 1.40f;
							_environment.FogDepthEnd = Math.Max(900f, shot.Distance * 2.6f);
						}
					}
				}
			}
			if (IsDomain)
			{
				// Play keeps the 21° long lens. Overview captures need a slightly
				// wider frame: drowned water sits 240 blocks south-east of the gate,
				// and 21° at 500 units cannot hold both.
				_camera.Fov = shot.Name.Contains("near", StringComparison.Ordinal) ? 26f : 30f;
			}
			Vector3 shotFocus = CaptureFocus(shot);
			Capture.Place(_camera, shot, shotFocus);
			for (int i = 0; i < 24; i++)
			{
				Capture.Place(_camera, shot, shotFocus);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton,
				RenderingServer.SignalName.FramePostDraw);
			Capture.Save(GetViewport(), _shotDirectory, shot.Name);
		}
		GetTree().Quit();
	}

	private Vector3 CaptureFocus(Capture.Shot shot)
	{
		if (!IsDomain) return GlobalFocus();
		// Near owns refs 6/7/8/10. Aiming at the 124 face (plan z≈28, x≈-16)
		// made the walking-distance frame a fortress; those references are the
		// circular shrine on the approach dais, with the gate as the cliff behind.
		int localX = 0;
		int localNorth = -6;
		if (shot.Name.Contains("far", StringComparison.Ordinal))
		{
			// Same drowned-spur look-at as wide, a little further south so
			// 700u still holds the waterfront gate on the ray.
			localX = -122;
			localNorth = -180;
		}
		else if (shot.Name.Contains("reverse", StringComparison.Ordinal))
		{
			localX = -20;
			localNorth = 70;
		}
		else if (shot.Name.Contains("wide", StringComparison.Ordinal))
		{
			// Drowned spur into the waterfront cleft (reference-1/2). The SW
			// corner (−70,−72) showed two monuments; the bank centre
			// (−110,−155) made the 124 the subject; (−38,−154) fogged the
			// inland gate and kept the pool; (−28,24) was a curtain wall.
			localX = -122;
			localNorth = -168;
		}
		BlockPoint point = _domain.Plan.ToGlobal(new PlanPoint { X = localX, Z = localNorth });
		Vector3 local = _window.FocusAtGlobal(point.X, point.Z, StreamRadius);
		return local + _content.Position;
	}

	private void SpawnTraveller()
	{
		int globalX;
		int globalZ;
		if (_requestedFocus is Vector2I requested)
		{
			globalX = requested.X;
			globalZ = requested.Y;
		}
		else if (IsDomain)
		{
			globalX = _domain.Plan.Origin.X;
			globalZ = _domain.Plan.Origin.Z;
		}
		else
		{
			globalX = (int)(_focusLocal.X + _window.Data.OriginX);
			globalZ = (int)(_focusLocal.Z + _window.Data.OriginZ);
		}
		Vector3 spawn = _window.FindLandSpawn(globalX, globalZ, StreamRadius);
		_player = new Controller { Name = "Player", Position = spawn };
		_content.AddChild(_player);
		_player.Setup(_window.WaterAt);
		_player.ResetPhysicsInterpolation();
		_character = new Character { Name = "Traveller" };
		_player.AddChild(_character);
		_character.Setup(_inkLight, _inkDark);
		GD.Print($"[atlas-play] spawn {spawn.X + _window.Data.OriginX:0},{spawn.Z + _window.Data.OriginZ:0} " +
		         $"y {spawn.Y:0.0}");
	}

	public override void _Process(double delta)
	{
		if (!_started || !_playable || _player == null || _camera == null) return;
		_streamer.UpdateAround(_player.Position);
		Vector3 p = _player.GetGlobalTransformInterpolated().Origin;
		_camera.Follow(p, _player.Velocity, delta);
		_character?.Animate(_player.Velocity, _player.Facing,
			_player.IsOnFloor(), _player.Swimming, _player.Sitting, delta);
	}

	public override void _UnhandledInput(InputEvent input)
	{
		if (!_started || _shotDirectory != null || !_playable || _camera == null) return;
		if (input is InputEventMouseButton mouse && mouse.Pressed)
		{
			if (mouse.ButtonIndex == MouseButton.WheelUp) _camera.Zoom(-8f);
			else if (mouse.ButtonIndex == MouseButton.WheelDown) _camera.Zoom(8f);
			else return;
			GetViewport().SetInputAsHandled();
		}
		else if (input is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Q) _camera.Rotate45(-1);
			else if (key.Keycode == Key.E) _camera.Rotate45(1);
			else return;
			GetViewport().SetInputAsHandled();
		}
	}
}
