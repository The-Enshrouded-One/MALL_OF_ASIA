import cv2
import numpy as np
import easyocr
import matplotlib.pyplot as plt
import os

# --- Configuration ---
ORIGINAL_IMAGE_PATH = "Original Images/-1.jpg" # <--- The color image with text
WALL_BITMAP_PATH = "Wall Bitmap/-1_walls_bitmask_better.png" # <--- The B/W wall bitmap you generated
OUTPUT_FOLDER = "detected_stores"

if not os.path.exists(OUTPUT_FOLDER):
    os.makedirs(OUTPUT_FOLDER)

def preprocess_crop_for_ocr(crop_img):
    """
    Enhances a small crop to make text clearer for OCR.
    """
    # 1. Convert to grayscale
    # crop_img = cv2.copyMakeBorder(crop_img, 10, 10, 10, 10, cv2.BORDER_CONSTANT, value=(255, 255, 255))
    gray = cv2.cvtColor(crop_img, cv2.COLOR_BGR2GRAY)
    
    # 2. Scaling: OCR works best if text is roughly 30px tall. 
    # Upscaling the crop helps significantly with small labels.
    scale_factor = 2
    if gray.shape[0] * gray.shape[1] < 5000:  # Only upscale small images
        scale_factor = 3
    width = int(gray.shape[1] * scale_factor)
    height = int(gray.shape[0] * scale_factor)
    # upscaled = cv2.resize(gray, (width, height), interpolation=cv2.INTER_CUBIC)
    upscaled = cv2.resize(gray, (width, height), interpolation=cv2.INTER_LANCZOS4)
    
    # 3. Thresholding: Make text black and background white
    # We use Otsu's binarization which automatically finds the best threshold
    # _, binary = cv2.threshold(upscaled, 0, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)

    # 5. Contrast Enhancement (CLAHE)
    # This makes the text 'pop' from the background before we threshold
    clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
    enhanced = clahe.apply(upscaled)

    _, binary = cv2.threshold(enhanced, 0, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)


    # 7. Denoising (Morphological Opening)
    # Removes small white noise dots
    # kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (1, 1)) # Very small kernel to not eat text
    # cleaned = cv2.morphologyEx(binary, cv2.MORPH_OPEN, kernel)

    # 8. Final Sharpening (Optional but helpful for blocky fonts)
    # kernel_sharpen = np.array([[-1,-1,-1], [-1,9,-1], [-1,-1,-1]])
    # cleaned = cv2.filter2D(cleaned, -1, kernel_sharpen)

    return binary

# def preprocess_crop_for_ocr(crop_img):
#     """
#     Enhances a small crop to make text clearer for OCR.
#     """
#     gray = cv2.cvtColor(crop_img, cv2.COLOR_BGR2GRAY)
#     scale_factor = 1
#     if gray.shape[0] * gray.shape[1] < 5000:  # Only upscale small images
#         scale_factor = 2
#     width = int(gray.shape[1] * scale_factor)
#     height = int(gray.shape[0] * scale_factor)
#     upscaled = cv2.resize(gray, (width, height), interpolation=cv2.INTER_CUBIC)
#     _, binary = cv2.threshold(upscaled, 0, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)
#     return binary

