using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Linq;

public class AirportContrabandCheckService
{
    private const float DefaultFindPercentage = 85f;
    private const int ArrestContextWaitYields = 10;

    private readonly ILocationInteractable Player;
    private readonly IModItems ModItems;
    private readonly ISettingsProvideable Settings;
    private readonly ITimeReportable Time;

    public AirportContrabandCheckService(ILocationInteractable player, IModItems modItems, ISettingsProvideable settings, ITimeReportable time)
    {
        Player = player;
        ModItems = modItems;
        Settings = settings;
        Time = time;
    }

    private WorldSettings WorldSettings => Settings?.SettingsManager?.WorldSettings;

    public void RunCommercialFlightCheck()
    {
        WorldSettings worldSettings = WorldSettings;
        if (worldSettings == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check skipped: missing world settings", 3);
            return;
        }
        if (!worldSettings.EnableAirportContrabandChecks)
        {
            EntryPoint.WriteToConsole("Airport contraband check skipped: disabled", 3);
            return;
        }

        List<ItemRiskEntry> itemRisks = GetItemRisks(worldSettings);
        List<WeaponRiskEntry> weaponRisks = GetWeaponRisks(worldSettings);
        List<ContrabandCandidate> candidates = GetContrabandCandidates(itemRisks, weaponRisks);
        int itemCount = itemRisks.Sum(x => x.Amount);
        int weaponCount = weaponRisks.Count;

        if (!candidates.Any())
        {
            EntryPoint.WriteToConsole("Airport contraband check skipped: no contraband", 3);
            return;
        }

        float itemRisk = itemRisks.Sum(x => x.Risk);
        float weaponRisk = weaponRisks.Sum(x => x.Risk);
        float finalChance = Clamp(worldSettings.AirportContrabandBaseChance + itemRisk + weaponRisk, 0f, Math.Max(0f, worldSettings.AirportContrabandMaxChance));
        if (finalChance <= 0f)
        {
            EntryPoint.WriteToConsole($"Airport contraband check skipped: chance <= 0 items={itemCount} itemRisk={itemRisk:0} weapons={weaponCount} weaponRisk={weaponRisk:0} finalChance={finalChance:0}", 3);
            return;
        }

        int roll = RandomItems.MyRand.Next(1, 101);
        bool caught = roll <= finalChance;
        EntryPoint.WriteToConsole($"Airport contraband check: candidates={candidates.Count} items={itemCount} itemRisk={itemRisk:0} weapons={weaponCount} weaponRisk={weaponRisk:0} finalChance={finalChance:0} roll={roll} caught={caught}", 3);

        if (!caught)
        {
            return;
        }

        ContrabandCandidate selectedCandidate = SelectWeightedCandidate(candidates);
        if (selectedCandidate == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check caught player but skipped arrest: no weighted candidate selected", 0);
            return;
        }
        EntryPoint.WriteToConsole($"Airport contraband found: selected={selectedCandidate.DisplayName} category={selectedCandidate.Category} quantity={selectedCandidate.Quantity} weight={selectedCandidate.Weight:0.0} crime={selectedCandidate.CrimeID} topWeights={FormatTopCandidateWeights(candidates)}", 3);

        TriggerProcessedBust(selectedCandidate);
    }

    private List<ItemRiskEntry> GetItemRisks(WorldSettings worldSettings)
    {
        List<ItemRiskEntry> itemRisks = new List<ItemRiskEntry>();
        List<ModItem> illicitItems = Player?.Inventory?.GetIllicitItems();
        if (illicitItems == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check skipped invalid item metadata: inventory unavailable", 3);
            return itemRisks;
        }

        foreach (ModItem item in illicitItems)
        {
            if (item == null)
            {
                EntryPoint.WriteToConsole("Airport contraband check skipped invalid item metadata: null item", 3);
                continue;
            }

            InventoryItem inventoryItem = Player.Inventory.Get(item);
            int amount = Math.Max(1, inventoryItem?.Amount ?? 1);
            float findChance = Clamp(item.PoliceFindDuringPlayerSearchPercentage, 0f, 100f);
            float severityFactor = GetItemSeverityFactor(item);
            float quantityFactor = 1f + Math.Max(0, amount - 1) * Math.Max(0f, worldSettings.AirportContrabandDrugQuantityMultiplier);
            float risk = findChance * Math.Max(0f, worldSettings.AirportContrabandIllicitItemMultiplier) * severityFactor * quantityFactor;
            if (risk <= 0f)
            {
                continue;
            }
            itemRisks.Add(new ItemRiskEntry(item, amount, risk, GetItemContrabandCategory(item), GetItemCrimeID(item)));
        }

        return itemRisks;
    }

