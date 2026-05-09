using System;

namespace LosSantosRED.lsr.Coop.Core
{
    [Flags]
    public enum CoopPermission
    {
        None = 0,
        ClientPresentation = 1,
        ActiveHostSimulation = 2,
        ServerPersistence = 4,
        AdminActions = 8,
    }
}
