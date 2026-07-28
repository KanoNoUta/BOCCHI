using BOCCHI.Data;
using System;

namespace BOCCHI.Modules.MobFarmer;

public interface IRotationPlugin : IDisposable
{
    public void PhantomJobOn(Job? job = null);

    public void PhantomJobOff(Job? job = null);
}
