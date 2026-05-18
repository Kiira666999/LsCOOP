using System;
using System.Collections.Generic;

namespace LsrCoop.Client
{
    public class CoopWorldSnapshotDto
    {
        public string SnapshotId { get; set; }
        public string StoreVersion { get; set; }
        public string WorldId { get; set; }
        public string ActiveHostProfileId { get; set; }
        public string Reason { get; set; }
        public CoopWorldRolesSnapshot Roles { get; set; }
        public int ConnectedCharacterReadyProfileCount { get; set; }
        public bool AllowGlobalTimeSkip { get; set; }
        public List<string> WorldFlags { get; set; } = new List<string>();
        public List<string> LongTermLsrRecords { get; set; } = new List<string>();
        public DateTimeOffset SnapshotUtc { get; set; }
        public List<CoopPlayerProfileSnapshotDto> Profiles { get; set; } = new List<CoopPlayerProfileSnapshotDto>();
    }
}
