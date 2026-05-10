using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopGameplayActionRequestDto
    {
        public string RequestId { get; set; }
        public string ActionType { get; set; }
        public string WorldId { get; set; }
        public string SourceProfileId { get; set; }
        public string SourceCharacterId { get; set; }
        public string TargetProfileId { get; set; }
        public string TargetCharacterId { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public bool AllowsOptimisticClientFeedback { get; set; }
        public DateTimeOffset RequestedUtc { get; set; }
    }
}
