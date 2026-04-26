"""Unit tests for multi_altitude_demo helpers."""
import pytest


def test_make_contact_sheet_with_no_images_does_nothing(tmp_path, capsys):
    from workflows.multi_altitude_demo import make_contact_sheet
    out = tmp_path / "sheet.png"
    make_contact_sheet([None, None, None], out)
    assert not out.exists()


def test_make_contact_sheet_with_one_image_creates_grid(tmp_path):
    from workflows.multi_altitude_demo import make_contact_sheet
    from PIL import Image
    p = tmp_path / "fpv-walk.png"
    Image.new("RGB", (100, 100), (50, 100, 50)).save(p)
    out = tmp_path / "sheet.png"
    make_contact_sheet([p, None, None, None, None, None], out)
    assert out.exists()
    assert out.stat().st_size > 100  # non-empty
