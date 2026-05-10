using LSR.Vehicles;
using System;
using System.Linq;

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
                return snapshot;
            }

            foreach (VehicleExt vehicle in player.VehicleOwnership.OwnedVehicles.Where(x => x?.Vehicle.Exists() == true))
            {
                snapshot.Vehicles.Add(CreateRecord(vehicle));
            }

            return snapshot;
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
            return new CoopOwnedVehicleRecord
            {
                VehicleId = GetVehicleId(vehicle),
                ModelHash = vehicle.Vehicle.Model.Hash.ToString(),
                ModelName = vehicle.Vehicle.Model.Name ?? string.Empty,
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

        private string GetVehicleId(VehicleExt vehicle)
        {
            string plate = vehicle.CarPlate?.PlateNumber ?? vehicle.Vehicle.LicensePlate ?? string.Empty;
            return $"{vehicle.Vehicle.Model.Hash}:{plate}:{vehicle.Handle}";
        }
    }
}
