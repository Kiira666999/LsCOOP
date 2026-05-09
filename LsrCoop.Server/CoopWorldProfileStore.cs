using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopWorldProfileStore
    {
        public string StoreVersion { get; set; } = "1";
        public string WorldId { get; set; } = "lsrcoop-default-world";
        public List<CoopPlayerProfile> Profiles { get; set; } = new List<CoopPlayerProfile>();
    }
}
