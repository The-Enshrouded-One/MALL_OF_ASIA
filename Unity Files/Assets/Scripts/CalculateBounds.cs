using UnityEngine;

public class CalculateBounds : MonoBehaviour
{
    void Start()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
        {
            Debug.LogError("No mesh renderers found!");
            return;
        }

        Bounds totalBounds = renderers[0].bounds;

        foreach (var r in renderers)
        {
            totalBounds.Encapsulate(r.bounds);
        }

        Debug.Log(
            "Mall Bounds Size = " + totalBounds.size +
            "\nMall Bounds Center = " + totalBounds.center
        );
    }
}
