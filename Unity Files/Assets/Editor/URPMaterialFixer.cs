using UnityEditor;
using UnityEngine;

/// <summary>
/// Converts materials that still use built-in render pipeline shaders (Standard, Unlit/Texture, ...)
/// to their URP equivalents. Built-in shaders are not supported by URP: they show magenta in the
/// editor and are stripped from player builds, which made the mall walls and floors invisible.
/// </summary>
public static class URPMaterialFixer
{
    [MenuItem("Tools/Mall/Convert Built-in Materials To URP")]
    public static void ConvertMenu() => ConvertAll(true);

    public static int ConvertAll(bool log = true)
    {

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (lit == null || unlit == null)
        {
            Debug.LogError("URP shaders were not found; cannot convert materials.");
            return 0;
        }

        int converted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".mat") || path.StartsWith("Assets/TextMesh Pro/"))
                continue;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
                continue;

            string shaderName = material.shader.name;
            Shader target;
            if (shaderName == "Standard" || shaderName == "Standard (Specular setup)" || shaderName.StartsWith("Legacy Shaders/"))
                target = lit;
            else if (shaderName.StartsWith("Unlit/"))
                target = unlit;
            else
                continue;

            Texture mainTexture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
            Vector2 scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
            Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            float smoothness = material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0.5f;
            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;

            material.shader = target;
            material.SetTexture("_BaseMap", mainTexture);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetColor("_BaseColor", color);
            if (target == lit)
            {
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
            }

            EditorUtility.SetDirty(material);
            converted++;
            if (log)
                Debug.Log($"Converted material {path} from '{shaderName}' to '{target.name}'.");
        }

        if (converted > 0)
            AssetDatabase.SaveAssets();
        return converted;
    }
}
