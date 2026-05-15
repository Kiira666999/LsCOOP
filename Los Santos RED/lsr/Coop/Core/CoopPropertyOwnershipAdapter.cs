using LosSantosRED.lsr.Data;
using LosSantosRED.lsr.Interface;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopPropertyOwnershipAdapter
    {
        private const string DiagnosticResidenceName = "0605 Apartment 4F";

        public CoopPropertyOwnershipSnapshot CaptureFromPlayer(Mod.Player player, CoopProfileId profileId, CoopCharacterId characterId, CoopWorldId worldId)
        {
            CoopPropertyOwnershipSnapshot snapshot = new CoopPropertyOwnershipSnapshot
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
            };

            if (player?.Properties?.PropertyList == null)
            {
                return snapshot;
            }

            foreach (GameLocation property in player.Properties.PropertyList.Where(x => x != null))
            {
                CoopPropertyOwnershipRecord record = CreateRecord(property);
                snapshot.Properties.Add(record);
                EntryPoint.WriteToConsole($"Co-op property snapshot record Profile:{profileId} PropertyId:{record.PropertyId} Name:{record.Name} Owned:{record.IsOwned} Rented:{record.IsRented}", 5);
            }

            EntryPoint.WriteToConsole($"Co-op property snapshot captured Profile:{profileId} Count:{snapshot.Properties.Count}", 5);
            return snapshot;
        }

        public CoopPropertyHydrationResult TryApplySnapshotToPlayer(Mod.Player player, CoopPropertyOwnershipSnapshot snapshot, IPlacesOfInterest placesOfInterest, IModItems modItems, ISettingsProvideable settings, IEntityProvideable world)
        {
            CoopPropertyHydrationResult result = new CoopPropertyHydrationResult();
            if (player == null || snapshot == null)
            {
                result.SkippedCount++;
                result.SkippedReasons.Add("MissingPlayerOrSnapshot");
                return result;
            }

            if (snapshot.Properties == null || snapshot.Properties.Count == 0)
            {
                result.SkippedCount++;
                result.SkippedReasons.Add("EmptySnapshotPreservedExistingState");
                EntryPoint.WriteToConsole($"Co-op property hydration skipped Profile:{snapshot.ProfileId} Reason:EmptySnapshotPreservedExistingState", 0);
                return result;
            }

            LogResidenceState("before", player, placesOfInterest, world, snapshot);

            GameSave gameSave = new GameSave();
            foreach (CoopPropertyOwnershipRecord record in snapshot.Properties)
            {
                SavedGameLocation savedLocation = CreateSavedLocation(record, result);
                if (savedLocation != null)
                {
                    gameSave.SavedGameLocations.Add(savedLocation);
                }
            }

            if (gameSave.SavedGameLocations.Count == 0)
            {
                EntryPoint.WriteToConsole($"Co-op property hydration skipped Profile:{snapshot.ProfileId} Reason:NoValidSavedLocations SnapshotCount:{snapshot.Properties.Count}", 0);
                return result;
            }

            EntryPoint.WriteToConsole($"Co-op property hydration loading Profile:{snapshot.ProfileId} SavedLocations:{gameSave.SavedGameLocations.Count} FirstSaved:{DescribeSavedLocation(gameSave.SavedGameLocations.FirstOrDefault())} Calling:GameSave.LoadOwnedProperties", 0);
            player.Properties?.Reset();
            gameSave.LoadOwnedProperties(player, placesOfInterest, modItems, settings, world);
            result.HydratedCount = player.Properties?.PropertyList?.Count ?? 0;
            result.Applied = result.HydratedCount > 0;
            LogResidenceState("after", player, placesOfInterest, world, snapshot);
            EntryPoint.WriteToConsole($"Co-op property hydration Profile:{snapshot.ProfileId} SnapshotCount:{snapshot.Properties.Count} HydratedCount:{result.HydratedCount} Skipped:{result.SkippedCount}", 0);
            return result;
        }

        public bool TrySaveSnapshot(CoopServerWorldSave worldSave, CoopPropertyOwnershipSnapshot snapshot)
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
            profile.PersistentState.PropertyOwnershipState = snapshot;
            profile.PersistentState.PropertyIds.Clear();
            foreach (CoopPropertyOwnershipRecord property in snapshot.Properties)
            {
                profile.PersistentState.PropertyIds.Add(property.PropertyId);
            }

            worldSave.UpdatedUtc = DateTime.UtcNow;
            return true;
        }

        private CoopPropertyOwnershipRecord CreateRecord(GameLocation property)
        {
            CoopPropertyOwnershipRecord record = new CoopPropertyOwnershipRecord
            {
                PropertyId = GetPropertyId(property),
                Name = property.Name,
                PropertyType = property.GetType().Name,
                IsOwned = property.IsOwned,
                EntranceX = property.EntrancePosition.X,
                EntranceY = property.EntrancePosition.Y,
                EntranceZ = property.EntrancePosition.Z,
                CurrentSalesPrice = property.CurrentSalesPrice,
                PayoutDate = property.DatePayoutDue,
                DateOfLastPayout = property.DatePayoutPaid,
                SavedGameLocationXml = SerializeSaveData(property.GetSaveData()),
            };

            if (property is Residence residence)
            {
                record.IsRented = residence.IsRented;
                record.IsRentedOut = residence.IsRentedOut;
                record.RentalPaymentDate = residence.DateRentalPaymentDue;
                record.DateOfLastRentalPayment = residence.DateRentalPaymentPaid;
            }

            return record;
        }

        private SavedGameLocation CreateSavedLocation(CoopPropertyOwnershipRecord record, CoopPropertyHydrationResult result)
        {
            if (record == null)
            {
                result.SkippedCount++;
                result.SkippedReasons.Add("MissingRecord");
                return null;
            }

            bool hadSerializedSaveData = !string.IsNullOrWhiteSpace(record.SavedGameLocationXml);
            SavedGameLocation savedLocation = DeserializeSaveData(record.SavedGameLocationXml);
            if (savedLocation == null)
            {
                savedLocation = CreateFallbackSaveData(record);
            }

            if (savedLocation == null || string.IsNullOrWhiteSpace(savedLocation.Name))
            {
                result.SkippedCount++;
                result.SkippedReasons.Add($"InvalidProperty:{record.PropertyId ?? "unknown"}");
                EntryPoint.WriteToConsole($"Co-op property hydration skipped PropertyId:{record.PropertyId} Name:{record.Name} Reason:InvalidSavedProperty", 0);
                return null;
            }

            EntryPoint.WriteToConsole($"Co-op property hydration queued PropertyId:{record.PropertyId} Name:{record.Name} Type:{record.PropertyType} Owned:{record.IsOwned} Rented:{record.IsRented} RentedOut:{record.IsRentedOut} SaveType:{savedLocation.GetType().Name} SaveName:{savedLocation.Name} SaveSource:{(hadSerializedSaveData ? "Xml" : "Fallback")}", 0);
            return savedLocation;
        }

        private SavedGameLocation CreateFallbackSaveData(CoopPropertyOwnershipRecord record)
        {
            SavedGameLocation savedLocation;
            if (string.Equals(record.PropertyType, nameof(Residence), StringComparison.OrdinalIgnoreCase))
            {
                savedLocation = new SavedResidence(record.Name, record.IsOwned, record.IsRented)
                {
                    IsRentedOut = record.IsRentedOut,
                    RentalPaymentDate = record.RentalPaymentDate,
                    DateOfLastRentalPayment = record.DateOfLastRentalPayment,
                };
            }
            else if (string.Equals(record.PropertyType, nameof(Business), StringComparison.OrdinalIgnoreCase))
            {
                savedLocation = new SavedBusiness(record.Name, record.IsOwned);
            }
            else
            {
                savedLocation = new SavedGameLocation(record.Name, record.IsOwned);
            }

            savedLocation.EntrancePosition = new Rage.Vector3(record.EntranceX, record.EntranceY, record.EntranceZ);
            savedLocation.CurrentSalesPrice = record.CurrentSalesPrice;
            savedLocation.PayoutDate = record.PayoutDate;
            savedLocation.DateOfLastPayout = record.DateOfLastPayout;
            return savedLocation;
        }

        private string SerializeSaveData(SavedGameLocation saveData)
        {
            if (saveData == null)
            {
                return string.Empty;
            }

            try
            {
                XDocument document = new XDocument();
                XmlSerializer serializer = new XmlSerializer(typeof(SavedGameLocation));
                using (XmlWriter writer = document.CreateWriter())
                {
                    serializer.Serialize(writer, saveData);
                }
                return document.ToString(SaveOptions.DisableFormatting);
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op property save data serialize skipped Error:{ex.Message}", 0);
                return string.Empty;
            }
        }

        private SavedGameLocation DeserializeSaveData(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(SavedGameLocation));
                using (StringReader stringReader = new StringReader(value))
                {
                    return serializer.Deserialize(stringReader) as SavedGameLocation;
                }
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op property save data deserialize skipped Error:{ex.Message}", 0);
                return null;
            }
        }

        private string GetPropertyId(GameLocation property)
        {
            return $"{property.GetType().Name}:{property.Name}:{property.EntrancePosition.X:0.###}:{property.EntrancePosition.Y:0.###}:{property.EntrancePosition.Z:0.###}";
        }

        private void LogResidenceState(string phase, Mod.Player player, IPlacesOfInterest placesOfInterest, IEntityProvideable world, CoopPropertyOwnershipSnapshot snapshot)
        {
            int residenceCount = placesOfInterest?.PossibleLocations?.Residences?.Count ?? 0;
            Residence targetResidence = placesOfInterest?.PossibleLocations?.Residences?.FirstOrDefault(x => string.Equals(x?.Name, DiagnosticResidenceName, StringComparison.OrdinalIgnoreCase));
            int propertyListCount = player?.Properties?.PropertyList?.Count ?? 0;
            bool propertyListContainsTarget = player?.Properties?.PropertyList?.Any(x => string.Equals(x?.Name, DiagnosticResidenceName, StringComparison.OrdinalIgnoreCase)) == true;
            EntryPoint.WriteToConsole($"Co-op property hydration {phase} Profile:{snapshot?.ProfileId} WorldSet:{world != null} MPMap:{world?.IsMPMapLoaded} PlacesSet:{placesOfInterest != null} Residences:{residenceCount} Target:{DiagnosticResidenceName} TargetFound:{targetResidence != null} TargetOwned:{targetResidence?.IsOwned} TargetRented:{targetResidence?.IsRented} TargetRentedOut:{targetResidence?.IsRentedOut} TargetInventorySet:{targetResidence?.SimpleInventory != null} TargetWeaponStorageSet:{targetResidence?.WeaponStorage != null} TargetCashStorageSet:{targetResidence?.CashStorage != null} PlayerPropertyListCount:{propertyListCount} PlayerPropertyListContainsTarget:{propertyListContainsTarget}", 0);
        }

        private string DescribeSavedLocation(SavedGameLocation savedLocation)
        {
            if (savedLocation == null)
            {
                return "none";
            }

            if (savedLocation is SavedResidence savedResidence)
            {
                return $"{savedLocation.GetType().Name}|name={savedLocation.Name}|owned={savedResidence.IsOwnedByPlayer}|rented={savedResidence.IsRentedByPlayer}";
            }

            return $"{savedLocation.GetType().Name}|name={savedLocation.Name}|entrance={savedLocation.EntrancePosition}";
        }
    }

    public class CoopPropertyHydrationResult
    {
        public CoopPropertyHydrationResult()
        {
            SkippedReasons = new List<string>();
        }

        public bool Applied { get; set; }
        public int HydratedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<string> SkippedReasons { get; private set; }
    }
}
