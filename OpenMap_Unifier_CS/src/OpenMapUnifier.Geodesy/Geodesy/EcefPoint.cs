namespace OpenMapUnifier.Geodesy;

/// <summary>Earth-centered earth-fixed cartesian position, meters.</summary>
public readonly record struct EcefPoint(double X, double Y, double Z);
