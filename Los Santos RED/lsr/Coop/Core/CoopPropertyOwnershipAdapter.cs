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
            }

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

            player.Properties?.Reset();
            gameSave.LoadOwnedProperties(player, placesOfInterest, modItems, settings, world);
            result.HydratedCount = player.Properties?.PropertyList?.Count ?? 0;
            result.Applied = result.HydratedCount > 0;
            CoopPersistenceDiagnostics.WriteVerbose($"Co-op property hydration Profile:{snapshot.ProfileId} SnapshotCount:{snapshot.Properties.Count} HydratedCount:{result.HydratedCount} Skipped:{result.SkippedCount}", settings);
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
                CoopPersistenceDiagnostics.WriteVerbose($"Co-op property save data deserialize skipped Error:{ex.Message}");
                return null;
            }
        }

        private string GetPropertyId(GameLocation property)
        {
            return $"{property.GetType().Name}:{property.Name}:{property.EntrancePosition.X:0.###}:{property.EntrancePosition.Y:0.###}:{property.EntrancePosition.Z:0.###}";
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
