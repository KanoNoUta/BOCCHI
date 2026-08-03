using BOCCHI.Data;
using BOCCHI.Modules;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Ui;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Ocelot;
using Ocelot.Modules;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace BOCCHI.Windows;

using BocchiModule = Modules.Module;

[OcelotConfigWindow]
public class ConfigWindow(Plugin primaryPlugin, Config config) : OcelotConfigWindow(primaryPlugin, config)
{
    private static readonly string[] SouthHornFates =
    [
        nameof(AutomatorConfig.DoRoughWaters),
        nameof(AutomatorConfig.DoTheGoldenGuardian),
        nameof(AutomatorConfig.DoKingOfTheCrescent),
        nameof(AutomatorConfig.DoTheWingedTerror),
        nameof(AutomatorConfig.DoAnUnendingDuty),
        nameof(AutomatorConfig.DoBrainDrain),
        nameof(AutomatorConfig.DoADelicateBalance),
        nameof(AutomatorConfig.DoSwornToSoil),
        nameof(AutomatorConfig.DoAPryingEye),
        nameof(AutomatorConfig.DoFatalAllure),
        nameof(AutomatorConfig.DoServingDarkness),
        nameof(AutomatorConfig.DoPersistentPots),
        nameof(AutomatorConfig.DoPleadingPots),
    ];

    private static readonly string[] SouthHornCriticalEncounters =
    [
        nameof(AutomatorConfig.DoScourgeOfTheMind),
        nameof(AutomatorConfig.DoTheBlackRegiment),
        nameof(AutomatorConfig.DoTheUnbridled),
        nameof(AutomatorConfig.DoCrawlingDeath),
        nameof(AutomatorConfig.DoCalamityBound),
        nameof(AutomatorConfig.DoTrialByClaw),
        nameof(AutomatorConfig.DoFromTimesBygone),
        nameof(AutomatorConfig.DoCompanyOfStone),
        nameof(AutomatorConfig.DoSharkAttack),
        nameof(AutomatorConfig.DoOnTheHunt),
        nameof(AutomatorConfig.DoWithExtremePrejudice),
        nameof(AutomatorConfig.DoNoiseComplaint),
        nameof(AutomatorConfig.DoCursedConcern),
        nameof(AutomatorConfig.DoEternalWatch),
        nameof(AutomatorConfig.DoFlameOfDusk),
    ];

    private static readonly string[] NorthHornFates =
    [
        nameof(AutomatorConfig.DoNorthHornFate2072),
        nameof(AutomatorConfig.DoNorthHornFate2073),
        nameof(AutomatorConfig.DoNorthHornFate2074),
        nameof(AutomatorConfig.DoNorthHornFate2075),
        nameof(AutomatorConfig.DoNorthHornFate2076),
        nameof(AutomatorConfig.DoNorthHornFate2077),
        nameof(AutomatorConfig.DoNorthHornFate2078),
        nameof(AutomatorConfig.DoNorthHornFate2079),
        nameof(AutomatorConfig.DoNorthHornFate2080),
        nameof(AutomatorConfig.DoNorthHornFate2081),
        nameof(AutomatorConfig.DoNorthHornFate2082),
        nameof(AutomatorConfig.DoNorthHornFate2083),
        nameof(AutomatorConfig.DoNorthHornFate2084),
    ];

    private static readonly string[] NorthHornCriticalEncounters =
    [
        nameof(AutomatorConfig.DoNorthHornCe49),
        nameof(AutomatorConfig.DoNorthHornCe50),
        nameof(AutomatorConfig.DoNorthHornCe51),
        nameof(AutomatorConfig.DoNorthHornCe52),
        nameof(AutomatorConfig.DoNorthHornCe53),
        nameof(AutomatorConfig.DoNorthHornCe54),
        nameof(AutomatorConfig.DoNorthHornCe55),
        nameof(AutomatorConfig.DoNorthHornCe56),
        nameof(AutomatorConfig.DoNorthHornCe57),
        nameof(AutomatorConfig.DoNorthHornCe58),
        nameof(AutomatorConfig.DoNorthHornCe59),
        nameof(AutomatorConfig.DoNorthHornCe60),
        nameof(AutomatorConfig.DoNorthHornCe61),
        nameof(AutomatorConfig.DoNorthHornCe62),
        nameof(AutomatorConfig.DoNorthHornCe63),
    ];

    private IModule? selectedConfigModule;
    private string settingsSearch = string.Empty;
    private string southHornMobSearch = string.Empty;
    private string northHornMobSearch = string.Empty;

