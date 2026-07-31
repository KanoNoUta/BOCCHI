using BOCCHI.Data;
using BOCCHI.Modules;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.MobFarmer;
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
    private string southHornMobSearch = string.Empty;
    private string northHornMobSearch = string.Empty;

    public override void PostInitialize()
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 520),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(980, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    protected override void Render(RenderContext context)
    {
        var modules = Plugin.Modules.GetModulesByConfigOrder()
            .Where(module => module is BocchiModule concrete && concrete.Config != null)
            .ToList();
        selectedConfigModule ??= modules.FirstOrDefault();

        using (ImRaii.Child("##ConfigNavigation", new Vector2(230, 0), true))
        {
            ImGui.TextDisabled("功能设置");
            ImGui.Separator();
            foreach (var module in modules)
            {
                var concreteModule = (BocchiModule)module;
                var name = concreteModule.Config!.GetTitle() ?? concreteModule.Config.GetType().Name;
                if (ImGui.Selectable($"{name}##{module.GetType().FullName}", module == selectedConfigModule))
                {
                    selectedConfigModule = module;
                }
            }
        }

        ImGui.SameLine();
        using (ImRaii.Child("##ConfigContent", Vector2.Zero, true))
        {
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
                    ImGui.TextDisabled("没有可用的设置模块。");
                    break;
            }
        }
    }

    private void DrawAutomatorConfig(AutomatorModule module)
    {
        ImGui.TextUnformatted(module.Config.GetTitle() ?? "非法模式");
        ImGui.TextDisabled("通用行为与南、北岛事件开关已分区，修改后立即保存。");
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##AutomatorConfigTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            return;
        }

        if (ImGui.BeginTabItem("通用设置"))
        {
            DrawAutomatorCommon(module);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"南岛事件 ({CountEnabled(module.Config, SouthHornFates) + CountEnabled(module.Config, SouthHornCriticalEncounters)}/{SouthHornFates.Length + SouthHornCriticalEncounters.Length})"))
        {
            DrawIslandEvents(module, "SouthHorn", SouthHornFates, SouthHornCriticalEncounters);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"北岛事件 ({CountEnabled(module.Config, NorthHornFates) + CountEnabled(module.Config, NorthHornCriticalEncounters)}/{NorthHornFates.Length + NorthHornCriticalEncounters.Length})"))
        {
            DrawIslandEvents(module, "NorthHorn", NorthHornFates, NorthHornCriticalEncounters);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawAutomatorCommon(AutomatorModule module)
    {
        var cfg = module.Config;

        var enabled = cfg.Enabled;
        if (ImGui.Checkbox($"{ConfigLabel(module, nameof(cfg.Enabled))}##AutomatorEnabled", ref enabled))
        {
            if (enabled)
            {
                module.EnableIllegalMode();
            }
            else
            {
                module.DisableIllegalMode();
            }
        }
        ConfigTooltip(module, nameof(cfg.Enabled));

        DrawSectionTitle("战斗与目标");
        DrawAiProvider(module);
        DrawBoolean(module, cfg, nameof(cfg.ToggleAiProvider));
        DrawBoolean(module, cfg, nameof(cfg.ChangeLowLevelJob));
        DrawBoolean(module, cfg, nameof(cfg.ForceTarget));
        DrawBoolean(module, cfg, nameof(cfg.ForceTargetCentralEnemy), !cfg.ForceTarget);
        DrawFloat(module, cfg, nameof(cfg.EngagementRange), 5f, 30f, "%.1f");

        DrawSectionTitle("FATE / CE 共用行为");
        DrawBoolean(module, cfg, nameof(cfg.StanceOnBeforeDoFates), !cfg.DoFates);
        DrawBoolean(module, cfg, nameof(cfg.StanceOffBeforeCriticalEncounters), !cfg.DoCriticalEncounters);
        DrawBoolean(module, cfg, nameof(cfg.DelayCriticalEncounters), !cfg.DoCriticalEncounters);
        DrawFloat(module, cfg, nameof(cfg.MinDelay), 0f, 30f, "%.1f 秒", !cfg.DelayCriticalEncounters);
        DrawFloat(module, cfg, nameof(cfg.MaxDelay), 0f, 30f, "%.1f 秒", !cfg.DelayCriticalEncounters);

        DrawSectionTitle("副本轮换");
        DrawInitialInstanceArea(module);
        DrawBoolean(module, cfg, nameof(cfg.AutoRotateInstance));
        DrawFloat(module, cfg, nameof(cfg.InstanceStayMinutes), 15f, 180f, "%.0f 分钟", !cfg.AutoRotateInstance);
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
        ImGui.TextDisabled("这里只显示该岛的事件；通用战斗、寻路和副本轮换选项位于「通用设置」。");
        if (ImGui.BeginTable($"##{id}EventColumns", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
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

        if (ImGui.SmallButton($"全部启用##{islandId}-{title}-all"))
        {
            SetAll(cfg, eventProperties, true);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton($"全部关闭##{islandId}-{title}-none"))
        {
            SetAll(cfg, eventProperties, false);
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
        ImGui.TextUnformatted(module.Config.GetTitle() ?? "刷怪设置");
        ImGui.TextDisabled("南、北岛使用独立目标怪物列表；距离、等级和拉怪策略属于共用参数。");
        ImGui.Separator();

        if (!ImGui.BeginTabBar("##MobFarmerConfigTabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            return;
        }

        if (ImGui.BeginTabItem($"南岛怪物 ({module.Config.SouthHornMobs.Count})"))
        {
            DrawMobSelector(
                "SouthHorn",
                MobData.SouthHornMobs,
                module.Config.SouthHornMobs,
                ref southHornMobSearch);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem($"北岛怪物 ({module.Config.NorthHornMobs.Count})"))
        {
            DrawMobSelector(
                "NorthHorn",
                MobData.NorthHornMobs,
                module.Config.NorthHornMobs,
                ref northHornMobSearch);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("通用参数"))
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
        ImGui.InputTextWithHint($"##{id}MobSearch", "搜索怪物名称……", ref search, 128);

        var searchTerm = search.Trim();
        var visible = available
            .Where(mob => searchTerm.Length == 0
                          || MobData.GetName(mob).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ImGui.SmallButton($"全选当前结果##{id}"))
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
        if (ImGui.SmallButton($"清空##{id}"))
        {
            selected.Clear();
            primaryPlugin.Config.Save();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"已选 {selected.Count} / {available.Count}，当前显示 {visible.Count}");

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
        DrawSectionTitle("基础");
        DrawBoolean(module, cfg, nameof(cfg.Enabled));
        DrawBoolean(module, cfg, nameof(cfg.ConsiderSpecialMobs));
        DrawInt(module, cfg, nameof(cfg.MaxMobLevel), 1, 40);
        DrawFloat(module, cfg, nameof(cfg.MaxEuclideanDistance), 10f, 1000f, "%.0f");

        DrawSectionTitle("循环与返回");
        DrawInt(module, cfg, nameof(cfg.MinimumMobsToStartLoop), 0, 20);
        DrawInt(module, cfg, nameof(cfg.MinimumMobsToStartFight), 1, 20);
        DrawInt(module, cfg, nameof(cfg.ExtraTimeToWait), 0, 20);
        DrawBoolean(module, cfg, nameof(cfg.ReturnToStartInWaitingPhase));
        DrawFloat(module, cfg, nameof(cfg.MinEuclideanDistanceToReturnHome), 10f, 1000f, "%.0f", !cfg.ReturnToStartInWaitingPhase);

        DrawSectionTitle("战斗之铃");
        DrawBoolean(module, cfg, nameof(cfg.ApplyBattleBell));
        DrawFloat(module, cfg, nameof(cfg.MaximumBattleBellWaitTime), 0f, 30f, "%.1f 秒", !cfg.ApplyBattleBell);

        DrawSectionTitle("调试显示");
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
}
