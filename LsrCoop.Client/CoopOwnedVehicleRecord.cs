using System;

namespace LsrCoop.Client
{
    public class CoopOwnedVehicleRecord
    {
        public string VehicleId { get; set; }
        public string ModelHash { get; set; }
        public string ModelName { get; set; }
        public string PlateNumber { get; set; }
        public int PlateType { get; set; }
        public bool PlateIsWanted { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float Heading { get; set; }
        public bool IsImpounded { get; set; }
        public DateTime DateTimeImpounded { get; set; }
        public int TimesImpounded { get; set; }
        public string ImpoundedLocation { get; set; }
        public int StoredCash { get; set; }
    }
}
