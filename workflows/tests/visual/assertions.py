"""assertions.py — image-metric helpers for the visual test harness.

All operate on float32 numpy arrays of shape (H, W, 3) in 0..1 range.
Use load_png() to convert from disk.
"""
from __future__ import annotations
from pathlib import Path

import numpy as np
from PIL import Image

SKY_HUE_LOW = 190.0 / 360.0
SKY_HUE_HIGH = 230.0 / 360.0


def load_png(path) -> np.ndarray:
    img = Image.open(path).convert("RGB")
    return np.asarray(img, dtype=np.float32) / 255.0


def _luminance(img: np.ndarray) -> np.ndarray:
    return 0.2126 * img[..., 0] + 0.7152 * img[..., 1] + 0.0722 * img[..., 2]


def _hue(img: np.ndarray) -> np.ndarray:
    """Per-pixel hue 0..1, vectorized colorsys-equivalent."""
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    cmax = np.max(img, axis=-1)
    cmin = np.min(img, axis=-1)
    delta = cmax - cmin
    delta_safe = np.where(delta == 0, 1, delta)
    h = np.zeros_like(cmax)
    mask_r = (cmax == r) & (delta != 0)
    mask_g = (cmax == g) & (delta != 0)
    mask_b = (cmax == b) & (delta != 0)
    h[mask_r] = (((g - b) / delta_safe) % 6)[mask_r]
    h[mask_g] = (((b - r) / delta_safe) + 2)[mask_g]
    h[mask_b] = (((r - g) / delta_safe) + 4)[mask_b]
    return (h / 6.0) % 1.0


def ground_visible(img: np.ndarray, min_ratio: float):
    h, w = img.shape[:2]
    lower = img[h // 2:, :]
    lum = _luminance(lower)
    hue = _hue(lower)
    r, g, b = lower[..., 0], lower[..., 1], lower[..., 2]
    # Sky: hue in blue band AND blue dominates over red/green (handles dim or bright sky).
    in_sky = (hue >= SKY_HUE_LOW) & (hue <= SKY_HUE_HIGH) & (b > r) & (b > g)
    # Pure white (overcast/blowout) also not "ground".
    blown_out = lum > 0.92
    ground_mask = ~in_sky & ~blown_out
    ratio = float(ground_mask.mean())
    if ratio < min_ratio:
        raise AssertionError(
            f"ground visibility too low: {ratio:.3f} < {min_ratio:.3f} "
            f"(lower half is mostly sky)")


def color_diversity(img: np.ndarray, min_unique_hues: int, region: str | None = None):
    sub = img
    if region == "buildings":
        h = img.shape[0]
        sub = img[int(h * 0.2):int(h * 0.8), :]
    hues = _hue(sub)
    sat = np.max(sub, -1) - np.min(sub, -1)
    significant = hues[sat > 0.05]
    if significant.size == 0:
        raise AssertionError("color_diversity: no saturated pixels")
    bins = (significant * 24).astype(np.int32) % 24
    populated = np.unique(bins).size
    if populated < min_unique_hues:
        raise AssertionError(
            f"color diversity too low: {populated} unique hues < {min_unique_hues}")


def no_haze_overpower(img: np.ndarray, max_blue_dominance: float):
    mean_blue = float(img[..., 2].mean())
    mean_lum = float(_luminance(img).mean())
    if mean_lum < 1e-6:
        raise AssertionError("no_haze_overpower: image is black")
    dominance = mean_blue / mean_lum
    if dominance > max_blue_dominance:
        raise AssertionError(
            f"haze overpower: blue/lum = {dominance:.3f} > {max_blue_dominance:.3f}")


def tree_present(img: np.ndarray, expected_green_ratio_range):
    r, g, b = img[..., 0], img[..., 1], img[..., 2]
    green_mask = (g > r * 1.1) & (g > b * 1.1) & (g > 0.15)
    ratio = float(green_mask.mean())
    lo, hi = expected_green_ratio_range
    if not (lo <= ratio <= hi):
        raise AssertionError(
            f"green ratio {ratio:.3f} out of range [{lo:.3f}, {hi:.3f}]")


def rmse_tripwire(img: np.ndarray, last_known_good, max_rmse: float):
    """Compare against a last-known-good image (path or numpy array)."""
    if isinstance(last_known_good, (str, Path)):
        lkg = load_png(last_known_good)
    else:
        lkg = last_known_good
    if lkg.shape != img.shape:
        raise AssertionError(f"rmse: shape mismatch {img.shape} vs {lkg.shape}")
    rmse = float(np.sqrt(((img - lkg) ** 2).mean()))
    if rmse > max_rmse:
        raise AssertionError(
            f"rmse tripwire: {rmse:.4f} > {max_rmse:.4f} (catastrophic regression?)")


def metrics_within_tolerance(actual: dict, golden: dict, tol: float):
    for key, golden_val in golden.items():
        if key not in actual:
            raise AssertionError(f"metrics: missing key '{key}'")
        a = float(actual[key]); g = float(golden_val)
        denom = max(abs(g), 1e-9)
        rel = abs(a - g) / denom
        if rel > tol:
            raise AssertionError(
                f"metrics: '{key}' actual={a:.4f} golden={g:.4f} "
                f"rel diff {rel:.3f} > tol {tol:.3f}")


def summary(img: np.ndarray) -> dict:
    """Compute scalar metrics for caching in golden/<slot>.json.

    Sky/ground detection matches ground_visible() exactly so a passing
    assertion produces a matching golden value.
    """
    h = img.shape[0]
    lower = img[h // 2:, :]
    lum_lower = _luminance(lower)
    hue_lower = _hue(lower)
    r, g, b = lower[..., 0], lower[..., 1], lower[..., 2]
    in_sky = (hue_lower >= SKY_HUE_LOW) & (hue_lower <= SKY_HUE_HIGH) & (b > r) & (b > g)
    blown_out = lum_lower > 0.92
    ground_mask = ~in_sky & ~blown_out
    return {
        "ground_pixel_ratio": float(ground_mask.mean()),
        "ground_luminance_variance": float(lum_lower.var()),
        "blue_dominance": float(img[..., 2].mean() / max(_luminance(img).mean(), 1e-6)),
        "mean_luminance": float(_luminance(img).mean()),
    }
