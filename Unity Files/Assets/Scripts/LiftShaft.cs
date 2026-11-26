using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// A panoramic glass lift that runs through every floor of the mall. Built procedurally at start-up.
/// The ride is fully animated: the cab is called to the visitor's floor, the doors slide open, the
/// visitor steps in and turns around, the doors close, the cab travels with eased motion, and the
/// visitor steps out on the destination floor.
/// </summary>
public class LiftShaft : MonoBehaviour
{
    public const float Radius = 0.62f;          // cab radius (m)
    public const float DoorStandDistance = 1.3f; // where the visitor waits, measured from the shaft centre
    const float CabHeight = 2.45f;
    const float DoorWidth = 0.56f;
    const float DoorTravel = 0.52f;

    int floorCount;
    float floorHeight;
    Vector3 doorDirection;
    Transform cab;
    Transform doorLeft;
    Transform doorRight;
    Renderer cabLight;
    Renderer[] floorIndicators;
    int cabFloor;
    float doorsOpen;      // 0 closed .. 1 open
    bool busy;

    public Vector3 Center => transform.position;
    public Vector3 DoorDirection => doorDirection;

    public static LiftShaft Create(string name, Vector3 baseCenter, Vector3 doorDirection, int floorCount, float floorHeight)
    {
        var root = new GameObject(name);
        root.transform.position = new Vector3(baseCenter.x, 0f, baseCenter.z);
        var lift = root.AddComponent<LiftShaft>();
        lift.floorCount = floorCount;
        lift.floorHeight = floorHeight;
        doorDirection.y = 0f;
        lift.doorDirection = doorDirection.sqrMagnitude > 0.0001f ? doorDirection.normalized : Vector3.forward;
        lift.Build();
        return lift;
    }

    public Vector3 DoorPoint(int floor) => Center + doorDirection * DoorStandDistance + Vector3.up * (floor * floorHeight);
    Vector3 CabPoint(int floor) => Center + Vector3.up * (floor * floorHeight);

    // ---------------------------------------------------------------- construction

    void Build()
    {
        Material glass = Resources.Load<Material>("Effects_Glass");
        Material metal = Resources.Load<Material>("LiftMetal");
        Material glow = Resources.Load<Material>("Effects_Glow");
        float shaftHeight = floorCount * floorHeight;
        Quaternion facing = Quaternion.LookRotation(doorDirection);

        // Glass shaft made of curved segments, open on the door side.
        const int segments = 18;
        float segmentAngle = 360f / segments;
        float segmentWidth = 2f * Mathf.PI * (Radius + 0.06f) / segments * 1.02f;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i + 0.5f) * segmentAngle;
            if (Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 52f)
                continue; // opening that faces the corridor
            Vector3 dir = facing * Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Part(transform, "Glass", PrimitiveType.Cube, glass,
                dir * (Radius + 0.06f) + Vector3.up * (shaftHeight / 2f),
                Quaternion.LookRotation(dir), new Vector3(segmentWidth, shaftHeight, 0.012f));

