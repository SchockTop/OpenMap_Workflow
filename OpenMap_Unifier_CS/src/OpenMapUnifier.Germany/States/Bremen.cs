using OpenMapUnifier.Networking;
using OpenMapUnifier.Elevation;
using OpenMapUnifier.Geodesy;
using OpenMapUnifier.Networking.Proxy;
using OpenMapUnifier.Raster;
using OpenMapUnifier.Germany.Common;

namespace OpenMapUnifier.Germany.States;

/// <summary>
/// Bremen (Landesamt GeoInformation) — EPSG:25832, static whole-city archives
/// under gdi2.geo.bremen.de (separate zips for Bremen and Bremerhaven).
/// DGM1/DOM1 are 1 km ASCII-XYZ tiles inside the zips; single tiles are
/// range-extracted. Inner names glue the zone to the easting
/// ("dgm1_32489_5882_1_hb.xyz"). CC BY 4.0.
/// </summary>
public sealed class Bremen : StateBase
{
    private const string Base = "https://gdi2.geo.bremen.de/inspire/download";

    private static readonly string[] DgmArchives =
    {
        $"{Base}/DGM/data/Gitternetz_DGM1_2017_HB_ASCII_XYZ.zip",
        $"{Base}/DGM/data/Gitternetz_DGM1_2015_BHV_ASCII_XYZ.zip",
    };

    private static readonly string[] DomArchives =
    {
        $"{Base}/DOM/data/Gitternetz_DOM1_2017_HB_ASCII_XYZ.zip",
        $"{Base}/DOM/data/Gitternetz_DOM1_2015_BHV_ASCII_XYZ.zip",
    };

    public override string Code => "hb";
    public override string Name => "Bremen";
    public override int UtmZone => 32;
    public override string License => "CC BY 4.0";
    public override string Attribution => "Landesamt GeoInformation Bremen (CC BY 4.0)";

    public override IReadOnlyDictionary<string, string> Datasets { get; } = new Dictionary<string, string>
    {
        ["dgm1"] = "Terrain 1 m, XYZ tiles range-extracted from the city archives (HB 2017 / BHV 2015)",
        ["dom1"] = "Surface model 1 m, XYZ tiles from the city archives",
        ["dop10"] = "Orthophoto 10 cm RGB JPG — whole-city archive (~6 GB, zip64)",
        ["lod2"] = "3D buildings CityGML — whole-city archives",
    };

    public override Task<IReadOnlyList<DownloadJob>> JobsForAsync(string datasetId, BoundingBox area,
        CancellationToken ct = default)
    {
        IReadOnlyList<string> archives = datasetId.ToLowerInvariant() switch
        {
            "dgm1" => DgmArchives,
            "dom1" => DomArchives,
            "dop10" => new[]
            {
                $"{Base}/DOP/data/DOP10_RGB_JPG_HB.zip",
                $"{Base}/DOP/data/DOP10_RGB_JPG_BHV.zip",
            },
            "lod2" => new[]
            {
                $"{Base}/LoD/data/LOD2_CITYGML_HB.zip",
                $"{Base}/LoD/data/LOD2_CITYGML_BHV.zip",
            },
            _ => throw new KeyNotFoundException($"Unknown Bremen dataset '{datasetId}'."),
        };
        return Task.FromResult<IReadOnlyList<DownloadJob>>(
            archives.Select(u => new DownloadJob(u.Split('/')[^1], u)).ToList());
    }

    protected override IHeightTileResolver CreateHeightResolver(string datasetId, ProxyManager? proxy)
    {
        RequireHeightDataset(datasetId, "dgm1", "dom1");
        var isDgm = datasetId.Equals("dgm1", StringComparison.OrdinalIgnoreCase);
        var http = CreateHttp(proxy, 600);
        var archives = isDgm ? DgmArchives : DomArchives;
        var fetcher = new RemoteArchiveFetcher(http,
            _ => Task.FromResult<IReadOnlyList<string>>(archives));

        var prod = isDgm ? "dgm1" : "dom1";
        return new ArchiveTileResolver(1, fetcher,
            // Zone glued to easting, no underscore: dgm1_32489_5882_1_hb.xyz;
            // suffix _hb/_bhv depends on the city, so match only the key.
            tile => name => name.Contains(FormattableString.Invariant(
                                $"{prod}_32{tile.EastKm:D3}_{tile.NorthKm:D4}_1_")) &&
                            name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase),
            tile => FormattableString.Invariant($"{prod}_32{tile.EastKm:D3}_{tile.NorthKm:D4}_1_hb.xyz"),
            (path, _) =>
            {
                using var stream = File.OpenRead(path);
                return XyzGridReader.Read(stream);
            });
    }
}
