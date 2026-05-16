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
    private const float SearchActivityCopDistance = 20f;
    private const float SearchActivityCopHeight = 5f;
    private const uint AirportContrabandIncidentMaxLifetimeMs = 300000;
    private const uint AirportContrabandIncidentResolvedCleanupDelayMs = 10000;
    private const uint AirportContrabandIncidentFarCleanupDelayMs = 60000;

    private readonly ILocationInteractable Player;
    private readonly IModItems ModItems;
    private readonly ISettingsProvideable Settings;
    private readonly ITimeReportable Time;
    private readonly Airport Airport;
    private readonly IEntityProvideable World;
    private readonly IAgencies Agencies;
    private readonly IWeapons Weapons;
    private readonly INameProvideable Names;
    private readonly IShopMenus ShopMenus;
    private readonly ICrimes Crimes;
    private readonly List<Cop> SpawnedAirportCops = new List<Cop>();

    public AirportContrabandCheckService(ILocationInteractable player, IModItems modItems, ISettingsProvideable settings, ITimeReportable time, Airport airport, IEntityProvideable world, IAgencies agencies, IWeapons weapons, INameProvideable names, IShopMenus shopMenus, ICrimes crimes)
    {
        Player = player;
        ModItems = modItems;
        Settings = settings;
        Time = time;
        Airport = airport;
        World = world;
        Agencies = agencies;
        Weapons = weapons;
        Names = names;
        ShopMenus = shopMenus;
        Crimes = crimes;
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

        TriggerBust(selectedCandidate);
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

    private void TriggerBust(ContrabandCandidate selectedCandidate)
    {
        string bustMode = WorldSettings?.AirportContrabandBustMode;
        if (string.Equals(bustMode, "Processed", StringComparison.OrdinalIgnoreCase))
        {
            TriggerProcessedBust(selectedCandidate, "configured processed mode", false);
            return;
        }
        if (!string.IsNullOrWhiteSpace(bustMode) && !string.Equals(bustMode, "SpawnSecurity", StringComparison.OrdinalIgnoreCase))
        {
            EntryPoint.WriteToConsole($"Airport contraband check unsupported bust mode '{bustMode}', using SpawnSecurity", 1);
        }
        TriggerSpawnSecurityBust(selectedCandidate);
    }

    private void TriggerSpawnSecurityBust(ContrabandCandidate selectedCandidate)
    {
        IPoliceRespondable policeRespondable = Player as IPoliceRespondable;
        if (policeRespondable == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check caught player but skipped arrest: player does not implement IPoliceRespondable", 0);
            return;
        }
        if (policeRespondable.IsBusted || policeRespondable.IsArrested)
        {
            EntryPoint.WriteToConsole("Airport contraband check caught player but skipped arrest: player already busted or arrested", 3);
            return;
        }

        bool requireCop = WorldSettings?.AirportContrabandRequireCopBeforeBustedMenu != false;
        Cop searchCop = EnsureNearbyAirportCop(policeRespondable);
        if (requireCop && searchCop == null)
        {
            HandleAirportSecuritySpawnFailure(selectedCandidate);
            return;
        }

        EntryPoint.WriteToConsole($"Airport contraband security context ready: airport={Airport?.AirportID ?? "Unknown"} cop={searchCop?.Handle.ToString() ?? "None"} agency={searchCop?.AssignedAgency?.ID ?? "None"} spawned={SpawnedAirportCops.Count}", 3);
        TriggerProcessedBust(selectedCandidate, "airport security spawn", requireCop);
    }

    private void HandleAirportSecuritySpawnFailure(ContrabandCandidate selectedCandidate)
    {
        EntryPoint.WriteToConsole($"Airport contraband check caught player but no valid airport police context was available: airport={Airport?.AirportID ?? "Unknown"} spawned={SpawnedAirportCops.Count}", 0);
        CleanupSpawnedAirportCops(false);
        if (WorldSettings?.AirportContrabandSkipIfSecuritySpawnFails == true)
        {
            EntryPoint.WriteToConsole("Airport contraband check skipped bust after airport police spawn failure", 1);
            return;
        }
        if (WorldSettings?.AirportContrabandFallbackToProcessedIfSecurityFails == true)
        {
            EntryPoint.WriteToConsole("Airport contraband check falling back to processed bust after airport police spawn failure", 1);
            TriggerProcessedBust(selectedCandidate, "airport security fallback", false);
            return;
        }
        EntryPoint.WriteToConsole("Airport contraband check skipped bust after airport police spawn failure and no fallback was enabled", 1);
    }

    private void TriggerProcessedBust(ContrabandCandidate selectedCandidate, string source, bool requireNearbyCop)
    {
        IPoliceRespondable policeRespondable = Player as IPoliceRespondable;
        if (policeRespondable == null)
        {
            EntryPoint.WriteToConsole("Airport contraband check caught player but skipped arrest: player does not implement IPoliceRespondable", 0);
            return;
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
        if (requireNearbyCop && GetValidNearbySearchCop(policeRespondable) == null)
        {
            if (observedBefore == 0 && reportedBefore == 0 && wantedBefore == 0)
            {
                policeRespondable.PoliceResponse?.Reset();
            }
            EntryPoint.WriteToConsole($"Airport contraband check caught player but skipped arrest: nearby airport police context was lost before busted menu source={source}", 0);
            CleanupSpawnedAirportCops(false);
            return;
        }
        policeRespondable.Arrest();
        StartAirportContrabandIncidentCleanup(policeRespondable);
        EntryPoint.WriteToConsole($"Airport contraband check arrest triggered source={source} observed={GetObservedCrimeCount(policeRespondable)} reported={GetReportedCrimeCount(policeRespondable)} wanted={policeRespondable.WantedLevel}", 3);
    }

    private Cop EnsureNearbyAirportCop(IPoliceRespondable policeRespondable)
    {
        UpdateNearbyAirportCops(policeRespondable);
        Cop existingCop = GetValidNearbySearchCop(policeRespondable);
        if (existingCop != null)
        {
            EntryPoint.WriteToConsole($"Airport contraband check using existing nearby cop: handle={existingCop.Handle} agency={existingCop.AssignedAgency?.ID ?? "None"}", 3);
            return existingCop;
        }

        SpawnAirportCops(policeRespondable);
        uint started = Game.GameTime;
        uint timeout = WorldSettings?.AirportContrabandSecuritySpawnTimeoutMs ?? 5000;
        while (EntryPoint.ModController.IsRunning && Game.GameTime - started <= timeout)
        {
            UpdateNearbyAirportCops(policeRespondable);
            existingCop = GetValidNearbySearchCop(policeRespondable);
            if (existingCop != null)
            {
                return existingCop;
            }
            GameFiber.Yield();
        }
        return null;
    }

    private void SpawnAirportCops(IPoliceRespondable policeRespondable)
    {
        if (World?.Pedestrians == null || Agencies == null || Weapons == null || Names == null || ShopMenus == null || Crimes == null)
        {
            EntryPoint.WriteToConsole("Airport contraband security spawn skipped: missing world or agency dependencies", 0);
            return;
        }

        List<Agency> agencies = GetPreferredAirportAgencies(policeRespondable);
        if (!agencies.Any())
        {
            EntryPoint.WriteToConsole("Airport contraband security spawn skipped: no law enforcement agencies available", 0);
            return;
        }

        int spawnCount = GetAirportSecuritySpawnCount();
        List<SpawnLocation> spawnLocations = GetAirportSecuritySpawnLocations(spawnCount);
        for (int i = 0; i < spawnCount; i++)
        {
            SpawnLocation spawnLocation = spawnLocations[i % spawnLocations.Count];
            Cop spawnedCop = null;
            foreach (Agency agency in agencies)
            {
                spawnedCop = TrySpawnAirportCop(agency, spawnLocation);
                if (spawnedCop != null)
                {
                    break;
                }
            }
            if (spawnedCop == null)
            {
                EntryPoint.WriteToConsole($"Airport contraband security failed to spawn cop index={i} airport={Airport?.AirportID ?? "Unknown"}", 1);
            }
            GameFiber.Yield();
        }
    }

    private Cop TrySpawnAirportCop(Agency agency, SpawnLocation spawnLocation)
    {
        if (agency == null || spawnLocation == null)
        {
            return null;
        }

        DispatchablePerson personType = null;
        try
        {
            personType = agency.GetRandomPed(0, "");
        }
        catch (Exception ex)
        {
            EntryPoint.WriteToConsole($"Airport contraband security failed to pick agency ped: agency={agency.ID} error={ex.Message}", 1);
        }
        if (personType == null)
        {
            return null;
        }

        LESpawnTask spawnTask = new LESpawnTask(agency, spawnLocation, null, personType, Settings?.SettingsManager?.PoliceSettings?.AttachBlipsToAmbientPeds == true, Settings, Weapons, Names, false, World, ModItems, false, ShopMenus, Crimes)
        {
            AllowAnySpawn = true,
            AllowBuddySpawn = false,
            PlacePedOnGround = true,
            DoPersistantEntityCheck = false,
            ClearVehicleArea = false,
        };
        spawnTask.AttemptSpawn();
        Cop spawnedCop = spawnTask.SpawnedCops.FirstOrDefault(x => x?.Pedestrian.Exists() == true);
        if (spawnedCop == null)
        {
            return null;
        }

        spawnedCop.IsLocationSpawned = false;
        spawnedCop.CanBeTasked = true;
        spawnedCop.CanBeAmbientTasked = true;
        spawnedCop.Pedestrian.IsPersistent = true;
        if (!SpawnedAirportCops.Any(x => x.Handle == spawnedCop.Handle))
        {
            SpawnedAirportCops.Add(spawnedCop);
        }
        EntryPoint.WriteToConsole($"Airport contraband security spawned cop: airport={Airport?.AirportID ?? "Unknown"} handle={spawnedCop.Handle} agency={agency.ID} position={spawnedCop.Pedestrian.Position}", 3);
        return spawnedCop;
    }

    private int GetAirportSecuritySpawnCount()
    {
        int min = Math.Max(1, WorldSettings?.AirportContrabandSecuritySpawnCountMin ?? 2);
        int max = Math.Max(min, WorldSettings?.AirportContrabandSecuritySpawnCountMax ?? 4);
        max = Math.Min(max, 8);
        return RandomItems.MyRand.Next(min, max + 1);
    }

    private List<Agency> GetPreferredAirportAgencies(IPoliceRespondable policeRespondable)
    {
        List<string> agencyIDs = new List<string>();
        string airportID = Airport?.AirportID?.ToUpperInvariant() ?? "";
        if (airportID == "LSIX")
        {
            AddAgencyID(agencyIDs, "LSIAPD");
        }
        else if (airportID == "CPA")
        {
            AddAgencyID(agencyIDs, "CPAS");
            AddAgencyID(agencyIDs, "CPPD");
        }

        AddAgencyID(agencyIDs, Airport?.AssignedAgency?.ID);
        AddAgencyID(agencyIDs, policeRespondable?.CurrentLocation?.CurrentZone?.AssignedLEAgency?.ID);
        AddAgencyID(agencyIDs, policeRespondable?.CurrentLocation?.CurrentZone?.AssignedSecondLEAgeny?.ID);

        if (airportID == "LSIX")
        {
            AddAgencyID(agencyIDs, "LSPD");
            AddAgencyID(agencyIDs, "LSSD");
        }
        else if (airportID == "CPA")
        {
            AddAgencyID(agencyIDs, "CPPD");
            AddAgencyID(agencyIDs, "CPAS");
        }
        else
        {
            AddAgencyID(agencyIDs, "LSSD-BC");
            AddAgencyID(agencyIDs, "LSSD");
            AddAgencyID(agencyIDs, "LSPD");
            AddAgencyID(agencyIDs, "SAHP");
            AddAgencyID(agencyIDs, "NYSP");
        }

        List<Agency> agencies = new List<Agency>();
        foreach (string agencyID in agencyIDs)
        {
            Agency agency = Agencies.GetAgency(agencyID);
            if (agency != null && !agencies.Any(x => x.ID == agency.ID))
            {
                agencies.Add(agency);
            }
        }
        if (!agencies.Any())
        {
            agencies.AddRange(Agencies.GetAgenciesByResponse(ResponseType.LawEnforcement).Where(x => x != null));
        }
        return agencies;
    }

    private void AddAgencyID(List<string> agencyIDs, string agencyID)
    {
        if (!string.IsNullOrWhiteSpace(agencyID) && !agencyIDs.Contains(agencyID))
        {
            agencyIDs.Add(agencyID);
        }
    }

    private List<SpawnLocation> GetAirportSecuritySpawnLocations(int spawnCount)
    {
        List<AirportSecuritySpawnPoint> spawnPoints = GetFixedAirportSecuritySpawnPoints();
        while (spawnPoints.Count < spawnCount)
        {
            spawnPoints.Add(GetGeneratedAirportSecuritySpawnPoint(spawnPoints.Count, spawnCount));
        }
        return spawnPoints.Take(spawnCount).Select(x => new SpawnLocation(x.Position) { SidewalkPosition = x.Position, Heading = x.Heading }).ToList();
    }

    private List<AirportSecuritySpawnPoint> GetFixedAirportSecuritySpawnPoints()
    {
        string airportID = Airport?.AirportID?.ToUpperInvariant() ?? "";
        if (airportID == "LSIX")
        {
            return new List<AirportSecuritySpawnPoint>
            {
                new AirportSecuritySpawnPoint(new Vector3(-1046.1f, -2743.8f, 21.36f), 235f),
                new AirportSecuritySpawnPoint(new Vector3(-1039.7f, -2748.7f, 21.36f), 55f),
                new AirportSecuritySpawnPoint(new Vector3(-1048.0f, -2748.4f, 21.36f), 305f),
                new AirportSecuritySpawnPoint(new Vector3(-1039.3f, -2742.2f, 21.36f), 140f),
            };
        }
        if (airportID == "CPA")
        {
            return new List<AirportSecuritySpawnPoint>
            {
                new AirportSecuritySpawnPoint(new Vector3(4521.5f, -4500.0f, 4.24f), 300f),
                new AirportSecuritySpawnPoint(new Vector3(4527.0f, -4499.2f, 4.24f), 235f),
                new AirportSecuritySpawnPoint(new Vector3(4522.5f, -4494.8f, 4.24f), 160f),
                new AirportSecuritySpawnPoint(new Vector3(4528.0f, -4495.5f, 4.24f), 210f),
            };
        }
        return new List<AirportSecuritySpawnPoint>();
    }

    private AirportSecuritySpawnPoint GetGeneratedAirportSecuritySpawnPoint(int index, int total)
    {
        Vector3 center = Airport != null && Airport.ArrivalPosition != Vector3.Zero ? Airport.ArrivalPosition : Player.Position;
        float radius = Clamp(WorldSettings?.AirportContrabandSecuritySpawnRadius ?? 20f, 4f, SearchActivityCopDistance);
        float offsetRadius = Math.Min(6f + index, radius);
        double angle = (Math.PI * 2d / Math.Max(1, total)) * index;
        Vector3 position = new Vector3(center.X + (float)Math.Cos(angle) * offsetRadius, center.Y + (float)Math.Sin(angle) * offsetRadius, center.Z);
        return new AirportSecuritySpawnPoint(position, GetHeadingToPlayer(position));
    }

    private float GetHeadingToPlayer(Vector3 position)
    {
        Vector3 playerPosition = Player?.Position ?? position;
        double x = playerPosition.X - position.X;
        double y = playerPosition.Y - position.Y;
        double heading = 270d - Math.Atan2(y, x) * (180d / Math.PI);
        while (heading < 0d)
        {
            heading += 360d;
        }
        while (heading >= 360d)
        {
            heading -= 360d;
        }
        return (float)heading;
    }

    private void UpdateNearbyAirportCops(IPoliceRespondable policeRespondable)
    {
        IPerceptable perceptable = Player as IPerceptable;
        if (perceptable == null || policeRespondable == null || World?.Pedestrians == null)
        {
            return;
        }
        foreach (Cop cop in World.Pedestrians.PoliceList.Where(x => x?.Pedestrian.Exists() == true && x.Pedestrian.DistanceTo2D(Player.Character) <= 60f))
        {
            cop.Update(perceptable, policeRespondable, policeRespondable.Position, World);
        }
    }

    private Cop GetValidNearbySearchCop(IPoliceRespondable policeRespondable)
    {
        if (policeRespondable?.Character == null || !policeRespondable.Character.Exists() || World?.Pedestrians == null)
        {
            return null;
        }
        return World.Pedestrians.PoliceList
            .Where(x => IsValidSearchCop(x, policeRespondable))
            .OrderBy(x => x.Pedestrian.DistanceTo2D(policeRespondable.Character))
            .FirstOrDefault();
    }

    private bool IsValidSearchCop(Cop cop, IPoliceRespondable policeRespondable)
    {
        if (cop == null || policeRespondable?.Character == null || !policeRespondable.Character.Exists() || !cop.Pedestrian.Exists())
        {
            return false;
        }
        return cop.Pedestrian.IsAlive
            && !cop.Pedestrian.IsDead
            && !cop.IsInVehicle
            && !cop.IsUnconscious
            && !cop.IsInWrithe
            && !cop.IsDead
            && !cop.Pedestrian.IsRagdoll
            && cop.Pedestrian.DistanceTo2D(policeRespondable.Character) <= SearchActivityCopDistance
            && Math.Abs(cop.Pedestrian.Position.Z - policeRespondable.Position.Z) <= SearchActivityCopHeight;
    }

    private void StartAirportContrabandIncidentCleanup(IPoliceRespondable policeRespondable)
    {
        List<Cop> incidentCops = SpawnedAirportCops.Where(x => x?.Pedestrian.Exists() == true).ToList();
        if (!incidentCops.Any())
        {
            return;
        }
        Vector3 incidentPosition = Player.Position;
        GameFiber.StartNew(delegate
        {
            try
            {
                uint started = Game.GameTime;
                while (EntryPoint.ModController.IsRunning && Game.GameTime - started <= AirportContrabandIncidentMaxLifetimeMs)
                {
                    bool playerStillInIncident = policeRespondable != null && (policeRespondable.IsBusted || policeRespondable.IsArrested || policeRespondable.IsWanted);
                    bool playerResolved = policeRespondable != null && !policeRespondable.IsBusted && !policeRespondable.IsArrested && policeRespondable.IsNotWanted;
                    bool playerLeftArea = Player?.Character != null && Player.Character.Exists() && Player.Character.DistanceTo2D(incidentPosition) >= Math.Max(100f, (WorldSettings?.AirportContrabandSecuritySpawnRadius ?? 20f) * 5f);
                    bool spawnedCopsFar = incidentCops.Where(x => x?.Pedestrian.Exists() == true).All(x => Player?.Character == null || !Player.Character.Exists() || x.Pedestrian.DistanceTo2D(Player.Character) >= 80f);
                    if (playerResolved && Game.GameTime - started >= AirportContrabandIncidentResolvedCleanupDelayMs)
                    {
                        break;
                    }
                    if (!playerStillInIncident && playerLeftArea)
                    {
                        break;
                    }
                    if (playerStillInIncident && playerLeftArea && spawnedCopsFar && Game.GameTime - started >= AirportContrabandIncidentFarCleanupDelayMs)
                    {
                        break;
                    }
                    GameFiber.Sleep(1000);
                }
                CleanupSpawnedAirportCops(policeRespondable?.IsWanted == true);
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Airport contraband security cleanup failed: {ex.Message} {ex.StackTrace}", 0);
            }
        }, "AirportContrabandSecurityCleanup");
    }

    private void CleanupSpawnedAirportCops(bool keepCloseWantedCops)
    {
        if (!SpawnedAirportCops.Any())
        {
            return;
        }
        List<uint> spawnedHandles = SpawnedAirportCops.Select(x => x.Handle).ToList();
        foreach (Cop cop in SpawnedAirportCops.ToList())
        {
            if (cop == null || !cop.Pedestrian.Exists())
            {
                continue;
            }
            bool closeToPlayer = Player?.Character != null && Player.Character.Exists() && cop.Pedestrian.DistanceTo2D(Player.Character) <= 80f;
            if (keepCloseWantedCops && closeToPlayer && cop.Pedestrian.IsAlive)
            {
                cop.Pedestrian.IsPersistent = false;
                continue;
            }
            cop.DeleteBlip();
            cop.Pedestrian.Delete();
            EntryPoint.PersistentPedsDeleted++;
        }
        World?.Pedestrians?.Police?.RemoveAll(x => x != null && spawnedHandles.Contains(x.Handle) && !x.Pedestrian.Exists());
        SpawnedAirportCops.Clear();
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

    private class AirportSecuritySpawnPoint
    {
        public AirportSecuritySpawnPoint(Vector3 position, float heading)
        {
            Position = position;
            Heading = heading;
        }

        public Vector3 Position { get; }
        public float Heading { get; }
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
