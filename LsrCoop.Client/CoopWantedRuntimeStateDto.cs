using System;

namespace LsrCoop.Client
{
    public class CoopWantedRuntimeStateDto
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public int WantedLevel { get; set; }
        public bool WantedLevelHasBeenRadioedIn { get; set; }
        public bool HasPlayerBeenIdentified { get; set; }
        public bool PoliceHaveDescription { get; set; }
        public bool IsInvestigationActive { get; set; }
        public bool IsInvestigationSuspicious { get; set; }
        public bool IsInSearchMode { get; set; }
        public bool IsInWantedActiveMode { get; set; }
        public float LastReportedCrimeX { get; set; }
        public float LastReportedCrimeY { get; set; }
        public float LastReportedCrimeZ { get; set; }
        public float InvestigationX { get; set; }
        public float InvestigationY { get; set; }
        public float InvestigationZ { get; set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
