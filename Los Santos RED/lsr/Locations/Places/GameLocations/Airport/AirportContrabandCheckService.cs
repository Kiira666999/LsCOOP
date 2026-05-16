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
        int itemCount = itemRisks.Sum(x => x.Amount);
        int weaponCount = weaponRisks.Count;

        if (itemCount <= 0 && weaponCount <= 0)
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
        EntryPoint.WriteToConsole($"Airport contraband check: items={itemCount} itemRisk={itemRisk:0} weapons={weaponCount} weaponRisk={weaponRisk:0} finalChance={finalChance:0} roll={roll} caught={caught}", 3);

        if (!caught)
        {
            return;
        }

        TriggerProcessedBust(itemRisks, weaponRisks);
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
            float quantityFactor = 1f + Math.Max(0, amount - 1) * Math.Max(0f, worldSettings.AirportContrabandDrugQuantityMultiplier);
            float risk = findChance * Math.Max(0f, worldSettings.AirportContrabandIllicitItemMultiplier) * quantityFactor;
            if (risk <= 0f)
            {
                continue;
            }
            itemRisks.Add(new ItemRiskEntry(item, amount, risk));
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
            weaponRisks.Add(new WeaponRiskEntry(weapon, risk));
        }

        return weaponRisks;
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

    private void TriggerProcessedBust(List<ItemRiskEntry> itemRisks, List<WeaponRiskEntry> weaponRisks)
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

        ItemRiskEntry topItem = itemRisks.OrderByDescending(x => x.Risk).FirstOrDefault();
        WeaponRiskEntry topWeapon = weaponRisks.OrderByDescending(x => x.Risk).ThenByDescending(x => x.Weapon.WeaponLevel).FirstOrDefault();
        EntryPoint.WriteToConsole($"Airport contraband check caught: topItem={topItem?.DisplayName ?? "None"} topWeapon={topWeapon?.DisplayName ?? "None"}", 3);

        if (itemRisks.Any())
        {
            policeRespondable.Violations?.OtherViolations?.AddFoundIllegalItem();
        }
        if (topWeapon != null)
        {
            bool hasCCW = Player.Licenses.HasValidCCWLicense(Time);
            policeRespondable.Violations?.WeaponViolations?.AddFoundWeapon(topWeapon.Weapon, hasCCW);
        }

        policeRespondable.Arrest();
        EntryPoint.WriteToConsole("Airport contraband check arrest triggered", 3);
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
        public ItemRiskEntry(ModItem item, int amount, float risk)
        {
            Item = item;
            Amount = amount;
            Risk = risk;
        }

        public ModItem Item { get; }
        public int Amount { get; }
        public float Risk { get; }
        public string DisplayName => Item == null ? "None" : $"{Item.Name} x{Amount}";
    }

    private class WeaponRiskEntry
    {
        public WeaponRiskEntry(WeaponInformation weapon, float risk)
        {
            Weapon = weapon;
            Risk = risk;
        }

        public WeaponInformation Weapon { get; }
        public float Risk { get; }
        public string DisplayName => Weapon == null ? "None" : $"{Weapon.ModelName} level={Weapon.WeaponLevel} category={Weapon.Category}";
    }
}
