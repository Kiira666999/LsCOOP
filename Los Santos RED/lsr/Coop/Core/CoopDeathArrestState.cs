using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopDeathArrestState
    {
        public CoopDeathArrestState()
        {
            StateId = Guid.NewGuid().ToString("N");
            OccurredUtc = DateTimeOffset.UtcNow;
        }

        public string StateId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public CoopGameplayActionType ActionType { get; set; }
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
