using System;

namespace LsrCoop.Server
{
    public class CoopCharacterSnapshot
    {
        public string CharacterId { get; set; }
        public string ProfileId { get; set; }
        public string DisplayName { get; set; }
        public string ModelName { get; set; }
        public CoopAppearanceState Appearance { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
