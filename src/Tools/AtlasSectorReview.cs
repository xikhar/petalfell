using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Petalfell.Core;
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
	private const int DomainInteractiveStreamRadius = 14;
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
		new("domain_near", 240f, 45f, 31f, time: 0.36f),
		new("domain_wide", 440f, 45f, 38f, time: 0.36f),
		new("domain_reverse", 440f, 225f, 36f, time: 0.36f),
		new("domain_far", 1000f, 45f, 46f, time: 0.36f),
		new("domain_night_near", 240f, 45f, 31f, time: 0.98f),
		new("domain_night_wide", 440f, 45f, 38f, time: 0.98f),
		new("domain_night_reverse", 440f, 225f, 36f, time: 0.98f),
		new("domain_night_far", 1000f, 45f, 46f, time: 0.98f),
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
	private Godot.Environment _environment;
	private Vector3 _focusLocal;
	private Node3D _content;
	private float _interactiveYaw = 45f;
	private float _interactivePitch = 34f;
	private float _interactiveDistance = 150f;
	private bool _started;
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
		var args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--review-sector" && i + 1 < args.Length) sector = args[++i];
			else if (args[i].StartsWith("--review-sector=")) sector = args[i][16..];
			else if (args[i] == "--review-domain" && i + 1 < args.Length) domain = args[++i];
			else if (args[i].StartsWith("--review-domain=")) domain = args[i][16..];
			else if (args[i] == "--review-focus" && i + 1 < args.Length) focus = args[++i];
			else if (args[i].StartsWith("--review-focus=")) focus = args[i][15..];
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}
		if (sector == null && domain == null) return false;
		if (sector != null && domain != null)
			throw new InvalidOperationException("choose either --review-sector or --review-domain, not both");

		int sectorX = 0, sectorZ = 0;
		if (sector != null) (sectorX, sectorZ) = ParsePair(sector, "sector address");
		Vector2I? requestedFocus = null;
		if (focus != null)
		{
			(int x, int z) = ParsePair(focus, "review focus");
			requestedFocus = new Vector2I(x, z);
		}
		(string shotDirectory, HashSet<string> only) = Capture.ParseArgs();
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
				: null;
			_content = new Node3D { Name = "AtlasWindow", Position = _window.GlobalOrigin };
			AddChild(_content);

			// The legacy world has one water plane and can hide submerged ink by a
			// uniform height. An atlas window has many surface heights; drawing its
			// same ink passes before water lets the real geometry occlude every bed.
			var ink = WorldMaterials.CreateInk(data.SeaLevel, priorityOffset: -6);
			_streamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
			_content.AddChild(_streamer);
			_streamer.Setup(_window.Grid, WorldMaterials.CreateVoxel(data.SeaLevel),
				ink.Light, ink.Dark, buildCollision: false);

			ShaderMaterial waterMaterial = WorldMaterials.CreateWater(data.SeaLevel,
				surfaceFromMesh: true, reflectionAvailable: false);
			var water = _window.BuildWater(waterMaterial);
			if (water != null) _content.AddChild(water);

			AddChild(Atmosphere.Build());
			_environment = Atmosphere.LastEnvironment;
			_key = Atmosphere.Sun();
			DirectionalLight3D fill = Atmosphere.Fill();
			AddChild(_key);
			AddChild(fill);
			_day = new DayCycle { Name = "DayCycle", Paused = _shotDirectory != null };
			AddChild(_day);
			_day.Setup(_environment, _key, fill, Atmosphere.LastSky,
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
			_streamer.UpdateAround(_focusLocal, prime: true);

			_camera = new CameraRig { Name = "AtlasCamera", Current = true };
			AddChild(_camera);
			if (IsDomain)
			{
				_camera.Far = 1600f;
				_interactiveDistance = 300f;
			}
			PlaceInteractiveCamera();
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
			else GD.Print("[atlas-review] W/A/S/D pan  Q/E orbit  mouse wheel zoom");
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
				// Preserve the authored density/curve but move its far plane with a
				// deliberate atlas overview. Leaving the 580-block play end here made
				// every 1,000-block composition test a flat fog-colour swatch.
				_environment.FogDepthBegin = Math.Max(130f, shot.Distance * .24f);
				_environment.FogDepthEnd = Math.Max(580f, shot.Distance * 1.55f);
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
		// The plan origin is the lower/upper court transition. Long-lens domain
		// captures look slightly north of it so the main stair, wall and hero arch
		// share the frame instead of spending half the image on empty forecourt.
		int localNorth = shot.Name.Contains("near", StringComparison.Ordinal) ? 112
			: shot.Name.Contains("wide", StringComparison.Ordinal) ||
			  shot.Name.Contains("reverse", StringComparison.Ordinal) ? 76 : 32;
		BlockPoint point = _domain.Plan.ToGlobal(new PlanPoint { X = 0, Z = localNorth });
		Vector3 local = _window.FocusAtGlobal(point.X, point.Z, StreamRadius);
		return local + _content.Position;
	}

	public override void _UnhandledInput(InputEvent input)
	{
		if (!_started || _shotDirectory != null) return;
		bool changed = false;
		bool moved = false;
		if (input is InputEventMouseButton mouse && mouse.Pressed)
		{
			if (mouse.ButtonIndex == MouseButton.WheelUp)
			{
				_interactiveDistance = Math.Max(60f, _interactiveDistance - 18f);
				changed = true;
			}
			else if (mouse.ButtonIndex == MouseButton.WheelDown)
			{
				_interactiveDistance = Math.Min(IsDomain ? 700f : 360f, _interactiveDistance + 18f);
				changed = true;
			}
		}
		else if (input is InputEventKey key && key.Pressed && !key.Echo)
		{
			const float step = 48f;
			switch (key.Keycode)
			{
				case Key.W: _focusLocal.Z -= step; moved = true; break;
				case Key.S: _focusLocal.Z += step; moved = true; break;
				case Key.A: _focusLocal.X -= step; moved = true; break;
				case Key.D: _focusLocal.X += step; moved = true; break;
				case Key.Q: _interactiveYaw -= 45f; changed = true; break;
				case Key.E: _interactiveYaw += 45f; changed = true; break;
			}
		}
		if (moved)
		{
			int margin = StreamRadius * ChunkMesher.ChunkSize;
			_focusLocal.X = Math.Clamp(_focusLocal.X, margin, _window.Data.Width - margin - 1);
			_focusLocal.Z = Math.Clamp(_focusLocal.Z, margin, _window.Data.Depth - margin - 1);
			int x = (int)_focusLocal.X, z = (int)_focusLocal.Z;
			_focusLocal.Y = _window.Grid.HeightAt(x, z) + 1.2f;
			_streamer.UpdateAround(_focusLocal, prime: true);
			changed = true;
		}
		if (changed)
		{
			PlaceInteractiveCamera();
			GetViewport().SetInputAsHandled();
		}
	}
}
