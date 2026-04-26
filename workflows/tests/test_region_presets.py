"""OpenMap_Workflow/workflows/tests/test_region_presets.py"""
import pytest


def test_muc_sued_4x2km_polygon_covers_target_tiles():
    from workflows.region_presets import REGIONS, polygon_for_region
    poly_wkt = polygon_for_region("muc-sued-4x2")
    assert poly_wkt.startswith("POLYGON")
    # Must intersect at least 8 DGM1 1km tiles (4 east x 2 north).
    from shapely.wkt import loads
    from shapely.geometry import box
    from pyproj import Transformer
    poly = loads(poly_wkt)
    t = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True)
    proj = type(poly)([t.transform(x, y) for x, y in poly.exterior.coords])
    minx, miny, maxx, maxy = proj.bounds
    assert (maxx - minx) >= 3500 and (maxx - minx) <= 4500
    assert (maxy - miny) >= 1500 and (maxy - miny) <= 2500


def test_polygon_for_unknown_region_raises():
    from workflows.region_presets import polygon_for_region
    with pytest.raises(KeyError):
        polygon_for_region("does-not-exist")
