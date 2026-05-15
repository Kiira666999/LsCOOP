namespace LsrCoop.Client
{
    public class CoopBridgeDiagnosticsReportDto
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public int ProcessId { get; set; }
        public string SessionId { get; set; }
        public string BridgeRootPath { get; set; }
        public int StartupStateFiles { get; set; }
        public int CharacterCreatedFiles { get; set; }
        public int CharacterSnapshotFiles { get; set; }
        public int PendingGameplayOutFiles { get; set; }
        public int PendingGameplayInFiles { get; set; }
        public int TempFiles { get; set; }
        public int DeletedTempFiles { get; set; }
        public int DeletedStaleFiles { get; set; }
        public int DeletedMalformedFiles { get; set; }
        public int DeletedLegacyFiles { get; set; }
        public int LegacyFilesDetected { get; set; }
        public int KeptPendingFiles { get; set; }
        public int CleanupFailedFiles { get; set; }
        public string LastCleanupUtc { get; set; }
    }
}
