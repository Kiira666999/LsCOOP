using LosSantosRED.lsr.Interface;
using Rage;
using System;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCriminalJusticeStateAdapter
    {
        public static CoopCriminalJusticeStateAdapter Current { get; } = new CoopCriminalJusticeStateAdapter();

        private Mod.Player localPlayer;
        private ICrimes crimes;

        public static void RegisterLocalRuntime(Mod.Player player, ICrimes crimeProvider)
        {
            Current.localPlayer = player;
            Current.crimes = crimeProvider;
        }

        public void NotifyLocalCriminalHistoryChanged()
        {
            if (!CoopStartupBridge.IsCoopEnabled || localPlayer == null)
            {
                return;
            }

            CoopCriminalJusticeStateSnapshot snapshot = CaptureFromPlayer(localPlayer, GetCurrentProfileId(), GetCurrentCharacterId(GetCurrentProfileId()), GetCurrentWorldId());
            CoopGameplayFileBridge.PublishCriminalJusticeSnapshot(snapshot);
            CoopPersistenceDiagnostics.WriteVerbose($"Co-op criminal history captured Profile:{snapshot.ProfileId} HasHistory:{snapshot.CriminalHistory?.HasHistory == true} Crimes:{snapshot.CriminalHistory?.Crimes?.Count ?? 0} Wanted:{snapshot.CriminalHistory?.WantedLevel ?? 0} DateTimeLastWantedEnded:{snapshot.CriminalHistory?.DateTimeLastWantedEnded.ToString("O") ?? string.Empty} ClearReason:{snapshot.CriminalHistory?.ClearReason ?? string.Empty}; live wanted/search state not persisted");
        }

        public CoopCriminalJusticeStateSnapshot CaptureFromPlayer(Mod.Player player, string profileId, string characterId, string worldId)
        {
            CoopWorldId resolvedWorldId = new CoopWorldId(string.IsNullOrWhiteSpace(worldId) ? CoopStartupBridge.WorldId : worldId);
            CoopProfileId resolvedProfileId = new CoopProfileId(string.IsNullOrWhiteSpace(profileId) ? CoopStartupBridge.LocalProfileId : profileId);
            CoopCharacterId resolvedCharacterId = new CoopCharacterId(string.IsNullOrWhiteSpace(characterId) ? resolvedProfileId.ToString() : characterId);

            return new CoopCriminalJusticeStateSnapshot
            {
                WorldId = resolvedWorldId,
                ProfileId = resolvedProfileId,
                CharacterId = resolvedCharacterId,
                CriminalHistory = player?.CriminalHistory?.CreateCoopState(resolvedWorldId, resolvedProfileId, resolvedCharacterId),
                WantedRuntime = null,
            };
        }

        public bool TrySavePersistentState(CoopServerWorldSave worldSave, CoopCriminalJusticeStateSnapshot snapshot)
        {
            if (worldSave?.WorldState?.Profiles == null || snapshot?.CriminalHistory == null || snapshot.ProfileId.IsEmpty)
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
            profile.PersistentState.CriminalHistoryState = snapshot.CriminalHistory;
            profile.PersistentState.CriminalHistoryRecords.Clear();
            foreach (CoopCriminalHistoryCrimeRecord crimeRecord in snapshot.CriminalHistory.Crimes)
            {
                profile.PersistentState.CriminalHistoryRecords.Add($"{crimeRecord.CrimeId}:{crimeRecord.Instances}");
            }

            worldSave.UpdatedUtc = DateTime.UtcNow;
            return true;
        }

        public bool TryGetPersistentState(CoopServerWorldSave worldSave, CoopProfileId profileId, out CoopCriminalHistoryState state)
        {
            state = null;
            CoopServerPlayerProfile profile = worldSave?.WorldState?.Profiles?.FirstOrDefault(x => x.ProfileId.Equals(profileId));
            if (profile?.PersistentState?.CriminalHistoryState == null)
            {
                return false;
            }

            state = profile.PersistentState.CriminalHistoryState;
            return true;
        }

        public bool TryApplyPersistentStateToPlayer(Mod.Player player, CoopCriminalHistoryState state)
        {
            return TryApplyPersistentStateToPlayer(player, state, crimes);
        }

        public bool TryApplyPersistentStateToPlayer(Mod.Player player, CoopCriminalHistoryState state, ICrimes crimeProvider)
        {
            if (player == null || state == null)
            {
                return false;
            }

            player.CriminalHistory.ApplyCoopState(state, crimeProvider ?? crimes);
            EntryPoint.WriteToConsole($"Co-op criminal history hydrated Profile:{state.ProfileId} HasHistory:{state.HasHistory} Crimes:{state.Crimes?.Count ?? 0} Wanted:{state.WantedLevel} DateTimeLastWantedEnded:{state.DateTimeLastWantedEnded:O}", 0);
            return true;
        }

        private CoopWantedRuntimeState CaptureWantedRuntime(IPoliceRespondable player, CoopWorldId worldId, CoopProfileId profileId, CoopCharacterId characterId)
        {
            if (player == null)
            {
                return null;
            }

            Vector3 lastReported = player.PoliceResponse?.PlaceLastReportedCrime ?? Vector3.Zero;
            Vector3 investigationPosition = player.Investigation?.Position ?? Vector3.Zero;

            return new CoopWantedRuntimeState
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
                WantedLevel = player.WantedLevel,
                WantedLevelHasBeenRadioedIn = player.PoliceResponse?.WantedLevelHasBeenRadioedIn == true,
                HasPlayerBeenIdentified = player.PoliceResponse?.HasPlayerBeenIdentified == true,
                PoliceHaveDescription = player.PoliceResponse?.PoliceHaveDescription == true,
                IsInvestigationActive = player.Investigation?.IsActive == true,
                IsInvestigationSuspicious = player.Investigation?.IsSuspicious == true,
                IsInSearchMode = player.IsInSearchMode,
                IsInWantedActiveMode = player.IsInWantedActiveMode,
                LastReportedCrimeX = lastReported.X,
                LastReportedCrimeY = lastReported.Y,
                LastReportedCrimeZ = lastReported.Z,
                InvestigationX = investigationPosition.X,
                InvestigationY = investigationPosition.Y,
                InvestigationZ = investigationPosition.Z,
                SnapshotUtc = DateTimeOffset.UtcNow,
            };
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
