using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopServerPlayerProfile
    {
        public CoopServerPlayerProfile()
        {
            Character = new CoopServerCharacterSave();
            PersistentState = new CoopPersistentPlayerState();
            CreatedUtc = DateTime.UtcNow;
            LastSeenUtc = CreatedUtc;
        }

        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public string DisplayName { get; set; }
        public LsrCoopAuthorityRole Role { get; set; }
        public CoopServerCharacterSave Character { get; set; }
        public CoopPersistentPlayerState PersistentState { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }
}
