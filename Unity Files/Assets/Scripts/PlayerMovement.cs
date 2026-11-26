using UnityEngine;

/// <summary>
/// Desktop fallback controls for exploring the mall without a VR headset.
/// WASD moves, right mouse drag looks, Shift moves faster, and Escape releases the cursor.
/// </summary>
[RequireComponent(typeof(Unit))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Desktop Controls")]
    [Min(0.001f)] public float moveSpeed = 1.4f;
    [Min(1f)] public float runMultiplier = 2f;
    [Min(0.1f)] public float mouseSensitivity = 2f;

    Unit unit;
    Transform cameraTransform;
    Transform pitchTransform;
    float pitch;
    Vector3 velocity;
    const float GuidedPitch = 10f;

    void Awake()
    {
        unit = GetComponent<Unit>();
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cameraTransform = mainCamera.transform;
            pitchTransform = cameraTransform.parent != null ? cameraTransform.parent : cameraTransform;
        }
    }

    void Start()
    {
        // The capsule is a navigation placeholder, not a visible first-person body.
        MeshRenderer bodyRenderer = GetComponent<MeshRenderer>();
        if (bodyRenderer != null)
            bodyRenderer.enabled = false;

        Collider bodyCollider = GetComponent<Collider>();
        if (bodyCollider != null && !(bodyCollider is CharacterController))
            bodyCollider.enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        bool looking = Input.GetMouseButton(1);
        if (Input.GetMouseButtonDown(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (Input.GetMouseButtonUp(1))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // The lift ride is scripted; manual controls wait until the doors open on the new floor.
        if (unit.IsInLift)
        {
            velocity = Vector3.zero;
            return;
        }

        bool moving = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f ||
                      Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;
        bool xrActive = UnityEngine.XR.XRSettings.isDeviceActive;

        if (looking && cameraTransform != null)
        {
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");

            // While being guided the route steers; looking up and down is still free.
            if (!unit.IsNavigating)
                transform.Rotate(0f, mouseX * mouseSensitivity, 0f, Space.World);

            pitch -= mouseY * mouseSensitivity;
            pitch = Mathf.Clamp(pitch, -80f, 80f);
        }
        else if (unit.IsNavigating && !xrActive)
        {
            // Gently tilt the view down so the route on the floor stays in sight.
            pitch = Mathf.Lerp(pitch, GuidedPitch, 1f - Mathf.Exp(-1.5f * Time.deltaTime));
        }

        if (!xrActive && pitchTransform != null)
            pitchTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        Vector3 desired = Vector3.zero;
        if (moving)
        {
            unit.CancelNavigation(false);
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            Vector3 direction = transform.right * horizontal + transform.forward * vertical;
            direction.y = 0f;
            if (direction.sqrMagnitude > 1f)
                direction.Normalize();

            float multiplier = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? runMultiplier
                : 1f;
            desired = direction * (moveSpeed * multiplier);
        }

        // Ease in and out of walking instead of starting and stopping instantly.
        velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
        if (!unit.IsNavigating && velocity.sqrMagnitude > 0.000001f)
            transform.position += velocity * Time.deltaTime;
    }
}
