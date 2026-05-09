using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopServerCharacterSave
    {
        public CoopServerCharacterSave()
        {
            AppearanceRecords = new List<string>();
        }

        public CoopCharacterId CharacterId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public string DisplayName { get; set; }
        public string ModelName { get; set; }
        public string AppearanceData { get; set; }
        public List<string> AppearanceRecords { get; private set; }
    }
}
