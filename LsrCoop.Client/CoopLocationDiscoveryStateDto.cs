using System;
using System.Collections.Generic;

namespace LsrCoop.Client
{
    public class CoopLocationDiscoveryStateDto
    {
        public CoopLocationDiscoveryStateDto()
        {
            StateId = Guid.NewGuid().ToString("N");
            DiscoveredLocationIds = new List<string>();
            UpdatedUtc = DateTimeOffset.UtcNow;
        }

        public string StateId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public List<string> DiscoveredLocationIds { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
