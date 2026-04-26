"""region_presets.py — named WGS84 polygons for repeatable test regions."""
from __future__ import annotations

# WKT polygons in EPSG:4326 (lon, lat).  Sized in *projected* (UTM32N) meters.
REGIONS: dict[str, str] = {
    # Marienplatz southward into the Isar valley — 4 km E-W x 2 km N-S.
    # Centred at approx UTM (688000, 5333000) -> WGS84 ~(11.532, 48.124).
    "muc-sued-4x2": (
        "POLYGON((11.530 48.117, 11.585 48.117, "
        "11.585 48.135, 11.530 48.135, 11.530 48.117))"
    ),
    # Tiny Marienplatz square — 1 DGM1 tile, 1 DOP, 1 LoD2.
    "muc-marienplatz-50m": (
        "POLYGON((11.5750 48.1370, 11.5760 48.1370, "
        "11.5760 48.1378, 11.5750 48.1378, 11.5750 48.1370))"
    ),
    # Cinematic baseline per playbook — 10 km x 4 km.
    "muc-sued-10x4": (
        "POLYGON((11.450 48.080, 11.585 48.080, "
        "11.585 48.118, 11.450 48.118, 11.450 48.080))"
    ),
}


def polygon_for_region(name: str) -> str:
    """Return the WKT polygon for a named region. Raises KeyError if unknown."""
    if name not in REGIONS:
        raise KeyError(f"unknown region {name!r}; known: {sorted(REGIONS)}")
    return REGIONS[name]
