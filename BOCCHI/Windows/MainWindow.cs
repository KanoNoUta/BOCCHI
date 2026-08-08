using BOCCHI.Data;
using BOCCHI.Modules.AggroRange;
using BOCCHI.Modules.Automator;
using BOCCHI.Modules.Buff;
using BOCCHI.Modules.Carrots;
using BOCCHI.Modules.CeCrowdsource;
using BOCCHI.Modules.CriticalEncounters;
using BOCCHI.Modules.Currency;
using BOCCHI.Modules.Exp;
using BOCCHI.Modules.Fates;
using BOCCHI.Modules.ForkedTower;
using BOCCHI.Modules.MobFarmer;
using BOCCHI.Modules.StateManager;
using BOCCHI.Modules.Treasure;
using BOCCHI.Ui;
using BOCCHI.Ui.Lumin;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using Ocelot;
using Ocelot.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace BOCCHI.Windows;

[OcelotMainWindow]
public class MainWindow(Plugin primaryPlugin, Config config) : OcelotMainWindow(primaryPlugin, config)
{
    private MainWindowPage selectedPage = MainWindowPage.Overview;
    private Vector2? fullWindowSize;
    private Vector2? compactWindowSize;
    private bool initialLayoutApplied;
    private Vector4 sidebarSelectedRect;
    private Vector4 sidebarOverlay;
    private Vector4 sidebarIndicator;
    private MainWindowPage? lastDrawnPage;
    private float pageAlpha = 1f;
    private LuminUiStyleScope? luminStyleScope;
    private ISharedImmediateTexture? pluginIconTexture;
    private TitleBarButton? compactTitleBarButton;

    /// <summary>
    /// The plugin's own icon.png, shipped next to the assembly, used as the
    /// sidebar brand mark. Returns null while the texture is still loading or if
    /// the file is missing, in which case the vector mark is drawn instead.
    /// </summary>
    private IDalamudTextureWrap? GetPluginIcon()
    {
        if (pluginIconTexture == null)
        {
            var iconPath = Path.Join(
                Svc.PluginInterface.AssemblyLocation.DirectoryName,
                "icon.png");
            if (!File.Exists(iconPath))
            {
                return null;
            }

            pluginIconTexture = Svc.Texture.GetFromFile(iconPath);
        }

        return pluginIconTexture.TryGetWrap(out var wrap, out _) ? wrap : null;
    }

    public override void PostInitialize()
    {
        base.PostInitialize();

        // Compact mode is a single capsule row, so the window has to be allowed
        // to shrink far below the full layout's width; the old 520px floor is
        // what left a black strip to the right of the capsule.
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 60),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        Size = new Vector2(860, 680);
        SizeCondition = ImGuiCond.FirstUseEver;
        // The native ImGui collapse arrow has its own hidden state and cannot
        // stay synchronized with our compact command bar. One shared compact
        // state now drives both title-bar and in-content controls.
        Flags |= ImGuiWindowFlags.NoCollapse;

