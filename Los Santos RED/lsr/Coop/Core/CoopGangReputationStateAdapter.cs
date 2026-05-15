using LosSantosRED.lsr.Interface;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopGangReputationStateAdapter
    {
        public static CoopGangReputationStateAdapter Current { get; } = new CoopGangReputationStateAdapter();

        private Mod.Player localPlayer;
        private IGangs gangs;
        private readonly Dictionary<string, string> lastPublishedSignaturesByProfile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, int>> lastPublishedReputationsByProfile = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

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
            int recordCount = snapshot.Reputations?.Count ?? 0;
            if (recordCount == 0)
            {
                return;
            }

            string profileKey = snapshot.ProfileId.ToString();
            string signature = CreateSignature(snapshot);
            if (lastPublishedSignaturesByProfile.TryGetValue(profileKey, out string lastSignature)
                && string.Equals(lastSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            LogCapturedChanges(snapshot);
            lastPublishedSignaturesByProfile[profileKey] = signature;
            lastPublishedReputationsByProfile[profileKey] = snapshot.Reputations
                .Where(x => !string.IsNullOrWhiteSpace(x.GangId))
                .GroupBy(x => x.GangId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Reputation, StringComparer.OrdinalIgnoreCase);

            CoopGameplayFileBridge.PublishGangReputationSnapshot(snapshot);
        }

        public CoopGangReputationState CaptureFromPlayer(Mod.Player player, string profileId, string characterId, string worldId)
        {
            CoopWorldId resolvedWorldId = new CoopWorldId(string.IsNullOrWhiteSpace(worldId) ? CoopStartupBridge.WorldId : worldId);
            CoopProfileId resolvedProfileId = new CoopProfileId(string.IsNullOrWhiteSpace(profileId) ? CoopStartupBridge.LocalProfileId : profileId);
            CoopCharacterId resolvedCharacterId = new CoopCharacterId(string.IsNullOrWhiteSpace(characterId) ? resolvedProfileId.ToString() : characterId);

            CoopGangReputationState state = player?.RelationshipManager?.GangRelationships?.CreateCoopState(resolvedWorldId, resolvedProfileId, resolvedCharacterId)
                ?? new CoopGangReputationState
                {
                    WorldId = resolvedWorldId,
                    ProfileId = resolvedProfileId,
                    CharacterId = resolvedCharacterId,
                };

            state.Reputations = state.Reputations ?? new List<CoopGangReputationRecord>();
            return state;
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

            if (player.RelationshipManager?.GangRelationships == null || state.Reputations == null || !state.Reputations.Any())
            {
                return false;
            }

            CoopGangReputationRecord incomingVagos = FindGangReputationRecord(state, "AMBIENT_GANG_MEXICAN");
            player.RelationshipManager.GangRelationships.ApplyCoopState(state, gangs);
            Gang vagosGang = gangs?.GetGang("AMBIENT_GANG_MEXICAN");
            GangReputation appliedVagos = player.RelationshipManager.GangRelationships.GetReputation(vagosGang);
            EntryPoint.WriteToConsole($"Co-op gang reputation hydrated Profile:{state.ProfileId} Records:{state.Reputations.Count} CurrentGang:{state.CurrentGangId ?? "none"} IncomingVagos:{DescribeGangReputationRecord(incomingVagos)} AppliedVagos:{DescribeGangReputation(appliedVagos)}; live gang combat/spawn state skipped", 5);
            return true;
        }

        private void LogCapturedChanges(CoopGangReputationState snapshot)
        {
            string profileKey = snapshot.ProfileId.ToString();
            lastPublishedReputationsByProfile.TryGetValue(profileKey, out Dictionary<string, int> previous);
            List<string> changedGangIds = new List<string>();
            foreach (CoopGangReputationRecord record in snapshot.Reputations.Where(x => !string.IsNullOrWhiteSpace(x.GangId)))
            {
                int oldReputation = 0;
                bool hasOld = previous != null && previous.TryGetValue(record.GangId, out oldReputation);
                if (hasOld && oldReputation == record.Reputation)
                {
                    continue;
                }

                Gang gang = gangs?.GetGang(record.GangId);
                if (!hasOld && IsDefaultRecord(record, gang))
                {
                    continue;
                }

                changedGangIds.Add(record.GangId);
            }

            EntryPoint.WriteToConsole($"Co-op gang reputation snapshot captured Profile:{snapshot.ProfileId} Records:{snapshot.Reputations.Count} Changed:{changedGangIds.Count} ChangedGangs:{(changedGangIds.Count == 0 ? "none" : string.Join("|", changedGangIds.Take(8)))} CurrentGang:{snapshot.CurrentGangId ?? "none"}", 5);
        }

        private static string CreateSignature(CoopGangReputationState snapshot)
        {
            if (snapshot?.Reputations == null)
            {
                return string.Empty;
            }

            return $"{snapshot.CurrentGangId ?? string.Empty}|"
                + string.Join(";", snapshot.Reputations
                    .Where(x => !string.IsNullOrWhiteSpace(x.GangId))
                    .OrderBy(x => x.GangId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => string.Join(",", new[]
                    {
                        x.GangId,
                        x.GangName ?? string.Empty,
                        x.Reputation.ToString(),
                        x.MembersHurt.ToString(),
                        x.MembersKilled.ToString(),
                        x.MembersCarJacked.ToString(),
                        x.MembersHurtInTerritory.ToString(),
                        x.MembersKilledInTerritory.ToString(),
                        x.MembersCarJackedInTerritory.ToString(),
                        x.PlayerDebt.ToString(),
                        x.IsMember.ToString(),
                        x.IsEnemy.ToString(),
                        x.TasksCompleted.ToString(),
                    })));
        }

        private static bool IsDefaultRecord(CoopGangReputationRecord record, Gang gang)
        {
            return gang != null
                && record.Reputation == gang.StartingRep
                && record.MembersHurt == 0
                && record.MembersKilled == 0
                && record.MembersCarJacked == 0
                && record.MembersHurtInTerritory == 0
                && record.MembersKilledInTerritory == 0
                && record.MembersCarJackedInTerritory == 0
                && record.PlayerDebt == 0
                && !record.IsMember
                && !record.IsEnemy
                && record.TasksCompleted == 0;
        }

        private static CoopGangReputationRecord FindGangReputationRecord(CoopGangReputationState state, string gangId)
        {
            return state?.Reputations?
                .Where(x => string.Equals(x?.GangId, gangId, StringComparison.OrdinalIgnoreCase))
                .LastOrDefault();
        }

        private static string DescribeGangReputationRecord(CoopGangReputationRecord record)
        {
            return record == null
                ? "missing"
                : $"{record.GangId}:rep={record.Reputation},hurt={record.MembersHurt},killed={record.MembersKilled}";
        }

        private static string DescribeGangReputation(GangReputation record)
        {
            return record == null || record.Gang == null
                ? "missing"
                : $"{record.Gang.ID}:rep={record.ReputationLevel},hurt={record.MembersHurt},killed={record.MembersKilled}";
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
