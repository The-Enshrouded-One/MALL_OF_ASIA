"""
Plans the "life" of the concourses for Unity: the open atrium voids and where benches and
planters go. Run AFTER build_unity_floor_textures.py; it (re)builds the walkable maps itself.

Outputs
  Unity Files/Assets/Textures/MallPlan/atrium_mask.png   RGBA mask read by the Mall/Plan Surface shader
      R = void in the floor-1 slab, G = void in the floor-2 slab,
      B = skylight over the top of the atrium (ceiling of floor 2),
      A = skylight strips over the wide top-floor concourses (ceiling of floor 3)
  Unity Files/Assets/Textures/NavRefined/floor_<n>_walkable.png   updated: atrium voids are not walkable
  Unity Files/Assets/Resources/MallDecorLayout.json   balustrade outlines and decor placements (world metres)

Rules
  * A slab is opened only where that floor's concourse stays >= VOID_CLEARANCE_M wide around the
    void and the floor below is walkable underneath, so no walkway is lost.
  * Decor goes only where the concourse is >= 2 * DECOR_CLEARANCE_M wide, spaced DECOR_SPACING_M
    apart, away from lifts and voids; its footprint is later blocked in the A* grid.

Run from the repository root: python "Floor Plans Image Processing/build_unity_decor_layout.py"
"""
import json
import math
import os
import sys

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_unity_walkable_maps as walkable_maps  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Unity Files", "Assets")
NAV = os.path.join(ASSETS, "Textures", "NavRefined")
M = walkable_maps.METRES_PER_PIXEL  # metres per plan pixel in Unity

VOID_CLEARANCE_M = 3.5
VOID_MIN_AREA_M2 = 30.0
VOID_EDGE_MARGIN_M = 0.6      # balustrade + a step back from it
DECOR_CLEARANCE_M = 4.6
DECOR_SPACING_M = 24.0
LIFT_PIXELS = [(1014, 477), (2020, 399)]   # keep in sync with InterFloorController.DefaultLifts
LIFT_KEEP_OUT_M = 9.0


def load_walkable():
    return [cv2.imread(os.path.join(NAV, f"floor_{f}_walkable.png"), cv2.IMREAD_GRAYSCALE) > 0 for f in range(4)]


def clearance(walk):
    return cv2.distanceTransform(walk.astype(np.uint8), cv2.DIST_L2, 5) * M


def smooth_mask(mask, radius_px):
    mask = cv2.GaussianBlur(mask.astype(np.float32), (0, 0), radius_px) > 0.5
    return mask


def void_in_slab(D, floor):
    """Void through the slab of `floor` (i.e. above floor-1)."""
    core = (D[floor] >= VOID_CLEARANCE_M) & (D[floor - 1] >= 1.5)
    core = cv2.morphologyEx(core.astype(np.uint8), cv2.MORPH_OPEN, np.ones((9, 9), np.uint8))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(core)
    keep = np.zeros(core.shape, bool)
    for label in range(1, count):
        if stats[label, cv2.CC_STAT_AREA] * M * M >= VOID_MIN_AREA_M2:
            keep |= labels == label
    return smooth_mask(keep, 3)


def world(px, py):
    return [round(-px * M, 3), round(py * M, 3)]


