namespace OpenMapUnifier.Core.Geodesy;

/// <summary>
/// Bidirectional transform between geographic coordinates (WGS84/ETRS89 degrees)
/// and the working CRS EPSG:25832. The framework never hardcodes a specific
/// implementation — plug in your own conversion algorithms by implementing this
/// interface and passing it wherever a transform is accepted.
/// <see cref="Etrs89Utm32Transform.Instance"/> is the built-in default.
/// </summary>
public interface ICoordinateTransform
{
    Utm32Point ToUtm32(GeoPoint geo);
    GeoPoint ToGeo(Utm32Point utm);
}
