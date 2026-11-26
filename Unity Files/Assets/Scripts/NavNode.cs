using UnityEngine;
using System.Collections.Generic;

public class NavNode : MonoBehaviour
{
    public List<NavNode> neighbors = new List<NavNode>();
    public int floorNumber = 0;  // for multi-floor navigation
}
