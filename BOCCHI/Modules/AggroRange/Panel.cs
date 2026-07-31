using BOCCHI.Data;
using BOCCHI.Pathfinding;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace BOCCHI.Modules.AggroRange;

public sealed class Panel
{
    public void Draw(AggroRangeModule module)
    {
        OcelotUi.Title("普通怪仇恨范围：");
        OcelotUi.Indent(() =>
        {
            if (!ZoneData.IsInNorthHorn())
            {
                ImGui.TextDisabled("进入北部新月岛后显示实时范围圈。");
                return;
            }

            OcelotUi.LabelledValue("当前绘制", module.VisibleMobCount);
            OcelotUi.LabelledValue("已进入范围", module.InsideRangeCount);
            OcelotUi.LabelledValue("已实测校准", module.CalibratedMobCount);
            OcelotUi.LabelledValue("路线预计算", AggroAvoidanceNavigation.IsPlanning ? "计算中" : "空闲");
            ImGui.TextDisabled($"目录 {CommonMobCatalog.Count} 种；基准为 10 yalms + 怪物实时 Hitbox。");
            ImGui.TextDisabled("视觉/听觉均绘制最大半径；被动开怪后自动校准该怪数据。");
        });
    }
}
