"""OpenMap_Workflow/workflows/tests/test_validate_workflow.py"""
import pytest
from pathlib import Path


def _make_test_png(path: Path, color, size=(64, 64)):
    from PIL import Image
    Image.new("RGB", size, color).save(path)


def test_phase_g_detects_blank_render(tmp_path):
    from workflows.validate_workflow import phase_g_render_readback
    blank = tmp_path / "blank.png"
    _make_test_png(blank, (0, 0, 0))
    r = phase_g_render_readback(blank)
    assert r.status == "FAIL", r.evidence


def test_phase_g_accepts_textured_render(tmp_path):
    from PIL import Image
    import numpy as np
    rng = np.random.default_rng(42)
    arr = rng.integers(0, 255, (256, 256, 3), dtype=np.uint8)
    out = tmp_path / "noise.png"
    Image.fromarray(arr).save(out)
    from workflows.validate_workflow import phase_g_render_readback
    r = phase_g_render_readback(out)
    assert r.status == "OK", r.evidence
    assert r.evidence["unique_gray_values"] > 50


def test_phase_result_lifecycle():
    from workflows.validate_workflow import PhaseResult
    r = PhaseResult("X").ok(items=3)
    assert r.status == "OK" and r.evidence["items"] == 3
    r2 = PhaseResult("Y").fail("nope")
    assert r2.status == "FAIL" and r2.error == "nope"
    r3 = PhaseResult("Z").skip("no input")
    assert r3.status == "SKIP"
