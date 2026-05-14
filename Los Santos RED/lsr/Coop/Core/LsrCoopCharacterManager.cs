using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class LsrCoopCharacterManager
    {
        private readonly Dictionary<CoopCharacterId, ICoopCharacter> characters = new Dictionary<CoopCharacterId, ICoopCharacter>();

        public ICoopCharacter LocalCharacter { get; private set; }
        public IEnumerable<ICoopCharacter> AllCharacters => characters.Values.ToList();

        public bool TryGetCharacter(CoopCharacterId id, out ICoopCharacter character)
        {
            return characters.TryGetValue(id, out character);
        }

        public LocalCoopCharacter RegisterLocalCharacter(Mod.Player player)
        {
            string displayName = player == null ? string.Empty : player.PlayerName;
            if (CoopStartupBridge.IsCoopEnabled && !string.IsNullOrWhiteSpace(CoopStartupBridge.LocalProfileId))
            {
                string profileId = CoopStartupBridge.LocalProfileId;
                return RegisterLocalCharacter(
                    player,
                    new CoopCharacterId(profileId),
                    new CoopProfileId(profileId),
                    displayName,
                    ResolveLocalRole(),
                    ResolveLocalPermissions());
            }

            return RegisterLocalCharacter(
                player,
                new CoopCharacterId("single-player-local"),
                new CoopProfileId("single-player-profile"),
                displayName,
                LsrCoopAuthorityRole.Admin,
                CoopPermission.ClientPresentation | CoopPermission.ActiveHostSimulation | CoopPermission.AdminActions);
        }

        public LocalCoopCharacter RegisterLocalCharacter(
            Mod.Player player,
            CoopCharacterId characterId,
            CoopProfileId profileId,
            string displayName,
            LsrCoopAuthorityRole role,
            CoopPermission permissions)
        {
            LocalCoopCharacter localCharacter = new LocalCoopCharacter(
                characterId,
                profileId,
                displayName,
                new LocalCoopPedProvider(player),
                role,
                permissions,
                player);

            LocalCharacter = localCharacter;
            characters[characterId] = localCharacter;

            return localCharacter;
        }

        public void Clear()
        {
            LocalCharacter = null;
            characters.Clear();
        }

        private LsrCoopAuthorityRole ResolveLocalRole()
        {
            if (CoopStartupBridge.IsLocalAdmin)
            {
                return LsrCoopAuthorityRole.Admin;
            }

            return CoopStartupBridge.IsLocalActiveHost ? LsrCoopAuthorityRole.TrustedHost : LsrCoopAuthorityRole.Player;
        }

        private CoopPermission ResolveLocalPermissions()
        {
            CoopPermission permissions = CoopPermission.ClientPresentation;
            if (CoopStartupBridge.IsLocalActiveHost)
            {
                permissions |= CoopPermission.ActiveHostSimulation;
            }

            if (CoopStartupBridge.IsLocalAdmin)
            {
                permissions |= CoopPermission.AdminActions;
            }

            return permissions;
        }
    }
}
