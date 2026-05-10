namespace LsrCoop.Server
{
    public class CoopStorePurchaseCommitDto
    {
        public CoopGameplayActionRequestDto Request { get; set; }
        public CoopGameplayActionResultDto Result { get; set; }
        public CoopInventoryMoneySnapshot InventoryMoneySnapshot { get; set; }
        public CoopWeaponSnapshot WeaponSnapshot { get; set; }
        public CoopPropertyOwnershipSnapshot PropertyOwnershipSnapshot { get; set; }
        public CoopOwnedVehicleSnapshot OwnedVehicleSnapshot { get; set; }
        public CoopDeathArrestStateDto DeathArrestState { get; set; }
    }
}
