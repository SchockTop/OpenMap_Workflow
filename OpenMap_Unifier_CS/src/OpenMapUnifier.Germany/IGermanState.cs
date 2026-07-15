using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Proxy;

namespace OpenMapUnifier.Germany;

/// <summary>
/// One German state's open-geodata source. Implementations wrap the state's
/// actual distribution mechanism (static grid URLs, index feeds, download
/// APIs, or remote-archive extraction) behind a uniform surface: enumerate
/// download jobs for an area, and answer terrain-height queries.
/// All URL patterns were verified live against the state services (July 2026);
/// see research notes in the repository README.
/// </summary>
public interface IGermanState
{
    /// <summary>Two-letter state code ("nw", "he", ...).</summary>
    string Code { get; }
    string Name { get; }

    /// <summary>32 (EPSG:25832) or 33 (EPSG:25833 — BE/BB/SN/MV).</summary>
    int UtmZone { get; }

    string License { get; }
    string Attribution { get; }

    /// <summary>Dataset id → short human description.</summary>
    IReadOnlyDictionary<string, string> Datasets { get; }

    /// <summary>
    /// Download jobs covering an area (bounding box in the STATE's CRS — zone
    /// 33 for BE/BB/SN/MV). For archive-only states (HH/HB/SL/HE) the jobs are
    /// the covering archives, which may be much larger than the area.
    /// </summary>
    Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default);

    /// <summary>
    /// Elevation provider for a height dataset ("dgm1"; most states also
    /// "dom1"). Positions are in the state's CRS; the provider's Transform
    /// handles lat/lon.
    /// </summary>
    TiledElevationProvider CreateElevationProvider(string datasetId, string cacheDirectory,
        IDownloader? downloader = null, ProxyManager? proxy = null);
}

/// <summary>Registry of ALL 16 German states.</summary>
public static class GermanStates
{
    public static readonly IReadOnlyDictionary<string, IGermanState> All =
        new IGermanState[]
        {
            new States.Bayern(),
            new States.NiedersachsenState(),
            new States.NordrheinWestfalen(),
            new States.Hessen(),
            new States.Thueringen(),
            new States.Sachsen(),
            new States.Berlin(),
            new States.Brandenburg(),
            new States.SachsenAnhalt(),
            new States.MecklenburgVorpommern(),
            new States.Hamburg(),
            new States.Bremen(),
            new States.SchleswigHolstein(),
            new States.BadenWuerttemberg(),
            new States.RheinlandPfalz(),
            new States.Saarland(),
        }.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

    public static IGermanState Get(string code) =>
        All.TryGetValue(code, out var state)
            ? state
            : throw new KeyNotFoundException(
                $"Unknown state '{code}'. Known: {string.Join(", ", All.Keys.OrderBy(k => k, StringComparer.Ordinal))}.");
}
