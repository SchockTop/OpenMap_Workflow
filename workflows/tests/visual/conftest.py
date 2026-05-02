"""Visual integration test fixtures.

Drives the real assemble pipeline on a small fixture region (muc-marienplatz-50m)
and exposes 8 rendered camera presets. Skipped unless OPENMAP_VISUAL_TESTS=1
(or pytest -m visual).
"""
from __future__ import annotations
import os
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[3]
FIXTURE_REGION = "muc-marienplatz-50m"
SCENE_BLEND = REPO_ROOT / "data" / f"scene_{FIXTURE_REGION}.blend"
ARTIFACTS = Path(__file__).parent / "artifacts"
GOLDEN_DIR = Path(__file__).parent / "golden"
CHECKS_DIR = Path(__file__).parent / "vision_checks"

# (slot, altitude_m, framing, camera_preset_name)
SLOTS = [
    ("wide_aerial",        2000.0, "wide", "aircraft-approach"),
    ("wide_mid_drone",      500.0, "wide", "mid-drone"),
    ("wide_low_drone",       80.0, "wide", "low-drone"),
    ("wide_fpv",              1.7, "wide", "fpv-walk"),
    ("close_building",       30.0, "close", "close-building"),
    ("close_tree",            5.0, "close", "close-tree"),
    ("close_ground_patch",   10.0, "close", "close-ground-patch"),
    ("close_seam",           20.0, "close", "close-seam"),
]


def _visual_enabled():
    return os.environ.get("OPENMAP_VISUAL_TESTS") == "1"


def pytest_collection_modifyitems(config, items):
    if _visual_enabled():
        return
    skip = pytest.mark.skip(reason="set OPENMAP_VISUAL_TESTS=1 to run")
    for item in items:
        # Skip everything in this directory unless visual tests are enabled.
        if Path(str(item.fspath)).is_relative_to(Path(__file__).parent):
            # But always run pure-unit tests like test_assertions.py
            if item.fspath.basename in ("test_assertions.py",):
                continue
            item.add_marker(skip)


@pytest.fixture(scope="session")
def assembled_scene():
    """Assemble (or reuse) the fixture .blend exactly once per session."""
    if SCENE_BLEND.exists():
        return SCENE_BLEND
    cmd = [sys.executable, str(REPO_ROOT / "workflows" / "full_pipeline.py"),
           "--region", FIXTURE_REGION, "--skip-download"]
    print(f"[visual] assembling fixture: {' '.join(cmd)}")
    subprocess.run(cmd, check=True)
    assert SCENE_BLEND.exists(), f"Fixture .blend not produced: {SCENE_BLEND}"
    return SCENE_BLEND


@pytest.fixture(scope="session")
def artifacts_dir():
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    return ARTIFACTS


@pytest.fixture(scope="session")
def rendered_slots(assembled_scene, artifacts_dir):
    """Render all 8 slots and return {slot: png_path}."""
    blender = os.environ.get("BLENDER_BIN",
                             r"C:\Program Files\Blender Foundation\Blender 5.1\blender.exe")
    out = {}
    render_script = REPO_ROOT / "workflows" / "tests" / "visual" / "_render_slot.py"
    for slot, alt, framing, preset in SLOTS:
        png = artifacts_dir / f"{slot}.png"
        cmd = [blender, "-b", str(assembled_scene),
               "--python", str(render_script), "--",
               "--slot", slot, "--altitude", str(alt),
               "--framing", framing, "--preset", preset,
               "--out", str(png)]
        subprocess.run(cmd, check=True)
        assert png.exists(), f"Render failed for slot {slot}"
        out[slot] = png
    return out
