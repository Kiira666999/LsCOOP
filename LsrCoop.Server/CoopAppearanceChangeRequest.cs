using System;

namespace LsrCoop.Server
{
    public class CoopAppearanceChangeRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string TargetProfileId { get; set; }
        public CoopAppearanceState Appearance { get; set; }
        public DateTimeOffset RequestedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
