using UnityEngine;

public class FollowSeekerCamera : MonoBehaviour
{
    public Transform seeker;

    // Offset relative to seeker
    public Vector3 offset = new Vector3(0, 1.2f, -1.5f);

    public float smoothSpeed = 5f;

    void LateUpdate()
    {
        if (!seeker) return;

        Vector3 targetPos = seeker.position + offset;
        transform.position = Vector3.Lerp(
            transform.position,
            targetPos,
            smoothSpeed * Time.deltaTime
        );

        // ALWAYS look in the movement direction / forward
        transform.LookAt(seeker.position + Vector3.forward * 0.01f);
    }
}
