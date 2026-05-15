using RageCoop.Server;

namespace LsrCoop.Server
{
    internal sealed class CoopClientStatus
    {
        public CoopClientStatus(string profileId, Client client)
        {
            ProfileId = profileId;
            Client = client;
        }

        public string ProfileId { get; }
        public string ClientId { get; set; }
        public Client Client { get; set; }
        public CoopPlayerProfile Profile { get; set; }
        public CoopClientCompatibilityState CompatibilityState { get; set; } = CoopClientCompatibilityState.Unknown;
        public string CoopBuildVersion { get; set; }
        public string LsrVersion { get; set; }
        public string ConfigVersion { get; set; }
        public bool RequiredResourceLoaded { get; set; }
        public CoopBridgeDiagnosticsReportDto BridgeDiagnostics { get; set; }
        public CoopClientReadinessState ReadinessState { get; set; } = CoopClientReadinessState.Connected;
        public bool CharacterSnapshotSent { get; set; }
        public bool CharacterSnapshotAcknowledged { get; set; }
        public bool CharacterReadyForSimulation => CompatibilityState == CoopClientCompatibilityState.Compatible
            && Profile?.Character != null
            && CharacterSnapshotAcknowledged;

        public void RefreshReadinessState()
        {
            if (CharacterReadyForSimulation)
            {
                ReadinessState = CoopClientReadinessState.CharacterReadyForSimulation;
            }
            else if (CharacterSnapshotSent)
            {
                ReadinessState = CoopClientReadinessState.CharacterSnapshotSent;
            }
            else if (Profile?.Character == null && Profile != null)
            {
                ReadinessState = CoopClientReadinessState.CharacterRequired;
            }
            else if (Profile != null)
            {
                ReadinessState = CoopClientReadinessState.ProfileLoaded;
            }
            else if (CompatibilityState == CoopClientCompatibilityState.Compatible)
            {
                ReadinessState = CoopClientReadinessState.Compatible;
            }
            else
            {
                ReadinessState = CoopClientReadinessState.Connected;
            }
        }
    }
}
