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


def test_sobel_detects_geometry_in_textured_image(tmp_path):
    """Real-content image (random noise) must have edge density > 0.05."""
    from PIL import Image
    import numpy as np
    rng = np.random.default_rng(42)
    arr = rng.integers(0, 255, (256, 256, 3), dtype=np.uint8)
    p = tmp_path / "noise.png"
    Image.fromarray(arr).save(p)
    from workflows.validate_workflow import _sobel_edge_density, phase_i_geometry_detail
    ratio, _ = _sobel_edge_density(p)
    assert ratio > 0.05, f"random noise should have many edges, got {ratio}"
    result = phase_i_geometry_detail(p)
    assert result.status == "OK"


def test_sobel_flags_grey_gradient_as_blank(tmp_path):
    """Pure linear gradient (no geometry) must FAIL the Sobel gate."""
    from PIL import Image
    import numpy as np
    h, w = 256, 256
    gradient = np.tile(np.linspace(50, 200, w, dtype=np.uint8)[None, :, None], (h, 1, 3))
    p = tmp_path / "gradient.png"
    Image.fromarray(gradient).save(p)
    from workflows.validate_workflow import _sobel_edge_density, phase_i_geometry_detail
    ratio, _ = _sobel_edge_density(p)
    assert ratio < 0.05, f"smooth gradient should have few edges, got {ratio}"
    result = phase_i_geometry_detail(p)
    assert result.status == "FAIL"
    assert "lacks geometry" in (result.error or "")


def test_sobel_flags_solid_color_as_blank(tmp_path):
    """Solid grey (zero edges) must FAIL."""
    from PIL import Image
    import numpy as np
    arr = np.full((256, 256, 3), 128, dtype=np.uint8)
    p = tmp_path / "flat.png"
    Image.fromarray(arr).save(p)
    from workflows.validate_workflow import phase_i_geometry_detail
    result = phase_i_geometry_detail(p)
    assert result.status == "FAIL"


def test_phase_result_lifecycle():
    from workflows.validate_workflow import PhaseResult
    r = PhaseResult("X").ok(items=3)
    assert r.status == "OK" and r.evidence["items"] == 3
    r2 = PhaseResult("Y").fail("nope")
    assert r2.status == "FAIL" and r2.error == "nope"
    r3 = PhaseResult("Z").skip("no input")
    assert r3.status == "SKIP"
