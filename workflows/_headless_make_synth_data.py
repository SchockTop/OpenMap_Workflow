"""Generate a synthetic 1 km heightmap + DOP UDIM tile for headless ortho-drape
plumbing test. Produces:

    data/synth/heightmap.png   gentle hills + a ridge
    data/synth/ortho_udim/ortho.1001.jpg   vivid fake aerial (fields + grid)

The ortho is intentionally garish so any visible vs. invisible drape is
unambiguous. NOT real Bayern data — this proves the code path, not data.
"""
from __future__ import annotations
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "synth"
ORTHO_DIR = OUT / "ortho_udim"
ORTHO_DIR.mkdir(parents=True, exist_ok=True)


def make_heightmap(size: int = 1024) -> np.ndarray:
    rng = np.random.default_rng(7)
    # Smooth low-frequency hills + a diagonal ridge.
    yy, xx = np.mgrid[0:size, 0:size] / size
    base = 0.3 * np.sin(xx * 6.0) * np.cos(yy * 4.0)
    ridge = 0.4 * np.exp(-((xx - yy - 0.1) ** 2) / 0.02)
    noise = 0.05 * rng.standard_normal((size, size))
    h = base + ridge + noise
    h = (h - h.min()) / (h.max() - h.min())  # 0..1
    return (h * 65535).astype(np.uint16)


def make_ortho(size: int = 2048) -> np.ndarray:
    """A vivid, photo-like 'aerial' image. Three colored fields with a road
    grid, dirt patches, a forest blob — high color variance + sharp edges so
    a draped material is unmistakable on the rendered terrain."""
    rng = np.random.default_rng(13)
    img = np.zeros((size, size, 3), dtype=np.uint8)
    yy, xx = np.mgrid[0:size, 0:size]

    # Background: yellow-green field with fine grain.
    img[..., 0] = 170 + (10 * rng.standard_normal((size, size))).astype(np.int16)
    img[..., 1] = 200 + (10 * rng.standard_normal((size, size))).astype(np.int16)
    img[..., 2] = 90 + (10 * rng.standard_normal((size, size))).astype(np.int16)

    # Forest patch (top-left quadrant).
    forest = ((xx < size * 0.35) & (yy < size * 0.45)
              & (((xx - size * 0.18) ** 2 + (yy - size * 0.22) ** 2) < (size * 0.18) ** 2))
    img[forest] = [40 + rng.integers(0, 25), 90 + rng.integers(0, 30), 35 + rng.integers(0, 20)]

    # Brown dirt patch (bottom-right).
    dirt = ((xx > size * 0.55) & (yy > size * 0.55)
            & ((xx - size * 0.78) ** 2 + (yy - size * 0.78) ** 2 < (size * 0.20) ** 2))
    img[dirt] = [140, 100, 60]

    # Blue water blob (right edge).
    water = ((xx - size * 0.92) ** 2 + (yy - size * 0.4) ** 2 < (size * 0.12) ** 2)
    img[water] = [40, 80, 160]

    # Roads — a 4-cell grid of dark strips.
    for i in range(1, 4):
        y0 = int(size * i / 4)
        img[y0 - 6:y0 + 6, :] = [55, 55, 55]
        x0 = int(size * i / 4)
        img[:, x0 - 6:x0 + 6] = [55, 55, 55]

    # Buildings: scatter of small white-ish blocks.
    for _ in range(120):
        cx, cy = rng.integers(40, size - 40, size=2)
        w, h = rng.integers(8, 30, size=2)
        img[cy - h:cy + h, cx - w:cx + w] = [220 + rng.integers(0, 30),
                                              215 + rng.integers(0, 30),
                                              200 + rng.integers(0, 30)]

    return np.clip(img, 0, 255).astype(np.uint8)


hm = make_heightmap(1024)
hm_path = OUT / "heightmap.png"
Image.fromarray(hm).save(hm_path)
print(f"heightmap -> {hm_path} ({hm_path.stat().st_size//1024} KB)")

ortho = make_ortho(2048)
ortho_path = ORTHO_DIR / "ortho.1001.jpg"
Image.fromarray(ortho).save(ortho_path, quality=92)
print(f"ortho     -> {ortho_path} ({ortho_path.stat().st_size//1024} KB)")
