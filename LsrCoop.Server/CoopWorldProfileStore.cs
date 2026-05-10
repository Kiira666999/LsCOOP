using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopWorldProfileStore
    {
        public string StoreVersion { get; set; } = "1";
        public string WorldId { get; set; } = "lsrcoop-default-world";
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public CoopWorldRolesSnapshot Roles { get; set; } = new CoopWorldRolesSnapshot();
        public List<CoopPlayerProfile> Profiles { get; set; } = new List<CoopPlayerProfile>();
        public List<string> WorldFlags { get; set; } = new List<string>();
        public List<string> LongTermLsrRecords { get; set; } = new List<string>();
    }
}
