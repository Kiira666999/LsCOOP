using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopLocationDiscoveryState
    {
        public CoopLocationDiscoveryState()
        {
            StateId = Guid.NewGuid().ToString("N");
            DiscoveredLocationIds = new List<string>();
            UpdatedUtc = DateTimeOffset.UtcNow;
        }

        public string StateId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public List<string> DiscoveredLocationIds { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
