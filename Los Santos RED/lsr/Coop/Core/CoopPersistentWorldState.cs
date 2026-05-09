using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopPersistentWorldState
    {
        public CoopPersistentWorldState()
        {
            AdminIds = new List<string>();
            TrustedHostIds = new List<string>();
            Profiles = new List<CoopServerPlayerProfile>();
            PropertyRecords = new List<string>();
            OwnedVehicleRecords = new List<string>();
            WorldFlags = new List<string>();
        }

        public CoopWorldId WorldId { get; set; }
        public List<string> AdminIds { get; private set; }
        public List<string> TrustedHostIds { get; private set; }
        public List<CoopServerPlayerProfile> Profiles { get; private set; }
        public List<string> PropertyRecords { get; private set; }
        public List<string> OwnedVehicleRecords { get; private set; }
        public List<string> WorldFlags { get; private set; }
    }
}
