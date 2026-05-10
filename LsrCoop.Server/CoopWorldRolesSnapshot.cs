using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopWorldRolesSnapshot
    {
        public string ConfigVersion { get; set; } = "1";
        public string WorldId { get; set; }
        public List<string> AdminIds { get; set; } = new List<string>();
        public List<string> TrustedHostIds { get; set; } = new List<string>();
        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
