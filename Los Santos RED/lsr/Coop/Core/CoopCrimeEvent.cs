using LSR.Vehicles;
using Rage;
using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCrimeEvent
    {
        public CoopCrimeEvent()
        {
            EventId = Guid.NewGuid().ToString("N");
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        public string EventId { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId OffenderProfileId { get; set; }
        public CoopCharacterId OffenderCharacterId { get; set; }
        public CoopProfileId VictimProfileId { get; set; }
        public CoopCharacterId VictimCharacterId { get; set; }
        public CoopCrimeActorContext ActorContext { get; set; }
        public CoopCrimeVictimContext VictimContext { get; set; }
        public global::Crime Crime { get; set; }
        public string CrimeId { get; set; }
        public string CrimeName { get; set; }
        public bool IsPlayerOnPlayerViolence { get; set; }
        public bool IsRemoteActorCrime { get; set; }
        public bool WasKilled { get; set; }
        public bool WasShot { get; set; }
        public bool WasMeleeAttacked { get; set; }
        public bool WasHitByVehicle { get; set; }
        public global::CrimeSceneDescription CrimeSceneDescription { get; set; }
        public bool IsObservedByPolice { get; set; }
        public VehicleExt ActorVehicle { get; set; }
        public global::WeaponInformation ActorWeapon { get; set; }
        public Vector3 Position { get; set; }
        public bool HaveDescription { get; set; }
        public bool AnnounceCrime { get; set; }
        public bool IsForPlayer { get; set; }
        public bool AlwaysAddInstance { get; set; }
        public string SourceClientId { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }
        public int WantedLevelBefore { get; set; }
        public int WantedLevelAfter { get; set; }
        public bool HadLongTermCriminalHistoryBefore { get; set; }
        public bool HasLongTermCriminalHistoryAfter { get; set; }
        public bool CreatedLongTermCriminalHistory { get; set; }
        public int LongTermCriminalHistoryCrimeCount { get; set; }
        public bool TemporaryStatePersisted { get; set; }
    }
}
