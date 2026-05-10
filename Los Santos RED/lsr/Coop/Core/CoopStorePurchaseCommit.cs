namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopStorePurchaseCommit
    {
        public CoopGameplayActionRequest Request { get; set; }
        public CoopGameplayActionResult Result { get; set; }
        public CoopInventoryMoneySnapshot InventoryMoneySnapshot { get; set; }
        public CoopWeaponSnapshot WeaponSnapshot { get; set; }
        public CoopPropertyOwnershipSnapshot PropertyOwnershipSnapshot { get; set; }
        public CoopOwnedVehicleSnapshot OwnedVehicleSnapshot { get; set; }
        public CoopDeathArrestState DeathArrestState { get; set; }
    }
}
