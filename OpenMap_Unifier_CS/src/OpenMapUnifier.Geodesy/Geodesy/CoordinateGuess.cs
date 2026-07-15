namespace OpenMapUnifier.Geodesy;

/// <summary>One ranked interpretation of a raw coordinate pair.</summary>
public sealed record CoordinateGuess(
    int Epsg,
    string CrsName,
    GeoPoint Geo,
    double Confidence,
    string Reason,
    bool AxesSwapped = false)
{
    public override string ToString() =>
        FormattableString.Invariant(
            $"EPSG:{Epsg} ({CrsName}) -> {Geo}  [{Confidence:P0}{(AxesSwapped ? ", axes swapped" : "")}] {Reason}");
}
