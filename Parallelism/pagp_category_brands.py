import csv
from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import Select
from concurrent.futures import ThreadPoolExecutor, as_completed

# Configuration
MAX_WORKERS = 4  # Number of parallel browsers
DRIVER_PATH = 'path/to/chromedriver'

def get_category_options():
    """Initializes one driver just to get the list of categories."""
    options = webdriver.ChromeOptions()
    options.add_argument('--headless')
    driver = webdriver.Chrome(options=options)
    driver.get('https://www.phoenixmallofasia.com/brands')
    
    select_element = driver.find_element(By.CLASS_NAME, 'custom-select')
    select_object = Select(select_element)
    # Extract values to pass to workers
    values = [opt.get_attribute('value') for opt in select_object.options]
    driver.quit()
    return values

def scrape_category_worker(category_value):
    """Worker function: Launches its own browser instance for thread safety."""
    local_data = []
    options = webdriver.ChromeOptions()
    options.add_argument('--headless')
    
    # Each thread needs its own driver instance
    driver = webdriver.Chrome(options=options)
    
    try:
        driver.get('https://www.phoenixmallofasia.com/brands')
        # Logic to select category and scrape (Simplified for brevity)
        select_object = Select(driver.find_element(By.CLASS_NAME, 'custom-select'))
        select_object.select_by_value(category_value)
        
        # ... (Insert wait logic and 'View More' click logic here) ...
        
        # Scrape results
        brand_cards = driver.find_elements(By.CLASS_NAME, 'brands_card-box')
        for card in brand_cards:
            local_data.append({
                'Category': category_value,
                'Link': card.find_element(By.TAG_NAME, 'a').get_attribute('href'),
                'Floor': card.find_element(By.TAG_NAME, 'h4').text
            })
    except Exception as e:
        print(f"Error in {category_value}: {e}")
    finally:
        driver.quit()
    
    return local_data

# --- Main Parallel Execution ---
if __name__ == "__main__":
    categories = get_category_options()
    all_results = []
    
    print(f"Starting parallel scraping for {len(categories)} categories...")
    
    with ThreadPoolExecutor(max_workers=MAX_WORKERS) as executor:
        # Map the worker function to the categories
        future_to_cat = {executor.submit(scrape_category_worker, cat): cat for cat in categories}
        
        for future in as_completed(future_to_cat):
            data = future.result()
            all_results.extend(data)
            
    # Write aggregated results to CSV
    with open('brands_by_category_parallel.csv', 'w', newline='', encoding='utf-8') as f:
        writer = csv.DictWriter(f, fieldnames=['Category', 'Link', 'Floor'])
        writer.writeheader()
        writer.writerows(all_results)