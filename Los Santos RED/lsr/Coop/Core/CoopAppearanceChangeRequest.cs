using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopAppearanceChangeRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopProfileId TargetProfileId { get; set; }
        public CoopAppearanceState Appearance { get; set; }
        public DateTimeOffset RequestedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
