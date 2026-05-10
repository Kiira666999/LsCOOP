using RageCoop.Server;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LsrCoop.Server
{
    internal class ActiveHostService
    {
        private readonly RoleConfigService roleConfigService;
        private readonly CompatibilityService compatibilityService;
        private readonly EventRouter eventRouter;
        private readonly Action<string> info;
        private string activeHostId;
        private bool activeHostUnavailableAnnounced;

        public ActiveHostService(RoleConfigService roleConfigService, CompatibilityService compatibilityService, EventRouter eventRouter, Action<string> info)
        {
            this.roleConfigService = roleConfigService;
            this.compatibilityService = compatibilityService;
            this.eventRouter = eventRouter;
            this.info = info;
        }

        public string ActiveHostId => activeHostId;

        public bool IsActiveHost(string profileId)
        {
            return string.Equals(activeHostId, profileId, StringComparison.OrdinalIgnoreCase);
        }

        public void Evaluate(string reason, Dictionary<string, CoopClientStatus> clientStatuses)
        {
            if (!string.IsNullOrWhiteSpace(activeHostId)
                && clientStatuses.ContainsKey(activeHostId)
                && roleConfigService.IsTrustedHost(activeHostId)
                && compatibilityService.IsCompatible(clientStatuses[activeHostId]))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(activeHostId))
            {
                Release("active-host-invalid", clientStatuses.Values.Select(x => x.Client));
            }

            Client nextHost = clientStatuses
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Where(x => roleConfigService.IsTrustedHost(x.Key) && compatibilityService.IsCompatible(x.Value))
                .Select(x => x.Value.Client)
                .FirstOrDefault();

            if (nextHost == null)
            {
                AnnounceUnavailable(reason, clientStatuses.Values.Select(x => x.Client));
                return;
            }

            Assign(nextHost, reason, clientStatuses.Values.Select(x => x.Client));
        }

        public void Release(string reason, IEnumerable<Client> clients)
        {
            if (string.IsNullOrWhiteSpace(activeHostId))
            {
                return;
            }

            string releasedHostId = activeHostId;
            activeHostId = null;
            info?.Invoke($"[LsrCoop.Server] active host released: {releasedHostId} ({reason})");
            eventRouter.Broadcast(clients, EventRouter.ActiveHostReleasedEventHash, new object[] { roleConfigService.WorldId, releasedHostId, reason });
        }

        private void Assign(Client client, string reason, IEnumerable<Client> clients)
        {
            activeHostId = string.IsNullOrWhiteSpace(client?.Username) ? null : client.Username;
            activeHostUnavailableAnnounced = false;
            info?.Invoke($"[LsrCoop.Server] active host assigned: {activeHostId} ({reason})");
            eventRouter.Broadcast(clients, EventRouter.ActiveHostAssignedEventHash, new object[] { roleConfigService.WorldId, activeHostId, reason });
        }

        private void AnnounceUnavailable(string reason, IEnumerable<Client> clients)
        {
            if (activeHostUnavailableAnnounced)
            {
                return;
            }

            activeHostUnavailableAnnounced = true;
            info?.Invoke($"[LsrCoop.Server] active host unavailable; no compatible connected TrustedHost ({reason})");
            eventRouter.Broadcast(clients, EventRouter.ActiveHostUnavailableEventHash, new object[] { roleConfigService.WorldId, reason });
        }
    }
}
