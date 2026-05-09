using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopGameplayActionResult
    {
        public CoopGameplayActionResult()
        {
            ResultData = new Dictionary<string, string>();
            ResolvedUtc = DateTimeOffset.UtcNow;
        }

        public string RequestId { get; set; }
        public CoopGameplayActionType ActionType { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId SourceProfileId { get; set; }
        public CoopCharacterId SourceCharacterId { get; set; }
        public CoopProfileId TargetProfileId { get; set; }
        public CoopCharacterId TargetCharacterId { get; set; }
        public bool Accepted { get; set; }
        public bool RequiresPersistentCommit { get; set; }
        public bool RequiresResync { get; set; }
        public string Reason { get; set; }
        public Dictionary<string, string> ResultData { get; private set; }
        public DateTimeOffset ResolvedUtc { get; set; }
    }
}
