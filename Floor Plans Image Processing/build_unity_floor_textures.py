"""
Builds the per-floor "plan" textures used by the Unity mall shaders.

For every floor it combines
  * Stores_Detected/stores_identified_bitmask_<n>.png  (shop outlines)
  * Actual_Floor_Maps/<n>.png                           (official, colour-coded map)
into one RGBA texture:
  RGB = the shop's category colour (text and icons removed), or the corridor colour
  A   = region type: 255 corridor, 160 shop, 60 outline, 0 outside the mall
Output: Unity Files/Assets/Textures/MallPlan/floor_<n>_plan.png

Run from the repository root:  python "Floor Plans Image Processing/build_unity_floor_textures.py"
"""
import os
import cv2
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Final Datasets")
OUT = os.path.join(ROOT, "Unity Files", "Assets", "Textures", "MallPlan")

CORRIDOR = (236, 232, 224)      # light warm stone
SERVICE = (196, 199, 204)       # shops whose colour could not be read
OUTLINE = (52, 56, 64)


def dominant_colour(pixels):
    """Most common saturated colour in a region, ignoring black/white text and icons."""
    if len(pixels) == 0:
        return None
    hsv = cv2.cvtColor(pixels.reshape(-1, 1, 3), cv2.COLOR_RGB2HSV).reshape(-1, 3)
    keep = (hsv[:, 1] > 60) & (hsv[:, 2] > 70)
    coloured = pixels[keep]
    if len(coloured) < max(30, 0.15 * len(pixels)):
        return None
    quantised = (coloured // 8).astype(np.int32)
    keys = quantised[:, 0] * 65536 + quantised[:, 1] * 256 + quantised[:, 2]
    values, counts = np.unique(keys, return_counts=True)
    best = values[np.argmax(counts)]
    mask = keys == best
    return tuple(int(c) for c in coloured[mask].mean(axis=0))


def soften(colour, amount=0.22):
    """Pull the map's poster colours slightly toward white so they read as floor finishes."""
    return tuple(int(c + (255 - c) * amount) for c in colour)


def build_floor(n):
    outline = cv2.imread(os.path.join(DATA, "Stores_Detected_3000_x_1385", f"stores_identified_bitmask_{n}.png"), cv2.IMREAD_GRAYSCALE)
    plan = cv2.cvtColor(cv2.imread(os.path.join(DATA, "Actual_Floor_Maps_3000_x_1385", f"{n}.png"), cv2.IMREAD_COLOR), cv2.COLOR_BGR2RGB)
    h, w = outline.shape

    lines = outline > 127
    lines = cv2.dilate(lines.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0

    count, labels, stats, _ = cv2.connectedComponentsWithStats((~lines).astype(np.uint8), connectivity=4)

    navigable = cv2.imread(os.path.join(DATA, "Navigable_Bitmaps_3000_x_1385", f"{n}_navigable_bitmask.png"), cv2.IMREAD_GRAYSCALE) > 127
    # Close the holes that corridor labels ("TO / FROM UPPER GROUND", arrows) punch into the walkable mask.
    navigable = cv2.morphologyEx(navigable.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((17, 17), np.uint8)) > 0

    border = set(np.unique(np.concatenate([labels[0], labels[-1], labels[:, 0], labels[:, -1]])))
    border.discard(0)
    nav_fraction = np.bincount(labels.ravel(), weights=navigable.ravel().astype(np.float64), minlength=count) / np.maximum(stats[:, cv2.CC_STAT_AREA], 1)

    rgb = np.zeros((h, w, 3), np.uint8)
    alpha = np.zeros((h, w), np.uint8)
    shops = 0
    for label in range(1, count):
        area = stats[label, cv2.CC_STAT_AREA]
        if label in border or area < 150 or area > 0.12 * h * w or nav_fraction[label] > 0.5:
            continue
        region = labels == label
        colour = dominant_colour(plan[region])
        if colour is None and area < 20000:
            continue  # escalator / lift / signage icons drawn in the corridor: keep them as floor
        rgb[region] = soften(colour) if colour else SERVICE
        alpha[region] = 160
        shops += 1

    # The walkable mask also covers the area around the building, so limit corridors to the mall
    # footprint: close the gaps between shop blocks with a large kernel and fill enclosed holes.
    pad = 350
    blocks = cv2.copyMakeBorder((alpha > 0).astype(np.uint8), pad, pad, pad, pad, cv2.BORDER_CONSTANT, value=0)
    blocks = cv2.morphologyEx(blocks, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (321, 321)))
    contours, _ = cv2.findContours(blocks, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    filled = np.zeros_like(blocks)
    cv2.drawContours(filled, contours, -1, 1, thickness=cv2.FILLED)
    footprint = filled[pad:-pad, pad:-pad] > 0
    footprint = cv2.erode(footprint.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0

    corridor = (navigable | ~lines) & footprint & (alpha == 0)
    rgb[corridor] = CORRIDOR
    alpha[corridor] = 255

    inside = alpha > 0
    near_inside = cv2.dilate(inside.astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
    near_shop = cv2.dilate((alpha == 160).astype(np.uint8), np.ones((7, 7), np.uint8)) > 0
    outline_px = lines & near_shop
    rgb[outline_px] = OUTLINE
    alpha[outline_px] = 60

    # Fill the leftover one-pixel gaps (small specks) from their neighbours.
    gaps = near_inside & (alpha == 0)
    if gaps.any():
        filled = cv2.inpaint(rgb, gaps.astype(np.uint8), 3, cv2.INPAINT_TELEA)
        rgb[gaps] = filled[gaps]
        alpha[gaps] = 160

    os.makedirs(OUT, exist_ok=True)
    out = np.dstack([rgb, alpha])
    path = os.path.join(OUT, f"floor_{n}_plan.png")
    cv2.imwrite(path, cv2.cvtColor(out, cv2.COLOR_RGBA2BGRA))
    print(f"floor {n}: {shops} shop regions -> {path}")


if __name__ == "__main__":
    for floor in range(4):
        build_floor(floor)
