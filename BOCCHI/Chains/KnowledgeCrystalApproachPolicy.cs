using System.Numerics;

namespace BOCCHI.Chains;

public static class KnowledgeCrystalApproachPolicy
{
    public const float DesiredOffset = 1.5f;

    public const float ArrivalDistance = 3f;

    public const float MaximumCastDistance = 4.3f;

    public static Vector3 GetDesiredApproachPosition(Vector3 player, Vector3 crystal)
    {
        var direction = new Vector2(player.X - crystal.X, player.Z - crystal.Z);
        if (direction.LengthSquared() < 0.0001f)
        {
            direction = Vector2.UnitX;
        }
        else
        {
            direction = Vector2.Normalize(direction);
        }

        return new Vector3(
            crystal.X + direction.X * DesiredOffset,
            crystal.Y,
            crystal.Z + direction.Y * DesiredOffset);
    }

    public static bool HasArrived(Vector3 player, Vector3 crystal)
    {
        return IsWithin(player, crystal, ArrivalDistance);
    }

    public static bool IsWithin(Vector3 player, Vector3 crystal, float distance)
    {
        return float.IsFinite(distance)
               && distance >= 0f
               && Vector3.DistanceSquared(player, crystal) <= distance * distance;
    }
}
