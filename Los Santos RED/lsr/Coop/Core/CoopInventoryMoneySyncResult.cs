using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopInventoryMoneySyncResult
    {
        public string RequestId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public bool Accepted { get; set; }
        public bool ValidatedByActiveHost { get; set; }
        public bool PersistedByServer { get; set; }
        public bool RequiresResync { get; set; }
        public string Reason { get; set; }
        public CoopInventoryMoneySnapshot Snapshot { get; set; }
        public DateTimeOffset ResultUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
