using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopWeaponSnapshot
    {
        public string SnapshotId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public List<CoopWeaponRecord> Weapons { get; set; } = new List<CoopWeaponRecord>();
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
