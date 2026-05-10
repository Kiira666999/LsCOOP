using System;
using System.Linq;

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
                snapshot.Properties.Add(CreateRecord(property));
            }

            return snapshot;
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

        private string GetPropertyId(GameLocation property)
        {
            return $"{property.GetType().Name}:{property.Name}:{property.EntrancePosition.X:0.###}:{property.EntrancePosition.Y:0.###}:{property.EntrancePosition.Z:0.###}";
        }
    }
}
