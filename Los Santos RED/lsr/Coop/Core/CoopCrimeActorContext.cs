using LSR.Vehicles;
using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCrimeActorContext
    {
        public CoopProfileId OffenderProfileId { get; set; }
        public CoopCharacterId OffenderCharacterId { get; set; }
        public Ped ActorPed { get; set; }
        public Entity ActorEntity { get; set; }
        public int ActorPedHandle { get; set; }
        public Vehicle ActorVehicle { get; set; }
        public VehicleExt ActorVehicleExt { get; set; }
        public Vector3 Position { get; set; }
        public string SourceClientId { get; set; }
        public bool IsLocalActor { get; set; }
        public bool IsActiveHostActor { get; set; }
    }
}