    private List<WeaponRiskEntry> GetWeaponRisks(WorldSettings worldSettings)
    {
        List<WeaponRiskEntry> weaponRisks = new List<WeaponRiskEntry>();
        if (Player?.WeaponEquipment == null || Player.Licenses == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check skipped invalid weapon metadata: weapon equipment or licenses unavailable", 3);
            return weaponRisks;
        }

        bool hasCCW = Player.Licenses.HasValidCCWLicense(Time);
        List<WeaponInformation> illegalWeapons = Player.WeaponEquipment.GetIllegalWeapons(hasCCW);
        if (illegalWeapons == null)
        {
            return weaponRisks;
        }

        foreach (WeaponInformation weapon in illegalWeapons)
        {
            if (weapon == null)
            {
                EntryPoint.WriteToConsole("Airport contraband check skipped invalid weapon metadata: null weapon", 3);
                continue;
            }

            float findChance = GetWeaponFindChance(weapon);
            float severityFactor = GetWeaponSeverityFactor(weapon);
            float ammoFactor = GetThrowableAmmoFactor(weapon, worldSettings);
            float risk = findChance * Math.Max(0f, worldSettings.AirportContrabandWeaponMultiplier) * severityFactor * ammoFactor;
            if (risk <= 0f)
            {
                continue;
            }
            weaponRisks.Add(new WeaponRiskEntry(weapon, risk, GetWeaponCrimeID(weapon)));
        }

        return weaponRisks;
    }

    private List<ContrabandCandidate> GetContrabandCandidates(List<ItemRiskEntry> itemRisks, List<WeaponRiskEntry> weaponRisks)
    {
        List<ContrabandCandidate> candidates = new List<ContrabandCandidate>();
        candidates.AddRange(itemRisks.Where(x => x?.Risk > 0f).Select(x => ContrabandCandidate.FromItem(x)));
        candidates.AddRange(weaponRisks.Where(x => x?.Risk > 0f).Select(x => ContrabandCandidate.FromWeapon(x)));
        return candidates;
    }

    private float GetWeaponFindChance(WeaponInformation weapon)
    {
        WeaponItem weaponItem = ModItems?.GetWeapon(weapon.ModelName);
        if (weaponItem == null)
        {
            EntryPoint.WriteToConsole($"Airport contraband check invalid weapon metadata: missing WeaponItem for {weapon.ModelName}, using default {DefaultFindPercentage:0}", 3);
            return DefaultFindPercentage;
        }
        return Clamp(weaponItem.PoliceFindDuringPlayerSearchPercentage, 0f, 100f);
    }

    private float GetWeaponSeverityFactor(WeaponInformation weapon)
    {
        if (weapon.Category == WeaponCategory.Heavy || weapon.WeaponLevel >= 4)
        {
            return 2.0f;
        }
        if (weapon.WeaponLevel >= 3 || weapon.Category == WeaponCategory.AR || weapon.Category == WeaponCategory.LMG || weapon.Category == WeaponCategory.Sniper)
        {
            return 1.5f;
        }
        if (weapon.WeaponLevel >= 2 || weapon.Category == WeaponCategory.SMG || weapon.Category == WeaponCategory.Shotgun || weapon.Category == WeaponCategory.Throwable)
        {
            return 1.25f;
        }
        if (weapon.Category == WeaponCategory.Melee)
        {
            return 0.5f;
        }
        return 1.0f;
    }

    private float GetItemSeverityFactor(ModItem item)
    {
        if (IsDrugCandidate(item))
        {
            return item.ItemSubType == ItemSubType.Narcotic ? 3.0f : 2.0f;
        }
        if (IsArmsSmugglingCandidate(item))
        {
            return 1.75f;
        }
        if (item.ItemSubType == ItemSubType.Paraphernalia)
        {
            return 0.75f;
        }
        return 0.25f;
    }

