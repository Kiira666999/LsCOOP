using LSR.Vehicles;
using Rage;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LosSantosRED.lsr;
using LosSantosRED.lsr.Interface;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCrimeRoutingService
    {
        public static CoopCrimeRoutingService Current { get; } = new CoopCrimeRoutingService();

        private readonly CoopActionAuthorityService authorityService = new CoopActionAuthorityService();
        private static readonly TimeSpan RemoteActorCrimeCooldown = TimeSpan.FromSeconds(2);
        private global::Mod.Player localPlayer;
        private ICrimes crimes;
        private bool suppressNextLocalCrimeCommit;
        private readonly Dictionary<string, DateTimeOffset> remoteActorCrimeCooldowns = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

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

        public bool TryReportRemotePedVictimCrime(global::PedExt victimPed, int offenderHandle, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
        {
            if (!CanReportRemoteActorCrime(victimPed))
            {
                return false;
            }

            int localPedHandle;
            int localVehicleHandle;
            GetLocalActorHandles(out localPedHandle, out localVehicleHandle);
            CoopRemoteActorState offender;
            if (!CoopRemoteActorRegistry.Current.TryResolveHandle(offenderHandle, localPedHandle, localVehicleHandle, out offender))
            {
                return false;
            }

            return ReportRemotePedVictimCrime(offender, victimPed, wasKilled, wasShot, wasMeleeAttacked, wasHitByVehicle);
        }

        public bool TryReportRemotePedVictimDamage(global::PedExt victimPed, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
        {
            if (!CanReportRemoteActorCrime(victimPed))
            {
                return false;
            }

            int localPedHandle;
            int localVehicleHandle;
            GetLocalActorHandles(out localPedHandle, out localVehicleHandle);
            CoopRemoteActorState offender;
            if (!CoopRemoteActorRegistry.Current.TryResolveDamageSource(victimPed.Pedestrian, localPedHandle, localVehicleHandle, out offender))
            {
                return false;
            }

            return ReportRemotePedVictimCrime(offender, victimPed, false, wasShot, wasMeleeAttacked, wasHitByVehicle);
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
                WantedLevelBefore = player?.WantedLevel ?? 0,
                WantedLevelAfter = player?.WantedLevel ?? 0,
            };
        }

        private bool CanReportRemoteActorCrime(global::PedExt victimPed)
        {
            return CoopStartupBridge.IsCoopEnabled
                && CoopStartupBridge.IsLocalActiveHost
                && localPlayer != null
                && crimes != null
                && victimPed != null
                && victimPed.Pedestrian.Exists();
        }

        private bool ReportRemotePedVictimCrime(CoopRemoteActorState offender, global::PedExt victimPed, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
        {
            if (offender == null || string.IsNullOrWhiteSpace(offender.ProfileId))
            {
                return false;
            }

            Crime crime = ResolvePedVictimCrime(victimPed, wasKilled);
            if (crime == null)
            {
                return false;
            }

            if (ShouldSuppressRemoteActorCrime(offender.ProfileId, unchecked((int)victimPed.Handle), crime.ID))
            {
                return true;
            }

            CoopCrimeEvent crimeEvent = CreateRemotePedVictimCrimeEvent(offender, victimPed, crime, wasKilled, wasShot, wasMeleeAttacked, wasHitByVehicle);
            PublishActiveHostCrimeCommit(crimeEvent);
            CrimeAppliedOnActiveHost?.Invoke(crimeEvent);
            EntryPoint.WriteToConsole($"Co-op remote actor crime attributed Crime:{crimeEvent.CrimeId} Offender:{crimeEvent.OffenderProfileId} Victim:{crimeEvent.VictimType} VictimHandle:{crimeEvent.VictimContext?.VictimPedHandle ?? 0} TemporaryStatePersisted:{crimeEvent.TemporaryStatePersisted}", 0);
            return true;
        }

        private CoopCrimeEvent CreateRemotePedVictimCrimeEvent(CoopRemoteActorState offender, global::PedExt victimPed, Crime crime, bool wasKilled, bool wasShot, bool wasMeleeAttacked, bool wasHitByVehicle)
        {
            string offenderProfileId = offender.ProfileId;
            string offenderCharacterId = string.IsNullOrWhiteSpace(offender.CharacterId) ? GetCurrentCharacterId(offenderProfileId) : offender.CharacterId;
            Vector3 victimPosition = victimPed.Pedestrian.Exists() ? victimPed.Pedestrian.Position : offender.Position;
            string victimType = victimPed.IsCop ? "Police" : "Civilian";
            CoopCrimeActorContext actorContext = new CoopCrimeActorContext
            {
                OffenderProfileId = new CoopProfileId(offenderProfileId),
                OffenderCharacterId = new CoopCharacterId(offenderCharacterId),
                ActorPedHandle = offender.PedHandle,
                ActorVehicle = null,
                ActorVehicleExt = null,
                Position = offender.Position,
                SourceClientId = offenderProfileId,
                IsLocalActor = false,
                IsActiveHostActor = false,
            };
            CoopCrimeVictimContext victimContext = new CoopCrimeVictimContext
            {
                VictimPed = victimPed.Pedestrian,
                VictimEntity = victimPed.Pedestrian,
                VictimPedHandle = unchecked((int)victimPed.Handle),
                Position = victimPosition,
                IsCoopPlayer = false,
                IsLocalVictim = false,
            };

            return new CoopCrimeEvent
            {
                WorldId = GetCurrentWorldId(),
                OffenderProfileId = actorContext.OffenderProfileId,
                OffenderCharacterId = actorContext.OffenderCharacterId,
                ActorContext = actorContext,
                VictimContext = victimContext,
                Crime = crime,
                CrimeId = crime.ID,
                CrimeName = crime.Name,
                IsRemoteActorCrime = true,
                VictimType = victimType,
                WasKilled = wasKilled,
                WasShot = wasShot,
                WasMeleeAttacked = wasMeleeAttacked,
                WasHitByVehicle = wasHitByVehicle,
                IsObservedByPolice = false,
                Position = victimPosition,
                HaveDescription = false,
                AnnounceCrime = false,
                IsForPlayer = false,
                AlwaysAddInstance = true,
                SourceClientId = offenderProfileId,
                TemporaryStatePersisted = false,
                HasLongTermCriminalHistoryAfter = true,
            };
        }

        private Crime ResolvePedVictimCrime(global::PedExt victimPed, bool wasKilled)
        {
            if (victimPed?.IsCop == true)
            {
                return crimes?.GetCrime(wasKilled ? StaticStrings.KillingPoliceCrimeID : StaticStrings.HurtingPoliceCrimeID);
            }

            return crimes?.GetCrime(wasKilled ? StaticStrings.KillingCiviliansCrimeID : StaticStrings.HurtingCiviliansCrimeID);
        }

        private bool ShouldSuppressRemoteActorCrime(string offenderProfileId, int victimHandle, string crimeId)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = $"{offenderProfileId}|{victimHandle.ToString(CultureInfo.InvariantCulture)}|{crimeId}";
            DateTimeOffset lastUtc;
            if (remoteActorCrimeCooldowns.TryGetValue(key, out lastUtc) && now - lastUtc < RemoteActorCrimeCooldown)
            {
                return true;
            }

            remoteActorCrimeCooldowns[key] = now;
            foreach (string staleKey in remoteActorCrimeCooldowns.Where(x => now - x.Value > TimeSpan.FromSeconds(15)).Select(x => x.Key).ToList())
            {
                remoteActorCrimeCooldowns.Remove(staleKey);
            }

            return false;
        }

        private void GetLocalActorHandles(out int localPedHandle, out int localVehicleHandle)
        {
            localPedHandle = 0;
            localVehicleHandle = 0;
            try
            {
                Ped localPed = localPlayer?.Character;
                if (localPed == null || !localPed.Exists())
                {
                    return;
                }

                localPedHandle = (int)localPed.Handle.Value;
                if (localPed.IsInAnyVehicle(false) && localPed.CurrentVehicle.Exists())
                {
                    localVehicleHandle = (int)localPed.CurrentVehicle.Handle.Value;
                }
            }
            catch
            {
                localVehicleHandle = 0;
            }
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

            if (!crimeEvent.IsForPlayer && !crimeEvent.IsRemoteActorCrime)
            {
                CoopPersistenceDiagnostics.WriteVerbose($"Co-op non-player crime report skipped Crime:{crimeEvent.CrimeId} IsForPlayer:{crimeEvent.IsForPlayer} ObservedByPolice:{crimeEvent.IsObservedByPolice} WantedBefore:{crimeEvent.WantedLevelBefore} WantedAfter:{crimeEvent.WantedLevelAfter}");
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
            request.Parameters["CrimePriority"] = crimeEvent.Crime.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["ResultsInLethalForce"] = crimeEvent.Crime.ResultsInLethalForce.ToString();
            request.Parameters["RemoteActorCrime"] = crimeEvent.IsRemoteActorCrime.ToString();
            request.Parameters["RequestedByProfileId"] = GetCurrentProfileId();
            request.Parameters["VictimType"] = crimeEvent.VictimType ?? string.Empty;
            request.Parameters["OffenderPedHandle"] = (crimeEvent.ActorContext?.ActorPedHandle ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["OffenderVehicleHandle"] = (crimeEvent.ActorContext?.ActorVehicle == null ? 0 : (int)crimeEvent.ActorContext.ActorVehicle.Handle.Value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["VictimPedHandle"] = (crimeEvent.VictimContext?.VictimPedHandle ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["WasKilled"] = crimeEvent.WasKilled.ToString();
            request.Parameters["WasShot"] = crimeEvent.WasShot.ToString();
            request.Parameters["WasMeleeAttacked"] = crimeEvent.WasMeleeAttacked.ToString();
            request.Parameters["WasHitByVehicle"] = crimeEvent.WasHitByVehicle.ToString();
            request.Parameters["IsObservedByPolice"] = crimeEvent.IsObservedByPolice.ToString();
            request.Parameters["IsForPlayer"] = crimeEvent.IsForPlayer.ToString();
            request.Parameters["AlwaysAddInstance"] = crimeEvent.AlwaysAddInstance.ToString();
            request.Parameters["HadLongTermCriminalHistoryBefore"] = crimeEvent.HadLongTermCriminalHistoryBefore.ToString();
            request.Parameters["HasLongTermCriminalHistoryAfter"] = crimeEvent.HasLongTermCriminalHistoryAfter.ToString();
            request.Parameters["CreatedLongTermCriminalHistory"] = crimeEvent.CreatedLongTermCriminalHistory.ToString();
            request.Parameters["LongTermCriminalHistoryCrimeCount"] = crimeEvent.LongTermCriminalHistoryCrimeCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["TemporaryStatePersisted"] = crimeEvent.TemporaryStatePersisted.ToString();
            request.Parameters["PositionX"] = crimeEvent.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["PositionY"] = crimeEvent.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            request.Parameters["PositionZ"] = crimeEvent.Position.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);

            CoopGameplayActionResult result = authorityService.CreateAcceptedResult(request, "Crime applied by active host");
            CoopGameplayFileBridge.PublishGameplayCommit(new CoopStorePurchaseCommit
            {
                Request = request,
                Result = result,
            });
            EntryPoint.WriteToConsole($"Co-op active-host crime routed Crime:{crimeEvent.CrimeId} Profile:{crimeEvent.OffenderProfileId} Character:{crimeEvent.OffenderCharacterId} LongTermHistoryCreated:{crimeEvent.CreatedLongTermCriminalHistory} TemporaryStatePersisted:{crimeEvent.TemporaryStatePersisted}", 0);
        }
    }
}
