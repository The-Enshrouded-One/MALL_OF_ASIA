from selenium import webdriver
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import Select
from bs4 import BeautifulSoup
import time
import csv
from tqdm import tqdm

# State the Webdriver options
options = webdriver.ChromeOptions()
options.add_argument('--headless')
DRIVER_PATH = ''

# Initialize the Chrome driver
# driver = webdriver.Chrome(executable_path=DRIVER_PATH)
driver = webdriver.Chrome(options=options)
# driver = webdriver.Chrome()
brand_deets = []
with open('dine_by_category_all.csv') as file_obj:
    reader_obj = csv.reader(file_obj)
    header = next(reader_obj)
    for row in tqdm(reader_obj):
        try:
            link = row[1]
            driver.get(link)
            time.sleep(3)
            brand_detail = driver.find_element(By.CLASS_NAME, 'dine_detail')
            brand_detail_head = brand_detail.find_element(By.CLASS_NAME, 'brand_detail_head')
            brand_name = brand_detail_head.find_element(By.TAG_NAME, 'h2').text # //*[@id="root"]/div[1]/div[3]/div[1]/div/div[1]/div/h2
            brand_logo = brand_detail_head.find_element(By.CLASS_NAME, 'logo-card').find_element(By.TAG_NAME, 'img').get_attribute('src')
            brand_p_tags = brand_detail_head.find_elements(By.TAG_NAME, 'p')
            brand_desc = brand_p_tags[0].text if brand_p_tags else ''
            brand_contact = brand_p_tags[1].text if brand_p_tags else ''
            brand_email = brand_p_tags[2].text if brand_p_tags else ''
            brand_timing = brand_p_tags[3].text if brand_p_tags else ''
            images = brand_detail.find_element(By.XPATH, '//*[@id="root"]/div[1]/div[5]/div[2]/div')  # '//*[@id="root"]/div[1]/div[4]/div'
            images = images.find_elements(By.TAG_NAME, 'img')
            brand_images = [img.get_attribute('src') for img in images]

            more_like_this_box = driver.find_element(By.CLASS_NAME, 'brands_category')
            more_like_this_links = more_like_this_box.find_elements(By.TAG_NAME, 'a')
            more_like_this = [a.get_attribute('href') for a in more_like_this_links]
            if len(brand_images) != 4 or len(more_like_this) != 4:
                print(f"Found {len(brand_images)} images for brand {brand_name} and {len(more_like_this)} 'more like this' links")

            brand_deets.append({
                'name': brand_name,
                'category': row[0],
                'link': row[1],
                'floor': row[2],
                'logo': brand_logo,
                'description': brand_desc,
                'contact': brand_contact,
                'email': brand_email,
                'timing': brand_timing,
                'images_0': brand_images[0] if len(brand_images) > 0 else '',
                'images_1': brand_images[1] if len(brand_images) > 1 else '',
                'images_2': brand_images[2] if len(brand_images) > 2 else '',
                'images_3': brand_images[3] if len(brand_images) > 3 else '',
                'more_like_this_0': more_like_this[0] if len(more_like_this) > 0 else '',
                'more_like_this_1': more_like_this[1] if len(more_like_this) > 1 else '',
                'more_like_this_2': more_like_this[2] if len(more_like_this) > 2 else '',
                'more_like_this_3': more_like_this[3] if len(more_like_this) > 3 else ''
            })
            print(f"Scraped details for brand: {brand_name}")
        except Exception as e:
            print(f"Error scraping brand at link {link}: {e}")

with open('dine_details_MOA.csv', 'w', newline='', encoding='utf-8') as file_obj:
    fieldnames = ['name', 'category', 'link', 'floor', 'logo', 'description', 'contact', 'email', 'timing',
                  'images_0', 'images_1', 'images_2', 'images_3',
                  'more_like_this_0', 'more_like_this_1', 'more_like_this_2', 'more_like_this_3']
    writer = csv.DictWriter(file_obj, fieldnames=fieldnames)
    writer.writeheader()
    for brand in brand_deets:
        writer.writerow(brand)
driver.quit()