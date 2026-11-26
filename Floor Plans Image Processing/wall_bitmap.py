import cv2
import numpy as np
import pytesseract
import csv
import os

MIN_CONTOUR_AREA = 1000 

def process_floor_plan(image_path):
    print(f"--- Processing {image_path} ---")
    
    # Load the image
    image = cv2.imread(image_path)
    if image is None:
        print(f"[Error] Could not load image: {image_path}")
        return
        
    img_height, img_width = image.shape[:2]
    base_name = os.path.splitext(os.path.basename(image_path))[0]

    gray_image = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    cv2.imwrite(f"{base_name}_gray_image.png", gray_image)

    # 3. Get a binary mask of the black lines/text
    # Adaptive thresholding is better than a fixed threshold
    # It calculates a threshold for small regions, good for uneven lighting
    # ADAPTIVE_THRESH_GAUSSIAN_C: threshold value is a weighted sum of neighborhood values
    # THRESH_BINARY_INV: We want lines to be "1" (white) and background to be "0" (black)
    # 11: Neighborhood size (pixels)
    # 2: Constant subtracted from the mean
    line_mask = cv2.adaptiveThreshold(gray_image, 255,
                                      cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
                                      cv2.THRESH_BINARY_INV, 11, 2)
    # save the line_mask
    cv2.imwrite(f"{base_name}_line_mask.png", line_mask)

    # --- New Step 4a: Remove text noise ---
    # Use Morphological Opening: erosion followed by dilation.
    # This removes small white specks (text remnants) while
    # preserving larger lines.
    # A (3,3) kernel is good for removing dot-like noise.
    opening_kernel = np.ones((2, 2), np.uint8)
    # 2 iterations should be strong enough to remove most text dots.
    opening_mask = cv2.morphologyEx(line_mask, cv2.MORPH_OPEN, opening_kernel, iterations=1)
    # kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5,5))
    # opening_mask = cv2.morphologyEx(opening_mask, cv2.MORPH_CLOSE, kernel, iterations=1)
    cv2.imwrite(f"{base_name}_opening_mask.png", opening_mask)
    
    

    # contours, _ = cv2.findContours(opening_mask, cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
    contours, hierarchy = cv2.findContours(opening_mask, cv2.RETR_TREE, cv2.CHAIN_APPROX_SIMPLE)
    wall_bitmap = np.zeros(opening_mask.shape, dtype=np.uint8)
    filtered_bitmap = np.zeros(opening_mask.shape, dtype=np.uint8)
    navigable_bitmap = np.zeros(opening_mask.shape, dtype=np.uint8)

    print(hierarchy)
    print(len(contours))
    valid_contours = []
    valid_contours_child = []
    for i, contour in enumerate(contours):
        area = cv2.contourArea(contour)   
        h = hierarchy[0][i]

        if area > MIN_CONTOUR_AREA:
            valid_contours.append(i)
            valid_contours_child.append(-1)
            if h[3] in valid_contours:
                valid_contours_child[valid_contours.index(h[3])] = i

    for j, i in enumerate(valid_contours):
        contour = contours[i]
        area = cv2.contourArea(contour)
        h = hierarchy[0][i]
        child = h[2]
        
        if area > MIN_CONTOUR_AREA and area < 1000000 and child != -1 and valid_contours_child[j] != -1:
            cv2.drawContours(wall_bitmap, [contour], -1, (255))
            cv2.drawContours(navigable_bitmap, [contour], -1, (255), thickness=cv2.FILLED)
        else:
            cv2.drawContours(filtered_bitmap, [contour], -1, (255))

    cv2.imwrite(f'{base_name}_walls_bitmask.png', wall_bitmap)
    cv2.imwrite(f'{base_name}_filtered_bitmask.png', filtered_bitmap)
    navigable_bitmap = cv2.bitwise_not(navigable_bitmap)
    cv2.imwrite(f'{base_name}_navigable_bitmask.png', navigable_bitmap)
    print("Filtered mask 'walls_only_mask.png' has been saved!")


# --- Main Execution ---
if __name__ == "__main__":
    # List of images to process
    # Add all your 5 floor plan image paths here
    image_files = ["1.png", "0.png", "-1.jpg", "2.png", "3.png"]
    image_files = ["-1.jpg"]
    
    # Check if Tesseract is installed and accessible
    try:
        pytesseract.pytesseract.tesseract_cmd = r'C:\Program Files\Tesseract-OCR\tesseract.exe'
        pytesseract.get_tesseract_version()
        print(f"Tesseract version {pytesseract.get_tesseract_version()} found.")
    except Exception as e:
        print("[CRITICAL ERROR] Tesseract-OCR not found or not in system PATH.")
        print("Please install it from: https://github.com/UB-Mannheim/tesseract/wiki")
        print("Then, you may need to set the path manually, e.g.:")
        print("# pytesseract.pytesseract.tesseract_cmd = r'C:\\Program Files\\Tesseract-OCR\\tesseract.exe'")
        exit()

    
    for img_file in image_files:
        if not os.path.exists(img_file):
            print(f"[Warning] File not found: {img_file}. Skipping.")
            continue
        process_floor_plan(img_file)