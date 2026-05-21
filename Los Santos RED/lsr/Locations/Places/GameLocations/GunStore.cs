using LosSantosRED.lsr.Interface;
using Mod;
using Rage;
using RAGENativeUI.Elements;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

public class GunStore : GameLocation
{
    private UIMenuItem completeTask;
    public GunStore() : base()
    {

    }

    public List<SpawnPlace> ParkingSpaces = new List<SpawnPlace>();
    public override bool ShowsOnDirectory { get; set; } = false;
    public override string TypeName { get; set; } = "Gun Store";
    public override int MapIcon { get; set; } = (int)BlipSprite.AmmuNation;
    public override string ButtonPromptText { get; set; }
    public int MoneyToUnlock { get; set; } = 0;
    public string ContactName { get; set; } = "";
    public bool RequiresCCWLicense { get; set; } = false;
    public float LegalWeaponPriceScalar { get; set; } = 1.2f;
    public override int RegisterCashMin { get; set; } = 1000;
    public override int RegisterCashMax { get; set; } = 2550;
    [XmlIgnore]
    public PhoneContact PhoneContact { get; set; }
    private bool IsLegalAmmunation => MenuID == "AmmunationMenu" || TypeName == "Ammu-Nation";
    private bool RequiresValidCCWLicense => RequiresCCWLicense || IsLegalAmmunation;

