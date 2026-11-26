# Saarathi: Unlocking Immersive Navigation Through Advanced Parallel Computing for a Commercial Hub

Team Members: Krupa, Pavithra, Manasa

> **Getting started:** see [`PROJECT_GUIDE.md`](PROJECT_GUIDE.md) for how to run the app, controls, the Unity project layout and how to rebuild everything.

**Saarathi** is an enterprise-grade, high-performance, immersive 3D Digital Twin and first-person Virtual Reality (VR) navigation ecosystem modeled explicitly for expansive, multi-level commercial complexes (such as the *Phoenix Mall of Asia*, Bengaluru). Traditional 2D kiosk wayfinding maps and static directional signs are inherently non-intuitive, confusing, and fail to provide real-time spatial orientation across multiple floors.

To bridge this gap, Saarathi fuses **Computer Graphics & Virtual Reality (CGVR)** with **Parallel Architecture & GPU Programming (PAGP)**. It processes structural store layouts, multi-spectral metadata datasets, and optimal route hierarchies using accelerated parallel algorithms, seamlessly streaming this data into Unity 3D to render an interactive, real-time navigation grid.

---

## Features

* Immersive first-person VR navigation.
* Geometrically accurate 3D reconstruction of mall interiors.
* Multi-floor navigation support.
* Automated extraction of store metadata.
* OCR-based recognition of store names from floor plans.
* Dynamic store search and discovery.
* Product/category-based recommendations.
* A* based shortest path computation.
* GPU-accelerated preprocessing and OCR.
* Data-driven Unity scene generation.
* Scalable architecture applicable to malls, airports, hospitals, and campuses.

---

## System Architecture

The framework consists of three major layers:

### 1. Extraction Layer (Offline)

Responsible for acquiring and processing raw data:

* Parallel web scraping of store information.
* Architectural floor plan processing.
* OCR-based store name extraction.
* Generation of navigational bitmaps.

### 2. Curation Layer

Transforms extracted data into structured assets:

* Cleaning and normalization of metadata.
* SQLite database construction.
* 2D-to-3D reconstruction.
* Coordinate mapping.
* Navigation graph preparation.

### 3. Runtime Layer (VR Application)

Provides the user-facing experience:

* Dynamic scene assembly in Unity.
* Real-time path computation.
* VR interaction and locomotion.
* Intelligent recommendations.

<img width="2816" height="1536" alt="BlockDiag" src="https://github.com/user-attachments/assets/e1d30be4-eed6-43d7-ad52-d7b8be0fa7e0" />

---

## Methodology

### 1. Data Acquisition

The dataset is generated automatically from publicly available resources.

#### Floor Plans

High-resolution architectural floor plans of Phoenix Mall of Asia are used to derive spatial layouts.

#### Store Metadata

Store information is collected using Selenium-based web scraping, including:

* Store names
* Categories
* Floor assignments
* Logos
* Descriptions

#### OCR Labels

Store names embedded within floor plans are extracted using a deep learning OCR pipeline.

---

### 2. Parallel Metadata Extraction

Store metadata acquisition is an I/O-bound task.

A producer-consumer architecture using `ThreadPoolExecutor` enables concurrent processing.

#### Techniques Used

* Thread-Level Parallelism (TLP)
* Dynamic load balancing
* Shared work queues
* Retry mechanisms with exponential backoff

#### Performance

* Sequential: ~500 seconds for 100 stores
* Parallel: ~96 seconds
* Speedup: **5.2×**

<img width="480" height="369" alt="Benchmark_1" src="https://github.com/user-attachments/assets/4dbf2339-3fcd-4185-a58b-8a9d02e52ddc" />

---

### 3. Floor Plan Processing

Architectural floor plans are transformed into machine-readable representations using OpenCV.

#### Preprocessing

* Grayscale conversion (`COLOR_BGR2GRAY`)
* High-quality resizing (`INTER_LANCZOS4`)
* CLAHE contrast enhancement

#### Segmentation

* Adaptive Thresholding
* Otsu's Binarization
* Flood Fill operations

#### Morphological Operations

* Opening (erosion followed by dilation)
* Closing (dilation followed by erosion)

These operations remove noise and preserve structural boundaries.

#### Feature Extraction

Structural elements are identified using:

* Contour Detection (`RETR_TREE`)
* Contour Moments
* Connected Components

Store centroids are computed for later placement and navigation.

<img width="2778" height="1070" alt="Image_proc_flow" src="https://github.com/user-attachments/assets/9f5e58dd-d680-4578-a84c-9d54941c39cf" />

---

### 4. OCR Pipeline

Store names are extracted automatically using a GPU-accelerated OCR framework.

#### Text Detection

**CRAFT (Character Region Awareness for Text)**

* Detects character regions accurately.
* Handles irregular and rotated text.

#### Text Recognition

**CRNN (Convolutional Recurrent Neural Network)**

Composed of:

* CNN feature extractor
* BiLSTM sequence modeling
* CTC decoding

A multi-orientation strategy processes each store region under different rotations to improve recognition robustness.

#### OCR Acceleration

Benchmarking demonstrated:

* CPU: 145.4 seconds
* GPU: 5.78 seconds

Speedup: **25.16×**

---

### 5. Data Integration

All extracted information is consolidated into an SQLite database.

Each store record includes:

