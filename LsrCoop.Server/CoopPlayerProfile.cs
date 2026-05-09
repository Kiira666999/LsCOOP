using System;

namespace LsrCoop.Server
{
    public class CoopPlayerProfile
    {
        public string ProfileId { get; set; }
        public string ClientId { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
        public CoopCharacterSnapshot Character { get; set; }
    }
}
