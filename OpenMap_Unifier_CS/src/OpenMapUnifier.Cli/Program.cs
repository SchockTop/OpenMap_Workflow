using System.Globalization;
using OpenMapUnifier.Bayern;
using OpenMapUnifier.Core.Catalog;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;
using OpenMapUnifier.Niedersachsen;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
try
{
    return command switch
    {
        "datasets" => Datasets(),
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
        OpenMapUnifier .NET — OpenData downloader & terrain queries (EPSG:25832)
        States: --state by (Bayern LDBV, default) | --state ni (Niedersachsen LGLN)

        Usage:
          openmap datasets
          openmap tiles <dataset> --bbox <minE,minN,maxE,maxN> [--state by|ni]
          openmap download <dataset> --bbox <minE,minN,maxE,maxN> [--state by|ni]
                   [--out DIR] [--parallel N] [--sidecars]
          openmap height <easting> <northing> [--state by|ni] [--dataset ID] [--cache DIR]
          openmap height --latlon <lat> <lon> [...]
          openmap profile <fromE> <fromN> <toE> <toN> [--state by|ni] [--samples N] [--cache DIR]
          openmap convert --to-utm <lat> <lon> | --to-latlon <easting> <northing>
          openmap proxy-test [proxy options]

        Elevation datasets: by: dgm1 (default), dgm5, dom20 | ni: dgm1 (default), dom1

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
          openmap height --latlon 52.374 9.738 --state ni             # Hannover (Niedersachsen)
          openmap download dgm1 --state ni --bbox 550000,5802000,551000,5803000

        Data: Bayerische Vermessungsverwaltung & LGLN Niedersachsen — CC BY 4.0
        """);
    return 0;
}

static int Datasets()
{
    Console.WriteLine("Bayern (--state by):");
    foreach (var d in BayernCatalog.Instance.Datasets.Values)
        Console.WriteLine($"  {d.Id,-12} [{d.Category}] {d.Label}");
    Console.WriteLine();
    Console.WriteLine("Niedersachsen (--state ni):");
    foreach (var d in NiedersachsenCatalog.Datasets.Values)
        Console.WriteLine($"  {d.Id,-12} [{d.Category}] {d.Label}");
    return 0;
}

static async Task<int> Tiles(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));

    if (IsNiedersachsen(args))
    {
        using var source = new NiedersachsenTileSource(new StacClient(proxy: ParseProxy(args)));
        var jobs = await source.JobsForAsync(dataset, bbox);
        foreach (var job in jobs)
            Console.WriteLine($"{job.FileName}  {job.Mirrors[0]}");
        Console.WriteLine($"{jobs.Count} tiles (newest flight per tile, via STAC)");
    }
    else
    {
        var source = new BayernTileSource();
        var jobs = source.JobsFor(dataset, bbox).ToList();
        foreach (var job in jobs)
            Console.WriteLine($"{job.FileName}  {job.Mirrors[0]}");
        Console.WriteLine($"{jobs.Count} tiles, ~{source.EstimateSizeMb(dataset, bbox):F0} MB");
    }
    return 0;
}

static async Task<int> Download(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));
    var outDir = GetOption(args, "--out") ?? "downloads";
    var parallel = int.Parse(GetOption(args, "--parallel") ?? "4", CultureInfo.InvariantCulture);
    var sidecars = args.Contains("--sidecars");
    var proxy = ParseProxy(args);

    IReadOnlyList<DownloadJob> jobs;
    double? pixelSize;
    string attribution;
    if (IsNiedersachsen(args))
    {
        using var source = new NiedersachsenTileSource(new StacClient(proxy: proxy));
        jobs = await source.JobsForAsync(dataset, bbox);
        pixelSize = NiedersachsenCatalog.Get(dataset).PixelSizeMeters;
        attribution = NiedersachsenCatalog.Attribution;
    }
    else
    {
        var info = BayernCatalog.Instance[dataset];
        jobs = (info.Kind == DatasetKind.Wms
            ? new BayernWmsSource().JobsFor(dataset, bbox)
            : new BayernTileSource().JobsFor(dataset, bbox)).ToList();
        pixelSize = info.PixelSizeMeters;
        attribution = BayernCatalog.Attribution;
    }

    Console.WriteLine($"Downloading {jobs.Count} {dataset} tiles to {outDir} ...");
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
    var point = ParsePosition(args);
    var ni = IsNiedersachsen(args);
    var dataset = GetOption(args, "--dataset") ?? "dgm1";
    var cache = GetOption(args, "--cache") ?? "tilecache";
    var proxy = ParseProxy(args);

    using var provider = CreateProvider(ni, dataset, cache, proxy);
    var elevation = await provider.GetElevationAsync(point);
    var geo = Etrs89Utm32Transform.Instance.ToGeo(point);
    Console.WriteLine($"Position: {point}  ({geo})");
    Console.WriteLine(elevation is null
        ? $"Elevation: no data (outside {(ni ? "Niedersachsen" : "Bavaria")} or NoData cell)"
        : FormattableString.Invariant($"Elevation: {elevation:F2} m ({dataset}, {(ni ? "ni" : "by")})"));

    var slopeAspect = await provider.GetSlopeAspectAsync(point);
    if (slopeAspect is { } sa)
        Console.WriteLine(FormattableString.Invariant(
            $"Slope: {sa.SlopeDegrees:F1} deg, aspect {sa.AspectDegrees:F0} deg from N"));
    return elevation is null ? 1 : 0;
}

static async Task<int> Profile(string[] args)
{
    if (args.Length < 5) throw new ArgumentException("profile needs <fromE> <fromN> <toE> <toN>.");
    var from = new Utm32Point(ParseDouble(args[1]), ParseDouble(args[2]));
    var to = new Utm32Point(ParseDouble(args[3]), ParseDouble(args[4]));
    var samples = int.Parse(GetOption(args, "--samples") ?? "50", CultureInfo.InvariantCulture);
    var cache = GetOption(args, "--cache") ?? "tilecache";

    using var provider = CreateProvider(IsNiedersachsen(args), "dgm1", cache, ParseProxy(args));
    var profile = await provider.GetProfileAsync(from, to, samples);
    var distance = 0.0;
    Utm32Point? prev = null;
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
    var t = Etrs89Utm32Transform.Instance;
    if (GetOptionIndex(args, "--to-utm") is { } i && args.Length > i + 2)
    {
        var geo = new GeoPoint(ParseDouble(args[i + 1]), ParseDouble(args[i + 2]));
        Console.WriteLine(t.ToUtm32(geo).ToString());
        return 0;
    }
    if (GetOptionIndex(args, "--to-latlon") is { } j && args.Length > j + 2)
    {
        var utm = new Utm32Point(ParseDouble(args[j + 1]), ParseDouble(args[j + 2]));
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

static TiledElevationProvider CreateProvider(bool ni, string dataset, string cache, ProxyManager? proxy)
{
    var downloader = new HttpTileDownloader(new DownloaderOptions { Proxy = proxy });
    return (ni, dataset.ToLowerInvariant()) switch
    {
        (true, "dgm1") => NiedersachsenElevation.CreateDgm1Provider(cache, downloader, proxy),
        (true, "dom1") => NiedersachsenElevation.CreateDom1Provider(cache, downloader, proxy),
        (false, "dgm1") => BayernElevation.CreateDgm1Provider(cache, downloader),
        (false, "dgm5") => BayernElevation.CreateDgm5Provider(cache, downloader),
        (false, "dom20") => BayernElevation.CreateDom20Provider(cache, downloader),
        _ => throw new ArgumentException(
            $"'{dataset}' is not an elevation dataset for this state (by: dgm1/dgm5/dom20, ni: dgm1/dom1)."),
    };
}

static bool IsNiedersachsen(string[] args) =>
    (GetOption(args, "--state") ?? "by").ToLowerInvariant() switch
    {
        "by" or "bayern" => false,
        "ni" or "niedersachsen" => true,
        var s => throw new ArgumentException($"Unknown state '{s}' (use by or ni)."),
    };

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

static Utm32Point ParsePosition(string[] args)
{
    if (GetOptionIndex(args, "--latlon") is { } i && args.Length > i + 2)
    {
        var geo = new GeoPoint(ParseDouble(args[i + 1]), ParseDouble(args[i + 2]));
        return Etrs89Utm32Transform.Instance.ToUtm32(geo);
    }
    if (args.Length < 3) throw new ArgumentException("height needs <easting> <northing> or --latlon <lat> <lon>.");
    return new Utm32Point(ParseDouble(args[1]), ParseDouble(args[2]));
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
