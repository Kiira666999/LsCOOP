using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class LegacyPlayerRequirementContext : LsrRequirementContext
    {
        public LegacyPlayerRequirementContext(
            object legacyPlayer,
            Ped actorPed,
            Vehicle actorVehicle,
            Vector3 position,
            bool isCoopEnabled)
            : base(null, legacyPlayer, actorPed, actorVehicle, position, isCoopEnabled)
        {
        }
    }
}