* Store ID
* Store Name
* Floor Number
* Category
* Centroid Coordinates
* Logo Path
* Description

<img width="3262" height="310" alt="Data" src="https://github.com/user-attachments/assets/8f57cbff-5466-4afc-a39e-584d7023b74a" />

This database acts as the single source of truth for the VR application.

---

### 6. 2D to 3D Reconstruction

Binary floor-plan bitmaps are converted into volumetric models.

#### Workflow

1. Generate wall and navigable bitmaps.
2. Import bitmaps into Blender.
3. Extrude structural boundaries into 3D meshes.
4. Process each floor independently.
5. Export reconstructed models to Unity.

This automated pipeline minimizes manual modeling effort while preserving geometric fidelity.
<img width="901" height="453" alt="image" src="https://github.com/user-attachments/assets/90f39b59-21a8-49e7-ac58-679559de736e" />
<img width="1016" height="387" alt="image" src="https://github.com/user-attachments/assets/ca01669c-6960-4182-b2ee-5781d077caa6" />

---

### 7. Unity Integration

Unity serves as the primary VR development platform.

The runtime application:

* Imports reconstructed meshes.
* Queries the SQLite database.
* Dynamically instantiates store prefabs.
* Applies store-specific branding and logos.
* Streams assets efficiently.

Additional optimizations include:

* Unity Job System
* Burst Compiler
* Modular asset loading

to maintain VR-grade performance.
<img width="3093" height="1419" alt="Picture10" src="https://github.com/user-attachments/assets/ef2712e5-654c-437c-a8ba-83ae896060c4" />

---

### 8. Navigation System

Indoor routing is implemented using a bitmap-driven A* framework.

#### Walkable Space Generation

Navigable bitmaps distinguish:

* Walkable areas
* Obstacles
* Store interiors
* Structural walls

Each pixel corresponds to a node in the navigation graph.

<img width="1500" height="532" alt="image" src="https://github.com/user-attachments/assets/d7feb2c3-da0b-4654-8bea-c1e8e994bcd8" />


#### A* Pathfinding

The shortest path is computed using:

```
f(n) = g(n) + h(n)
```

where:

* `g(n)` = accumulated travel cost
* `h(n)` = heuristic estimate

Heuristics include:

* Manhattan distance
* Euclidean distance

depending on layout requirements.

<img width="664" height="442" alt="Picture11" src="https://github.com/user-attachments/assets/bdb9169c-05f5-40e4-88a3-075d3d020544" />

---

### 9. Multi-Floor Navigation

Each floor is modeled as an independent graph.

Inter-floor transitions are represented using connector nodes corresponding to:

* Elevators
* Escalators

The routing process:

1. Computes the path to the nearest connector.
2. Transitions between floors.
3. Computes the final path to the destination.

---

### 10. VR Visualization

Computed routes are transformed into Unity world coordinates and rendered as visual guidance trails.

Users receive:

* First-person navigation cues
* Continuous directional guidance
* Intuitive spatial understanding

using Unity's XR interaction framework.

<img width="1366" height="768" alt="Screenshot (982)" src="https://github.com/user-attachments/assets/a9214c9d-4647-467f-8438-3d7c503a70b1" />

---

### 11. Intelligent Store Recommendation

The system incorporates an AI-driven recommendation mechanism.

User queries regarding products or interests are mapped against:

* Store categories
* Store descriptions
* Brand metadata

Recommended stores are automatically linked to the navigation module for route generation.

<img width="1366" height="768" alt="Screenshot (995)" src="https://github.com/user-attachments/assets/6a65d38b-ae7b-4670-8cb4-2d12931479d2" />

---

## Technology Stack

### VR & Graphics

* Unity
* XR Interaction Toolkit
* Blender

### Computer Vision

* OpenCV
* EasyOCR
* CRAFT
* CRNN

### Data Processing

* Python
* Pandas
* SQLite

### Data Acquisition

* Selenium
* ThreadPoolExecutor

### Parallel Computing

* CPU Multithreading
* SIMD-based vectorized operations
* GPU acceleration
* OpenCL

### AI Components

* TinyLlama-1.1B
* LoRA
* QLoRA

---

## Results

| Metric                           | Result                    |
| -------------------------------- | ------------------------- |
| Stores Processed                 | 293+                      |
| Web Scraping Speedup             | 5.2×                      |
| OCR GPU Speedup                  | 25.16×                    |
| Batch OCR Throughput Improvement | Up to 32×                 |
| Runtime Objective                | 90–120 FPS VR Performance |

---

## Applications

The proposed framework can be extended beyond shopping malls to support:

* Airports
* Railway stations
* Hospitals
* University campuses
* Convention centers
* Large public infrastructures

---

## Credits

* **Original project** — data pipeline (web scraping, floor-plan processing, OCR, GPU benchmarking), the store database, the Blender reconstruction of the floors and the first Unity navigation prototype: **M Nithyashree** ([@nithya333](https://github.com/nithya333)) with team members Krupa, Pavithra and Manasa. Original repository: [nithya333/Mall-Of-Asia-VR](https://github.com/nithya333/Mall-Of-Asia-VR).
* **This version** — real-scale mall reconstruction, wall-aware smooth navigation, glass lifts, route guide, visual overhaul, atrium and concourse furnishing, live store search and the build/test tooling: **Syed Muzammil Hussaini**.