    public GunStore(Vector3 _EntrancePosition, float _EntranceHeading, string _Name, string _Description, string menuID) : base(_EntrancePosition, _EntranceHeading, _Name, _Description)
    {
        MenuID = menuID;
        ButtonPromptText = $"Shop at {Name}";
    }
    public override bool CanCurrentlyInteract(ILocationInteractable player)
    {
        ButtonPromptText = RequiresValidCCWLicense && !HasValidCCWLicense() ? "Requires valid CCW license" : $"Shop At {Name}";
        return true;
    }
    public override void StoreData(IShopMenus shopMenus, IAgencies agencies, IGangs gangs, IZones zones, IJurisdictions jurisdictions, IGangTerritories gangTerritories, INameProvideable names, ICrimes crimes, 
        IPedGroups PedGroups, IEntityProvideable world, IStreets streets, ILocationTypes locationTypes, ISettingsProvideable settings, IPlateTypes plateTypes, IOrganizations associations, IContacts contacts, IInteriors interiors,
        ILocationInteractable player, IModItems modItems, IWeapons weapons, ITimeControllable time, IPlacesOfInterest placesOfInterest, IIssuableWeapons issuableWeapons, IHeads heads, IDispatchablePeople dispatchablePeople, ModDataFileManager modDataFileManager)
    {
        PhoneContact = contacts.GetContactData(ContactName);
        base.StoreData(shopMenus, agencies, gangs, zones, jurisdictions, gangTerritories, names, crimes, PedGroups, world, streets, locationTypes, settings, plateTypes, associations, contacts, interiors, player, modItems, weapons, time, placesOfInterest, issuableWeapons, heads, dispatchablePeople, modDataFileManager);
        if (IsLegalAmmunation)
        {
            SetupLegalAmmunationMenu();
        }
    }
    public override void OnInteract()//ILocationInteractable player, IModItems modItems, IEntityProvideable world, ISettingsProvideable settings, IWeapons weapons, ITimeControllable time, IPlacesOfInterest placesOfInterest)
    {
        //Player = player;
        //ModItems = modItems;
        //World = world;
        //Settings = settings;
        //Weapons = weapons;
        //Time = time;

        if (IsLocationClosed())
        {
            return;
        }
        if (!CanInteract)
        {
            return;
        }
        if (Interior != null && Interior.IsTeleportEntry)
        {
            DoEntranceCamera(false);
            Interior.Teleport(Player, this, StoreCamera);
        }
        else
        {
            StandardInteract(null, false);
        }
    }
    protected override bool ShouldSpawnVendor() => PhoneContact == null || !Player.RelationshipManager.GetOrCreate(PhoneContact).IsHostile;
    protected override bool IsLocationClosed()
    {
        if (RequiresValidCCWLicense && !HasValidCCWLicense())
        {
            Game.DisplayHelp("A valid CCW license is required to shop here.");
            return true;
        }
        if (PhoneContact != null && Player.RelationshipManager.GetOrCreate(PhoneContact).IsHostile)
        {
            Game.DisplayHelp("Increase your reputation to access.");
            return true;
        }
        return base.IsLocationClosed();
    }
    private bool HasValidCCWLicense()
    {
        return Player?.Licenses?.HasValidCCWLicense(Time) == true;
    }
    private void SetupLegalAmmunationMenu()
    {
        if (Menu == null || Menu.Items == null || Weapons == null || ModItems == null)
        {
            return;
        }
        ShopMenu originalMenu = Menu;
        List<MenuItem> nonWeaponItems = originalMenu.Items
            .Where(IsRetainedLegalAmmunationBaseItem)
            .Select(x => x.Copy())
            .ToList();
        List<MenuItem> bodyArmorItems = ModItems.AllItems()
            .OfType<BodyArmorItem>()
            .Where(x => !nonWeaponItems.Any(y => y.ModItemName == x.Name))
            .Select(x => CreateLegalAmmunationBodyArmorItem(x, originalMenu))
            .Where(x => x != null)
            .OrderBy(x => x.PurchasePrice)
            .ThenBy(x => x.ModItemName)
            .ToList();
        List<MenuItem> legalWeaponItems = Weapons.GetAllWeapons()
            .Where(x => x.IsLegal && x.IsRegular && x.Hash != 0)
            .Select(x => CreateLegalAmmunationMenuItem(x, originalMenu))
            .Where(x => x != null)
            .OrderBy(x => x.ModItem?.ModelItem == null ? "Weapon" : x.ModItem.MenuCategory)
            .ThenBy(x => x.PurchasePrice)
            .ThenBy(x => x.ModItemName)
            .ToList();

        Menu = originalMenu.Copy();
        Menu.Items = new List<MenuItem>();
        Menu.Items.AddRange(nonWeaponItems);
        Menu.Items.AddRange(bodyArmorItems);
        Menu.Items.AddRange(legalWeaponItems);
    }
    private bool IsRetainedLegalAmmunationBaseItem(MenuItem menuItem)
    {
        ModItem modItem = ModItems.Get(menuItem.ModItemName);
        return modItem?.ModelItem?.Type != ePhysicalItemType.Weapon && !(modItem is BodyArmorItem);
    }
    private MenuItem CreateLegalAmmunationMenuItem(WeaponInformation weaponInfo, ShopMenu originalMenu)
    {
        WeaponItem weaponItem = ModItems.GetWeapon(weaponInfo.ModelName);
        if (weaponItem == null)
        {
            return null;
        }
        MenuItem sourceItem = originalMenu.Items.FirstOrDefault(x => x.ModItemName == weaponItem.Name);
        MenuItem menuItem = sourceItem == null ? new MenuItem(weaponItem.Name) : sourceItem.Copy();
        menuItem.ModItemName = weaponItem.Name;
        menuItem.PurchasePrice = GetLegalAmmunationPrice(weaponItem, weaponInfo);
        menuItem.SalesPrice = -1;
        menuItem.IsIllicilt = false;
        menuItem.NumberOfItemsToSellToPlayer = -1;
        menuItem.NumberOfItemsToPurchaseFromPlayer = -1;
        menuItem.NumberOfItemsSoldToPlayer = 0;
        menuItem.NumberOfItemsPurchasedByPlayer = 0;
        menuItem.ModItem = weaponItem;
        if (menuItem.Extras == null)
        {
            menuItem.Extras = new List<MenuItemExtra>();
        }
        foreach (MenuItemExtra extra in menuItem.Extras.Where(x => x.PurchasePrice > 0))
        {
            extra.PurchasePrice = Math.Max(1, (int)Math.Round(extra.PurchasePrice * LegalPriceScalar));
        }
        return menuItem;
    }
    private MenuItem CreateLegalAmmunationBodyArmorItem(BodyArmorItem armorItem, ShopMenu originalMenu)
    {
        if (armorItem == null)
        {
            return null;
        }
        MenuItem sourceItem = originalMenu.Items.FirstOrDefault(x => x.ModItemName == armorItem.Name);
        MenuItem menuItem = sourceItem == null ? new MenuItem(armorItem.Name) : sourceItem.Copy();
        menuItem.ModItemName = armorItem.Name;
        menuItem.PurchasePrice = GetLegalAmmunationPrice(armorItem);
        menuItem.SalesPrice = -1;
        menuItem.IsIllicilt = false;
        menuItem.NumberOfItemsToSellToPlayer = -1;
        menuItem.NumberOfItemsToPurchaseFromPlayer = -1;
        menuItem.NumberOfItemsSoldToPlayer = 0;
        menuItem.NumberOfItemsPurchasedByPlayer = 0;
        menuItem.ModItem = armorItem;
        return menuItem;
    }
    private int GetLegalAmmunationPrice(WeaponItem weaponItem, WeaponInformation weaponInfo)
    {
        int fallbackPrice = GetFallbackLegalWeaponPrice(weaponInfo);
        Tuple<int, int> existingPriceRange = ShopMenus?.GetPrices(weaponItem.Name);
        if (existingPriceRange != null && existingPriceRange.Item1 > 0 && existingPriceRange.Item1 < 9999)
        {
            int scaledPrice = (int)Math.Round(existingPriceRange.Item1 * LegalPriceScalar);
            return Math.Max(1, Math.Min(fallbackPrice, scaledPrice));
        }
        return fallbackPrice;
    }
    private int GetLegalAmmunationPrice(BodyArmorItem armorItem)
    {
        Tuple<int, int> existingPriceRange = ShopMenus?.GetPrices(armorItem.Name);
        if (existingPriceRange != null && existingPriceRange.Item1 > 0 && existingPriceRange.Item1 < 9999)
        {
            return existingPriceRange.Item1;
        }
        return GetFallbackBodyArmorPrice(armorItem);
    }
    private float LegalPriceScalar => LegalWeaponPriceScalar > 0.0f ? LegalWeaponPriceScalar : 1.2f;
    private int GetFallbackBodyArmorPrice(BodyArmorItem armorItem)
    {
        if (armorItem.ArmorChangeAmount <= 50)
        {
            return 650;
        }
        if (armorItem.ArmorChangeAmount <= 100)
        {
            return 1250;
        }
        if (armorItem.ArmorChangeAmount <= 150)
        {
            return 1500;
        }
        return 2000;
    }
    private int GetFallbackLegalWeaponPrice(WeaponInformation weaponInfo)
    {
        int weaponLevel = Math.Max(0, weaponInfo.WeaponLevel);
        switch (weaponInfo.Category)
        {
            case WeaponCategory.Melee:
                return Math.Max(50, 150 + (weaponLevel * 50));
            case WeaponCategory.Pistol:
                return 900 + (weaponLevel * 300);
            case WeaponCategory.Shotgun:
                return 1500 + (weaponLevel * 500);
            case WeaponCategory.SMG:
                return 1700 + (weaponLevel * 600);
            case WeaponCategory.AR:
                return 2400 + (weaponLevel * 800);
            case WeaponCategory.LMG:
                return 3600 + (weaponLevel * 1000);
            case WeaponCategory.Sniper:
                return 4400 + (weaponLevel * 1200);
            case WeaponCategory.Heavy:
                return 6000 + (weaponLevel * 1500);
            case WeaponCategory.Throwable:
                return 100 + (weaponLevel * 100);
            default:
                return 200;
        }
    }
    public override void StandardInteract(LocationCamera locationCamera, bool isInside)
    {
        Player.ActivityManager.IsInteractingWithLocation = true;
        CanInteract = false;
        Player.IsTransacting = true;
        GameFiber.StartNew(delegate
        {
            try
            {
                SetupLocationCamera(locationCamera, isInside, false);
                CreateInteractionMenu();
                HandleVariableItems();
                Transaction = new Transaction(MenuPool, InteractionMenu, Menu, this);
                Transaction.UseAccounts = false;
                Transaction.CreateTransactionMenu(Player, ModItems, World, Settings, Weapons, Time);
                InteractionMenu.Visible = true;
                Transaction.ProcessTransactionMenu();
                Player.RelationshipManager.OnInteracted(PhoneContact, Transaction.MoneySpent, (Transaction.MoneySpent) / 5);
                Transaction.DisposeTransactionMenu();
                DisposeInteractionMenu();
                DisposeCamera(isInside);
                DisposeInterior();
                Player.ActivityManager.IsInteractingWithLocation = false;
                Player.IsTransacting = false;
                CanInteract = true;
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole("Location Interaction" + ex.Message + " " + ex.StackTrace, 0);
                EntryPoint.ModController.CrashUnload();
            }
        }, "GangDenInteract");
    }
    public override void AddDistanceOffset(Vector3 offsetToAdd)
    {
        foreach (SpawnPlace sp in ParkingSpaces)
        {
            sp.AddDistanceOffset(offsetToAdd);
        }
        base.AddDistanceOffset(offsetToAdd);
    }
    public override void OnVendorKilledByPlayer(Merchant merchant, IViolateable player, IZones zones, IGangTerritories gangTerritories)
    {
        player.RelationshipManager.OnVendorKilledByPlayer(PhoneContact, merchant, player, zones, gangTerritories);
        base.OnVendorKilledByPlayer(merchant, player, zones, gangTerritories);
    }
    public override void OnVendorInjuredByPlayer(Merchant merchant, IViolateable player, IZones zones, IGangTerritories gangTerritories)
    {
        player.RelationshipManager.OnVendorInjuredByPlayer(PhoneContact, merchant, player, zones, gangTerritories);
        base.OnVendorInjuredByPlayer(merchant, player, zones, gangTerritories);
    }
    public override void AddLocation(PossibleLocations possibleLocations)
    {
        possibleLocations.GunStores.Add(this);
        base.AddLocation(possibleLocations);
    }
}

