using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class LsrCoopRoleConfig
    {
        public string ConfigVersion { get; set; } = "1";
        public string WorldId { get; set; } = "lsrcoop-default-world";
        public List<string> AdminIds { get; set; } = new List<string>();
        public List<string> TrustedHostIds { get; set; } = new List<string>();
    }
}
