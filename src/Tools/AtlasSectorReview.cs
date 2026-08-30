using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Petalfell.Core;
using Petalfell.Player;
using Petalfell.Render;
using Petalfell.UI;
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
	private const int Reference1CaptureStreamRadius = 18;
	// Trigger while the full eight-chunk collision/mesh circle still fits inside
	// the old core. The extra four chunks before rearming make the two thresholds
	// observably distinct even if the traveller turns around during cooldown.
	private const int WalkingHandoffTriggerMargin =
		AtlasRuntimeHandoff.DefaultWalkingTriggerMargin;
	private const int WalkingHandoffRearmMargin =
		AtlasRuntimeHandoff.DefaultWalkingRearmMargin;
	private const int WalkingHandoffCooldownFrames =
		AtlasRuntimeHandoff.DefaultWalkingCooldownFrames;
	private const string Reference10SiteId = "bloom-grove-court";
	private const string Reference10TopPath = "res://world-new/reference-10-top.png";
	private const string Reference1SiteId = "shallows-gate-and-causeway";
	private const string Reference1TopPath = "res://world-new/reference-1-top.png";
	private const string ReferenceTopShotName = "reference_top_day";
	private static readonly Vector2I Reference10TopSize = new(1254, 1254);
	private const float Reference10TopPixelsPerVoxel = 10f;
	// The overhead source puts site-local 0,0 at pixel 555,646. Its square image
	// centre therefore looks at local 7.2,1.9; preserving that offset lets a grid
	// overlay compare authored cells without translating either image by eye.
	private static readonly Vector2 Reference10TopLocalCentre = new(7.2f, 1.9f);
	private static readonly Vector2I Reference1TopSize = new(1254, 1254);
	private const float Reference1TopPixelsPerVoxel = 12.5f;
	// Reference 1 registers source pixels as u=665+12.5*x and v=456+12.5*z.
	// Its 627,627 image centre therefore lies at local -3.04,13.68. Focusing
	// there is what makes the raw render directly overlay the tracked top source;
	// using the architectural origin would put the rendered origin 38px left and
	// 171px down from its registered source pixel.
	private static readonly Vector2 Reference1TopLocalCentre = new(-3.04f, 13.68f);
	private static readonly Capture.Shot[] SectorShots =
	{
		// The old nearest atlas view began after the voxel material's 40-block
		// pattern fade had already started. A true play-scale frame is required to
		// judge ground marks, one-course lips and wilderness spacing before the
		// wider composition shots judge the macro silhouette.
		new("atlas_play", 52f, 45f, 29f),
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
	private bool _directTerrain;
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
	private ShaderMaterial _voxelMaterial;
	private ShaderMaterial _detailMaterial;
	private ShaderMaterial _waterDetailMaterial;
	private ShaderMaterial _waterMaterial;
	private WorldAtlasDefinition _atlas;
	private MapDefinition _mapDefinition;
	private AtlasSectorCompiler _compiler;
	private string _packagePath;
	private int _worldSeed;
	private int _playableSectorSpan;
	private AtlasWorldMap _atlasMap;
	private DeveloperMenu _developerMenu;
	private readonly AtlasWalkingHandoffLatch _walkingHandoff =
		new(WalkingHandoffCooldownFrames);
	private bool _walkingHandoffBusy;
	private AtlasWindowEdge _walkingBlockedEdges;
	private AtlasMosaicBounds _walkingBlockedBounds;
	private float _interactiveYaw = 45f;
	private float _interactivePitch = 34f;
	private float _interactiveDistance = 150f;
	private bool _started;
	private bool IsDomain => _domainId != null;
	private bool IsSite => _siteId != null;
	private bool IsAuthoredWindow => IsDomain || IsSite;
	private int StreamRadius => IsSite
		? (_shotDirectory == null ? SiteInteractiveStreamRadius :
			_referenceSite?.SiteId == Reference1SiteId ? Reference1CaptureStreamRadius :
			SiteCaptureStreamRadius)
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
				else if (_referenceSite.SiteId == Reference1SiteId)
				{
					// Reference 1's tracked overhead owns the complete bridge/gate footprint.
					// Distance only clears its tall gate in an orthographic shot; registration
					// comes from the source pixels-per-voxel and centre below.
					siteShots.Add(new Capture.Shot(ReferenceTopShotName,
						220f * _referenceSite.RuntimePlanScale, 0f, 89f,
						time: 0.41f));
					// Similarity-scaled composition frames cannot judge the new one-voxel
					// recesses or whether the unchanged player can traverse subdivided stairs.
					// Keep a player-scale detail ring in addition to the scaled acceptance set.
					for (int quarter = 0; quarter < 4; quarter++)
						siteShots.Add(new Capture.Shot($"site_detail_r{quarter}", 96f,
							siteView.YawDegrees + quarter * 90f, siteView.PitchDegrees,
							time: 0.36f));
				}
				// Every acceptance pass uses one centre at four useful scales and all
				// four cardinal rotations. A hero view alone can conceal a hollow rear
				// facade or a composition that only works from one carefully chosen zoom.
				float reviewScale = _referenceSite.SiteId == Reference1SiteId
					? _referenceSite.RuntimePlanScale : 1f;
				(string name, float distance)[] scales =
				{
					("close", 62f * reviewScale),
					("play", 96f * reviewScale),
					("wide", 154f * reviewScale),
					("far", 240f * reviewScale),
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
		bool compiledAtlas = false;
		var args = OS.GetCmdlineUserArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--review-sector" && i + 1 < args.Length) sector = args[++i];
			else if (args[i].StartsWith("--review-sector=")) sector = args[i][16..];
			else if (args[i] == "--review-domain" && i + 1 < args.Length) domain = args[++i];
			else if (args[i].StartsWith("--review-domain=")) domain = args[i][16..];
			else if (args[i] == "--review-site" && i + 1 < args.Length) site = args[++i];
			else if (args[i].StartsWith("--review-site=")) site = args[i][14..];
			else if (args[i] == "--legacy-world" || args[i] == "--legacy-atlas-demo") legacyWorld = true;
			else if (args[i] == "--compiled-atlas") compiledAtlas = true;
			else if (args[i] == "--review-focus" && i + 1 < args.Length) focus = args[++i];
			else if (args[i].StartsWith("--review-focus=")) focus = args[i][15..];
			else if (args[i] == "--terrain-focus" && i + 1 < args.Length) focus = args[++i];
			else if (args[i].StartsWith("--terrain-focus=")) focus = args[i][16..];
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}
		if (legacyWorld) return false;
		bool directTerrain = !compiledAtlas && sector == null && domain == null && site == null;
		bool playable = (compiledAtlas || directTerrain) && sector == null && domain == null &&
		                site == null && defaultSiteId != null;
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
			_directTerrain = directTerrain,
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
			AtlasSectorCompiler compiler = _directTerrain ? null :
				new AtlasSectorCompiler(map.CanonicalAtlas, map.DefaultSeed, map.CanonicalAtlasPath);
			int pathSplit = map.CanonicalAtlasPath.LastIndexOf('/');
			string packagePath = pathSplit >= 0 ? map.CanonicalAtlasPath[..pathSplit] : "res://content";
			_atlas = map.CanonicalAtlas;
			_mapDefinition = map;
			_compiler = compiler;
			_packagePath = packagePath;
			_worldSeed = map.DefaultSeed;
			string sourceDescription;
			AtlasSectorData data;
			AtlasPreparedWindow directPrepared = null;
			if (_directTerrain)
			{
				if (map.CanonicalAtlas.Topology == null)
					throw new InvalidOperationException("production atlas has no registered authored topology");
				_site = map.CanonicalAtlas.Topology.Sites.Find(s => s.Id == _siteId) ??
					throw new InvalidOperationException($"canonical site '{_siteId}' does not exist");
				_referenceSite = _site.ReferencePlan ??
					throw new InvalidOperationException($"canonical site '{_siteId}' has no loaded reference blueprint");
				_playableSectorSpan = 2;
				Vector2I centre = _requestedFocus ??
					new Vector2I(_referenceSite.Origin.X, _referenceSite.Origin.Z);
				CanonicalSite focusedSite = map.CanonicalAtlas.Topology.Sites.FirstOrDefault(candidate =>
					candidate.RunsInProduction &&
					candidate.ReferencePlan?.ContainsGlobal(centre.X, centre.Y) == true);
				if (focusedSite != null)
				{
					_site = focusedSite;
					_siteId = focusedSite.Id;
					_referenceSite = focusedSite.ReferencePlan;
				}
				AtlasMosaicBounds bounds = AtlasRuntimeHandoff.WindowAround(map.CanonicalAtlas,
					centre.X, centre.Y, _playableSectorSpan);
				directPrepared = ProductionTerrainWindow.Build(map, map.DefaultSeed, bounds,
					message => GD.PrintErr($"[production-terrain] {message}"));
				data = directPrepared.Window.Data;
				sourceDescription = $"full production atlas through terrain window {bounds}";
			}
			else if (IsDomain)
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
					_referenceSite, map.CanonicalAtlas, StreamRadius);
				data = AtlasSectorMosaic.Compose(map.CanonicalAtlas, minX, minZ, maxX, maxZ,
					(sx, sz) => LoadOrRebuild(compiler, packagePath, sx, sz));
				sourceDescription = $"site {_siteId} sectors {minX},{minZ}..{maxX},{maxZ}";
			}
			else
			{
				data = LoadOrRebuild(compiler, packagePath, _sectorX, _sectorZ);
				sourceDescription = $"sector {_sectorX},{_sectorZ}";
			}
			if (_playable)
				_playableSectorSpan = Math.Max(1, data.CoreSize / map.CanonicalAtlas.SectorSize);

			_window = directPrepared?.Window ??
				new AtlasSectorWindow(data, map.CanonicalAtlas, map.DefaultSeed);
			DomainBlockoutStatistics? blockout = !_directTerrain && IsDomain
				? DomainPlanBlockout.Compile(_window, map.CanonicalAtlas.Topology, _domain)
				: null;
			ReferenceSiteStatistics? siteBuild = _directTerrain
				? directPrepared.SiteBuilds
					.Where(build => build.SiteId == _referenceSite.SiteId)
					.Select(build => (ReferenceSiteStatistics?)build.Statistics)
					.FirstOrDefault()
				: IsSite
					? ReferenceSiteBuilder.Build(_window, _referenceSite,
						ReviewSiteVerticalOffset(_window, _referenceSite))
					: null;
			AtlasWildernessDressingStatistics wilderness = _directTerrain
				? directPrepared.Wilderness
				: IsDomain
				? AtlasWildernessDressing.Apply(_window, map.CanonicalAtlas,
					_domain.Plan, map.DefaultSeed)
				: IsSite
					? AtlasWildernessDressing.Apply(_window, map.CanonicalAtlas,
						_referenceSite, map.DefaultSeed)
					: AtlasWildernessDressing.Apply(_window, map.CanonicalAtlas,
						map.DefaultSeed);
			AtlasDomainDressingStatistics? reclamation = IsDomain
				? AtlasDomainDressing.ApplyReclamation(_window, map.CanonicalAtlas,
					_domain.Plan, map.DefaultSeed)
				: null;
			_content = new Node3D { Name = "AtlasWindow", Position = _window.GlobalOrigin };
			AddChild(_content);

			// The legacy world has one water plane and can hide submerged ink by a
			// uniform height. An atlas window has many surface heights; drawing its
			// same ink passes before water lets the real geometry occlude every bed.
			var ink = WorldMaterials.CreateInk(data.SeaLevel, priorityOffset: -6);
			_inkLight = ink.Light;
			_inkDark = ink.Dark;
			_voxelMaterial = WorldMaterials.CreateVoxel(data.SeaLevel);
			_detailMaterial = WorldMaterials.CreateDetail();
			_waterDetailMaterial = WorldMaterials.CreateWaterDetail();
			_streamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
			_content.AddChild(_streamer);
			GroundDetail.Seed = map.DefaultSeed;
			_streamer.Setup(_window, _voxelMaterial, ink.Light, ink.Dark,
				_detailMaterial, _waterDetailMaterial, buildCollision: _playable);
			if (directPrepared != null)
				AttachFineSiteGeometry(_content, _window, directPrepared.SiteBuilds);
			else if (siteBuild is ReferenceSiteStatistics builtSite)
				AttachFineSiteGeometry(_content, _window,
					new[] { new AtlasReferenceSiteBuild(_referenceSite.SiteId, builtSite) });

			_waterMaterial = WorldMaterials.CreateWater(data.SeaLevel,
				surfaceFromMesh: true, reflectionAvailable: false);
			var water = _window.BuildWater(_waterMaterial);
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
				_waterMaterial, map.DefaultSeed);
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
					: _window.FindReviewFocus(StreamRadius * ChunkMesher.ChunkSize);
			// The information-rich point may sit near a sector edge. A one-sector
			// artifact only owns one apron there, so clamp the review anchor far
			// enough inside the local window that every requested chunk is real.
			if (_requestedFocus == null && !IsAuthoredWindow)
				_focusLocal = _window.FocusAtGlobal(
					(int)(_focusLocal.X + _window.Data.OriginX),
					(int)(_focusLocal.Z + _window.Data.OriginZ), StreamRadius);
			_streamer.UpdateAround(_focusLocal, prime: true);
			if (IsSite)
			{
				if (_directTerrain) BuildProductionTraveller();
				else BuildSiteTraveller();
			}

			_camera = new CameraRig { Name = "AtlasCamera", Current = true };
			AddChild(_camera);
			if (IsDomain)
			{
				_camera.Far = 1600f;
				_interactiveDistance = 300f;
			}
			else if (IsSite)
			{
				float siteScale = _referenceSite.SiteId == Reference1SiteId
					? _referenceSite.RuntimePlanScale : 1f;
				if (_referenceSite.SiteId == Reference1SiteId)
					_camera.Near = siteScale;
				_camera.Far = 800f * siteScale;
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
			if (_playable)
			{
				// These are the same controls as the legacy fixture, attached to the
				// actual atlas camera, ink materials and clock. Keeping the map on a
				// separate L0/L1 surface means later sector handoff only replaces the
				// teleport boundary; it does not have to rebuild cartography or UI.
				_developerMenu = new DeveloperMenu { Name = "DeveloperSettings" };
				_developerMenu.Setup(_inkLight, _inkDark, _camera, _day);
				_developerMenu.OpenChanged += open =>
				{
					// Sliders own mouse/keyboard focus while the panel is open. Input
					// actions are polled by Controller rather than delivered as events,
					// so consuming tilde alone would still let a held movement key walk
					// the player behind the overlay.
					_player?.StopTravel();
					if (_player != null)
						_player.InputEnabled = !open && _atlasMap?.IsOpen != true;
				};
				AddChild(_developerMenu);

				_atlasMap = new AtlasWorldMap { Name = "ProductionAtlasMap" };
				_atlasMap.Setup(map.CanonicalAtlas,
					_directTerrain ? "" : $"{packagePath}/derived",
					_directTerrain ? "" : compiler.SourceFingerprint);
				_atlasMap.TeleportRequested += TeleportAcrossAtlas;
				AddChild(_atlasMap);
				_atlasMap.SetPlayer(_player.GlobalPosition);
			}
			AtlasSectorStatistics stats = data.CoreStatistics();
			Vector3 globalFocus = GlobalFocus();
			GD.Print($"[atlas-review] {sourceDescription} " +
			         $"window {data.Width}x{data.Depth} origin {data.OriginX},{data.OriginZ} " +
			         $"focus {globalFocus.X:0},{globalFocus.Z:0} chunks {_streamer.LoadedCount}");
			GD.Print($"[atlas-review] land {stats.LandCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
			         $"water {stats.WaterCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
			         $"cliff {stats.CliffCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
			         $"shore {stats.ShoreCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
			         $"water-steps {stats.WaterStepEdges} severe {stats.SevereWaterStepEdges} " +
			         $"max-step {stats.MaxWaterStep}@{stats.MaxWaterStepX},{stats.MaxWaterStepZ} " +
			         $"submerged-dry {stats.SubmergedDryBoundaryEdges} " +
			         $"max-depth {stats.MaxSubmergedDryDepth}@{stats.MaxSubmergedDryX},{stats.MaxSubmergedDryZ} " +
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
			GD.Print($"[atlas-wilderness] {wilderness.Trees} trees/{wilderness.Boulders} boulders " +
			         $"from {wilderness.Candidates} candidates; rejected " +
			         $"{wilderness.Excluded} authored/{wilderness.Unsuitable} terrain/" +
			         $"{wilderness.Occupied} occupied; manifest {wilderness.ManifestHash:x16}");
			if (reclamation is AtlasDomainDressingStatistics d)
				GD.Print($"[atlas-reclamation] {d.Trees} trees from {d.Candidates} authored candidates");
			if (siteBuild is ReferenceSiteStatistics s)
				GD.Print($"[reference-site] {_siteId} explicit surface {s.SurfaceCells} cells, " +
				         $"{s.Voxels} voxel writes, source {_referenceSite.ReferencePath}");
			_started = true;

			if (_shotDirectory != null) await RunCapture();
			else if (_playable)
				GD.Print("[atlas-runtime] W/A/S/D move  hold Shift slow-walk  Space jump  Q/E orbit  K auto-zoom  mouse wheel zoom  M atlas map  Shift-click atlas reload/teleport  tilde developer settings  --legacy-world restores the retired fixture");
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
		ReferenceSiteDefinition site, WorldAtlasDefinition atlas, int streamRadius)
	{
		int columns = atlas.Width / atlas.SectorSize;
		int rows = atlas.Depth / atlas.SectorSize;
		PlanPoint[] corners =
		{
			new() { X = site.RuntimeFootprintMin.X, Z = site.RuntimeFootprintMin.Z },
			new() { X = site.RuntimeFootprintMax.X, Z = site.RuntimeFootprintMin.Z },
			new() { X = site.RuntimeFootprintMin.X, Z = site.RuntimeFootprintMax.Z },
			new() { X = site.RuntimeFootprintMax.X, Z = site.RuntimeFootprintMax.Z },
		};
		int minGlobalX = atlas.Width - 1, minGlobalZ = atlas.Depth - 1;
		int maxGlobalX = 0, maxGlobalZ = 0;
		foreach (PlanPoint corner in corners)
		{
			BlockPoint point = site.ToGlobalRuntime(corner);
			minGlobalX = Math.Min(minGlobalX, point.X);
			maxGlobalX = Math.Max(maxGlobalX, point.X);
			minGlobalZ = Math.Min(minGlobalZ, point.Z);
			maxGlobalZ = Math.Max(maxGlobalZ, point.Z);
		}
		// A valid camera centre is allowed anywhere in the registered footprint.
		// Include its complete mesh circle before choosing the square mosaic; merely
		// making the footprint fit can still make FocusAtGlobal clamp hundreds of
		// blocks away and emit a perfectly valid screenshot of the wrong place.
		int margin = streamRadius * ChunkMesher.ChunkSize;
		int minX = Math.Clamp(Math.Max(0, minGlobalX - margin) / atlas.SectorSize,
			0, columns - 1);
		int minZ = Math.Clamp(Math.Max(0, minGlobalZ - margin) / atlas.SectorSize,
			0, rows - 1);
		int maxX = Math.Clamp(Math.Min(atlas.Width - 1, maxGlobalX + margin) /
			atlas.SectorSize, 0, columns - 1);
		int maxZ = Math.Clamp(Math.Min(atlas.Depth - 1, maxGlobalZ + margin) /
			atlas.SectorSize, 0, rows - 1);
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
		// Keep a two-sector floor for tiny sites as well. This preserves the accepted
		// Bloom review context while the margin calculation above naturally grows the
		// much larger Reference 1 capture to a real 3x3 sector mosaic.
		if (maxX == minX && maxZ == minZ)
		{
			if (site.Origin.X - minX * atlas.SectorSize < atlas.SectorSize / 2 && minX > 0)
				minX--;
			else if (maxX + 1 < columns) maxX++;
			else minX--;
			if (site.Origin.Z - minZ * atlas.SectorSize < atlas.SectorSize / 2 && minZ > 0)
				minZ--;
			else if (maxZ + 1 < rows) maxZ++;
			else minZ--;
		}
		return (minX, minZ, maxX, maxZ);
	}

	private void BuildSiteTraveller()
	{
		BlockPoint global = _referenceSite.ToGlobal(_referenceSite.PlayerSpawn);
		int x = global.X - _window.Data.OriginX;
		int z = global.Z - _window.Data.OriginZ;
		BuildTraveller(new Vector3(x + .5f, _window.Grid.HeightAt(x, z) + .2f,
			z + .5f));
	}

	private void BuildProductionTraveller()
	{
		BlockPoint authored = _referenceSite.ToGlobal(_referenceSite.PlayerSpawn);
		// A direct atlas focus chooses the window, but when it names a production
		// site the traveller still belongs at that site's authored review spawn.
		// Spawning on the requested centre put Reference 12's player on a statue leg.
		bool focusedAuthoredSite = _requestedFocus.HasValue &&
			_referenceSite.ContainsGlobal(_requestedFocus.Value.X, _requestedFocus.Value.Y);
		int globalX = focusedAuthoredSite ? authored.X : _requestedFocus?.X ?? authored.X;
		int globalZ = focusedAuthoredSite ? authored.Z : _requestedFocus?.Y ?? authored.Z;
		if (!AtlasRuntimeHandoff.TryResolveLanding(_window, globalX, globalZ,
		    out AtlasRuntimeLanding landing, out string rejection))
			throw new InvalidOperationException(
				$"production terrain has no safe landing near {globalX},{globalZ} ({rejection})");
		BuildTraveller(new Vector3(landing.LocalX + .5f, landing.SurfaceY + .2f,
			landing.LocalZ + .5f));
		if (!landing.ExactCell)
			GD.Print($"[production-terrain] requested spawn {globalX},{globalZ} " +
			         $"resolved at radius {landing.SearchRadius}");
	}

	private void BuildTraveller(Vector3 spawn)
	{
		_player = new Controller
		{
			Name = _playable ? "Player" : "ScaleTraveller",
			Position = spawn,
			InputEnabled = _playable,
		};
		_content.AddChild(_player);
		// The production atlas has a different water surface in each global cell.
		// Capture the mutable review field rather than one window instance so the
		// same controller immediately samples the replacement after a map or walking
		// handoff. Falling back to the legacy constant made atlas rivers unswimmable.
		_player.Setup(null, AtlasWaterColumnAt);
		_player.ResetPhysicsInterpolation();
		if (!_playable) _player.SetPhysicsProcess(false);
		_character = new Character { Name = "Traveller" };
		_player.AddChild(_character);
		_character.Setup(_inkLight, _inkDark);
	}

	private Controller.WaterColumn? AtlasWaterColumnAt(int globalX, int globalZ)
	{
		if (_window != null && _window.TryWaterColumnAtGlobal(globalX, globalZ,
		    out float bedY, out float surfaceY))
			return new Controller.WaterColumn(bedY, surfaceY);
		return null;
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
			Vector2I referenceTopSize = referenceTop ? ReferenceTopSize() : default;
			captureViewport.Size = referenceTop ? referenceTopSize : captureSize;
			captureCamera.Projection = referenceTop
				? Camera3D.ProjectionType.Orthogonal
				: _camera.Projection;
			if (referenceTop)
				captureCamera.Size = referenceTopSize.Y / ReferenceTopPixelsPerVoxel();
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
				float siteScale = IsSite && _referenceSite?.SiteId == Reference1SiteId
					? _referenceSite.RuntimePlanScale : 1f;
				_key.DirectionalShadowMaxDistance = Math.Clamp(shot.Distance * 1.18f,
					260f * siteScale, IsDomain ? 1200f : 420f * siteScale);
			}
			if (_environment != null)
			{
				// Preserve the authored density/curve but move its far plane with a
				// deliberate atlas overview. Leaving the 580-block play end here made
				// every 1,000-block composition test a flat fog-colour swatch.
				float siteScale = IsSite && _referenceSite?.SiteId == Reference1SiteId
					? _referenceSite.RuntimePlanScale : 1f;
				_environment.FogDepthBegin = Math.Max(180f * siteScale, shot.Distance * .40f);
				_environment.FogDepthEnd = Math.Max(700f * siteScale, shot.Distance * 2.00f);
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
			if (IsReferenceTopShot(shot)) return ReferenceTopFocus();
			BlockPoint sitePoint = _referenceSite.ToGlobal(_referenceSite.ReferenceView.Focus);
			Vector3 siteLocal = _window.FocusAtGlobal(sitePoint.X, sitePoint.Z, StreamRadius);
			siteLocal.Y += _referenceSite.ReferenceView.HeightOffset *
				_referenceSite.RuntimePlanScale;
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
		IsSite && _referenceSite != null &&
		(_referenceSite.SiteId == Reference10SiteId ||
		 _referenceSite.SiteId == Reference1SiteId) &&
		shot.Name == ReferenceTopShotName;

	private Vector2I ReferenceTopSize() => _referenceSite.SiteId switch
	{
		Reference10SiteId => Reference10TopSize,
		Reference1SiteId => Reference1TopSize,
		_ => throw new InvalidOperationException(
			$"site '{_referenceSite.SiteId}' has no registered overhead source"),
	};

	private float ReferenceTopPixelsPerVoxel() => _referenceSite.SiteId switch
	{
		Reference10SiteId => Reference10TopPixelsPerVoxel,
		Reference1SiteId => Reference1TopPixelsPerVoxel /
			_referenceSite.RuntimePlanScale,
		_ => throw new InvalidOperationException(
			$"site '{_referenceSite.SiteId}' has no registered overhead scale"),
	};

	private Vector3 ReferenceTopFocus() => _referenceSite.SiteId switch
	{
		Reference10SiteId => Reference10TopFocus(),
		Reference1SiteId => Reference1TopFocus(),
		_ => throw new InvalidOperationException(
			$"site '{_referenceSite.SiteId}' has no registered overhead focus"),
	};

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

	private Vector3 Reference1TopFocus()
	{
		float radians = _referenceSite.AxisDegrees * MathF.PI / 180f;
		float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
		// The resolved Reference 1 contract has no X reflection: source +X and +Z
		// are runtime local +X and +Z. Rotate its fractional source-image centre by
		// the permanent site axis before sampling the atlas window. The fractional
		// remainder is restored after FocusAtGlobal because a 0.04-voxel error is
		// already half a source pixel at this 12.5px/voxel registration.
		float runtimeX = Reference1TopLocalCentre.X * _referenceSite.RuntimePlanScale;
		float runtimeZ = Reference1TopLocalCentre.Y * _referenceSite.RuntimePlanScale;
		float globalX = _referenceSite.Origin.X + runtimeX * cos + runtimeZ * sin;
		float globalZ = _referenceSite.Origin.Z - runtimeX * sin + runtimeZ * cos;
		int cellX = (int)MathF.Round(globalX);
		int cellZ = (int)MathF.Round(globalZ);
		Vector3 local = _window.FocusAtGlobal(cellX, cellZ, StreamRadius);
		local.X += globalX - cellX;
		local.Z += globalZ - cellZ;
		return local + _content.Position;
	}

	private void PlaceReviewCamera(CameraRig camera, Capture.Shot shot,
		Vector3 focus, bool referenceTop)
	{
		if (!referenceTop)
		{
			Capture.Place(camera, shot, focus);
			return;
		}
		// A true vertical transform avoids the small height-dependent footprint
		// drift caused by an 89-degree perspective approximation.
		camera.GlobalPosition = focus + Vector3.Up * shot.Distance;
		if (_referenceSite.SiteId == Reference10SiteId)
		{
			// Preserve Reference 10 exactly: its runtime-reflected source X points
			// world -X, and this up vector puts world -X screen-right.
			camera.LookAt(focus, Vector3.Back);
			return;
		}

		float radians = _referenceSite.AxisDegrees * MathF.PI / 180f;
		// Reference 1 has no reflection. Its source +Z points toward image-bottom,
		// so screen-up must be the negative of runtime +Z after atlas rotation.
		// At the permanent zero-degree axis this is Vector3.Forward, placing runtime
		// +X screen-right and runtime +Z screen-down exactly as the source contract.
		Vector3 screenUp = new(-MathF.Sin(radians), 0f, -MathF.Cos(radians));
		camera.LookAt(focus, screenUp);
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
		if (IsSite && IsRegisteredReferenceTopSite() &&
		    (_only == null || _only.Contains(ReferenceTopShotName)))
			WriteImageComparison($"{_shotDirectory}/{ReferenceTopShotName}.png",
				ReferenceTopPath(), "reference_top", "overhead");
	}

	private bool IsRegisteredReferenceTopSite() =>
		_referenceSite?.SiteId == Reference10SiteId ||
		_referenceSite?.SiteId == Reference1SiteId;

	private string ReferenceTopPath() => _referenceSite.SiteId switch
	{
		Reference10SiteId => Reference10TopPath,
		Reference1SiteId => Reference1TopPath,
		_ => throw new InvalidOperationException(
			$"site '{_referenceSite.SiteId}' has no registered overhead source"),
	};

	private void WriteImageComparison(string capturePath, string referencePath,
		string outputStem, string label)
	{
		if (!Godot.FileAccess.FileExists(capturePath) ||
		    !Godot.FileAccess.FileExists(referencePath)) return;

		Image captured = Image.LoadFromFile(capturePath);
		// Comparison sources are ordinary tracked PNGs, not runtime texture assets.
		// Loading only through ResourceLoader silently skipped a newly added top
		// reference until an editor happened to create its .godot/imported cache.
		// The review result must be identical on a cold clone and in an isolated XDG
		// capture process, so decode the source file directly first.
		Image reference = Image.LoadFromFile(referencePath);
		if (reference == null || reference.IsEmpty())
		{
			Texture2D referenceTexture = ResourceLoader.Load<Texture2D>(referencePath);
			reference = referenceTexture?.GetImage();
		}
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
			ConstrainRefusedWalkingEdge();
			TryWalkingBoundaryHandoff();
			_atlasMap?.SetPlayer(_player.GlobalPosition);
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
			if (input is InputEventKey mapKey && mapKey.Pressed && !mapKey.Echo &&
			    (mapKey.PhysicalKeycode == Key.M || mapKey.Keycode == Key.M))
			{
				ToggleAtlasMap();
				handled = true;
			}
			// The map's Control owns pointer gestures. Suppressing gameplay keyboard
			// handling here also keeps Q/E from orbiting a world hidden by the map.
			if (_atlasMap?.IsOpen == true)
			{
				if (handled) GetViewport().SetInputAsHandled();
				return;
			}
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
				else if (playableKey.PhysicalKeycode == Key.K || playableKey.Keycode == Key.K)
				{
					_camera.StartAutoZoomToMaximum();
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

	private void ToggleAtlasMap()
	{
		if (_atlasMap == null || _player == null) return;
		_atlasMap.SetPlayer(_player.GlobalPosition);
		_atlasMap.Toggle();
		_player.StopTravel();
		_player.InputEnabled = !_atlasMap.IsOpen && _developerMenu?.IsOpen != true;
	}

	private void TryWalkingBoundaryHandoff()
	{
		if (_walkingHandoffBusy || _window == null || _player == null ||
		    _atlas == null || (!_directTerrain && _compiler == null) ||
		    _atlasMap?.IsOpen == true ||
		    _developerMenu?.IsOpen == true) return;
		Vector3 globalPosition = _player.GlobalPosition;
		AtlasMosaicBounds current = AtlasRuntimeHandoff.BoundsOf(_atlas, _window.Data);
		AtlasWalkingHandoffDecision decision = _walkingHandoff.Evaluate(_atlas, current,
			globalPosition.X, globalPosition.Z, WalkingHandoffTriggerMargin,
			WalkingHandoffRearmMargin, _walkingBlockedEdges,
			out AtlasWalkingTransition transition,
			out string refusal);
		if (_walkingBlockedEdges != AtlasWindowEdge.None &&
		    AtlasRuntimeHandoff.InsideWalkingRearmBand(_atlas, current,
			    globalPosition.X, globalPosition.Z, WalkingHandoffRearmMargin,
			    _walkingBlockedEdges))
			_walkingBlockedEdges = AtlasWindowEdge.None;
		if (decision == AtlasWalkingHandoffDecision.Refused)
		{
			BlockWalkingEdges(current, transition.TriggeredEdges);
			GD.PrintErr($"[atlas-walk-handoff] refused at " +
			            $"{globalPosition.X:0.00},{globalPosition.Z:0.00}: {refusal}; " +
			            $"retained sectors {current}");
			ConstrainRefusedWalkingEdge();
			return;
		}
		if (decision != AtlasWalkingHandoffDecision.Transition) return;

		_walkingHandoffBusy = true;
		try
		{
			AtlasPreparedWindow prepared = PrepareRuntimeWindowAtBounds(transition.To,
				message => GD.PrintErr($"[atlas-walk-handoff] {message}"));
			int globalX = Mathf.FloorToInt(globalPosition.X);
			int globalZ = Mathf.FloorToInt(globalPosition.Z);
			if (!AtlasRuntimeHandoff.TryResolveExactLanding(_window, globalX, globalZ,
			    out AtlasRuntimeLanding oldLanding, out string oldRejection))
			{
				RefuseWalkingTransition(transition, current, globalPosition,
					$"current exact cell rejected ({oldRejection})");
				return;
			}
			if (!AtlasRuntimeHandoff.TryResolveExactLanding(prepared.Window,
			    globalX, globalZ, out AtlasRuntimeLanding nextLanding,
			    out string nextRejection))
			{
				RefuseWalkingTransition(transition, current, globalPosition,
					$"adjacent exact cell rejected ({nextRejection})");
				return;
			}
			if (oldLanding.SurfaceY != nextLanding.SurfaceY)
			{
				RefuseWalkingTransition(transition, current, globalPosition,
					$"exact seam surface changed {oldLanding.SurfaceY}->{nextLanding.SurfaceY}");
				return;
			}

			InstallWalkingWindow(prepared, transition, globalPosition, nextLanding);
			_walkingHandoff.Complete(transition);
			AtlasWindowEdge unshifted = transition.TriggeredEdges &
				~transition.ShiftedEdges;
			if (unshifted != AtlasWindowEdge.None)
				BlockWalkingEdges(transition.To, unshifted);
			else _walkingBlockedEdges = AtlasWindowEdge.None;
		}
		catch (Exception ex)
		{
			RefuseWalkingTransition(transition, current, globalPosition,
				$"replacement failed ({ex.Message})");
		}
		finally
		{
			_walkingHandoffBusy = false;
		}
	}

	private void InstallWalkingWindow(AtlasPreparedWindow prepared,
		AtlasWalkingTransition transition, Vector3 exactGlobalPosition,
		AtlasRuntimeLanding landing)
	{
		AtlasSectorWindow nextWindow = prepared.Window;
		var nextContent = new Node3D
		{
			Name = "AtlasWindow",
			Position = nextWindow.GlobalOrigin,
		};
		var nextStreamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
		nextContent.AddChild(nextStreamer);
		nextStreamer.Setup(nextWindow, _voxelMaterial, _inkLight, _inkDark,
			_detailMaterial, _waterDetailMaterial, buildCollision: true);
		AttachFineSiteGeometry(nextContent, nextWindow, prepared.SiteBuilds);
		MeshInstance3D water = nextWindow.BuildWater(_waterMaterial);
		if (water != null) nextContent.AddChild(water);
		Vector3 nextLocal = exactGlobalPosition - nextWindow.GlobalOrigin;
		nextStreamer.UpdateAround(nextLocal, prime: true);

		Transform3D playerGlobalTransform = _player.GlobalTransform;
		Vector3 velocity = _player.Velocity;
		bool inputEnabled = _player.InputEnabled;
		Node3D previousContent = _content;
		AddChild(nextContent);
		if (_player.GetParent() is Node previousParent) previousParent.RemoveChild(_player);
		nextContent.AddChild(_player);
		_player.GlobalTransform = playerGlobalTransform;
		_player.Velocity = velocity;
		_player.InputEnabled = inputEnabled;
		_player.ResetPhysicsInterpolation();
		if (previousContent?.GetParent() == this) RemoveChild(previousContent);
		previousContent?.QueueFree();

		_window = nextWindow;
		_content = nextContent;
		_streamer = nextStreamer;
		_focusLocal = _player.Position;
		_atlasMap.SetPlayer(_player.GlobalPosition);
		GD.Print($"[atlas-walk-handoff] {AtlasRuntimeHandoff.DescribeEdges(transition.TriggeredEdges)} " +
		         $"{transition.From} -> {transition.To} at exact " +
		         $"{exactGlobalPosition.X:0.00},{exactGlobalPosition.Z:0.00}," +
		         $"{exactGlobalPosition.Y:0.00}; surface {landing.SurfaceY}; " +
		         $"velocity {velocity.X:0.00},{velocity.Y:0.00},{velocity.Z:0.00}; " +
		         $"wilderness {prepared.Wilderness.ManifestHash:x16}");
		foreach (AtlasReferenceSiteBuild site in prepared.SiteBuilds)
			GD.Print($"[atlas-walk-handoff] rebuilt site {site.SiteId}: " +
			         $"{site.Statistics.SurfaceCells} surface cells/" +
			         $"{site.Statistics.Voxels} voxel writes");
	}

	private void RefuseWalkingTransition(AtlasWalkingTransition transition,
		AtlasMosaicBounds current, Vector3 globalPosition, string reason)
	{
		_walkingHandoff.Reject(transition);
		BlockWalkingEdges(current, transition.TriggeredEdges);
		GD.PrintErr($"[atlas-walk-handoff] refused " +
		            $"{AtlasRuntimeHandoff.DescribeEdges(transition.TriggeredEdges)} at " +
		            $"{globalPosition.X:0.00},{globalPosition.Z:0.00}: {reason}; " +
		            $"retained sectors {current}; no fallback teleport");
		ConstrainRefusedWalkingEdge();
	}

	private void BlockWalkingEdges(AtlasMosaicBounds bounds, AtlasWindowEdge edges)
	{
		_walkingBlockedBounds = bounds;
		_walkingBlockedEdges = edges;
	}

	private void ConstrainRefusedWalkingEdge()
	{
		if (_walkingBlockedEdges == AtlasWindowEdge.None || _player == null ||
		    _atlas == null || _window == null) return;
		AtlasMosaicBounds current = AtlasRuntimeHandoff.BoundsOf(_atlas, _window.Data);
		if (current != _walkingBlockedBounds)
		{
			_walkingBlockedEdges = AtlasWindowEdge.None;
			return;
		}
		float minX = current.MinSectorX * _atlas.SectorSize +
			WalkingHandoffTriggerMargin + .5f;
		float minZ = current.MinSectorZ * _atlas.SectorSize +
			WalkingHandoffTriggerMargin + .5f;
		float maxX = (current.MaxSectorX + 1) * _atlas.SectorSize -
			WalkingHandoffTriggerMargin - .5f;
		float maxZ = (current.MaxSectorZ + 1) * _atlas.SectorSize -
			WalkingHandoffTriggerMargin - .5f;
		Vector3 global = _player.GlobalPosition;
		Vector3 velocity = _player.Velocity;
		if ((_walkingBlockedEdges & AtlasWindowEdge.West) != 0)
		{
			if (global.X < minX) global.X = minX;
			if (velocity.X < 0f) velocity.X = 0f;
		}
		if ((_walkingBlockedEdges & AtlasWindowEdge.East) != 0)
		{
			if (global.X > maxX) global.X = maxX;
			if (velocity.X > 0f) velocity.X = 0f;
		}
		if ((_walkingBlockedEdges & AtlasWindowEdge.North) != 0)
		{
			if (global.Z < minZ) global.Z = minZ;
			if (velocity.Z < 0f) velocity.Z = 0f;
		}
		if ((_walkingBlockedEdges & AtlasWindowEdge.South) != 0)
		{
			if (global.Z > maxZ) global.Z = maxZ;
			if (velocity.Z > 0f) velocity.Z = 0f;
		}
		_player.GlobalPosition = global;
		_player.Velocity = velocity;
	}

	private void TeleportAcrossAtlas(Vector3 requestedGlobal)
	{
		if (_window == null || _player == null || _atlas == null ||
		    (!_directTerrain && _compiler == null)) return;
		int requestedX = Mathf.FloorToInt(requestedGlobal.X);
		int requestedZ = Mathf.FloorToInt(requestedGlobal.Z);
		if (requestedX < 0 || requestedZ < 0 ||
		    requestedX >= _atlas.Width || requestedZ >= _atlas.Depth)
		{
			GD.PrintErr($"[atlas-handoff] rejected out-of-bounds address " +
			            $"{requestedGlobal.X:0.00},{requestedGlobal.Z:0.00} for " +
			            $"{_atlas.Width}x{_atlas.Depth} atlas");
			return;
		}

		try
		{
			if (InsideCurrentCore(requestedX, requestedZ) &&
			    AtlasRuntimeHandoff.TryResolveLanding(_window, requestedX, requestedZ,
				    out AtlasRuntimeLanding currentLanding, out string currentRejection))
			{
				PlacePlayerInCurrentWindow(requestedGlobal, currentLanding, currentRejection,
					"current mosaic");
				return;
			}

			if (TryPrepareLanding(requestedX, requestedZ, out AtlasPreparedWindow prepared,
			    out AtlasRuntimeLanding landing, out string requestedRejection))
			{
				InstallPreparedWindow(prepared, requestedGlobal, landing, requestedRejection,
					"requested address");
				return;
			}
			string originalRejection = requestedRejection ??
				"no traversable surface in requested mosaic";

			GD.Print($"[atlas-handoff] no traversable surface in the requested " +
			         $"{_playableSectorSpan}x{_playableSectorSpan} mosaic around " +
			         $"{requestedX},{requestedZ}; consulting registered dry land");
			if (AtlasRuntimeHandoff.TryNearestAuthoredDryHint(_atlas, requestedX, requestedZ,
			    out BlockPoint dryHint, out int pixelRadius) &&
			    TryPrepareLanding(dryHint.X, dryHint.Z, out prepared, out landing,
				    out string dryHintRejection))
			{
				GD.Print($"[atlas-handoff] registered dry hint {dryHint.X},{dryHint.Z} " +
				         $"at source-pixel radius {pixelRadius}");
				InstallPreparedWindow(prepared, requestedGlobal, landing, originalRejection,
					"nearest registered dry land", fallbackAddress: true);
				if (dryHintRejection != null)
					GD.Print($"[atlas-handoff] dry hint cell rejected ({dryHintRejection}); " +
					         $"local fallback radius {landing.SearchRadius}");
				return;
			}

			// A malformed or unusually steep authored macro source must not strand the
			// traveller. Bloom's explicit player spawn is the final deterministic
			// recovery address because its authored blueprint owns a traversable court.
			if (AtlasRuntimeHandoff.TryGetAuthoredRecoverySpawn(_atlas,
			    out BlockPoint bloomSpawn) &&
			    TryPrepareLanding(bloomSpawn.X, bloomSpawn.Z, out prepared, out landing,
				    out string bloomRejection))
			{
				GD.PrintErr($"[atlas-handoff] dry-land fallback failed; recovering at " +
				            $"authored Bloom spawn {bloomSpawn.X},{bloomSpawn.Z}");
				InstallPreparedWindow(prepared, requestedGlobal, landing, originalRejection,
					"authored Bloom recovery", fallbackAddress: true);
				if (bloomRejection != null)
					GD.Print($"[atlas-handoff] Bloom spawn cell rejected ({bloomRejection}); " +
					         $"local fallback radius {landing.SearchRadius}");
				return;
			}

			GD.PushError($"[atlas-handoff] no deterministic traversable surface could be " +
			             $"resolved for {requestedX},{requestedZ}; current window retained");
		}
		catch (Exception ex)
		{
			// Compilation or cache corruption must leave the currently playable scene
			// intact. The old content is detached only after a complete replacement has
			// been materialised and primed.
			GD.PushError($"[atlas-handoff] {requestedX},{requestedZ} failed: {ex.Message}; " +
			             "current window retained");
		}
	}

	private bool InsideCurrentCore(int globalX, int globalZ)
	{
		AtlasSectorData data = _window.Data;
		int minX = data.OriginX + data.Apron;
		int minZ = data.OriginZ + data.Apron;
		return globalX >= minX && globalZ >= minZ &&
		       globalX < minX + data.CoreSize && globalZ < minZ + data.CoreSize;
	}

	private bool TryPrepareLanding(int centreGlobalX, int centreGlobalZ,
		out AtlasPreparedWindow prepared, out AtlasRuntimeLanding landing,
		out string requestedRejection)
	{
		prepared = PrepareRuntimeWindow(centreGlobalX, centreGlobalZ,
			message => GD.PrintErr($"[atlas-handoff] {message}"));
		return AtlasRuntimeHandoff.TryResolveLanding(prepared.Window,
			centreGlobalX, centreGlobalZ,
			out landing, out requestedRejection);
	}

	private AtlasPreparedWindow PrepareRuntimeWindow(int centreGlobalX,
		int centreGlobalZ, Action<string> warning)
	{
		AtlasMosaicBounds bounds = AtlasRuntimeHandoff.WindowAround(_atlas,
			centreGlobalX, centreGlobalZ, _playableSectorSpan);
		return PrepareRuntimeWindowAtBounds(bounds, warning);
	}

	private AtlasPreparedWindow PrepareRuntimeWindowAtBounds(AtlasMosaicBounds bounds,
		Action<string> warning)
	{
		if (_directTerrain)
			return ProductionTerrainWindow.Build(_mapDefinition, _worldSeed, bounds, warning);
		return AtlasRuntimeHandoff.PrepareWindowAtBounds(_atlas, _worldSeed, bounds,
			(sx, sz) => LoadOrRebuild(_compiler, _packagePath, sx, sz), warning);
	}

	private void InstallPreparedWindow(AtlasPreparedWindow prepared,
		Vector3 requestedGlobal, AtlasRuntimeLanding landing, string requestedRejection,
		string resolution, bool fallbackAddress = false)
	{
		AtlasSectorWindow nextWindow = prepared.Window;
		var nextContent = new Node3D
		{
			Name = "AtlasWindow",
			Position = nextWindow.GlobalOrigin,
		};
		var nextStreamer = new ChunkStreamer { Name = "Chunks", LoadRadius = StreamRadius };
		nextContent.AddChild(nextStreamer);
		nextStreamer.Setup(nextWindow, _voxelMaterial, _inkLight, _inkDark,
			_detailMaterial, _waterDetailMaterial, buildCollision: true);
		AttachFineSiteGeometry(nextContent, nextWindow, prepared.SiteBuilds);
		MeshInstance3D water = nextWindow.BuildWater(_waterMaterial);
		if (water != null) nextContent.AddChild(water);
		var localLanding = new Vector3(landing.LocalX + .5f, landing.SurfaceY + .2f,
			landing.LocalZ + .5f);
		nextStreamer.UpdateAround(localLanding, prime: true);

		Node3D previousContent = _content;
		AddChild(nextContent);
		if (_player.GetParent() is Node previousParent) previousParent.RemoveChild(_player);
		nextContent.AddChild(_player);
		_player.StopTravel();
		_player.Velocity = Vector3.Zero;
		_player.Position = localLanding;
		_player.ResetPhysicsInterpolation();
		if (previousContent?.GetParent() == this) RemoveChild(previousContent);
		previousContent?.QueueFree();

		_window = nextWindow;
		_content = nextContent;
		_streamer = nextStreamer;
		_focusLocal = localLanding;
		FinishHandoff(requestedGlobal, landing, requestedRejection, resolution,
			prepared.Bounds, prepared.Wilderness, prepared.SiteBuilds, fallbackAddress);
	}

	private void AttachFineSiteGeometry(Node3D parent, AtlasSectorWindow window,
		IReadOnlyList<AtlasReferenceSiteBuild> builds)
	{
		if (parent == null || window == null || builds == null || _atlas?.Topology == null)
			return;
		foreach (AtlasReferenceSiteBuild build in builds)
		{
			if (build.SiteId != Reference12SculptureDetail.SiteId) continue;
			ReferenceSiteDefinition site = _atlas.Topology.Sites
				.FirstOrDefault(candidate => candidate.Id == build.SiteId)?.ReferencePlan;
			Node3D detail = Reference12SculptureDetail.Build(window, site,
				_inkLight, _inkDark);
			if (detail != null) parent.AddChild(detail);
		}
	}

	private static int ReviewSiteVerticalOffset(AtlasSectorWindow window,
		ReferenceSiteDefinition site)
	{
		// The review tool composes current atlas terrain, whose accepted elevation
		// can move independently of a reference's local Y datum. Production already
		// translates supported site blueprints onto that ground; applying the same
		// rule here prevents a valid monument being buried after a terrain rebuild.
		if (window == null || site == null ||
		    site.BuilderId == Reference1ShallowsGateCauseway.BuilderId) return 0;
		int localX = site.Origin.X - window.Data.OriginX;
		int localZ = site.Origin.Z - window.Data.OriginZ;
		ReferenceGroundPlanTerrain datum = ReferenceSiteGroundPlan.Load(site).Terrain
			.FirstOrDefault(shape => shape.WriteMode == "preserve-atlas" &&
				shape.SurfaceY.HasValue);
		if (datum?.SurfaceY == null || localX < 0 || localZ < 0 ||
		    localX >= window.Grid.Size || localZ >= window.Grid.Size) return 0;
		return window.Grid.Top[localZ * window.Grid.Size + localX] - datum.SurfaceY.Value;
	}

	private void PlacePlayerInCurrentWindow(Vector3 requestedGlobal,
		AtlasRuntimeLanding landing, string requestedRejection, string resolution)
	{
		var localLanding = new Vector3(landing.LocalX + .5f, landing.SurfaceY + .2f,
			landing.LocalZ + .5f);
		_streamer.UpdateAround(localLanding, prime: true);
		_player.StopTravel();
		_player.Velocity = Vector3.Zero;
		_player.Position = localLanding;
		_player.ResetPhysicsInterpolation();
		_focusLocal = localLanding;
		int minSectorX = (_window.Data.OriginX + _window.Data.Apron) / _atlas.SectorSize;
		int minSectorZ = (_window.Data.OriginZ + _window.Data.Apron) / _atlas.SectorSize;
		var bounds = new AtlasMosaicBounds(minSectorX, minSectorZ,
			minSectorX + _playableSectorSpan - 1,
			minSectorZ + _playableSectorSpan - 1);
		FinishHandoff(requestedGlobal, landing, requestedRejection, resolution,
			bounds, null, null, fallbackAddress: false);
	}

	private void FinishHandoff(Vector3 requestedGlobal, AtlasRuntimeLanding landing,
		string requestedRejection, string resolution, AtlasMosaicBounds bounds,
		AtlasWildernessDressingStatistics? wilderness,
		IReadOnlyList<AtlasReferenceSiteBuild> siteBuilds,
		bool fallbackAddress)
	{
		_walkingHandoff.Reset();
		_walkingBlockedEdges = AtlasWindowEdge.None;
		Vector3 globalLanding = _player.GlobalPosition;
		_camera.Follow(globalLanding, Vector3.Zero, 1.0);
		_atlasMap.SetPlayer(globalLanding);
		// Map and developer surfaces are not reconstructed by a handoff. If either
		// was open, it stays open with its current pan/zoom/slider state.
		_player.InputEnabled = !_atlasMap.IsOpen && _developerMenu?.IsOpen != true;
		string fallback = fallbackAddress
			? $"requested address unresolved ({requestedRejection}); fallback source " +
			  $"landed at local radius {landing.SearchRadius}"
			: landing.ExactCell
				? "requested cell accepted"
				: $"requested cell rejected ({requestedRejection}); fallback radius {landing.SearchRadius}";
		GD.Print($"[atlas-handoff] request {requestedGlobal.X:0.00},{requestedGlobal.Z:0.00} " +
		         $"=> {landing.GlobalX + .5f:0.00},{landing.GlobalZ + .5f:0.00}," +
		         $"{globalLanding.Y:0.00} via {resolution}; {fallback}; sectors {bounds}");
		if (wilderness is AtlasWildernessDressingStatistics dressed)
			GD.Print($"[atlas-handoff] wilderness {dressed.Trees} trees/{dressed.Boulders} " +
			         $"boulders, manifest {dressed.ManifestHash:x16}");
		if (siteBuilds != null)
			foreach (AtlasReferenceSiteBuild site in siteBuilds)
				GD.Print($"[atlas-handoff] rebuilt site {site.SiteId}: " +
				         $"{site.Statistics.SurfaceCells} surface cells/" +
				         $"{site.Statistics.Voxels} voxel writes");
	}
}
