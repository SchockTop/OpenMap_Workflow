"""Convert flight_path.csv from WGS84 to UTM32N for Blender (no pyproj in Blender Python)."""
from __future__ import annotations
import csv
from pathlib import Path
from pyproj import Transformer

WORKFLOW_ROOT = Path(__file__).resolve().parent.parent
SRC = WORKFLOW_ROOT / "data" / "flight_path.csv"
DST = WORKFLOW_ROOT / "data" / "processed" / "flight_path_utm.csv"

t = Transformer.from_crs("EPSG:4326", "EPSG:25832", always_xy=True)

rows_in = []
with open(SRC, newline="") as f:
    reader = csv.DictReader(f)
    for row in reader:
        rows_in.append(row)

with open(DST, "w", newline="") as f:
    writer = csv.writer(f)
    writer.writerow(["utm_x", "utm_y", "alt"])
    for r in rows_in:
        x, y = t.transform(float(r["lon"]), float(r["lat"]))
        writer.writerow([f"{x:.2f}", f"{y:.2f}", r["alt"]])

print(f"Written {len(rows_in)} points to {DST}")
with open(DST) as f:
    for i, line in enumerate(f):
        print(line.rstrip())
        if i >= 4:
            break
