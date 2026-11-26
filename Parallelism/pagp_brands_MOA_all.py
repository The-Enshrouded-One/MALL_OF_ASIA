import csv
import time
from concurrent.futures import ThreadPoolExecutor
from selenium import webdriver
from selenium.webdriver.common.by import By

# Thread-safe writing mechanism (Optional: or collect list and write at end)
import threading
csv_lock = threading.Lock()

def scrape_brand_details_worker(row_data):
    """
    Worker function to scrape a single brand URL.
    Input: A dictionary/row containing the 'Link'
    """
    link = row_data['Link']
    brand_info = {}
    
    options = webdriver.ChromeOptions()
    options.add_argument('--headless')
    driver = webdriver.Chrome(options=options)
    
    try:
        driver.get(link)
        time.sleep(2) # Allow React/JS to render
        
        # Extraction Logic
        brand_detail = driver.find_element(By.CLASS_NAME, 'brand_detail')
        head = brand_detail.find_element(By.CLASS_NAME, 'brand_detail_head')
        
        brand_info = {
            'name': head.find_element(By.TAG_NAME, 'h2').text,
            'category': row_data['Category'],
            'floor': row_data['Floor'],
            'link': link,
            'description': head.find_elements(By.TAG_NAME, 'p')[0].text
            # ... (Add other field extractions here)
        }
    except Exception as e:
        print(f"Failed to scrape {link}: {e}")
        return None
    finally:
        driver.quit()
        
    return brand_info

# --- Main Parallel Execution ---
def run_brand_scraping():
    # Read the input links
    with open('brands_by_category_parallel.csv', 'r') as f:
        reader = csv.DictReader(f)
        tasks = list(reader)

    # Output file setup
    output_headers = ['name', 'category', 'floor', 'link', 'description']
    
    with open('final_brand_details.csv', 'w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=output_headers)
        writer.writeheader()

        # Execute in parallel
        with ThreadPoolExecutor(max_workers=5) as executor:
            results = executor.map(scrape_brand_details_worker, tasks)
            
            for result in results:
                if result:
                    writer.writerow(result)
                    print(f"Scraped: {result['name']}")

if __name__ == "__main__":
    run_brand_scraping()