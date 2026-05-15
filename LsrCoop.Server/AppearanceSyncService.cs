using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LsrCoop.Server
{
    internal class AppearanceSyncService
    {
        private readonly WorldProfileStoreService worldProfileStoreService;
        private readonly RoleConfigService roleConfigService;
        private readonly EventRouter eventRouter;
        private readonly Func<IEnumerable<RageCoop.Server.Client>> getClients;
        private readonly Action<string> info;
        private readonly Action<string> warning;

        public AppearanceSyncService(WorldProfileStoreService worldProfileStoreService, RoleConfigService roleConfigService, EventRouter eventRouter, Func<IEnumerable<RageCoop.Server.Client>> getClients, Action<string> info, Action<string> warning)
        {
            this.worldProfileStoreService = worldProfileStoreService;
            this.roleConfigService = roleConfigService;
            this.eventRouter = eventRouter;
            this.getClients = getClients;
            this.info = info;
            this.warning = warning;
        }

        public void HandleAppearanceChange(CoopClientStatus requester, CoopAppearanceChangeRequest request)
        {
            if (requester == null)
            {
                return;
            }

            if (request?.Appearance == null)
            {
                warning?.Invoke($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: empty appearance");
                return;
            }

            request.ProfileId = string.IsNullOrWhiteSpace(request.ProfileId) ? requester.ProfileId : request.ProfileId;
            request.TargetProfileId = string.IsNullOrWhiteSpace(request.TargetProfileId) ? requester.ProfileId : request.TargetProfileId;
            request.WorldId = string.IsNullOrWhiteSpace(request.WorldId) ? worldProfileStoreService.WorldId : request.WorldId;

            if (!string.Equals(request.WorldId, worldProfileStoreService.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                warning?.Invoke($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: wrong world {request.WorldId}");
                return;
            }

            bool requesterIsAdmin = roleConfigService.IsAdmin(requester.ProfileId);
            if (!string.Equals(requester.ProfileId, request.TargetProfileId, StringComparison.OrdinalIgnoreCase) && !requesterIsAdmin)
            {
                warning?.Invoke($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: target {request.TargetProfileId} requires Admin");
                return;
            }

            CoopPlayerProfile targetProfile = worldProfileStoreService.GetProfile(request.TargetProfileId);
            if (targetProfile == null)
            {
                warning?.Invoke($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: missing profile {request.TargetProfileId}");
                return;
            }

            if (IsModelChangeAfterCreation(targetProfile, request.Appearance) && !requesterIsAdmin)
            {
                warning?.Invoke($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: model change after creation requires Admin");
                return;
            }

            SaveAcceptedAppearance(targetProfile, request.Appearance);
            CoopAppearanceChanged changed = new CoopAppearanceChanged
            {
                WorldId = worldProfileStoreService.WorldId,
                ProfileId = targetProfile.ProfileId,
                SourceProfileId = requester.ProfileId,
                Appearance = request.Appearance,
                AcceptedUtc = DateTimeOffset.UtcNow
            };

            eventRouter.Broadcast(getClients(), EventRouter.AppearanceChangedEventHash, new object[] { JsonSerializer.Serialize(changed) });
            BroadcastCharacterSnapshot(targetProfile);
            info?.Invoke($"[LsrCoop.Server] appearance accepted: profile={targetProfile.ProfileId}, source={requester.ProfileId}");
        }

        public void BroadcastCharacterSnapshot(CoopPlayerProfile profile)
        {
            if (profile?.Character == null)
            {
                return;
            }

            eventRouter.Broadcast(getClients(), EventRouter.CharacterSnapshotEventHash, new object[]
            {
                worldProfileStoreService.WorldId,
                profile.ProfileId,
                JsonSerializer.Serialize(profile.Character),
                JsonSerializer.Serialize(profile.InventoryMoney),
                JsonSerializer.Serialize(profile.Weapons),
                JsonSerializer.Serialize(profile.CriminalHistory),
                JsonSerializer.Serialize(profile.GangReputation),
                JsonSerializer.Serialize(profile.OwnedVehicles)
            });
        }

        private void SaveAcceptedAppearance(CoopPlayerProfile profile, CoopAppearanceState appearance)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (profile.Character == null)
            {
                profile.Character = new CoopCharacterSnapshot
                {
                    CharacterId = profile.ProfileId,
                    ProfileId = profile.ProfileId,
                    DisplayName = profile.DisplayName,
                    ModelName = appearance.ModelName,
                    UpdatedUtc = now
                };
            }

            profile.Character.Appearance = appearance;
            profile.Character.UpdatedUtc = now;
            profile.Character.DisplayName = profile.DisplayName;
            if (!string.IsNullOrWhiteSpace(appearance.ModelName))
            {
                profile.Character.ModelName = appearance.ModelName;
            }

            worldProfileStoreService.Save();
        }

        private bool IsModelChangeAfterCreation(CoopPlayerProfile profile, CoopAppearanceState appearance)
        {
            if (profile.Character == null || appearance == null || string.IsNullOrWhiteSpace(appearance.ModelName))
            {
                return false;
            }

            string existingModel = !string.IsNullOrWhiteSpace(profile.Character.ModelName)
                ? profile.Character.ModelName
                : profile.Character.Appearance?.ModelName;

            return !string.IsNullOrWhiteSpace(existingModel) && !string.Equals(existingModel, appearance.ModelName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
