using BOCCHI.Data;
using BOCCHI.Enums;
using System.Collections.Generic;
using System.Numerics;

namespace BOCCHI.Pathfinding;

/// <summary>
/// Transit checkpoints for North Horn FATE 2075 when its nearest aethernet
/// leaves the player on the west side of the river.  Every leg was projected
/// onto the CN 7.55 vnavmesh cache and raycast before being added here.
/// </summary>
public static class NorthHornSouthCrossingRoute
{
    public const float ArrivalDistance = 4f;

    private const float WispApproachRadius = 90f;

    private static readonly Vector3 WispLanding = Aethernet.WillOWispVillage.GetData().Destination;

    // FollowPath travels in direct straight segments.  This is therefore a
    // dense land route, not sparse pathfinding checkpoints: every consecutive
    // leg is <= 15 yalms and has a successful navmesh raycast.
    private static readonly Vector3[] Waypoints =
    [
        new(-15.02f, 2.18f, -43.83f),
        new(-21.66f, 2.34f, -57.29f),
        new(-23.21f, 2.26f, -60.42f),
        new(-22.84f, 1.81f, -67.60f),
        new(-17.71f, 2.76f, -81.17f),
        new(-12.58f, 3.11f, -94.73f),
        new(-7.45f, 3.58f, -108.29f),
        new(-2.32f, 3.91f, -121.86f),
        new(0.71f, 4.36f, -136.38f),
        new(1.71f, 4.15f, -150.85f),
        new(2.72f, 4.20f, -165.31f),
        new(2.75f, 4.24f, -165.81f),
        new(2.13f, 3.49f, -180.30f),
        new(1.48f, 3.49f, -195.29f),
        new(0.86f, 4.25f, -209.77f),
        new(0.24f, 4.10f, -224.26f),
        new(-0.00f, 3.75f, -239.25f),
        new(-0.00f, 3.75f, -254.25f),
        new(0.00f, 3.62f, -269.25f),
        new(1.81f, 3.34f, -283.75f),
        new(9.56f, 3.27f, -296.59f),
        new(17.05f, 2.66f, -309.01f),
        new(18.08f, 2.52f, -310.72f),
        new(18.34f, 2.50f, -311.15f),
        new(21.56f, 2.50f, -324.26f),
        new(18.74f, 2.50f, -338.99f),
        new(15.91f, 2.50f, -353.72f),
        new(13.09f, 2.50f, -368.45f),
        new(10.04f, 2.50f, -383.14f),
        new(6.69f, 2.50f, -397.76f),
        new(3.35f, 2.50f, -412.38f),
        new(-0.00f, 2.50f, -427.00f),
        new(-0.00f, 2.50f, -442.00f),
        new(-0.00f, 2.50f, -457.00f),
        new(-0.00f, 3.81f, -471.50f),
        new(2.49f, 7.39f, -485.84f),
        new(9.71f, 10.67f, -498.42f),
        new(16.92f, 13.29f, -510.99f),
        new(31.67f, 12.81f, -512.24f),
        new(46.17f, 12.45f, -512.19f),
        new(60.67f, 12.90f, -512.13f),
        new(75.17f, 13.08f, -512.08f),
        new(89.67f, 13.44f, -512.02f),
        new(102.82f, 13.08f, -505.43f),
        new(112.72f, 10.69f, -494.84f),
        new(122.63f, 8.47f, -484.25f),
        new(134.71f, 6.96f, -476.11f),
        new(148.41f, 6.57f, -471.36f),
        new(162.11f, 6.26f, -466.61f),
        new(175.81f, 6.01f, -461.86f),
        new(189.51f, 5.52f, -457.11f),
        new(204.18f, 5.62f, -454.17f),
        new(218.98f, 5.69f, -451.73f),
        new(233.29f, 5.89f, -449.36f),
        new(245.22f, 6.33f, -440.80f),
        new(251.64f, 6.72f, -427.80f),
        new(259.69f, 6.65f, -415.38f),
        new(270.16f, 6.82f, -405.36f),
        new(280.64f, 7.02f, -395.33f),
        new(291.48f, 6.90f, -384.96f),
        new(301.02f, 6.50f, -373.40f),
        new(310.09f, 6.08f, -362.09f),
        new(319.17f, 4.40f, -350.78f),
        new(328.25f, 3.50f, -339.48f),
        new(337.32f, 3.03f, -328.17f),
        new(346.40f, 3.76f, -316.86f),
        new(355.48f, 3.43f, -305.55f),
        new(364.55f, 2.35f, -294.25f),
        new(373.94f, 2.29f, -282.55f),
        new(383.02f, 1.91f, -271.24f),
        new(392.09f, 1.41f, -259.93f),
        new(400.10f, 1.27f, -247.31f),
        new(407.31f, 1.15f, -234.16f),
        new(414.52f, 1.31f, -221.01f),
        new(421.49f, 1.73f, -208.29f),
        new(428.47f, 2.88f, -195.58f),
        new(431.11f, 2.96f, -190.76f),
        new(431.35f, 3.00f, -190.32f),
        new(434.65f, 3.00f, -175.68f),
        new(437.07f, 2.79f, -164.95f),
        new(445.18f, 3.57f, -152.93f),
        new(453.58f, 4.46f, -141.12f),
        new(462.17f, 5.89f, -129.44f),
        new(470.75f, 7.26f, -117.75f),
        new(479.34f, 8.46f, -106.06f),
        new(479.93f, 8.56f, -105.26f),
        new(489.91f, 10.52f, -94.74f),
        new(499.89f, 12.35f, -84.22f),
        new(506.77f, 14.60f, -71.47f),
        new(506.76f, 15.74f, -56.97f),
        new(506.75f, 16.10f, -42.47f),
        new(510.00f, 15.65f, -30.00f),
    ];

    public static bool MayNeedTransitRoute(EventData data)
    {
        return data.Type == EventType.Fate &&
               data.Id == 2075 &&
               data.EffectiveTerritoryId == ZoneData.NORTHHORN;
    }

    public static bool ShouldUse(EventData data, Vector3 playerPosition)
    {
        return MayNeedTransitRoute(data) &&
               Vector3.DistanceSquared(playerPosition, WispLanding) <= WispApproachRadius * WispApproachRadius;
    }

    public static bool TryCreate(EventData data, Vector3 playerPosition, out List<Vector3> route)
    {
        route = [];

        if (!ShouldUse(data, playerPosition))
        {
            return false;
        }

        route.AddRange(Waypoints);
        return true;
    }
}
