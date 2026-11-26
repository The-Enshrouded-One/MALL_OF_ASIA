#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using System.Linq;

public class BannerPlacerEditor : MonoBehaviour
{
    public DatabaseManager db;
    public GameObject bannerPrefab;
    public Transform mallRoot;

    public int targetFloorNumber = 0;
    public float floorHeight = MallWorldScale.FloorHeight;
    public float bannerHeightOffset = 0.05f;

    [ContextMenu("Place Banners At Centroids")]
    public void PlaceBannersForFloor()
    {
        if (db == null || bannerPrefab == null)
        {
            Debug.LogError("Database or Banner Prefab is NULL");
            return;
        }
        db.Init();
        var stores = db.Connection
            .Table<final_stores_dataset_v3>()
            .Where(s => s.floor_number == targetFloorNumber)
            .ToList();

        Debug.Log($"Placing {stores.Count} banners for floor {targetFloorNumber}");

        foreach (var store in stores)
        {
            Vector3 pos = ConvertCentroidToUnity(
                store.Centroid_X,
                store.Centroid_Y);

            pos.y = GetFloorHeight(targetFloorNumber) + bannerHeightOffset;

            Debug.Log($"Placing banner for {store.normalized_name} at {pos}");

            GameObject banner = Instantiate(bannerPrefab);

            if (banner == null)
            {
                Debug.LogError("Banner instantiation failed");
                continue;
            }

            banner.transform.position = pos;
            banner.transform.rotation = Quaternion.Euler(0, 180, 0);

            if (mallRoot != null)
                banner.transform.SetParent(mallRoot);

            banner.name = $"Banner_{store.normalized_name}_F{targetFloorNumber}";

            var logo = banner.GetComponent<SearchBannerLogo>();
            if (logo == null)
            {
                Debug.LogError("SearchBannerLogo missing on prefab!");
            }
            else
            {
                logo.ApplyLogoFromNormalizedName(store.normalized_name);
            }

#if UNITY_EDITOR
            Undo.RegisterCreatedObjectUndo(banner, "Place Banner");
#endif

            Debug.Log($"Placed banner for {store.normalized_name}");

        }

        Debug.Log("DONE. SAVE THE SCENE.");
    }

    float GetFloorHeight(int floor)
    {
        return floor * floorHeight;
    }

    Vector3 ConvertCentroidToUnity(float x, float y)
    {
        float scale = MallWorldScale.WorldUnitsPerPixel;
        return new Vector3(-x * scale, 0, y * scale);
    }
}
