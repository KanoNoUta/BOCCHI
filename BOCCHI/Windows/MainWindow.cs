using BOCCHI.Data;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.AggroRange;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Currency;
using BOCCHI.Modules.Exp;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.ForkedTower;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.StateManager;
using BOCCHI.Modules.Treasure;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using Ocelot;
using Ocelot.Windows;
using System;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Windows;

[OcelotMainWindow]
public class MainWindow(Plugin primaryPlugin, Config config) : OcelotMainWindow(primaryPlugin, config)
{
    private static readonly Vector4 SouthHornAccent = new(0.31f, 0.72f, 0.48f, 1f);
    private static readonly Vector4 NorthHornAccent = new(0.42f, 0.68f, 0.95f, 1f);
    private static readonly Vector4 ReadyAccent = new(0.35f, 0.82f, 0.82f, 1f);
    private static readonly Vector4 WaitingAccent = new(0.48f, 0.53f, 0.61f, 1f);
    private static readonly Vector4 CardBackground = new(0.055f, 0.085f, 0.12f, 0.72f);
    private static readonly Vector4 CardBorder = new(0.24f, 0.34f, 0.45f, 0.72f);
    private DateTimeOffset nextCompactDependencyCheck = DateTimeOffset.MinValue;
    private DailyRoutinesModuleStatus compactDailyRoutinesStatus = DailyRoutinesModuleStatus.Unavailable;