    private bool IsDrugCandidate(ModItem item)
    {
        ConsumableItem consumableItem = item as ConsumableItem;
        return item != null && (item.ItemType == ItemType.Drugs || item.ItemSubType == ItemSubType.Narcotic || (consumableItem != null && consumableItem.IsIntoxicating));
    }

    private bool IsArmsSmugglingCandidate(ModItem item)
    {
        return item != null && (item.ItemType == ItemType.Smuggling || item.ItemSubType == ItemSubType.Arms);
    }

    private string GetItemContrabandCategory(ModItem item)
    {
        if (IsDrugCandidate(item))
        {
            return item.ItemSubType == ItemSubType.Narcotic ? "Drug" : "Intoxicant";
        }
        if (IsArmsSmugglingCandidate(item))
        {
            return "ArmsSmuggling";
        }
        if (item?.ItemSubType == ItemSubType.Paraphernalia)
        {
            return "Paraphernalia";
        }
        return "GenericIllicitItem";
    }

    private string GetItemCrimeID(ModItem item)
    {
        if (IsDrugCandidate(item))
        {
            return StaticStrings.DrugPossessionCrimeID;
        }
        if (IsArmsSmugglingCandidate(item))
        {
            return StaticStrings.DealingGunsCrimeID;
        }
        return StaticStrings.SuspiciousActivityCrimeID;
    }

    private string GetWeaponCrimeID(WeaponInformation weapon)
    {
        if (weapon == null)
        {
            return "None";
        }
        if (weapon.WeaponLevel >= 4)
        {
            return StaticStrings.TerroristActivityCrimeID;
        }
        if (weapon.WeaponLevel >= 3)
        {
            return StaticStrings.BrandishingHeavyWeaponCrimeID;
        }
        return StaticStrings.BrandishingWeaponCrimeID;
    }

    private float GetThrowableAmmoFactor(WeaponInformation weapon, WorldSettings worldSettings)
    {
        if (weapon.Category != WeaponCategory.Throwable)
        {
            return 1f;
        }

        int ammo = 1;
        try
        {
            if (Player?.Character != null && Player.Character.Exists())
            {
                ammo = NativeFunction.Natives.GET_AMMO_IN_PED_WEAPON<int>(Player.Character, weapon.Hash);
            }
        }
        catch (Exception ex)
        {
            EntryPoint.WriteToConsole($"Airport contraband check invalid weapon metadata: throwable ammo lookup failed for {weapon.ModelName}: {ex.Message}", 3);
        }

        int cap = Math.Max(1, worldSettings.AirportContrabandThrowableAmmoCap);
        return Clamp(ammo, 1, cap);
    }

