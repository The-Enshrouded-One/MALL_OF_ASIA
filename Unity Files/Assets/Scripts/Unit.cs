using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// The navigating visitor (the XR rig / desktop camera rig). Requests a route, glides along it with
/// eased acceleration and look-ahead steering, and rides a lift when the destination is on another floor.
/// </summary>
public class Unit : MonoBehaviour
{
    [Header("Movement")]
    [Min(0.001f)] public float speed = 2.4f;          // brisk guided walking pace, m/s
    [Min(0.01f)] public float acceleration = 1.2f;    // m/s^2, also used to brake before arriving
    public float rotationSpeed = 3f;                  // how quickly the view turns toward the route
    [Min(0.1f)] public float lookAhead = 3.5f;        // metres ahead the view aims at

    [Header("Floor Settings")]
    public int currentFloor;
    public float floorHeight = MallWorldScale.FloorHeight;
    public Vector2 liftXZ;

    public event Action<string> StatusChanged;

    /// <summary>True while a route is being followed or a lift ride is in progress.</summary>
    public bool IsNavigating => followRoutine != null || liftRoutine != null || awaitingPath;
    /// <summary>True during a lift ride; manual controls are locked so the ride cannot be interrupted.</summary>
    public bool IsInLift => liftRoutine != null;

    enum Leg { ToDestination, ToLift }

    Vector3[] path;
    float[] cumulative;
    float currentSpeed;
    Vector3 finalTarget;
    int targetFloor;
    string destinationName;
    Leg pendingLeg;
    Leg currentLeg;
    bool awaitingPath;
    int requestId;
    LiftShaft activeLift;
    Coroutine followRoutine;
    Coroutine liftRoutine;
    RouteGuide guide;
    const float StopShort = 1.8f;

    public RouteGuide Guide => guide;
    /// <summary>The route currently being followed (world positions), or null.</summary>
    public Vector3[] CurrentPath => path;
    /// <summary>Metres left on the current walking leg (0 when idle).</summary>
    public float RemainingDistance { get; private set; }
    /// <summary>True while walking to a lift rather than to the store itself.</summary>
    public bool HeadingToLift => followRoutine != null && currentLeg == Leg.ToLift;

    void Awake()
    {
        guide = RouteGuide.Create();
    }

    void OnDestroy()
    {
        if (guide != null)
            Destroy(guide.gameObject);
    }

    public void SetTarget(Vector3 targetPosition, int destinationFloor, string storeName = null)
    {
        if (InterFloorController.Instance == null)
        {
            ReportStatus("Navigation is unavailable: floor routing did not initialize.");
            return;
        }

        if (!InterFloorController.Instance.IsValidFloor(destinationFloor))
        {
            ReportStatus("The destination floor is not available in this mall scene.");
            return;
        }

        if (IsInLift)
        {
            ReportStatus("Please wait for the lift to arrive before starting a new route.");
            return;
        }

        CancelNavigation(false);
        destinationName = string.IsNullOrWhiteSpace(storeName) ? "your destination" : storeName;
        targetFloor = destinationFloor;
        finalTarget = targetPosition;
        finalTarget.y = targetFloor * floorHeight;

        if (currentFloor == targetFloor)
        {
            RequestLeg(finalTarget, Leg.ToDestination);
            return;
        }

        activeLift = InterFloorController.Instance.ChooseLift(transform.position, finalTarget);
        if (activeLift == null)
        {
            // No lift in the scene: fall back to moving straight to the destination floor.
            InterFloorController.Instance.MoveUnitToFloor(this, targetFloor);
            RequestLeg(finalTarget, Leg.ToDestination);
            return;
        }

        RequestLeg(activeLift.DoorPoint(currentFloor), Leg.ToLift);
    }

    void RequestLeg(Vector3 targetPosition, Leg leg)
    {
        PathRequestManager requestManager = InterFloorController.Instance.GetActivePRM();
        if (requestManager == null)
        {
            ReportStatus("Navigation is unavailable on this floor.");
            return;
        }

        pendingLeg = leg;
        awaitingPath = true;
        int request = ++requestId;
        requestManager.RequestPath(transform.position, targetPosition, (newPath, success) =>
        {
            if (request == requestId) // ignore answers to routes that were cancelled meanwhile
                OnPathFound(newPath, success);
        });
    }

    public void OnPathFound(Vector3[] newPath, bool pathSuccessful)
    {
        awaitingPath = false;
        if (!pathSuccessful || newPath == null || newPath.Length == 0)
        {
            guide.Clear();
            ReportStatus("No walkable route was found. Try another destination.");
            return;
        }

        SetPath(newPath);
        guide.ShowRoute(path, floorHeight * currentFloor);
        if (pendingLeg == Leg.ToLift)
        {
            guide.ShowBeacon(activeLift.DoorPoint(currentFloor), RouteGuide.LiftColour);
            ReportStatus($"Route ready. Follow the path to the lift for the {InterFloorController.FloorName(targetFloor)}.");
        }
        else
        {
            guide.ShowBeacon(path[path.Length - 1], RouteGuide.DestinationColour);
            ReportStatus("Route ready to " + destinationName + ". Follow the highlighted path.");
        }

        if (followRoutine != null)
            StopCoroutine(followRoutine);
        followRoutine = StartCoroutine(FollowPath(pendingLeg));
    }

