namespace OpenMapUnifier.Core.Geodesy;

/// <summary>
/// Bidirectional transform between geographic coordinates (WGS84/ETRS89 degrees)
/// and the working CRS EPSG:25832. The framework never hardcodes a specific
/// implementation — plug in your own conversion algorithms by implementing this
/// interface and passing it wherever a transform is accepted.
/// <see cref="Etrs89UtmTransform.Zone32"/> is the built-in default.
/// </summary>
public interface ICoordinateTransform
{
    UtmPoint ToUtm(GeoPoint geo);
    GeoPoint ToGeo(UtmPoint utm);
}