def outlines(mask, floor):
    contours, _ = cv2.findContours(mask.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    result = []
    for contour in contours:
        contour = cv2.approxPolyDP(contour, 0.25 / M, True)
        if len(contour) < 3:
            continue
        points = []
        for (px, py) in contour[:, 0, :]:
            points.extend(world(px, py))
        result.append({"floor": floor, "points": points})
    return result


def corridor_axis(walk, x, y, radius_px):
    """Main direction of the walkable area around (x, y), in plan pixel axes."""
    y0, y1 = max(0, y - radius_px), min(walk.shape[0], y + radius_px)
    x0, x1 = max(0, x - radius_px), min(walk.shape[1], x + radius_px)
    ys, xs = np.nonzero(walk[y0:y1, x0:x1])
    if len(xs) < 10:
        return 1.0, 0.0
    pts = np.stack([xs - xs.mean(), ys - ys.mean()])
    eigenvalues, eigenvectors = np.linalg.eigh(np.cov(pts))
    ax, ay = eigenvectors[:, np.argmax(eigenvalues)]
    return float(ax), float(ay)


def decor_spots(walk, D, floor, voids):
    candidates = D >= DECOR_CLEARANCE_M
    for (lx, ly) in LIFT_PIXELS:
        cv2.circle(candidates.view(np.uint8), (lx, ly), int(LIFT_KEEP_OUT_M / M), 0, -1)
    if voids is not None:
        near_void = cv2.dilate(voids.astype(np.uint8), np.ones((int(4 / M),) * 2, np.uint8)) > 0
        candidates &= ~near_void

    score = np.where(candidates, D, 0).astype(np.float32)
    spots = []
    while True:
        y, x = np.unravel_index(np.argmax(score), score.shape)
        if score[y, x] <= 0:
            break
        ax, ay = corridor_axis(walk, x, y, int(7 / M))
        wx, wz = -ax, ay   # plan axes -> world axes
        yaw = math.degrees(math.atan2(wx, wz))
        kind = "cluster" if D[y, x] >= DECOR_CLEARANCE_M + 0.6 else "planter"
        spots.append({"floor": floor, "x": world(x, y)[0], "z": world(x, y)[1], "yaw": round(yaw, 1), "kind": kind})
        cv2.circle(score, (int(x), int(y)), int(DECOR_SPACING_M / M), 0, -1)
    return spots


def main():
    walkable_maps.main()
    walk = load_walkable()
    D = [clearance(w) for w in walk]

    void1 = void_in_slab(D, 1)
    void2 = void_in_slab(D, 2)
    skylight_atrium = cv2.dilate(void2.astype(np.uint8), np.ones((int(1.5 / M),) * 2, np.uint8)) > 0
    skylight_top = smooth_mask(D[3] >= 3.0, 4)

    mask = np.dstack([void1, void2, skylight_atrium, skylight_top]).astype(np.uint8) * 255
    cv2.imwrite(os.path.join(ASSETS, "Textures", "MallPlan", "atrium_mask.png"), cv2.cvtColor(mask, cv2.COLOR_RGBA2BGRA))

    # Voids are not walkable on the floor whose slab is open; keep a margin for the balustrade.
    margin = np.ones((int(2 * VOID_EDGE_MARGIN_M / M) + 1,) * 2, np.uint8)
    for floor, void in ((1, void1), (2, void2)):
        walk[floor] &= ~(cv2.dilate(void.astype(np.uint8), margin) > 0)
        cv2.imwrite(os.path.join(NAV, f"floor_{floor}_walkable.png"), walk[floor].astype(np.uint8) * 255)
    D = [clearance(w) for w in walk]

    layout = {"atria": outlines(void1, 1) + outlines(void2, 2), "decor": []}
    voids_on = {0: None, 1: void1, 2: void2, 3: None}
    for floor in range(4):
        layout["decor"].extend(decor_spots(walk[floor], D[floor], floor, voids_on[floor]))

    with open(os.path.join(ASSETS, "Resources", "MallDecorLayout.json"), "w") as f:
        json.dump(layout, f, indent=1)

    for floor in range(4):
        items = [d for d in layout["decor"] if d["floor"] == floor]
        print(f"floor {floor}: {len(items)} decor spots ({sum(d['kind'] == 'cluster' for d in items)} seating clusters)")
    print(f"atrium voids: slab 1 {void1.sum() * M * M:.0f} m2, slab 2 {void2.sum() * M * M:.0f} m2, "
          f"{len(layout['atria'])} balustrade outlines")


if __name__ == "__main__":
    main()
