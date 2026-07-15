using System.Globalization;
using OpenMapUnifier.Germany.Bayern;
using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Grid;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;
using OpenMapUnifier.Germany;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
try
{
    return command switch
    {
        "datasets" => Datasets(args),
        "tiles" => await Tiles(args),
        "download" => await Download(args),
        "height" => await Height(args),
        "profile" => await Profile(args),
        "convert" => Convert_(args),
        "detect" => Detect(args),
        "import-json" => ImportJson(args),
        "scene" => await Scene(args),
        "proxy-test" => await ProxyTest(args),
        _ => Help(),
    };
}
catch (Exception e)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    return 1;
}

static int Help()
{
    Console.WriteLine("""
        OpenMapUnifier .NET — German OpenData downloader & terrain queries

        States (--state CODE, default by):
          by Bayern           ni Niedersachsen     nw Nordrhein-Westfalen  he Hessen
          bw Baden-Württemb.  rp Rheinland-Pfalz   sl Saarland             th Thüringen
          sh Schleswig-Holst. hh Hamburg           hb Bremen               st Sachsen-Anhalt
          be Berlin*          bb Brandenburg*      sn Sachsen*             mv Mecklenb.-Vorp.*
          (* = EPSG:25833 / UTM zone 33 — bbox and easting/northing in that CRS)

        Usage:
          openmap datasets [--state CODE]
          openmap tiles <dataset> --bbox <minE,minN,maxE,maxN> [--state CODE]
          openmap download <dataset> --bbox <minE,minN,maxE,maxN> [--state CODE]
                   [--out DIR] [--parallel N] [--sidecars]
          openmap height <easting> <northing> [--state CODE] [--dataset ID] [--cache DIR]
          openmap height --latlon <lat> <lon> [...]      (lat/lon works for every state)
          openmap profile <fromE> <fromN> <toE> <toN> [--state CODE] [--samples N] [--cache DIR]
          openmap convert --to-utm <lat> <lon> | --to-latlon <easting> <northing> [--zone 32|33]
          openmap convert <x> <y> --from <epsg> --to <epsg>
          openmap detect <a> <b>            what CRS is this pair? ranked guesses + all conversions
          openmap import-json <file> [--to <epsg>] [--out <file.geojson>]
                   [--assume-epsg <epsg>] [--min-confidence 0.6] [--region germany|central-europe]
                                            recover coordinates from messy JSON, any format mix
          openmap scene <scene.json>        run a MapScene document: map + trajectory + sensor
                                            -> coverage GeoTIFFs, area masks, Unity point exports
                                            (format: docs/mapscene.md)
          openmap proxy-test [proxy options]

        Known EPSG codes: 4326, 25832/25833 (ETRS89 UTM), 32632/32633 (WGS84 UTM),
          4647/5650 (zone-prefixed UTM), 3857 (Web Mercator), 31466-31469 (Gauß-Krüger 2-5)
        Axis order for --from/--to 4326 is lon lat (GIS x,y convention);
        unsure what your numbers are? -> openmap detect <a> <b>

        Elevation datasets: dgm1 everywhere (default); surface models where open:
          by: dgm5, dom20 | ni/he/th/mv/be/sn/st/bw/rp/sl/hb/hh: dom1 | bb: bdom

        Proxy options (all commands; mirror the Python Unifier's proxy manager):
          --proxy URL             explicit proxy, e.g. http://proxy.company.com:8080
                                  (omit to auto-use HTTPS_PROXY/HTTP_PROXY env vars)
          --proxy-user U --proxy-pass P     Basic auth credentials
          --proxy-domain DOMAIN   switches auth to NTLM with this Windows domain
          --no-proxy LIST         comma-separated bypass hosts
          --ca-bundle FILE.pem    custom CA bundle (TLS-inspecting proxies)
          --no-ssl-verify         disable TLS verification (dev only)

        Examples:
          openmap height --latlon 48.137222 11.575556                 # Munich (Bayern)
          openmap height --latlon 52.52 13.405 --state be             # Berlin (zone 33)
          openmap height --latlon 50.9375 6.9603 --state nw           # Köln
          openmap download dgm1 --state ni --bbox 550000,5802000,551000,5803000

        Data: © the respective state survey agencies — CC BY 4.0 / DL-DE-BY 2.0 / DL-DE Zero
        (run `openmap datasets --state CODE` for each state's license and products)
        """);
    return 0;
}