    void SetPath(Vector3[] waypoints)
    {
        // Start from where the visitor actually stands so the first step is seamless.
        path = new Vector3[waypoints.Length + 1];
        path[0] = transform.position;
        Array.Copy(waypoints, 0, path, 1, waypoints.Length);
        for (int i = 0; i < path.Length; i++)
            path[i].y = transform.position.y;

        cumulative = new float[path.Length];
        for (int i = 1; i < path.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(path[i - 1], path[i]);
    }

    Vector3 PointAt(float distance)
    {
        if (distance <= 0f)
            return path[0];
        float total = cumulative[cumulative.Length - 1];
        if (distance >= total)
            return path[path.Length - 1];

        int index = Array.BinarySearch(cumulative, distance);
        if (index < 0)
            index = ~index;
        index = Mathf.Clamp(index, 1, path.Length - 1);
        float segment = cumulative[index] - cumulative[index - 1];
        float t = segment > 0.0001f ? (distance - cumulative[index - 1]) / segment : 1f;
        return Vector3.Lerp(path[index - 1], path[index], t);
    }

    IEnumerator FollowPath(Leg leg)
    {
        float routeLength = cumulative[cumulative.Length - 1];
        // Stop a comfortable step short of the shopfront instead of pressing against the glass.
        float total = leg == Leg.ToDestination && routeLength > 2.5f ? routeLength - StopShort : routeLength;
        float travelled = 0f;
        currentLeg = leg;

        while (travelled < total - 0.001f)
        {
            RemainingDistance = total - travelled;
            float dt = Time.deltaTime;
            float remaining = total - travelled;

            // Brake smoothly into the destination and ease off on tight bends.
            float targetSpeed = Mathf.Min(speed, Mathf.Sqrt(2f * acceleration * remaining) + 0.08f);
            Vector3 here = PointAt(travelled);
            Vector3 a = PointAt(travelled + 1.5f) - here;
            Vector3 b = PointAt(travelled + 3f) - PointAt(travelled + 1.5f);
            if (a.sqrMagnitude > 0.0001f && b.sqrMagnitude > 0.0001f)
            {
                float bend = Vector3.Angle(a, b);
                targetSpeed *= Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(15f, 90f, bend));
            }

            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * dt);
            travelled = Mathf.Min(total, travelled + currentSpeed * dt);
            transform.position = PointAt(travelled);

            Vector3 aim = PointAt(travelled + lookAhead) - transform.position;
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.0025f)
            {
                Quaternion facing = Quaternion.LookRotation(aim);
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-rotationSpeed * dt));
            }

            guide.SetProgress(travelled / Mathf.Max(routeLength, 0.001f));
            yield return null;
        }

        if (leg == Leg.ToDestination)
        {
            // Turn to face the store entrance.
            Vector3 toStore = path[path.Length - 1] - transform.position;
            toStore.y = 0f;
            if (toStore.sqrMagnitude > 0.01f)
            {
                Quaternion from = transform.rotation;
                Quaternion to = Quaternion.LookRotation(toStore);
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
                {
                    transform.rotation = Quaternion.Slerp(from, to, t * t * (3f - 2f * t));
                    yield return null;
                }
                transform.rotation = to;
            }
        }

        currentSpeed = 0f;
        RemainingDistance = 0f;
        followRoutine = null;

        if (leg == Leg.ToLift && activeLift != null)
        {
            liftRoutine = StartCoroutine(RideLift());
            yield break;
        }

        guide.ShowArrived();
        ReportStatus("You have arrived at " + destinationName + ".");
    }

    IEnumerator RideLift()
    {
        guide.Clear();
        yield return activeLift.Ride(this, currentFloor, targetFloor, ReportStatus, floor =>
        {
            InterFloorController.Instance.ActivateFloor(floor);
            currentFloor = floor;
        });
        liftRoutine = null;
        RequestLeg(finalTarget, Leg.ToDestination);
    }

    public void CancelNavigation(bool clearStatus = true)
    {
        if (IsInLift)
            return; // a ride in progress always completes

        if (followRoutine != null)
        {
            StopCoroutine(followRoutine);
            followRoutine = null;
        }

        awaitingPath = false;
        RemainingDistance = 0f;
        requestId++;
        path = null;
        currentSpeed = 0f;
        if (guide != null)
            guide.Clear();
        if (clearStatus)
            ReportStatus("Navigation stopped.");
    }

    public void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);
    }
}
