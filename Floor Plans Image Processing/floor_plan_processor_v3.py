import cv2
import numpy as np
import easyocr
import os

# --- Configuration ---
TARGET_WIDTH = 3000
TARGET_HEIGHT = 1385
IMAGE_PATH = "1.png" # <--- IMPORTANT: SET THIS TO YOUR IMAGE PATH

# --- Main Processing Function ---
def process_floor_plan(image_path, target_width, target_height):
    """
    Processes a B&W contour-state floor plan image to extract bitmaps
    and a list of store names with their scaled geometric centroids.
    """
    print(f"--- Processing {image_path} ---")

    # --- Step 1: Load Image and Get Wall Bitmap ---
    image = cv2.imread(image_path)
    if image is None:
        print(f"[Error] Could not load image: {image_path}")
        return

    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    proc_height, proc_width = gray.shape[:2]
    print(f"Loaded processed image: {proc_width}x{proc_height}")

    # Binarize the image. This is our "Wall Bitmap" (Goal 1).
    # White = walls/text. Black = store interiors/walkways.
    _, wall_bitmap = cv2.threshold(gray, 128, 255, cv2.THRESH_BINARY)

    #############
    # 2. Set a minimum area threshold. This is the key value to tune.
    # Text characters will be small (e.g., < 100 pixels).
    # Walls will be very large (e.g., > 1000 pixels).
    MIN_CONTOUR_AREA = 1000  # You will need to experiment with this value

    binary_image = cv2.imread("('1_opening_mask', '.png')_wall_bitmap - Copy.png", cv2.IMREAD_GRAYSCALE)
    # 3. Find all contours
    # cv2.RETR_EXTERNAL is great here, it only finds the outer-most contours
    contours, _ = cv2.findContours(binary_image, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)

    # 4. Create a new blank (black) image to draw the filtered walls onto
    # It must be the same size as the original
    filtered_image = np.zeros(binary_image.shape, dtype=np.uint8)

    # 5. Loop through contours, check area, and draw the large ones
    print(len(contours))
    for contour in contours:
        # Get the area of the contour
        area = cv2.contourArea(contour)
        print(area)
        
        if area > MIN_CONTOUR_AREA:
            # This contour is large (a wall), so draw it on our new image
            # -1 as thickness fills the contour
            cv2.drawContours(filtered_image, [contour], -1, (255))

    # 6. Save or display the result
    cv2.imwrite('walls_only_mask.png', filtered_image)
    print("Filtered mask 'walls_only_mask.png' has been saved!")
    ########

    output_wall_bitmap_path = f"{os.path.splitext(image_path)}_wall_bitmap.png"
    cv2.imwrite(output_wall_bitmap_path, wall_bitmap)
    print(f"Successfully saved Wall Bitmap to: {output_wall_bitmap_path}")

    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5,5))
    store_mask_filled = cv2.morphologyEx(wall_bitmap, cv2.MORPH_CLOSE, kernel, iterations=1)
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3,3))
    store_mask_filled = cv2.morphologyEx(store_mask_filled, cv2.MORPH_CLOSE, kernel, iterations=2)
    opening_kernel = np.ones((2, 2), np.uint8)
    store_mask_filled = cv2.morphologyEx(store_mask_filled, cv2.MORPH_OPEN, opening_kernel, iterations=2)
    
    cv2.imwrite(f"{os.path.splitext(image_path)}_closed_wall_bitmap.png", store_mask_filled)
    # --- Step 2: NEW - Isolate and Fill Store Blobs ---
    
    # Create a copy to flood-fill
    im_floodfill = wall_bitmap.copy()
    
    # Create a mask for flood fill (must be 2 pixels larger)
    h, w = wall_bitmap.shape[:2]
    mask = np.zeros((h + 2, w + 2), np.uint8)
    
    # Flood-fill from (0, 0)
    # This fills all "open" areas (walkways) connected to the edge with white
    print("Isolating store interiors using flood fill...")
    cv2.floodFill(im_floodfill, mask, (0, 0), 255)
    
    # Invert the flood-filled image
    # Now, only the enclosed "holes" (the store interiors) are white.
    store_interiors_with_holes = cv2.bitwise_not(im_floodfill)
    
    # Fill the text holes inside the store interiors using Morphological Closing
    # A 5x5 kernel is usually good for closing text-sized gaps
    print("Filling text holes in store blobs...")
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5,5))
    store_mask_filled = cv2.morphologyEx(store_interiors_with_holes, cv2.MORPH_CLOSE, kernel, iterations=2)
    
    # Save a debug image of the final store blobs
    cv2.imwrite("debug_store_blobs.png", store_mask_filled)
    print("Saved debug store blobs to 'debug_store_blobs.png'")

    # --- Step 3: Get Store Centroids from the NEW Mask ---
    
    # Run Connected Components on the *filled store mask*
    # This will now correctly identify each store as a separate blob.
    print("Running Connected Component Analysis on stores...")
    output = cv2.connectedComponentsWithStats(store_mask_filled, 8, cv2.CV_32S)
    (numLabels, labels_matrix, stats, store_centroids_proc) = output
    print(f"Found {numLabels - 1} total store blobs.")
    
    # --- VISUALIZATION BLOCK (Now works correctly) ---
    print(f"Visualizing {numLabels} store labels...")
    color_visualization = np.zeros(labels_matrix.shape + (3,), dtype=np.uint8)
    colors = []
    for i in range(1, numLabels):
        colors.append((np.random.randint(50, 256), np.random.randint(50, 256), np.random.randint(50, 256)))

    for label in range(1, numLabels):
        mask = labels_matrix == label
        color_visualization[mask] = colors[label - 1]

    cv2.imwrite("connected_store_blobs_visualization.png", color_visualization)
    print("Saved store visualization to 'connected_store_blobs_visualization.png'")
    
    cv2.imshow("Connected Store Blobs", color_visualization)
    print("Press any key to close the visualization...")
    cv2.waitKey(0)
    cv2.destroyAllWindows()
    # --- END VISUALIZATION ---

    # --- Step 4: Isolate Navigable Area Bitmap (Goal 2) ---
    
    # The walkway is the flood-filled area, MINUS the original walls/text
    navigable_bitmap = cv2.bitwise_xor(im_floodfill, wall_bitmap)
    
    output_nav_bitmap_path = f"{os.path.splitext(image_path)}_navigable_bitmap.png"
    cv2.imwrite(output_nav_bitmap_path, navigable_bitmap)
    print(f"Successfully saved Navigable Area Bitmap to: {output_nav_bitmap_path}")

    # --- Step 5: Run OCR (No changes here) ---
    
    print("Initializing EasyOCR...")
    reader = easyocr.Reader(['en'])
    print("Running OCR to extract all store names...")
    ocr_results = reader.readtext(wall_bitmap) # OCR still runs on the original wall_bitmap
    print(f"OCR found {len(ocr_results)} text fragments.")

    # --- Step 6: Associate Text with NEW Store Centroids (Goal 3) ---

    print("Associating text with store centroids and scaling...")
    final_store_list = []
    
    x_scale = target_width / proc_width
    y_scale = target_height / proc_height
    processed_texts = set()

    for (bbox, text, prob) in ocr_results:
        cleaned_text = " ".join(text.split()).strip()
        if not cleaned_text or cleaned_text in processed_texts:
            continue
        
        (tl, tr, br, bl) = bbox
        text_centroid_x = int((tl + br) / 2)
        text_centroid_y = int((tl + br) / 2)
        
        if 0 <= text_centroid_y < labels_matrix.shape and 0 <= text_centroid_x < labels_matrix.shape:
            # --- THIS IS THE KEY ASSOCIATION ---
            # Look up which *store blob* the text centroid is on
            store_label_id = labels_matrix[text_centroid_y, text_centroid_x]
            
            # ** CRITICAL FIX **
            # We just need to check if it's not the background (label 0)
            if store_label_id!= 0:
                
                (store_cx_proc, store_cy_proc) = store_centroids_proc[store_label_id]
                
                store_cx_final = int(store_cx_proc * x_scale)
                store_cy_final = int(store_cy_proc * y_scale)
                
                final_store_list.append(
                    {"store_name": cleaned_text, 
                     "x_px": store_cx_final, 
                     "y_px": store_cy_final}
                )
                processed_texts.add(cleaned_text)
        else:
            print(f"Warning: Calculated text centroid for '{cleaned_text}' is out of bounds.")

    # --- Step 7: Final Output ---
    
    print("\n--- Processing Complete ---")
    print(f"Total stores extracted: {len(final_store_list)}")
    print(f"\nStore Name and Scaled Centroid Locations ({target_width}x{target_height}):")
    
    final_store_list.sort(key=lambda s: s['store_name'])
    for store in final_store_list:
        print(f"- {store['store_name']}: ({store['x_px']}, {store['y_px']})")
        
    return final_store_list

# --- Main Execution ---
if __name__ == "__main__":
    try:
        reader = easyocr.Reader(['en'], gpu=True)
        print(f"EasyOCR found. Running on {'GPU' if reader.device!= 'cpu' else 'CPU'}.")
    except Exception as e:
        print(f"EasyOCR initialization error: {e}. See setup instructions.")
        exit()

    if not os.path.exists(IMAGE_PATH):
        print(f" Image file not found at: {IMAGE_PATH}")
    else:
        store_data = process_floor_plan(IMAGE_PATH, TARGET_WIDTH, TARGET_HEIGHT)