static int Datasets(string[] args)
{
    if (GetOption(args, "--state") is { } code)
    {
        var state = GermanStates.Get(code);
        Console.WriteLine($"{state.Name} — EPSG:258{state.UtmZone} — {state.License} — {state.Attribution}");
        foreach (var (id, description) in state.Datasets)
            Console.WriteLine($"  {id,-12} {description}");
        return 0;
    }

    foreach (var state in GermanStates.All.Values.OrderBy(s => s.Code, StringComparer.Ordinal))
        Console.WriteLine($"{state.Code,-3} {state.Name,-22} — {state.License,-16} ({string.Join(", ", state.Datasets.Keys)})");
    Console.WriteLine();
    Console.WriteLine("Details per state: openmap datasets --state CODE");
    return 0;
}

static async Task<int> Tiles(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));

    var jobs = await ResolveJobs(args, dataset, bbox);
    foreach (var job in jobs)
        Console.WriteLine($"{job.FileName}  {job.Mirrors[0]}");
    Console.WriteLine($"{jobs.Count} downloads.");
    return 0;
}

static async Task<IReadOnlyList<DownloadJob>> ResolveJobs(string[] args, string dataset, BoundingBox bbox) =>
    await GermanStates.Get(StateCode(args)).JobsForAsync(dataset, bbox);

static async Task<int> Download(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));
    var outDir = GetOption(args, "--out") ?? "downloads";
    var parallel = int.Parse(GetOption(args, "--parallel") ?? "4", CultureInfo.InvariantCulture);
    var sidecars = args.Contains("--sidecars");
    var proxy = ParseProxy(args);

    var jobs = await ResolveJobs(args, dataset, bbox);
    var state = GermanStates.Get(StateCode(args));
    // Sidecars only make sense for Bayern's grid-named GeoTIFFs (every other
    // state's rasters carry internal georeferencing).
    double? pixelSize = state.Code == "by" ? BayernCatalog.Instance[dataset].PixelSizeMeters : null;

    Console.WriteLine($"Downloading {jobs.Count} {dataset} files to {outDir} ...");
    using var downloader = new HttpTileDownloader(new DownloaderOptions { MaxParallel = parallel, Proxy = proxy });
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Status is "Completed" or "Skipped (exists)")
            Console.WriteLine($"  {p.FileName}: {p.Status}");
    });
    var results = await downloader.DownloadAllAsync(jobs, outDir, progress);

    var ok = results.Count(r => r.Success);
    foreach (var r in results.Where(r => !r.Success))
        Console.Error.WriteLine($"  FAILED {r.Job.FileName}: {r.Error}");

    if (sidecars && pixelSize is { } px)
        foreach (var r in results.Where(r => r.Success && r.LocalPath is not null))
            WorldFile.WriteSidecarsForBayernTile(r.LocalPath!, px);

    Console.WriteLine($"{ok}/{results.Count} tiles OK.");
    Console.WriteLine(state.Attribution);
    return ok == results.Count ? 0 : 1;
}

static async Task<int> Height(string[] args)
{
    var code = StateCode(args);
    var dataset = GetOption(args, "--dataset") ?? "dgm1";
    var cache = GetOption(args, "--cache") ?? "tilecache";
    var proxy = ParseProxy(args);

    using var provider = CreateProvider(code, dataset, cache, proxy);
    var point = ParsePosition(args, provider.Transform);
    var elevation = await provider.GetElevationAsync(point);
    var geo = provider.Transform.ToGeo(point);
    Console.WriteLine($"Position: {point}  ({geo})  [zone {((Etrs89UtmTransform)provider.Transform).Zone}]");
    Console.WriteLine(elevation is null
        ? $"Elevation: no data (outside coverage of '{code}' or NoData cell)"
        : FormattableString.Invariant($"Elevation: {elevation:F2} m ({dataset}, {code})"));

    var slopeAspect = await provider.GetSlopeAspectAsync(point);
    if (slopeAspect is { } sa)
        Console.WriteLine(FormattableString.Invariant(
            $"Slope: {sa.SlopeDegrees:F1} deg, aspect {sa.AspectDegrees:F0} deg from N"));
    return elevation is null ? 1 : 0;
}

static async Task<int> Profile(string[] args)
{
    if (args.Length < 5) throw new ArgumentException("profile needs <fromE> <fromN> <toE> <toN>.");
    var from = new UtmPoint(ParseDouble(args[1]), ParseDouble(args[2]));
    var to = new UtmPoint(ParseDouble(args[3]), ParseDouble(args[4]));
    var samples = int.Parse(GetOption(args, "--samples") ?? "50", CultureInfo.InvariantCulture);
    var cache = GetOption(args, "--cache") ?? "tilecache";

    using var provider = CreateProvider(StateCode(args), "dgm1", cache, ParseProxy(args));
    var profile = await provider.GetProfileAsync(from, to, samples);
    var distance = 0.0;
    UtmPoint? prev = null;
    foreach (var (p, h) in profile)
    {
        if (prev is { } q) distance += q.DistanceTo(p);
        Console.WriteLine(FormattableString.Invariant(
            $"{distance,8:F1} m  {p}  {(h is null ? "no data" : $"{h:F2} m")}"));
        prev = p;
    }
    return 0;
}

