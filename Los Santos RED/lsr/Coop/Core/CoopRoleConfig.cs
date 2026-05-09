using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopRoleConfig
    {
        public CoopRoleConfig()
        {
            AdminIds = new List<string>();
            TrustedHostIds = new List<string>();
        }

        public List<string> AdminIds { get; private set; }
        public List<string> TrustedHostIds { get; private set; }

        public LsrCoopAuthorityRole GetRole(string profileId)
        {
            if (AdminIds.Contains(profileId))
            {
                return LsrCoopAuthorityRole.Admin;
            }

            if (TrustedHostIds.Contains(profileId))
            {
                return LsrCoopAuthorityRole.TrustedHost;
            }

            return LsrCoopAuthorityRole.Player;
        }
    }
}
