using ExtensionsMethods;
using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public class BankAccounts
{
    private IBankAccountHoldable Player;
    private ISettingsProvideable Settings;
    private IPlacesOfInterest PlacesOfInterest;
    private uint GameTimeLastChangedMoney;
    private int money = 0;
    private int currentMoney;
    private bool useCoopLocalCashSource;

    public int LastChangeMoneyAmount { get; set; }
    public bool RecentlyChangedMoney => GameTimeLastChangedMoney != 0 && Game.GameTime - GameTimeLastChangedMoney <= 7000;
    public int TotalAccountMoney => BankAccountList == null || !BankAccountList.Any() ? 0 : BankAccountList.Sum(x => x.Money);
    public int TotalMoney => TotalAccountMoney + Money;
    public List<BankAccount> BankAccountList { get; set; } = new List<BankAccount>();
    private int Money
    {
        get
        {
            int CurrentCash;
            uint PlayerCashHash = GetPlayerCashHash();
            if (ShouldUseNativeCashStat())
            {
                unsafe
                {
                    NativeFunction.CallByName<int>("STAT_GET_INT", PlayerCashHash, &CurrentCash, -1);
                }
                return CurrentCash;
            }
            else
            {
                return money;
            }
        }
    }

    public BankAccounts(IBankAccountHoldable player, ISettingsProvideable settings, IPlacesOfInterest placesOfInterest)
    {
        Player = player;
        Settings = settings;
        PlacesOfInterest = placesOfInterest;
    }
    public void Setup()
    {
        money = Money;
        currentMoney = Money;
    }
    public void Dispose()
    {
        BankAccountList = new List<BankAccount>();
    }
    public void Reset()
    {
        BankAccountList = new List<BankAccount>();
    }
    public void Update()
    {
        if (currentMoney != Money)
        {
            LastChangeMoneyAmount = Money - currentMoney;
            GameTimeLastChangedMoney = Game.GameTime;
            currentMoney = Money;
        }
    }
    public int GetMoney(bool useAccounts)
    {
        if(!useAccounts)
        {
            return Money;
        }
        return Money + TotalAccountMoney;
    }
    public int GetOnHandCashSafe()
    {
        int money = GetMoney(false);
        if (money >= 2147483647)
        {
            money = 2147483646;
        }
        return money;
    }
    public void GiveMoney(int Amount, bool useAccounts)
    {
        bool logDiagnostics = IsCoopMoneyDiagnosticsEnabled();
        int accountCountBefore = logDiagnostics ? BankAccountList?.Count ?? 0 : 0;
        int totalAccountMoneyBefore = logDiagnostics ? TotalAccountMoney : 0;
        int onHandCashBefore = logDiagnostics ? GetMoney(false) : 0;
        int totalMoneyBefore = logDiagnostics ? GetMoney(true) : 0;
        string modelName = logDiagnostics ? Player?.ModelName ?? "<missing>" : string.Empty;
        bool isPrimaryCharacter = logDiagnostics && Player?.CharacterModelIsPrimaryCharacter == true;
        bool aliasPedAsMainCharacter = logDiagnostics && Settings?.SettingsManager?.PedSwapSettings?.AliasPedAsMainCharacter == true;

        if (logDiagnostics)
        {
            EntryPoint.WriteToConsole($"Co-op money diagnostic GiveMoney begin Amount:{Amount} UseAccounts:{useAccounts} AccountCount:{accountCountBefore} AccountMoneyBefore:{totalAccountMoneyBefore} CashBefore:{onHandCashBefore} TotalBefore:{totalMoneyBefore} Model:{modelName} IsPrimary:{isPrimaryCharacter} AliasPedAsMainCharacter:{aliasPedAsMainCharacter}", 5);
        }

        if (Amount != 0)
        {
            LastChangeMoneyAmount = Amount;
            GameTimeLastChangedMoney = Game.GameTime;
        }
        if (useAccounts)
        {
            if (logDiagnostics)
            {
                EntryPoint.WriteToConsole($"GiveMoney ACCOUNT TO REMOVE {Amount}", 5);
            }
            Amount = GiveMoneyAccount(Amount);
            if (logDiagnostics)
            {
                EntryPoint.WriteToConsole($"GiveMoney ACCOUNT STILL TO REMOVE {Amount}", 5);
            }
            if (logDiagnostics)
            {
                EntryPoint.WriteToConsole($"Co-op money diagnostic GiveMoney after accounts RemainingAmount:{Amount} AccountMoneyAfterDebit:{TotalAccountMoney} AccountCount:{BankAccountList?.Count ?? 0}", 5);
            }
        }
 
        //EntryPoint.WriteToConsoleTestLong($"PlayerCashHash {PlayerCashHash} ModelName {Player.ModelName}");
        if (ShouldUseNativeCashStat())
        {
            int currentCashForTarget = GetMoney(false);
            int targetCash = currentCashForTarget + Amount < 0 ? 0 : currentCashForTarget + Amount;
            bool nativeWriteMatched = TrySetNativeCash(targetCash, out int currentCashBeforeWrite, out int currentCashAfterWrite, out int statWriteResult);
            if (logDiagnostics)
            {
                EntryPoint.WriteToConsole($"Co-op money diagnostic GiveMoney stat write Source:STAT Hash:{GetPlayerCashHash()} CashBeforeWrite:{currentCashBeforeWrite} TargetCash:{targetCash} WriteResult:{statWriteResult} CashAfterWrite:{currentCashAfterWrite}", 5);
                if (!nativeWriteMatched)
                {
                    EntryPoint.WriteToConsole($"Co-op purchase money write failed Source:STAT TargetCash:{targetCash} CashAfterWrite:{currentCashAfterWrite}", 5);
                }
            }
        }
        else
        {
            int localCashBeforeWrite = money;
            if (money + Amount < 0)
            {
                money = 0;
            }
            else
            {
                money += Amount;
            }
            if (logDiagnostics)
            {
                EntryPoint.WriteToConsole($"Co-op money diagnostic GiveMoney local write Source:Field CashBeforeWrite:{localCashBeforeWrite} TargetCash:{money} CashAfterWrite:{money}", 5);
            }
        }
        if (logDiagnostics)
        {
            EntryPoint.WriteToConsole($"Co-op money diagnostic GiveMoney end AmountRemainingApplied:{Amount} AccountMoneyBefore:{totalAccountMoneyBefore} AccountMoneyAfter:{TotalAccountMoney} CashBefore:{onHandCashBefore} CashAfter:{GetMoney(false)} TotalBefore:{totalMoneyBefore} TotalAfter:{GetMoney(true)}", 5);
        }
        //currentMoney = Money;
    }
    private int GiveMoneyAccount(int Amount)
    {
        if(Amount > 0)
        {
            BankAccount bankAccount = BankAccountList.OrderBy(x => x.IsPrimary ? 0 : 999).FirstOrDefault();
            if(bankAccount != null)
            {
                bankAccount.Money += Amount;
                return 0;//is positive and has added the money to the account, sent remaining to zero
            }
            return Amount;
        }
        else
        {
            int AccountMoneyTakenAlready = Amount;
            foreach (BankAccount ba in BankAccountList.OrderByDescending(x => x.Money))
            {
                if (IsCoopMoneyDiagnosticsEnabled())
                {
                    EntryPoint.WriteToConsole($"GiveMoneyAccount BEGIN {ba.BankContactName} {ba.Money}", 5);
                }
                if (AccountMoneyTakenAlready + ba.Money < 0)
                {
                    AccountMoneyTakenAlready += ba.Money;
                    ba.Money = 0;
                }
                else
                {
                    ba.Money = ba.Money += AccountMoneyTakenAlready;
                    AccountMoneyTakenAlready = 0;
                }
                if (IsCoopMoneyDiagnosticsEnabled())
                {
                    EntryPoint.WriteToConsole($"GiveMoneyAccount END {ba.BankContactName} {ba.Money}", 5);
                }
            }
            return AccountMoneyTakenAlready;
        }
    }
    public void SetCash(int Amount)
    {
        //EntryPoint.WriteToConsoleTestLong($"PlayerCashHash {PlayerCashHash} ModelName {Player.ModelName}");
        if (ShouldUseNativeCashStat())
        {
            TrySetNativeCash(Amount, out _, out _, out _);
        }
        else
        {
            money = Amount;
        }
        currentMoney = Money;
    }
    public bool TrySetCashForCoopReconciliation(int Amount, out int cashBefore, out int cashAfter, out string result)
    {
        cashBefore = GetMoney(false);
        cashAfter = cashBefore;
        result = string.Empty;

        if (!IsCoopCashReconciliationEnabled())
        {
            result = "Skipped:CoopDisabled";
            return false;
        }

        int targetCash = Amount < 0 ? 0 : Amount;
        if (ShouldUseNativeCashStat())
        {
            bool nativeWriteMatched = TrySetNativeCash(targetCash, out int statCashBefore, out int statCashAfter, out int statWriteResult);
            if (nativeWriteMatched)
            {
                cashAfter = GetMoney(false);
                currentMoney = Money;
                result = $"NativeStatMatched WriteResult:{statWriteResult} StatBefore:{statCashBefore} StatAfter:{statCashAfter}";
                return true;
            }

            useCoopLocalCashSource = true;
            money = targetCash;
            currentMoney = Money;
            cashAfter = GetMoney(false);
            result = $"NativeStatFailed WriteResult:{statWriteResult} StatBefore:{statCashBefore} StatAfter:{statCashAfter} FallbackSource:Field";
            return true;
        }

        money = targetCash;
        currentMoney = Money;
        cashAfter = GetMoney(false);
        result = useCoopLocalCashSource ? "CoopFieldUpdated" : "LocalFieldUpdated";
        return true;
    }
    public string CashDisplay(bool showFull)
    {
        string toReturn = $"${Money}";

        if (showFull)
        {
            if (BankAccountList != null && BankAccountList.Any())
            {
                foreach (BankAccount ba in BankAccountList)
                {
                    toReturn += ba.CashDisplay;
                }
            }
        }
        else
        {
            int totalAccount = TotalAccountMoney;
            if (totalAccount > 0)
            {
                toReturn += $" (${totalAccount})";
            }
        }
        return toReturn;
    }
    public BankAccount GetAccount(string name)
    {
        BankAccount bankAccount = BankAccountList.Where(x => x.BankContactName == name).FirstOrDefault();
        return bankAccount;
    }
    public int GetAccountValue(string name)
    {
        BankAccount ba = GetAccount(name);
        if(ba == null)
        {
            return 0;
        }
        return ba.Money;
    }
    public void WriteToConsole()
    {
        EntryPoint.WriteToConsole("BANK ACCOUNTS---------------");

        EntryPoint.WriteToConsole($"On Hand Cash {Money}");
        foreach(BankAccount bankAccount in BankAccountList)
        {
            EntryPoint.WriteToConsole($"Account: {bankAccount.BankContactName} {bankAccount.Money}");
        }
        EntryPoint.WriteToConsole("BANK ACCOUNTS---------------");
    }
    public void CreateRandomAccount(int amount)
    {
        Bank randomBank = PlacesOfInterest.PossibleLocations.Banks.PickRandom();
        if(randomBank == null)
        {
            EntryPoint.WriteToConsole("CANNOT GIVE RANDOM BANK ACCOUNT, NO BANKS FOUND");
            return;
        }
        BankAccountList.Add(new BankAccount(randomBank.Name,randomBank.ShortName, amount));
    }
    public void CreateNewAccount(Bank bank)
    {
        if(bank == null)
        {
            return;
        }
        if(!BankAccountList.Any(x=> x.BankContactName == bank.Name))
        {
            BankAccountList.Add(new BankAccount(bank.Name, bank.ShortName, 0));
        }
    }

    public void Remove(BankAccount selectedItem)
    {
        if(selectedItem == null)
        {
            return;
        }
        BankAccountList.Remove(selectedItem);
    }

    private bool IsCoopMoneyDiagnosticsEnabled()
    {
        try
        {
            return LosSantosRED.lsr.Coop.Core.CoopStartupBridge.IsCoopEnabled
                && Settings?.SettingsManager?.DebugSettings?.LogCoopPersistenceDiagnostics == true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsCoopCashReconciliationEnabled()
    {
        try
        {
            return LosSantosRED.lsr.Coop.Core.CoopStartupBridge.IsCoopEnabled;
        }
        catch
        {
            return false;
        }
    }

    private bool ShouldUseNativeCashStat()
    {
        return !useCoopLocalCashSource && (Settings.SettingsManager.PedSwapSettings.AliasPedAsMainCharacter || Player.CharacterModelIsPrimaryCharacter);
    }

    private uint GetPlayerCashHash()
    {
        if (Player.CharacterModelIsPrimaryCharacter)
        {
            return NativeHelper.CashHash(Player.ModelName.ToLower());
        }

        return NativeHelper.CashHash(Settings.SettingsManager.PedSwapSettings.MainCharacterToAlias);
    }

    private bool TrySetNativeCash(int amount, out int cashBefore, out int cashAfter, out int writeResult)
    {
        cashBefore = 0;
        cashAfter = 0;
        writeResult = 0;
        int nativeCashBefore = 0;
        int nativeCashAfter = 0;
        uint playerCashHash = GetPlayerCashHash();
        unsafe
        {
            NativeFunction.CallByName<int>("STAT_GET_INT", playerCashHash, &nativeCashBefore, -1);
        }

        writeResult = NativeFunction.CallByName<int>("STAT_SET_INT", playerCashHash, amount, 1);

        unsafe
        {
            NativeFunction.CallByName<int>("STAT_GET_INT", playerCashHash, &nativeCashAfter, -1);
        }

        cashBefore = nativeCashBefore;
        cashAfter = nativeCashAfter;
        return cashAfter == amount;
    }
}

