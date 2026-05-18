using System;
using System.Linq;
using System.Text.Json;

namespace LsrCoop.Server
{
    internal class ActiveHostHandoffService
    {
        private readonly WorldProfileStoreService worldProfileStoreService;
        private readonly PlayerRegistrationService playerRegistrationService;
        private readonly ActiveHostService activeHostService;
        private readonly EventRouter eventRouter;
        private readonly Action<string> info;
        private bool hasCalendarPolicySnapshot;
        private int lastConnectedCharacterReadyProfileCount;
        private bool lastAllowGlobalTimeSkip;

        public ActiveHostHandoffService(WorldProfileStoreService worldProfileStoreService, PlayerRegistrationService playerRegistrationService, ActiveHostService activeHostService, EventRouter eventRouter, Action<string> info)
        {
            this.worldProfileStoreService = worldProfileStoreService;
            this.playerRegistrationService = playerRegistrationService;
            this.activeHostService = activeHostService;
            this.eventRouter = eventRouter;
            this.info = info;
        }

        public string LastHandoffReason { get; private set; }

        public void EvaluateAndSync(string reason)
        {
            string previousHostId = activeHostService.ActiveHostId;
            activeHostService.Evaluate(reason, playerRegistrationService.ClientStatuses);
            bool activeHostChanged = !SameHost(previousHostId, activeHostService.ActiveHostId);
            bool calendarPolicyChanged = HasCalendarPolicyChanged();
            if (activeHostChanged)
            {
                SavePersistentState(reason);
                DiscardTemporaryLiveState(reason);
                BroadcastWorldSnapshot(reason);
            }
            else if (calendarPolicyChanged)
            {
                BroadcastWorldSnapshot(reason);
            }
        }

        public void HandleActiveHostLeft(string profileId, string reason)
        {
            SavePersistentState(reason);
            DiscardTemporaryLiveState(reason);
            activeHostService.Release(reason, playerRegistrationService.ConnectedClients);
            activeHostService.Evaluate(reason, playerRegistrationService.ClientStatuses);
            BroadcastWorldSnapshot(reason);
            info?.Invoke($"[LsrCoop.Server] active host soft handoff complete: previous={profileId}, next={activeHostService.ActiveHostId ?? "none"}");
        }

        public void BroadcastWorldSnapshot(string reason)
        {
            LastHandoffReason = reason ?? string.Empty;
            CoopWorldSnapshotDto snapshot = CreateWorldSnapshot(reason);
            RememberCalendarPolicy(snapshot);
            eventRouter.Broadcast(playerRegistrationService.ConnectedClients, EventRouter.WorldSnapshotEventHash, new object[] { JsonSerializer.Serialize(snapshot) });
            info?.Invoke($"[LsrCoop.Server] world snapshot broadcast: world={snapshot.WorldId}, profiles={snapshot.Profiles.Count}, activeHost={snapshot.ActiveHostProfileId ?? "none"}, readyProfiles={snapshot.ConnectedCharacterReadyProfileCount}, allowGlobalTimeSkip={snapshot.AllowGlobalTimeSkip}, reason={reason}");
        }

        private void SavePersistentState(string reason)
        {
            worldProfileStoreService.Save();
            info?.Invoke($"[LsrCoop.Server] persistent world state saved before handoff ({reason})");
        }

        private void DiscardTemporaryLiveState(string reason)
        {
            info?.Invoke($"[LsrCoop.Server] temporary live incidents discarded for handoff ({reason}); active chases, spawned peds, roadblocks, witnesses, random vehicles, and live combat were not persisted");
        }

        private CoopWorldSnapshotDto CreateWorldSnapshot(string reason)
        {
            int connectedReadyProfiles = CountConnectedCharacterReadyProfiles();
            CoopWorldSnapshotDto snapshot = new CoopWorldSnapshotDto
            {
                StoreVersion = worldProfileStoreService.Store?.StoreVersion ?? "1",
                WorldId = worldProfileStoreService.WorldId,
                ActiveHostProfileId = activeHostService.ActiveHostId ?? string.Empty,
                Reason = reason ?? string.Empty,
                Roles = worldProfileStoreService.Store?.Roles,
                ConnectedCharacterReadyProfileCount = connectedReadyProfiles,
                AllowGlobalTimeSkip = connectedReadyProfiles <= 1,
                WorldFlags = worldProfileStoreService.Store?.WorldFlags?.ToList() ?? new System.Collections.Generic.List<string>(),
                LongTermLsrRecords = worldProfileStoreService.Store?.LongTermLsrRecords?.ToList() ?? new System.Collections.Generic.List<string>(),
            };

            foreach (CoopPlayerProfile profile in worldProfileStoreService.Profiles.OrderBy(x => x.ProfileId, StringComparer.OrdinalIgnoreCase))
            {
                snapshot.Profiles.Add(new CoopPlayerProfileSnapshotDto
                {
                    ProfileId = profile.ProfileId,
                    DisplayName = profile.DisplayName,
                    Role = profile.Role,
                    LastSeenUtc = profile.LastSeenUtc,
                    Character = profile.Character,
                    InventoryMoney = profile.InventoryMoney,
                    Weapons = profile.Weapons,
                    OwnedVehicles = profile.OwnedVehicles,
                    PropertyOwnership = profile.PropertyOwnership,
                    GangReputation = profile.GangReputation,
                    LocationDiscovery = profile.LocationDiscovery,
                    CriminalHistory = profile.CriminalHistory,
                    DeathArrestState = profile.DeathArrestState,
                    LastPosition = profile.LastPosition,
                    LongTermLsrRecords = profile.LongTermLsrRecords?.ToList() ?? new System.Collections.Generic.List<string>(),
                });
            }

            return snapshot;
        }

        private bool HasCalendarPolicyChanged()
        {
            int connectedReadyProfiles = CountConnectedCharacterReadyProfiles();
            bool allowGlobalTimeSkip = connectedReadyProfiles <= 1;
            return hasCalendarPolicySnapshot
                && (connectedReadyProfiles != lastConnectedCharacterReadyProfileCount || allowGlobalTimeSkip != lastAllowGlobalTimeSkip);
        }

        private int CountConnectedCharacterReadyProfiles()
        {
            return playerRegistrationService.ClientStatuses.Values.Count(x => x?.Client != null && x.CharacterReadyForSimulation);
        }

        private void RememberCalendarPolicy(CoopWorldSnapshotDto snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            hasCalendarPolicySnapshot = true;
            lastConnectedCharacterReadyProfileCount = snapshot.ConnectedCharacterReadyProfileCount;
            lastAllowGlobalTimeSkip = snapshot.AllowGlobalTimeSkip;
        }

        private bool SameHost(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
