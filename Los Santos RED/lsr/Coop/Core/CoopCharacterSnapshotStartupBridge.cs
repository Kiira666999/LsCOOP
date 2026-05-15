using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Interface;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public sealed class CoopCharacterStartupSnapshot
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string ModelName { get; set; }
        public CoopAppearanceState Appearance { get; set; }
        public CoopInventoryMoneySnapshot InventoryMoney { get; set; }
        public CoopWeaponSnapshot Weapons { get; set; }
        public CoopOwnedVehicleSnapshot OwnedVehicles { get; set; }
        public CoopPropertyOwnershipSnapshot PropertyOwnership { get; set; }
        public CoopCriminalHistoryState CriminalHistory { get; set; }
        public CoopGangReputationState GangReputation { get; set; }
    }

    public static class CoopCharacterSnapshotStartupBridge
    {
        private const string CharacterSnapshotBridgeFileName = "LsrCoopCharacterSnapshot.txt";
        private const string RequiredBridgeVersion = "1";
        private const string RequiredTransportMode = "RAGECOOP";
        private const int SafePedHealth = 200;

        public static CoopCharacterStartupSnapshot TryReadReadySnapshot()
        {
            if (!CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsCharacterReadyForSimulation)
            {
                return null;
            }

            string blockedReason;
            CoopStartupMode startupMode = CoopStartupBridge.GetStartupMode(out blockedReason);
            if (startupMode != CoopStartupMode.FullSimulation && startupMode != CoopStartupMode.ClientMode)
            {
                return null;
            }

            foreach (string path in GetSnapshotBridgePaths())
            {
                CoopCharacterStartupSnapshot snapshot = TryReadSnapshot(path);
                if (snapshot != null)
                {
                    EntryPoint.WriteToConsole($"Co-op character startup snapshot loaded Profile:{snapshot.ProfileId} Model:{snapshot.ModelName}", 0);
                    return snapshot;
                }
            }

            EntryPoint.WriteToConsole("Co-op character startup snapshot not found; using current local ped model", 0);
            return null;
        }

        public static bool ApplyModelBeforeCreatePlayer(CoopCharacterStartupSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ModelName))
            {
                return false;
            }

            Ped ped = GetLocalPed();
            if (ped == null)
            {
                EntryPoint.WriteToConsole($"Co-op character startup model apply skipped; local ped missing Model:{snapshot.ModelName}", 0);
                return false;
            }

            string currentModel = ped.Model.Name;
            if (string.Equals(currentModel, snapshot.ModelName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                SafeStatePed(ped, true);
                NativeHelper.ChangeModel(snapshot.ModelName);
                GameFiber.Yield();
                Ped changedPed = GetLocalPed();
                SafeStatePed(changedPed, false);
                return changedPed != null && string.Equals(changedPed.Model.Name, snapshot.ModelName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                SafeStatePed(GetLocalPed(), false);
                EntryPoint.WriteToConsole($"Co-op character startup model apply failed Model:{snapshot.ModelName} Error:{ex.Message}", 0);
                return false;
            }
        }

        public static bool ApplyAppearanceAfterPlayerSetup(CoopCharacterStartupSnapshot snapshot, Mod.Player player)
        {
            if (snapshot?.Appearance == null)
            {
                return false;
            }

            Ped ped = GetLocalPed();
            if (ped == null)
            {
                EntryPoint.WriteToConsole("Co-op character startup appearance apply skipped; local ped missing", 0);
                return false;
            }

            bool applied = new CoopAppearanceApplyService().TryApply(ped, snapshot.Appearance);
            if (applied && player != null)
            {
                player.ModelName = ped.Model.Name;
                player.CurrentModelVariation = NativeHelper.GetPedVariation(ped);
            }

            return applied;
        }

        public static bool ApplyProfileHydrationAfterPlayerSetup(CoopCharacterStartupSnapshot snapshot, Mod.Player player, IModItems modItems, ICrimes crimes, IEntityProvideable world, ISettingsProvideable settings, IPlacesOfInterest placesOfInterest, ITimeReportable time, IWeapons weapons)
        {
            if (snapshot == null || player == null)
            {
                return false;
            }

            bool appliedInventoryMoney = false;
            if (snapshot.InventoryMoney != null)
            {
                appliedInventoryMoney = new CoopInventoryMoneyAdapter().TryApplySnapshotToPlayer(player, snapshot.InventoryMoney, modItems);
            }

            CoopWeaponHydrationResult weaponHydration = null;
            if (snapshot.Weapons != null)
            {
                weaponHydration = new CoopWeaponInventoryAdapter().TryApplySnapshotToPlayer(player, snapshot.Weapons);
            }

            CoopOwnedVehicleHydrationResult ownedVehicleHydration = null;
            if (snapshot.OwnedVehicles != null)
            {
                if (CoopStartupBridge.IsLocalActiveHost)
                {
                    ownedVehicleHydration = new CoopOwnedVehicleAdapter().TryApplySnapshotToPlayer(player, snapshot.OwnedVehicles, world, settings, modItems, placesOfInterest, time, weapons);
                }
                else
                {
                    EntryPoint.WriteToConsole($"Co-op owned vehicle hydration skipped Profile:{snapshot.ProfileId} Reason:ClientModeNotActiveHost SnapshotCount:{snapshot.OwnedVehicles.Vehicles?.Count ?? 0}", 0);
                }
            }

            CoopPropertyHydrationResult propertyHydration = null;
            if (snapshot.PropertyOwnership != null)
            {
                propertyHydration = new CoopPropertyOwnershipAdapter().TryApplySnapshotToPlayer(player, snapshot.PropertyOwnership, placesOfInterest, modItems, settings, world);
            }

            bool appliedCriminalHistory = false;
            if (snapshot.CriminalHistory != null)
            {
                appliedCriminalHistory = CoopCriminalJusticeStateAdapter.Current.TryApplyPersistentStateToPlayer(player, snapshot.CriminalHistory, crimes);
            }

            bool appliedGangReputation = false;
            if (snapshot.GangReputation != null)
            {
                appliedGangReputation = CoopGangReputationStateAdapter.Current.TryApplyPersistentStateToPlayer(player, snapshot.GangReputation);
            }

            EntryPoint.WriteToConsole($"Co-op profile hydration apply InventoryMoney:{appliedInventoryMoney} Items:{snapshot.InventoryMoney?.InventoryItems?.Count ?? 0} BankAccounts:{snapshot.InventoryMoney?.BankAccounts?.Count ?? 0} Money:{snapshot.InventoryMoney?.TotalMoney ?? 0} Weapons:{snapshot.Weapons?.Weapons?.Count ?? 0} WeaponsHydrated:{weaponHydration?.HydratedCount ?? 0} WeaponsExisting:{weaponHydration?.ExistingCount ?? 0} WeaponsSkippedDuplicate:{weaponHydration?.SkippedDuplicateCount ?? 0} OwnedVehicles:{snapshot.OwnedVehicles?.Vehicles?.Count ?? 0} OwnedVehiclesHydrated:{ownedVehicleHydration?.HydratedCount ?? 0} OwnedVehiclesSkipped:{ownedVehicleHydration?.SkippedCount ?? 0} Properties:{snapshot.PropertyOwnership?.Properties?.Count ?? 0} PropertiesHydrated:{propertyHydration?.HydratedCount ?? 0} PropertiesSkipped:{propertyHydration?.SkippedCount ?? 0} CriminalHistory:{appliedCriminalHistory} Crimes:{snapshot.CriminalHistory?.Crimes?.Count ?? 0} GangReputation:{appliedGangReputation} GangRecords:{snapshot.GangReputation?.Reputations?.Count ?? 0} DateTimeLastWantedEnded:{snapshot.CriminalHistory?.DateTimeLastWantedEnded.ToString("O") ?? string.Empty}", 0);
            return appliedInventoryMoney || weaponHydration?.Applied == true || ownedVehicleHydration?.Applied == true || propertyHydration?.Applied == true || appliedCriminalHistory || appliedGangReputation;
        }

        public static void HydrateLocalCharacter(LocalCoopCharacter localCharacter, CoopCharacterStartupSnapshot snapshot)
        {
            if (localCharacter == null || snapshot == null)
            {
                return;
            }

            localCharacter.HydratePersistentState(snapshot.InventoryMoney, snapshot.Weapons);
        }

        private static CoopCharacterStartupSnapshot TryReadSnapshot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                Dictionary<string, string> values = ReadKeyValues(path);
                if (!string.Equals(GetValue(values, "BridgeVersion"), RequiredBridgeVersion, StringComparison.Ordinal)
                    || !string.Equals(GetValue(values, "TransportMode"), RequiredTransportMode, StringComparison.OrdinalIgnoreCase)
                    || !IsCurrentProcess(GetValue(values, "ProcessId")))
                {
                    return null;
                }

                string worldId = GetValue(values, "WorldId");
                string profileId = GetValue(values, "ProfileId");
                if (!string.Equals(worldId, CoopStartupBridge.WorldId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(profileId, CoopStartupBridge.LocalProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string modelName = GetValue(values, "ModelName");
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    return null;
                }

                return new CoopCharacterStartupSnapshot
                {
                    WorldId = worldId,
                    ProfileId = profileId,
                    ModelName = modelName,
                    Appearance = new CoopAppearanceState
                    {
                        ModelName = modelName,
                        Components = ParseComponents(GetValue(values, "Components")),
                        Props = ParseProps(GetValue(values, "Props")),
                    },
                    InventoryMoney = ParseInventoryMoney(values, worldId, profileId),
                    Weapons = ParseWeapons(values, worldId, profileId),
                    OwnedVehicles = ParseOwnedVehicles(values, worldId, profileId),
                    PropertyOwnership = ParseProperties(values, worldId, profileId),
                    CriminalHistory = ParseCriminalHistory(values, worldId, profileId),
                    GangReputation = ParseGangReputation(values, worldId, profileId),
                };
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op character startup snapshot read skipped Path:{path} Error:{ex.Message}", 0);
                return null;
            }
        }

        private static Dictionary<string, string> ReadKeyValues(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int separatorIndex = line?.IndexOf('=') ?? -1;
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex);
                string value = line.Substring(separatorIndex + 1);
                values[key] = Uri.UnescapeDataString(value ?? string.Empty);
            }

            return values;
        }

        private static List<CoopPedComponentState> ParseComponents(string raw)
        {
            List<CoopPedComponentState> components = new List<CoopPedComponentState>();
            foreach (string entry in SplitEntries(raw))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                components.Add(new CoopPedComponentState
                {
                    ComponentId = ParseInt(parts[0]),
                    DrawableId = ParseInt(parts[1]),
                    TextureId = ParseInt(parts[2]),
                    PaletteId = ParseInt(parts[3]),
                });
            }

            return components;
        }

        private static List<CoopPedPropState> ParseProps(string raw)
        {
            List<CoopPedPropState> props = new List<CoopPedPropState>();
            foreach (string entry in SplitEntries(raw))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                props.Add(new CoopPedPropState
                {
                    PropId = ParseInt(parts[0]),
                    DrawableId = ParseInt(parts[1]),
                    TextureId = ParseInt(parts[2]),
                    IsCleared = string.Equals(parts[3], "true", StringComparison.OrdinalIgnoreCase),
                });
            }

            return props;
        }

        private static CoopInventoryMoneySnapshot ParseInventoryMoney(Dictionary<string, string> values, string worldId, string profileId)
        {
            string snapshotId = GetValue(values, "InventoryMoneySnapshotId");
            string onHandCash = GetValue(values, "OnHandCash");
            string totalAccountMoney = GetValue(values, "TotalAccountMoney");
            string inventoryItems = GetValue(values, "InventoryItems");
            string bankAccounts = GetValue(values, "BankAccounts");
            if (string.IsNullOrWhiteSpace(snapshotId)
                && string.IsNullOrWhiteSpace(onHandCash)
                && string.IsNullOrWhiteSpace(totalAccountMoney)
                && string.IsNullOrWhiteSpace(inventoryItems)
                && string.IsNullOrWhiteSpace(bankAccounts))
            {
                return null;
            }

            CoopInventoryMoneySnapshot snapshot = new CoopInventoryMoneySnapshot
            {
                SnapshotId = string.IsNullOrWhiteSpace(snapshotId) ? Guid.NewGuid().ToString("N") : snapshotId,
                WorldId = new CoopWorldId(worldId),
                ProfileId = new CoopProfileId(profileId),
                CharacterId = new CoopCharacterId(GetValue(values, "CharacterId")),
                OnHandCash = ParseInt(onHandCash),
                TotalAccountMoney = ParseInt(totalAccountMoney),
                SnapshotUtc = ParseDateTimeOffset(GetValue(values, "InventoryMoneySnapshotUtc")),
            };

            foreach (string entry in SplitEntries(inventoryItems))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 2)
                {
                    continue;
                }

                snapshot.InventoryItems.Add(new CoopInventoryItemState
                {
                    ItemName = UnescapePart(parts[0]),
                    RemainingPercent = ParseFloat(parts[1]),
                });
            }

            foreach (string entry in SplitEntries(bankAccounts))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                snapshot.BankAccounts.Add(new CoopBankAccountState
                {
                    BankContactName = UnescapePart(parts[0]),
                    AccountName = UnescapePart(parts[1]),
                    Money = ParseInt(parts[2]),
                    IsPrimary = string.Equals(parts[3], "true", StringComparison.OrdinalIgnoreCase),
                });
            }

            return snapshot;
        }

        private static string DescribeFirstProperty(CoopPropertyOwnershipSnapshot snapshot)
        {
            CoopPropertyOwnershipRecord property = snapshot?.Properties?.FirstOrDefault();
            return property == null
                ? "none"
                : $"{property.PropertyId}|name={property.Name}|type={property.PropertyType}|owned={property.IsOwned}|rented={property.IsRented}|rentedOut={property.IsRentedOut}";
        }

        private static CoopCriminalHistoryState ParseCriminalHistory(Dictionary<string, string> values, string worldId, string profileId)
        {
            string hasHistory = GetValue(values, "CriminalHistoryHasHistory");
            string crimes = GetValue(values, "CriminalHistoryCrimes");
            string wantedLevel = GetValue(values, "CriminalHistoryWantedLevel");
            string dateTimeLastWantedEnded = GetValue(values, "CriminalHistoryDateTimeLastWantedEnded");
            if (string.IsNullOrWhiteSpace(hasHistory)
                && string.IsNullOrWhiteSpace(crimes)
                && string.IsNullOrWhiteSpace(wantedLevel)
                && string.IsNullOrWhiteSpace(dateTimeLastWantedEnded))
            {
                return null;
            }

            CoopCriminalHistoryState state = new CoopCriminalHistoryState
            {
                WorldId = new CoopWorldId(worldId),
                ProfileId = new CoopProfileId(profileId),
                CharacterId = new CoopCharacterId(GetValue(values, "CharacterId")),
                HasHistory = IsTrue(hasHistory),
                LastSeenX = ParseFloat(GetValue(values, "CriminalHistoryLastSeenX")),
                LastSeenY = ParseFloat(GetValue(values, "CriminalHistoryLastSeenY")),
                LastSeenZ = ParseFloat(GetValue(values, "CriminalHistoryLastSeenZ")),
                WantedLevel = ParseInt(wantedLevel),
                DateTimeLastWantedEnded = ParseOptionalDateTimeOffset(dateTimeLastWantedEnded),
                UpdatedUtc = ParseDateTimeOffset(GetValue(values, "CriminalHistoryUpdatedUtc")),
            };

            foreach (string entry in SplitEntries(crimes))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 6)
                {
                    continue;
                }

                state.Crimes.Add(new CoopCriminalHistoryCrimeRecord
                {
                    CrimeId = UnescapePart(parts[0]),
                    CrimeName = UnescapePart(parts[1]),
                    Instances = ParseInt(parts[2]),
                    ResultingWantedLevel = ParseInt(parts[3]),
                    Priority = ParseInt(parts[4]),
                    ResultsInLethalForce = IsTrue(parts[5]),
                });
            }

            return state;
        }

        private static CoopGangReputationState ParseGangReputation(Dictionary<string, string> values, string worldId, string profileId)
        {
            string records = GetValue(values, "GangReputationRecords");
            string stateId = GetValue(values, "GangReputationStateId");
            string currentGangId = GetValue(values, "GangReputationCurrentGangId");
            if (string.IsNullOrWhiteSpace(records)
                && string.IsNullOrWhiteSpace(stateId)
                && string.IsNullOrWhiteSpace(currentGangId))
            {
                return null;
            }

            CoopGangReputationState state = new CoopGangReputationState
            {
                StateId = string.IsNullOrWhiteSpace(stateId) ? Guid.NewGuid().ToString("N") : stateId,
                WorldId = new CoopWorldId(worldId),
                ProfileId = new CoopProfileId(profileId),
                CharacterId = new CoopCharacterId(GetValue(values, "CharacterId")),
                CurrentGangId = currentGangId,
                UpdatedUtc = ParseDateTimeOffset(GetValue(values, "GangReputationUpdatedUtc")),
            };

            foreach (string entry in SplitEntries(records))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 12)
                {
                    continue;
                }

                bool includesGangName = parts.Length >= 13;
                int offset = includesGangName ? 1 : 0;
                state.Reputations.Add(new CoopGangReputationRecord
                {
                    GangId = UnescapePart(parts[0]),
                    GangName = includesGangName ? UnescapePart(parts[1]) : string.Empty,
                    Reputation = ParseInt(parts[1 + offset]),
                    MembersHurt = ParseInt(parts[2 + offset]),
                    MembersKilled = ParseInt(parts[3 + offset]),
                    MembersCarJacked = ParseInt(parts[4 + offset]),
                    MembersHurtInTerritory = ParseInt(parts[5 + offset]),
                    MembersKilledInTerritory = ParseInt(parts[6 + offset]),
                    MembersCarJackedInTerritory = ParseInt(parts[7 + offset]),
                    PlayerDebt = ParseInt(parts[8 + offset]),
                    IsMember = IsTrue(parts[9 + offset]),
                    IsEnemy = IsTrue(parts[10 + offset]),
                    TasksCompleted = ParseInt(parts[11 + offset]),
                });
            }

            return state;
        }

        private static CoopWeaponSnapshot ParseWeapons(Dictionary<string, string> values, string worldId, string profileId)
        {
            string snapshotId = GetValue(values, "WeaponSnapshotId");
            string weapons = GetValue(values, "Weapons");
            if (string.IsNullOrWhiteSpace(snapshotId) && string.IsNullOrWhiteSpace(weapons))
            {
                return null;
            }

            CoopWeaponSnapshot snapshot = new CoopWeaponSnapshot
            {
                SnapshotId = string.IsNullOrWhiteSpace(snapshotId) ? Guid.NewGuid().ToString("N") : snapshotId,
                WorldId = new CoopWorldId(worldId),
                ProfileId = new CoopProfileId(profileId),
                CharacterId = new CoopCharacterId(GetValue(values, "CharacterId")),
                SnapshotUtc = ParseDateTimeOffset(GetValue(values, "WeaponSnapshotUtc")),
            };

            foreach (string entry in SplitEntries(weapons))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 6)
                {
                    continue;
                }

                snapshot.Weapons.Add(new CoopWeaponRecord
                {
                    WeaponHash = UnescapePart(parts[0]),
                    WeaponName = UnescapePart(parts[1]),
                    Category = UnescapePart(parts[2]),
                    Ammo = ParseInt(parts[3]),
                    IsLegal = string.Equals(parts[4], "true", StringComparison.OrdinalIgnoreCase),
                    IsEquipped = string.Equals(parts[5], "true", StringComparison.OrdinalIgnoreCase),
                });
            }

            return snapshot;
        }

        private static CoopOwnedVehicleSnapshot ParseOwnedVehicles(Dictionary<string, string> values, string worldId, string profileId)
        {
            string snapshotId = GetValue(values, "OwnedVehicleSnapshotId");
            string vehicles = GetValue(values, "OwnedVehicles");
            if (string.IsNullOrWhiteSpace(snapshotId) && string.IsNullOrWhiteSpace(vehicles))
            {
                return null;
            }

            CoopOwnedVehicleSnapshot snapshot = new CoopOwnedVehicleSnapshot
            {
                SnapshotId = string.IsNullOrWhiteSpace(snapshotId) ? Guid.NewGuid().ToString("N") : snapshotId,
                WorldId = new CoopWorldId(worldId),
                ProfileId = new CoopProfileId(profileId),
                CharacterId = new CoopCharacterId(GetValue(values, "CharacterId")),
                SnapshotUtc = ParseDateTimeOffset(GetValue(values, "OwnedVehicleSnapshotUtc")),
            };

            foreach (string entry in SplitEntries(vehicles))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 16)
                {
                    continue;
                }

                snapshot.Vehicles.Add(new CoopOwnedVehicleRecord
                {
                    VehicleId = UnescapePart(parts[0]),
                    ModelHash = UnescapePart(parts[1]),
                    ModelName = UnescapePart(parts[2]),
                    VehicleSaveStatusXml = UnescapePart(parts[3]),
                    PlateNumber = UnescapePart(parts[4]),
                    PlateType = ParseInt(parts[5]),
                    PlateIsWanted = IsTrue(parts[6]),
                    PositionX = ParseFloat(parts[7]),
                    PositionY = ParseFloat(parts[8]),
                    PositionZ = ParseFloat(parts[9]),
                    Heading = ParseFloat(parts[10]),
                    IsImpounded = IsTrue(parts[11]),
                    DateTimeImpounded = ParseOptionalDateTime(parts[12]),
                    TimesImpounded = ParseInt(parts[13]),
                    ImpoundedLocation = UnescapePart(parts[14]),
                    StoredCash = ParseInt(parts[15]),
                });
            }

            return snapshot;
        }

        private static CoopPropertyOwnershipSnapshot ParseProperties(Dictionary<string, string> values, string worldId, string profileId)
        {
            string snapshotId = GetValue(values, "PropertyOwnershipSnapshotId");
            string properties = GetValue(values, "Properties");
            if (string.IsNullOrWhiteSpace(snapshotId) && string.IsNullOrWhiteSpace(properties))
            {
                return null;
            }

            CoopPropertyOwnershipSnapshot snapshot = new CoopPropertyOwnershipSnapshot
            {
                SnapshotId = string.IsNullOrWhiteSpace(snapshotId) ? Guid.NewGuid().ToString("N") : snapshotId,
                WorldId = new CoopWorldId(worldId),
                ProfileId = new CoopProfileId(profileId),
                CharacterId = new CoopCharacterId(GetValue(values, "CharacterId")),
                SnapshotUtc = ParseDateTimeOffset(GetValue(values, "PropertyOwnershipSnapshotUtc")),
            };

            int entryIndex = 0;
            foreach (string entry in SplitEntries(properties))
            {
                entryIndex++;
                string[] parts = entry.Split(',');
                if (parts.Length < 15)
                {
                    string preview = entry.Length > 160 ? entry.Substring(0, 160) + "..." : entry;
                    EntryPoint.WriteToConsole($"Co-op property startup parse skipped Profile:{profileId} Entry:{entryIndex} Reason:MalformedPropertyEntry Parts:{parts.Length} Preview:{preview}", 0);
                    continue;
                }

                snapshot.Properties.Add(new CoopPropertyOwnershipRecord
                {
                    PropertyId = UnescapePart(parts[0]),
                    Name = UnescapePart(parts[1]),
                    PropertyType = UnescapePart(parts[2]),
                    IsOwned = IsTrue(parts[3]),
                    IsRented = IsTrue(parts[4]),
                    IsRentedOut = IsTrue(parts[5]),
                    EntranceX = ParseFloat(parts[6]),
                    EntranceY = ParseFloat(parts[7]),
                    EntranceZ = ParseFloat(parts[8]),
                    CurrentSalesPrice = ParseInt(parts[9]),
                    PayoutDate = ParseOptionalDateTime(parts[10]),
                    DateOfLastPayout = ParseOptionalDateTime(parts[11]),
                    RentalPaymentDate = ParseOptionalDateTime(parts[12]),
                    DateOfLastRentalPayment = ParseOptionalDateTime(parts[13]),
                    SavedGameLocationXml = UnescapePart(parts[14]),
                });
            }

            return snapshot;
        }

        private static IEnumerable<string> SplitEntries(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                yield break;
            }

            foreach (string entry in raw.Split(';'))
            {
                if (!string.IsNullOrWhiteSpace(entry))
                {
                    yield return entry;
                }
            }
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }

        private static float ParseFloat(string value)
        {
            float parsed;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0.0f;
        }

        private static DateTimeOffset ParseDateTimeOffset(string value)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : DateTimeOffset.UtcNow;
        }

        private static DateTimeOffset ParseOptionalDateTimeOffset(string value)
        {
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : DateTimeOffset.MinValue;
        }

        private static DateTime ParseOptionalDateTime(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : DateTime.MinValue;
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string UnescapePart(string value)
        {
            return Uri.UnescapeDataString(value ?? string.Empty);
        }

        private static string GetValue(Dictionary<string, string> values, string key)
        {
            return values != null && values.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        }

        private static bool IsCurrentProcess(string value)
        {
            int processId;
            return int.TryParse(value, out processId) && processId == Process.GetCurrentProcess().Id;
        }

        private static IEnumerable<string> GetSnapshotBridgePaths()
        {
            foreach (string folder in GetBridgeFolders())
            {
                yield return Path.Combine(folder, CharacterSnapshotBridgeFileName);
            }
        }

        private static string[] GetBridgeFolders()
        {
            return new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED"),
                Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "LosSantosRED"),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };
        }

        private static Ped GetLocalPed()
        {
            try
            {
                Ped ped = Game.LocalPlayer?.Character;
                if (ped != null && ped.Exists())
                {
                    return ped;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void SafeStatePed(Ped ped, bool freeze)
        {
            if (ped == null || !ped.Exists())
            {
                return;
            }

            NativeFunction.Natives.FREEZE_ENTITY_POSITION(ped, freeze);
            ped.IsInvincible = freeze;
            if (ped.MaxHealth < SafePedHealth)
            {
                ped.MaxHealth = SafePedHealth;
            }
            if (ped.Health < SafePedHealth)
            {
                ped.Health = SafePedHealth;
            }
            ped.IsCollisionEnabled = true;
        }
    }
}
