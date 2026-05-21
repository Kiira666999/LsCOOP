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
        public List<CoopCriminalHistoryVehiclePlateRecordDto> KnownVehiclePlates { get; set; } = new List<CoopCriminalHistoryVehiclePlateRecordDto>();
        public DateTimeOffset DateTimeLastWantedEnded { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public string ClearReason { get; set; }
    }

    public class CoopCriminalHistoryVehiclePlateRecordDto
    {
        public string PlateNumber { get; set; }
        public int PlateType { get; set; }
        public uint OriginalModelHash { get; set; }
    }
}
