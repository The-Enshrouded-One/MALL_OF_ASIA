from selenium import webdriver
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import Select
from bs4 import BeautifulSoup
import time
import csv

options = webdriver.ChromeOptions()
# Set the path to the Chromedriver
DRIVER_PATH = ''

# Initialize the Chrome driver
# driver = webdriver.Chrome(executable_path=DRIVER_PATH)
driver = webdriver.Chrome(options=options)
# driver = webdriver.Chrome()

# Navigate to the URL
driver.get('https://www.phoenixmallofasia.com/brands')
time.sleep(3)
html = driver.page_source


# Parse the HTML with BeautifulSoup
soup = BeautifulSoup(html, 'html.parser')


# Find the dropdown for categories from the wbsite having XPath "//*[@id="root"]/div[1]/div[3]/div/div/div[1]/div[1]/select" and iterate for each selection, list the website html content
# category_dropdown = soup.find(by=By.XPATH, value='//select[@class="custom-select"]')
category_dropdown = driver.find_element(By.CLASS_NAME, 'custom-select')
# category_dropdown = driver.find_element(By.CLASS_NAME, 'custom-select')
print(category_dropdown.get_attribute('outerHTML'))
print(category_dropdown.text)
print(category_dropdown)
select_object = Select(category_dropdown)
all_option_elements = select_object.options
option_values = [opt.get_attribute('value') for opt in all_option_elements]
print(f"Found categories: {option_values}")
all_brands_by_category = {}

for value in option_values:
    print(f"\n--- Scraping Category: '{value}' ---")
    
    # We must re-find the element in each loop iteration
    # This prevents a "StaleElementReferenceException" as the page re-renders
    dropdown_element = driver.find_element(By.CLASS_NAME, 'custom-select')
    select_object = Select(dropdown_element)
    
    # Select the option by its 'value' attribute
    select_object.select_by_value(value)
    
    # **CRITICAL STEP:** Wait for the JavaScript to update the brand list.
    # 3 seconds is usually safe. For a more advanced solution,
    # you would use WebDriverWait.
    time.sleep(5) 

    # If there is a button in the page with the text "View More", click it
    present = True
    while present:
        try:
            brand_category = driver.find_element(By.CLASS_NAME, 'brands_category')
            # view_more_button = brand_category.find_element(By.XPATH, "//button[text()='View More']") # //*[@id="root"]/div[1]/div[3]/div/div/button
            view_more_button = driver.find_element(By.XPATH, "/html/body/div[1]/div[1]/div[3]/div/div/button") # //*[@id="root"]/div[1]/div[3]/div/div/button
            print("Button found ", view_more_button.text)
            element = WebDriverWait(driver, 10).until(
                EC.element_to_be_clickable((By.XPATH, "/html/body/div[1]/div[1]/div[3]/div/div/button"))
            )
            element.click()
            print("Button clicked ", element.text)
        except Exception as e:
            print("No 'View More' button found or an error occurred:", e)
            present = False
    
    # --- 3. Parse the updated page content with BeautifulSoup ---
    
    # Get the page source *after* the content has updated
    html = driver.page_source
    soup = BeautifulSoup(html, 'html.parser')
    
    # Find all the brand "cards"
    # From inspecting the site, each card has the class 'brands_card-box'
    brand_cards = soup.find_all('div', class_='col-md-3 col-6 px-md-3 px-1')
    
    if not brand_cards:
        print("No brands found for this category.")
        continue
        
    category_brands_list = []
    for card in brand_cards:
        # The brand name is in a <p> tag
        brand_link = card.find('a', href=True)['href']
        # The floor is in a <span> tag
        floor_tag = card.find('h4')
        
        brand_link = brand_link.strip() if brand_link else 'N/A'
        brand_link.replace(' ', '%20')
        floor = floor_tag.text.strip() if floor_tag else 'N/A'
        
        category_brands_list.append({
            'link': brand_link,
            'floor': floor
        })
        print(f"Found: {brand_link} ({floor})")
        
    # Store the list of brands in our main dictionary
    all_brands_by_category[value] = category_brands_list

# Save category_brands_list to csv
with open('brands_by_category_all.csv', mode='w', newline='', encoding='utf-8') as file:
    writer = csv.writer(file)
    writer.writerow(['Category', 'Brand Link', 'Floor'])
    
    for category, brands in all_brands_by_category.items():
        for brand in brands:
            writer.writerow([category, brand['link'], brand['floor']])



# It's a good practice to close the browser when done
driver.quit()