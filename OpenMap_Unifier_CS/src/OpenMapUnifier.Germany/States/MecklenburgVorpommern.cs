using OpenMapUnifier.Core.Downloading;
using OpenMapUnifier.Core.Elevation;
using OpenMapUnifier.Core.Geodesy;
using OpenMapUnifier.Core.Grid;
using OpenMapUnifier.Core.Proxy;
using OpenMapUnifier.Core.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Mecklenburg-Vorpommern (geodaten-mv.de INSPIRE ATOM downloads) —
/// EPSG:25833, 2 km tiles, plain GeoTIFFs for the height/ortho products.
/// The dataset ids in the URLs are opaque tokens from the ATOM feeds
/// (verified stable July 2026). CC BY 4.0. NOTE: MV DGM1 GeoTIFFs may lack a
/// NoData tag — the reader then assumes -9999, which never occurs as a real
/// elevation. Servers ignore HTTP Range (full streams only).
/// </summary>
public sealed class MecklenburgVorpommern : StateBase
{
    private const string Base = "https://www.geodaten-mv.de/dienste";
    private const string DgmDataset = "ca268792-s2q1-4a39-b34c-9ec5bf9a4469";
    private const string DomDataset = "us214578-a1n5-4v12-v31c-5tg2az3a2164";
    private const string DopRgbiDataset = "f94d17fa-b29b-41f7-a4b8-6e10f1aae38e";
    private const string Lod2Dataset = "8397b554-5cb9-4274-8be8-c20490d9a6e8";

    public override string Code => "mv";
    public override string Name => "Mecklenburg-Vorpommern";
    public override int UtmZone => 33;
    public override string License => "CC BY 4.0";
    public override string Attribution => "© GeoBasis-DE/M-V";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, GeoTIFF (2 km tiles, 2000x2000 px)",
        ["dom1"] = "Surface model 1 m, GeoTIFF (2 km tiles)",
        ["dop20rgbi"] = "Orthophoto 20 cm RGBI, GeoTIFF (2 km tiles, 10000x10000 px)",
        ["lod2"] = "3D buildings CityGML in zip (2 km tiles)",
    };

    internal static DownloadJob JobFor(string datasetId, TileId tile)
    {
        var key = FormattableString.Invariant($"33_{tile.EastKm:D3}_{tile.NorthKm:D4}_2");
        return datasetId.ToLowerInvariant() switch
        {
            "dgm1" => Job($"dgm1_{key}_gtiff.tif", $"{Base}/dgm_download?index=4&dataset={DgmDataset}"),
            "dom1" => Job($"dom1_{key}_gtiff.tif", $"{Base}/dom_download?index=4&dataset={DomDataset}"),
            "dop20rgbi" => Job($"dop20rgbi_{key}_mv.tif", $"{Base}/dop20_download?index=0&dataset={DopRgbiDataset}"),
            "lod2" => Job($"lod2_{key}_gml.zip", $"{Base}/gebaeude_download?index=0&dataset={Lod2Dataset}"),
            _ => throw new KeyNotFoundException($"Unknown Mecklenburg-Vorpommern dataset '{datasetId}'."),
        };

        static DownloadJob Job(string fileName, string urlPrefix) =>
            new(fileName, $"{urlPrefix}&file={fileName}");
    }

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DownloadJob>>(
            TileGrid.TilesFor(area, 2).Select(t => JobFor(datasetId, t)).ToList());

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        return new DelegateTileResolver(2,
            (tile, _) => Task.FromResult<DownloadJob?>(JobFor(datasetId, tile)),
            (path, _) => GeoTiffReader.Read(path));
    }
}
