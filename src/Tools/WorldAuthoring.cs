using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Petalfell.World;

namespace Petalfell.Tools;

/// <summary>
/// Fast authoring entrypoint. It deliberately exits before Planner/Terrain are
/// constructed: topology edits need a sub-second feedback loop, not a continent
/// generation followed by a report about a typo.
/// </summary>
public static class WorldAuthoring
{
	public static bool TryRun(Node owner, string defaultMapPath)
	{
		var args = OS.GetCmdlineUserArgs();
		bool audit = false;
		string preview = null;
		string atlasPreview = null;
		string domainId = null;
		string compileSector = null;
		string verifySector = null;
		string sectorOutput = null;
		string sectorPreview = null;
		int sectorApron = AtlasSectorCompiler.DefaultApron;
		string mapPath = defaultMapPath;

		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--world-audit") audit = true;
			else if (args[i] == "--world-preview" && i + 1 < args.Length) preview = args[++i];
			else if (args[i].StartsWith("--world-preview=")) preview = args[i][16..];
			else if (args[i] == "--atlas-preview" && i + 1 < args.Length) atlasPreview = args[++i];
			else if (args[i].StartsWith("--atlas-preview=")) atlasPreview = args[i][16..];
			else if (args[i] == "--world-domain" && i + 1 < args.Length) domainId = args[++i];
			else if (args[i].StartsWith("--world-domain=")) domainId = args[i][15..];
			else if (args[i] == "--compile-sector" && i + 1 < args.Length) compileSector = args[++i];
			else if (args[i].StartsWith("--compile-sector=")) compileSector = args[i][17..];
			else if (args[i] == "--verify-sector" && i + 1 < args.Length) verifySector = args[++i];
			else if (args[i].StartsWith("--verify-sector=")) verifySector = args[i][16..];
			else if (args[i] == "--sector-output" && i + 1 < args.Length) sectorOutput = args[++i];
			else if (args[i].StartsWith("--sector-output=")) sectorOutput = args[i][16..];
			else if (args[i] == "--sector-preview" && i + 1 < args.Length) sectorPreview = args[++i];
			else if (args[i].StartsWith("--sector-preview=")) sectorPreview = args[i][17..];
			else if (args[i] == "--sector-apron" && i + 1 < args.Length) sectorApron = int.Parse(args[++i], CultureInfo.InvariantCulture);
			else if (args[i].StartsWith("--sector-apron=")) sectorApron = int.Parse(args[i][15..], CultureInfo.InvariantCulture);
			else if (args[i] == "--map-definition" && i + 1 < args.Length) mapPath = args[++i];
			else if (args[i].StartsWith("--map-definition=")) mapPath = args[i][17..];
		}