        compactTitleBarButton = new TitleBarButton
        {
            Click = m =>
            {
                if (m == ImGuiMouseButton.Left)
                {
                    SetCompactMode(!config.CompactMainWindow);
                }
            },
            Icon = FontAwesomeIcon.Compress,
            IconOffset = new Vector2(1, 1),
            ShowTooltip = () => ImGui.SetTooltip(T(
                config.CompactMainWindow ? "buttons.leave_compact" : "buttons.enter_compact")),
        };
        TitleBarButtons.Add(compactTitleBarButton);

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
            IconOffset = new Vector2(1, 1),
            ShowTooltip = () => ImGui.SetTooltip(T("buttons.emergency_stop")),
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
            IconOffset = new Vector2(1, 1),
            ShowTooltip = () => ImGui.SetTooltip(T("buttons.toggle_illegal_mode")),
        });
    }

    public override void PreDraw()
    {
        if (compactTitleBarButton != null)
        {
            compactTitleBarButton.Icon = config.CompactMainWindow
                ? FontAwesomeIcon.Expand
                : FontAwesomeIcon.Compress;
        }

        // Style is scoped to this window's full frame (frame + content) and is
        // released in PostDraw, so it never leaks into other plugins' windows.
        luminStyleScope?.Dispose();
        luminStyleScope = LuminTheme.PushGlobalStyle();
        base.PreDraw();
    }

    public override void PostDraw()
    {
        base.PostDraw();
        luminStyleScope?.Dispose();
        luminStyleScope = null;
    }

    protected override void Render(RenderContext context)
    {
        if (!initialLayoutApplied)
        {
            initialLayoutApplied = true;
            if (config.CompactMainWindow)
            {
                ImGui.SetWindowSize(new Vector2(
                    CompactCapsuleWidth() + ImGui.GetStyle().WindowPadding.X * 2f,
                    ImGui.GetFrameHeight() + LuminTheme.S(24f)));
            }
        }

        // The full layout needs ~860px to breathe; clamp only when the capsule
        // row is what will be drawn, so entering compact mode never leaves
        // empty window to the right of the gear.
        if (config.CompactMainWindow)
        {
            var width = MathF.Max(
                CompactCapsuleWidth() + ImGui.GetStyle().WindowPadding.X * 2f,
                (SizeConstraints?.MinimumSize.X ?? 0f));
            ImGui.SetWindowSize(new Vector2(width, ImGui.GetWindowSize().Y));
        }

        if (config.CompactMainWindow)
        {
            DrawCompactCommandBar();
            return;
        }

        DrawCommandBar();
        ImGui.Spacing();
        DrawWorkspace(context);
    }

    private void DrawCommandBar()
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        var snapshot = GetOperationSnapshot(automator);
        if (ImGui.GetContentRegionAvail().X < LuminTheme.S(700f))
        {
            DrawNarrowCommandBar(automator, snapshot);
            return;
        }

        var toolsWidth = FullToolColumnWidth();
        var startWidth = LuminTheme.S(126f);
        var stopWidth = LuminTheme.S(108f);

        if (ImGui.BeginTable(
                "##MainCommandBar",
                4,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Area", ImGuiTableColumnFlags.WidthFixed, LuminTheme.S(150f));
            ImGui.TableSetupColumn("Operation", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(
                "Commands",
                ImGuiTableColumnFlags.WidthFixed,
                startWidth + stopWidth + ImGui.GetStyle().ItemSpacing.X);
            ImGui.TableSetupColumn("Tools", ImGuiTableColumnFlags.WidthFixed, toolsWidth);

            ImGui.TableNextColumn();
            DrawAreaStatus();

            ImGui.TableNextColumn();
            DrawOperationStatus(snapshot, compact: false);

            ImGui.TableNextColumn();
            DrawAutomatorButton(automator, "Full", startWidth);
            ImGui.SameLine();
            DrawStopAllButton(automator, snapshot, stopWidth);

            ImGui.TableNextColumn();
            if (BocchiUi.IconButton(FontAwesomeIcon.Cog, "OpenSettings", T("buttons.open_settings")))
            {
                Plugin.Windows.ToggleConfigUI();
            }
            ImGui.SameLine();
            DrawModePill(snapshot, "EnterCompact", compact: false);

            ImGui.EndTable();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Detail))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, BocchiUi.GetStatusColor(snapshot.State));
            ImGui.TextWrapped(snapshot.Detail);
            ImGui.PopStyleColor();
        }
        ImGui.Separator();
    }

    /// <summary>Settings icon button plus the mode pill, including the gap between them.</summary>
    private static float FullToolColumnWidth() =>
        ImGui.GetFrameHeight()
        + ImGui.GetStyle().ItemSpacing.X
        + LuminWidgets.PillToggleWidth(LongestStateLabel(), T("buttons.collapse"));

    private void DrawNarrowCommandBar(
        AutomatorModule automator,
        BocchiOperationSnapshot snapshot)
    {
        if (ImGui.BeginTable(
                "##MainCommandBarNarrow",
                3,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("Area", ImGuiTableColumnFlags.WidthFixed, LuminTheme.S(138f));
            ImGui.TableSetupColumn("Operation", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Tools", ImGuiTableColumnFlags.WidthFixed, FullToolColumnWidth());
            ImGui.TableNextColumn();
            DrawAreaStatus();
            ImGui.TableNextColumn();
            DrawOperationStatus(snapshot, compact: true);
            ImGui.TableNextColumn();
            if (BocchiUi.IconButton(FontAwesomeIcon.Cog, "OpenSettingsNarrow", T("buttons.open_settings")))
            {
                Plugin.Windows.ToggleConfigUI();
            }
            ImGui.SameLine();
            DrawModePill(snapshot, "EnterCompactNarrow", compact: false);
            ImGui.EndTable();
        }

        DrawAutomatorButton(automator, "FullNarrow", LuminTheme.S(140f));
        ImGui.SameLine();
        DrawStopAllButton(automator, snapshot, LuminTheme.S(108f));
        if (!string.IsNullOrWhiteSpace(snapshot.Detail))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, BocchiUi.GetStatusColor(snapshot.State));
            ImGui.TextWrapped(snapshot.Detail);
            ImGui.PopStyleColor();
        }
        ImGui.Separator();
    }

    private void DrawCompactCommandBar()
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        var snapshot = GetOperationSnapshot(automator);

        // Minimal capsule: state pill on the left, gear + stop icon on the right.
        // Nothing else, so the collapsed window is just this row.
        DrawModePill(snapshot, "LeaveCompact", compact: true);
        ImGui.SameLine();
        if (BocchiUi.IconButton(FontAwesomeIcon.Cog, "CompactSettings", T("buttons.open_settings")))
        {
            Plugin.Windows.ToggleConfigUI();
        }
        ImGui.SameLine();
        if (BocchiUi.IconButton(
                FontAwesomeIcon.Stop,
                "CompactStopAll",
                T("buttons.stop_all_tooltip"),
                snapshot.CanStopAll))
        {
            automator.RequestStopAll();
        }
    }

    /// <summary>
    /// Mode pill: a status half naming the current operation state and an action
    /// half that switches between the compact and full layouts.
    /// </summary>
    private void DrawModePill(BocchiOperationSnapshot snapshot, string id, bool compact)
    {
        if (LuminWidgets.PillToggle(
                $"##ModePill-{id}",
                GetStateLabel(snapshot.State),
                BocchiUi.GetStatusColor(snapshot.State),
                T(compact ? "buttons.expand" : "buttons.collapse"),
                T(compact ? "buttons.leave_compact" : "buttons.enter_compact")))
        {
            SetCompactMode(!compact);
        }
    }

    /// <summary>
    /// The pill lives in a fixed-width table column, so it has to be sized for
    /// the widest state label; otherwise the label clips whenever the operation
    /// state changes to a longer one.
    /// </summary>
    private static string LongestStateLabel()
    {
        var longest = string.Empty;
        var width = 0f;
        foreach (var state in Enum.GetValues<BocchiOperationState>())
        {
            var label = GetStateLabel(state);
            var labelWidth = ImGui.CalcTextSize(label).X;
            if (labelWidth > width)
            {
                width = labelWidth;
                longest = label;
            }
        }

        return longest;
    }

    private void DrawWorkspace(RenderContext context)
    {
        var inOccultCrescent = ZoneData.IsInOccultCrescent();
        var inForkedTower = ZoneData.IsInForkedTower();
        var inNorthHorn = ZoneData.IsInNorthHorn();
        var pages = BocchiUiPolicy.GetVisiblePages(inOccultCrescent, inForkedTower, inNorthHorn);
        if (!pages.Contains(selectedPage))
        {
            selectedPage = pages[0];
        }

        var availableWidth = LuminTheme.ToDesign(ImGui.GetContentRegionAvail().X);
        if (BocchiUiPolicy.UseSidebar(availableWidth))
        {
            DrawSidebar(pages);
            ImGui.SameLine();
            if (ImGui.BeginChild("##MainWorkspace", Vector2.Zero, false))
            {
                DrawSelectedPage(context);
            }
            ImGui.EndChild();
            return;
        }

        DrawHorizontalNavigation(pages);
        ImGui.Separator();
        if (ImGui.BeginChild("##MainWorkspaceNarrow", Vector2.Zero, false))
        {
            DrawSelectedPage(context);
        }
        ImGui.EndChild();
    }

    private void DrawSidebar(IReadOnlyList<MainWindowPage> pages)
    {
        var sidebarWidth = LuminTheme.S(LuminTheme.SidebarWidth);
        if (ImGui.BeginChild("##MainNavigation", new Vector2(sidebarWidth, 0f), false))
        {
            LuminWidgets.BrandHeader("BOCCHI", T("nav.workspace"), GetPluginIcon());
            ImGui.Spacing();

            var drawList = ImGui.GetWindowDrawList();
            var selectedIndex = 0;
            for (var i = 0; i < pages.Count; i++)
            {
                if (pages[i] == selectedPage)
                {
                    selectedIndex = i;
                    break;
                }
            }

            LuminDraw.EaseRect(ref sidebarOverlay, sidebarSelectedRect, 18f);
            if (sidebarOverlay.Z > sidebarOverlay.X + 1f)
            {
                LuminDraw.RectFilled(
                    drawList,
                    new Vector2(sidebarOverlay.X, sidebarOverlay.Y),
                    new Vector2(sidebarOverlay.Z, sidebarOverlay.W),
                    LuminTheme.Col(LuminTheme.Widget),
                    LuminTheme.S(LuminTheme.SidebarTabRounding));
            }

            var index = 0;
            foreach (var page in pages)
            {
                var (label, icon) = GetPagePresentation(page);
                if (LuminWidgets.TabButton(label, index, ref selectedIndex, ref sidebarSelectedRect, icon))
                {
                    selectedPage = page;
                }

                index++;
            }

            LuminDraw.EaseRect(ref sidebarIndicator, sidebarSelectedRect, 18f);
            if (sidebarIndicator.W > sidebarIndicator.Y + 1f)
            {
                var barWidth = LuminTheme.S(2);
                var barInsetY = LuminTheme.S(6);
                var barX = sidebarIndicator.X + LuminTheme.S(2);
                LuminDraw.RectFilled(
                    drawList,
                    new Vector2(barX, sidebarIndicator.Y + barInsetY),
                    new Vector2(barX + barWidth, sidebarIndicator.W - barInsetY),
                    LuminTheme.Col(LuminTheme.Accent),
                    barWidth * 0.5f);
            }
        }
        ImGui.EndChild();
    }

    private void DrawHorizontalNavigation(IReadOnlyList<MainWindowPage> pages)
    {
        var height = ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y;
        if (ImGui.BeginChild(
                "##MainNavigationNarrow",
                new Vector2(0f, height),
                false,
                ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var page in pages)
            {
                var (label, icon) = GetPagePresentation(page);
                // Icon slot (one frame height) + label + trailing padding.
                var width = ImGui.GetFrameHeight() + ImGui.CalcTextSize(label).X + LuminTheme.S(12);
                if (BocchiUi.NavigationItem(page, label, icon, page == selectedPage, width))
                {
                    selectedPage = page;
                }
                ImGui.SameLine();
            }
        }
        ImGui.EndChild();
    }

    private void DrawSelectedPage(RenderContext context)
    {
        if (selectedPage != lastDrawnPage)
        {
            lastDrawnPage = selectedPage;
            pageAlpha = 0f;
        }

        LuminDraw.StaticEase(ref pageAlpha, 1f, 7f);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, pageAlpha);
        try
        {
            switch (selectedPage)
        {
            case MainWindowPage.Events:
                DrawEventsPage(context);
                break;
            case MainWindowPage.Explore:
                DrawExplorePage(context);
                break;
            case MainWindowPage.Farming:
                DrawFarmingPage(context);
                break;
            case MainWindowPage.Statistics:
                DrawStatisticsPage(context);
                break;
            case MainWindowPage.Tower:
                DrawTowerPage(context);
                break;
            case MainWindowPage.AggroRange:
                DrawAggroRangePage(context);
                break;

            default:
                DrawOverviewPage();
                break;
        }
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    private void DrawOverviewPage()
    {
        BocchiUi.PageHeading(T("pages.overview.title"), T("pages.overview.subtitle"));

        LuminWidgets.BeginSection(T("pages.overview.title"));
        DrawRuntimeSummary();
        LuminWidgets.EndSection();
        ImGui.Spacing();

        uint? territory = ZoneData.IsInSouthHorn()
            ? ZoneData.SOUTHHORN
            : ZoneData.IsInNorthHorn()
                ? ZoneData.NORTHHORN
                : null;
        if (territory is { } activeTerritory)
        {
            LuminWidgets.BeginSection(T("pages.overview.current_area"));
            DrawIslandSummary(activeTerritory);
            LuminWidgets.EndSection();
        }
        else
        {
            BocchiUi.EmptyState(
                T("pages.overview.outside_title"),
                T("pages.overview.outside_detail"));
            ImGui.Spacing();
            DrawConfiguredMobsSummary();
        }

        ImGui.Spacing();
        ImGui.TextDisabled(T("pages.overview.open_source"));
    }

    private void DrawEventsPage(RenderContext context)
    {
        BocchiUi.PageHeading(T("pages.events.title"), T("pages.events.subtitle"));
        if (ZoneData.IsInOccultCrescent())
        {
            var columns = BocchiUiPolicy.GetWorkspaceColumns(LuminTheme.ToDesign(ImGui.GetContentRegionAvail().X));
            if (ImGui.BeginTable(
                    "##EventWorkspace",
                    columns,
                    ImGuiTableFlags.SizingStretchSame | (columns > 1 ? ImGuiTableFlags.BordersInnerV : ImGuiTableFlags.None)))
            {
                ImGui.TableNextColumn();
                Plugin.Modules.GetModule<FatesModule>().RenderMainUi(context);
                ImGui.TableNextColumn();
                Plugin.Modules.GetModule<CriticalEncountersModule>().RenderMainUi(context);
                ImGui.EndTable();
            }
        }
        else
        {
            BocchiUi.EmptyState(T("pages.events.empty_title"), T("pages.events.empty_detail"));
        }

        ImGui.Spacing();
        Plugin.Modules.GetModule<CeCrowdsourceModule>().RenderMainUi(context);
    }

    private void DrawExplorePage(RenderContext context)
    {
        BocchiUi.PageHeading(T("pages.explore.title"), T("pages.explore.subtitle"));
        if (!ZoneData.IsInOccultCrescent())
        {
            BocchiUi.EmptyState(T("pages.explore.empty_title"), T("pages.explore.empty_detail"));
            return;
        }

        Plugin.Modules.GetModule<TreasureModule>().RenderMainUi(context);
        ImGui.Spacing();
        Plugin.Modules.GetModule<CarrotsModule>().RenderMainUi(context);
    }

    private void DrawFarmingPage(RenderContext context)
    {
        BocchiUi.PageHeading(T("pages.farming.title"), T("pages.farming.subtitle"));
        if (!ZoneData.IsInOccultCrescent())
        {
            BocchiUi.EmptyState(T("pages.farming.empty_title"), T("pages.farming.empty_detail"));
            DrawConfiguredMobsSummary();
            return;
        }

        Plugin.Modules.GetModule<MobFarmerModule>().RenderMainUi(context);
        ImGui.Spacing();
        DrawConfiguredMobNames(Svc.ClientState.TerritoryType);
    }

    private void DrawStatisticsPage(RenderContext context)
    {
        BocchiUi.PageHeading(T("pages.statistics.title"), T("pages.statistics.subtitle"));
        var columns = BocchiUiPolicy.GetWorkspaceColumns(LuminTheme.ToDesign(ImGui.GetContentRegionAvail().X));
        if (!ImGui.BeginTable("##StatisticsWorkspace", columns, ImGuiTableFlags.SizingStretchSame))
        {
            return;
        }

        ImGui.TableNextColumn();
        Plugin.Modules.GetModule<CurrencyModule>().RenderMainUi(context);
        ImGui.Spacing();
        Plugin.Modules.GetModule<ExpModule>().RenderMainUi(context);
        ImGui.TableNextColumn();
        Plugin.Modules.GetModule<BuffModule>().RenderMainUi(context);
        ImGui.EndTable();
    }

    private void DrawTowerPage(RenderContext context)
    {
        BocchiUi.PageHeading(T("pages.tower.title"), T("pages.tower.subtitle"));
        Plugin.Modules.GetModule<ForkedTowerModule>().RenderMainUi(context);
    }

    private void DrawAggroRangePage(RenderContext context)
    {
        BocchiUi.PageHeading(T("pages.aggro_range.title"), T("pages.aggro_range.subtitle"));
        Plugin.Modules.GetModule<AggroRangeModule>().RenderMainUi(context);
    }

    private static void DrawAutomatorButton(AutomatorModule automator, string id, float width)
    {
        var label = automator.RunState switch
        {
            AutomatorRunState.Starting => T("buttons.cancel_start"),
            AutomatorRunState.Running => T("buttons.stop_automatic"),
            AutomatorRunState.Stopping => T("buttons.stopping"),
            _ => T("buttons.start_automatic"),
        };
        var stopping = automator.RunState == AutomatorRunState.Stopping;
        var stoppingRequested = automator.RequestedEnabled;

        var background = stoppingRequested ? LuminTheme.ErrorAccent : LuminTheme.Accent;
        var text = stoppingRequested ? LuminTheme.White : LuminTheme.Black;
        if (LuminWidgets.PrimaryButton(
                label,
                width: width,
                enabled: !stopping,
                backgroundOverride: background,
                textOverride: text))
        {
            automator.RequestEnabled(!automator.RequestedEnabled);
        }
    }

    private static void DrawStopAllButton(
        AutomatorModule automator,
        BocchiOperationSnapshot snapshot,
        float width)
    {
        if (LuminWidgets.PrimaryButton(
                T("buttons.stop_all"),
                tooltip: T("buttons.stop_all_tooltip"),
                width: width,
                enabled: snapshot.CanStopAll,
                backgroundOverride: new Vector4(0.58f, 0.20f, 0.18f, 1f),
                textOverride: LuminTheme.White))
        {
            automator.RequestStopAll();
        }
    }

    private static void DrawAreaStatus()
    {
        var north = ZoneData.IsInNorthHorn();
        var color = north ? BocchiUi.NorthHornAccent : ZoneData.IsInSouthHorn() ? BocchiUi.SouthHornAccent : BocchiUi.Muted;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(color, "●");
        ImGui.SameLine(0f, 5f);
        ImGui.TextUnformatted(GetZoneName());
    }

    private static void DrawOperationStatus(BocchiOperationSnapshot snapshot, bool compact)
    {
        ImGui.AlignTextToFramePadding();
        BocchiUi.StatusDot(snapshot.State);
        ImGui.SameLine(0f, 5f);
        ImGui.TextUnformatted(GetStateLabel(snapshot.State));
        ImGui.SameLine(0f, 8f);
        ImGui.TextDisabled($"· {GetOperationLabel(snapshot.Operation)}");
        if (!compact && snapshot.Source != BocchiOperationSource.None)
        {
            ImGui.SameLine(0f, 6f);
            ImGui.TextDisabled(snapshot.Source == BocchiOperationSource.Automatic ? T("source.automatic") : T("source.manual"));
        }
    }

    private BocchiOperationSnapshot GetOperationSnapshot(AutomatorModule automator)
    {
        var state = automator.RunState switch
        {
            AutomatorRunState.Starting => BocchiOperationState.Starting,
            AutomatorRunState.Running => BocchiOperationState.Running,
            AutomatorRunState.Stopping => BocchiOperationState.Stopping,
            _ when !string.IsNullOrWhiteSpace(automator.RunStateDetail) => BocchiOperationState.Failed,
            _ => BocchiOperationState.Stopped,
        };

        return BocchiOperationPolicy.Create(new BocchiOperationInput(
            state,
            automator.RunStateDetail,
            state is BocchiOperationState.Starting or BocchiOperationState.Running or BocchiOperationState.Stopping
                ? GetActivitySummary(automator)
                : null,
            Plugin.Modules.GetModule<TreasureModule>().IsHuntRunning,
            Plugin.Modules.GetModule<CarrotsModule>().IsHuntRunning,
            Plugin.Modules.GetModule<MobFarmerModule>().Farmer.Running));
    }

    /// <summary>
    /// Width of the collapsed capsule row's contents: mode pill plus the
    /// settings and stop icon buttons, including the gaps between them. Window
    /// padding is added by the caller so the number stays a pure content width.
    /// </summary>
    private static float CompactCapsuleWidth()
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        return LuminWidgets.PillToggleWidth(LongestStateLabel(), T("buttons.expand"))
               + spacing + ImGui.GetFrameHeight() // settings gear
               + spacing + ImGui.GetFrameHeight(); // stop all
    }

    private void SetCompactMode(bool compact)
    {
        var currentSize = ImGui.GetWindowSize();
        if (compact)
        {
            fullWindowSize = currentSize;
            ImGui.SetWindowSize(
                compactWindowSize ?? new Vector2(
                    CompactCapsuleWidth() + ImGui.GetStyle().WindowPadding.X * 2f,
                    ImGui.GetFrameHeight() + LuminTheme.S(24f)));
        }
        else
        {
            compactWindowSize = currentSize;
            ImGui.SetWindowSize(fullWindowSize ?? new Vector2(860f, 680f));
        }
        config.CompactMainWindow = compact;
        config.Save();
    }

    private static string GetZoneName()
    {
        return ZoneData.IsInSouthHorn()
            ? T("area.south")
            : ZoneData.IsInNorthHorn()
                ? T("area.north")
                : T("area.outside");
    }

    private static string GetStateLabel(BocchiOperationState state)
    {
        return state switch
        {
            BocchiOperationState.Starting => T("state.starting"),
            BocchiOperationState.Running => T("state.running"),
            BocchiOperationState.Stopping => T("state.stopping"),
            BocchiOperationState.Completed => T("state.completed"),
            BocchiOperationState.Failed => T("state.failed"),
            _ => T("state.stopped"),
        };
    }

    private static string GetOperationLabel(string operation)
    {
        return operation switch
        {
            "Automatic operation" => T("operation.automatic"),
            "Treasure hunt" => T("operation.treasure"),
            "Carrot hunt" => T("operation.carrot"),
            "Mob farming" => T("operation.farming"),
            "No active operation" => T("operation.none"),
            _ => operation,
        };
    }

    private static (string Label, FontAwesomeIcon Icon) GetPagePresentation(MainWindowPage page)
    {
        return page switch
        {
            MainWindowPage.Events => (T("nav.events"), FontAwesomeIcon.Calendar),
            MainWindowPage.Explore => (T("nav.explore"), FontAwesomeIcon.Compass),
            MainWindowPage.Farming => (T("nav.farming"), FontAwesomeIcon.Crosshairs),
            MainWindowPage.Statistics => (T("nav.statistics"), FontAwesomeIcon.ChartBar),
            MainWindowPage.Tower => (T("nav.tower"), FontAwesomeIcon.Building),
            MainWindowPage.AggroRange => (T("nav.aggro_range"), FontAwesomeIcon.Eye),

            _ => (T("nav.overview"), FontAwesomeIcon.Home),
        };
    }

    private string GetActivitySummary(AutomatorModule automator)
    {
        try
        {
            var name = automator.automator.Activity?.GetName();
            if (string.IsNullOrEmpty(name))
            {
                return automator.RunState == AutomatorRunState.Starting ? T("operation.preparing") : T("operation.waiting_target");
            }

            var state = automator.automator.Activity?.state.ToString();
            return string.IsNullOrEmpty(state) ? name : $"{name}（{state}）";
        }
        catch (AccessViolationException)
        {
            return T("operation.refreshing");
        }
    }

    private void DrawRuntimeSummary()
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>();
        var stateManager = Plugin.Modules.GetModule<StateManagerModule>();
        var activityName = T("operation.none");
        var activityState = T("operation.waiting_target");
        try
        {
            activityName = automator.automator.Activity?.GetName() ?? T("operation.none");
            activityState = automator.automator.Activity?.state.ToString() ?? T("operation.waiting_target");
        }
        catch (AccessViolationException)
        {
            activityName = T("operation.refreshing");
        }

        var rotation = automator.instanceRotation;
        var rotationEnabled = automator.Config.ShouldAutoRotateInstance
                              || rotation.State != InstanceRotationState.Idle;
        var rotationState = rotationEnabled ? rotation.GetStateLabel(automator) : T("runtime.not_enabled");
        var rotationRemaining = rotation.GetRemainingLabel(automator);
        var rotationPopulation = rotation.CurrentPopulation?.ToString() ?? "--";
        var vnavmesh = automator.GetVnavmeshAvailability();
        var vnavmeshLabel = vnavmesh.IsAvailable
            ? string.Format(T("runtime.vnav_available"), vnavmesh.DisplayVersion?.ToString() ?? T("runtime.version_unknown"))
            : vnavmesh.Status == Pathfinding.VnavmeshAvailabilityStatus.Missing
                ? T("runtime.not_installed")
                : T("runtime.not_loaded");

        if (ImGui.BeginTable("##RuntimeSummary", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg))
        {
            DrawKeyValueRow(T("runtime.game_state"), stateManager.GetStateText());
            DrawKeyValueRow(T("runtime.current_target"), activityName);
            DrawKeyValueRow(T("runtime.target_stage"), activityState);
            DrawKeyValueRow(T("runtime.rotation"), string.Format(T("runtime.rotation_value"), rotationState, rotationRemaining, rotationPopulation));
            DrawKeyValueRow("vnavmesh", vnavmeshLabel);
            ImGui.EndTable();
        }
    }

    private void DrawIslandSummary(uint territoryId)
    {
        var automator = Plugin.Modules.GetModule<AutomatorModule>().Config;
        var farmer = Plugin.Modules.GetModule<MobFarmerModule>().Config;
        var fateIds = EventData.GetFatesForTerritory(territoryId).Select(data => data.Id).Distinct().ToArray();
        var ceIds = EventData.GetCriticalEncountersForTerritory(territoryId).Select(data => data.Id).Distinct().ToArray();
        var enabledFates = fateIds.Count(id => automator.FatesMap.TryGetValue(id, out var enabled) && enabled);
        var enabledCes = ceIds.Count(id => automator.CriticalEncountersMap.TryGetValue(id, out var enabled) && enabled);
        var columns = BocchiUiPolicy.GetWorkspaceColumns(LuminTheme.ToDesign(ImGui.GetContentRegionAvail().X));

        if (ImGui.BeginTable("##IslandSummary", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextColumn();
            BocchiUi.Metric("FATE", automator.DoFates ? $"{enabledFates}/{fateIds.Length}" : T("metrics.disabled"));
            ImGui.TableNextColumn();
            BocchiUi.Metric("CE", automator.DoCriticalEncounters ? $"{enabledCes}/{ceIds.Length}" : T("metrics.disabled"));
            ImGui.TableNextColumn();
            BocchiUi.Metric(T("metrics.target_mobs"), farmer.GetMobsForTerritory(territoryId).Count.ToString());
            ImGui.TableNextColumn();
            BocchiUi.Metric(T("metrics.area_status"), Svc.ClientState.TerritoryType == territoryId ? T("metrics.current_area") : T("metrics.not_entered"));
            ImGui.EndTable();
        }
    }

    private void DrawConfiguredMobsSummary()
    {
        var farmerConfig = Plugin.Modules.GetModule<MobFarmerModule>().Config;
        var south = farmerConfig.GetMobsForTerritory(ZoneData.SOUTHHORN).Count;
        var north = farmerConfig.GetMobsForTerritory(ZoneData.NORTHHORN).Count;
        ImGui.TextDisabled(string.Format(T("mobs.summary"), south, north));
    }

    private void DrawConfiguredMobNames(uint territoryId)
    {
        var selected = Plugin.Modules.GetModule<MobFarmerModule>().Config.GetMobsForTerritory(territoryId);
        if (selected.Count == 0)
        {
            BocchiUi.EmptyState(T("mobs.empty_title"), T("mobs.empty_detail"));
            return;
        }

        ImGui.TextDisabled(string.Format(T("mobs.selected"), selected.Count));
        var columns = BocchiUiPolicy.GetWorkspaceColumns(LuminTheme.ToDesign(ImGui.GetContentRegionAvail().X));
        if (ImGui.BeginTable("##ConfiguredMobNames", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg))
        {
            foreach (var mob in selected)
            {
                ImGui.TableNextColumn();
                ImGui.BulletText(MobData.GetName(mob));
            }
            ImGui.EndTable();
        }
    }

    private static void DrawKeyValueRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private static string T(string key) => I18N.T($"windows.main.{key}");
}