static int Convert_(string[] args)
{
    // General EPSG-to-EPSG conversion via the registry.
    if (GetOption(args, "--from") is { } fromText && GetOption(args, "--to") is { } toText)
    {
        if (args.Length < 3) throw new ArgumentException("convert needs <x> <y> before --from/--to.");
        var (x, y) = CrsRegistry.Convert(ParseDouble(args[1]), ParseDouble(args[2]),
            int.Parse(fromText, CultureInfo.InvariantCulture), int.Parse(toText, CultureInfo.InvariantCulture));
        Console.WriteLine(FormattableString.Invariant($"{x:F4} {y:F4}"));
        return 0;
    }

    var zone = int.Parse(GetOption(args, "--zone") ?? "32", CultureInfo.InvariantCulture);
    var t = Etrs89UtmTransform.ForZone(zone);
    if (GetOptionIndex(args, "--to-utm") is { } i && args.Length > i + 2)
    {
        var geo = new GeoPoint(ParseDouble(args[i + 1]), ParseDouble(args[i + 2]));
        Console.WriteLine(t.ToUtm(geo).ToString());
        return 0;
    }
    if (GetOptionIndex(args, "--to-latlon") is { } j && args.Length > j + 2)
    {
        var utm = new UtmPoint(ParseDouble(args[j + 1]), ParseDouble(args[j + 2]));
        Console.WriteLine(t.ToGeo(utm).ToString());
        return 0;
    }
    throw new ArgumentException(
        "Use: convert --to-utm <lat> <lon> | convert --to-latlon <E> <N> | convert <x> <y> --from <epsg> --to <epsg>.");
}

static int Detect(string[] args)
{
    if (args.Length < 3) throw new ArgumentException("detect needs two numbers: detect <a> <b>.");
    var a = ParseDouble(args[1]);
    var b = ParseDouble(args[2]);

    var guesses = CoordinateDetector.Detect(a, b);
    if (guesses.Count == 0)
    {
        Console.WriteLine("No plausible interpretation — values match no known German CRS range.");
        return 1;
    }

    Console.WriteLine($"Interpretations for ({a}, {b}), most likely first:");
    foreach (var guess in guesses)
        Console.WriteLine($"  {guess}");

    var best = guesses[0];
    Console.WriteLine();
    Console.WriteLine($"Best guess EPSG:{best.Epsg} — the same position in every known CRS:");
    Console.WriteLine(FormattableString.Invariant(
        $"  {"lat/lon (EPSG:4326)",-34} {best.Geo.Latitude:F7}, {best.Geo.Longitude:F7}"));
    Console.WriteLine($"  {"DMS",-34} {Dms.Format(best.Geo)}");
    foreach (var epsg in new[] { 25832, 25833, 32632, 4647, 3857, 31468 })
    {
        var (x, y) = CrsRegistry.FromGeo(epsg, best.Geo);
        Console.WriteLine(FormattableString.Invariant(
            $"  {CrsRegistry.Get(epsg).Name + $" (EPSG:{epsg})",-34} {x:F3}, {y:F3}"));
    }
    return 0;
}

static int ImportJson(string[] args)
{
    var file = Require(args, 1, "JSON file path");
    var targetEpsg = int.Parse(GetOption(args, "--to") ?? "25832", CultureInfo.InvariantCulture);

    var options = new OpenMapUnifier.Import.ImportOptions();
    if (GetOption(args, "--assume-epsg") is { } assume)
        options.AssumeEpsg = int.Parse(assume, CultureInfo.InvariantCulture);
    if (GetOption(args, "--min-confidence") is { } minConf)
        options.MinConfidence = ParseDouble(minConf);
    if (GetOption(args, "--region") is { } region)
        options.Region = region.ToLowerInvariant() switch
        {
            "germany" => DetectionRegion.Germany,
            "central-europe" => DetectionRegion.CentralEurope,
            _ => throw new ArgumentException("--region must be germany or central-europe."),
        };

    var found = OpenMapUnifier.Import.ChaoticJsonImporter.ScanFile(file, options);
    if (found.Count == 0)
    {
        Console.WriteLine("No coordinates recognized in this JSON.");
        return 1;
    }

    Console.WriteLine($"{found.Count} coordinate(s) found; converted to EPSG:{targetEpsg}:");
    foreach (var f in found)
    {
        var (x, y) = f.In(targetEpsg);
        var zText = f.Z is { } z ? FormattableString.Invariant($"  z={z:F2}") : "";
        Console.WriteLine(FormattableString.Invariant(
            $"  {f.Path,-40} {x,12:F2} {y,12:F2}{zText}  [EPSG:{f.Guess.Epsg} {f.Guess.Confidence:P0}] {f.Guess.Reason}"));
        if (f.Guess.Confidence < 0.6)
            Console.WriteLine($"  {"",40} ^ LOW CONFIDENCE — verify this one manually");
    }

    if (GetOption(args, "--out") is { } outPath)
    {
        OpenMapUnifier.Import.NormalizedGeoJson.WriteFile(outPath, found, targetEpsg);
        Console.WriteLine($"Normalized GeoJSON written to {outPath}");
    }
    return 0;
}

