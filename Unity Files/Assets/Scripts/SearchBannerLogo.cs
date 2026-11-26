using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SearchBannerLogo : MonoBehaviour
{
    public Renderer targetRenderer;

    public void ApplyLogoFromNormalizedName(string normalizedName)
    {
#if UNITY_EDITOR
        if (targetRenderer == null)
        {
            Debug.LogError("Target Renderer is NULL on " + gameObject.name);
            return;
        }

        string pngPath = $"Assets/Logos/{normalizedName}.png";
        string jpgPath = $"Assets/Logos/{normalizedName}.jpg";

        Texture2D tex =
            AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath) ??
            AssetDatabase.LoadAssetAtPath<Texture2D>(jpgPath);

        if (tex == null)
        {
            Debug.LogError($"Logo not found for {normalizedName}");
            return;
        }

        // IMPORTANT: force material instance
        Material mat = new Material(targetRenderer.sharedMaterial);
        mat.mainTexture = tex;
        targetRenderer.sharedMaterial = mat;

        EditorUtility.SetDirty(targetRenderer);
#endif
    }
}
