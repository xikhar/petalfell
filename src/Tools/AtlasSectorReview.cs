using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Petalfell.Core;
using Petalfell.Player;
using Petalfell.Render;
using Petalfell.World;
using Petalfell.World.Sites;

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
	private const int SiteInteractiveStreamRadius = 8;
	private const int SiteCaptureStreamRadius = 14;
	private const string Reference10SiteId = "bloom-grove-court";
	private const string Reference10TopPath = "res://world-new/reference-10-top.png";
	private const string ReferenceTopShotName = "reference_top_day";
	private static readonly Vector2I Reference10TopSize = new(1254, 1254);
	private const float Reference10TopPixelsPerVoxel = 10f;
	// The overhead source puts site-local 0,0 at pixel 555,646. Its square image
	// centre therefore looks at local 7.2,1.9; preserving that offset lets a grid
	// overlay compare authored cells without translating either image by eye.
	private static readonly Vector2 Reference10TopLocalCentre = new(7.2f, 1.9f);
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
		// Early full night keeps the moon oblique enough to model the terraces.
		// Midnight put the key almost overhead and reduced the district to dark
		// blue albedo separated only by ink, despite using the ordinary day cycle.
		new("domain_night_near", 240f, 45f, 31f, time: 0.90f),
		new("domain_night_wide", 440f, 45f, 38f, time: 0.90f),
		new("domain_night_reverse", 440f, 225f, 36f, time: 0.90f),
		new("domain_night_far", 1000f, 45f, 46f, time: 0.90f),
	};

	private string _mapPath;
	private int _sectorX;
	private int _sectorZ;
	private string _domainId;
	private CanonicalDomain _domain;
	private string _siteId;
	private CanonicalSite _site;
	private ReferenceSiteDefinition _referenceSite;
	private bool _playable;
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
	private Controller _player;
	private Character _character;
	private ShaderMaterial _inkLight;
	private ShaderMaterial _inkDark;
	private float _interactiveYaw = 45f;
	private float _interactivePitch = 34f;
	private float _interactiveDistance = 150f;
	private bool _started;
	private bool IsDomain => _domainId != null;
	private bool IsSite => _siteId != null;
	private bool IsAuthoredWindow => IsDomain || IsSite;
	private int StreamRadius => IsSite
		? (_shotDirectory == null ? SiteInteractiveStreamRadius : SiteCaptureStreamRadius)
		: IsDomain
			? (_shotDirectory == null ? DomainInteractiveStreamRadius : DomainCaptureStreamRadius)
			: (_shotDirectory == null ? InteractiveStreamRadius : CaptureStreamRadius);
	private Capture.Shot[] ReviewShots
	{
		get
		{
			if (IsSite)
			{
				PlanReferenceView siteView = _referenceSite?.ReferenceView;
				if (siteView == null) return SectorShots;
				var siteShots = new List<Capture.Shot>
				{
					new("reference_match_day", siteView.Distance, siteView.YawDegrees,
						// Reference 10 is high, neutral late-morning light. The old 0.36 key
						// left its pale stone in saturated violet dawn shadow.
						siteView.PitchDegrees, time: 0.41f),
					new("reference_match_night", siteView.Distance, siteView.YawDegrees,
						// The 0.76 dusk and 0.83 night keys meet here: the sun has set, the
						// moon models the terraces, and the pastel materials still separate.
						// Later night samples reduced this pale court to navy silhouettes.
						siteView.PitchDegrees, time: 0.80f),
				};
				if (_referenceSite.SiteId == Reference10SiteId)
				{
					// This separate orthographic shot owns footprint review. The accepted
					// isometric reference_match_day values above remain the silhouette lock.
					siteShots.Add(new Capture.Shot(ReferenceTopShotName, 160f, 180f, 89f,
						time: 0.41f));
				}
				// Every acceptance pass uses one centre at four useful scales and all
				// four cardinal rotations. A hero view alone can conceal a hollow rear
				// facade or a composition that only works from one carefully chosen zoom.
				(string name, float distance)[] scales =
				{
					("close", 62f),
					("play", 96f),
					("wide", 154f),
					("far", 240f),
				};
				foreach ((string name, float distance) in scales)
				for (int quarter = 0; quarter < 4; quarter++)
					siteShots.Add(new Capture.Shot($"site_{name}_r{quarter}", distance,
						siteView.YawDegrees + quarter * 90f, siteView.PitchDegrees,
						time: 0.36f));
				return siteShots.ToArray();
			}
			if (!IsDomain) return SectorShots;
			PlanReferenceView view = _domain?.Plan?.ReferenceView;
			if (_domain?.Plan?.SourceMode != PlanSourceMode.ReferenceReconstruction || view == null)
				return DomainShots;
			var shots = new List<Capture.Shot>
			{
				new("reference_match_day", view.Distance, view.YawDegrees,
					view.PitchDegrees, time: 0.36f),
				new("reference_match_night", view.Distance, view.YawDegrees,
					view.PitchDegrees, time: 0.90f),
			};
			shots.AddRange(DomainShots);
			return shots.ToArray();
		}
	}

	public static bool TryRun(Node owner, string defaultMapPath, string defaultSiteId = null)
	{
		string sector = null;
		string domain = null;
		string site = null;
		string focus = null;
		string mapPath = defaultMapPath;
		bool legacyWorld = false;
		var args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--review-sector" && i + 1 < args.Length) sector = args[++i];
			else if (args[i].StartsWith("--review-sector=")) sector = args[i][16..];
			else if (args[i] == "--review-domain" && i + 1 < args.Length) domain = args[++i];
			else if (args[i].StartsWith("--review-domain=")) domain = args[i][16..];
			else if (args[i] == "--review-site" && i + 1 < args.Length) site = args[++i];
			else if (args[i].StartsWith("--review-site=")) site = args[i][14..];
			else if (args[i] == "--legacy-world") legacyWorld = true;
			else if (args[i] == "--review-focus" && i + 1 < args.Length) focus = args[++i];
			else if (args[i].StartsWith("--review-focus=")) focus = args[i][15..];
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}
		if (legacyWorld) return false;
		bool playable = sector == null && domain == null && site == null && defaultSiteId != null;
		if (playable) site = defaultSiteId;
		int selections = (sector == null ? 0 : 1) + (domain == null ? 0 : 1) + (site == null ? 0 : 1);
		if (selections == 0) return false;
		if (selections != 1)
			throw new InvalidOperationException("choose one of --review-sector, --review-domain or --review-site");

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
			Name = site != null ? (playable ? "ProductionAtlasRuntime" : "AtlasSiteReview")
				: domain == null ? "AtlasSectorReview" : "AtlasDomainReview",
			_mapPath = mapPath,
			_sectorX = sectorX,
			_sectorZ = sectorZ,
			_domainId = domain,
			_siteId = site,
			_playable = playable,
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
			else if (IsSite)
			{
				if (map.CanonicalAtlas.Topology == null)
					throw new InvalidOperationException("production atlas has no registered authored topology");
				_site = map.CanonicalAtlas.Topology.Sites.Find(s => s.Id == _siteId) ??
					throw new InvalidOperationException($"canonical site '{_siteId}' does not exist");
				_referenceSite = _site.ReferencePlan ??
					throw new InvalidOperationException($"canonical site '{_siteId}' has no loaded reference blueprint");
				(int minX, int minZ, int maxX, int maxZ) = SiteSectorBounds(
					_referenceSite, map.CanonicalAtlas);
				data = AtlasSectorMosaic.Compose(map.CanonicalAtlas, minX, minZ, maxX, maxZ,
					(sx, sz) => LoadOrRebuild(compiler, packagePath, sx, sz));
				sourceDescription = $"site {_siteId} sectors {minX},{minZ}..{maxX},{maxZ}";
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
			ReferenceSiteStatistics? siteBuild = IsSite
				? ReferenceSiteBuilder.Build(_window, _referenceSite)
				: null;
			AtlasDomainDressingStatistics? dressing = IsDomain
				? AtlasDomainDressing.Apply(_window, map.CanonicalAtlas,
					_domain.Plan, map.DefaultSeed)
				: IsSite
					? AtlasDomainDressing.Apply(_window, map.CanonicalAtlas,
						_referenceSite, map.DefaultSeed)
					: null;
			_content = new Node3D { Name = "AtlasWindow", Position = _window.GlobalOrigin };
			AddChild(_content);

			// The legacy world has one water plane and can hide submerged ink by a
			// uniform height. An atlas window has many surface heights; drawing its
			// same ink passes before water lets the real geometry occlude every bed.
			var ink = WorldMaterials.CreateInk(data.SeaLevel, priorityOffset: -6);
			_inkLight = ink.Light;
			_inkDark = ink.Dark;
			_streamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
			_content.AddChild(_streamer);
			GroundDetail.Seed = map.DefaultSeed;
			_streamer.Setup(_window, WorldMaterials.CreateVoxel(data.SeaLevel),
				ink.Light, ink.Dark, WorldMaterials.CreateDetail(),
				WorldMaterials.CreateWaterDetail(), buildCollision: _playable);

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
				: IsSite
					? new Vector2I(_referenceSite.Origin.X, _referenceSite.Origin.Z)
					: null;
			_focusLocal = _requestedFocus is Vector2I focus
				? _window.FocusAtGlobal(focus.X, focus.Y, StreamRadius)
				: canonicalFocus is Vector2I authored
					? _window.FocusAtGlobal(authored.X, authored.Y, StreamRadius)
					: _window.FindReviewFocus();
			// The information-rich point may sit near a sector edge. A one-sector
			// artifact only owns one apron there, so clamp the review anchor far
			// enough inside the local window that every requested chunk is real.
			if (_requestedFocus == null && !IsAuthoredWindow)
				_focusLocal = _window.FocusAtGlobal(
					(int)(_focusLocal.X + _window.Data.OriginX),
					(int)(_focusLocal.Z + _window.Data.OriginZ), StreamRadius);
			_streamer.UpdateAround(_focusLocal, prime: true);
			if (IsSite) BuildSiteTraveller();

			_camera = new CameraRig { Name = "AtlasCamera", Current = true };
			AddChild(_camera);
			if (IsDomain)
			{
				_camera.Far = 1600f;
				_interactiveDistance = 300f;
			}
			else if (IsSite)
			{
				_camera.Far = 800f;
				_interactiveYaw = _referenceSite.ReferenceView.YawDegrees;
				_interactivePitch = _referenceSite.ReferenceView.PitchDegrees;
				_interactiveDistance = _referenceSite.ReferenceView.Distance;
			}
			if (_playable && _player != null)
			{
				_camera.SetZoomLimits(50f, 180f);
				_camera.TargetDistance = 75f;
				_camera.Distance = 75f;
				_camera.Follow(_player.GlobalPosition, Vector3.Zero, 1.0);
			}
			else PlaceInteractiveCamera();
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
					         $"surface {b.SurfacePatches}/{b.SurfacePatchCells} cells/" +
					         $"{b.RubbleClusters} rubble clusters  " +
					         $"{b.Stairs} stairs/{b.StairCells} cells  routes {b.RouteCells} cells  " +
				         $"walls {b.Walls} landmarks {b.Landmarks} placed {b.PlacedBlocks} blocks");
			if (dressing is AtlasDomainDressingStatistics d)
				GD.Print($"[atlas-dressing] {d.Trees} trees from {d.Candidates} globally anchored candidates");
			if (siteBuild is ReferenceSiteStatistics s)
				GD.Print($"[reference-site] {_siteId} explicit surface {s.SurfaceCells} cells, " +
				         $"{s.Voxels} voxel writes, source {_referenceSite.ReferencePath}");
			_started = true;

			if (_shotDirectory != null) await RunCapture();
			else if (_playable)
				GD.Print("[atlas-runtime] W/A/S/D move  Space jump  Q/E orbit  mouse wheel zoom  --legacy-world restores the retired fixture");
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

	private static (int minX, int minZ, int maxX, int maxZ) SiteSectorBounds(
		ReferenceSiteDefinition site, WorldAtlasDefinition atlas)
	{
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		PlanPoint[] corners =
		{
			new() { X = site.FootprintMin.X, Z = site.FootprintMin.Z },
			new() { X = site.FootprintMax.X, Z = site.FootprintMin.Z },
			new() { X = site.FootprintMin.X, Z = site.FootprintMax.Z },
			new() { X = site.FootprintMax.X, Z = site.FootprintMax.Z },
		};
		int minX = columns - 1, minZ = rows - 1, maxX = 0, maxZ = 0;
		foreach (PlanPoint corner in corners)
		{
			BlockPoint point = site.ToGlobal(corner);
			int sx = Math.Clamp(point.X / atlas.SectorSize, 0, columns - 1);
			int sz = Math.Clamp(point.Z / atlas.SectorSize, 0, rows - 1);
			minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
			minZ = Math.Min(minZ, sz); maxZ = Math.Max(maxZ, sz);
		}
		// AtlasSectorWindow deliberately stays square. Add real neighbouring sectors
		// on the shorter axis; fabricated padding would defeat seam verification.
		while (maxX - minX < maxZ - minZ)
		{
			if (maxX + 1 < columns) maxX++;
			else minX--;
		}
		while (maxZ - minZ < maxX - minX)
		{
			if (maxZ + 1 < rows) maxZ++;
			else minZ--;
		}
		return (minX, minZ, maxX, maxZ);
	}

	private void BuildSiteTraveller()
	{
		BlockPoint global = _referenceSite.ToGlobal(_referenceSite.PlayerSpawn);
		int x = global.X - _window.Data.OriginX;
		int z = global.Z - _window.Data.OriginZ;
		var spawn = new Vector3(x + .5f, _window.Grid.HeightAt(x, z) + .2f, z + .5f);
		_player = new Controller
		{
			Name = _playable ? "Player" : "ScaleTraveller",
			Position = spawn,
			InputEnabled = _playable,
		};
		_content.AddChild(_player);
		_player.Setup(null);
		_player.ResetPhysicsInterpolation();
		if (!_playable) _player.SetPhysicsProcess(false);
		_character = new Character { Name = "Traveller" };
		_player.AddChild(_character);
		_character.Setup(_inkLight, _inkDark);
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
		Vector2I captureSize = IsSite && _referenceSite?.ReferenceView != null
			? new Vector2I(_referenceSite.ReferenceView.SourceWidth,
				_referenceSite.ReferenceView.SourceHeight)
			: new Vector2I(1600, 900);
		var captureViewport = new SubViewport
		{
			Name = "DeterministicCaptureViewport",
			Size = captureSize,
			World3D = GetViewport().World3D,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			Msaa3D = Viewport.Msaa.Msaa4X,
		};
		var captureCamera = new Camera3D
		{
			Name = "DeterministicCaptureCamera",
			Current = true,
			Projection = _camera.Projection,
			Fov = _camera.Fov,
			Near = _camera.Near,
			Far = _camera.Far,
			KeepAspect = _camera.KeepAspect,
			CullMask = _camera.CullMask,
		};
		captureViewport.AddChild(captureCamera);
		captureViewport.AddChild(WorldMaterials.CreateGrade());
		AddChild(captureViewport);
		for (int i = 0; i < 12; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		foreach (Capture.Shot shot in ReviewShots)
		{
			if (_only != null && !_only.Contains(shot.Name)) continue;
			bool referenceTop = IsReferenceTopShot(shot);
			captureViewport.Size = referenceTop ? Reference10TopSize : captureSize;
			captureCamera.Projection = referenceTop
				? Camera3D.ProjectionType.Orthogonal
				: _camera.Projection;
			if (referenceTop)
				captureCamera.Size = Reference10TopSize.Y / Reference10TopPixelsPerVoxel;
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
				_environment.FogDepthBegin = Math.Max(180f, shot.Distance * .40f);
				_environment.FogDepthEnd = Math.Max(700f, shot.Distance * 2.00f);
			}
			Vector3 shotFocus = CaptureFocus(shot);
			PlaceReviewCamera(_camera, shot, shotFocus, referenceTop);
			for (int i = 0; i < 24; i++)
			{
				PlaceReviewCamera(_camera, shot, shotFocus, referenceTop);
				captureCamera.GlobalTransform = _camera.GlobalTransform;
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton,
				RenderingServer.SignalName.FramePostDraw);
			Capture.Save(captureViewport, _shotDirectory, shot.Name);
		}
		WriteReferenceComparisons();
		GetTree().Quit();
	}

	private Vector3 CaptureFocus(Capture.Shot shot)
	{
		if (IsSite)
		{
			if (IsReferenceTopShot(shot)) return Reference10TopFocus();
			BlockPoint sitePoint = _referenceSite.ToGlobal(_referenceSite.ReferenceView.Focus);
			Vector3 siteLocal = _window.FocusAtGlobal(sitePoint.X, sitePoint.Z, StreamRadius);
			siteLocal.Y += _referenceSite.ReferenceView.HeightOffset;
			return siteLocal + _content.Position;
		}
		if (!IsDomain) return GlobalFocus();
		if (shot.Name.StartsWith("reference_match", StringComparison.Ordinal) &&
		    _domain.Plan.ReferenceView != null)
		{
			BlockPoint referencePoint = _domain.Plan.ToGlobal(_domain.Plan.ReferenceView.Focus);
			Vector3 referenceLocal = _window.FocusAtGlobal(referencePoint.X,
				referencePoint.Z, StreamRadius);
			referenceLocal.Y += _domain.Plan.ReferenceView.HeightOffset;
			return referenceLocal + _content.Position;
		}
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

	private bool IsReferenceTopShot(Capture.Shot shot) =>
		IsSite && _referenceSite?.SiteId == Reference10SiteId &&
		shot.Name == ReferenceTopShotName;

	private Vector3 Reference10TopFocus()
	{
		float radians = _referenceSite.AxisDegrees * MathF.PI / 180f;
		float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
		// The builder's source-facing coordinate contract reflects plan X before
		// applying the site's atlas rotation. Carry the same reflection into the
		// camera calibration so source-local +X remains screen-right.
		float planX = -Reference10TopLocalCentre.X;
		float planZ = Reference10TopLocalCentre.Y;
		float globalX = _referenceSite.Origin.X + planX * cos + planZ * sin;
		float globalZ = _referenceSite.Origin.Z - planX * sin + planZ * cos;
		int cellX = (int)MathF.Round(globalX);
		int cellZ = (int)MathF.Round(globalZ);
		Vector3 local = _window.FocusAtGlobal(cellX, cellZ, StreamRadius);
		local.X += globalX - cellX;
		local.Z += globalZ - cellZ;
		return local + _content.Position;
	}

	private static void PlaceReviewCamera(CameraRig camera, Capture.Shot shot,
		Vector3 focus, bool referenceTop)
	{
		if (!referenceTop)
		{
			Capture.Place(camera, shot, focus);
			return;
		}
		// A true vertical transform avoids the small height-dependent footprint
		// drift caused by an 89-degree perspective approximation. World -X is
		// screen-right because the source plan's X axis is mirrored at runtime.
		camera.GlobalPosition = focus + Vector3.Up * shot.Distance;
		camera.LookAt(focus, Vector3.Back);
	}

	private void WriteReferenceComparisons()
	{
		bool domainReference = IsDomain &&
			_domain.Plan.SourceMode == PlanSourceMode.ReferenceReconstruction &&
			_domain.Plan.ReferenceView != null;
		if (!IsSite && !domainReference) return;
		if (_only == null || _only.Contains("reference_match_day"))
		{
			string referencePath = IsSite
				? _referenceSite.ReferencePath
				: _domain.Plan.ReconstructionReferencePath;
			WriteImageComparison($"{_shotDirectory}/reference_match_day.png",
				referencePath, "reference", "isometric");
		}
		if (IsSite && _referenceSite.SiteId == Reference10SiteId &&
		    (_only == null || _only.Contains(ReferenceTopShotName)))
			WriteImageComparison($"{_shotDirectory}/{ReferenceTopShotName}.png",
				Reference10TopPath, "reference_top", "overhead");
	}

	private void WriteImageComparison(string capturePath, string referencePath,
		string outputStem, string label)
	{
		if (!Godot.FileAccess.FileExists(capturePath) ||
		    !Godot.FileAccess.FileExists(referencePath)) return;

		Image captured = Image.LoadFromFile(capturePath);
		Texture2D referenceTexture = ResourceLoader.Load<Texture2D>(referencePath);
		Image reference = referenceTexture?.GetImage();
		if (captured == null || reference == null || captured.IsEmpty() || reference.IsEmpty()) return;
		captured.Convert(Image.Format.Rgba8);
		reference.Convert(Image.Format.Rgba8);
		reference.Resize(captured.GetWidth(), captured.GetHeight(), Image.Interpolation.Lanczos);
		int width = captured.GetWidth(), height = captured.GetHeight();
		Image overlay = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		Image edgeDifference = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
		double squaredColourError = 0d;
		double summedEdgeDifference = 0d;
		for (int y = 0; y < height; y++)
		for (int x = 0; x < width; x++)
		{
			Color source = reference.GetPixel(x, y);
			Color render = captured.GetPixel(x, y);
			overlay.SetPixel(x, y, source.Lerp(render, .5f));
			double red = source.R - render.R;
			double green = source.G - render.G;
			double blue = source.B - render.B;
			squaredColourError += red * red + green * green + blue * blue;
			if (x == 0 || y == 0 || x == width - 1 || y == height - 1) continue;
			float sourceEdge = EdgeMagnitude(reference, x, y);
			float renderEdge = EdgeMagnitude(captured, x, y);
			float difference = Math.Clamp(MathF.Abs(sourceEdge - renderEdge) * 3.5f, 0f, 1f);
			summedEdgeDifference += difference;
			edgeDifference.SetPixel(x, y, new Color(difference, difference, difference, 1f));
		}
		string overlayPath = $"{_shotDirectory}/{outputStem}_overlay_50.png";
		string differencePath = $"{_shotDirectory}/{outputStem}_edge_difference.png";
		overlay.SavePng(overlayPath);
		edgeDifference.SavePng(differencePath);
		double colourRmse = Math.Sqrt(squaredColourError / (width * height * 3d));
		double meanEdgeDifference = summedEdgeDifference /
			Math.Max(1d, (width - 2d) * (height - 2d));
		GD.Print($"[reference-compare] {label} colour-rmse {colourRmse:F6}, " +
		         $"mean-edge-delta {meanEdgeDifference:F6}");
		GD.Print($"[reference-compare] {overlayPath}");
		GD.Print($"[reference-compare] {differencePath}");
	}

	private static float EdgeMagnitude(Image image, int x, int y)
	{
		static float Luminance(Color c) => c.R * .2126f + c.G * .7152f + c.B * .0722f;
		float gx = Luminance(image.GetPixel(x + 1, y)) - Luminance(image.GetPixel(x - 1, y));
		float gy = Luminance(image.GetPixel(x, y + 1)) - Luminance(image.GetPixel(x, y - 1));
		return MathF.Sqrt(gx * gx + gy * gy);
	}

	public override void _Process(double delta)
	{
		if (!_started || _player == null || _character == null) return;
		if (_playable)
		{
			Vector3 local = _player.Position;
			_streamer.UpdateAround(local);
			_camera.Follow(_player.GetGlobalTransformInterpolated().Origin,
				_player.Velocity, delta);
		}
		_character.Animate(_player.Velocity, _player.Facing,
			_player.IsOnFloor(), _player.Swimming, _player.Sitting, delta);
	}

	public override void _UnhandledInput(InputEvent input)
	{
		if (!_started || _shotDirectory != null) return;
		if (_playable)
		{
			bool handled = false;
			if (input is InputEventMouseButton playableMouse && playableMouse.Pressed)
			{
				if (playableMouse.ButtonIndex == MouseButton.WheelUp)
				{
					_camera.Zoom(-10f);
					handled = true;
				}
				else if (playableMouse.ButtonIndex == MouseButton.WheelDown)
				{
					_camera.Zoom(10f);
					handled = true;
				}
			}
			else if (input is InputEventKey playableKey && playableKey.Pressed && !playableKey.Echo)
			{
				if (playableKey.Keycode == Key.Q)
				{
					_camera.Rotate45(-1);
					handled = true;
				}
				else if (playableKey.Keycode == Key.E)
				{
					_camera.Rotate45(1);
					handled = true;
				}
			}
			if (handled) GetViewport().SetInputAsHandled();
			return;
		}
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
