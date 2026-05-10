using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopPropertyOwnershipSnapshot
    {
        public CoopPropertyOwnershipSnapshot()
        {
            Properties = new List<CoopPropertyOwnershipRecord>();
        }

        public string SnapshotId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public List<CoopPropertyOwnershipRecord> Properties { get; private set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
