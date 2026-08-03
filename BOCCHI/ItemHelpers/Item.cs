using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BOCCHI.ItemHelpers;

public unsafe class Item(uint id)
{
    public int Count()
    {
        return TryCount(out var count) ? count : 0;
    }

    public bool TryCount(out int count)
    {
        try
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
            {
                count = 0;
                return false;
            }

            count = inventoryManager->GetInventoryItemCount(id);
            return true;
        }
        catch
        {
            count = 0;
            return false;
        }
    }

    public void Use()
    {
        try
        {
            AgentInventoryContext.Instance()->UseItem(id);
        }
        catch
        {
            // ignored
        }
    }
}
