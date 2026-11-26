"""
Builds the walkable maps used by Unity's A* grid (GridFromTexture) for every floor.

The original navigable bitmasks were traced from the floor-plan images, while the 3D walls were
modelled separately; on some floors the walls stand 0.3-0.5 m inside the "walkable" area, so
routes that looked centred brushed against real walls. This script combines:
  * Final Datasets/Navigable_Bitmaps_3000_x_1385/<n>_navigable_bitmask.png
  * Final Datasets/Wall_Mesh_Footprint/wall_triangles.csv  (vertical wall triangles exported from
    the Unity scene with Tools > Mall > Export Wall Footprint, in the original plan coordinates)
  * Unity Files/Assets/Textures/MallPlan/floor_<n>_plan.png (building footprint, alpha > 0)
into Unity Files/Assets/Textures/NavRefined/floor_<n>_walkable.png (white = walkable):
walkable = navigable AND inside the building AND at least WALL_MARGIN_M away from any 3D wall.

Run from the repository root: python "Floor Plans Image Processing/build_unity_walkable_maps.py"
"""
import csv
import os
import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Final Datasets")
UNITY = os.path.join(ROOT, "Unity Files", "Assets", "Textures")
OUT = os.path.join(UNITY, "NavRefined")

W, H = 3000, 1385
SOURCE_UNITS_PER_PIXEL = 0.02645833   # coordinates in wall_triangles.csv (before the 4x footprint scale)
METRES_PER_PIXEL = SOURCE_UNITS_PER_PIXEL * 4.0  # MallWorldScale.HorizontalScale
WALL_MARGIN_M = 0.45


def wall_rasters():
    walls = [np.zeros((H, W), np.uint8) for _ in range(4)]
    with open(os.path.join(DATA, "Wall_Mesh_Footprint", "wall_triangles.csv")) as f:
        for row in csv.reader(f):
            v = list(map(float, row))
            floor = int(np.clip(np.floor((min(v[1::3]) + 0.01) / 5.0), 0, 3))
            pts = np.array([[round(-v[i] / SOURCE_UNITS_PER_PIXEL), round(v[i + 2] / SOURCE_UNITS_PER_PIXEL)] for i in (0, 3, 6)], np.int32)
            cv2.polylines(walls[floor], [pts], True, 255, 1)
    return walls


def main():
    os.makedirs(OUT, exist_ok=True)
    margin_px = WALL_MARGIN_M / METRES_PER_PIXEL
    for floor, walls in enumerate(wall_rasters()):
        nav = cv2.imread(os.path.join(DATA, "Navigable_Bitmaps_3000_x_1385", f"{floor}_navigable_bitmask.png"), cv2.IMREAD_GRAYSCALE) > 127
        plan = cv2.imread(os.path.join(UNITY, "MallPlan", f"floor_{floor}_plan.png"), cv2.IMREAD_UNCHANGED)[:, :, 3]
        wall_distance = cv2.distanceTransform((walls == 0).astype(np.uint8), cv2.DIST_L2, 5)
        walkable = nav & (plan > 0) & (wall_distance > margin_px)
        walkable = cv2.morphologyEx(walkable.astype(np.uint8), cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))

        # Keep the main connected concourse (plus any large separate areas); drop isolated slivers.
        count, labels, stats, _ = cv2.connectedComponentsWithStats(walkable, connectivity=8)
        keep = np.zeros_like(walkable)
        largest = stats[1:, cv2.CC_STAT_AREA].max() if count > 1 else 0
        for label in range(1, count):
            if stats[label, cv2.CC_STAT_AREA] >= 0.05 * largest:
                keep[labels == label] = 255
        cv2.imwrite(os.path.join(OUT, f"floor_{floor}_walkable.png"), keep)
        print(f"floor {floor}: walkable {keep.mean() / 255 * 100:.1f}% of map, {count - 1} regions before cleanup")


if __name__ == "__main__":
    main()
