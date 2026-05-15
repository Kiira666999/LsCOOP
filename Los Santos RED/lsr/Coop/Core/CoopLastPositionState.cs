using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopLastPositionState
    {
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Heading { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
        public string Source { get; set; }
    }
}
