using System;
using System.Collections.Generic;

namespace LsrCoop.Client
{
    public class CoopOwnedVehicleSnapshot
    {
        public CoopOwnedVehicleSnapshot()
        {
            Vehicles = new List<CoopOwnedVehicleRecord>();
        }

        public string SnapshotId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public List<CoopOwnedVehicleRecord> Vehicles { get; private set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
