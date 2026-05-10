using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopWeaponSnapshot
    {
        public CoopWeaponSnapshot()
        {
            SnapshotId = Guid.NewGuid().ToString("N");
            Weapons = new List<CoopWeaponRecord>();
            SnapshotUtc = DateTimeOffset.UtcNow;
        }

        public string SnapshotId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public List<CoopWeaponRecord> Weapons { get; private set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
