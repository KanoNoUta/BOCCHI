using System;
using System.Numerics;

namespace BOCCHI.Modules.AggroRange;

public enum AggroRangeState
{
    Outside,
    Near,
    Inside,
}

public static class AggroRangeGeometry
{
    public static float HorizontalDistance(Vector3 left, Vector3 right)
    {
        var x = left.X - right.X;
        var z = left.Z - right.Z;
        return MathF.Sqrt(x * x + z * z);
    }

    public static AggroRangeState GetState(float distance, float radius, float nearBoundaryDistance)
    {
        if (distance <= radius)
        {
            return AggroRangeState.Inside;
        }

        return distance - radius <= Math.Max(0f, nearBoundaryDistance)
            ? AggroRangeState.Near
            : AggroRangeState.Outside;
    }
}
