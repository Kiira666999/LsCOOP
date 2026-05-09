using System;

namespace LsrCoop.Client
{
    public class CoopAppearanceChanged
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string SourceProfileId { get; set; }
        public CoopAppearanceState Appearance { get; set; }
        public DateTimeOffset AcceptedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
