using LosSantosRED.lsr.Interface;
using System;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopGangReputationStateAdapter
    {
        public static CoopGangReputationStateAdapter Current { get; } = new CoopGangReputationStateAdapter();

        private Mod.Player localPlayer;
        private IGangs gangs;

        public static void RegisterLocalRuntime(Mod.Player player, IGangs gangProvider)
        {
            Current.localPlayer = player;
            Current.gangs = gangProvider;
        }

        public void NotifyLocalGangReputationChanged()
        {
            if (!CoopStartupBridge.IsCoopEnabled || localPlayer == null)
            {
                return;
            }

            CoopGangReputationState snapshot = CaptureFromPlayer(localPlayer, GetCurrentProfileId(), GetCurrentCharacterId(GetCurrentProfileId()), GetCurrentWorldId());
            CoopGameplayFileBridge.PublishGangReputationSnapshot(snapshot);
        }

        public CoopGangReputationState CaptureFromPlayer(Mod.Player player, string profileId, string characterId, string worldId)
        {
            CoopWorldId resolvedWorldId = new CoopWorldId(string.IsNullOrWhiteSpace(worldId) ? CoopStartupBridge.WorldId : worldId);
            CoopProfileId resolvedProfileId = new CoopProfileId(string.IsNullOrWhiteSpace(profileId) ? CoopStartupBridge.LocalProfileId : profileId);
            CoopCharacterId resolvedCharacterId = new CoopCharacterId(string.IsNullOrWhiteSpace(characterId) ? resolvedProfileId.ToString() : characterId);

            return player?.RelationshipManager?.GangRelationships?.CreateCoopState(resolvedWorldId, resolvedProfileId, resolvedCharacterId)
                ?? new CoopGangReputationState
                {
                    WorldId = resolvedWorldId,
                    ProfileId = resolvedProfileId,
                    CharacterId = resolvedCharacterId,
                };
        }

        public bool TrySavePersistentState(CoopServerWorldSave worldSave, CoopGangReputationState snapshot)
        {
            if (worldSave?.WorldState?.Profiles == null || snapshot == null || snapshot.ProfileId.IsEmpty)
            {
                return false;
            }

            CoopServerPlayerProfile profile = worldSave.WorldState.Profiles.FirstOrDefault(x => x.ProfileId.Equals(snapshot.ProfileId));
            if (profile == null)
            {
                profile = new CoopServerPlayerProfile
                {
                    WorldId = snapshot.WorldId,
                    ProfileId = snapshot.ProfileId,
                    DisplayName = snapshot.ProfileId.ToString(),
                };
                worldSave.WorldState.Profiles.Add(profile);
            }

            profile.PersistentState.WorldId = snapshot.WorldId;
            profile.PersistentState.ProfileId = snapshot.ProfileId;
            profile.PersistentState.CharacterId = snapshot.CharacterId;
            profile.PersistentState.GangReputationState = snapshot;
            profile.PersistentState.GangReputationRecords.Clear();
            foreach (CoopGangReputationRecord record in snapshot.Reputations)
            {
                profile.PersistentState.GangReputationRecords.Add($"{record.GangId}:{record.Reputation}");
            }

            worldSave.UpdatedUtc = DateTime.UtcNow;
            return true;
        }

        public bool TryGetPersistentState(CoopServerWorldSave worldSave, CoopProfileId profileId, out CoopGangReputationState state)
        {
            state = null;
            CoopServerPlayerProfile profile = worldSave?.WorldState?.Profiles?.FirstOrDefault(x => x.ProfileId.Equals(profileId));
            if (profile?.PersistentState?.GangReputationState == null)
            {
                return false;
            }

            state = profile.PersistentState.GangReputationState;
            return true;
        }

        public bool TryApplyPersistentStateToPlayer(Mod.Player player, CoopGangReputationState state)
        {
            if (player == null || state == null)
            {
                return false;
            }

            player.RelationshipManager?.GangRelationships?.ApplyCoopState(state, gangs);
            return true;
        }

        private static string GetCurrentWorldId()
        {
            return string.IsNullOrWhiteSpace(CoopStartupBridge.WorldId) ? "single-player-world" : CoopStartupBridge.WorldId;
        }

        private static string GetCurrentProfileId()
        {
            return string.IsNullOrWhiteSpace(CoopStartupBridge.LocalProfileId) ? "single-player-profile" : CoopStartupBridge.LocalProfileId;
        }

        private static string GetCurrentCharacterId(string profileId)
        {
            return string.IsNullOrWhiteSpace(profileId) || profileId == "single-player-profile" ? "single-player-character" : profileId;
        }
    }
}
