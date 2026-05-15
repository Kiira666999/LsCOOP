using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopPropertyOwnershipSnapshot
    {
        public CoopPropertyOwnershipSnapshot()
        {
            SnapshotId = Guid.NewGuid().ToString("N");
            Properties = new List<CoopPropertyOwnershipRecord>();
            SnapshotUtc = DateTimeOffset.UtcNow;
        }

        public string SnapshotId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public List<CoopPropertyOwnershipRecord> Properties { get; set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
