namespace LosSantosRED.lsr.Coop.Core
{
    public interface ICoopCharacter
    {
        CoopCharacterId CharacterId { get; }
        CoopProfileId ProfileId { get; }
        LsrCoopAuthorityRole Role { get; }
        CoopPermission Permissions { get; }
        ICoopPedProvider PedProvider { get; }
        CoopInventoryMoneySnapshot InventoryMoneySnapshot { get; }
        CoopWeaponSnapshot WeaponSnapshot { get; }
        CoopCharacterSnapshot GetSnapshot();
    }
}
