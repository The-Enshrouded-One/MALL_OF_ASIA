using System.Collections.Generic;
using UnityEngine;

public class InterFloorController : MonoBehaviour
{
    public static InterFloorController Instance { get; private set; }

    [Header("Floor A* Objects (index = floor number)")]
    public GameObject[] floorAStars;

    [Header("Lift Settings")]
    public Vector2 liftXZ; // legacy single lift point (floor-plan pixels), used only when no lifts are built
    public float floorHeight = MallWorldScale.FloorHeight;

    [System.Serializable]
    public class LiftDefinition
    {
        [Tooltip("Shaft centre in floor-plan pixels (3000 x 1385 map).")]
        public Vector2 pixel;
        [Tooltip("Direction the doors face, in floor-plan pixel axes (x right, y down).")]
        public Vector2 doorDirection = Vector2.right;
    }

    [Tooltip("Glass lifts running through every floor. Positions were chosen where the corridor is open on all four floors (see build_unity_walkable_maps.py).")]
    public LiftDefinition[] lifts;

    static readonly LiftDefinition[] DefaultLifts =
    {
        // Beside the wall of the cross-passage through the west island, doors facing the open passage.
        new LiftDefinition { pixel = new Vector2(1014, 477), doorDirection = new Vector2(0.822f, 0.570f) },
        // East concourse, doors facing west along the corridor.
        new LiftDefinition { pixel = new Vector2(2020, 399), doorDirection = new Vector2(-0.962f, -0.272f) },
    };

    readonly List<LiftShaft> liftShafts = new List<LiftShaft>();
    public IReadOnlyList<LiftShaft> LiftShafts => liftShafts;

    public static string FloorName(int floor)
    {
        switch (floor)
        {
            case 0: return "Ground Floor";
            case 1: return "First Floor";
            case 2: return "Second Floor";
            case 3: return "Third Floor";
            default: return "Floor " + floor;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one InterFloorController can be active in the scene.", this);
            enabled = false;
            return;
        }

        Instance = this;
    }

    void Start()
    {
        BuildLifts();
        ActivateFloor(0);
    }

    void BuildLifts()
    {
        LiftDefinition[] definitions = lifts != null && lifts.Length > 0 ? lifts : DefaultLifts;
        int floorCount = floorAStars != null ? floorAStars.Length : 0;
        float pixelToUnity = MallWorldScale.WorldUnitsPerPixel;

        for (int i = 0; i < definitions.Length; i++)
        {
            LiftDefinition definition = definitions[i];
            var center = new Vector3(-definition.pixel.x * pixelToUnity, 0f, definition.pixel.y * pixelToUnity);
            var doorDirection = new Vector3(-definition.doorDirection.x, 0f, definition.doorDirection.y);
            LiftShaft shaft = LiftShaft.Create($"Glass Lift {(char)('A' + i)}", center, doorDirection, floorCount, floorHeight);
            liftShafts.Add(shaft);

            // Routes must go around the shaft, never through it.
            for (int floor = 0; floor < floorCount; floor++)
            {
                GridFromTexture grid = floorAStars[floor] != null ? floorAStars[floor].GetComponent<GridFromTexture>() : null;
                if (grid != null)
                    grid.BlockCircle(center + Vector3.up * (floor * floorHeight), LiftShaft.Radius + 0.2f);
            }
        }
    }

    /// <summary>The lift that makes the whole journey (here -> lift -> destination) shortest.</summary>
    public LiftShaft ChooseLift(Vector3 from, Vector3 to)
    {
        LiftShaft best = null;
        float bestCost = float.MaxValue;
        foreach (LiftShaft shaft in liftShafts)
        {
            Vector3 door = shaft.DoorPoint(0);
            float cost = FlatDistance(from, door) + FlatDistance(door, to);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = shaft;
            }
        }
        return best;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool IsValidFloor(int floor)
    {
        return floorAStars != null && floor >= 0 && floor < floorAStars.Length && floorAStars[floor] != null;
    }

    public void ActivateFloor(int floor)
    {
        if (!IsValidFloor(floor))
        {
            Debug.LogError("Cannot activate mall floor " + floor + ": the floor is missing from the scene configuration.", this);
            return;
        }

        for (int i = 0; i < floorAStars.Length; i++)
        {
            if (floorAStars[i] != null)
                floorAStars[i].SetActive(i == floor);
        }
    }

    public PathRequestManager GetPRM(int floor)
    {
        if (!IsValidFloor(floor))
            return null;

        return floorAStars[floor].GetComponent<PathRequestManager>();
    }

    public PathRequestManager GetActivePRM()
    {
        if (floorAStars == null)
            return null;

        for (int i = 0; i < floorAStars.Length; i++)
        {
            if (floorAStars[i] != null && floorAStars[i].activeInHierarchy)
                return floorAStars[i].GetComponent<PathRequestManager>();
        }

        Debug.LogError("No active floor pathfinder is configured.", this);
        return null;
    }

    public Vector3 GetLiftPosition(int floor)
    {
        float pixelToUnity = MallWorldScale.WorldUnitsPerPixel;
        return new Vector3(-liftXZ.x * pixelToUnity, floor * floorHeight, liftXZ.y * pixelToUnity);
    }

    public void MoveUnitToFloor(Unit unit, int targetFloor)
    {
        if (unit == null || !IsValidFloor(targetFloor))
        {
            Debug.LogError("Cannot move the seeker to the requested floor.", this);
            return;
        }

        ActivateFloor(targetFloor);
        unit.transform.position = GetLiftPosition(targetFloor);
        unit.currentFloor = targetFloor;
    }
}
