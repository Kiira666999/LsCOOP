using System;

namespace LsrCoop.Server
{
    public class CoopDeathArrestStateDto
    {
        public string StateId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public string ActionType { get; set; }
        public string OutcomeType { get; set; }
        public string RespawnLocationName { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float Heading { get; set; }
        public int HospitalFee { get; set; }
        public int HospitalBillPastDue { get; set; }
        public int HospitalDuration { get; set; }
        public int BailFee { get; set; }
        public int BailFeePastDue { get; set; }
        public int BailDuration { get; set; }
        public int TodayPayment { get; set; }
        public int TimesDied { get; set; }
        public bool HadIllegalWeapons { get; set; }
        public bool HadIllegalItems { get; set; }
        public DateTime ReleaseDate { get; set; }
        public DateTimeOffset OccurredUtc { get; set; }
    }
}
