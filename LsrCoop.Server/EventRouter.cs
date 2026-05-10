using RageCoop.Core.Scripting;
using RageCoop.Server;
using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    internal class EventRouter
    {
        public static readonly int PingEventHash = CustomEvents.Hash("lsrcoop.ping");
        public static readonly int PongEventHash = CustomEvents.Hash("lsrcoop.pong");
        public static readonly int CompatibilityReportEventHash = CustomEvents.Hash("lsrcoop.compatibility.report");
        public static readonly int CompatibilityStatusEventHash = CustomEvents.Hash("lsrcoop.compatibility.status");
        public static readonly int PlayerRegisteredEventHash = CustomEvents.Hash("lsrcoop.player.registered");
        public static readonly int CharacterCreateRequiredEventHash = CustomEvents.Hash("lsrcoop.character.createRequired");
        public static readonly int CharacterSnapshotEventHash = CustomEvents.Hash("lsrcoop.character.snapshot");
        public static readonly int CharacterSnapshotAckEventHash = CustomEvents.Hash("lsrcoop.character.snapshotAck");
        public static readonly int AppearanceChangeRequestedEventHash = CustomEvents.Hash("lsrcoop.appearance.changeRequested");
        public static readonly int AppearanceChangedEventHash = CustomEvents.Hash("lsrcoop.appearance.changed");
        public static readonly int ActiveHostAssignedEventHash = CustomEvents.Hash("lsrcoop.activeHost.assigned");
        public static readonly int ActiveHostReleasedEventHash = CustomEvents.Hash("lsrcoop.activeHost.released");
        public static readonly int ActiveHostUnavailableEventHash = CustomEvents.Hash("lsrcoop.activeHost.unavailable");
        public static readonly int WorldSnapshotEventHash = CustomEvents.Hash("lsrcoop.world.snapshot");
        public static readonly int GameplayActionCommittedEventHash = CustomEvents.Hash("lsrcoop.gameplay.action.committed");
        public static readonly int GameplayActionResultEventHash = CustomEvents.Hash("lsrcoop.gameplay.action.result");
        public static readonly int PvpCrimeReportedEventHash = CustomEvents.Hash("lsrcoop.crime.pvp.reported");
        public static readonly int PvpCrimeAssignedEventHash = CustomEvents.Hash("lsrcoop.crime.pvp.assigned");
        public static readonly int CriminalJusticeSnapshotCommittedEventHash = CustomEvents.Hash("lsrcoop.criminalJustice.snapshot.committed");
        public static readonly int GangReputationSnapshotCommittedEventHash = CustomEvents.Hash("lsrcoop.gangReputation.snapshot.committed");

        private readonly Action<string> warning;

        public EventRouter(Action<string> warning)
        {
            this.warning = warning;
        }

        public void Send(Client client, int eventHash, object[] args)
        {
            client?.SendCustomEvent(eventHash, args);
        }

        public void Broadcast(IEnumerable<Client> clients, int eventHash, object[] args)
        {
            if (clients == null)
            {
                return;
            }

            foreach (Client client in clients)
            {
                try
                {
                    client?.SendCustomEvent(eventHash, args);
                }
                catch (Exception ex)
                {
                    warning?.Invoke($"[LsrCoop.Server] failed to send event {eventHash} to {GetClientName(client)}: {ex.Message}");
                }
            }
        }

        private string GetClientName(Client client)
        {
            return string.IsNullOrWhiteSpace(client?.Username) ? "unknown" : client.Username;
        }
    }
}
