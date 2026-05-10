using System;

namespace LsrCoop.Client
{
    public class CoopCriminalJusticeStateSnapshotDto
    {
        public string SnapshotId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public CoopCriminalHistoryStateDto CriminalHistory { get; set; }
        public CoopWantedRuntimeStateDto WantedRuntime { get; set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
