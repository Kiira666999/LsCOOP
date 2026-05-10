using System;

namespace LsrCoop.Client
{
    public class CoopPropertyOwnershipRecord
    {
        public string PropertyId { get; set; }
        public string Name { get; set; }
        public string PropertyType { get; set; }
        public bool IsOwned { get; set; }
        public bool IsRented { get; set; }
        public bool IsRentedOut { get; set; }
        public float EntranceX { get; set; }
        public float EntranceY { get; set; }
        public float EntranceZ { get; set; }
        public int CurrentSalesPrice { get; set; }
        public DateTime PayoutDate { get; set; }
        public DateTime DateOfLastPayout { get; set; }
        public DateTime RentalPaymentDate { get; set; }
        public DateTime DateOfLastRentalPayment { get; set; }
    }
}
