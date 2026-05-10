using RageCoop.Core.Scripting;
using RageCoop.Client.Scripting;
using GTA;
using GTA.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace LsrCoop.Client
{
    public class Main : ClientScript
    {
        private const string CoopBuildVersion = "0.1.0";
        private const string ConfigVersion = "1";

        private static readonly int PingEventHash = CustomEvents.Hash("lsrcoop.ping");
        private static readonly int PongEventHash = CustomEvents.Hash("lsrcoop.pong");
        private static readonly int CompatibilityReportEventHash = CustomEvents.Hash("lsrcoop.compatibility.report");
        private static readonly int CompatibilityStatusEventHash = CustomEvents.Hash("lsrcoop.compatibility.status");
        private static readonly int PlayerRegisteredEventHash = CustomEvents.Hash("lsrcoop.player.registered");
        private static readonly int CharacterCreateRequiredEventHash = CustomEvents.Hash("lsrcoop.character.createRequired");
        private static readonly int CharacterCreatedEventHash = CustomEvents.Hash("lsrcoop.character.created");
        private static readonly int CharacterSnapshotEventHash = CustomEvents.Hash("lsrcoop.character.snapshot");
        private static readonly int CharacterSnapshotAckEventHash = CustomEvents.Hash("lsrcoop.character.snapshotAck");
        private static readonly int AppearanceChangeRequestedEventHash = CustomEvents.Hash("lsrcoop.appearance.changeRequested");
        private static readonly int AppearanceChangedEventHash = CustomEvents.Hash("lsrcoop.appearance.changed");
        private static readonly int ActiveHostAssignedEventHash = CustomEvents.Hash("lsrcoop.activeHost.assigned");
        private static readonly int ActiveHostReleasedEventHash = CustomEvents.Hash("lsrcoop.activeHost.released");
        private static readonly int ActiveHostUnavailableEventHash = CustomEvents.Hash("lsrcoop.activeHost.unavailable");
        private static readonly int WorldSnapshotEventHash = CustomEvents.Hash("lsrcoop.world.snapshot");
        private static readonly int GameplayActionCommittedEventHash = CustomEvents.Hash("lsrcoop.gameplay.action.committed");
        private static readonly int GameplayActionResultEventHash = CustomEvents.Hash("lsrcoop.gameplay.action.result");
        private static readonly int PvpCrimeReportedEventHash = CustomEvents.Hash("lsrcoop.crime.pvp.reported");
        private static readonly int PvpCrimeAssignedEventHash = CustomEvents.Hash("lsrcoop.crime.pvp.assigned");
        private static readonly int CriminalJusticeSnapshotCommittedEventHash = CustomEvents.Hash("lsrcoop.criminalJustice.snapshot.committed");
        private static readonly int GangReputationSnapshotCommittedEventHash = CustomEvents.Hash("lsrcoop.gangReputation.snapshot.committed");

        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly CoopAppearanceCaptureService appearanceCaptureService = new CoopAppearanceCaptureService();
        private readonly CoopAppearanceApplyService appearanceApplyService = new CoopAppearanceApplyService();
        private readonly Dictionary<string, PvpVictimDamageState> pvpVictimDamageStates = new Dictionary<string, PvpVictimDamageState>(StringComparer.OrdinalIgnoreCase);
        private CoopWorldSnapshotDto latestWorldSnapshot;
        private string localWorldId;
        private string localProfileId;
        private string activeHostProfileId;
        private bool localCharacterReadyForSimulation;
        private bool characterCreationRequired;
        private bool characterCreationRequestSent;
        private DateTimeOffset characterCreateRequiredUtc;
        private bool lsrGameplayBridgeRegistered;
        private DateTimeOffset lastLsrBridgeRegistrationAttemptUtc;

        public Main()
        {
            serializer.MaxJsonLength = int.MaxValue;
        }

        public override void OnStart()
        {
            Logger.Info("[LsrCoop.Client] loaded");
            UpdateBridgeWaiting(localWorldId);
            RegisterCustomEventStubs();
            RegisterLsrGameplayBridge();
            SendLoadPing();
            SendCompatibilityReport();
        }

        public override void OnStop()
        {
            ExitCharacterCreationSafeState();
            UnregisterLsrGameplayBridge();
            SetBridgeDisabled();
            Logger.Info("[LsrCoop.Client] stopped");
        }

        private void RegisterCustomEventStubs()
        {
            API.RegisterCustomEventHandler(PongEventHash, OnPongReceived);
            API.RegisterCustomEventHandler(CompatibilityStatusEventHash, OnCompatibilityStatusReceived);
            API.RegisterCustomEventHandler(PlayerRegisteredEventHash, OnPlayerRegistered);
            API.RegisterCustomEventHandler(CharacterCreateRequiredEventHash, OnCharacterCreateRequired);
            API.RegisterCustomEventHandler(CharacterSnapshotEventHash, OnCharacterSnapshot);
            API.RegisterCustomEventHandler(AppearanceChangedEventHash, OnAppearanceChanged);
            API.RegisterCustomEventHandler(ActiveHostAssignedEventHash, OnActiveHostAssigned);
            API.RegisterCustomEventHandler(ActiveHostReleasedEventHash, OnActiveHostReleased);
            API.RegisterCustomEventHandler(ActiveHostUnavailableEventHash, OnActiveHostUnavailable);
            API.RegisterCustomEventHandler(WorldSnapshotEventHash, OnWorldSnapshotReceived);
            API.RegisterCustomEventHandler(GameplayActionResultEventHash, OnGameplayActionResultReceived);
            API.RegisterCustomEventHandler(PvpCrimeAssignedEventHash, OnPvpCrimeAssigned);
            Logger.Info("[LsrCoop.Client] custom event stubs registered");
        }

        public void OnTick()
        {
            EnsureLsrGameplayBridgeRegistered();
            DetectPlayerOnPlayerViolence();
        }

        private void SendLoadPing()
        {
            if (!API.IsOnServer)
            {
                Logger.Info("[LsrCoop.Client] not connected; ping skipped");
                return;
            }

            API.SendCustomEvent(PingEventHash, new object[] { "client-loaded" });
            Logger.Info("[LsrCoop.Client] ping sent");
        }

        private void SendCompatibilityReport()
        {
            if (!API.IsOnServer)
            {
                Logger.Info("[LsrCoop.Client] not connected; compatibility report skipped");
                return;
            }

            API.SendCustomEvent(CompatibilityReportEventHash, new object[]
            {
                CoopBuildVersion,
                GetLsrVersion(),
                ConfigVersion,
                true
            });

            Logger.Info("[LsrCoop.Client] compatibility report sent");
        }

        private void OnPongReceived(CustomEventReceivedArgs args)
        {
            string message = args?.Args != null && args.Args.Length > 0 ? args.Args[0]?.ToString() : string.Empty;
            Logger.Info($"[LsrCoop.Client] pong received: {message}");
        }

        private void OnCompatibilityStatusReceived(CustomEventReceivedArgs args)
        {
            string state = GetArg(args, 0);
            string requiredBuild = GetArg(args, 1);
            string requiredConfig = GetArg(args, 2);
            string requiredLsr = GetArg(args, 3);
            Logger.Info($"[LsrCoop.Client] compatibility status: {state}, requiredBuild={requiredBuild}, requiredConfig={requiredConfig}, requiredLsr={requiredLsr}");
        }

        private void OnPlayerRegistered(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            string profileId = GetArg(args, 1);
            string role = GetArg(args, 4);
            string compatibility = GetArg(args, 5);
            if (!string.Equals(localProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                localCharacterReadyForSimulation = false;
            }
            localWorldId = worldId;
            localProfileId = profileId;
            if (string.IsNullOrWhiteSpace(activeHostProfileId))
            {
                UpdateBridgeSession();
            }
            else
            {
                UpdateBridgeActiveHost(localWorldId, activeHostProfileId);
            }
            Logger.Info($"[LsrCoop.Client] player registered: world={worldId}, profile={profileId}, role={role}, compatibility={compatibility}");
        }

        private void OnActiveHostAssigned(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            string activeHostProfileId = GetArg(args, 1);
            this.activeHostProfileId = activeHostProfileId;
            UpdateBridgeActiveHost(worldId, activeHostProfileId);
            Logger.Info($"[LsrCoop.Client] active host assigned: world={worldId}, profile={activeHostProfileId}");
        }

        private void OnActiveHostReleased(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            activeHostProfileId = string.Empty;
            pvpVictimDamageStates.Clear();
            UpdateBridgeWaiting(worldId);
            Logger.Info($"[LsrCoop.Client] active host released: world={worldId}, profile={GetArg(args, 1)}");
        }

        private void OnActiveHostUnavailable(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            activeHostProfileId = string.Empty;
            pvpVictimDamageStates.Clear();
            UpdateBridgeWaiting(worldId);
            Logger.Info($"[LsrCoop.Client] active host unavailable: world={worldId}");
        }

        private void OnWorldSnapshotReceived(CustomEventReceivedArgs args)
        {
            CoopWorldSnapshotDto snapshot = Deserialize<CoopWorldSnapshotDto>(GetArg(args, 0));
            if (snapshot == null)
            {
                return;
            }

            latestWorldSnapshot = snapshot;
            localWorldId = string.IsNullOrWhiteSpace(snapshot.WorldId) ? localWorldId : snapshot.WorldId;
            activeHostProfileId = snapshot.ActiveHostProfileId ?? string.Empty;
            pvpVictimDamageStates.Clear();

            if (string.IsNullOrWhiteSpace(activeHostProfileId))
            {
                UpdateBridgeWaiting(localWorldId);
            }
            else
            {
                UpdateBridgeActiveHost(localWorldId, activeHostProfileId);
            }

            foreach (CoopPlayerProfileSnapshotDto profile in snapshot.Profiles ?? new List<CoopPlayerProfileSnapshotDto>())
            {
                ApplyAppearanceIfLocal(snapshot.WorldId, profile.ProfileId, profile.Character?.Appearance);
            }

            Logger.Info($"[LsrCoop.Client] world snapshot received: world={snapshot.WorldId}, profiles={snapshot.Profiles?.Count ?? 0}, activeHost={activeHostProfileId}, reason={snapshot.Reason}");
        }

        private void OnCharacterCreateRequired(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            string profileId = GetArg(args, 1);
            if (string.Equals(profileId, localProfileId, StringComparison.OrdinalIgnoreCase))
            {
                localCharacterReadyForSimulation = false;
                EnterCharacterCreationSafeState(worldId, profileId);
                UpdateCurrentBridgeState();
            }
            Logger.Info($"[LsrCoop.Client] character create required: world={worldId}, profile={profileId}");
        }

        private void OnCharacterSnapshot(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            string profileId = GetArg(args, 1);
            CoopCharacterSnapshot snapshot = Deserialize<CoopCharacterSnapshot>(GetArg(args, 2));
            ApplyAppearanceIfLocal(worldId, profileId, snapshot?.Appearance);
            if (string.Equals(profileId, localProfileId, StringComparison.OrdinalIgnoreCase))
            {
                localCharacterReadyForSimulation = snapshot != null;
                if (localCharacterReadyForSimulation)
                {
                    ExitCharacterCreationSafeState();
                }
                UpdateCurrentBridgeState();
                SendCharacterSnapshotAck(worldId, profileId);
            }
            Logger.Info($"[LsrCoop.Client] character snapshot received: world={worldId}, profile={profileId}");
        }

        private void EnterCharacterCreationSafeState(string worldId, string profileId)
        {
            localWorldId = string.IsNullOrWhiteSpace(worldId) ? localWorldId : worldId;
            localProfileId = string.IsNullOrWhiteSpace(profileId) ? localProfileId : profileId;
            characterCreationRequired = true;
            characterCreationRequestSent = false;
            characterCreateRequiredUtc = DateTimeOffset.UtcNow;
            ApplyCharacterCreationSafeState(true);
            TryOpenExistingLsrCustomizer();
            Logger.Info("[LsrCoop.Client] character creation required; local player safe-stated");
        }

        private void ExitCharacterCreationSafeState()
        {
            if (!characterCreationRequired && !characterCreationRequestSent)
            {
                return;
            }

            characterCreationRequired = false;
            characterCreationRequestSent = false;
            ApplyCharacterCreationSafeState(false);
            Logger.Info("[LsrCoop.Client] character creation safe-state cleared");
        }

        private void EnsureLsrGameplayBridgeRegistered()
        {
            if (lsrGameplayBridgeRegistered || DateTimeOffset.UtcNow - lastLsrBridgeRegistrationAttemptUtc < TimeSpan.FromSeconds(3))
            {
                return;
            }

            lastLsrBridgeRegistrationAttemptUtc = DateTimeOffset.UtcNow;
            lsrGameplayBridgeRegistered = RegisterLsrGameplayBridge();
        }

        private void ApplyCharacterCreationSafeState(bool enabled)
        {
            Ped ped = Game.Player?.Character;
            if (ped == null || !ped.Exists())
            {
                return;
            }

            Function.Call(Hash.FREEZE_ENTITY_POSITION, ped.Handle, enabled);
            Function.Call(Hash.SET_ENTITY_INVINCIBLE, ped.Handle, enabled);
        }

        private void TryOpenExistingLsrCustomizer()
        {
            Logger.Info("[LsrCoop.Client] press Shift+F10 to open LSR co-op BootstrapOnly character creation");
        }

        private void OnLsrCharacterCreated(object character)
        {
            string modelName = GetString(character, "ModelName");
            string displayName = GetString(character, "PlayerName");
            SendCharacterCreatedRequest(modelName, displayName);
        }

        private void SendCharacterCreatedRequest(string modelName, string displayName)
        {
            if (characterCreationRequestSent)
            {
                return;
            }

            if (!API.IsOnServer || string.IsNullOrWhiteSpace(localWorldId) || string.IsNullOrWhiteSpace(localProfileId))
            {
                Logger.Info("[LsrCoop.Client] character created request skipped; client is not registered");
                return;
            }

            Ped ped = Game.Player?.Character;
            if (ped == null || !ped.Exists())
            {
                Logger.Info("[LsrCoop.Client] character created request skipped; local ped unavailable");
                return;
            }

            modelName = string.IsNullOrWhiteSpace(modelName) ? GetPedModelName(ped) : modelName;
            displayName = string.IsNullOrWhiteSpace(displayName) ? localProfileId : displayName;
            CoopAppearanceState appearance = appearanceCaptureService.Capture(ped, modelName);
            if (appearance == null)
            {
                Logger.Info("[LsrCoop.Client] character created request skipped; appearance unavailable");
                return;
            }

            CoopCharacterCreatedRequest request = new CoopCharacterCreatedRequest
            {
                WorldId = localWorldId,
                ProfileId = localProfileId,
                Character = new CoopCharacterSnapshot
                {
                    CharacterId = localProfileId,
                    ProfileId = localProfileId,
                    DisplayName = displayName,
                    ModelName = modelName,
                    Appearance = appearance
                }
            };

            characterCreationRequestSent = true;
            API.SendCustomEvent(CharacterCreatedEventHash, new object[] { serializer.Serialize(request) });
            Logger.Info($"[LsrCoop.Client] character created request sent: world={localWorldId}, profile={localProfileId}, model={modelName}");
        }

        private string GetPedModelName(Ped ped)
        {
            if (ped == null || !ped.Exists())
            {
                return string.Empty;
            }

            int modelHash = Function.Call<int>(Hash.GET_ENTITY_MODEL, ped.Handle);
            return modelHash.ToString();
        }

        private void OnAppearanceChanged(CustomEventReceivedArgs args)
        {
            CoopAppearanceChanged changed = Deserialize<CoopAppearanceChanged>(GetArg(args, 0));
            if (changed == null)
            {
                return;
            }

            ApplyAppearanceIfLocal(changed.WorldId, changed.ProfileId, changed.Appearance);
            Logger.Info($"[LsrCoop.Client] appearance changed: world={changed.WorldId}, profile={changed.ProfileId}, source={changed.SourceProfileId}");
        }

        private void OnGameplayActionResultReceived(CustomEventReceivedArgs args)
        {
            CoopGameplayActionResultDto result = Deserialize<CoopGameplayActionResultDto>(GetArg(args, 0));
            if (result == null)
            {
                return;
            }

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopStorePurchaseBridge",
                "HandlePurchaseResult",
                result.RequestId ?? string.Empty,
                result.Accepted,
                result.RequiresResync,
                result.Reason ?? string.Empty);

            Logger.Info($"[LsrCoop.Client] gameplay action result: request={result.RequestId}, accepted={result.Accepted}, resync={result.RequiresResync}");
        }

        private bool RegisterLsrGameplayBridge()
        {
            bool registered = InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopStorePurchaseBridge",
                "RegisterPurchaseCommitSink",
                new Action<object>(OnLsrPurchaseCommitted));

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopDeathArrestBridge",
                "RegisterOutcomeCommitSink",
                new Action<object>(OnLsrPurchaseCommitted));

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCrimeRoutingService",
                "RegisterCrimeRouteSink",
                new Action<object>(OnLsrCrimeRouted));

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCriminalJusticeStateAdapter",
                "RegisterSnapshotCommittedSink",
                new Action<object>(OnLsrCriminalJusticeSnapshotCommitted));

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopGangReputationStateAdapter",
                "RegisterSnapshotCommittedSink",
                new Action<object>(OnLsrGangReputationSnapshotCommitted));

            registered = InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCharacterCreationBridge",
                "RegisterCharacterCreatedSink",
                new Action<object>(OnLsrCharacterCreated)) || registered;

            if (registered)
            {
                Logger.Info("[LsrCoop.Client] LSR gameplay bridge registered");
            }

            return registered;
        }

        private void UnregisterLsrGameplayBridge()
        {
            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopStorePurchaseBridge",
                "UnregisterPurchaseCommitSink");

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopDeathArrestBridge",
                "UnregisterOutcomeCommitSink");

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCrimeRoutingService",
                "UnregisterCrimeRouteSink");

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCriminalJusticeStateAdapter",
                "UnregisterSnapshotCommittedSink");

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopGangReputationStateAdapter",
                "UnregisterSnapshotCommittedSink");

            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCharacterCreationBridge",
                "UnregisterCharacterCreatedSink");
        }

        private void OnLsrPurchaseCommitted(object commit)
        {
            if (!API.IsOnServer)
            {
                Logger.Info("[LsrCoop.Client] gameplay commit skipped; client is not connected");
                return;
            }

            CoopStorePurchaseCommitDto dto = ConvertPurchaseCommit(commit);
            if (dto?.Request == null || dto.Result == null)
            {
                Logger.Info("[LsrCoop.Client] gameplay commit skipped; payload unavailable");
                return;
            }

            API.SendCustomEvent(GameplayActionCommittedEventHash, new object[] { serializer.Serialize(dto) });
            Logger.Info($"[LsrCoop.Client] gameplay commit sent: request={dto.Request.RequestId}, type={dto.Request.ActionType}");
        }

        private void OnLsrCrimeRouted(object crimeEvent)
        {
            CoopPvpCrimeReportDto dto = ConvertPvpCrimeReport(crimeEvent);
            if (dto == null || string.IsNullOrWhiteSpace(dto.TargetProfileId))
            {
                return;
            }

            SendPvpCrimeReport(dto);
        }

        private void OnLsrCriminalJusticeSnapshotCommitted(object snapshot)
        {
            if (!API.IsOnServer)
            {
                return;
            }

            CoopCriminalJusticeStateSnapshotDto dto = ConvertCriminalJusticeSnapshot(snapshot);
            if (dto == null || string.IsNullOrWhiteSpace(dto.ProfileId))
            {
                return;
            }

            API.SendCustomEvent(CriminalJusticeSnapshotCommittedEventHash, new object[] { serializer.Serialize(dto) });
            Logger.Info($"[LsrCoop.Client] criminal justice snapshot sent: profile={dto.ProfileId}, history={dto.CriminalHistory?.HasHistory}");
        }

        private void OnLsrGangReputationSnapshotCommitted(object snapshot)
        {
            if (!API.IsOnServer)
            {
                return;
            }

            CoopGangReputationStateDto dto = ConvertGangReputationState(snapshot);
            if (dto == null || string.IsNullOrWhiteSpace(dto.ProfileId))
            {
                return;
            }

            API.SendCustomEvent(GangReputationSnapshotCommittedEventHash, new object[] { serializer.Serialize(dto) });
            Logger.Info($"[LsrCoop.Client] gang reputation snapshot sent: profile={dto.ProfileId}, gangs={dto.Reputations?.Count ?? 0}");
        }

        private void DetectPlayerOnPlayerViolence()
        {
            if (!API.IsOnServer || string.IsNullOrWhiteSpace(localProfileId))
            {
                return;
            }

            foreach (object remotePlayer in API.Players.Values)
            {
                string remoteProfileId = GetString(remotePlayer, "Username");
                if (string.IsNullOrWhiteSpace(remoteProfileId) || string.Equals(remoteProfileId, localProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Ped remotePed = GetRemotePlayerPed(remotePlayer);
                if (remotePed == null || !remotePed.Exists())
                {
                    pvpVictimDamageStates.Remove(remoteProfileId);
                    continue;
                }

                CheckRemotePlayerDamage(remoteProfileId, remotePed);
            }
        }

        private void CheckRemotePlayerDamage(string remoteProfileId, Ped remotePed)
        {
            if (!pvpVictimDamageStates.TryGetValue(remoteProfileId, out PvpVictimDamageState state))
            {
                pvpVictimDamageStates[remoteProfileId] = new PvpVictimDamageState(remotePed.Health, remotePed.Armor, remotePed.IsDead);
                return;
            }

            bool healthChanged = remotePed.Health < state.Health || remotePed.Armor < state.Armor;
            bool killedNow = remotePed.IsDead && !state.IsDead;
            bool damagedByLocalPlayer = Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, remotePed.Handle, Game.Player.Character.Handle, true);
            bool canReport = DateTimeOffset.UtcNow - state.LastReportedUtc > TimeSpan.FromMilliseconds(1200);

            if (damagedByLocalPlayer && canReport && (healthChanged || killedNow))
            {
                CoopPvpCrimeReportDto report = CreatePvpCrimeReport(remoteProfileId, remotePed, killedNow);
                InvokeLsrStaticBridge(
                    "LosSantosRED.lsr.Coop.Core.CoopCrimeRoutingService",
                    "ReportLocalPlayerOnPlayerViolence",
                    report.TargetProfileId,
                    report.VictimPedHandle,
                    report.WasKilled,
                    report.WasShot,
                    report.WasMeleeAttacked,
                    report.WasHitByVehicle);

                if (!string.Equals(localProfileId, activeHostProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    SendPvpCrimeReport(report);
                }

                state.LastReportedUtc = DateTimeOffset.UtcNow;
            }

            state.Health = remotePed.Health;
            state.Armor = remotePed.Armor;
            state.IsDead = remotePed.IsDead;
        }

        private CoopPvpCrimeReportDto CreatePvpCrimeReport(string victimProfileId, Ped victimPed, bool wasKilled)
        {
            return new CoopPvpCrimeReportDto
            {
                WorldId = localWorldId ?? string.Empty,
                SourceProfileId = localProfileId ?? string.Empty,
                SourceCharacterId = localProfileId ?? string.Empty,
                TargetProfileId = victimProfileId ?? string.Empty,
                TargetCharacterId = victimProfileId ?? string.Empty,
                OffenderPedHandle = Game.Player.Character?.Handle ?? 0,
                VictimPedHandle = victimPed?.Handle ?? 0,
                WasKilled = wasKilled,
                WasShot = Function.Call<bool>(Hash.IS_PED_SHOOTING, Game.Player.Character.Handle),
                WasMeleeAttacked = Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, Game.Player.Character.Handle),
                WasHitByVehicle = Function.Call<bool>(Hash.IS_PED_IN_ANY_VEHICLE, Game.Player.Character.Handle, false),
                PositionX = victimPed?.Position.X ?? 0.0f,
                PositionY = victimPed?.Position.Y ?? 0.0f,
                PositionZ = victimPed?.Position.Z ?? 0.0f,
            };
        }

        private void SendPvpCrimeReport(CoopPvpCrimeReportDto report)
        {
            if (!API.IsOnServer || report == null)
            {
                return;
            }

            API.SendCustomEvent(PvpCrimeReportedEventHash, new object[] { serializer.Serialize(report) });
            Logger.Info($"[LsrCoop.Client] PvP crime reported: offender={report.SourceProfileId}, victim={report.TargetProfileId}, killed={report.WasKilled}");
        }

        private void OnPvpCrimeAssigned(CustomEventReceivedArgs args)
        {
            CoopPvpCrimeReportDto report = Deserialize<CoopPvpCrimeReportDto>(GetArg(args, 0));
            if (report == null || !string.Equals(localProfileId, activeHostProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Ped offenderPed = ResolveTargetPed(report.SourceProfileId);
            Ped victimPed = ResolveTargetPed(report.TargetProfileId);
            InvokeLsrStaticBridge(
                "LosSantosRED.lsr.Coop.Core.CoopCrimeRoutingService",
                "ApplyRemotePlayerOnPlayerViolence",
                report.SourceProfileId ?? string.Empty,
                report.TargetProfileId ?? string.Empty,
                offenderPed?.Handle ?? report.OffenderPedHandle,
                victimPed?.Handle ?? report.VictimPedHandle,
                report.WasKilled,
                report.WasShot,
                report.WasMeleeAttacked,
                report.WasHitByVehicle);

            Logger.Info($"[LsrCoop.Client] PvP crime assigned to active host: offender={report.SourceProfileId}, victim={report.TargetProfileId}");
        }

        public void SendLocalAppearanceApplyRequest(string modelName)
        {
            if (!API.IsOnServer || string.IsNullOrWhiteSpace(localProfileId))
            {
                Logger.Info("[LsrCoop.Client] appearance request skipped; client is not registered");
                return;
            }

            CoopAppearanceState appearance = appearanceCaptureService.Capture(Game.Player.Character, modelName);
            if (appearance == null)
            {
                Logger.Info("[LsrCoop.Client] appearance request skipped; local ped unavailable");
                return;
            }

            CoopAppearanceChangeRequest request = new CoopAppearanceChangeRequest
            {
                WorldId = localWorldId,
                ProfileId = localProfileId,
                TargetProfileId = localProfileId,
                Appearance = appearance
            };

            API.SendCustomEvent(AppearanceChangeRequestedEventHash, new object[] { serializer.Serialize(request) });
            Logger.Info("[LsrCoop.Client] appearance change requested");
        }

        private void ApplyAppearanceIfLocal(string worldId, string profileId, CoopAppearanceState appearance)
        {
            if (appearance == null)
            {
                return;
            }

            if (!string.Equals(worldId, localWorldId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Ped targetPed = ResolveTargetPed(profileId);
            if (targetPed == null || !targetPed.Exists())
            {
                Logger.Info($"[LsrCoop.Client] appearance queued; target ped unavailable: {profileId}");
                return;
            }

            appearanceApplyService.TryApply(targetPed, appearance);
        }

        private Ped ResolveTargetPed(string profileId)
        {
            if (string.Equals(profileId, localProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return Game.Player.Character;
            }

            return API.Players.Values
                .FirstOrDefault(x => string.Equals(x.Username, profileId, StringComparison.OrdinalIgnoreCase))
                ?.Character
                ?.MainPed;
        }

        private Ped GetRemotePlayerPed(object remotePlayer)
        {
            return GetPropertyValue(GetPropertyValue(remotePlayer, "Character"), "MainPed") as Ped;
        }

        private string GetLsrVersion()
        {
            string lsrPath = FindLsrPluginPath();
            if (string.IsNullOrWhiteSpace(lsrPath))
            {
                return "unknown";
            }

            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(lsrPath);
            return string.IsNullOrWhiteSpace(versionInfo.FileVersion) ? "unknown" : versionInfo.FileVersion;
        }

        private string FindLsrPluginPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string currentDirectory = Directory.GetCurrentDirectory();
            string[] candidates =
            {
                Path.Combine(baseDirectory, "Plugins", "Los Santos RED.dll"),
                Path.Combine(baseDirectory, "Los Santos RED.dll"),
                Path.Combine(currentDirectory, "Plugins", "Los Santos RED.dll"),
                Path.Combine(currentDirectory, "Los Santos RED.dll"),
                Path.Combine(Directory.GetParent(baseDirectory)?.FullName ?? string.Empty, "Plugins", "Los Santos RED.dll")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private string GetArg(CustomEventReceivedArgs args, int index)
        {
            if (args?.Args == null || args.Args.Length <= index)
            {
                return string.Empty;
            }

            return args.Args[index]?.ToString() ?? string.Empty;
        }

        private T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return serializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] failed to read event payload: {ex.Message}");
                return null;
            }
        }

        private void UpdateBridgeSession()
        {
            WriteBridgeState(true, false, localWorldId, localProfileId, string.Empty, localCharacterReadyForSimulation);
            InvokeLsrBridge("SetSession", localWorldId ?? string.Empty, localProfileId ?? string.Empty, localCharacterReadyForSimulation);
        }

        private void UpdateBridgeActiveHost(string worldId, string activeHostProfileId)
        {
            localWorldId = string.IsNullOrWhiteSpace(worldId) ? localWorldId : worldId;
            WriteBridgeState(true, true, localWorldId, localProfileId, activeHostProfileId, localCharacterReadyForSimulation);
            InvokeLsrBridge("SetActiveHost", localWorldId ?? string.Empty, activeHostProfileId ?? string.Empty, localProfileId ?? string.Empty, localCharacterReadyForSimulation);
        }

        private void UpdateBridgeWaiting(string worldId)
        {
            localWorldId = string.IsNullOrWhiteSpace(worldId) ? localWorldId : worldId;
            WriteBridgeState(true, false, localWorldId, localProfileId, string.Empty, localCharacterReadyForSimulation);
            InvokeLsrBridge("ClearActiveHost", localWorldId ?? string.Empty);
        }

        private void SetBridgeDisabled()
        {
            WriteBridgeState(false, false, string.Empty, string.Empty, string.Empty, false);
            activeHostProfileId = string.Empty;
            localCharacterReadyForSimulation = false;
            InvokeLsrBridge("SetDisabled");
        }

        private void UpdateCurrentBridgeState()
        {
            if (string.IsNullOrWhiteSpace(activeHostProfileId))
            {
                UpdateBridgeSession();
                return;
            }

            UpdateBridgeActiveHost(localWorldId, activeHostProfileId);
        }

        private void SendCharacterSnapshotAck(string worldId, string profileId)
        {
            if (!API.IsOnServer)
            {
                return;
            }

            API.SendCustomEvent(CharacterSnapshotAckEventHash, new object[] { worldId ?? string.Empty, profileId ?? string.Empty });
            Logger.Info($"[LsrCoop.Client] character snapshot ack sent: world={worldId}, profile={profileId}");
        }

        private void InvokeLsrBridge(string methodName, params object[] args)
        {
            InvokeLsrStaticBridge("LosSantosRED.lsr.Coop.Core.CoopStartupBridge", methodName, args);
        }

        private bool InvokeLsrStaticBridge(string typeName, string methodName, params object[] args)
        {
            try
            {
                Type bridgeType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(x => x.GetType(typeName, false))
                    .FirstOrDefault(x => x != null);

                MethodInfo method = bridgeType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(null, args);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] LSR bridge update skipped: {ex.Message}");
                return false;
            }
        }

        private CoopStorePurchaseCommitDto ConvertPurchaseCommit(object commit)
        {
            object request = GetPropertyValue(commit, "Request");
            object result = GetPropertyValue(commit, "Result");
            object snapshot = GetPropertyValue(commit, "InventoryMoneySnapshot");
            object weaponSnapshot = GetPropertyValue(commit, "WeaponSnapshot");
            object propertySnapshot = GetPropertyValue(commit, "PropertyOwnershipSnapshot");
            object ownedVehicleSnapshot = GetPropertyValue(commit, "OwnedVehicleSnapshot");
            object deathArrestState = GetPropertyValue(commit, "DeathArrestState");

            return new CoopStorePurchaseCommitDto
            {
                Request = ConvertActionRequest(request),
                Result = ConvertActionResult(result),
                InventoryMoneySnapshot = ConvertInventoryMoneySnapshot(snapshot),
                WeaponSnapshot = ConvertWeaponSnapshot(weaponSnapshot),
                PropertyOwnershipSnapshot = ConvertPropertyOwnershipSnapshot(propertySnapshot),
                OwnedVehicleSnapshot = ConvertOwnedVehicleSnapshot(ownedVehicleSnapshot),
                DeathArrestState = ConvertDeathArrestState(deathArrestState),
            };
        }

        private CoopPvpCrimeReportDto ConvertPvpCrimeReport(object crimeEvent)
        {
            if (crimeEvent == null)
            {
                return null;
            }

            object victimContext = GetPropertyValue(crimeEvent, "VictimContext");
            return new CoopPvpCrimeReportDto
            {
                EventId = GetString(crimeEvent, "EventId"),
                WorldId = GetString(crimeEvent, "WorldId"),
                SourceProfileId = GetString(crimeEvent, "OffenderProfileId"),
                SourceCharacterId = GetString(crimeEvent, "OffenderCharacterId"),
                TargetProfileId = GetString(crimeEvent, "VictimProfileId"),
                TargetCharacterId = GetString(crimeEvent, "VictimCharacterId"),
                OffenderPedHandle = GetInt(GetPropertyValue(crimeEvent, "ActorContext"), "ActorPedHandle"),
                VictimPedHandle = GetInt(victimContext, "VictimPedHandle"),
                WasKilled = GetBool(crimeEvent, "WasKilled"),
                WasShot = GetBool(crimeEvent, "WasShot"),
                WasMeleeAttacked = GetBool(crimeEvent, "WasMeleeAttacked"),
                WasHitByVehicle = GetBool(crimeEvent, "WasHitByVehicle"),
                PositionX = GetVectorComponent(crimeEvent, "Position", "X"),
                PositionY = GetVectorComponent(crimeEvent, "Position", "Y"),
                PositionZ = GetVectorComponent(crimeEvent, "Position", "Z"),
                TimestampUtc = GetDateTimeOffset(crimeEvent, "TimestampUtc"),
            };
        }

        private CoopCriminalJusticeStateSnapshotDto ConvertCriminalJusticeSnapshot(object snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            return new CoopCriminalJusticeStateSnapshotDto
            {
                SnapshotId = GetString(snapshot, "SnapshotId"),
                WorldId = GetString(snapshot, "WorldId"),
                ProfileId = GetString(snapshot, "ProfileId"),
                CharacterId = GetString(snapshot, "CharacterId"),
                CriminalHistory = ConvertCriminalHistoryState(GetPropertyValue(snapshot, "CriminalHistory")),
                WantedRuntime = ConvertWantedRuntimeState(GetPropertyValue(snapshot, "WantedRuntime")),
                SnapshotUtc = GetDateTimeOffset(snapshot, "SnapshotUtc"),
            };
        }

        private CoopCriminalHistoryStateDto ConvertCriminalHistoryState(object state)
        {
            if (state == null)
            {
                return null;
            }

            CoopCriminalHistoryStateDto dto = new CoopCriminalHistoryStateDto
            {
                WorldId = GetString(state, "WorldId"),
                ProfileId = GetString(state, "ProfileId"),
                CharacterId = GetString(state, "CharacterId"),
                HasHistory = GetBool(state, "HasHistory"),
                LastSeenX = GetFloat(state, "LastSeenX"),
                LastSeenY = GetFloat(state, "LastSeenY"),
                LastSeenZ = GetFloat(state, "LastSeenZ"),
                WantedLevel = GetInt(state, "WantedLevel"),
                UpdatedUtc = GetDateTimeOffset(state, "UpdatedUtc"),
            };

            foreach (object crime in GetEnumerable(GetPropertyValue(state, "Crimes")))
            {
                dto.Crimes.Add(new CoopCriminalHistoryCrimeRecordDto
                {
                    CrimeId = GetString(crime, "CrimeId"),
                    CrimeName = GetString(crime, "CrimeName"),
                    Instances = GetInt(crime, "Instances"),
                    ResultingWantedLevel = GetInt(crime, "ResultingWantedLevel"),
                    Priority = GetInt(crime, "Priority"),
                    ResultsInLethalForce = GetBool(crime, "ResultsInLethalForce"),
                });
            }

            return dto;
        }

        private CoopWantedRuntimeStateDto ConvertWantedRuntimeState(object state)
        {
            if (state == null)
            {
                return null;
            }

            return new CoopWantedRuntimeStateDto
            {
                WorldId = GetString(state, "WorldId"),
                ProfileId = GetString(state, "ProfileId"),
                CharacterId = GetString(state, "CharacterId"),
                WantedLevel = GetInt(state, "WantedLevel"),
                WantedLevelHasBeenRadioedIn = GetBool(state, "WantedLevelHasBeenRadioedIn"),
                HasPlayerBeenIdentified = GetBool(state, "HasPlayerBeenIdentified"),
                PoliceHaveDescription = GetBool(state, "PoliceHaveDescription"),
                IsInvestigationActive = GetBool(state, "IsInvestigationActive"),
                IsInvestigationSuspicious = GetBool(state, "IsInvestigationSuspicious"),
                IsInSearchMode = GetBool(state, "IsInSearchMode"),
                IsInWantedActiveMode = GetBool(state, "IsInWantedActiveMode"),
                LastReportedCrimeX = GetFloat(state, "LastReportedCrimeX"),
                LastReportedCrimeY = GetFloat(state, "LastReportedCrimeY"),
                LastReportedCrimeZ = GetFloat(state, "LastReportedCrimeZ"),
                InvestigationX = GetFloat(state, "InvestigationX"),
                InvestigationY = GetFloat(state, "InvestigationY"),
                InvestigationZ = GetFloat(state, "InvestigationZ"),
                SnapshotUtc = GetDateTimeOffset(state, "SnapshotUtc"),
            };
        }

        private CoopGangReputationStateDto ConvertGangReputationState(object state)
        {
            if (state == null)
            {
                return null;
            }

            CoopGangReputationStateDto dto = new CoopGangReputationStateDto
            {
                StateId = GetString(state, "StateId"),
                WorldId = GetString(state, "WorldId"),
                ProfileId = GetString(state, "ProfileId"),
                CharacterId = GetString(state, "CharacterId"),
                CurrentGangId = GetString(state, "CurrentGangId"),
                UpdatedUtc = GetDateTimeOffset(state, "UpdatedUtc"),
            };

            foreach (object record in GetEnumerable(GetPropertyValue(state, "Reputations")))
            {
                dto.Reputations.Add(new CoopGangReputationRecordDto
                {
                    GangId = GetString(record, "GangId"),
                    Reputation = GetInt(record, "Reputation"),
                    MembersHurt = GetInt(record, "MembersHurt"),
                    MembersKilled = GetInt(record, "MembersKilled"),
                    MembersCarJacked = GetInt(record, "MembersCarJacked"),
                    MembersHurtInTerritory = GetInt(record, "MembersHurtInTerritory"),
                    MembersKilledInTerritory = GetInt(record, "MembersKilledInTerritory"),
                    MembersCarJackedInTerritory = GetInt(record, "MembersCarJackedInTerritory"),
                    PlayerDebt = GetInt(record, "PlayerDebt"),
                    IsMember = GetBool(record, "IsMember"),
                    IsEnemy = GetBool(record, "IsEnemy"),
                    TasksCompleted = GetInt(record, "TasksCompleted"),
                });
            }

            return dto;
        }

        private CoopGameplayActionRequestDto ConvertActionRequest(object request)
        {
            if (request == null)
            {
                return null;
            }

            return new CoopGameplayActionRequestDto
            {
                RequestId = GetString(request, "RequestId"),
                ActionType = GetString(request, "ActionType"),
                WorldId = GetString(request, "WorldId"),
                SourceProfileId = GetString(request, "SourceProfileId"),
                SourceCharacterId = GetString(request, "SourceCharacterId"),
                TargetProfileId = GetString(request, "TargetProfileId"),
                TargetCharacterId = GetString(request, "TargetCharacterId"),
                Parameters = GetStringDictionary(GetPropertyValue(request, "Parameters")),
                AllowsOptimisticClientFeedback = GetBool(request, "AllowsOptimisticClientFeedback"),
                RequestedUtc = GetDateTimeOffset(request, "RequestedUtc"),
            };
        }

        private CoopGameplayActionResultDto ConvertActionResult(object result)
        {
            if (result == null)
            {
                return null;
            }

            return new CoopGameplayActionResultDto
            {
                RequestId = GetString(result, "RequestId"),
                ActionType = GetString(result, "ActionType"),
                WorldId = GetString(result, "WorldId"),
                SourceProfileId = GetString(result, "SourceProfileId"),
                SourceCharacterId = GetString(result, "SourceCharacterId"),
                TargetProfileId = GetString(result, "TargetProfileId"),
                TargetCharacterId = GetString(result, "TargetCharacterId"),
                Accepted = GetBool(result, "Accepted"),
                RequiresPersistentCommit = GetBool(result, "RequiresPersistentCommit"),
                RequiresResync = GetBool(result, "RequiresResync"),
                Reason = GetString(result, "Reason"),
                ResultData = GetStringDictionary(GetPropertyValue(result, "ResultData")),
                ResolvedUtc = GetDateTimeOffset(result, "ResolvedUtc"),
            };
        }

        private CoopInventoryMoneySnapshot ConvertInventoryMoneySnapshot(object snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            CoopInventoryMoneySnapshot dto = new CoopInventoryMoneySnapshot
            {
                SnapshotId = GetString(snapshot, "SnapshotId"),
                WorldId = GetString(snapshot, "WorldId"),
                ProfileId = GetString(snapshot, "ProfileId"),
                CharacterId = GetString(snapshot, "CharacterId"),
                OnHandCash = GetInt(snapshot, "OnHandCash"),
                TotalAccountMoney = GetInt(snapshot, "TotalAccountMoney"),
                TotalMoney = GetInt(snapshot, "TotalMoney"),
                SnapshotUtc = GetDateTimeOffset(snapshot, "SnapshotUtc"),
            };

            foreach (object item in GetEnumerable(GetPropertyValue(snapshot, "InventoryItems")))
            {
                dto.InventoryItems.Add(new CoopInventoryItemState
                {
                    ItemName = GetString(item, "ItemName"),
                    RemainingPercent = GetFloat(item, "RemainingPercent"),
                });
            }

            foreach (object account in GetEnumerable(GetPropertyValue(snapshot, "BankAccounts")))
            {
                dto.BankAccounts.Add(new CoopBankAccountState
                {
                    BankContactName = GetString(account, "BankContactName"),
                    AccountName = GetString(account, "AccountName"),
                    Money = GetInt(account, "Money"),
                    IsPrimary = GetBool(account, "IsPrimary"),
                });
            }

            return dto;
        }

        private CoopPropertyOwnershipSnapshot ConvertPropertyOwnershipSnapshot(object snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            CoopPropertyOwnershipSnapshot dto = new CoopPropertyOwnershipSnapshot
            {
                SnapshotId = GetString(snapshot, "SnapshotId"),
                WorldId = GetString(snapshot, "WorldId"),
                ProfileId = GetString(snapshot, "ProfileId"),
                CharacterId = GetString(snapshot, "CharacterId"),
                SnapshotUtc = GetDateTimeOffset(snapshot, "SnapshotUtc"),
            };

            foreach (object property in GetEnumerable(GetPropertyValue(snapshot, "Properties")))
            {
                dto.Properties.Add(new CoopPropertyOwnershipRecord
                {
                    PropertyId = GetString(property, "PropertyId"),
                    Name = GetString(property, "Name"),
                    PropertyType = GetString(property, "PropertyType"),
                    IsOwned = GetBool(property, "IsOwned"),
                    IsRented = GetBool(property, "IsRented"),
                    IsRentedOut = GetBool(property, "IsRentedOut"),
                    EntranceX = GetFloat(property, "EntranceX"),
                    EntranceY = GetFloat(property, "EntranceY"),
                    EntranceZ = GetFloat(property, "EntranceZ"),
                    CurrentSalesPrice = GetInt(property, "CurrentSalesPrice"),
                    PayoutDate = GetDateTime(property, "PayoutDate"),
                    DateOfLastPayout = GetDateTime(property, "DateOfLastPayout"),
                    RentalPaymentDate = GetDateTime(property, "RentalPaymentDate"),
                    DateOfLastRentalPayment = GetDateTime(property, "DateOfLastRentalPayment"),
                });
            }

            return dto;
        }

        private CoopWeaponSnapshot ConvertWeaponSnapshot(object snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            CoopWeaponSnapshot dto = new CoopWeaponSnapshot
            {
                SnapshotId = GetString(snapshot, "SnapshotId"),
                WorldId = GetString(snapshot, "WorldId"),
                ProfileId = GetString(snapshot, "ProfileId"),
                CharacterId = GetString(snapshot, "CharacterId"),
                SnapshotUtc = GetDateTimeOffset(snapshot, "SnapshotUtc"),
            };

            foreach (object weapon in GetEnumerable(GetPropertyValue(snapshot, "Weapons")))
            {
                dto.Weapons.Add(new CoopWeaponRecord
                {
                    WeaponHash = GetString(weapon, "WeaponHash"),
                    WeaponName = GetString(weapon, "WeaponName"),
                    Category = GetString(weapon, "Category"),
                    Ammo = GetInt(weapon, "Ammo"),
                    IsLegal = GetBool(weapon, "IsLegal"),
                    IsEquipped = GetBool(weapon, "IsEquipped"),
                });
            }

            return dto;
        }

        private CoopOwnedVehicleSnapshot ConvertOwnedVehicleSnapshot(object snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            CoopOwnedVehicleSnapshot dto = new CoopOwnedVehicleSnapshot
            {
                SnapshotId = GetString(snapshot, "SnapshotId"),
                WorldId = GetString(snapshot, "WorldId"),
                ProfileId = GetString(snapshot, "ProfileId"),
                CharacterId = GetString(snapshot, "CharacterId"),
                SnapshotUtc = GetDateTimeOffset(snapshot, "SnapshotUtc"),
            };

            foreach (object vehicle in GetEnumerable(GetPropertyValue(snapshot, "Vehicles")))
            {
                dto.Vehicles.Add(new CoopOwnedVehicleRecord
                {
                    VehicleId = GetString(vehicle, "VehicleId"),
                    ModelHash = GetString(vehicle, "ModelHash"),
                    ModelName = GetString(vehicle, "ModelName"),
                    PlateNumber = GetString(vehicle, "PlateNumber"),
                    PlateType = GetInt(vehicle, "PlateType"),
                    PlateIsWanted = GetBool(vehicle, "PlateIsWanted"),
                    PositionX = GetFloat(vehicle, "PositionX"),
                    PositionY = GetFloat(vehicle, "PositionY"),
                    PositionZ = GetFloat(vehicle, "PositionZ"),
                    Heading = GetFloat(vehicle, "Heading"),
                    IsImpounded = GetBool(vehicle, "IsImpounded"),
                    DateTimeImpounded = GetDateTime(vehicle, "DateTimeImpounded"),
                    TimesImpounded = GetInt(vehicle, "TimesImpounded"),
                    ImpoundedLocation = GetString(vehicle, "ImpoundedLocation"),
                    StoredCash = GetInt(vehicle, "StoredCash"),
                });
            }

            return dto;
        }

        private CoopDeathArrestStateDto ConvertDeathArrestState(object state)
        {
            if (state == null)
            {
                return null;
            }

            return new CoopDeathArrestStateDto
            {
                StateId = GetString(state, "StateId"),
                WorldId = GetString(state, "WorldId"),
                ProfileId = GetString(state, "ProfileId"),
                CharacterId = GetString(state, "CharacterId"),
                ActionType = GetString(state, "ActionType"),
                OutcomeType = GetString(state, "OutcomeType"),
                RespawnLocationName = GetString(state, "RespawnLocationName"),
                PositionX = GetFloat(state, "PositionX"),
                PositionY = GetFloat(state, "PositionY"),
                PositionZ = GetFloat(state, "PositionZ"),
                Heading = GetFloat(state, "Heading"),
                HospitalFee = GetInt(state, "HospitalFee"),
                HospitalBillPastDue = GetInt(state, "HospitalBillPastDue"),
                HospitalDuration = GetInt(state, "HospitalDuration"),
                BailFee = GetInt(state, "BailFee"),
                BailFeePastDue = GetInt(state, "BailFeePastDue"),
                BailDuration = GetInt(state, "BailDuration"),
                TodayPayment = GetInt(state, "TodayPayment"),
                TimesDied = GetInt(state, "TimesDied"),
                HadIllegalWeapons = GetBool(state, "HadIllegalWeapons"),
                HadIllegalItems = GetBool(state, "HadIllegalItems"),
                ReleaseDate = GetDateTime(state, "ReleaseDate"),
                OccurredUtc = GetDateTimeOffset(state, "OccurredUtc"),
            };
        }

        private object GetPropertyValue(object source, string propertyName)
        {
            return source?.GetType().GetProperty(propertyName)?.GetValue(source);
        }

        private string GetString(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value?.ToString() ?? string.Empty;
        }

        private bool GetBool(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value is bool boolValue && boolValue;
        }

        private int GetInt(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value is int intValue ? intValue : 0;
        }

        private float GetFloat(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value is float floatValue ? floatValue : 0.0f;
        }

        private DateTimeOffset GetDateTimeOffset(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value is DateTimeOffset dateTimeOffset ? dateTimeOffset : DateTimeOffset.UtcNow;
        }

        private DateTime GetDateTime(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value is DateTime dateTime ? dateTime : DateTime.MinValue;
        }

        private float GetVectorComponent(object source, string propertyName, string componentName)
        {
            object vector = GetPropertyValue(source, propertyName);
            object value = GetPropertyValue(vector, componentName);
            return value is float floatValue ? floatValue : 0.0f;
        }

        private Dictionary<string, string> GetStringDictionary(object source)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            if (source is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    values[entry.Key?.ToString() ?? string.Empty] = entry.Value?.ToString() ?? string.Empty;
                }
            }

            return values;
        }

        private IEnumerable<object> GetEnumerable(object source)
        {
            if (source is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    yield return item;
                }
            }
        }

        private sealed class PvpVictimDamageState
        {
            public PvpVictimDamageState(int health, int armor, bool isDead)
            {
                Health = health;
                Armor = armor;
                IsDead = isDead;
                LastReportedUtc = DateTimeOffset.MinValue;
            }

            public int Health { get; set; }
            public int Armor { get; set; }
            public bool IsDead { get; set; }
            public DateTimeOffset LastReportedUtc { get; set; }
        }

        private void WriteBridgeState(bool isCoopEnabled, bool hasActiveHostAssigned, string worldId, string localProfileId, string activeHostProfileId, bool isCharacterReadyForSimulation)
        {
            string[] lines =
            {
                "BridgeVersion=2",
                "TransportMode=RAGECOOP",
                $"CoopModeEnabled={isCoopEnabled.ToString().ToLowerInvariant()}",
                $"ProcessId={Process.GetCurrentProcess().Id}",
                $"IsCoopEnabled={isCoopEnabled.ToString().ToLowerInvariant()}",
                $"HasActiveHostAssigned={hasActiveHostAssigned.ToString().ToLowerInvariant()}",
                $"CharacterReadyForSimulation={isCharacterReadyForSimulation.ToString().ToLowerInvariant()}",
                $"CharacterCreationRequired={(!isCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(localProfileId)).ToString().ToLowerInvariant()}",
                $"WorldId={worldId ?? string.Empty}",
                $"LocalProfileId={localProfileId ?? string.Empty}",
                $"ActiveHostProfileId={activeHostProfileId ?? string.Empty}",
            };

            foreach (string folder in GetBridgeStateFolders())
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllLines(Path.Combine(folder, "LsrCoopStartupState.txt"), lines);
                }
                catch (Exception ex)
                {
                    Logger.Info($"[LsrCoop.Client] startup bridge file update skipped: {ex.Message}");
                }
            }
        }

        private string[] GetBridgeStateFolders()
        {
            return new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED"),
                Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "LosSantosRED"),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };
        }
    }
}
