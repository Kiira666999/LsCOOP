using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopAppearanceChanged
    {
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopAppearanceState Appearance { get; set; }
        public CoopProfileId SourceProfileId { get; set; }
        public DateTimeOffset AcceptedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
