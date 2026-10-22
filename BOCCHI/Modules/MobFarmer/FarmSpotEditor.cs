using BOCCHI.Ui.Lumin;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BOCCHI.Modules.MobFarmer;

/// <summary>
/// Editor for the farmer's named spots: add the current position, set a
/// stack point, rename, reorder, enable and delete. Ported from upstream
/// BOCCHI 4.2's FarmSpotListRenderer.
/// </summary>
public static class FarmSpotEditor
{
    public static void Draw(MobFarmerModule module, Action save)
    {
        var spots = module.Config.Spots;
        var position = Svc.Objects.LocalPlayer?.Position;
        var territory = Svc.ClientState.TerritoryType;

        ImGui.TextWrapped(module.T("spots.help"));
        ImGui.BeginDisabled(position == null);
        if (ImGui.Button($"{module.T("spots.add_here")}##MobFarmerAddSpot") && position is { } here)
        {
            var spot = new FarmSpot
            {
                Name = string.Format(module.T("spots.default_name"), spots.Count + 1),
                Priority = FarmSpotPolicy.NextPriority(spots),
                TerritoryId = territory,
            };
            spot.SetOrigin(here);
            spots.Add(spot);
            save();
        }
        ImGui.EndDisabled();

        if (spots.Count == 0)
        {
            ImGui.TextDisabled(module.T("spots.empty"));
            return;
        }

        var current = module.Farmer.Running ? module.Farmer.Spots.Current : null;
        FarmSpot? removed = null;
        var ordered = FarmSpotPolicy.Ordered(spots).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var spot = ordered[index];

            // The id follows the spot itself, so renaming or reordering keeps it open.
            ImGui.PushID(RuntimeHelpers.GetHashCode(spot));
            if (ImGui.CollapsingHeader($"{GetHeader(module, spot, current, position, territory)}###Spot"))
            {
                if (DrawSpot(module, spots, spot, position, index, ordered.Count, save))
                {
                    removed = spot;
                }
            }

            ImGui.PopID();
        }

        if (removed != null)
        {
            spots.Remove(removed);
            save();
        }
    }

    private static string GetHeader(
        MobFarmerModule module,
        FarmSpot spot,
        FarmSpot? current,
        Vector3? position,
        uint territory)
    {
        var header = spot.Name;
        if (!FarmSpotPolicy.IsInZone(spot, territory))
        {
            header += $"  {module.T("spots.other_zone")}";
        }
        else if (position is { } here)
        {
            header += $"  {string.Format(module.T("spots.distance"), FarmSpotPolicy.Distance2D(here, spot.GetOrigin()))}";
        }

        if (!spot.Enabled)
        {
            header += $"  {module.T("spots.disabled")}";
        }

        if (ReferenceEquals(spot, current))
        {
            header += $"  {module.T("spots.current")}";
        }

        return header;
    }

    /// <summary>Returns true when the spot should be deleted.</summary>
    private static bool DrawSpot(
        MobFarmerModule module,
        List<FarmSpot> spots,
        FarmSpot spot,
        Vector3? position,
        int index,
        int count,
        Action save)
    {
        var name = spot.Name;
        ImGui.SetNextItemWidth(MathF.Min(LuminTheme.S(260f), ImGui.GetContentRegionAvail().X));
        if (ImGui.InputText(module.T("spots.name"), ref name, 64))
        {
            spot.Name = string.IsNullOrWhiteSpace(name) ? module.T("spots.unnamed") : name;
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            save();
        }

        var enabled = spot.Enabled;
        if (ImGui.Checkbox(module.T("spots.enabled"), ref enabled))
        {
            spot.Enabled = enabled;
            save();
        }

        var priority = spot.Priority;
        ImGui.SetNextItemWidth(MathF.Min(LuminTheme.S(140f), ImGui.GetContentRegionAvail().X));
        if (ImGui.InputInt(module.T("spots.priority"), ref priority, 1, 5))
        {
            spot.Priority = priority;
            save();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(index == 0);
        if (ImGui.SmallButton(module.T("spots.move_up")))
        {
            FarmSpotPolicy.Move(spots, spot, up: true);
            save();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(index >= count - 1);
        if (ImGui.SmallButton(module.T("spots.move_down")))
        {
            FarmSpotPolicy.Move(spots, spot, up: false);
            save();
        }
        ImGui.EndDisabled();

        var origin = spot.GetOrigin();
        ImGui.TextDisabled(string.Format(module.T("spots.origin"), origin.X, origin.Y, origin.Z));
        ImGui.BeginDisabled(position == null);
        if (ImGui.SmallButton(module.T("spots.set_origin")) && position is { } here)
        {
            spot.SetOrigin(here);
            spot.TerritoryId = Svc.ClientState.TerritoryType;
            save();
        }
        ImGui.EndDisabled();

        var useStack = spot.UseStackPoint;
        if (ImGui.Checkbox(module.T("spots.use_stack"), ref useStack))
        {
            spot.UseStackPoint = useStack;
            if (useStack && spot.StackX == 0f && spot.StackY == 0f && spot.StackZ == 0f && position is { } stackHere)
            {
                spot.SetStackPoint(stackHere);
            }

            save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(module.T("spots.use_stack_tooltip"));
        }

        if (spot.UseStackPoint)
        {
            ImGui.TextDisabled(string.Format(module.T("spots.stack"), spot.StackX, spot.StackY, spot.StackZ));
            ImGui.BeginDisabled(position == null);
            if (ImGui.SmallButton(module.T("spots.set_stack")) && position is { } stackHere)
            {
                spot.SetStackPoint(stackHere);
                save();
            }
            ImGui.EndDisabled();
        }

        var minimum = spot.MinimumMobsToStartFight;
        ImGui.SetNextItemWidth(MathF.Min(LuminTheme.S(260f), ImGui.GetContentRegionAvail().X));
        if (ImGui.SliderInt(module.T("spots.min_fight"), ref minimum, 0, FarmSpotPolicy.MaximumMobsToStartFight))
        {
            spot.MinimumMobsToStartFight = minimum;
            save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(module.T("spots.min_fight_tooltip"));
        }

        ImGui.Spacing();
        return ImGui.SmallButton(module.T("spots.delete"));
    }
}