def process_floor_plan_hybrid(original_path, wall_bitmap_path):
    print(f"--- Loading Images ---")
    # Load Original (Color) for Text Extraction
    img_original = cv2.imread(original_path)
    if img_original is None:
        print("Error: Could not load Original Image.")
        return

    # Load Wall Bitmap (B/W) for Store Detection
    img_walls = cv2.imread(wall_bitmap_path, cv2.IMREAD_GRAYSCALE)
    if img_walls is None:
        print("Error: Could not load Wall Bitmap.")
        return

    # Ensure dimensions match (resize walls to match original if needed)
    if img_original.shape[:2]!= img_walls.shape[:2]:
        print("Resizing wall bitmap to match original image dimensions...")
        img_walls = cv2.resize(img_walls, (img_original.shape, img_original.shape))

    img_original_copy = img_original.copy()

    # --- Step 1: Isolate Store Interiors (The Floodfill Method) ---
    print("Isolating store blobs...")
    h, w = img_walls.shape
    
    # Floodfill from (0,0) to find all "outside" navigable areas
    mask_flood = np.zeros((h + 2, w + 2), np.uint8)
    im_floodfill = img_walls.copy()
    cv2.floodFill(im_floodfill, mask_flood, (0, 0), 255)
    
    # Invert: Now Navigable/Exterior is Black (0), Stores are White (255)
    stores_mask = cv2.bitwise_not(im_floodfill)
    
    # # Close small holes (text inside stores) to make solid blobs
    # kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5, 5))
    # stores_mask = cv2.morphologyEx(stores_mask, cv2.MORPH_CLOSE, kernel, iterations=2)

    # --- Step 2: Find Contours of Each Store ---
    contours, _ = cv2.findContours(stores_mask, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)
    print(f"Found {len(contours)} potential store regions.")

    # Initialize OCR
    allowed_chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ &.1522'"
    reader = easyocr.Reader(['en'])
    
    detected_stores = []
    stores_identified_bitmap = np.zeros(stores_mask.shape, dtype=np.uint8)
    
    print("Starting extraction loop...")
    for i, cnt in enumerate(contours):
        # Filter noise: Ignore tiny blobs
        area = cv2.contourArea(cnt)
        if area < 1000: 
            continue
        cv2.drawContours(stores_identified_bitmap, [cnt], -1, (255))

        # --- Step 3: Geometry Analysis ---
        # Calculate Centroid
        M = cv2.moments(cnt)
        if M["m00"]!= 0:
            cx = int(M["m10"] / M["m00"])
            cy = int(M["m01"] / M["m00"])
        else:
            continue # Avoid division by zero

        # Get Bounding Box
        x, y, w, h = cv2.boundingRect(cnt)
        
        # --- Step 4: The "Perfect" Crop Technique ---
        
        # A. Create a mask JUST for this single store
        # This "blanks out" any neighboring store that might be in the square crop
        single_store_mask = np.zeros_like(img_walls)
        cv2.drawContours(single_store_mask, [cnt], -1, 255, thickness=cv2.FILLED)
        # cv2.imwrite(f"{OUTPUT_FOLDER}/store_{i}_mask.png", single_store_mask)
        
        # B. Crop the mask and the original image
        crop_mask = single_store_mask[y:y+h, x:x+w]
        crop_original = img_original[y:y+h, x:x+w]
        
        # C. Apply the mask to the crop
        # We create a white background image
        final_crop = np.ones_like(crop_original) * 255 
        # Copy pixels ONLY where the mask is active (inside the store)
        # This removes walls and neighbors from the crop
        final_crop[crop_mask == 255] = crop_original[crop_mask == 255]
        # cv2.imwrite(f"{OUTPUT_FOLDER}/store_{i}_crop_mask.png", final_crop)

        store_name = ""
        store_name_2 = ""
        store_name_3 = ""
        store_name_4 = ""

        # --- Step 5: OCR Execution ---
        # D. Preprocess for OCR (Upscale & Threshold)
        ocr_ready_img = preprocess_crop_for_ocr(final_crop)
        results = reader.readtext(ocr_ready_img, detail=0, allowlist=allowed_chars) # detail=0 gives just list of text
        # Join text if multiple words found (e.g., "RARE" "RABBIT")
        store_name = " ".join(results).strip()

        rotated_img = cv2.rotate(final_crop, cv2.ROTATE_90_CLOCKWISE)
        ocr_ready_img_2 = preprocess_crop_for_ocr(rotated_img)
        results = reader.readtext(ocr_ready_img_2, detail=0, allowlist=allowed_chars) # detail=0 gives just list of text
        store_name_2 = " ".join(results).strip()

        results = reader.readtext(final_crop, detail=0, allowlist=allowed_chars)
        store_name_3 = " ".join(results).strip()

        # if len(store_name) > len(store_name_2):
        #     rotated_img_2 = cv2.rotate(final_crop, cv2.ROTATE_90_COUNTERCLOCKWISE)
        #     ocr_ready_img_3 = preprocess_crop_for_ocr_2(rotated_img_2)
        #     results = reader.readtext(ocr_ready_img_3, detail=0, allowlist=allowed_chars) # detail=0 gives just list of text
        #     store_name_4 = " ".join(results).strip()
        # else:
        #     ocr_ready_img_3 = preprocess_crop_for_ocr_2(final_crop)
        #     results = reader.readtext(ocr_ready_img_3, detail=0, allowlist=allowed_chars) # detail=0 gives just list of text
        #     store_name_4 = " ".join(results).strip()

        rotated_img_2 = cv2.rotate(final_crop, cv2.ROTATE_90_CLOCKWISE)
        results = reader.readtext(rotated_img_2, detail=0, allowlist=allowed_chars) # detail=0 gives just list of text
        store_name_4 = " ".join(results).strip()

        store_name_1 = store_name
        store_names = [store_name_4, store_name_3, store_name_1, store_name_2]
        ocr_ready_imgs = [rotated_img_2, final_crop, ocr_ready_img, ocr_ready_img_2]
        store_name = store_names[np.argmax([len(name) for name in store_names])]
        if store_name:
            print(f"Store #{i}: '{store_name}' at ({cx}, {cy})")
            detected_stores.append({
                "best_name": store_name,
                "name1": store_name_1,
                "name2": store_name_2,
                "name3": store_name_3,
                "name4": store_name_4,
                "centroid": (cx, cy),
                "bbox": (x, y, w, h)
            })
            
            # Save crop for debugging
            cv2.imwrite(f"{OUTPUT_FOLDER}/store_{i}_{store_name}.png", ocr_ready_imgs[np.argmax([len(name) for name in store_names])])
            
            # Draw on Original Image for Visualization
            cv2.circle(img_original_copy, (cx, cy), 5, (0, 0, 255), -1)
            cv2.putText(img_original_copy, store_name, (x, y-5), 
                        cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 0), 2)
            cv2.rectangle(img_original_copy, (x,y), (x+w, y+h), (0, 255, 0), 2)

    # --- Step 6: Final Output ---
    output_path = "final_floor_plan_analyzed.jpg"
    cv2.imwrite(output_path, img_original_copy)
    print(f"Done! Analyzed image saved to {output_path}")
    cv2.imwrite(f'stores_identified_bitmask.png', stores_identified_bitmap)
    print(f"Individual store crops saved to folder: {OUTPUT_FOLDER}")

    # Save detected_stores in CSV
    import csv
    csv_path = os.path.join(OUTPUT_FOLDER, "detected_stores.csv")
    with open(csv_path, 'w', newline='', encoding='utf-8') as f:
        writer = csv.writer(f)
        writer.writerow(['StoreName', 'StoreName1', 'StoreName2', 'StoreName3', 'StoreName4', 'Centroid_X', 'Centroid_Y', 'BBox_X', 'BBox_Y', 'BBox_Width', 'BBox_Height'])
        for store in detected_stores:
            writer.writerow([
                store["best_name"],
                store["name1"],
                store["name2"],
                store["name3"],
                store["name4"],
                store["centroid"][0],
                store["centroid"][1],
                store["bbox"][0],
                store["bbox"][1],
                store["bbox"][2],
                store["bbox"][3]
            ])

# --- Execution ---
if __name__ == "__main__":
    # Ensure you have your images ready
    if os.path.exists(ORIGINAL_IMAGE_PATH) and os.path.exists(WALL_BITMAP_PATH):
        process_floor_plan_hybrid(ORIGINAL_IMAGE_PATH, WALL_BITMAP_PATH)
    else:
        print("Please check that both input images exist.")