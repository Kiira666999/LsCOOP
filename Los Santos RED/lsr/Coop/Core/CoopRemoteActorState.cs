using System;
using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopRemoteActorState
    {
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public string RageCoopPlayerId { get; set; }
        public int PedHandle { get; set; }
        public int VehicleHandle { get; set; }
        public Vector3 Position { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
