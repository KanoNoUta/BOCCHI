using System.Numerics;

namespace BOCCHI.Modules.AggroRange;

/// <summary>
/// A snapshot of an ordinary North Horn mob's resolved aggro trigger area.
/// Radius already includes live hitbox size, calibration/user correction and
/// the navigation safety margin.
/// </summary>
public readonly record struct AggroDangerZone(uint NameId, Vector3 Center, float Radius);
