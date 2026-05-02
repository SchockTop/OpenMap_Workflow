"""test_pipeline_visual.py — end-to-end visual regression tests.

Three tiers per slot:
  1. Metric assertions (cheap, deterministic, run always)
  2. RMSE tripwire vs golden/<slot>_lkg.png (catches catastrophic regressions)
  3. Vision review via Claude Code /review-renders (hash-gated cache)

Skipped unless OPENMAP_VISUAL_TESTS=1.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path

import pytest

from workflows.tests.visual import assertions
from workflows.tests.visual.conftest import (
    SLOTS, GOLDEN_DIR, CHECKS_DIR, ARTIFACTS,
)


pytestmark = pytest.mark.visual


def _hash_png(path: Path) -> str:
    return "sha256:" + hashlib.sha256(path.read_bytes()).hexdigest()


def _load_vision_results():
    p = ARTIFACTS / "vision_review.results.json"
    if not p.exists():
        return None
    return json.loads(p.read_text())


def _write_todo(rendered: dict[str, Path]):
    todo = {
        "instructions": (
            "For each slot, Read the PNG and answer every question in its "
            "checklist. Write structured results to vision_review.results.json "
            "with schema: {slot: {question_id: 'pass' | 'fail: <reason>'}, "
            "_png_hashes: {slot: 'sha256:...'}}"
        ),
        "slots": [],
    }
    for slot, png_path in rendered.items():
        checklist = json.loads((CHECKS_DIR / f"{slot}.json").read_text())
        todo["slots"].append({
            "slot": slot,
            "png_path": str(png_path),
            "png_hash": _hash_png(png_path),
            "checklist": checklist,
        })
    (ARTIFACTS / "vision_review.todo.json").write_text(json.dumps(todo, indent=2))


# Per-slot metric expectations (relaxed for fixture region's small density).
SLOT_METRIC_EXPECT = {
    "wide_aerial":         {"min_ground": 0.30},
    "wide_mid_drone":      {"min_ground": 0.40},
    "wide_low_drone":      {"min_ground": 0.30},
    "wide_fpv":            {"min_ground": 0.50},
    "close_building":      {"min_ground": 0.05},
    "close_tree":          {"min_ground": 0.05},
    "close_ground_patch":  {"min_ground": 0.70},
    "close_seam":          {"min_ground": 0.30},
}


@pytest.mark.parametrize("slot,_alt,_framing,_preset", SLOTS)
def test_slot_metrics(slot, _alt, _framing, _preset, rendered_slots):
    img = assertions.load_png(rendered_slots[slot])
    exp = SLOT_METRIC_EXPECT[slot]
    assertions.ground_visible(img, min_ratio=exp["min_ground"])
    assertions.no_haze_overpower(img, max_blue_dominance=1.5)
    metrics = assertions.summary(img)
    golden_path = GOLDEN_DIR / f"{slot}.json"
    if golden_path.exists():
        golden = json.loads(golden_path.read_text())
        assertions.metrics_within_tolerance(metrics, golden, tol=0.20)


@pytest.mark.parametrize("slot,_alt,_framing,_preset", SLOTS)
def test_slot_rmse_tripwire(slot, _alt, _framing, _preset, rendered_slots):
    lkg = GOLDEN_DIR / f"{slot}_lkg.png"
    if not lkg.exists():
        pytest.skip(f"no last-known-good image for {slot}")
    img = assertions.load_png(rendered_slots[slot])
    lkg_img = assertions.load_png(lkg)
    if img.shape != lkg_img.shape:
        pytest.skip(f"shape mismatch — regenerate LKG for {slot}")
    assertions.rmse_tripwire(img, lkg_img, max_rmse=0.30)


def test_vision_review(rendered_slots):
    """Triggers vision review via Claude Code. Hash-gated cache."""
    _write_todo(rendered_slots)
    results = _load_vision_results()
    if results is None:
        pytest.xfail("No vision_review.results.json — run /review-renders in Claude Code")

    stale = [
        slot for slot, png in rendered_slots.items()
        if results.get("_png_hashes", {}).get(slot) != _hash_png(png)
    ]
    if stale:
        pytest.xfail(f"Vision review stale for {stale} — re-run /review-renders")

    failures = []
    for slot, _png in rendered_slots.items():
        checklist = json.loads((CHECKS_DIR / f"{slot}.json").read_text())
        slot_results = results.get(slot, {})
        for q in checklist["questions"]:
            ans = slot_results.get(q["id"], "missing")
            if q["fail_severity"] == "blocker" and not ans.startswith("pass"):
                failures.append(f"{slot}/{q['id']}: {ans}")
    assert not failures, "Vision review blocker failures:\n" + "\n".join(failures)
