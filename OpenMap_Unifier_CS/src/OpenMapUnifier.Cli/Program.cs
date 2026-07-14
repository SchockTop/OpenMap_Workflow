using System.Globalization;
using OpenMapUnifier.Bayern;
using OpenMapUnifier.Core.Catalog;
using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Raster;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
try
{
    return command switch
    {
        "datasets" => Datasets(),
        "tiles" => Tiles(args),
        "download" => await Download(args),
        "height" => await Height(args),
        "profile" => await Profile(args),
        "convert" => Convert_(args),
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
        OpenMapUnifier .NET — Bayern OpenData downloader & terrain queries (EPSG:25832)

        Usage:
          openmap datasets
          openmap tiles <dataset> --bbox <minE,minN,maxE,maxN>
          openmap download <dataset> --bbox <minE,minN,maxE,maxN> [--out DIR] [--parallel N] [--sidecars]
          openmap height <easting> <northing> [--dataset dgm1|dgm5|dom20] [--cache DIR]
          openmap height --latlon <lat> <lon> [...]
          openmap profile <fromE> <fromN> <toE> <toN> [--samples N] [--cache DIR]
          openmap convert --to-utm <lat> <lon>
          openmap convert --to-latlon <easting> <northing>

        Examples (Munich, Marienplatz):
          openmap convert --to-utm 48.137222 11.575556
          openmap height --latlon 48.137222 11.575556
          openmap download dgm1 --bbox 691000,5334000,692000,5335000 --out downloads

        Data: Bayerische Vermessungsverwaltung, CC BY 4.0 (www.geodaten.bayern.de)
        """);
    return 0;
}

static int Datasets()
{
    foreach (var d in BayernCatalog.Instance.Datasets.Values)
    {
        Console.WriteLine($"{d.Id,-14} [{d.Category}] {d.Label}");
        Console.WriteLine($"{"",-14} {d.Description}");
    }
    return 0;
}

static int Tiles(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));
    var source = new BayernTileSource();
    var jobs = source.JobsFor(dataset, bbox).ToList();
    foreach (var job in jobs)
        Console.WriteLine($"{job.FileName}  {job.Mirrors[0]}");
    Console.WriteLine($"{jobs.Count} tiles, ~{source.EstimateSizeMb(dataset, bbox):F0} MB");
    return 0;
}

static async Task<int> Download(string[] args)
{
    var dataset = Require(args, 1, "dataset id");
    var bbox = ParseBbox(GetOption(args, "--bbox") ?? throw new ArgumentException("--bbox is required."));
    var outDir = GetOption(args, "--out") ?? "downloads";
    var parallel = int.Parse(GetOption(args, "--parallel") ?? "4", CultureInfo.InvariantCulture);
    var sidecars = args.Contains("--sidecars");

    var catalog = BayernCatalog.Instance;
    var info = catalog[dataset];
    var jobs = (info.Kind == DatasetKind.Wms
        ? new BayernWmsSource().JobsFor(dataset, bbox)
        : new BayernTileSource().JobsFor(dataset, bbox)).ToList();

    Console.WriteLine($"Downloading {jobs.Count} {dataset} tiles to {outDir} ...");
    using var downloader = new HttpTileDownloader(new DownloaderOptions { MaxParallel = parallel });
    var progress = new Progress<DownloadProgress>(p =>
    {
        if (p.Status is "Completed" or "Skipped (exists)")
            Console.WriteLine($"  {p.FileName}: {p.Status}");
    });
    var results = await downloader.DownloadAllAsync(jobs, outDir, progress);

    var ok = results.Count(r => r.Success);
    foreach (var r in results.Where(r => !r.Success))
        Console.Error.WriteLine($"  FAILED {r.Job.FileName}: {r.Error}");

    if (sidecars && info.PixelSizeMeters is { } px)
        foreach (var r in results.Where(r => r.Success && r.LocalPath is not null))
            WorldFile.WriteSidecarsForBayernTile(r.LocalPath!, px);

    Console.WriteLine($"{ok}/{results.Count} tiles OK.");
    Console.WriteLine(BayernCatalog.Attribution);
    return ok == results.Count ? 0 : 1;
}

static async Task<int> Height(string[] args)
{
    var point = ParsePosition(args);
    var dataset = GetOption(args, "--dataset") ?? "dgm1";
    var cache = GetOption(args, "--cache") ?? "tilecache";

    using var provider = CreateProvider(dataset, cache);
    var elevation = await provider.GetElevationAsync(point);
    var geo = Etrs89Utm32Transform.Instance.ToGeo(point);
    Console.WriteLine($"Position: {point}  ({geo})");
    Console.WriteLine(elevation is null
        ? "Elevation: no data (outside Bavaria or NoData cell)"
        : FormattableString.Invariant($"Elevation: {elevation:F2} m ({dataset})"));

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

    using var provider = BayernElevation.CreateDgm1Provider(cache);
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

static TiledElevationProvider CreateProvider(string dataset, string cache) => dataset.ToLowerInvariant() switch
{
    "dgm1" => BayernElevation.CreateDgm1Provider(cache),
    "dgm5" => BayernElevation.CreateDgm5Provider(cache),
    "dom20" => BayernElevation.CreateDom20Provider(cache),
    _ => throw new ArgumentException($"'{dataset}' is not an elevation dataset (use dgm1, dgm5, or dom20)."),
};

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
