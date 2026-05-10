using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCriminalJusticeStateSnapshot
    {
        public CoopCriminalJusticeStateSnapshot()
        {
            SnapshotId = Guid.NewGuid().ToString("N");
            SnapshotUtc = DateTimeOffset.UtcNow;
        }

        public string SnapshotId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public CoopCriminalHistoryState CriminalHistory { get; set; }
        public CoopWantedRuntimeState WantedRuntime { get; set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
