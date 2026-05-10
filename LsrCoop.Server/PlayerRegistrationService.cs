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

            if (isNew)
            {
                info?.Invoke($"[LsrCoop.Server] client registered ({reason}): {profileId}, role={roleConfigService.GetRoleName(profileId)}, trustedHost={roleConfigService.IsTrustedHost(profileId)}, compatibility={status.CompatibilityState}");
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
                eventRouter.Send(status.Client, EventRouter.CharacterCreateRequiredEventHash, new object[] { worldProfileStoreService.WorldId, status.Profile.ProfileId, reason });
                info?.Invoke($"[LsrCoop.Server] character create required: {status.Profile.ProfileId} ({reason})");
                return;
            }

            eventRouter.Send(status.Client, EventRouter.CharacterSnapshotEventHash, new object[]
            {
                worldProfileStoreService.WorldId,
                status.Profile.ProfileId,
                JsonSerializer.Serialize(status.Profile.Character)
            });
            info?.Invoke($"[LsrCoop.Server] character snapshot sent: {status.Profile.ProfileId} ({reason})");
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
    }
}