    private sealed record ConfigEntry(
        IModule Module,
        BocchiModule Concrete,
        string Title,
        BocchiSettingsGroup Group);

    public override void PostInitialize()
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(1040, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    protected override void Render(RenderContext context)
    {
        var entries = Plugin.Modules.GetModulesByConfigOrder()
            .Where(module => module is BocchiModule concrete && concrete.Config != null)
            .Select(module =>
            {
                var concrete = (BocchiModule)module;
                return new ConfigEntry(
                    module,
                    concrete,
                    concrete.Config!.GetTitle() ?? concrete.Config.GetType().Name,
                    BocchiUiPolicy.GetSettingsGroup(module.GetType().Name));
            })
            .ToList();
        var visibleEntries = entries
            .Where(entry => BocchiUiPolicy.MatchesSettingsSearch(
                entry.Title,
                entry.Group,
                settingsSearch,
                GetSettingsGroupLabel(entry.Group)))
            .ToList();

        selectedConfigModule ??= entries.FirstOrDefault()?.Module;
        if (selectedConfigModule != null && visibleEntries.All(entry => entry.Module != selectedConfigModule))
        {
            selectedConfigModule = visibleEntries.FirstOrDefault()?.Module;
        }

        var wide = ImGui.GetContentRegionAvail().X >= 840f;
        if (wide)
        {
            using (ImRaii.Child("##ConfigNavigation", new Vector2(248f, 0f), false))
            {
                DrawSettingsSearch();
                ImGui.Spacing();
                DrawGroupedNavigation(visibleEntries);
            }
            ImGui.SameLine();
            DrawConfigContent(context);
            return;
        }

        DrawSettingsSearch();
        DrawNarrowNavigation(visibleEntries);
        ImGui.Separator();
        DrawConfigContent(context);
    }

    private void DrawSettingsSearch()
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##SettingsSearch", T("search_hint"), ref settingsSearch, 128);
    }

    private void DrawGroupedNavigation(IReadOnlyList<ConfigEntry> entries)
    {
        if (entries.Count == 0)
        {
            BocchiUi.EmptyState(T("no_matches.title"), T("no_matches.detail"));
            return;
        }

        foreach (var group in Enum.GetValues<BocchiSettingsGroup>())
        {
            var groupEntries = entries.Where(entry => entry.Group == group).ToList();
            if (groupEntries.Count == 0)
            {
                continue;
            }

            ImGui.TextDisabled(GetSettingsGroupLabel(group));
            foreach (var entry in groupEntries)
            {
                if (ImGui.Selectable(
                        $"{entry.Title}##ConfigNav-{entry.Module.GetType().FullName}",
                        entry.Module == selectedConfigModule,
                        ImGuiSelectableFlags.None,
                        new Vector2(0f, ImGui.GetFrameHeight())))
                {
                    selectedConfigModule = entry.Module;
                }
            }
            ImGui.Spacing();
        }
    }

    private void DrawNarrowNavigation(IReadOnlyList<ConfigEntry> entries)
    {
        if (entries.Count == 0)
        {
            BocchiUi.EmptyState(T("no_matches.title"), T("no_matches.detail"));
            return;
        }

        var selectedEntry = entries.FirstOrDefault(entry => entry.Module == selectedConfigModule)
                            ?? entries[0];
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo("##SettingsPage", $"{GetSettingsGroupLabel(selectedEntry.Group)}  /  {selectedEntry.Title}"))
        {
            return;
        }

