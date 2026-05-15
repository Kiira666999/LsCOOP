using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopOwnedVehicleSnapshot
    {
        public string SnapshotId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public List<CoopOwnedVehicleRecord> Vehicles { get; set; } = new List<CoopOwnedVehicleRecord>();
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
