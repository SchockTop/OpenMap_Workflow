using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Germany;
using OpenMapUnifier.Networking;

namespace OpenMapUnifier.MapScene;

/// <summary>
/// The friendly front door: "give me a map of this region" — terrain (and
/// optionally the surface model and ortho tiles) fetched through the Unifier,
/// wrapped in a scene-local frame, ready for trajectories, sensors and area
/// analysis. Everything it builds is also reachable piecewise through the
/// individual classes; this type just removes the ceremony.
/// </summary>
public sealed class Map
{
    public SceneAnchor Anchor { get; }
    public TerrainLayer Terrain { get; }
    /// <summary>Surface model (DOM/bDOM) when loaded — enables object-height conditions.</summary>
    public TerrainLayer? Surface { get; }
    public RasterLayer? Classification { get; private set; }
    public LineOfSight LineOfSight { get; }
    public CoverageAnalyzer Coverage { get; }

    private Map(SceneAnchor anchor, TerrainLayer terrain, TerrainLayer? surface)
    {
        Anchor = anchor;
        Terrain = terrain;
        Surface = surface;
        LineOfSight = new LineOfSight(terrain, anchor);
        Coverage = new CoverageAnalyzer(terrain, anchor);
    }

    /// <summary>
    /// Load a map region from a state's open data. Downloads are cached in
    /// <paramref name="cacheDirectory"/>, so repeated loads are offline.
    /// </summary>
    public static async Task<Map> LoadAsync(string stateCode, BoundingBox region,
        string cacheDirectory, double resolution = 1.0, bool withSurfaceModel = false,
        CancellationToken ct = default)
    {
        var state = GermanStates.Get(stateCode);
        var anchor = new SceneAnchor(region.Center, state.UtmZone);

        using var terrainProvider = state.CreateElevationProvider("dgm1", cacheDirectory);
        var terrain = await TerrainLayer.LoadAsync(terrainProvider, region, resolution, ct)
            .ConfigureAwait(false);

        TerrainLayer? surface = null;
        if (withSurfaceModel)
        {
            var surfaceDataset = state.Datasets.Keys.FirstOrDefault(
                d => d is "dom1" or "dom20" or "bdom");
            if (surfaceDataset is not null)
            {
                using var surfaceProvider = state.CreateElevationProvider(surfaceDataset, cacheDirectory);
                surface = await TerrainLayer.LoadAsync(surfaceProvider, region, resolution, ct)
                    .ConfigureAwait(false);
            }
        }
        return new Map(anchor, terrain, surface);
    }

    /// <summary>Build a map from files on disk (no network).</summary>
    public static Map FromFiles(string terrainPath, string? surfacePath = null, int utmZone = 32)
    {
        var terrain = TerrainLayer.FromFile(terrainPath);
        var surface = surfacePath is null ? null : TerrainLayer.FromFile(surfacePath);
        return new Map(SceneAnchor.CenterOf(terrain.Bounds, utmZone), terrain, surface);
    }

    /// <summary>Attach a classification raster (e.g. a ground-texture material map).</summary>
    public Map WithClassification(string path)
    {
        Classification = RasterLayer.FromFile(path);
        return this;
    }

    /// <summary>Download the ortho imagery covering the region (for texturing).</summary>
    public async Task<IReadOnlyList<DownloadResult>> DownloadOrthoAsync(string stateCode,
        string datasetId, string targetDirectory, CancellationToken ct = default)
    {
        var jobs = await GermanStates.Get(stateCode)
            .JobsForAsync(datasetId, Terrain.Bounds, ct).ConfigureAwait(false);
        using var downloader = new HttpTileDownloader();
        return await downloader.DownloadAllAsync(jobs, targetDirectory, ct: ct).ConfigureAwait(false);
    }

    // ---- ready-made area conditions ("bendable" building blocks) --------------

    /// <summary>Objects (trees, buildings…) taller than a threshold; needs the surface model.</summary>
    public GroundMask ObjectsTallerThan(double meters) =>
        Surface is null
            ? throw new InvalidOperationException(
                "No surface model loaded — load the map with withSurfaceModel: true.")
            : GroundMask.FromObjectHeight(Terrain, Surface, meters);

    /// <summary>Cells of one classification class; needs WithClassification().</summary>
    public GroundMask InClass(int classId) =>
        Classification is null
            ? throw new InvalidOperationException("No classification layer attached.")
            : GroundMask.FromClass(Terrain, Classification, classId);

    /// <summary>Any custom condition over ground positions.</summary>
    public GroundMask Where(Func<UtmPoint, bool> condition) =>
        GroundMask.FromCondition(Terrain, condition);
}
