namespace LosSantosRED.lsr.Coop.Core
{
    public enum CoopEventType
    {
        PlayerJoined = 0,
        PlayerLeft = 1,
        PlayerSnapshot = 2,
        ActiveHostAssigned = 3,
        ActiveHostReleased = 4,
        CharacterCreateRequested = 5,
        CharacterCreated = 6,
        CharacterSnapshotChanged = 7,
        AppearanceChangeRequested = 8,
        AppearanceChanged = 9,
        GameplayActionRequested = 10,
        GameplayActionResult = 11,
        CrimeCommitted = 12,
        PurchaseRequested = 13,
        PurchaseResult = 14,
        PropertyChanged = 15,
        VehicleOwnershipChanged = 16,
        GangReputationChanged = 17,
        CriminalHistoryChanged = 18,
        DeathStateChanged = 19,
        ArrestStateChanged = 20,
        SaveRequested = 21,
        SaveCompleted = 22,
        InventoryMoneySnapshot = 23,
        InventoryMoneySyncResult = 24,
    }
}
