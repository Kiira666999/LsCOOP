using System;
using System.Collections.Generic;

namespace LsrCoop.Client
{
    public class CoopGameplayActionResultDto
    {
        public string RequestId { get; set; }
        public string ActionType { get; set; }
        public string WorldId { get; set; }
        public string SourceProfileId { get; set; }
        public string SourceCharacterId { get; set; }
        public string TargetProfileId { get; set; }
        public string TargetCharacterId { get; set; }
        public bool Accepted { get; set; }
        public bool RequiresPersistentCommit { get; set; }
        public bool RequiresResync { get; set; }
        public string Reason { get; set; }
        public Dictionary<string, string> ResultData { get; set; } = new Dictionary<string, string>();
        public DateTimeOffset ResolvedUtc { get; set; }
    }
}