static async Task<int> Scene(string[] args)
{
    var file = Require(args, 1, "scene JSON file");
    var doc = OpenMapUnifier.MapScene.Scene.SceneDocument.Load(file);
    var baseDir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".";
    var written = await OpenMapUnifier.MapScene.Scene.SceneRunner.RunAsync(
        doc, baseDir, Console.WriteLine);
    Console.WriteLine($"{written.Count} output file(s) written.");
    return 0;
}

static async Task<int> ProxyTest(string[] args)
{
    var proxy = ParseProxy(args) ?? new ProxyManager();
    if (!proxy.Config.Enabled)
    {
        proxy.AutoDetect();
        Console.WriteLine(proxy.LastDetectMessage);
    }
    Console.WriteLine(proxy.Diagnose());
    var results = await proxy.TestConnectionsAsync();
    var allOk = true;
    foreach (var (label, (ok, message)) in results)
    {
        Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {label}: {message}");
        allOk &= ok;
    }
    return allOk ? 0 : 1;
}

static TiledElevationProvider CreateProvider(string code, string dataset, string cache, ProxyManager? proxy)
{
    var downloader = new HttpTileDownloader(new DownloaderOptions { Proxy = proxy });
    return GermanStates.Get(code).CreateElevationProvider(dataset, cache, downloader, proxy);
}

static string StateCode(string[] args)
{
    var code = (GetOption(args, "--state") ?? "by").ToLowerInvariant();
    if (!GermanStates.All.ContainsKey(code))
        throw new ArgumentException(
            $"Unknown state '{code}'. Known: {string.Join(", ", GermanStates.All.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
    return code;
}

static ProxyManager? ParseProxy(string[] args)
{
    var url = GetOption(args, "--proxy");
    var caBundle = GetOption(args, "--ca-bundle");
    var noSslVerify = args.Contains("--no-ssl-verify");
    if (url is null && caBundle is null && !noSslVerify)
        return null; // default: HttpClient's env-based proxy behavior

    var manager = new ProxyManager();
    if (url is not null)
    {
        var user = GetOption(args, "--proxy-user") ?? "";
        var pass = GetOption(args, "--proxy-pass") ?? "";
        var domain = GetOption(args, "--proxy-domain") ?? "";
        var auth = user.Length == 0 ? ProxyAuthType.None
            : domain.Length > 0 ? ProxyAuthType.Ntlm : ProxyAuthType.Basic;
        manager.SetManualProxy(url, auth, user, pass, domain);
        if (GetOption(args, "--no-proxy") is { } bypass)
            manager.Config.NoProxy = bypass;
    }
    manager.SetSsl(!noSslVerify, caBundle ?? "");
    return manager;
}

static UtmPoint ParsePosition(string[] args, ICoordinateTransform transform)
{
    if (GetOptionIndex(args, "--latlon") is { } i && args.Length > i + 2)
    {
        var geo = new GeoPoint(ParseDouble(args[i + 1]), ParseDouble(args[i + 2]));
        return transform.ToUtm(geo);
    }
    if (args.Length < 3) throw new ArgumentException("height needs <easting> <northing> or --latlon <lat> <lon>.");
    return new UtmPoint(ParseDouble(args[1]), ParseDouble(args[2]));
}

static BoundingBox ParseBbox(string s)
{
    var parts = s.Split(',');
    if (parts.Length != 4) throw new ArgumentException("--bbox expects minE,minN,maxE,maxN.");
    return new BoundingBox(
        ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2]), ParseDouble(parts[3]));
}

static double ParseDouble(string s) => double.Parse(s, CultureInfo.InvariantCulture);

static string Require(string[] args, int index, string what) =>
    args.Length > index ? args[index] : throw new ArgumentException($"Missing {what}.");

static string? GetOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int? GetOptionIndex(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 ? i : null;
}
