using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCriminalHistoryState
    {
        public CoopCriminalHistoryState()
        {
            Crimes = new List<CoopCriminalHistoryCrimeRecord>();
            UpdatedUtc = DateTimeOffset.UtcNow;
        }

        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public bool HasHistory { get; set; }
        public float LastSeenX { get; set; }
        public float LastSeenY { get; set; }
        public float LastSeenZ { get; set; }
        public int WantedLevel { get; set; }
        public List<CoopCriminalHistoryCrimeRecord> Crimes { get; private set; }
        public DateTimeOffset DateTimeLastWantedEnded { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public string ClearReason { get; set; }
    }
}
