namespace BOCCHI.Pathfinding;

public enum PathfinderState
{
    None,
    LoadingFile,
    FileUnavailable,
    NoCompatibleNodes,
    FileLoaded,
    Pathfinding,
    PathfindingDone,
}
