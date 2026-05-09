using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopSessionState
    {
        public CoopSessionState()
        {
            Characters = new List<CoopCharacterSnapshot>();
        }

        public LsrCoopMode Mode { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopCharacterId LocalCharacterId { get; set; }
        public CoopCharacterId ActiveHostCharacterId { get; set; }
        public List<CoopCharacterSnapshot> Characters { get; private set; }

        public bool IsEnabled => Mode != LsrCoopMode.Disabled;
        public bool IsActiveHost => Mode == LsrCoopMode.ActiveHost;
    }
}
