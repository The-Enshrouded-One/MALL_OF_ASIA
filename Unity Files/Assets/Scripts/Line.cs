using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public struct Line
{
    const float verticalLineGradient = 1e5f;

    float gradient;
    float y_intercept;

    Vector2 pointOnLine_1;
    Vector2 pointOnLine_2;

    bool approachSide;

    public Line(Vector2 pointOnLine, Vector2 pointPerpendicularToLine)
    {
        float dx = pointOnLine.x - pointPerpendicularToLine.x;
        float dy = pointOnLine.y - pointPerpendicularToLine.y;

        float gradientPerpendicular = (dx == 0) ? verticalLineGradient : dy / dx;

        gradient = (gradientPerpendicular == 0) ? verticalLineGradient : -1f / gradientPerpendicular;

        y_intercept = pointOnLine.y - gradient * pointOnLine.x;

        pointOnLine_1 = pointOnLine;
        pointOnLine_2 = pointOnLine + new Vector2(1, gradient);

        approachSide = false;
        approachSide = GetSide(pointPerpendicularToLine);
    }

    bool GetSide(Vector2 p)
    {
        return (p.x - pointOnLine_1.x) * (pointOnLine_2.y - pointOnLine_1.y)
             > (p.y - pointOnLine_1.y) * (pointOnLine_2.x - pointOnLine_1.x);
    }

    public bool HasCrossedLine(Vector2 p)
    {
        return GetSide(p) != approachSide;
    }

    public void DrawWithGizmos(float length)
    {
        Vector3 lineDir = new Vector3(1, 0, gradient).normalized;
        Vector3 centre = new Vector3(pointOnLine_1.x, 0, pointOnLine_1.y);
        Gizmos.DrawLine(centre - lineDir * length / 2f, centre + lineDir * length / 2f);
    }
}
