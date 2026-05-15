namespace LsrCoop.Server
{
    internal sealed class CoopBridgeDiagnosticsReportDto
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public int ProcessId { get; set; }
        public string SessionId { get; set; }
        public int StartupStateFiles { get; set; }
        public int CharacterCreatedFiles { get; set; }
        public int CharacterSnapshotFiles { get; set; }
        public int PendingGameplayOutFiles { get; set; }
        public int PendingGameplayInFiles { get; set; }
        public int TempFiles { get; set; }
        public int DeletedStaleFiles { get; set; }
        public int CleanupFailedFiles { get; set; }
        public string LastCleanupUtc { get; set; }
    }
}
