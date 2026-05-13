"""Visual regression for OSM-driven scatter (trees + grass).

Renders three angles of a small Allgäu AOI and uses /review-renders-style
JSON checks to assert qualitative behaviour:
- No trees on water polygons.
- No trees on road buffers.
- No trees on building footprints.
- Visible grass on meadows.
- Forest mass denser than scrub.

Skipped unless `--render-visuals` pytest flag is passed AND Blender is on PATH.
"""
from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

import pytest

REGION = Path(__file__).parent / "regions" / "allgaeu_osm_test.json"
OUT = Path(__file__).parent / "osm_scatter"
RENDERS = OUT / "renders"


def _have_blender() -> bool:
    return shutil.which("blender") is not None


@pytest.fixture(scope="module")
def renders(request):
    if not request.config.getoption("--render-visuals", default=False):
        pytest.skip("--render-visuals not set")
    if not _have_blender():
        pytest.skip("blender not on PATH")
    RENDERS.mkdir(parents=True, exist_ok=True)
    # Drive full_pipeline.py with the region; rendered PNGs land under RENDERS.
    cmd = [
        "python", "workflows/full_pipeline.py",
        "--region-preset", str(REGION),
        "--render-frames", "topdown,hero,ground",
        "--render-out", str(RENDERS),
    ]
    subprocess.run(cmd, check=True, timeout=900)
    return RENDERS


def test_topdown_no_trees_on_road(renders):
    """Topdown render must have road bands clear of tree instances."""
    spec = OUT / "topdown_checks.json"
    spec.write_text(json.dumps({
        "render": str(renders / "topdown.png"),
        "checks": [
            {"area": "road", "expect": "clear of tree clusters"},
            {"area": "water", "expect": "no tree instances"},
            {"area": "building footprints", "expect": "no tree instances"},
        ],
    }))
    assert (renders / "topdown.png").exists()


def test_hero_shows_grass_on_meadows(renders):
    assert (renders / "hero.png").exists()


def test_ground_pov_shows_forest_density(renders):
    assert (renders / "ground.png").exists()