    private void TriggerProcessedBust(ContrabandCandidate selectedCandidate)
    {
        IPoliceRespondable policeRespondable = Player as IPoliceRespondable;
        if (policeRespondable == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check caught player but skipped arrest: player does not implement IPoliceRespondable", 0);
            return;
        }
        if (!string.Equals(WorldSettings.AirportContrabandBustMode, "Processed", StringComparison.OrdinalIgnoreCase))
        {
            EntryPoint.WriteToConsole($"Airport contraband check unsupported bust mode '{WorldSettings.AirportContrabandBustMode}', using Processed", 1);
        }
        if (policeRespondable.IsBusted || policeRespondable.IsArrested)
        {
            EntryPoint.WriteToConsole("Airport contraband check caught player but skipped arrest: player already busted or arrested", 3);
            return;
        }

        int observedBefore = GetObservedCrimeCount(policeRespondable);
        int reportedBefore = GetReportedCrimeCount(policeRespondable);
        int wantedBefore = policeRespondable.WantedLevel;

        // Airport security is the observing authority; prevent the wanted state from being discarded as unradioed before arrest processing starts.
        policeRespondable.PoliceResponse?.RadioInWanted();
        policeRespondable.PlacePoliceLastSeenPlayer = policeRespondable.Position;

        if (!AddSelectedContrabandViolation(policeRespondable, selectedCandidate))
        {
            if (observedBefore == 0 && reportedBefore == 0 && wantedBefore == 0)
            {
                policeRespondable.PoliceResponse?.Reset();
            }
            EntryPoint.WriteToConsole($"Airport contraband check caught player but skipped arrest: selected violation could not be added selected={selectedCandidate?.DisplayName ?? "None"} crime={selectedCandidate?.CrimeID ?? "None"}", 0);
            return;
        }

        EntryPoint.WriteToConsole($"Airport contraband arrest context added: observedBefore={observedBefore} reportedBefore={reportedBefore} wantedBefore={wantedBefore} observed={GetObservedCrimeCount(policeRespondable)} reported={GetReportedCrimeCount(policeRespondable)} wanted={policeRespondable.WantedLevel}", 3);
        WaitForUsableArrestChargeContext(policeRespondable);
        CrimeEvent arrestCharge = GetHighestPriorityObservedCrime(policeRespondable);
        if (!HasUsableArrestChargeContext(policeRespondable))
        {
            if (observedBefore == 0 && reportedBefore == 0 && wantedBefore == 0)
            {
                policeRespondable.PoliceResponse?.Reset();
            }
            EntryPoint.WriteToConsole($"Airport contraband check caught player but skipped arrest: missing usable charge context observed={GetObservedCrimeCount(policeRespondable)} reported={GetReportedCrimeCount(policeRespondable)} wanted={policeRespondable.WantedLevel}", 0);
            return;
        }

        EntryPoint.WriteToConsole($"Airport contraband arrest charge: crime={arrestCharge?.AssociatedCrime?.ID ?? "None"} name={arrestCharge?.AssociatedCrime?.Name ?? "None"} talkAllowed={arrestCharge?.AssociatedCrime?.CanReleaseOnTalkItOut.ToString() ?? "False"} cleanSearchAllowed={arrestCharge?.AssociatedCrime?.CanReleaseOnCleanSearch.ToString() ?? "False"} citeAllowed={arrestCharge?.AssociatedCrime?.CanReleaseOnCite.ToString() ?? "False"} observed={GetObservedCrimeCount(policeRespondable)} reported={GetReportedCrimeCount(policeRespondable)} wanted={policeRespondable.WantedLevel}", 3);
        policeRespondable.Arrest();
        EntryPoint.WriteToConsole($"Airport contraband check arrest triggered observed={GetObservedCrimeCount(policeRespondable)} reported={GetReportedCrimeCount(policeRespondable)} wanted={policeRespondable.WantedLevel}", 3);
    }

    private bool AddSelectedContrabandViolation(IPoliceRespondable policeRespondable, ContrabandCandidate selectedCandidate)
    {
        if (policeRespondable?.Violations == null || selectedCandidate == null)
        {
            return false;
        }
        if (selectedCandidate.IsWeapon)
        {
            bool hasCCW = Player.Licenses.HasValidCCWLicense(Time);
            return policeRespondable.Violations.WeaponViolations?.AddFoundWeapon(selectedCandidate.WeaponRisk.Weapon, hasCCW) == true;
        }
        if (selectedCandidate.CrimeID == StaticStrings.DrugPossessionCrimeID)
        {
            return policeRespondable.Violations.OtherViolations?.AddFoundIllegalItem() == true;
        }
        policeRespondable.Violations.AddViolatingAndObserved(selectedCandidate.CrimeID);
        return true;
    }

    private void WaitForUsableArrestChargeContext(IPoliceRespondable policeRespondable)
    {
        for (int i = 0; i < ArrestContextWaitYields && !HasUsableArrestChargeContext(policeRespondable); i++)
        {
            GameFiber.Yield();
        }
    }

    private bool HasUsableArrestChargeContext(IPoliceRespondable policeRespondable)
    {
        return policeRespondable?.WantedLevel > 0 && GetHighestPriorityObservedCrime(policeRespondable) != null;
    }

    private CrimeEvent GetHighestPriorityObservedCrime(IPoliceRespondable policeRespondable)
    {
        return policeRespondable?.PoliceResponse?.CrimesObserved?.Where(x => x?.AssociatedCrime != null).OrderBy(x => x.AssociatedCrime.Priority).FirstOrDefault();
    }

