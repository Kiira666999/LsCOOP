using LSR.Vehicles;
using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCrimeVictimContext
    {
        public CoopProfileId VictimProfileId { get; set; }
        public CoopCharacterId VictimCharacterId { get; set; }
        public Ped VictimPed { get; set; }
        public Entity VictimEntity { get; set; }
        public int VictimPedHandle { get; set; }
        public Vehicle VictimVehicle { get; set; }
        public VehicleExt VictimVehicleExt { get; set; }
        public Vector3 Position { get; set; }
        public bool IsCoopPlayer { get; set; }
        public bool IsLocalVictim { get; set; }
    }
}
