using System;
using System.Collections.Generic;

namespace LsrCoop.Client
{
    public class CoopPlayerProfileSnapshotDto
    {
        public string ProfileId { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public CoopCharacterSnapshot Character { get; set; }
        public CoopInventoryMoneySnapshot InventoryMoney { get; set; }
        public CoopWeaponSnapshot Weapons { get; set; }
        public CoopOwnedVehicleSnapshot OwnedVehicles { get; set; }
        public CoopPropertyOwnershipSnapshot PropertyOwnership { get; set; }
        public CoopGangReputationStateDto GangReputation { get; set; }
        public CoopCriminalHistoryStateDto CriminalHistory { get; set; }
        public CoopDeathArrestStateDto DeathArrestState { get; set; }
        public CoopLastPositionStateDto LastPosition { get; set; }
        public List<string> LongTermLsrRecords { get; set; } = new List<string>();
    }
}
