using System.Globalization;
using OpenMapUnifier.Bayern;
using OpenMapUnifier.Core.Catalog;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;
using OpenMapUnifier.Germany;
using OpenMapUnifier.Niedersachsen;

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
          openmap proxy-test [proxy options]

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
        if (code.Equals("by", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var d in BayernCatalog.Instance.Datasets.Values)
                Console.WriteLine($"  {d.Id,-12} [{d.Category}] {d.Label}");
        }
        else if (code.Equals("ni", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var d in NiedersachsenCatalog.Datasets.Values)
                Console.WriteLine($"  {d.Id,-12} [{d.Category}] {d.Label}");
        }
        else
        {
            var state = GermanStates.Get(code);
            Console.WriteLine($"{state.Name} — EPSG:258{state.UtmZone} — {state.License} — {state.Attribution}");
            foreach (var (id, description) in state.Datasets)
                Console.WriteLine($"  {id,-12} {description}");
        }
        return 0;
    }

    Console.WriteLine("by  Bayern             — CC BY 4.0        (dgm1, dgm5, dom20, dop20/40, lod2, laser, WMS)");
    Console.WriteLine("ni  Niedersachsen      — CC BY 4.0        (dgm1, dom1, dop20rgb/rgbi via STAC)");
    foreach (var state in GermanStates.All.Values.OrderBy(s => s.Code, StringComparer.Ordinal))
        Console.WriteLine($"{state.Code,-3} {state.Name,-18} — {state.License,-16} ({string.Join(", ", state.Datasets.Keys)})");
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

static async Task<IReadOnlyList<DownloadJob>> ResolveJobs(string[] args, string dataset, BoundingBox bbox)
{
    var code = StateCode(args);
    switch (code)
    {
        case "by":
            var info = BayernCatalog.Instance[dataset];
            return (info.Kind == DatasetKind.Wms
                ? new BayernWmsSource().JobsFor(dataset, bbox)
                : new BayernTileSource().JobsFor(dataset, bbox)).ToList();
        case "ni":
            using (var source = new NiedersachsenTileSource(new StacClient(proxy: ParseProxy(args))))
                return await source.JobsForAsync(dataset, bbox);
        default:
            return await GermanStates.Get(code).JobsForAsync(dataset, bbox);
    }
}

static string AttributionFor(string code) => code switch
{
    "by" => BayernCatalog.Attribution,
    "ni" => NiedersachsenCatalog.Attribution,
    _ => GermanStates.Get(code).Attribution,
};

static async Task<int> Download(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));
    var outDir = GetOption(args, "--out") ?? "downloads";
    var parallel = int.Parse(GetOption(args, "--parallel") ?? "4", CultureInfo.InvariantCulture);
    var sidecars = args.Contains("--sidecars");
    var proxy = ParseProxy(args);

    var jobs = await ResolveJobs(args, dataset, bbox);
    var code = StateCode(args);
    var attribution = AttributionFor(code);
    double? pixelSize = code == "by" ? BayernCatalog.Instance[dataset].PixelSizeMeters : null;

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
    Console.WriteLine(attribution);
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
    throw new ArgumentException("Use: convert --to-utm <lat> <lon>  OR  convert --to-latlon <E> <N>.");
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
    return (code, dataset.ToLowerInvariant()) switch
    {
        ("ni", "dgm1") => NiedersachsenElevation.CreateDgm1Provider(cache, downloader, proxy),
        ("ni", "dom1") => NiedersachsenElevation.CreateDom1Provider(cache, downloader, proxy),
        ("by", "dgm1") => BayernElevation.CreateDgm1Provider(cache, downloader),
        ("by", "dgm5") => BayernElevation.CreateDgm5Provider(cache, downloader),
        ("by", "dom20") => BayernElevation.CreateDom20Provider(cache, downloader),
        ("by" or "ni", _) => throw new ArgumentException(
            $"'{dataset}' is not an elevation dataset for '{code}' (by: dgm1/dgm5/dom20, ni: dgm1/dom1)."),
        _ => GermanStates.Get(code).CreateElevationProvider(dataset, cache, downloader, proxy),
    };
}

static string StateCode(string[] args)
{
    var code = (GetOption(args, "--state") ?? "by").ToLowerInvariant();
    if (code is "bayern") code = "by";
    if (code is "niedersachsen") code = "ni";
    if (code is not ("by" or "ni") && !GermanStates.All.ContainsKey(code))
        throw new ArgumentException(
            $"Unknown state '{code}'. Known: by, ni, {string.Join(", ", GermanStates.All.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
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
