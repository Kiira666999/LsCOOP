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

        private readonly CoopActionAuthorityService authorityService = new CoopActionAuthorityService();
        private global::Mod.Player localPlayer;
        private ICrimes crimes;
        private bool suppressNextLocalCrimeCommit;

        public event Action<CoopCrimeEvent> CrimeRoutedToActiveHost;
        public event Action<CoopCrimeEvent> CrimeAppliedOnActiveHost;

        public static void RegisterLocalCrimeRuntime(global::Mod.Player player, ICrimes crimeProvider)
        {
            Current.localPlayer = player;
            Current.crimes = crimeProvider;
        }

        internal static bool ApplyRemotePlayerOnPlayerViolence(string offenderProfileId, string victimProfileId, int offenderPedHandle, int victimPedHandle, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
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
            CoopGameplayFileBridge.PublishPvpCrimeReport(crimeEvent);
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
                PublishActiveHostCrimeCommit(crimeEvent);
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

            bool previousSuppressNextLocalCrimeCommit = suppressNextLocalCrimeCommit;
            suppressNextLocalCrimeCommit = suppressNextLocalCrimeCommit || crimeEvent.IsPlayerOnPlayerViolence;
            try
            {
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
            finally
            {
                suppressNextLocalCrimeCommit = previousSuppressNextLocalCrimeCommit;
            }
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

        private void PublishActiveHostCrimeCommit(CoopCrimeEvent crimeEvent)
        {
            if (crimeEvent == null
                || crimeEvent.Crime == null
                || crimeEvent.IsPlayerOnPlayerViolence
                || suppressNextLocalCrimeCommit
                || !CoopStartupBridge.IsCoopEnabled
                || !CoopStartupBridge.IsLocalActiveHost)
            {
                return;
            }

            CoopGameplayActionRequest request = new CoopGameplayActionRequest
            {
                ActionType = CoopGameplayActionType.CommitCrime,
                WorldId = crimeEvent.WorldId,
                SourceProfileId = crimeEvent.OffenderProfileId,
                SourceCharacterId = crimeEvent.OffenderCharacterId,
                AllowsOptimisticClientFeedback = authorityService.CanUseOptimisticClientFeedback(CoopGameplayActionType.CommitCrime),
                RequestedUtc = crimeEvent.TimestampUtc,
            };

            request.Parameters["CrimeEventId"] = crimeEvent.EventId;
            request.Parameters["CrimeId"] = crimeEvent.CrimeId ?? string.Empty;
            request.Parameters["CrimeName"] = crimeEvent.CrimeName ?? string.Empty;
            request.Parameters["ResultingWantedLevel"] = crimeEvent.Crime.ResultingWantedLevel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["IsObservedByPolice"] = crimeEvent.IsObservedByPolice.ToString();
            request.Parameters["IsForPlayer"] = crimeEvent.IsForPlayer.ToString();
            request.Parameters["AlwaysAddInstance"] = crimeEvent.AlwaysAddInstance.ToString();
            request.Parameters["PositionX"] = crimeEvent.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["PositionY"] = crimeEvent.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["PositionZ"] = crimeEvent.Position.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);

            CoopGameplayActionResult result = authorityService.CreateAcceptedResult(request, "Crime applied by active host");
            CoopGameplayFileBridge.PublishGameplayCommit(new CoopStorePurchaseCommit
            {
                Request = request,
                Result = result,
            });
        }
    }
}
