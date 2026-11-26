using UnityEngine;
using System.Collections.Generic;

public class FaceCamera : MonoBehaviour
{
    void LateUpdate()
    {
        if (Camera.main == null) return;

        transform.LookAt(transform.position + Camera.main.transform.forward);
    }
}
