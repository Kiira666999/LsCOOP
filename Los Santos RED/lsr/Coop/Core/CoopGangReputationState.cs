using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopGangReputationState
    {
        public CoopGangReputationState()
        {
            StateId = Guid.NewGuid().ToString("N");
            Reputations = new List<CoopGangReputationRecord>();
            UpdatedUtc = DateTimeOffset.UtcNow;
        }

        public string StateId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public string CurrentGangId { get; set; }
        public List<CoopGangReputationRecord> Reputations { get; private set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
