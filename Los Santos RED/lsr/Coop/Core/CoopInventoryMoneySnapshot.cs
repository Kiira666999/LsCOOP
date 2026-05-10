using System;
using System.Collections.Generic;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopInventoryMoneySnapshot
    {
        public CoopInventoryMoneySnapshot()
        {
            InventoryItems = new List<CoopInventoryItemState>();
            BankAccounts = new List<CoopBankAccountState>();
            SnapshotUtc = DateTimeOffset.UtcNow;
        }

        public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
        public CoopWorldId WorldId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public CoopCharacterId CharacterId { get; set; }
        public int OnHandCash { get; set; }
        public int TotalAccountMoney { get; set; }
        public int TotalMoney => OnHandCash + TotalAccountMoney;
        public List<CoopInventoryItemState> InventoryItems { get; private set; }
        public List<CoopBankAccountState> BankAccounts { get; private set; }
        public DateTimeOffset SnapshotUtc { get; set; }
    }
}
