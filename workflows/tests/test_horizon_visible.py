"""Tests for the horizon-visible checker."""
import pytest
from pathlib import Path


def test_check_passes_on_synthetic_two_tone(tmp_path):
    """Image with bright sky on top + dark ground on bottom should pass."""
    from PIL import Image
    import numpy as np
    img = np.zeros((100, 100, 3), dtype=np.uint8)
    img[:50] = (180, 200, 220)  # bright sky
    img[50:] = (50, 70, 40)     # dark ground
    p = tmp_path / "horizon.png"
    Image.fromarray(img).save(p)
    from workflows.test_horizon_visible import check
    assert check(p) is True


def test_check_fails_on_uniform_image(tmp_path):
    from PIL import Image
    import numpy as np
    img = np.full((100, 100, 3), 100, dtype=np.uint8)
    p = tmp_path / "flat.png"
    Image.fromarray(img).save(p)
    from workflows.test_horizon_visible import check
    assert check(p) is False
