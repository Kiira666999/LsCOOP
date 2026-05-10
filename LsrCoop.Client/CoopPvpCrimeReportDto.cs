using System;

namespace LsrCoop.Client
{
    public class CoopPvpCrimeReportDto
    {
        public CoopPvpCrimeReportDto()
        {
            EventId = Guid.NewGuid().ToString("N");
            TimestampUtc = DateTimeOffset.UtcNow;
        }

        public string EventId { get; set; }
        public string WorldId { get; set; }
        public string SourceProfileId { get; set; }
        public string SourceCharacterId { get; set; }
        public string TargetProfileId { get; set; }
        public string TargetCharacterId { get; set; }
        public int OffenderPedHandle { get; set; }
        public int VictimPedHandle { get; set; }
        public bool WasKilled { get; set; }
        public bool WasShot { get; set; }
        public bool WasMeleeAttacked { get; set; }
        public bool WasHitByVehicle { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public DateTimeOffset TimestampUtc { get; set; }
    }
}
