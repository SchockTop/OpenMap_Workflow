using OpenMapUnifier.Core.Geodesy;

namespace OpenMapUnifier.Core.Grid;

/// <summary>
/// One cell of the AdV kilometer grid in EPSG:25832. <see cref="EastKm"/> /
/// <see cref="NorthKm"/> are the SW corner in kilometers (e.g. 729, 5433 for
/// the tile covering E 729000-730000, N 5433000-5434000). <see cref="GridKm"/>
/// is the cell size: 1 for DGM1/DOP, 2 for LoD2.
/// </summary>
public readonly record struct TileId(int EastKm, int NorthKm, int GridKm = 1)
{
    public double MinEasting => EastKm * 1000.0;
    public double MinNorthing => NorthKm * 1000.0;
    public double MaxEasting => (EastKm + GridKm) * 1000.0;
    public double MaxNorthing => (NorthKm + GridKm) * 1000.0;

    public BoundingBox Bounds => new(MinEasting, MinNorthing, MaxEasting, MaxNorthing);

    /// <summary>"&lt;east_km&gt;_&lt;north_km&gt;", the core of every Bayern tile filename.</summary>
    public string Key => FormattableString.Invariant($"{EastKm}_{NorthKm}");

    public override string ToString() => FormattableString.Invariant($"{Key} ({GridKm} km)");
}
