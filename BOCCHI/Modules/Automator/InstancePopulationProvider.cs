using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;

namespace BOCCHI.Modules.Automator;

/// <summary>
/// Reads the server-backed special-field member list used by the native
/// ContentMemberList UI. It intentionally never estimates population from the
/// nearby object table.
/// </summary>
public sealed unsafe class InstancePopulationProvider
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ResponseDelay = TimeSpan.FromSeconds(2);

    private DateTimeOffset nextRequestAt;
    private DateTimeOffset? sampleAt;
    private int consecutiveLowSamples;

    public int? CurrentPopulation { get; private set; }

    public bool IsConfirmedBelow(int threshold)
    {
        return CurrentPopulation is > 0 && CurrentPopulation < threshold && consecutiveLowSamples >= 2;
    }

    public void Update(DateTimeOffset now, int lowThreshold)
    {
        if (sampleAt is { } due && now >= due)
        {
            sampleAt = null;
            var population = ReadPopulation();
            if (population > 0)
            {
                CurrentPopulation = population;
                consecutiveLowSamples = population < lowThreshold ? consecutiveLowSamples + 1 : 0;
            }
        }

        if (now < nextRequestAt || !TryRequestPopulation())
        {
            return;
        }

        sampleAt = now + ResponseDelay;
        nextRequestAt = now + RefreshInterval;
    }

    public void Reset()
    {
        CurrentPopulation = null;
        consecutiveLowSamples = 0;
        nextRequestAt = default;
        sampleAt = null;
    }

    private static int ReadPopulation()
    {
        var proxy = InfoProxyContentMember.Instance();
        return proxy == null ? 0 : checked((int)proxy->EntryCount);
    }

    private static bool TryRequestPopulation()
    {
        var agentModule = AgentModule.Instance();
        var proxy = InfoProxyContentMember.Instance();
        if (agentModule == null || proxy == null)
        {
            return false;
        }

        var agent = agentModule->GetAgentByInternalId(AgentId.ContentMemberList);
        if (agent == null || agent->IsAgentActive())
        {
            return false;
        }

        var returnValue = stackalloc AtkValue[1];
        var arguments = stackalloc AtkValue[1];
        arguments[0].SetInt(1);
        agent->ReceiveEvent(returnValue, arguments, 1, 0);
        return true;
    }
}
