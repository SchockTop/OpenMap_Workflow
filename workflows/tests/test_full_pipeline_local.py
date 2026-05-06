"""Tests for the offline / proxy ("bring your own tiles") mode of
`workflows.full_pipeline`.

These exercise everything that does NOT need the OpenMap_Unifier or
openmap_blender_tools submodules: argparse plumbing, the local-file
collector, and the bbox/region resolution branches. The download and the
Blender phases are stubbed.
"""
from __future__ import annotations
import sys
from pathlib import Path
from typing import Optional

import pytest

from workflows import full_pipeline as fp


# ---------------------------------------------------------------------------
# _collect_local_files
# ---------------------------------------------------------------------------

def test_collect_local_files_from_directory(tmp_path: Path):
    (tmp_path / "a.tif").write_bytes(b"x")
    (tmp_path / "b.TIF").write_bytes(b"x")  # case-insensitive
    (tmp_path / "ignore.txt").write_bytes(b"x")
    sub = tmp_path / "sub"
    sub.mkdir()
    (sub / "c.tiff").write_bytes(b"x")  # recursive
    out = fp._collect_local_files([tmp_path], (".tif", ".tiff"))
    names = sorted(p.name for p in out)
    assert names == ["a.tif", "b.TIF", "c.tiff"]


def test_collect_local_files_from_explicit_files(tmp_path: Path):
    a = tmp_path / "a.tif"; a.write_bytes(b"x")
    b = tmp_path / "b.tif"; b.write_bytes(b"x")
    out = fp._collect_local_files([a, b], (".tif",))
    assert sorted(p.name for p in out) == ["a.tif", "b.tif"]


def test_collect_local_files_warns_on_missing(tmp_path: Path, capsys):
    bogus = tmp_path / "does_not_exist"
    out = fp._collect_local_files([bogus], (".tif",))
    assert out == []
    err = capsys.readouterr().err
    assert "does not exist" in err


def test_collect_local_files_mixed_files_and_dirs(tmp_path: Path):
    (tmp_path / "explicit.tif").write_bytes(b"x")
    d = tmp_path / "d"; d.mkdir()
    (d / "from_dir.tif").write_bytes(b"x")
    out = fp._collect_local_files([tmp_path / "explicit.tif", d], (".tif",))
    names = sorted(p.name for p in out)
    assert names == ["explicit.tif", "from_dir.tif"]


# ---------------------------------------------------------------------------
# phase1_collect_local — dataset-aware extension dispatch
# ---------------------------------------------------------------------------

def test_phase1_collect_local_picks_right_extensions(tmp_path: Path):
    dgm_dir = tmp_path / "dgm"; dgm_dir.mkdir()
    dop_dir = tmp_path / "dop"; dop_dir.mkdir()
    lod2_dir = tmp_path / "lod2"; lod2_dir.mkdir()

    (dgm_dir / "h1.tif").write_bytes(b"x")
    (dgm_dir / "h2.tif").write_bytes(b"x")
    (dop_dir / "o1.tif").write_bytes(b"x")
    (lod2_dir / "b1.gml").write_bytes(b"x")
    (lod2_dir / "b2.zip").write_bytes(b"x")
    (lod2_dir / "ignored.tif").write_bytes(b"x")  # wrong extension for lod2

    out = fp.phase1_collect_local({
        "dgm1":  [dgm_dir],
        "dop40": [dop_dir],
        "lod2":  [lod2_dir],
    })
    assert {p.name for p in out["dgm1"]}  == {"h1.tif", "h2.tif"}
    assert {p.name for p in out["dop40"]} == {"o1.tif"}
    assert {p.name for p in out["lod2"]}  == {"b1.gml", "b2.zip"}


def test_phase1_collect_local_warns_on_empty(tmp_path: Path, capsys):
    empty = tmp_path / "empty"; empty.mkdir()
    out = fp.phase1_collect_local({"dgm1": [empty]})
    assert out["dgm1"] == []
    assert "no" in capsys.readouterr().err.lower()


def test_phase1_collect_local_skips_datasets_with_no_paths():
    out = fp.phase1_collect_local({"dgm1": [], "dop40": []})
    assert out == {}


# ---------------------------------------------------------------------------
# main() — argparse + bbox/region resolution. Phase 5 (Blender) is stubbed.
# ---------------------------------------------------------------------------

