using System;
using System.Collections.Generic;

namespace LsrCoop.Client
{
    public class CoopCriminalHistoryStateDto
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public bool HasHistory { get; set; }
        public float LastSeenX { get; set; }
        public float LastSeenY { get; set; }
        public float LastSeenZ { get; set; }
        public int WantedLevel { get; set; }
        public List<CoopCriminalHistoryCrimeRecordDto> Crimes { get; set; } = new List<CoopCriminalHistoryCrimeRecordDto>();
        public DateTimeOffset DateTimeLastWantedEnded { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public string ClearReason { get; set; }
    }
}
