using BOCCHI.Modules.Buff;
using Ocelot.Commands;
using Ocelot.Modules;

namespace BOCCHI.Commands;

[OcelotCommand]
public class BuffCommand(Plugin plugin) : OcelotCommand
{
    protected override string Command
    {
        get => "/bocchibuff";
    }

    protected override string Description
    {
        get => @"
重新施放配置的增益与爆发技能。
 - /bocchibuff : 立即重新施放增益/爆发
--------------------------------
".Trim();
    }


    public override void Execute(string command, string arguments)
    {
        plugin.Modules.GetModule<BuffModule>().BuffManager.QueueBuffs();
    }
}
