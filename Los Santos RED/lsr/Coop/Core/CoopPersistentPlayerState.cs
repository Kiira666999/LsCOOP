using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopPersistentPlayerState
    {
        public CoopPersistentPlayerState()
        {
            InventoryRecords = new List<string>();
            InventoryItems = new List<CoopInventoryItemState>();
            BankAccounts = new List<CoopBankAccountState>();
            WeaponRecords = new List<string>();
            OwnedVehicleIds = new List<string>();
            PropertyIds = new List<string>();
            GangReputationRecords = new List<string>();
            CriminalHistoryRecords = new List<string>();
            LongTermLsrRecords = new List<string>();
        }

        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public string CharacterAppearanceData { get; set; }
        public int Money { get; set; }
        public int OnHandCash { get; set; }
        public CoopOwnedVehicleSnapshot OwnedVehicleState { get; set; }
        public CoopGangReputationState GangReputationState { get; set; }
        public CoopCriminalHistoryState CriminalHistoryState { get; set; }
        public CoopPropertyOwnershipSnapshot PropertyOwnershipState { get; set; }
        public List<string> InventoryRecords { get; private set; }
        public List<CoopInventoryItemState> InventoryItems { get; private set; }
        public List<CoopBankAccountState> BankAccounts { get; private set; }
        public List<string> WeaponRecords { get; private set; }
        public List<string> OwnedVehicleIds { get; private set; }
        public List<string> PropertyIds { get; private set; }
        public List<string> GangReputationRecords { get; private set; }
        public List<string> CriminalHistoryRecords { get; private set; }
        public List<string> LongTermLsrRecords { get; private set; }
    }
}