            if (i % 3 == 0)
                Part(transform, "Mullion", PrimitiveType.Cube, metal, dir * (Radius + 0.08f) + Vector3.up * (shaftHeight / 2f),
                    Quaternion.LookRotation(dir), new Vector3(0.035f, shaftHeight, 0.035f));
        }

        // Door jambs either side of the opening.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 dir = facing * Quaternion.Euler(0f, side * 52f, 0f) * Vector3.forward;
            Part(transform, "Jamb", PrimitiveType.Cube, metal, dir * (Radius + 0.08f) + Vector3.up * (shaftHeight / 2f),
                Quaternion.LookRotation(dir), new Vector3(0.07f, shaftHeight, 0.07f));
        }

        // Landing collar, lintel and a floor indicator light on every floor.
        floorIndicators = new Renderer[floorCount];
        for (int floor = 0; floor < floorCount; floor++)
        {
            float y = floor * floorHeight;
            Part(transform, "Landing Plate", PrimitiveType.Cylinder, metal, Vector3.up * (y + 0.01f), Quaternion.identity,
                new Vector3((Radius + 0.16f) * 2f, 0.01f, (Radius + 0.16f) * 2f));
            Part(transform, "Lintel", PrimitiveType.Cube, metal, doorDirection * (Radius + 0.1f) + Vector3.up * (y + CabHeight + 0.12f),
                facing, new Vector3(1.05f, 0.16f, 0.06f));
            var indicator = Part(transform, "Floor Indicator", PrimitiveType.Cube, glow,
                doorDirection * (Radius + 0.135f) + Vector3.up * (y + CabHeight + 0.12f), facing,
                new Vector3(0.5f, 0.05f, 0.01f));
            floorIndicators[floor] = indicator.GetComponent<Renderer>();
            floorIndicators[floor].material = glow != null ? new Material(glow) : null;
        }

        Part(transform, "Shaft Cap", PrimitiveType.Cylinder, metal, Vector3.up * shaftHeight, Quaternion.identity,
            new Vector3((Radius + 0.16f) * 2f, 0.06f, (Radius + 0.16f) * 2f));

        // The cab: platform, canopy with a light, slim posts and two sliding glass doors.
        cab = new GameObject("Cab").transform;
        cab.SetParent(transform, false);
        Part(cab, "Platform", PrimitiveType.Cylinder, metal, Vector3.up * 0.04f, Quaternion.identity,
            new Vector3(Radius * 2f, 0.04f, Radius * 2f));
        Part(cab, "Canopy", PrimitiveType.Cylinder, metal, Vector3.up * CabHeight, Quaternion.identity,
            new Vector3(Radius * 2f, 0.05f, Radius * 2f));
        var lightDisc = Part(cab, "Cab Light", PrimitiveType.Cylinder, glow, Vector3.up * (CabHeight - 0.06f), Quaternion.identity,
            new Vector3(Radius * 1.3f, 0.01f, Radius * 1.3f));
        cabLight = lightDisc.GetComponent<Renderer>();
        Part(cab, "Handrail", PrimitiveType.Cylinder, metal, -doorDirection * (Radius - 0.08f) + Vector3.up * 0.95f,
            Quaternion.LookRotation(doorDirection) * Quaternion.Euler(0f, 0f, 90f), new Vector3(0.04f, 0.35f, 0.04f));

        doorLeft = BuildDoor("Door Left", glass, metal, facing);
        doorRight = BuildDoor("Door Right", glass, metal, facing);
        SetDoors(0f);

        cabFloor = 0;
        cab.localPosition = Vector3.zero;
        UpdateIndicators();
    }

    Transform BuildDoor(string name, Material glass, Material metal, Quaternion facing)
    {
        var door = new GameObject(name).transform;
        door.SetParent(cab, false);
        door.localRotation = facing;
        Part(door, "Pane", PrimitiveType.Cube, glass, Vector3.up * (CabHeight / 2f), Quaternion.identity,
            new Vector3(DoorWidth, CabHeight - 0.08f, 0.015f));
        foreach (float y in new[] { 0.06f, CabHeight - 0.06f })
            Part(door, "Rail", PrimitiveType.Cube, metal, Vector3.up * y, Quaternion.identity, new Vector3(DoorWidth, 0.05f, 0.03f));
        return door;
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Material material, Vector3 localPosition, Quaternion localRotation, Vector3 scale)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = scale;
        var renderer = part.GetComponent<Renderer>();
        if (material != null)
            renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return part;
    }

    void SetDoors(float open)
    {
        doorsOpen = open;
        Vector3 front = doorDirection * (Radius + 0.02f);
        Vector3 side = Vector3.Cross(Vector3.up, doorDirection).normalized;
        float offset = DoorWidth / 2f + open * DoorTravel;
        doorLeft.localPosition = front - side * offset;
        doorRight.localPosition = front + side * offset;
    }

    void UpdateIndicators()
    {
        if (floorIndicators == null)
            return;
        for (int i = 0; i < floorIndicators.Length; i++)
        {
            if (floorIndicators[i] == null || floorIndicators[i].material == null)
                continue;
            bool here = i == cabFloor;
            floorIndicators[i].material.SetColor("_Color", here ? new Color(1f, 0.78f, 0.3f) : new Color(0.25f, 0.3f, 0.35f));
        }
    }

    // ---------------------------------------------------------------- ride

    static float Ease(float t) => t * t * (3f - 2f * t);
    static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

    IEnumerator AnimateDoors(float to, float duration)
    {
        float from = doorsOpen;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            SetDoors(Mathf.Lerp(from, to, Ease(t)));
            yield return null;
        }
        SetDoors(to);
    }

    IEnumerator MoveCab(int toFloor, Transform passenger)
    {
        float startY = cab.localPosition.y;
        float endY = toFloor * floorHeight;
        float distance = Mathf.Abs(endY - startY);
        if (distance < 0.001f)
            yield break;

        float duration = 1.4f + distance / floorHeight * 1.1f;
        Vector3 passengerOffset = passenger != null ? passenger.position - cab.position : Vector3.zero;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float y = Mathf.Lerp(startY, endY, EaseInOut(t));
            cab.localPosition = new Vector3(0f, y, 0f);
            if (passenger != null)
                passenger.position = cab.position + passengerOffset;
            int nearest = Mathf.Clamp(Mathf.RoundToInt(y / floorHeight), 0, floorCount - 1);
            if (nearest != cabFloor)
            {
                cabFloor = nearest;
                UpdateIndicators();
            }
            yield return null;
        }
        cab.localPosition = new Vector3(0f, endY, 0f);
        if (passenger != null)
            passenger.position = cab.position + passengerOffset;
        cabFloor = toFloor;
        UpdateIndicators();
    }

    static IEnumerator Walk(Transform who, Vector3 to, Vector3 faceDirection, float duration)
    {
        Vector3 from = who.position;
        Quaternion fromRotation = who.rotation;
        faceDirection.y = 0f;
        Quaternion toRotation = faceDirection.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(faceDirection) : fromRotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float k = Ease(t);
            who.position = Vector3.Lerp(from, to, k);
            who.rotation = Quaternion.Slerp(fromRotation, toRotation, Ease(Mathf.Clamp01(t * 1.3f)));
            yield return null;
        }
        who.position = to;
        who.rotation = toRotation;
    }

    /// <summary>
    /// Full lift journey for <paramref name="unit"/>. <paramref name="arrived"/> is invoked once the
    /// cab reaches <paramref name="toFloor"/> (used to switch the active floor's pathfinding).
    /// </summary>
    public IEnumerator Ride(Unit unit, int fromFloor, int toFloor, Action<string> status, Action<int> arrived)
    {
        while (busy)
            yield return null;
        busy = true;
        Transform visitor = unit.transform;

        // Settle on the waiting spot and face the lift while it is called.
        yield return Walk(visitor, DoorPoint(fromFloor), -doorDirection, 0.7f);

        if (cabFloor != fromFloor)
        {
            status?.Invoke("Calling the lift…");
            yield return MoveCab(fromFloor, null);
        }

        status?.Invoke("Lift doors opening. Step in.");
        yield return AnimateDoors(1f, 0.9f);

        // Step in, then turn to face the doors.
        yield return Walk(visitor, CabPoint(fromFloor), -doorDirection, 1.2f);
        yield return Walk(visitor, CabPoint(fromFloor), doorDirection, 0.9f);
        yield return AnimateDoors(0f, 0.9f);

        string destination = InterFloorController.FloorName(toFloor);
        status?.Invoke($"Going {(toFloor > fromFloor ? "up" : "down")} to the {destination}…");
        yield return MoveCab(toFloor, visitor);
        arrived?.Invoke(toFloor);

        status?.Invoke($"Arrived at the {destination}.");
        yield return AnimateDoors(1f, 0.9f);
        yield return Walk(visitor, DoorPoint(toFloor), doorDirection, 1.2f);
        StartCoroutine(AnimateDoors(0f, 1.0f));
        busy = false;
    }
}
