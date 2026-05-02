"""Unit tests for visual assertion helpers — uses synthetic numpy arrays."""
from __future__ import annotations
import colorsys

import numpy as np
import pytest

from workflows.tests.visual import assertions


def _make_img(shape=(64, 96, 3), color=(0.5, 0.5, 0.5)):
    img = np.zeros(shape, dtype=np.float32)
    img[..., 0] = color[0]; img[..., 1] = color[1]; img[..., 2] = color[2]
    return img


def test_ground_visible_passes_when_lower_half_has_ground():
    img = _make_img()
    img[32:, :, :] = (0.30, 0.25, 0.15)  # ground-brown lower half
    img[:32, :, :] = (0.55, 0.70, 0.85)  # sky upper half
    assertions.ground_visible(img, min_ratio=0.4)


def test_ground_visible_fails_when_lower_half_is_sky():
    img = _make_img()
    # All sky-blue, bright.
    img[:, :, :] = (0.55, 0.70, 0.85)
    with pytest.raises(AssertionError, match="ground"):
        assertions.ground_visible(img, min_ratio=0.25)


def test_color_diversity_counts_unique_hues():
    img = np.zeros((100, 100, 3), dtype=np.float32)
    for i in range(24):
        h = i / 24
        r, g, b = colorsys.hsv_to_rgb(h, 1.0, 1.0)
        img[:, i*4:(i+1)*4, :] = (r, g, b)
    assertions.color_diversity(img, min_unique_hues=20)


def test_color_diversity_fails_on_monochrome():
    img = _make_img(color=(0.5, 0.5, 0.5))
    with pytest.raises(AssertionError):
        assertions.color_diversity(img, min_unique_hues=12)


def test_no_haze_overpower_passes_balanced_image():
    img = _make_img(color=(0.4, 0.4, 0.4))
    assertions.no_haze_overpower(img, max_blue_dominance=1.5)


def test_no_haze_overpower_fails_on_blue_dominant():
    img = _make_img(color=(0.2, 0.3, 0.9))
    with pytest.raises(AssertionError, match="haze"):
        assertions.no_haze_overpower(img, max_blue_dominance=1.5)


def test_rmse_tripwire_passes_close_images():
    a = _make_img(color=(0.5, 0.5, 0.5))
    rng = np.random.default_rng(0)
    b = a + rng.normal(0, 0.05, a.shape).astype(np.float32)
    assertions.rmse_tripwire(a, b, max_rmse=0.10)


def test_rmse_tripwire_fails_when_inverted():
    a = _make_img(color=(0.1, 0.1, 0.1))
    b = _make_img(color=(0.9, 0.9, 0.9))
    with pytest.raises(AssertionError, match="rmse"):
        assertions.rmse_tripwire(a, b, max_rmse=0.10)


def test_metrics_within_tolerance():
    actual = {"x": 1.0, "y": 2.0}
    golden = {"x": 1.05, "y": 1.95}
    assertions.metrics_within_tolerance(actual, golden, tol=0.10)


def test_metrics_within_tolerance_fails_outside():
    actual = {"x": 1.0}
    golden = {"x": 2.0}
    with pytest.raises(AssertionError):
        assertions.metrics_within_tolerance(actual, golden, tol=0.10)


def test_metrics_within_tolerance_missing_key_fails():
    with pytest.raises(AssertionError, match="missing"):
        assertions.metrics_within_tolerance({}, {"x": 1.0}, tol=0.10)


def test_summary_produces_expected_keys():
    img = _make_img(color=(0.4, 0.4, 0.4))
    s = assertions.summary(img)
    for k in ("ground_pixel_ratio", "ground_luminance_variance",
              "blue_dominance", "mean_luminance"):
        assert k in s, f"missing key {k}"
        assert isinstance(s[k], float)


def test_tree_present_passes_with_green():
    img = _make_img(color=(0.2, 0.5, 0.15))  # green dominant
    assertions.tree_present(img, expected_green_ratio_range=(0.5, 1.01))


def test_tree_present_fails_with_no_green():
    img = _make_img(color=(0.5, 0.2, 0.15))  # red dominant
    with pytest.raises(AssertionError):
        assertions.tree_present(img, expected_green_ratio_range=(0.5, 1.01))


def test_load_png_roundtrip(tmp_path):
    from PIL import Image
    arr = (np.random.default_rng(0).random((32, 48, 3)) * 255).astype(np.uint8)
    p = tmp_path / "x.png"
    Image.fromarray(arr).save(p)
    out = assertions.load_png(p)
    assert out.dtype == np.float32
    assert out.shape == arr.shape
    assert out.min() >= 0.0 and out.max() <= 1.0
