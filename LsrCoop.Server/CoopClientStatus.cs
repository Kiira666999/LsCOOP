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
    }
}
