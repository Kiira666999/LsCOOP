using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopInventoryMoneySnapshot
    {
        public string SnapshotId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public int OnHandCash { get; set; }
        public int TotalAccountMoney { get; set; }
        public int TotalMoney { get; set; }
        public List<CoopInventoryItemState> InventoryItems { get; set; } = new List<CoopInventoryItemState>();
        public List<CoopBankAccountState> BankAccounts { get; set; } = new List<CoopBankAccountState>();
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
