using LosSantosRED.lsr.Data;
using LosSantosRED.lsr.Helper;
using LosSantosRED.lsr.Interface;
using LSR.Vehicles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopOwnedVehicleAdapter
    {
        public CoopOwnedVehicleSnapshot CaptureFromPlayer(Mod.Player player, CoopProfileId profileId, CoopCharacterId characterId, CoopWorldId worldId)
        {
            CoopOwnedVehicleSnapshot snapshot = new CoopOwnedVehicleSnapshot
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
            };

            if (player?.VehicleOwnership?.OwnedVehicles == null)
            {
                EntryPoint.WriteToConsole($"Co-op owned vehicle snapshot capture skipped Profile:{profileId} Reason:MissingPlayerVehicleOwnership", 5);
                return snapshot;
            }

            foreach (VehicleExt vehicle in player.VehicleOwnership.OwnedVehicles)
            {
                if (vehicle?.Vehicle.Exists() != true)
                {
                    EntryPoint.WriteToConsole($"Co-op owned vehicle snapshot skipped Profile:{profileId} Reason:MissingVehicleEntity", 5);
                    continue;
                }

                CoopOwnedVehicleRecord record = CreateRecord(vehicle);
                snapshot.Vehicles.Add(record);
            }

            return snapshot;
        }

        public CoopOwnedVehicleHydrationResult TryApplySnapshotToPlayer(
            Mod.Player player,
            CoopOwnedVehicleSnapshot snapshot,
            IEntityProvideable world,
            ISettingsProvideable settings,
            IModItems modItems,
            IPlacesOfInterest placesOfInterest,
            ITimeReportable time,
            IWeapons weapons)
        {
            CoopOwnedVehicleHydrationResult result = new CoopOwnedVehicleHydrationResult();
            if (player == null || snapshot == null)
            {
                result.SkippedCount++;
                result.SkippedReasons.Add("MissingPlayerOrSnapshot");
                return result;
            }

            GameSave gameSave = new GameSave();
            foreach (CoopOwnedVehicleRecord record in snapshot.Vehicles ?? Enumerable.Empty<CoopOwnedVehicleRecord>())
            {
                VehicleSaveStatus saveStatus = CreateSaveStatus(record, result);
                if (saveStatus == null)
                {
                    continue;
                }

                gameSave.OwnedVehicleVariations.Add(saveStatus);
            }

            using (CoopStorePurchaseBridge.SuppressOwnedVehicleCommits("OwnedVehicleHydration"))
            {
                gameSave.LoadOwnedVehicles(player, world, settings, modItems, placesOfInterest, time, weapons);
            }

            result.HydratedCount = player.VehicleOwnership?.OwnedVehicles?.Count ?? 0;
            result.Applied = result.HydratedCount > 0 || gameSave.OwnedVehicleVariations.Count > 0;
            EntryPoint.WriteToConsole($"Co-op owned vehicle hydration Profile:{snapshot.ProfileId} SnapshotCount:{snapshot.Vehicles?.Count ?? 0} HydratedCount:{result.HydratedCount} Skipped:{result.SkippedCount}", 0);
            return result;
        }

        public bool TrySaveSnapshot(CoopServerWorldSave worldSave, CoopOwnedVehicleSnapshot snapshot)
        {
            if (worldSave?.WorldState?.Profiles == null || snapshot == null || snapshot.ProfileId.IsEmpty)
            {
                return false;
            }

            CoopServerPlayerProfile profile = worldSave.WorldState.Profiles.FirstOrDefault(x => x.ProfileId.Equals(snapshot.ProfileId));
            if (profile == null)
            {
                profile = new CoopServerPlayerProfile
                {
                    WorldId = snapshot.WorldId,
                    ProfileId = snapshot.ProfileId,
                    DisplayName = snapshot.ProfileId.ToString(),
                };
                worldSave.WorldState.Profiles.Add(profile);
            }

            profile.PersistentState.WorldId = snapshot.WorldId;
            profile.PersistentState.ProfileId = snapshot.ProfileId;
            profile.PersistentState.CharacterId = snapshot.CharacterId;
            profile.PersistentState.OwnedVehicleState = snapshot;
            profile.PersistentState.OwnedVehicleIds.Clear();
            foreach (CoopOwnedVehicleRecord vehicle in snapshot.Vehicles)
            {
                profile.PersistentState.OwnedVehicleIds.Add(vehicle.VehicleId);
            }

            worldSave.UpdatedUtc = DateTime.UtcNow;
            return true;
        }

        private CoopOwnedVehicleRecord CreateRecord(VehicleExt vehicle)
        {
            VehicleSaveStatus saveStatus = CreateVehicleSaveStatus(vehicle);
            return new CoopOwnedVehicleRecord
            {
                VehicleId = GetVehicleId(vehicle),
                ModelHash = vehicle.Vehicle.Model.Hash.ToString(),
                ModelName = vehicle.Vehicle.Model.Name ?? string.Empty,
                VehicleSaveStatusXml = SerializeSaveStatus(saveStatus),
                PlateNumber = vehicle.CarPlate?.PlateNumber ?? vehicle.Vehicle.LicensePlate ?? string.Empty,
                PlateType = vehicle.CarPlate?.PlateType ?? 0,
                PlateIsWanted = vehicle.CarPlate?.IsWanted == true,
                PositionX = vehicle.Vehicle.Position.X,
                PositionY = vehicle.Vehicle.Position.Y,
                PositionZ = vehicle.Vehicle.Position.Z,
                Heading = vehicle.Vehicle.Heading,
                IsImpounded = vehicle.IsImpounded,
                DateTimeImpounded = vehicle.DateTimeImpounded,
                TimesImpounded = vehicle.TimesImpounded,
                ImpoundedLocation = vehicle.ImpoundedLocation,
                StoredCash = vehicle.CashStorage?.StoredCash ?? 0,
            };
        }

        private VehicleSaveStatus CreateVehicleSaveStatus(VehicleExt vehicle)
        {
            VehicleSaveStatus saveStatus = new VehicleSaveStatus(vehicle.Vehicle.Model.Hash, vehicle.Vehicle.Position, vehicle.Vehicle.Heading)
            {
                IsImpounded = vehicle.IsImpounded,
                DateTimeImpounded = vehicle.DateTimeImpounded,
                TimesImpounded = vehicle.TimesImpounded,
                ImpoundedLocation = vehicle.ImpoundedLocation,
                StoredCash = vehicle.CashStorage?.StoredCash ?? 0,
                VehicleVariation = NativeHelper.GetVehicleVariation(vehicle.Vehicle),
            };

            if (vehicle.WeaponStorage != null)
            {
                saveStatus.WeaponInventory = new List<StoredWeapon>();
                foreach (StoredWeapon storedWeapon in vehicle.WeaponStorage.StoredWeapons)
                {
                    saveStatus.WeaponInventory.Add(storedWeapon.Copy());
                }
            }

            if (vehicle.SimpleInventory != null)
            {
                saveStatus.InventoryItems = new List<InventorySave>();
                foreach (InventoryItem item in vehicle.SimpleInventory.ItemsList)
                {
                    saveStatus.InventoryItems.Add(new InventorySave(item.ModItem?.Name, item.RemainingPercent));
                }
            }

            return saveStatus;
        }

        private VehicleSaveStatus CreateSaveStatus(CoopOwnedVehicleRecord record, CoopOwnedVehicleHydrationResult result)
        {
            if (record == null)
            {
                result.SkippedCount++;
                result.SkippedReasons.Add("MissingRecord");
                return null;
            }

            VehicleSaveStatus saveStatus = DeserializeSaveStatus(record.VehicleSaveStatusXml);
            if (saveStatus == null)
            {
                saveStatus = CreateFallbackSaveStatus(record);
            }

            if (saveStatus == null || saveStatus.LastPosition == Rage.Vector3.Zero)
            {
                result.SkippedCount++;
                result.SkippedReasons.Add($"InvalidSavedVehicle:{record.VehicleId ?? "unknown"}");
                EntryPoint.WriteToConsole($"Co-op owned vehicle hydration skipped VehicleId:{record.VehicleId} Plate:{record.PlateNumber} Model:{record.ModelName}/{record.ModelHash} Reason:InvalidSavedVehicle", 0);
                return null;
            }

            return saveStatus;
        }

        private VehicleSaveStatus CreateFallbackSaveStatus(CoopOwnedVehicleRecord record)
        {
            uint modelHash;
            uint.TryParse(record.ModelHash, out modelHash);
            VehicleSaveStatus saveStatus = !string.IsNullOrWhiteSpace(record.ModelName)
                ? new VehicleSaveStatus(record.ModelName, new Rage.Vector3(record.PositionX, record.PositionY, record.PositionZ), record.Heading)
                : new VehicleSaveStatus(modelHash, new Rage.Vector3(record.PositionX, record.PositionY, record.PositionZ), record.Heading);

            saveStatus.IsImpounded = record.IsImpounded;
            saveStatus.DateTimeImpounded = record.DateTimeImpounded;
            saveStatus.TimesImpounded = record.TimesImpounded;
            saveStatus.ImpoundedLocation = record.ImpoundedLocation;
            saveStatus.StoredCash = record.StoredCash;
            return saveStatus;
        }

        private string SerializeSaveStatus(VehicleSaveStatus saveStatus)
        {
            if (saveStatus == null)
            {
                return string.Empty;
            }

            try
            {
                XDocument document = new XDocument();
                XmlSerializer serializer = new XmlSerializer(typeof(VehicleSaveStatus));
                using (XmlWriter writer = document.CreateWriter())
                {
                    serializer.Serialize(writer, saveStatus);
                }
                return document.ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op owned vehicle save status serialize skipped Error:{ex.Message}", 0);
                return string.Empty;
            }
        }

        private VehicleSaveStatus DeserializeSaveStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(VehicleSaveStatus));
                using (StringReader stringReader = new StringReader(value))
                {
                    return serializer.Deserialize(stringReader) as VehicleSaveStatus;
                }
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op owned vehicle save status deserialize skipped Error:{ex.Message}", 0);
                return null;
            }
        }

        private string GetVehicleId(VehicleExt vehicle)
        {
            string plate = vehicle.CarPlate?.PlateNumber ?? vehicle.Vehicle.LicensePlate ?? string.Empty;
            return $"{vehicle.Vehicle.Model.Hash}:{plate}:{vehicle.Handle}";
        }
    }

    public class CoopOwnedVehicleHydrationResult
    {
        public CoopOwnedVehicleHydrationResult()
        {
            SkippedReasons = new List<string>();
        }

        public bool Applied { get; set; }
        public int HydratedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> SkippedReasons { get; private set; }
    }
}
