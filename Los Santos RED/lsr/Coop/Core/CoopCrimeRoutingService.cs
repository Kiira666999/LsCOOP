using LSR.Vehicles;
using Rage;
using System;
using LosSantosRED.lsr;
using LosSantosRED.lsr.Interface;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCrimeRoutingService
    {
        public static CoopCrimeRoutingService Current { get; } = new CoopCrimeRoutingService();

        private Action<object> crimeRouteSink;
        private global::Mod.Player localPlayer;
        private ICrimes crimes;

        public event Action<CoopCrimeEvent> CrimeRoutedToActiveHost;
        public event Action<CoopCrimeEvent> CrimeAppliedOnActiveHost;

        public static void RegisterCrimeRouteSink(Action<object> sink)
        {
            Current.crimeRouteSink = sink;
        }

        public static void UnregisterCrimeRouteSink()
        {
            Current.crimeRouteSink = null;
        }

        public static void RegisterLocalCrimeRuntime(global::Mod.Player player, ICrimes crimeProvider)
        {
            Current.localPlayer = player;
            Current.crimes = crimeProvider;
        }

        public static bool ReportLocalPlayerOnPlayerViolence(string victimProfileId, int victimPedHandle, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
        {
            return Current.ReportPlayerOnPlayerViolence(GetCurrentProfileId(), victimProfileId, 0, victimPedHandle, wasKilled, wasShot, wasMeleeAttacked, wasHitByVehicle, true);
        }

        public static bool ApplyRemotePlayerOnPlayerViolence(string offenderProfileId, string victimProfileId, int offenderPedHandle, int victimPedHandle, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
        {
            if (!CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsLocalActiveHost)
            {
                return false;
            }

            return Current.ReportPlayerOnPlayerViolence(offenderProfileId, victimProfileId, offenderPedHandle, victimPedHandle, wasKilled, wasShot, wasMeleeAttacked, wasHitByVehicle, false);
        }

        public CoopCrimeEvent CreateLocalCrimeEvent(global::Mod.Player player, global::Crime crime, bool isObservedByPolice, Vector3 location, VehicleExt vehicleObserved, global::WeaponInformation weaponObserved, bool haveDescription, bool announceCrime, bool isForPlayer, bool alwaysAddInstance)
        {
            CoopCrimeActorContext actorContext = CreateLocalActorContext(player, location, vehicleObserved);
            return new CoopCrimeEvent
            {
                WorldId = GetCurrentWorldId(),
                OffenderProfileId = actorContext.OffenderProfileId,
                OffenderCharacterId = actorContext.OffenderCharacterId,
                ActorContext = actorContext,
                Crime = crime,
                CrimeId = crime?.ID ?? string.Empty,
                CrimeName = crime?.Name ?? string.Empty,
                IsObservedByPolice = isObservedByPolice,
                ActorVehicle = vehicleObserved,
                ActorWeapon = weaponObserved,
                Position = location,
                HaveDescription = haveDescription,
                AnnounceCrime = announceCrime,
                IsForPlayer = isForPlayer,
                AlwaysAddInstance = alwaysAddInstance,
                SourceClientId = actorContext.SourceClientId,
            };
        }

        public bool ReportPlayerOnPlayerViolence(string offenderProfileId, string victimProfileId, int offenderPedHandle, int victimPedHandle, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle, bool isLocalOffender)
        {
            if (!CoopStartupBridge.IsCoopEnabled || localPlayer == null || crimes == null || string.IsNullOrWhiteSpace(victimProfileId))
            {
                return false;
            }

            Crime crime = ResolvePlayerViolenceCrime(wasKilled);
            if (crime == null)
            {
                return false;
            }

            CoopCrimeEvent crimeEvent = CreatePlayerOnPlayerCrimeEvent(offenderProfileId, victimProfileId, offenderPedHandle, victimPedHandle, crime, wasKilled, wasShot, wasMeleeAttacked, wasHitByVehicle, isLocalOffender);
            if (!RouteOrAllowLocal(crimeEvent))
            {
                return true;
            }

            ApplyCrimeThroughExistingPlayerLogic(crimeEvent);
            return true;
        }

        public bool RouteOrAllowLocal(CoopCrimeEvent crimeEvent)
        {
            if (crimeEvent == null)
            {
                return true;
            }

            if (!CoopStartupBridge.IsCoopEnabled || CoopStartupBridge.IsLocalActiveHost)
            {
                return true;
            }

            CrimeRoutedToActiveHost?.Invoke(crimeEvent);
            crimeRouteSink?.Invoke(crimeEvent);
            return false;
        }

        public void NotifyAppliedOnActiveHost(CoopCrimeEvent crimeEvent)
        {
            if (crimeEvent == null)
            {
                return;
            }

            if (!CoopStartupBridge.IsCoopEnabled || CoopStartupBridge.IsLocalActiveHost)
            {
                CrimeAppliedOnActiveHost?.Invoke(crimeEvent);
            }
        }

        public bool TryApplyRemoteCrime(CoopCrimeEvent crimeEvent, Action<CoopCrimeEvent> applyExistingCrimeLogic)
        {
            if (crimeEvent == null || applyExistingCrimeLogic == null || !CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsLocalActiveHost)
            {
                return false;
            }

            applyExistingCrimeLogic(crimeEvent);
            CrimeAppliedOnActiveHost?.Invoke(crimeEvent);
            return true;
        }

        private CoopCrimeEvent CreatePlayerOnPlayerCrimeEvent(string offenderProfileId, string victimProfileId, int offenderPedHandle, int victimPedHandle, Crime crime, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle, bool isLocalOffender)
        {
            string resolvedOffenderProfileId = string.IsNullOrWhiteSpace(offenderProfileId) ? GetCurrentProfileId() : offenderProfileId;
            Vector3 position = GetPlayerPosition(localPlayer);
            CoopCrimeActorContext actorContext = CreateLocalActorContext(localPlayer, position, localPlayer?.CurrentSeenVehicle);
            actorContext.OffenderProfileId = new CoopProfileId(resolvedOffenderProfileId);
            actorContext.OffenderCharacterId = new CoopCharacterId(GetCurrentCharacterId(resolvedOffenderProfileId));
            actorContext.ActorPedHandle = offenderPedHandle;
            actorContext.SourceClientId = resolvedOffenderProfileId;
            actorContext.IsLocalActor = isLocalOffender;

            CoopCrimeVictimContext victimContext = new CoopCrimeVictimContext
            {
                VictimProfileId = new CoopProfileId(victimProfileId),
                VictimCharacterId = new CoopCharacterId(GetCurrentCharacterId(victimProfileId)),
                VictimPedHandle = victimPedHandle,
                Position = position,
                IsCoopPlayer = true,
                IsLocalVictim = string.Equals(victimProfileId, CoopStartupBridge.LocalProfileId, StringComparison.OrdinalIgnoreCase),
            };

            return new CoopCrimeEvent
            {
                WorldId = GetCurrentWorldId(),
                OffenderProfileId = actorContext.OffenderProfileId,
                OffenderCharacterId = actorContext.OffenderCharacterId,
                VictimProfileId = victimContext.VictimProfileId,
                VictimCharacterId = victimContext.VictimCharacterId,
                ActorContext = actorContext,
                VictimContext = victimContext,
                Crime = crime,
                CrimeId = crime.ID,
                CrimeName = crime.Name,
                IsPlayerOnPlayerViolence = true,
                WasKilled = wasKilled,
                WasShot = wasShot,
                WasMeleeAttacked = wasMeleeAttacked,
                WasHitByVehicle = wasHitByVehicle,
                IsObservedByPolice = localPlayer?.AnyPoliceCanSeePlayer == true,
                ActorVehicle = localPlayer?.CurrentSeenVehicle,
                ActorWeapon = localPlayer?.WeaponEquipment?.CurrentSeenWeapon,
                Position = position,
                HaveDescription = true,
                AnnounceCrime = true,
                IsForPlayer = isLocalOffender,
                AlwaysAddInstance = true,
                SourceClientId = resolvedOffenderProfileId,
            };
        }

        private Crime ResolvePlayerViolenceCrime(bool wasKilled)
        {
            return crimes?.GetCrime(wasKilled ? StaticStrings.KillingCiviliansCrimeID : StaticStrings.HurtingCiviliansCrimeID);
        }

        private void ApplyCrimeThroughExistingPlayerLogic(CoopCrimeEvent crimeEvent)
        {
            if (crimeEvent?.Crime == null || localPlayer == null)
            {
                return;
            }

            localPlayer.AddCrime(
                crimeEvent.Crime,
                crimeEvent.IsObservedByPolice,
                crimeEvent.Position,
                crimeEvent.ActorVehicle,
                crimeEvent.ActorWeapon,
                crimeEvent.HaveDescription,
                crimeEvent.AnnounceCrime,
                crimeEvent.IsForPlayer,
                crimeEvent.AlwaysAddInstance);
        }

        private CoopCrimeActorContext CreateLocalActorContext(global::Mod.Player player, Vector3 location, VehicleExt vehicleObserved)
        {
            Ped actorPed = player?.Character;
            VehicleExt actorVehicleExt = vehicleObserved ?? player?.CurrentVehicle;
            Vehicle actorVehicle = actorVehicleExt?.Vehicle;
            string profileId = GetCurrentProfileId();
            string characterId = GetCurrentCharacterId(profileId);

            return new CoopCrimeActorContext
            {
                OffenderProfileId = new CoopProfileId(profileId),
                OffenderCharacterId = new CoopCharacterId(characterId),
                ActorPed = actorPed,
                ActorEntity = actorPed,
                ActorPedHandle = 0,
                ActorVehicle = actorVehicle,
                ActorVehicleExt = actorVehicleExt,
                Position = location != Vector3.Zero ? location : GetPlayerPosition(player),
                SourceClientId = profileId,
                IsLocalActor = true,
                IsActiveHostActor = !CoopStartupBridge.IsCoopEnabled || CoopStartupBridge.IsLocalActiveHost,
            };
        }

        private static CoopWorldId GetCurrentWorldId()
        {
            string worldId = CoopStartupBridge.IsCoopEnabled ? CoopStartupBridge.WorldId : "single-player-world";
            return new CoopWorldId(string.IsNullOrWhiteSpace(worldId) ? "single-player-world" : worldId);
        }

        private static string GetCurrentProfileId()
        {
            if (CoopStartupBridge.IsCoopEnabled && !string.IsNullOrWhiteSpace(CoopStartupBridge.LocalProfileId))
            {
                return CoopStartupBridge.LocalProfileId;
            }

            return "single-player-profile";
        }

        private static string GetCurrentCharacterId(string profileId)
        {
            if (!string.IsNullOrWhiteSpace(profileId) && profileId != "single-player-profile")
            {
                return profileId;
            }

            return "single-player-character";
        }

        private static Vector3 GetPlayerPosition(global::Mod.Player player)
        {
            return player == null ? Vector3.Zero : player.Position;
        }
    }
}