@pytest.fixture
def stub_phases(monkeypatch):
    """Replace phases 1/2/3/5 with no-op stubs; capture the bbox handed to
    phase 5 so tests can assert on it."""
    captured: dict[str, object] = {}

    def fake_download(poly, datasets, out_root):
        captured["downloaded"] = True
        # Return a non-empty dgm1 so the pipeline doesn't bail.
        out_root.mkdir(parents=True, exist_ok=True)
        f = out_root / "dgm1" / "tile.tif"
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_bytes(b"x")
        return {"dgm1": [f]}

    def fake_phase2(downloads, bbox, out_root):
        captured["bbox"] = tuple(bbox)
        if not downloads.get("dgm1"):
            return None, None  # mirrors real behaviour — no DGM tiles -> no heightmap
        out_root.mkdir(parents=True, exist_ok=True)
        hm = out_root / "heightmap.tif"
        hm.write_bytes(b"x")
        return hm, None

    def fake_phase3(downloads, out_root):
        return None

    def fake_phase5(*args, **kwargs):
        captured["phase5_args"] = (args, kwargs)
        return 0

    monkeypatch.setattr(fp, "phase1_download",  fake_download)
    monkeypatch.setattr(fp, "phase2_preprocess", fake_phase2)
    monkeypatch.setattr(fp, "phase3_lod2",       fake_phase3)
    monkeypatch.setattr(fp, "phase5_blender",    fake_phase5)
    monkeypatch.setattr(fp, "phase4_synthetic_waypoints",
                        lambda bbox, out, preset_name="x": (out.parent.mkdir(parents=True, exist_ok=True), out.write_text("lat,lon,alt\n"), out)[2])
    return captured


def test_main_skip_download_with_local_dgm(tmp_path: Path, stub_phases):
    dgm = tmp_path / "dgm"; dgm.mkdir()
    (dgm / "tile.tif").write_bytes(b"x")
    rc = fp.main([
        "--skip-download",
        "--region", "muc-marienplatz-50m",
        "--local-dgm", str(dgm),
        "--data-dir", str(tmp_path / "data"),
    ])
    assert rc == 0
    assert "downloaded" not in stub_phases  # phase1_download was NOT called
    assert "bbox" in stub_phases


def test_main_local_flag_implies_skip_download(tmp_path: Path, stub_phases):
    """Passing --local-dgm without --skip-download still skips the download."""
    dgm = tmp_path / "dgm"; dgm.mkdir()
    (dgm / "tile.tif").write_bytes(b"x")
    rc = fp.main([
        "--region", "muc-marienplatz-50m",
        "--local-dgm", str(dgm),
        "--data-dir", str(tmp_path / "data"),
    ])
    assert rc == 0
    assert "downloaded" not in stub_phases


def test_main_explicit_bbox_overrides_region(tmp_path: Path, stub_phases):
    dgm = tmp_path / "dgm"; dgm.mkdir()
    (dgm / "tile.tif").write_bytes(b"x")
    rc = fp.main([
        "--skip-download",
        "--region", "muc-marienplatz-50m",
        "--bbox-utm32n", "100", "200", "300", "400",
        "--local-dgm", str(dgm),
        "--data-dir", str(tmp_path / "data"),
    ])
    assert rc == 0
    assert stub_phases["bbox"] == (100.0, 200.0, 300.0, 400.0)


def test_main_explicit_bbox_works_without_region(tmp_path: Path, stub_phases):
    dgm = tmp_path / "dgm"; dgm.mkdir()
    (dgm / "tile.tif").write_bytes(b"x")
    rc = fp.main([
        "--skip-download",
        "--bbox-utm32n", "100", "200", "300", "400",
        "--local-dgm", str(dgm),
        "--data-dir", str(tmp_path / "data"),
    ])
    assert rc == 0
    assert stub_phases["bbox"] == (100.0, 200.0, 300.0, 400.0)


def test_main_errors_without_region_or_bbox(tmp_path: Path, stub_phases):
    with pytest.raises(SystemExit):
        fp.main([
            "--skip-download",
            "--local-dgm", str(tmp_path),
            "--data-dir", str(tmp_path / "data"),
        ])


def test_main_errors_when_no_dgm_supplied(tmp_path: Path, stub_phases, capsys):
    rc = fp.main([
        "--skip-download",
        "--region", "muc-marienplatz-50m",
        "--data-dir", str(tmp_path / "data"),  # no --local-dgm and no data/raw/dgm1
    ])
    assert rc == 1
    assert "no DGM1" in capsys.readouterr().err


def test_main_skip_download_falls_back_to_data_raw(tmp_path: Path, stub_phases):
    """With --skip-download and no --local-* flags, the pipeline should
    fall back to data/raw/<dataset>/ (re-runs after a one-time download)."""
    data_dir = tmp_path / "data"
    raw_dgm = data_dir / "raw" / "dgm1"
    raw_dgm.mkdir(parents=True)
    (raw_dgm / "tile.tif").write_bytes(b"x")
    rc = fp.main([
        "--skip-download",
        "--region", "muc-marienplatz-50m",
        "--data-dir", str(data_dir),
    ])
    assert rc == 0
    assert "downloaded" not in stub_phases


def test_main_help_runs_without_submodules(capsys):
    """--help must work even when OpenMap_Unifier isn't checked out;
    that's the proxy user's first interaction."""
    with pytest.raises(SystemExit) as exc:
        fp.main(["--help"])
    assert exc.value.code == 0
    captured = capsys.readouterr()
    assert "--skip-download" in captured.out
    assert "--local-dgm" in captured.out
    assert "--bbox-utm32n" in captured.out