    public override void PostInitialize()
    {
        base.PostInitialize();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 260),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        TitleBarButtons.Add(new TitleBarButton
        {
            Click = m =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    Plugin.Modules.GetModule<AutomatorModule>().DisableIllegalMode();
                }
            },
            Icon = FontAwesomeIcon.Stop,
            IconOffset = new Vector2(2, 2),
            ShowTooltip = () => ImGui.SetTooltip(I18N.T("windows.main.buttons.emergency_stop")),
        });

        TitleBarButtons.Add(new TitleBarButton
        {
            Click = m =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    AutomatorModule.ToggleIllegalMode(Plugin);
                }
            },
            Icon = FontAwesomeIcon.Skull,
            IconOffset = new Vector2(2, 2),
            ShowTooltip = () => ImGui.SetTooltip(I18N.T("windows.main.buttons.toggle_illegal_mode")),
        });
    }

    protected override void Render(RenderContext context)
    {
        DrawCompactModeSwitch();
        if (config.CompactMainWindow)
        {
            DrawCompactMode();
            return;
        }

        DrawOpenSourceBanner();
        DrawStatusHeader();

        if (!ImGui.BeginTabBar("##MainSections", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            return;
        }

        if (ImGui.BeginTabItem("总览"))
        {
            DrawOverview(context);
            ImGui.EndTabItem();
        }

        var southActive = ZoneData.IsInSouthHorn();
        if (ImGui.BeginTabItem($"南岛{(southActive ? "  ●" : string.Empty)}##SouthHorn"))
        {
            DrawIsland(context, ZoneData.SOUTHHORN, "南部新月岛", SouthHornAccent, southActive);
            ImGui.EndTabItem();
        }

        var northActive = ZoneData.IsInNorthHorn();
        if (ImGui.BeginTabItem($"北岛{(northActive ? "  ●" : string.Empty)}##NorthHorn"))
        {
            DrawIsland(context, ZoneData.NORTHHORN, "北部新月岛", NorthHornAccent, northActive);
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawCompactModeSwitch()
    {
        var compact = config.CompactMainWindow;
        if (ImGui.Checkbox("精简模式##CompactMainWindow", ref compact))
        {
            config.CompactMainWindow = compact;
            config.Save();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("启动前仅显示依赖检查与岛屿选择，启动后仅保留运行概览。");
        }

        ImGui.Separator();
    }

    private void DrawCompactMode()
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        if (automator.Config.Enabled)
        {
            DrawCompactRuntime(automator);
            return;
        }

        var readiness = GetCompactDependencyReadiness(automator);
        DrawCompactIslandSelector(automator);
        ImGui.Spacing();
        DrawCompactLaunch(automator, readiness);
    }

    private void DrawCompactRuntime(AutomatorModule automator)
    {
        ImGui.TextColored(ReadyAccent, "自动模式运行中");

        const float emergencySize = 28f;
        var remainingWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(MathF.Max(
            ImGui.GetCursorPosX() + 12f,
            ImGui.GetCursorPosX() + remainingWidth - emergencySize));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.62f, 0.15f, 0.13f, 0.92f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushFont(UiBuilder.IconFont);
        var emergencyClicked = ImGui.Button(
            $"{FontAwesomeIcon.Stop.ToIconString()}##CompactEmergencyStop",
            new Vector2(emergencySize, 0));
        ImGui.PopFont();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("紧急停止");
        }

        if (emergencyClicked)
        {
            automator.DisableIllegalMode();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("运行概览");
        ImGui.Spacing();
        DrawRuntimeSummary();
    }

    private void DrawCompactIslandSelector(AutomatorModule automator)
    {
        ImGui.TextDisabled("启动岛屿");
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var buttonWidth = MathF.Max(120f, (ImGui.GetContentRegionAvail().X - spacing) / 2f);
        DrawIslandChoiceButton(
            automator,
            InstanceEntryArea.SouthHorn,
            "南部新月岛",
            SouthHornAccent,
            buttonWidth);
        ImGui.SameLine();
        DrawIslandChoiceButton(
            automator,
            InstanceEntryArea.NorthHorn,
            "北部新月岛",
            NorthHornAccent,
            buttonWidth);

        if (ZoneData.IsInOccultCrescent())
        {
            ImGui.TextDisabled("当前已在岛内，本次启动从当前区域开始；该选择用于下次从岛外启动。");
        }
    }

    private void DrawIslandChoiceButton(
        AutomatorModule automator,
        InstanceEntryArea area,
        string label,
        Vector4 accent,
        float width)
    {
        var selected = automator.Config.InitialInstanceArea == area;
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, accent with { W = 0.78f });
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accent with { W = 0.92f });
        }

        if (ImGui.Button($"{(selected ? "● " : string.Empty)}{label}##Compact-{area}", new Vector2(width, 0))
            && !selected)
        {
            automator.Config.InitialInstanceArea = area;
            config.Save();
        }

        if (selected)
        {
            ImGui.PopStyleColor(2);
        }
    }

    private void DrawCompactLaunch(AutomatorModule automator, CompactDependencyReadiness readiness)
    {
        var icon = readiness.IsReady ? FontAwesomeIcon.Play : FontAwesomeIcon.Lock;
        const float launchButtonSize = 76f;
        ImGui.SetCursorPosX(MathF.Max(
            ImGui.GetCursorPosX(),
            ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - launchButtonSize) / 2f));

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, launchButtonSize / 2f);
        ImGui.PushStyleColor(
            ImGuiCol.Button,
            readiness.IsReady
                ? new Vector4(0.15f, 0.56f, 0.58f, 0.92f)
                : new Vector4(0.20f, 0.24f, 0.30f, 0.92f));
        ImGui.PushStyleColor(
            ImGuiCol.ButtonHovered,
            readiness.IsReady
                ? new Vector4(0.20f, 0.69f, 0.71f, 1f)
                : new Vector4(0.20f, 0.24f, 0.30f, 0.92f));
        ImGui.BeginDisabled(!readiness.IsReady);
        ImGui.PushFont(UiBuilder.IconFont);
        var startClicked = ImGui.Button(
            $"{icon.ToIconString()}##CompactAutomationStart",
            new Vector2(launchButtonSize, launchButtonSize));
        ImGui.PopFont();
        ImGui.EndDisabled();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();

        if (startClicked)
        {
            AutomatorModule.ToggleIllegalMode(Plugin);
        }

        ImGui.Spacing();
        DrawCenteredText(
            readiness.IsReady ? "准备完成，可以启动" : "正在准备自动化插件",
            readiness.IsReady ? ReadyAccent : WaitingAccent);
        DrawCenteredText(
            automator.Config.InitialInstanceArea == InstanceEntryArea.NorthHorn
                ? "目标：北部新月岛"
                : "目标：南部新月岛");

        ImGui.Spacing();
        ImGui.Separator();
        DrawDependencyStatus("Lifestream", readiness.LifestreamReady ? "已加载" : "未加载", readiness.LifestreamReady);
        DrawDependencyStatus("vnavmesh", readiness.VnavmeshLabel, readiness.VnavmeshReady);
        DrawDependencyStatus(
            "DailyRoutines",
            readiness.DailyRoutinesStatus switch
            {
                DailyRoutinesModuleStatus.Ready => "入岛模块已准备",
                DailyRoutinesModuleStatus.Enabling => "正在启用入岛模块",
                _ => "未加载或 IPC 不可用",
            },
            readiness.DailyRoutinesStatus == DailyRoutinesModuleStatus.Ready,
            readiness.DailyRoutinesStatus == DailyRoutinesModuleStatus.Enabling);
    }

    private CompactDependencyReadiness GetCompactDependencyReadiness(AutomatorModule automator)
    {
        var now = DateTimeOffset.UtcNow;
        if (now >= nextCompactDependencyCheck)
        {
            nextCompactDependencyCheck = now + TimeSpan.FromMilliseconds(500);
            compactDailyRoutinesStatus = automator.instanceRotation.EnsureDailyRoutinesCommandModules(automator);
        }

        var vnavmesh = automator.GetVnavmeshAvailability();
        var lifestreamReady = IsPluginLoaded("Lifestream");
        var vnavmeshLabel = vnavmesh.IsAvailable
            ? $"{vnavmesh.DisplayVersion?.ToString() ?? "版本未知"}（可用）"
            : vnavmesh.Status == Pathfinding.VnavmeshAvailabilityStatus.Missing
                ? "未安装"
                : "未加载";
        return new CompactDependencyReadiness(
            lifestreamReady,
            vnavmesh.IsAvailable,
            vnavmeshLabel,
            compactDailyRoutinesStatus);
    }

    private static bool IsPluginLoaded(string internalName)
    {
        return Svc.PluginInterface.InstalledPlugins.Any(plugin =>
            string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase)
            && plugin.IsLoaded);
    }

    private static void DrawDependencyStatus(
        string name,
        string status,
        bool ready,
        bool pending = false)
    {
        var color = ready
            ? new Vector4(0.33f, 0.82f, 0.48f, 1f)
            : pending
                ? ImGuiColors.DalamudYellow
                : new Vector4(0.88f, 0.39f, 0.34f, 1f);
        ImGui.TextColored(color, "●");
        ImGui.SameLine();
        ImGui.TextUnformatted(name);
        ImGui.SameLine(150f);
        ImGui.TextDisabled(status);
    }

    private static void DrawCenteredText(string text, Vector4? color = null)
    {
        var width = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(MathF.Max(
            ImGui.GetCursorPosX(),
            ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - width) / 2f));
        if (color.HasValue)
        {
            ImGui.TextColored(color.Value, text);
        }
        else
        {
            ImGui.TextUnformatted(text);
        }
    }

    private readonly record struct CompactDependencyReadiness(
        bool LifestreamReady,
        bool VnavmeshReady,
        string VnavmeshLabel,
        DailyRoutinesModuleStatus DailyRoutinesStatus)
    {
        public bool IsReady => LifestreamReady
                               && VnavmeshReady
                               && DailyRoutinesStatus == DailyRoutinesModuleStatus.Ready;
    }

    private void DrawOpenSourceBanner()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.38f, 0.25f, 0.02f, 0.42f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.92f, 0.65f, 0.08f, 0.72f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 7f));
        if (ImGui.BeginChild("##OpenSourceNotice", new Vector2(0, 38), true, ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ImGuiColors.DalamudYellow, FontAwesomeIcon.ExclamationTriangle.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextUnformatted("插件开源免费，汉化维护不易，请勿从任何闲鱼小店购买本插件。");
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DrawStatusHeader()
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        var farmer = Plugin.Modules.GetModule<MobFarmerModule>();
        var zoneName = ZoneData.IsInSouthHorn()
            ? "南部新月岛"
            : ZoneData.IsInNorthHorn()
                ? "北部新月岛"
                : "新月岛外";
        var accent = ZoneData.IsInNorthHorn() ? NorthHornAccent : SouthHornAccent;

        ImGui.Spacing();
        ImGui.TextColored(accent, zoneName);
        ImGui.SameLine();
        ImGui.TextDisabled($"  区域 {Svc.ClientState.TerritoryType}");

        var available = ImGui.GetContentRegionAvail().X;
        var emergencyWidth = 72f;
        var toggleWidth = automator.Config.Enabled ? 116f : 128f;
        ImGui.SameLine(MathF.Max(ImGui.GetCursorPosX() + 18f, available - emergencyWidth - toggleWidth));

        ImGui.PushStyleColor(
            ImGuiCol.Button,
            automator.Config.Enabled
                ? new Vector4(0.60f, 0.22f, 0.16f, 0.90f)
                : new Vector4(0.16f, 0.48f, 0.30f, 0.90f));
        if (ImGui.Button(automator.Config.Enabled ? "暂停自动模式" : "启动自动模式", new Vector2(toggleWidth, 0)))
        {
            AutomatorModule.ToggleIllegalMode(Plugin);
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.62f, 0.15f, 0.13f, 0.92f));
        if (ImGui.Button("紧急停止", new Vector2(emergencyWidth, 0)))
        {
            automator.DisableIllegalMode();
        }
        ImGui.PopStyleColor();

        ImGui.TextDisabled(
            $"自动模式：{(automator.Config.Enabled ? "运行中" : "已停止")}   " +
            $"刷怪：{(farmer.Farmer.Running ? farmer.Farmer.StateMachine.State.ToString() : "未运行")}");
        ImGui.Separator();
    }

    private void DrawOverview(RenderContext context)
    {
        if (ImGui.BeginTable("##OverviewCards", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            DrawCard("OverviewStatus", "运行概览", () => DrawRuntimeSummary(), 182f);

            ImGui.TableNextColumn();
            DrawCard("OverviewIncome", "效率统计与 Buff", () =>
            {
                Plugin.Modules.GetModule<CurrencyModule>().RenderMainUi(context);
                ImGui.Spacing();
                Plugin.Modules.GetModule<ExpModule>().RenderMainUi(context);
                ImGui.Spacing();
                Plugin.Modules.GetModule<BuffModule>().RenderMainUi(context);
            }, 182f);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        DrawCard("OverviewEvents", "当前区域事件", () =>
        {
            if (!ZoneData.IsInOccultCrescent())
            {
                ImGui.TextDisabled("进入南部或北部新月岛后显示实时 FATE 与紧急遭遇战。");
                return;
            }

            if (ImGui.BeginTable("##OverviewEventColumns", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableNextColumn();
                Plugin.Modules.GetModule<FatesModule>().RenderMainUi(context);
                ImGui.TableNextColumn();
                Plugin.Modules.GetModule<CriticalEncountersModule>().RenderMainUi(context);
                ImGui.EndTable();
            }
        }, 260f);
    }

    private void DrawRuntimeSummary()
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        var stateManager = Plugin.Modules.GetModule<StateManagerModule>();
        var activityName = "无";
        var activityState = "等待运行";
        try
        {
            activityName = automator.automator.Activity?.GetName() ?? "无";
            activityState = automator.automator.Activity?.state.ToString() ?? "等待运行";
        }
        catch (AccessViolationException)
        {
            activityName = "目标数据刷新中";
        }

        DrawKeyValue("游戏状态", stateManager.GetStateText());
        DrawKeyValue("自动模式", automator.Config.Enabled ? "运行中" : "已停止");
        DrawKeyValue("当前目标", activityName);
        DrawKeyValue("目标阶段", activityState);

        var rotation = automator.instanceRotation;
        var rotationEnabled = automator.Config.ShouldAutoRotateInstance
                              || rotation.State != InstanceRotationState.Idle;
        var rotationState = rotationEnabled ? rotation.GetStateLabel(automator) : "未启用";
        var rotationRemaining = rotation.GetRemainingLabel(automator);
        var rotationPopulation = rotation.CurrentPopulation?.ToString() ?? "--";
        DrawKeyValue("副本轮换状态", $"{rotationState} · 剩余 {rotationRemaining} · 人数 {rotationPopulation}");

        var vnavmesh = automator.GetVnavmeshAvailability();
        DrawKeyValue(
            "vnavmesh",
            vnavmesh.IsAvailable
                ? $"{vnavmesh.DisplayVersion?.ToString() ?? "版本未知"}（可用）"
                : vnavmesh.Status == Pathfinding.VnavmeshAvailabilityStatus.Missing
                    ? "未安装"
                    : "未加载");
    }

    private void DrawIsland(
        RenderContext context,
        uint territoryId,
        string title,
        Vector4 accent,
        bool isActive)
    {
        ImGui.TextColored(accent, title);
        ImGui.SameLine();
        ImGui.TextDisabled(isActive ? "当前所在区域 · 实时数据" : "非当前区域 · 显示独立配置摘要");
        DrawIslandSummary(territoryId);
        ImGui.Spacing();

        if (!isActive)
        {
            DrawCard($"Inactive-{territoryId}", "实时模块", () =>
            {
                ImGui.TextDisabled($"进入{title}后，这里会显示该岛的宝箱、胡萝卜、FATE、CE、塔和刷怪运行状态。");
                ImGui.TextWrapped("配置和目标怪物仍可在下方查看；南北岛列表互不混排。");
            }, 92f);
            ImGui.Spacing();
            DrawConfiguredMobs(territoryId, 260f);
            return;
        }

        if (ImGui.BeginTable($"##IslandPrimary-{territoryId}", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            DrawCard($"Explore-{territoryId}", "探索与采集", () =>
            {
                Plugin.Modules.GetModule<TreasureModule>().RenderMainUi(context);
                ImGui.Spacing();
                Plugin.Modules.GetModule<CarrotsModule>().RenderMainUi(context);
            }, 260f);

            ImGui.TableNextColumn();
            DrawCard($"Events-{territoryId}", "FATE 与紧急遭遇战", () =>
            {
                Plugin.Modules.GetModule<FatesModule>().RenderMainUi(context);
                ImGui.Spacing();
                Plugin.Modules.GetModule<CriticalEncountersModule>().RenderMainUi(context);
            }, 260f);
            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ImGui.BeginTable($"##IslandSecondary-{territoryId}", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            DrawCard($"Farmer-{territoryId}", "本岛刷怪", () =>
            {
                Plugin.Modules.GetModule<MobFarmerModule>().RenderMainUi(context);
                ImGui.Spacing();
                DrawConfiguredMobNames(territoryId);
            }, 230f);

            ImGui.TableNextColumn();
            DrawCard($"Tower-{territoryId}", "分歧之塔", () =>
            {
                Plugin.Modules.GetModule<ForkedTowerModule>().RenderMainUi(context);
            }, 230f);
            ImGui.EndTable();
        }

        if (territoryId == ZoneData.NORTHHORN)
        {
            ImGui.Spacing();
            DrawCard($"AggroRange-{territoryId}", "普通怪仇恨范围", () =>
            {
                Plugin.Modules.GetModule<AggroRangeModule>().RenderMainUi(context);
            }, 125f);
        }
    }

    private void DrawIslandSummary(uint territoryId)
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>().Config;
        var farmer = Plugin.Modules.GetModule<MobFarmerModule>().Config;
        var fateIds = EventData.GetFatesForTerritory(territoryId).Select(data => data.Id).Distinct().ToArray();
        var ceIds = EventData.GetCriticalEncountersForTerritory(territoryId).Select(data => data.Id).Distinct().ToArray();
        var enabledFates = fateIds.Count(id =>
            automator.FatesMap.TryGetValue(id, out var enabled) && enabled);
        var enabledCes = ceIds.Count(id =>
            automator.CriticalEncountersMap.TryGetValue(id, out var enabled) && enabled);

        if (ImGui.BeginTable($"##IslandSummary-{territoryId}", 4, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextColumn();
            DrawMetric("FATE", automator.DoFates ? $"{enabledFates}/{fateIds.Length}" : "总开关关闭");
            ImGui.TableNextColumn();
            DrawMetric("CE", automator.DoCriticalEncounters ? $"{enabledCes}/{ceIds.Length}" : "总开关关闭");
            ImGui.TableNextColumn();
            DrawMetric("目标怪物", farmer.GetMobsForTerritory(territoryId).Count.ToString());
            ImGui.TableNextColumn();
            DrawMetric("区域状态", Svc.ClientState.TerritoryType == territoryId ? "当前区域" : "未进入");
            ImGui.EndTable();
        }
    }

    private void DrawConfiguredMobs(uint territoryId, float height)
    {
        DrawCard($"ConfiguredMobs-{territoryId}", "目标怪物", () => DrawConfiguredMobNames(territoryId), height);
    }

    private void DrawConfiguredMobNames(uint territoryId)
    {
        var selected = Plugin.Modules.GetModule<MobFarmerModule>().Config.GetMobsForTerritory(territoryId);
        if (selected.Count == 0)
        {
            ImGui.TextDisabled("尚未选择本岛目标怪物。请在设置 → 刷怪设置中配置。");
            return;
        }

        ImGui.TextDisabled($"已选择 {selected.Count} 只：");
        if (ImGui.BeginTable($"##ConfiguredMobNames-{territoryId}", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg))
        {
            foreach (var mob in selected)
            {
                ImGui.TableNextColumn();
                ImGui.BulletText(MobData.GetName(mob));
            }
            ImGui.EndTable();
        }
    }

    private static void DrawCard(string id, string title, Action content, float height)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBackground);
        ImGui.PushStyleColor(ImGuiCol.Border, CardBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12f, 10f));
        if (ImGui.BeginChild($"##Card-{id}", new Vector2(0, height), true, ImGuiWindowFlags.AlwaysUseWindowPadding))
        {
            ImGui.TextUnformatted(title);
            ImGui.Separator();
            content();
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawMetric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.TextUnformatted(value);
    }

    private static void DrawKeyValue(string label, string value)
    {
        ImGui.TextDisabled($"{label}：");
        ImGui.SameLine(118f);
        ImGui.TextUnformatted(value);
    }
}
