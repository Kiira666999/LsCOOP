using LSR.Vehicles;
using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class LsrActorContextFactory
    {
        private static readonly CoopCharacterId SinglePlayerCharacterId = new CoopCharacterId("single-player-local");
        private static readonly CoopProfileId SinglePlayerProfileId = new CoopProfileId("single-player-profile");

        public LsrActorContext CreateLocalSinglePlayer(Mod.Player player)
        {
            Ped actorPed = player == null ? null : player.Character;
            Vehicle actorVehicle = GetActorVehicle(player, actorPed);
            Vector3 position = GetActorPosition(player, actorPed);
            string displayName = player == null ? string.Empty : player.PlayerName;

            return Create(
                SinglePlayerCharacterId,
                SinglePlayerProfileId,
                displayName,
                actorPed,
                actorVehicle,
                position,
                true,
                true,
                true,
                true,
                player);
        }

        public LsrActorContext Create(
            CoopCharacterId characterId,
            CoopProfileId profileId,
            string displayName,
            Ped actorPed,
            Vehicle actorVehicle,
            Vector3 position,
            bool isLocal,
            bool isActiveHost,
            bool isAdmin,
            bool isTrustedHost,
            Mod.Player existingPlayer)
        {
            return new LsrActorContext(
                characterId,
                profileId,
                displayName ?? string.Empty,
                actorPed,
                actorVehicle,
                position,
                isLocal,
                isActiveHost,
                isAdmin,
                isTrustedHost,
                existingPlayer);
        }

        private Vehicle GetActorVehicle(Mod.Player player, Ped actorPed)
        {
            VehicleExt currentVehicle = player == null ? null : player.CurrentVehicle;
            if (currentVehicle != null && currentVehicle.Vehicle.Exists())
            {
                return currentVehicle.Vehicle;
            }

            if (actorPed != null && actorPed.Exists() && actorPed.IsInAnyVehicle(false) && actorPed.CurrentVehicle.Exists())
            {
                return actorPed.CurrentVehicle;
            }

            return null;
        }

        private Vector3 GetActorPosition(Mod.Player player, Ped actorPed)
        {
            if (actorPed != null && actorPed.Exists())
            {
                return actorPed.Position;
            }

            if (player != null)
            {
                return player.Position;
            }

            return Vector3.Zero;
        }
    }
}
