"""Animate a missile in Blender along a CSV trajectory.

Usage (Blender Text Editor):
    1. Open this file in Blender's Text Editor (Scripting workspace).
    2. Edit ``CSV_PATH`` below to point at ``traj_1_with_headings.csv``.
    3. Press "Run Script". A control empty + missile placeholder are created
       on first run. Moving / scaling the control empty moves and scales the
       whole trajectory (the missile is parented to it).

CSV format (header row required, time in seconds, rotations in radians)::

    time,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z
    0.000,0.0,-5.0,0.0,0.0,0.17453292519943,0.0
    0.001,1.0e-06,-5.0,0.0,0.0,0.17453292519943,0.0
    ...

Why keyframes instead of drivers: a driver fires per-frame and would have to
re-parse the CSV (or hold it in a global), which is fragile across file
reloads and doesn't survive renders on a farm. Baked keyframes with LINEAR
interpolation reproduce the sampled velocity exactly and are portable.
"""
from __future__ import annotations

import csv
from pathlib import Path

import bpy


CSV_PATH = r"C:\path\to\traj_1_with_headings.csv"
MISSILE_NAME = "Missile"
CONTROL_NAME = "Missile_Control"
KEYFRAME_INTERPOLATION = "LINEAR"


def load_trajectory(csv_path: str | Path) -> list[dict]:
    rows: list[dict] = []
    with open(csv_path, newline="") as f:
        for r in csv.DictReader(f):
            rows.append({
                "t":  float(r["time"]),
                "px": float(r["position_x"]),
                "py": float(r["position_y"]),
                "pz": float(r["position_z"]),
                "rx": float(r["rotation_x"]),
                "ry": float(r["rotation_y"]),
                "rz": float(r["rotation_z"]),
            })
    return rows


def ensure_control(name: str) -> bpy.types.Object:
    obj = bpy.data.objects.get(name)
    if obj is not None:
        return obj
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "ARROWS"
    empty.empty_display_size = 2.0
    bpy.context.collection.objects.link(empty)
    return empty


def ensure_missile(name: str) -> bpy.types.Object:
    obj = bpy.data.objects.get(name)
    if obj is not None:
        return obj
    bpy.ops.mesh.primitive_cone_add(radius1=0.2, depth=1.0, location=(0, 0, 0))
    cone = bpy.context.active_object
    cone.name = name
    return cone


def clear_action(obj: bpy.types.Object) -> None:
    if obj.animation_data and obj.animation_data.action:
        bpy.data.actions.remove(obj.animation_data.action)


def animate_missile(
    missile: bpy.types.Object,
    control: bpy.types.Object,
    rows: list[dict],
) -> None:
    if missile.parent is not control:
        missile.parent = control
        missile.matrix_parent_inverse.identity()

    clear_action(missile)
    missile.rotation_mode = "XYZ"

    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    t0 = rows[0]["t"]

    for row in rows:
        frame = scene.frame_start + (row["t"] - t0) * fps
        missile.location = (row["px"], row["py"], row["pz"])
        missile.rotation_euler = (row["rx"], row["ry"], row["rz"])
        missile.keyframe_insert("location", frame=frame)
        missile.keyframe_insert("rotation_euler", frame=frame)

    action = missile.animation_data.action
    for fc in action.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = KEYFRAME_INTERPOLATION

    last_frame = int(scene.frame_start + (rows[-1]["t"] - t0) * fps) + 1
    scene.frame_end = max(scene.frame_end, last_frame)


def main() -> None:
    rows = load_trajectory(CSV_PATH)
    if not rows:
        raise RuntimeError(f"CSV {CSV_PATH!r} had no rows")

    control = ensure_control(CONTROL_NAME)
    missile = ensure_missile(MISSILE_NAME)
    animate_missile(missile, control, rows)

    duration = rows[-1]["t"] - rows[0]["t"]
    print(
        f"[missile_traj] {len(rows)} samples baked onto {MISSILE_NAME!r} "
        f"(parent={CONTROL_NAME!r}, duration={duration:.3f}s)"
    )


if __name__ == "__main__":
    main()
