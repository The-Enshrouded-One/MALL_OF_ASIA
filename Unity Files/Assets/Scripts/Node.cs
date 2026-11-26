using UnityEngine;
using System.Collections.Generic;

// Node Definition
public class Node: IHeapItem<Node> {
    public bool walkable;
    public Vector3 worldPosition;
    public int gridX;
    public int gridY;
    // Distance (in grid cells) to the nearest blocked cell; used to keep routes in the middle of corridors.
    public int clearance;

    public int gCost = int.MaxValue;
    public int hCost;
    public Node parent;
    int heapIndex;

    public Node(bool _walkable, Vector3 _worldPos, int _x, int _y) {
        walkable = _walkable;
        worldPosition = _worldPos;
        gridX = _x;
        gridY = _y;
    }

    public int fCost {
        get {
            return gCost + hCost;
        }
    }

    public int HeapIndex { 
        get{
            return heapIndex;
        }
        set{
            heapIndex = value;
        }
    }

    public int CompareTo(Node nodeToCompare) {
        int compare = fCost.CompareTo(nodeToCompare.fCost);
        if (compare == 0) {
            compare = hCost.CompareTo(nodeToCompare.hCost);
        }
        return -compare;
    }
}