        foreach (var group in Enum.GetValues<BocchiSettingsGroup>())
        {
            foreach (var entry in entries.Where(entry => entry.Group == group))
            {
                var label = $"{GetSettingsGroupLabel(group)}  /  {entry.Title}";
                if (ImGui.Selectable(
                        $"{label}##ConfigCombo-{entry.Module.GetType().FullName}",
                        entry.Module == selectedConfigModule))
                {
                    selectedConfigModule = entry.Module;
                }
            }
        }
        ImGui.EndCombo();
    }

    private void DrawConfigContent(RenderContext context)
    {
        var width = MathF.Min(760f, ImGui.GetContentRegionAvail().X);
        using (ImRaii.Child("##ConfigContent", new Vector2(width, 0f), false))
        {
            if (selectedConfigModule != null
                && BocchiUiPolicy.GetSettingsGroup(selectedConfigModule.GetType().Name) == BocchiSettingsGroup.Advanced)
            {
                DrawAdvancedUiPreference();
            }

            switch (selectedConfigModule)
            {
                case AutomatorModule automator:
                    DrawAutomatorConfig(automator);
                    break;
                case MobFarmerModule mobFarmer:
                    DrawMobFarmerConfig(mobFarmer);
                    break;
                case not null:
                    selectedConfigModule.RenderConfigUi(context);
                    break;
                default:
                    BocchiUi.EmptyState(T("no_modules.title"), T("no_modules.detail"));
                    break;
            }
        }
    }

    private void DrawAdvancedUiPreference()
    {
        ImGui.TextDisabled(T("advanced.title"));
        var showAdvanced = config.ShowAdvancedUi;
        if (ImGui.Checkbox($"{T("advanced.toggle")}##ShowAdvancedUi", ref showAdvanced))
        {
            config.ShowAdvancedUi = showAdvanced;
            config.Save();
        }
        ImGui.TextDisabled(T("advanced.help"));
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawAutomatorConfig(AutomatorModule module)
    {
        ImGui.TextUnformatted(module.Config.GetTitle() ?? T("editors.automation.title"));
        ImGui.TextDisabled(T("editors.automation.subtitle"));
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##AutomatorConfigTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            return;
        }

        if (ImGui.BeginTabItem(T("editors.automation.common_tab")))
        {
            DrawAutomatorCommon(module);
            ImGui.EndTabItem();
        }

        // The visible label carries a live enabled/total count. ImGui derives a
        // tab's identity from its label text, so a changing count would spawn a
        // "new" tab every time a checkbox is toggled and snap the view back to
        // the first tab. The ### suffix pins a stable id independent of the
        // count so the active tab stays put while editing.
        if (ImGui.BeginTabItem($"{string.Format(T("editors.automation.south_events"), CountEnabled(module.Config, SouthHornFates) + CountEnabled(module.Config, SouthHornCriticalEncounters), SouthHornFates.Length + SouthHornCriticalEncounters.Length)}###AutomatorSouthHornEvents"))
        {
            DrawIslandEvents(module, "SouthHorn", SouthHornFates, SouthHornCriticalEncounters);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{string.Format(T("editors.automation.north_events"), CountEnabled(module.Config, NorthHornFates) + CountEnabled(module.Config, NorthHornCriticalEncounters), NorthHornFates.Length + NorthHornCriticalEncounters.Length)}###AutomatorNorthHornEvents"))
        {
            DrawIslandEvents(module, "NorthHorn", NorthHornFates, NorthHornCriticalEncounters);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawAutomatorCommon(AutomatorModule module)
    {
        var cfg = module.Config;

        DrawSectionTitle(T("editors.automation.combat_targets"));
        DrawAiProvider(module);
        DrawBoolean(module, cfg, nameof(cfg.ToggleAiProvider));
        DrawBoolean(module, cfg, nameof(cfg.ChangeLowLevelJob));
        DrawBoolean(module, cfg, nameof(cfg.ForceTarget));
        DrawBoolean(module, cfg, nameof(cfg.ForceTargetCentralEnemy), !cfg.ForceTarget);
        DrawFloat(module, cfg, nameof(cfg.EngagementRange), 5f, 30f, "%.1f");

        DrawSectionTitle(T("editors.automation.event_behavior"));
        DrawBoolean(module, cfg, nameof(cfg.StanceOnBeforeDoFates), !cfg.DoFates);
        DrawBoolean(module, cfg, nameof(cfg.StanceOffBeforeCriticalEncounters), !cfg.DoCriticalEncounters);
        DrawBoolean(module, cfg, nameof(cfg.DelayCriticalEncounters), !cfg.DoCriticalEncounters);
        DrawFloat(module, cfg, nameof(cfg.MinDelay), 0f, 30f, T("units.seconds_1"), !cfg.DelayCriticalEncounters);
        DrawFloat(module, cfg, nameof(cfg.MaxDelay), 0f, 30f, T("units.seconds_1"), !cfg.DelayCriticalEncounters);

        DrawSectionTitle(T("editors.automation.instance_rotation"));
        DrawInitialInstanceArea(module);
        DrawBoolean(module, cfg, nameof(cfg.AutoRotateInstance));
        DrawFloat(module, cfg, nameof(cfg.InstanceStayMinutes), 15f, 180f, T("units.minutes_0"), !cfg.AutoRotateInstance);
        DrawBoolean(module, cfg, nameof(cfg.RotateWhenPopulationLow), !cfg.AutoRotateInstance);
        DrawInt(module, cfg, nameof(cfg.MinimumInstancePopulation), 1, 72, !cfg.AutoRotateInstance || !cfg.RotateWhenPopulationLow);
    }

    private void DrawInitialInstanceArea(AutomatorModule module)
    {
        var current = module.Config.InitialInstanceArea;
        var label = ConfigLabel(module, nameof(module.Config.InitialInstanceArea));
        var preview = module.T($"config.initial_instance_area.options.{ToSnakeCase(current.ToString())}");
        if (ImGui.BeginCombo($"{label}##InitialInstanceArea", preview))
        {
            foreach (var area in Enum.GetValues<InstanceEntryArea>())
            {
                var option = module.T($"config.initial_instance_area.options.{ToSnakeCase(area.ToString())}");
                if (ImGui.Selectable(option, area == current))
                {
                    module.Config.InitialInstanceArea = area;
                    primaryPlugin.Config.Save();
                }
            }
            ImGui.EndCombo();
        }
        ConfigTooltip(module, nameof(module.Config.InitialInstanceArea));
    }

    private void DrawAiProvider(AutomatorModule module)
    {
        var current = module.Config.AiProvider;
        var label = ConfigLabel(module, nameof(module.Config.AiProvider));
        if (ImGui.BeginCombo($"{label}##AiProvider", current.ToLabel()))
        {
            foreach (var provider in Enum.GetValues<AiType>())
            {
                if (ImGui.Selectable(provider.ToLabel(), provider == current))
                {
                    module.Config.AiProvider = provider;
                    primaryPlugin.Config.Save();
                }
            }
            ImGui.EndCombo();
        }
        ConfigTooltip(module, nameof(module.Config.AiProvider));
    }

    private void DrawIslandEvents(
        AutomatorModule module,
        string id,
        IReadOnlyList<string> fateProperties,
        IReadOnlyList<string> criticalEncounterProperties)
    {
        ImGui.TextDisabled(T("editors.automation.island_help"));
        var columns = BocchiUiPolicy.GetWorkspaceColumns(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginTable(
                $"##{id}EventColumns",
                columns,
                ImGuiTableFlags.SizingStretchSame | (columns > 1 ? ImGuiTableFlags.BordersInnerV : ImGuiTableFlags.None)))
        {
            ImGui.TableNextColumn();
            DrawEventGroup(module, id, "FATE", nameof(AutomatorConfig.DoFates), fateProperties);
            ImGui.TableNextColumn();
            DrawEventGroup(module, id, "CE", nameof(AutomatorConfig.DoCriticalEncounters), criticalEncounterProperties);
            ImGui.EndTable();
        }
    }

    private void DrawEventGroup(
        AutomatorModule module,
        string islandId,
        string title,
        string masterProperty,
        IReadOnlyList<string> eventProperties)
    {
        var cfg = module.Config;
        DrawSectionTitle($"{title}  {CountEnabled(cfg, eventProperties)}/{eventProperties.Count}");
        DrawBoolean(module, cfg, masterProperty);

        if (ImGui.SmallButton($"{T("actions.enable_all")}##{islandId}-{title}-all"))
        {
            SetAll(cfg, eventProperties, true);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton($"{T("actions.disable_all")}##{islandId}-{title}-none"))
        {
            ImGui.OpenPopup($"##ConfirmDisableAll-{islandId}-{title}");
        }
        if (ImGui.BeginPopup($"##ConfirmDisableAll-{islandId}-{title}"))
        {
            ImGui.TextUnformatted(string.Format(T("actions.confirm_disable_all"), title));
            if (ImGui.Button($"{T("actions.disable_all")}##Confirm-{islandId}-{title}"))
            {
                SetAll(cfg, eventProperties, false);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button($"{T("actions.cancel")}##Cancel-{islandId}-{title}"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        var masterEnabled = (bool)(cfg.GetType().GetProperty(masterProperty)?.GetValue(cfg) ?? false);
        ImGui.BeginDisabled(!masterEnabled);
        foreach (var property in eventProperties)
        {
            DrawBoolean(module, cfg, property);
        }
        ImGui.EndDisabled();
    }

    private void DrawMobFarmerConfig(MobFarmerModule module)
    {
        ImGui.TextUnformatted(module.Config.GetTitle() ?? T("editors.farming.title"));
        ImGui.TextDisabled(T("editors.farming.subtitle"));
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##MobFarmerConfigTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            return;
        }

        // See the automator tabs above: pin a stable ### id so toggling a mob
        // (which changes the selected count in the label) does not reset the
        // active tab back to the first one.
        if (ImGui.BeginTabItem($"{string.Format(T("editors.farming.south_mobs"), module.Config.SouthHornMobs.Count)}###MobFarmerSouthHornMobs"))
        {
            DrawMobSelector(
                "SouthHorn",
                MobData.SouthHornMobs,
                module.Config.SouthHornMobs,
                ref southHornMobSearch);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"{string.Format(T("editors.farming.north_mobs"), module.Config.NorthHornMobs.Count)}###MobFarmerNorthHornMobs"))
        {
            DrawMobSelector(
                "NorthHorn",
                MobData.NorthHornMobs,
                module.Config.NorthHornMobs,
                ref northHornMobSearch);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem(T("editors.farming.common_tab")))
        {
            DrawMobFarmerCommon(module);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawMobSelector(
        string id,
        IReadOnlyList<Mob> available,
        List<Mob> selected,
        ref string search)
    {
        ImGui.SetNextItemWidth(MathF.Min(360f, ImGui.GetContentRegionAvail().X));
        ImGui.InputTextWithHint($"##{id}MobSearch", T("editors.farming.search_hint"), ref search, 128);

        var searchTerm = search.Trim();
        var visible = available
            .Where(mob => searchTerm.Length == 0
                          || MobData.GetName(mob).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ImGui.SmallButton($"{T("actions.select_visible")}##{id}"))
        {
            foreach (var mob in visible)
            {
                if (!selected.Contains(mob))
                {
                    selected.Add(mob);
                }
            }
            SortAndSave(selected);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton($"{T("actions.clear")}##{id}"))
        {
            ImGui.OpenPopup($"##ConfirmClearMobs-{id}");
        }
        ImGui.SameLine();
        ImGui.TextDisabled(string.Format(T("editors.farming.selection_summary"), selected.Count, available.Count, visible.Count));

        if (ImGui.BeginPopup($"##ConfirmClearMobs-{id}"))
        {
            ImGui.TextUnformatted(T("actions.confirm_clear_mobs"));
            if (ImGui.Button($"{T("actions.clear")}##ConfirmClearMobs-{id}"))
            {
                selected.Clear();
                primaryPlugin.Config.Save();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button($"{T("actions.cancel")}##CancelClearMobs-{id}"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        using (ImRaii.Child($"##{id}MobList", new Vector2(0, 0), true))
        {
            var columns = ImGui.GetContentRegionAvail().X >= 620f ? 2 : 1;
            if (!ImGui.BeginTable($"##{id}MobTable", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg))
            {
                return;
            }

            foreach (var mob in visible)
            {
                ImGui.TableNextColumn();
                var isSelected = selected.Contains(mob);
                if (ImGui.Checkbox($"{MobData.GetName(mob)}##{id}-{(uint)mob}", ref isSelected))
                {
                    if (isSelected)
                    {
                        selected.Add(mob);
                    }
                    else
                    {
                        selected.Remove(mob);
                    }
                    SortAndSave(selected);
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawMobFarmerCommon(MobFarmerModule module)
    {
        var cfg = module.Config;
        DrawSectionTitle(T("editors.farming.basic"));
        DrawBoolean(module, cfg, nameof(cfg.Enabled));
        DrawBoolean(module, cfg, nameof(cfg.ConsiderSpecialMobs));
        DrawInt(module, cfg, nameof(cfg.MaxMobLevel), 1, 40);
        DrawFloat(module, cfg, nameof(cfg.MaxEuclideanDistance), 10f, 1000f, "%.0f");

        DrawSectionTitle(T("editors.farming.loop_return"));
        DrawInt(module, cfg, nameof(cfg.MinimumMobsToStartLoop), 0, 20);
        DrawInt(module, cfg, nameof(cfg.MinimumMobsToStartFight), 1, 20);
        DrawInt(module, cfg, nameof(cfg.ExtraTimeToWait), 0, 20);
        DrawBoolean(module, cfg, nameof(cfg.ReturnToStartInWaitingPhase));
        DrawFloat(module, cfg, nameof(cfg.MinEuclideanDistanceToReturnHome), 10f, 1000f, "%.0f", !cfg.ReturnToStartInWaitingPhase);

        DrawSectionTitle(T("editors.farming.battle_bell"));
        DrawBoolean(module, cfg, nameof(cfg.ApplyBattleBell));
        DrawFloat(module, cfg, nameof(cfg.MaximumBattleBellWaitTime), 0f, 30f, T("units.seconds_1"), !cfg.ApplyBattleBell);

        DrawSectionTitle(T("editors.farming.debug_display"));
        DrawBoolean(module, cfg, nameof(cfg.RenderDebugLines));
        DrawBoolean(module, cfg, nameof(cfg.RenderDebugLinesWhileNotRunning), !cfg.RenderDebugLines);
    }

    private void DrawBoolean(BocchiModule module, object target, string propertyName, bool disabled = false)
    {
        var property = GetProperty(target, propertyName, typeof(bool));
        var value = (bool)(property.GetValue(target) ?? false);
        ImGui.BeginDisabled(disabled);
        if (ImGui.Checkbox($"{ConfigLabel(module, propertyName)}##{target.GetType().Name}-{propertyName}", ref value))
        {
            property.SetValue(target, value);
            primaryPlugin.Config.Save();
        }
        ConfigTooltip(module, propertyName);
        ImGui.EndDisabled();
    }

    private void DrawFloat(
        BocchiModule module,
        object target,
        string propertyName,
        float minimum,
        float maximum,
        string format,
        bool disabled = false)
    {
        var property = GetProperty(target, propertyName, typeof(float));
        var value = (float)(property.GetValue(target) ?? minimum);
        ImGui.BeginDisabled(disabled);
        ImGui.SetNextItemWidth(MathF.Min(420f, ImGui.GetContentRegionAvail().X));
        if (ImGui.SliderFloat($"{ConfigLabel(module, propertyName)}##{target.GetType().Name}-{propertyName}", ref value, minimum, maximum, format))
        {
            property.SetValue(target, value);
            primaryPlugin.Config.Save();
        }
        ConfigTooltip(module, propertyName);
        ImGui.EndDisabled();
    }

    private void DrawInt(
        BocchiModule module,
        object target,
        string propertyName,
        int minimum,
        int maximum,
        bool disabled = false)
    {
        var property = GetProperty(target, propertyName, typeof(int));
        var value = (int)(property.GetValue(target) ?? minimum);
        ImGui.BeginDisabled(disabled);
        ImGui.SetNextItemWidth(MathF.Min(420f, ImGui.GetContentRegionAvail().X));
        if (ImGui.SliderInt($"{ConfigLabel(module, propertyName)}##{target.GetType().Name}-{propertyName}", ref value, minimum, maximum))
        {
            property.SetValue(target, value);
            primaryPlugin.Config.Save();
        }
        ConfigTooltip(module, propertyName);
        ImGui.EndDisabled();
    }

    private void SetAll(AutomatorConfig cfg, IEnumerable<string> propertyNames, bool value)
    {
        foreach (var propertyName in propertyNames)
        {
            GetProperty(cfg, propertyName, typeof(bool)).SetValue(cfg, value);
        }
        primaryPlugin.Config.Save();
    }

    private void SortAndSave(List<Mob> selected)
    {
        selected.Sort((left, right) => ((uint)left).CompareTo((uint)right));
        primaryPlugin.Config.Save();
    }

    private static int CountEnabled(object target, IEnumerable<string> propertyNames)
    {
        return propertyNames.Count(name =>
            (bool)(GetProperty(target, name, typeof(bool)).GetValue(target) ?? false));
    }

    private static PropertyInfo GetProperty(object target, string propertyName, Type propertyType)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property == null || property.PropertyType != propertyType || !property.CanRead || !property.CanWrite)
        {
            throw new InvalidOperationException($"Invalid config property {target.GetType().Name}.{propertyName}.");
        }
        return property;
    }

    private static void DrawSectionTitle(string title)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(title);
        ImGui.Separator();
    }

    private static string ConfigLabel(BocchiModule module, string propertyName)
    {
        if (propertyName == "Enabled")
        {
            return I18N.T("generic.label.enabled");
        }

        return module.T($"config.{ToSnakeCase(propertyName)}.label");
    }

    private static void ConfigTooltip(BocchiModule module, string propertyName)
    {
        if (propertyName == "Enabled")
        {
            return;
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(module.T($"config.{ToSnakeCase(propertyName)}.tooltip"));
        }
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsUpper(current) && index > 0)
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(current));
        }
        return result.ToString();
    }

    private static string GetSettingsGroupLabel(BocchiSettingsGroup group) =>
        T($"groups.{BocchiUiPolicy.GetSettingsGroupKey(group)}");

    private static string T(string key) => I18N.T($"windows.config.{key}");
}
