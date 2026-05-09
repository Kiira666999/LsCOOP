using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopGameplayActionRequest
    {
        public CoopGameplayActionRequest()
        {
            RequestId = Guid.NewGuid().ToString("N");
            Parameters = new Dictionary<string, string>();
            RequestedUtc = DateTimeOffset.UtcNow;
        }

        public string RequestId { get; set; }
        public CoopGameplayActionType ActionType { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId SourceProfileId { get; set; }
        public CoopCharacterId SourceCharacterId { get; set; }
        public CoopProfileId TargetProfileId { get; set; }
        public CoopCharacterId TargetCharacterId { get; set; }
        public Dictionary<string, string> Parameters { get; private set; }
        public bool AllowsOptimisticClientFeedback { get; set; }
        public DateTimeOffset RequestedUtc { get; set; }
    }
}
