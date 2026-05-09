using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public interface IActorRequirementContext
    {
        LsrActorContext ActorContext { get; }
        object LegacyPlayer { get; }
        Ped ActorPed { get; }
        Vehicle ActorVehicle { get; }
        Vector3 Position { get; }
        bool IsLocal { get; }
        bool IsRemote { get; }
        bool IsCoopEnabled { get; }
        bool IsActiveHost { get; }
        bool IsAdmin { get; }
        bool IsTrustedHost { get; }
    }
}
