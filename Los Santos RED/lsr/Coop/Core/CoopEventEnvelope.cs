using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopEventEnvelope
    {
        public CoopEventEnvelope()
        {
            EventId = Guid.NewGuid().ToString("N");
            CreatedUtc = DateTime.UtcNow;
            Payload = new byte[0];
        }

        public string EventId { get; set; }
        public CoopEventType EventType { get; set; }
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId SourceProfileId { get; set; }
        public CoopCharacterId SourceCharacterId { get; set; }
        public CoopProfileId TargetProfileId { get; set; }
        public CoopCharacterId TargetCharacterId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string CorrelationId { get; set; }
        public byte[] Payload { get; set; }
    }
}
