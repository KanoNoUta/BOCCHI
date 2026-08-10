using BOCCHI.Data;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Fates;
using ECommons;
using Ocelot.Modules;
using System;
using System.Numerics;

namespace BOCCHI.Modules.Fates;

public class Fate
{
    public readonly EventData Data;

    public uint Id { get; }

    public string Name { get; private set; } = "Unknown Fate";

    public float Radius { get; private set; }

    public Vector3 StartPosition { get; private set; }

    public readonly EventProgress Progress = new();

    public byte CurrentProgress { get; private set; }

    public long TimeRemaining { get; private set; }

    public long SpawnedAt { get; }

    public Fate(IFate fate)
    {
        Id = fate.FateId;
        SpawnedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Data = EventData.GetFate(Id, ECommons.DalamudServices.Svc.ClientState.TerritoryType);
        Refresh(fate);
    }

    internal void Refresh(IFate fate)
    {
        // IFate is backed by game memory and becomes invalid as soon as the FATE
        // despawns. Copy every value while the object is present in Svc.Fates so
        // despawn callbacks and long-lived activities only touch managed data.
        Name = fate.Name.GetText();
        Radius = Data.Radius ?? fate.Radius;
        StartPosition = Data.StartPosition ?? fate.Position;
        CurrentProgress = fate.Progress;
        TimeRemaining = fate.TimeRemaining;
    }

    public void Update(UpdateContext context)
    {
        if (CurrentProgress <= 0)
        {
            return;
        }

        if (Progress.Count == 0 || Progress.Latest != CurrentProgress)
        {
            Progress.Add(CurrentProgress);
        }
    }

    public bool IsPotFate()
    {
        return Data.IsPot || Data.Note == MonsterNote.PersistentPots;
    }

    public Aethernet GetAethernet()
    {
        return Data.Aethernet ?? ZoneData.GetClosestAethernetShard(StartPosition);
    }
}
