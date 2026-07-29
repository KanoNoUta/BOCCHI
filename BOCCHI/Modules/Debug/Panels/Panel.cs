using System;

namespace BOCCHI.Modules.Debug.Panels;

public abstract class Panel : IDisposable
{
    public abstract string GetName();

    public virtual void Update(DebugModule module)
    {
    }

    public virtual void Render(DebugModule module)
    {
    }

    public virtual void OnTerritoryChanged(uint id, DebugModule module)
    {
    }

    public virtual void Dispose()
    {
    }
}
