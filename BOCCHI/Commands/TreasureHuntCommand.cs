using BOCCHI.Modules.Treasure;
using ECommons.DalamudServices;
using Ocelot.Commands;
using Ocelot.Modules;
using System.Collections.Generic;

namespace BOCCHI.Commands;

[OcelotCommand]
public sealed class TreasureHuntCommand(Plugin plugin) : OcelotCommand
{
    public const string ShortAlias = "/ochth";

    protected override string Command => "/bocchitreasure";

    protected override string Description => @"
切换宝箱猎人。
 - /ochth
 - /bocchi th
--------------------------------
".Trim();

    protected override IReadOnlyList<string> Aliases => [ShortAlias, "/bocchith"];

    public override void Execute(string command, string arguments)
    {
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            Svc.Chat.PrintError("用法：/ochth");
            return;
        }

        if (!plugin.Modules.TryGetModule<TreasureModule>(out var treasure) || treasure == null)
        {
            Svc.Chat.PrintError("宝箱模块尚未准备完成。");
            return;
        }

        if (treasure.IsHuntRunning)
        {
            treasure.StopHunt();
            Svc.Chat.Print("宝箱猎人已停止。");
            return;
        }

        if (!treasure.TryStartHunt(out var error))
        {
            Svc.Chat.PrintError(error);
            return;
        }

        Svc.Chat.Print("宝箱猎人已启动。再次输入 /ochth 可停止。");
    }
}
