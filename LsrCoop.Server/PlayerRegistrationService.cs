using RageCoop.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace LsrCoop.Server
{
    internal class PlayerRegistrationService
    {
        private readonly WorldProfileStoreService worldProfileStoreService;
        private readonly RoleConfigService roleConfigService;
        private readonly EventRouter eventRouter;
        private readonly Action<string> info;

        public PlayerRegistrationService(WorldProfileStoreService worldProfileStoreService, RoleConfigService roleConfigService, EventRouter eventRouter, Action<string> info)
        {
            this.worldProfileStoreService = worldProfileStoreService;
            this.roleConfigService = roleConfigService;
            this.eventRouter = eventRouter;
            this.info = info;
            ClientStatuses = new Dictionary<string, CoopClientStatus>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, CoopClientStatus> ClientStatuses { get; }
        public IEnumerable<Client> ConnectedClients => ClientStatuses.Values.Select(x => x.Client).Where(x => x != null);

        public CoopClientStatus RegisterClient(Client client, string reason)
        {
            string profileId = GetClientProfileId(client);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            bool isNew = !ClientStatuses.TryGetValue(profileId, out CoopClientStatus status);
            if (isNew)
            {
                status = new CoopClientStatus(profileId, client);
                ClientStatuses[profileId] = status;
            }
            else
            {
                status.Client = client;
            }

            status.ClientId = GetClientId(client);
            status.Profile = worldProfileStoreService.LoadOrCreateProfile(status, client, status.ClientId, roleConfigService.GetRoleName(profileId));
            status.RefreshReadinessState();

            if (isNew)
            {
                info?.Invoke($"[LsrCoop.Server] client registered ({reason}): {profileId}, role={roleConfigService.GetRoleName(profileId)}, trustedHost={roleConfigService.IsTrustedHost(profileId)}, compatibility={status.CompatibilityState}, readiness={status.ReadinessState}");
            }

            return status;
        }

        public void RefreshConnectedClients(Dictionary<int, Client> allClients)
        {
            if (allClients == null)
            {
                return;
            }

            foreach (Client client in allClients.Values)
            {
                CoopClientStatus status = RegisterClient(client, "refresh");
                SendRegistrationState(status, "refresh");
            }
        }

        public void SendRegistrationState(CoopClientStatus status, string reason)
        {
            if (status?.Client == null || status.Profile == null)
            {
                return;
            }

            eventRouter.Send(status.Client, EventRouter.PlayerRegisteredEventHash, new object[]
            {
                worldProfileStoreService.WorldId,
                status.Profile.ProfileId,
                status.ClientId,
                status.Profile.DisplayName,
                status.Profile.Role,
                status.CompatibilityState.ToString()
            });

            if (status.Profile.Character == null)
            {
                status.CharacterSnapshotSent = false;
                status.CharacterSnapshotAcknowledged = false;
                status.RefreshReadinessState();
                eventRouter.Send(status.Client, EventRouter.CharacterCreateRequiredEventHash, new object[] { worldProfileStoreService.WorldId, status.Profile.ProfileId, reason });
                info?.Invoke($"[LsrCoop.Server] character create required: {status.Profile.ProfileId} ({reason}); readiness={status.ReadinessState}");
                return;
            }

            status.CharacterSnapshotSent = true;
            status.RefreshReadinessState();
            eventRouter.Send(status.Client, EventRouter.CharacterSnapshotEventHash, new object[]
            {
                worldProfileStoreService.WorldId,
                status.Profile.ProfileId,
                JsonSerializer.Serialize(status.Profile.Character),
                JsonSerializer.Serialize(status.Profile.InventoryMoney),
                JsonSerializer.Serialize(status.Profile.Weapons),
                JsonSerializer.Serialize(status.Profile.CriminalHistory),
                JsonSerializer.Serialize(status.Profile.GangReputation),
                JsonSerializer.Serialize(status.Profile.OwnedVehicles),
                JsonSerializer.Serialize(status.Profile.PropertyOwnership)
            });
            info?.Invoke($"[LsrCoop.Server] character snapshot sent: {status.Profile.ProfileId} ({reason}); readiness={status.ReadinessState}, ownedVehicles={(status.Profile.OwnedVehicles?.Vehicles?.Count ?? 0)}, properties={(status.Profile.PropertyOwnership?.Properties?.Count ?? 0)}, firstProperty={DescribeFirstProperty(status.Profile.PropertyOwnership)}, criminalHistory={(status.Profile.CriminalHistory?.Crimes?.Count ?? 0)}, gangReputation={(status.Profile.GangReputation?.Reputations?.Count ?? 0)}, vagos={DescribeGangReputationRecord(FindGangReputationRecord(status.Profile.GangReputation, "AMBIENT_GANG_MEXICAN"))}");
        }

        public CoopClientStatus AcknowledgeCharacterSnapshot(Client client, string worldId, string profileId, string reason, out bool readinessChanged)
        {
            readinessChanged = false;
            CoopClientStatus status = RegisterClient(client, reason);
            if (status == null)
            {
                return null;
            }

            if (!string.Equals(worldProfileStoreService.WorldId, worldId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(status.ProfileId, profileId, StringComparison.OrdinalIgnoreCase)
                || status.Profile?.Character == null)
            {
                status.RefreshReadinessState();
                info?.Invoke($"[LsrCoop.Server] character snapshot ack ignored: profile={profileId}, world={worldId}, readiness={status.ReadinessState}");
                return status;
            }

            bool wasReady = status.CharacterReadyForSimulation;
            bool wasAcknowledged = status.CharacterSnapshotAcknowledged;
            status.CharacterSnapshotSent = true;
            status.CharacterSnapshotAcknowledged = true;
            status.RefreshReadinessState();
            readinessChanged = !wasReady && status.CharacterReadyForSimulation;
            if (readinessChanged)
            {
                info?.Invoke($"[LsrCoop.Server] character ready for simulation: {status.ProfileId} ({reason})");
            }
            else if (!wasAcknowledged)
            {
                info?.Invoke($"[LsrCoop.Server] character snapshot acknowledged: {status.ProfileId} ({reason}); readiness={status.ReadinessState}");
            }
            return status;
        }

        public bool TrySaveCreatedCharacter(CoopClientStatus status, CoopCharacterCreatedRequest request, out string reason)
        {
            reason = string.Empty;
            if (status?.Profile == null)
            {
                reason = "Missing profile";
                return false;
            }

            if (request == null || request.Character == null)
            {
                reason = "Missing character payload";
                return false;
            }

            if (!string.Equals(worldProfileStoreService.WorldId, request.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Wrong co-op world {request.WorldId}";
                return false;
            }

            if (!string.Equals(status.ProfileId, request.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Profile mismatch {request.ProfileId}";
                return false;
            }

            if (status.Profile.Character != null)
            {
                reason = "Profile already has a character";
                return false;
            }

            if (request.Character.Appearance == null)
            {
                reason = "Missing appearance snapshot";
                return false;
            }

            request.Character.ProfileId = status.Profile.ProfileId;
            request.Character.CharacterId = string.IsNullOrWhiteSpace(request.Character.CharacterId) ? status.Profile.ProfileId : request.Character.CharacterId;
            request.Character.DisplayName = string.IsNullOrWhiteSpace(request.Character.DisplayName) ? status.Profile.DisplayName : request.Character.DisplayName;
            request.Character.ModelName = string.IsNullOrWhiteSpace(request.Character.ModelName) ? request.Character.Appearance.ModelName : request.Character.ModelName;
            request.Character.UpdatedUtc = DateTimeOffset.UtcNow;
            status.Profile.Character = request.Character;
            status.CharacterSnapshotSent = false;
            status.CharacterSnapshotAcknowledged = false;
            status.RefreshReadinessState();
            worldProfileStoreService.Save();
            info?.Invoke($"[LsrCoop.Server] character saved: profile={status.ProfileId}, character={status.Profile.Character.CharacterId}");
            return true;
        }

        public void Remove(string profileId)
        {
            if (!string.IsNullOrWhiteSpace(profileId))
            {
                ClientStatuses.Remove(profileId);
            }
        }

        public string GetClientProfileId(Client client)
        {
            return string.IsNullOrWhiteSpace(client?.Username) ? null : client.Username;
        }

        public string GetClientId(Client client)
        {
            if (client == null)
            {
                return null;
            }

            string[] propertyNames = { "Id", "ID", "NetHandle", "Handle" };
            foreach (string propertyName in propertyNames)
            {
                object value = client.GetType().GetProperty(propertyName)?.GetValue(client);
                if (value != null)
                {
                    return value.ToString();
                }
            }

            return GetClientProfileId(client);
        }

        public string GetClientName(Client client)
        {
            return string.IsNullOrWhiteSpace(client?.Username) ? "unknown" : client.Username;
        }

        private static CoopGangReputationRecordDto FindGangReputationRecord(CoopGangReputationStateDto state, string gangId)
        {
            return state?.Reputations?
                .Where(x => string.Equals(x?.GangId, gangId, StringComparison.OrdinalIgnoreCase))
                .LastOrDefault();
        }

        private static string DescribeGangReputationRecord(CoopGangReputationRecordDto record)
        {
            return record == null
                ? "missing"
                : $"{record.GangId}:rep={record.Reputation},hurt={record.MembersHurt},killed={record.MembersKilled}";
        }

        private static string DescribeFirstProperty(CoopPropertyOwnershipSnapshot snapshot)
        {
            CoopPropertyOwnershipRecord property = snapshot?.Properties?.FirstOrDefault();
            return property == null
                ? "none"
                : $"{property.PropertyId}|name={property.Name}|type={property.PropertyType}|owned={property.IsOwned}|rented={property.IsRented}|rentedOut={property.IsRentedOut}";
        }
    }
}
