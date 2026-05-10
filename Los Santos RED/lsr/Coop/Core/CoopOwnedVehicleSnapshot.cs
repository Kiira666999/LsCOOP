using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopOwnedVehicleSnapshot
    {
        public CoopOwnedVehicleSnapshot()
        {
            SnapshotId = Guid.NewGuid().ToString("N");
            Vehicles = new List<CoopOwnedVehicleRecord>();
            SnapshotUtc = DateTimeOffset.UtcNow;
        }

        public string SnapshotId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public List<CoopOwnedVehicleRecord> Vehicles { get; private set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