    private int GetObservedCrimeCount(IPoliceRespondable policeRespondable)
    {
        return policeRespondable?.PoliceResponse?.CrimesObserved?.Count ?? 0;
    }

    private int GetReportedCrimeCount(IPoliceRespondable policeRespondable)
    {
        return policeRespondable?.PoliceResponse?.CrimesReported?.Count ?? 0;
    }

    private ContrabandCandidate SelectWeightedCandidate(List<ContrabandCandidate> candidates)
    {
        float totalWeight = candidates?.Where(x => x != null && x.Weight > 0f).Sum(x => x.Weight) ?? 0f;
        if (totalWeight <= 0f)
        {
            return null;
        }

        float selectedWeight = (float)RandomItems.MyRand.NextDouble() * totalWeight;
        float currentWeight = 0f;
        foreach (ContrabandCandidate candidate in candidates.Where(x => x != null && x.Weight > 0f))
        {
            currentWeight += candidate.Weight;
            if (currentWeight >= selectedWeight)
            {
                return candidate;
            }
        }
        return candidates.LastOrDefault(x => x != null && x.Weight > 0f);
    }

    private string FormatTopCandidateWeights(List<ContrabandCandidate> candidates)
    {
        return string.Join(",", candidates.Where(x => x != null && x.Weight > 0f).OrderByDescending(x => x.Weight).Take(3).Select(x => $"{x.Name}:{x.Weight:0.0}"));
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }

    private class ItemRiskEntry
    {
        public ItemRiskEntry(ModItem item, int amount, float risk, string category, string crimeID)
        {
            Item = item;
            Amount = amount;
            Risk = risk;
            Category = category;
            CrimeID = crimeID;
        }

        public ModItem Item { get; }
        public int Amount { get; }
        public float Risk { get; }
        public string Category { get; }
        public string CrimeID { get; }
        public string DisplayName => Item == null ? "None" : $"{Item.Name} x{Amount}";
    }

    private class WeaponRiskEntry
    {
        public WeaponRiskEntry(WeaponInformation weapon, float risk, string crimeID)
        {
            Weapon = weapon;
            Risk = risk;
            CrimeID = crimeID;
        }

        public WeaponInformation Weapon { get; }
        public float Risk { get; }
        public string CrimeID { get; }
        public string DisplayName => Weapon == null ? "None" : $"{Weapon.ModelName} level={Weapon.WeaponLevel} category={Weapon.Category}";
    }

    private class ContrabandCandidate
    {
        private ContrabandCandidate()
        {
        }

        public ItemRiskEntry ItemRisk { get; private set; }
        public WeaponRiskEntry WeaponRisk { get; private set; }
        public bool IsWeapon => WeaponRisk != null;
        public string Name { get; private set; }
        public string DisplayName { get; private set; }
        public string Category { get; private set; }
        public int Quantity { get; private set; }
        public float Weight { get; private set; }
        public string CrimeID { get; private set; }

        public static ContrabandCandidate FromItem(ItemRiskEntry itemRisk)
        {
            return new ContrabandCandidate
            {
                ItemRisk = itemRisk,
                Name = itemRisk?.Item?.Name ?? "None",
                DisplayName = itemRisk?.DisplayName ?? "None",
                Category = itemRisk?.Category ?? "Item",
                Quantity = itemRisk?.Amount ?? 0,
                Weight = itemRisk?.Risk ?? 0f,
                CrimeID = itemRisk?.CrimeID ?? "None",
            };
        }

        public static ContrabandCandidate FromWeapon(WeaponRiskEntry weaponRisk)
        {
            return new ContrabandCandidate
            {
                WeaponRisk = weaponRisk,
                Name = weaponRisk?.Weapon?.ModelName ?? "None",
                DisplayName = weaponRisk?.DisplayName ?? "None",
                Category = weaponRisk?.Weapon == null ? "Weapon" : $"Weapon:{weaponRisk.Weapon.Category}",
                Quantity = 1,
                Weight = weaponRisk?.Risk ?? 0f,
                CrimeID = weaponRisk?.CrimeID ?? "None",
            };
        }
    }
}