		if (!audit && preview == null && atlasPreview == null && compileSector == null && verifySector == null) return false;
		int exit = 0;
		try
		{
			var map = MapDefinition.Load(mapPath);
			if (map.CanonicalAtlas != null)
			{
				var atlasReport = map.CanonicalAtlas.Audit();
				GD.Print(atlasReport.Format(map.CanonicalAtlasPath));
				GD.Print($"[atlas-audit] version {map.CanonicalAtlas.Version}  " +
				         $"atlas {map.CanonicalAtlas.Width}x{map.CanonicalAtlas.Depth}x{map.CanonicalAtlas.Height}  " +
				         $"sectors {map.CanonicalAtlas.Width / map.CanonicalAtlas.SectorSize}x{map.CanonicalAtlas.Depth / map.CanonicalAtlas.SectorSize}  " +
				         $"provinces {map.CanonicalAtlas.Provinces.Count}  profiles {map.CanonicalAtlas.BiomeCatalog?.Profiles.Count ?? 0}");
				if (!atlasReport.Valid) exit = 2;
				if (atlasReport.Valid && atlasPreview != null)
				{
					WriteAtlasSvg(map.CanonicalAtlas, atlasPreview);
					GD.Print($"[atlas-preview] {ProjectSettings.GlobalizePath(atlasPreview)}");
				}
				if (atlasReport.Valid && (compileSector != null || verifySector != null))
				{
					var compiler = new AtlasSectorCompiler(map.CanonicalAtlas, map.DefaultSeed, map.CanonicalAtlasPath);
					if (compileSector != null)
					{
						(int sx, int sz) = ParseSector(compileSector);
						var data = compiler.Compile(sx, sz, sectorApron);
						sectorOutput ??= $"res://content/chapter_01/derived/sector-{sx}-{sz}.pfs";
						sectorPreview ??= $"res://../shots/atlas-sector-{sx}-{sz}.png";
						string artifactHash = compiler.WriteArtifact(data, sectorOutput);
						compiler.WritePreview(data, sectorPreview);
						var stats = data.CoreStatistics();
						GD.Print($"[sector-compile] {sx},{sz} origin {data.OriginX},{data.OriginZ} " +
						         $"window {data.Width}x{data.Depth} apron {data.Apron} " +
						         $"land {stats.LandCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"water {stats.WaterCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"floodplain {stats.FloodplainCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"bank {stats.BankCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"region-blend {stats.BlendedCells * 100f / (data.CoreSize * data.CoreSize):0.0}% " +
						         $"land-height {stats.MinHeight}..{stats.MaxHeight} " +
						         $"water-surface {stats.MinWaterSurface}..{stats.MaxWaterSurface} hash {artifactHash}");
						GD.Print($"[sector-artifact] {ProjectSettings.GlobalizePath(sectorOutput)}");
						GD.Print($"[sector-preview] {ProjectSettings.GlobalizePath(sectorPreview)}");
					}
					if (verifySector != null)
					{
						(int sx, int sz) = ParseSector(verifySector);
						var result = compiler.Verify(sx, sz, sectorApron);
						GD.Print($"[sector-verify] {sx},{sz} repeat hash {result.ContentHash} " +
						         $"east overlap {result.EastOverlapCells} cells south overlap {result.SouthOverlapCells} cells");
					}
				}
			}
			else if (atlasPreview != null)
				throw new InvalidOperationException($"Map '{mapPath}' has no canonicalAtlasPath.");
			if (map.CanonicalWorld == null)
			{
				if (preview != null) throw new InvalidOperationException($"Map '{mapPath}' has no canonicalWorldPath.");
				owner.GetTree().Quit(exit);
				return true;
			}

			var report = map.CanonicalWorld.Audit(map);
			GD.Print(report.Format(map.CanonicalWorldPath));
			GD.Print($"[world-audit] version {map.CanonicalWorld.Version}  world {map.CanonicalWorld.WorldSize}  " +
			         $"domains {map.CanonicalWorld.Domains.Count}  sites {map.CanonicalWorld.Sites.Count}  " +
			         $"nodes {map.CanonicalWorld.RouteNodes.Count}  routes {map.CanonicalWorld.Routes.Count}");
			if (!report.Valid) exit = 2;
			if (report.Valid && preview != null)
			{
				WriteSvg(map, preview, domainId);
				GD.Print($"[world-preview] {ProjectSettings.GlobalizePath(preview)}");
			}
		}
		catch (Exception ex)
		{
			GD.PushError($"[world-authoring] {ex.Message}");
			exit = 2;
		}

		owner.GetTree().Quit(exit);
		return true;
	}

	private static (int x, int z) ParseSector(string address)
	{
		string[] parts = address.Split(',');
		if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
		    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
			throw new InvalidOperationException($"sector address '{address}' must be x,z");
		return (x, z);
	}

	public static void WriteAtlasSvg(WorldAtlasDefinition atlas, string outputPath)
	{
		const int canvasW = 1600, canvasH = 1260, pad = 60, header = 105;
		float scale = Math.Min((canvasW - pad * 2f) / atlas.Width,
			(canvasH - header - pad) / (float)atlas.Depth);
		float mapW = atlas.Width * scale, mapH = atlas.Depth * scale;
		float ox = (canvasW - mapW) * 0.5f, oz = header + (canvasH - header - mapH) * 0.5f;
		string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
		float X(int x) => ox + x * scale;
		float Z(int z) => oz + z * scale;
		string Esc(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
			.Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
		string Points(IEnumerable<BlockPoint> points) =>
			string.Join(" ", points.Select(p => $"{F(X(p.X))},{F(Z(p.Z))}"));

		var svg = new StringBuilder(4_000_000);
		svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{canvasW}\" height=\"{canvasH}\" viewBox=\"0 0 {canvasW} {canvasH}\">");
		svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#191622\"/>");
		svg.AppendLine("<defs><filter id=\"atlasTerrain\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\".55 0 0 0 .10  .55 0 0 0 .16  .50 0 0 0 .24  0 0 0 1 0\"/></filter><filter id=\"atlasWater\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"0 0 0 0 .28  0 0 0 0 .62  0 0 0 0 .90  .2126 .7152 .0722 0 0\"/><feComponentTransfer><feFuncA type=\"gamma\" amplitude=\".88\" exponent=\"2.6\" offset=\"0\"/></feComponentTransfer></filter><filter id=\"atlasRegion\" color-interpolation-filters=\"sRGB\"><feColorMatrix type=\"matrix\" values=\"1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  .2126 .7152 .0722 0 0\"/><feComponentTransfer><feFuncA type=\"linear\" slope=\".5\"/></feComponentTransfer></filter></defs>");
		svg.AppendLine("<style>text{font-family:Inter,system-ui,sans-serif}.label{paint-order:stroke;stroke:#191622;stroke-width:5px;stroke-linejoin:round}.province{font-size:18px;font-weight:750;fill:#fff}.small{font-size:13px;fill:#d8d0df}</style>");

		AtlasSourceLayer elevation = atlas.SourceLayers.FirstOrDefault(l => l.Kind == AtlasLayerKind.Elevation &&
			l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
		string underlayPath = elevation?.Path ?? atlas.PreviewReferencePath;
		if (!string.IsNullOrWhiteSpace(underlayPath) && Godot.FileAccess.FileExists(underlayPath))
		{
			string absoluteReference = ProjectSettings.GlobalizePath(underlayPath);
			string encoded = Convert.ToBase64String(File.ReadAllBytes(absoluteReference));
			string filter = elevation == null ? "" : " filter=\"url(#atlasTerrain)\"";
			svg.AppendLine($"<image x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" preserveAspectRatio=\"none\" opacity=\".88\"{filter} href=\"data:image/png;base64,{encoded}\"/>");
		}
		AtlasSourceLayer water = atlas.SourceLayers.FirstOrDefault(l => l.Kind == AtlasLayerKind.Water &&
			l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
		if (water != null)
		{
			string absoluteWater = ProjectSettings.GlobalizePath(water.Path);
			string encoded = Convert.ToBase64String(File.ReadAllBytes(absoluteWater));
			svg.AppendLine($"<image x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" preserveAspectRatio=\"none\" filter=\"url(#atlasWater)\" href=\"data:image/png;base64,{encoded}\"/>");
		}
		AtlasSourceLayer region = atlas.SourceLayers.FirstOrDefault(l => l.Kind == AtlasLayerKind.Region &&
			l.Status != AtlasLayerStatus.Planned && Godot.FileAccess.FileExists(l.Path));
		if (region != null)
		{
			string absoluteRegion = ProjectSettings.GlobalizePath(region.Path);
			string encoded = Convert.ToBase64String(File.ReadAllBytes(absoluteRegion));
			svg.AppendLine($"<image x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" preserveAspectRatio=\"none\" filter=\"url(#atlasRegion)\" href=\"data:image/png;base64,{encoded}\"/>");
		}

		for (int x = 0; x <= atlas.Width; x += atlas.SectorSize)
		{
			float px = X(x);
			svg.AppendLine($"<line x1=\"{F(px)}\" y1=\"{F(oz)}\" x2=\"{F(px)}\" y2=\"{F(oz + mapH)}\" stroke=\"#fff\" stroke-opacity=\".19\" stroke-width=\"1\"/>");
		}
		for (int z = 0; z <= atlas.Depth; z += atlas.SectorSize)
		{
			float pz = Z(z);
			svg.AppendLine($"<line x1=\"{F(ox)}\" y1=\"{F(pz)}\" x2=\"{F(ox + mapW)}\" y2=\"{F(pz)}\" stroke=\"#fff\" stroke-opacity=\".19\" stroke-width=\"1\"/>");
		}

		foreach (var province in atlas.Provinces)
		{
			string fillOpacity = region == null ? ".16" : "0";
			string guideStyle = region == null
				? "stroke-width=\"4\""
				: "stroke-width=\"2\" stroke-opacity=\".32\" stroke-dasharray=\"8 7\"";
			svg.AppendLine($"<polygon points=\"{Points(province.Boundary)}\" fill=\"{province.PreviewColour}\" fill-opacity=\"{fillOpacity}\" stroke=\"{province.PreviewColour}\" {guideStyle} stroke-linejoin=\"round\"/>");
			float cx = province.Boundary.Average(p => X(p.X));
			float cz = province.Boundary.Average(p => Z(p.Z));
			svg.AppendLine($"<text class=\"province label\" x=\"{F(cx)}\" y=\"{F(cz)}\" text-anchor=\"middle\">{Esc(province.DisplayName)}</text>");
			svg.AppendLine($"<text class=\"small label\" x=\"{F(cx)}\" y=\"{F(cz + 19)}\" text-anchor=\"middle\">{Esc(string.Join(" + ", province.BiomeProfileIds))}</text>");
		}

		svg.AppendLine($"<rect x=\"{F(ox)}\" y=\"{F(oz)}\" width=\"{F(mapW)}\" height=\"{F(mapH)}\" fill=\"none\" stroke=\"#fff7f1\" stroke-opacity=\".65\" stroke-width=\"2\"/>");
		int planned = atlas.SourceLayers.Count(l => l.Status == AtlasLayerStatus.Planned);
		int blockout = atlas.SourceLayers.Count(l => l.Status == AtlasLayerStatus.Blockout);
		int accepted = atlas.SourceLayers.Count(l => l.Status == AtlasLayerStatus.Accepted);
		svg.AppendLine($"<text x=\"{pad}\" y=\"38\" fill=\"#fff7f1\" font-size=\"24\" font-weight=\"750\">{Esc(atlas.DisplayName)} — province and sector blockout</text>");
		svg.AppendLine($"<text x=\"{pad}\" y=\"62\" fill=\"#cfc4d8\" font-size=\"14\">{atlas.Width} × {atlas.Depth} × {atlas.Height} blocks · sea {atlas.SeaLevel} · {atlas.Width / atlas.SectorSize} × {atlas.Depth / atlas.SectorSize} sectors · {atlas.Provinces.Count} provinces · layers {accepted} accepted / {blockout} blockout / {planned} planned</text>");
		string underlayNote = elevation == null
			? "Reference image underlay only; its generated labels, exact coastline and landmark density are not canonical."
			: water == null
				? "Underlay is registered elevation; province polygons remain provisional until the region layer is painted."
				: region == null
					? "Underlay is registered elevation with the water blockout in blue; province polygons remain allocation guides."
					: "Registered elevation, water and categorical region blockouts; polygon outlines are allocation guides only.";
		svg.AppendLine($"<text x=\"{pad}\" y=\"84\" fill=\"#a99db4\" font-size=\"12\">{underlayNote}</text>");
		svg.AppendLine("</svg>");

		string absolute = ProjectSettings.GlobalizePath(outputPath);
		string dir = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(absolute, svg.ToString(), new UTF8Encoding(false));
	}

	public static void WriteSvg(MapDefinition map, string outputPath, string domainId = null)
	{
		var world = map.CanonicalWorld ?? throw new InvalidOperationException("No canonical world loaded.");
		const int canvas = 1400, pad = 70;
		CanonicalDomain focus = null;
		int minX = 0, minZ = 0, maxX = world.WorldSize, maxZ = world.WorldSize;
		if (!string.IsNullOrWhiteSpace(domainId))
		{
			focus = world.Domains.FirstOrDefault(d => d.Id == domainId)
			        ?? throw new InvalidOperationException($"Unknown authored domain '{domainId}'.");
			minX = Math.Max(0, focus.Boundary.Min(p => p.X) - 80);
			minZ = Math.Max(0, focus.Boundary.Min(p => p.Z) - 80);
			maxX = Math.Min(world.WorldSize, focus.Boundary.Max(p => p.X) + 80);
			maxZ = Math.Min(world.WorldSize, focus.Boundary.Max(p => p.Z) + 80);
		}
		float scale = Math.Min((canvas - pad * 2f) / (maxX - minX), (canvas - pad * 2f) / (maxZ - minZ));
		float offsetX = (canvas - (maxX - minX) * scale) * 0.5f;
		float offsetZ = (canvas - (maxZ - minZ) * scale) * 0.5f;
		string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
		float X(int x) => offsetX + (x - minX) * scale;
		float Z(int z) => offsetZ + (z - minZ) * scale;
		string Esc(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;")
			.Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
		string Points(IEnumerable<BlockPoint> points) =>
			string.Join(" ", points.Select(p => $"{F(X(p.X))},{F(Z(p.Z))}"));

		var svg = new StringBuilder(64_000);
		svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{canvas}\" height=\"{canvas}\" viewBox=\"0 0 {canvas} {canvas}\">");
		svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#191622\"/>");
		svg.AppendLine("<style>text{font-family:Inter,system-ui,sans-serif}.label{paint-order:stroke;stroke:#191622;stroke-width:5px;stroke-linejoin:round}.small{font-size:12px;fill:#d8d0df}.site{font-size:14px;font-weight:650;fill:#fff7f1}.domain{font-size:15px;font-weight:700;fill:#d9c7ef;letter-spacing:.03em}</style>");

		float bx = X((int)(map.Boundary.Centre.X * world.WorldSize));
		float bz = Z((int)(map.Boundary.Centre.Z * world.WorldSize));
		float brx = map.Boundary.RadiusX * world.WorldSize * scale;
		float brz = map.Boundary.RadiusZ * world.WorldSize * scale;
		svg.AppendLine($"<ellipse cx=\"{F(bx)}\" cy=\"{F(bz)}\" rx=\"{F(brx)}\" ry=\"{F(brz)}\" fill=\"#546789\" fill-opacity=\".28\" stroke=\"#8da0c8\" stroke-width=\"2\"/>");

		var visibleDomains = focus == null ? world.Domains : new List<CanonicalDomain> { focus };
		foreach (var domain in visibleDomains)
		{
			svg.AppendLine($"<polygon points=\"{Points(domain.Boundary)}\" fill=\"#9c7fc4\" fill-opacity=\".12\" stroke=\"#b99bdb\" stroke-width=\"3\" stroke-dasharray=\"10 6\"/>");
			float dx = domain.Boundary.Average(p => X(p.X));
			float dz = domain.Boundary.Min(p => Z(p.Z)) - 10;
			svg.AppendLine($"<text class=\"domain label\" x=\"{F(dx)}\" y=\"{F(dz)}\" text-anchor=\"middle\">{Esc(domain.DisplayName)}</text>");
		}

		string RouteColour(RoadKind kind) => kind switch
		{
			RoadKind.Major => "#fff0dc",
			RoadKind.Local or RoadKind.Street => "#e7bfc9",
			RoadKind.Abandoned => "#bd8f9c",
			_ => "#b78878",
		};
		var visibleSiteIds = new HashSet<string>((focus == null ? world.Sites : world.Sites.Where(s => s.DomainId == focus.Id)).Select(s => s.Id), StringComparer.Ordinal);
		var visibleNodeIds = new HashSet<string>(world.RouteNodes.Where(n => focus == null ||
			visibleSiteIds.Contains(n.SiteId) || (n.Point.X >= minX && n.Point.X <= maxX && n.Point.Z >= minZ && n.Point.Z <= maxZ)).Select(n => n.Id), StringComparer.Ordinal);
		var visibleRoutes = world.Routes.Where(route => focus == null || visibleNodeIds.Contains(route.FromNodeId) || visibleNodeIds.Contains(route.ToNodeId)).ToList();
		foreach (var route in visibleRoutes)
		{
			float width = Math.Clamp(route.Width * scale, 1.5f, 8f);
			svg.AppendLine($"<polyline points=\"{Points(route.Points)}\" fill=\"none\" stroke=\"#191622\" stroke-width=\"{F(width + 3f)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
			svg.AppendLine($"<polyline points=\"{Points(route.Points)}\" fill=\"none\" stroke=\"{RouteColour(route.Kind)}\" stroke-width=\"{F(width)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
		}

		string SiteColour(SiteTier tier) => tier switch
		{
			SiteTier.GreatWork => "#ffe08a",
			SiteTier.District => "#ffb9a6",
			SiteTier.Precinct => "#d9b8ef",
			_ => "#a9dbd4",
		};
		var visibleSites = world.Sites.Where(site => focus == null || site.DomainId == focus.Id).ToList();
		foreach (var site in visibleSites)
		{
			float cx = X(site.Centre.X), cz = Z(site.Centre.Z);
			float w = Math.Max(8f, site.ExtentX * scale), h = Math.Max(8f, site.ExtentZ * scale);
			string colour = SiteColour(site.Tier);
			svg.AppendLine($"<rect x=\"{F(cx - w / 2)}\" y=\"{F(cz - h / 2)}\" width=\"{F(w)}\" height=\"{F(h)}\" rx=\"5\" fill=\"{colour}\" fill-opacity=\".18\" stroke=\"{colour}\" stroke-width=\"3\" transform=\"rotate({F(site.OrientationDegrees)} {F(cx)} {F(cz)})\"/>");
			float length = Math.Max(12f, Math.Min(w, h) * .38f);
			float rad = site.OrientationDegrees * MathF.PI / 180f;
			svg.AppendLine($"<line x1=\"{F(cx)}\" y1=\"{F(cz)}\" x2=\"{F(cx + MathF.Sin(rad) * length)}\" y2=\"{F(cz + MathF.Cos(rad) * length)}\" stroke=\"{colour}\" stroke-width=\"3\"/>");
			svg.AppendLine($"<circle cx=\"{F(cx)}\" cy=\"{F(cz)}\" r=\"5\" fill=\"{colour}\" stroke=\"#191622\" stroke-width=\"2\"/>");
			if (focus != null || site.Tier is SiteTier.District or SiteTier.GreatWork)
			{
				svg.AppendLine($"<text class=\"site label\" x=\"{F(cx + 9)}\" y=\"{F(cz - 9)}\">{Esc(site.DisplayName)}</text>");
				svg.AppendLine($"<text class=\"small label\" x=\"{F(cx + 9)}\" y=\"{F(cz + 7)}\">{Esc(site.Tier.ToString())} · {site.Centre.X},{site.Centre.Z}</text>");
			}
		}

		foreach (var node in world.RouteNodes.Where(n => visibleNodeIds.Contains(n.Id)))
			svg.AppendLine($"<circle cx=\"{F(X(node.Point.X))}\" cy=\"{F(Z(node.Point.Z))}\" r=\"3\" fill=\"#fff\" stroke=\"#191622\" stroke-width=\"1.5\"/>");

		string scope = focus == null ? "authored topology" : $"{focus.DisplayName} detail";
		svg.AppendLine($"<text x=\"{pad}\" y=\"35\" fill=\"#fff7f1\" font-size=\"22\" font-weight=\"750\">{Esc(map.DisplayName)} — {Esc(scope)}</text>");
		svg.AppendLine($"<text x=\"{pad}\" y=\"56\" fill=\"#bdb2c8\" font-size=\"13\">world {world.WorldSize} · version {world.Version} · {visibleDomains.Count} domains · {visibleSites.Count} sites · {visibleRoutes.Count} routes</text>");
		svg.AppendLine("</svg>");

		string absolute = ProjectSettings.GlobalizePath(outputPath);
		string dir = Path.GetDirectoryName(absolute);
		if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
		File.WriteAllText(absolute, svg.ToString(), new UTF8Encoding(false));
	}
}
