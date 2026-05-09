namespace LosSantosRED.lsr.Coop.Core
{
    public class LocalCoopCharacter : ICoopCharacter
    {
        public LocalCoopCharacter(
            CoopCharacterId characterId,
            CoopProfileId profileId,
            string displayName,
            LocalCoopPedProvider pedProvider,
            LsrCoopAuthorityRole role,
            CoopPermission permissions,
            Mod.Player player)
        {
            CharacterId = characterId;
            ProfileId = profileId;
            DisplayName = displayName ?? string.Empty;
            PedProvider = pedProvider;
            Role = role;
            Permissions = permissions;
            Player = player;
        }

        public CoopCharacterId CharacterId { get; private set; }
        public CoopProfileId ProfileId { get; private set; }
        public string DisplayName { get; private set; }
        public LsrCoopAuthorityRole Role { get; private set; }
        public CoopPermission Permissions { get; private set; }
        public ICoopPedProvider PedProvider { get; private set; }
        public Mod.Player Player { get; private set; }

        public CoopCharacterSnapshot GetSnapshot()
        {
            LocalCoopPedProvider localPedProvider = PedProvider as LocalCoopPedProvider;

            return new CoopCharacterSnapshot
            {
                CharacterId = CharacterId,
                ProfileId = ProfileId,
                DisplayName = DisplayName,
                ModelName = localPedProvider != null && localPedProvider.HasPed ? localPedProvider.Ped.Model.Name : string.Empty,
                IsLocal = true,
                IsConnected = true,
                Role = Role,
                Permissions = Permissions
            };
        }
    }
}
