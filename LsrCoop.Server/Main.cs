using RageCoop.Core.Scripting;
using RageCoop.Server;
using RageCoop.Server.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;

namespace LsrCoop.Server
{
    public class Main : ServerScript
    {
        private readonly List<ConnectionEventSubscription> connectionSubscriptions = new List<ConnectionEventSubscription>();

        private EventRouter eventRouter;
        private RoleConfigService roleConfigService;
        private WorldProfileStoreService worldProfileStoreService;
        private CompatibilityService compatibilityService;
        private PlayerRegistrationService playerRegistrationService;
        private ActiveHostService activeHostService;
        private ActiveHostHandoffService activeHostHandoffService;
        private AppearanceSyncService appearanceSyncService;

        public override void OnStart()
        {
            Logger.Info("[LsrCoop.Server] loaded");
            InitializeServices();
            RegisterConnectionHooks();
            RegisterCustomEventStubs();
            playerRegistrationService.RefreshConnectedClients(API?.GetAllClients());
            activeHostHandoffService.EvaluateAndSync("resource-start");
        }

        public override void OnStop()
        {
            activeHostService?.Release("resource-stop", playerRegistrationService?.ConnectedClients);
            UnregisterConnectionHooks();
            Logger.Info("[LsrCoop.Server] stopped");
        }

        private void InitializeServices()
        {
            string dataFolder = GetDataFolder();
            eventRouter = new EventRouter(Logger.Warning);
            roleConfigService = new RoleConfigService(dataFolder, Logger.Info, Logger.Warning);
            roleConfigService.Load();
            worldProfileStoreService = new WorldProfileStoreService(dataFolder, roleConfigService, Logger.Info, Logger.Warning);
            worldProfileStoreService.Load();
            compatibilityService = new CompatibilityService();
            playerRegistrationService = new PlayerRegistrationService(worldProfileStoreService, roleConfigService, eventRouter, Logger.Info);
            activeHostService = new ActiveHostService(roleConfigService, compatibilityService, eventRouter, Logger.Info);
            activeHostHandoffService = new ActiveHostHandoffService(worldProfileStoreService, playerRegistrationService, activeHostService, eventRouter, Logger.Info);
            appearanceSyncService = new AppearanceSyncService(worldProfileStoreService, roleConfigService, eventRouter, () => playerRegistrationService.ConnectedClients, Logger.Info, Logger.Warning);
        }

        private void RegisterConnectionHooks()
        {
            object events = API?.Events;
            if (events == null)
            {
                Logger.Warning("[LsrCoop.Server] connection hooks unavailable");
                return;
            }

            string eventNames = string.Join(", ", events.GetType().GetEvents().Select(x => x.Name));
            Logger.Info($"[LsrCoop.Server] connection hook stubs available: {eventNames}");
            TrySubscribeConnectionEvent(events, "OnPlayerReady", OnClientReady);
            TrySubscribeConnectionEvent(events, "OnPlayerConnected", OnClientReady);
            TrySubscribeConnectionEvent(events, "OnPlayerDisconnected", OnClientDisconnected);
        }

        private void UnregisterConnectionHooks()
        {
            foreach (ConnectionEventSubscription subscription in connectionSubscriptions.ToArray())
            {
                try
                {
                    subscription.Unsubscribe();
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[LsrCoop.Server] failed to remove connection hook {subscription.EventInfo.Name}: {ex.Message}");
                }
            }

            connectionSubscriptions.Clear();
        }

        private void RegisterCustomEventStubs()
        {
            API.RegisterCustomEventHandler(EventRouter.PingEventHash, OnPingReceived);
            API.RegisterCustomEventHandler(EventRouter.CompatibilityReportEventHash, OnCompatibilityReportReceived);
            API.RegisterCustomEventHandler(EventRouter.CharacterCreatedEventHash, OnCharacterCreatedReceived);
            API.RegisterCustomEventHandler(EventRouter.CharacterSnapshotAckEventHash, OnCharacterSnapshotAckReceived);
            API.RegisterCustomEventHandler(EventRouter.AppearanceChangeRequestedEventHash, OnAppearanceChangeRequested);
            API.RegisterCustomEventHandler(EventRouter.GameplayActionCommittedEventHash, OnGameplayActionCommitted);
            API.RegisterCustomEventHandler(EventRouter.PvpCrimeReportedEventHash, OnPvpCrimeReported);
            API.RegisterCustomEventHandler(EventRouter.CriminalJusticeSnapshotCommittedEventHash, OnCriminalJusticeSnapshotCommitted);
            API.RegisterCustomEventHandler(EventRouter.GangReputationSnapshotCommittedEventHash, OnGangReputationSnapshotCommitted);
            Logger.Info("[LsrCoop.Server] custom event stubs registered");
        }

        private void OnPingReceived(CustomEventReceivedArgs args)
        {
            CoopClientStatus status = playerRegistrationService.RegisterClient(args?.Client, "ping");
            playerRegistrationService.SendRegistrationState(status, "ping");
            string source = playerRegistrationService.GetClientName(args?.Client);
            string message = args?.Args != null && args.Args.Length > 0 ? args.Args[0]?.ToString() : string.Empty;
            Logger.Info($"[LsrCoop.Server] ping from {source}: {message}");
            eventRouter.Send(args?.Client, EventRouter.PongEventHash, new object[] { "server-pong" });
        }

        private void OnCompatibilityReportReceived(CustomEventReceivedArgs args)
        {
            CoopClientStatus status = playerRegistrationService.RegisterClient(args?.Client, "compatibility-report");
            if (status == null)
            {
                return;
            }

            compatibilityService.ApplyReport(
                status,
                GetArg(args, 0),
                GetArg(args, 1),
                GetArg(args, 2),
                bool.TryParse(GetArg(args, 3), out bool loaded) && loaded);

            Logger.Info($"[LsrCoop.Server] compatibility {status.ProfileId}: {status.CompatibilityState}, coop={status.CoopBuildVersion}, lsr={status.LsrVersion}, config={status.ConfigVersion}, resourceLoaded={status.RequiredResourceLoaded}");
            status.RefreshReadinessState();
            eventRouter.Send(status.Client, EventRouter.CompatibilityStatusEventHash, new object[] { status.CompatibilityState.ToString(), CompatibilityService.RequiredCoopBuildVersion, CompatibilityService.RequiredConfigVersion, CompatibilityService.RequiredLsrVersion });
            playerRegistrationService.SendRegistrationState(status, "compatibility-report");
            activeHostHandoffService.EvaluateAndSync("compatibility-report");
        }

        private void OnCharacterSnapshotAckReceived(CustomEventReceivedArgs args)
        {
            CoopClientStatus status = playerRegistrationService.AcknowledgeCharacterSnapshot(args?.Client, GetArg(args, 0), GetArg(args, 1), "character-snapshot-ack", out bool readinessChanged);
            if (status == null)
            {
                return;
            }

            if (readinessChanged)
            {
                activeHostHandoffService.EvaluateAndSync("character-snapshot-ack");
            }
        }

        private void OnCharacterCreatedReceived(CustomEventReceivedArgs args)
        {
            CoopClientStatus status = playerRegistrationService.RegisterClient(args?.Client, "character-created");
            if (status == null)
            {
                return;
            }

            CoopCharacterCreatedRequest request = Deserialize<CoopCharacterCreatedRequest>(GetArg(args, 0));
            if (!playerRegistrationService.TrySaveCreatedCharacter(status, request, out string reason))
            {
                Logger.Warning($"[LsrCoop.Server] rejected character create from {status.ProfileId}: {reason}");
                playerRegistrationService.SendRegistrationState(status, "character-create-rejected");
                activeHostHandoffService.EvaluateAndSync("character-create-rejected");
                return;
            }

            playerRegistrationService.SendRegistrationState(status, "character-created");
            activeHostHandoffService.EvaluateAndSync("character-created");
        }

        private void OnAppearanceChangeRequested(CustomEventReceivedArgs args)
        {
            CoopClientStatus requester = playerRegistrationService.RegisterClient(args?.Client, "appearance-change-request");
            appearanceSyncService.HandleAppearanceChange(requester, Deserialize<CoopAppearanceChangeRequest>(GetArg(args, 0)));
        }

        private void OnGameplayActionCommitted(CustomEventReceivedArgs args)
        {
            CoopClientStatus requester = playerRegistrationService.RegisterClient(args?.Client, "gameplay-action-commit");
            if (requester == null)
            {
                return;
            }

            string payloadJson = GetArg(args, 0);
            string eventType = GetArg(args, 1);
            string nonce = GetArg(args, 2);
            string sourceProfile = GetArg(args, 3);
            if (!TryDeserializeEventPayload(payloadJson, eventType, nonce, sourceProfile, out CoopStorePurchaseCommitDto commit))
            {
                return;
            }

            CoopGameplayActionRequestDto request = commit?.Request;
            CoopGameplayActionResultDto result = commit?.Result;
            if (request == null || result == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected gameplay action from {requester.ProfileId}: empty payload");
                SendGameplayActionResult(requester, CreateGameplayActionResult(request, false, true, "Invalid gameplay action payload"));
                return;
            }

            if (!string.Equals(request.WorldId, worldProfileStoreService.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[LsrCoop.Server] rejected gameplay action from {requester.ProfileId}: wrong world {request.WorldId}");
                SendGameplayActionResult(requester, CreateGameplayActionResult(request, false, true, "Wrong co-op world"));
                return;
            }

            if (!string.Equals(request.SourceProfileId, requester.ProfileId, StringComparison.OrdinalIgnoreCase)
                && !activeHostService.IsActiveHost(requester.ProfileId))
            {
                Logger.Warning($"[LsrCoop.Server] rejected gameplay action from {requester.ProfileId}: source mismatch {request.SourceProfileId}");
                SendGameplayActionResult(requester, CreateGameplayActionResult(request, false, true, "Profile mismatch"));
                return;
            }

            if (!activeHostService.IsActiveHost(requester.ProfileId))
            {
                Logger.Warning($"[LsrCoop.Server] rejected gameplay action from {requester.ProfileId}: active host is {activeHostService.ActiveHostId ?? "none"}");
                SendGameplayActionResult(requester, CreateGameplayActionResult(request, false, true, "Only the active host can commit gameplay actions"));
                return;
            }

            CoopPlayerProfile profile = worldProfileStoreService.GetProfile(request.SourceProfileId);
            if (profile == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected gameplay action from {requester.ProfileId}: missing profile");
                SendGameplayActionResult(requester, CreateGameplayActionResult(request, false, true, "Missing profile"));
                return;
            }

            if (commit.InventoryMoneySnapshot != null)
            {
                profile.InventoryMoney = commit.InventoryMoneySnapshot;
                profile.InventoryMoney.WorldId = worldProfileStoreService.WorldId;
                profile.InventoryMoney.ProfileId = profile.ProfileId;
                profile.InventoryMoney.CharacterId = string.IsNullOrWhiteSpace(profile.InventoryMoney.CharacterId) ? profile.ProfileId : profile.InventoryMoney.CharacterId;
                worldProfileStoreService.Save();
            }

            if (commit.WeaponSnapshot != null)
            {
                profile.Weapons = commit.WeaponSnapshot;
                profile.Weapons.WorldId = worldProfileStoreService.WorldId;
                profile.Weapons.ProfileId = profile.ProfileId;
                profile.Weapons.CharacterId = string.IsNullOrWhiteSpace(profile.Weapons.CharacterId) ? profile.ProfileId : profile.Weapons.CharacterId;
                worldProfileStoreService.Save();
            }

            if (commit.PropertyOwnershipSnapshot != null)
            {
                profile.PropertyOwnership = commit.PropertyOwnershipSnapshot;
                profile.PropertyOwnership.WorldId = worldProfileStoreService.WorldId;
                profile.PropertyOwnership.ProfileId = profile.ProfileId;
                profile.PropertyOwnership.CharacterId = string.IsNullOrWhiteSpace(profile.PropertyOwnership.CharacterId) ? profile.ProfileId : profile.PropertyOwnership.CharacterId;
                worldProfileStoreService.Save();
            }

            if (commit.OwnedVehicleSnapshot != null)
            {
                profile.OwnedVehicles = commit.OwnedVehicleSnapshot;
                profile.OwnedVehicles.WorldId = worldProfileStoreService.WorldId;
                profile.OwnedVehicles.ProfileId = profile.ProfileId;
                profile.OwnedVehicles.CharacterId = string.IsNullOrWhiteSpace(profile.OwnedVehicles.CharacterId) ? profile.ProfileId : profile.OwnedVehicles.CharacterId;
                worldProfileStoreService.Save();
            }

            if (commit.DeathArrestState != null)
            {
                profile.DeathArrestState = commit.DeathArrestState;
                profile.DeathArrestState.WorldId = worldProfileStoreService.WorldId;
                profile.DeathArrestState.ProfileId = profile.ProfileId;
                profile.DeathArrestState.CharacterId = string.IsNullOrWhiteSpace(profile.DeathArrestState.CharacterId) ? profile.ProfileId : profile.DeathArrestState.CharacterId;
                worldProfileStoreService.Save();
            }

            CoopGameplayActionResultDto accepted = CreateGameplayActionResult(request, true, false, "Committed by active host");
            SendGameplayActionResult(requester, accepted);
            Logger.Info($"[LsrCoop.Server] gameplay action committed: profile={profile.ProfileId}, request={request.RequestId}, type={request.ActionType}");
        }

        private void OnPvpCrimeReported(CustomEventReceivedArgs args)
        {
            CoopClientStatus requester = playerRegistrationService.RegisterClient(args?.Client, "pvp-crime-report");
            if (requester == null)
            {
                return;
            }

            string payloadJson = GetArg(args, 0);
            string eventType = GetArg(args, 1);
            string nonce = GetArg(args, 2);
            string sourceProfile = GetArg(args, 3);
            if (!TryDeserializeEventPayload(payloadJson, eventType, nonce, sourceProfile, out CoopPvpCrimeReportDto report))
            {
                return;
            }

            if (report == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected PvP crime from {requester.ProfileId}: empty payload");
                return;
            }

            if (!string.Equals(report.WorldId, worldProfileStoreService.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[LsrCoop.Server] rejected PvP crime from {requester.ProfileId}: wrong world {report.WorldId}");
                return;
            }

            if (!string.Equals(report.SourceProfileId, requester.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[LsrCoop.Server] rejected PvP crime from {requester.ProfileId}: source mismatch {report.SourceProfileId}");
                return;
            }

            if (string.IsNullOrWhiteSpace(report.TargetProfileId) || worldProfileStoreService.GetProfile(report.TargetProfileId) == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected PvP crime from {requester.ProfileId}: unknown victim {report.TargetProfileId}");
                return;
            }

            if (activeHostService.IsActiveHost(requester.ProfileId))
            {
                Logger.Info($"[LsrCoop.Server] PvP crime already handled by active host: offender={report.SourceProfileId}, victim={report.TargetProfileId}");
                return;
            }

            if (string.IsNullOrWhiteSpace(activeHostService.ActiveHostId) || !playerRegistrationService.ClientStatuses.TryGetValue(activeHostService.ActiveHostId, out CoopClientStatus activeHost))
            {
                Logger.Warning($"[LsrCoop.Server] PvP crime queued without active host: offender={report.SourceProfileId}, victim={report.TargetProfileId}");
                return;
            }

            eventRouter.Send(activeHost.Client, EventRouter.PvpCrimeAssignedEventHash, new object[] { JsonSerializer.Serialize(report) });
            Logger.Info($"[LsrCoop.Server] PvP crime routed to active host: offender={report.SourceProfileId}, victim={report.TargetProfileId}, killed={report.WasKilled}");
        }

        private void OnCriminalJusticeSnapshotCommitted(CustomEventReceivedArgs args)
        {
            CoopClientStatus requester = playerRegistrationService.RegisterClient(args?.Client, "criminal-justice-snapshot");
            if (requester == null)
            {
                return;
            }

            string payloadJson = GetArg(args, 0);
            string eventType = GetArg(args, 1);
            string nonce = GetArg(args, 2);
            string sourceProfile = GetArg(args, 3);
            if (!TryDeserializeEventPayload(payloadJson, eventType, nonce, sourceProfile, out CoopCriminalJusticeStateSnapshotDto snapshot))
            {
                return;
            }

            if (snapshot == null || snapshot.CriminalHistory == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected criminal justice snapshot from {requester.ProfileId}: empty payload");
                return;
            }

            if (!string.Equals(snapshot.WorldId, worldProfileStoreService.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[LsrCoop.Server] rejected criminal justice snapshot from {requester.ProfileId}: wrong world {snapshot.WorldId}");
                return;
            }

            if (!string.Equals(snapshot.ProfileId, requester.ProfileId, StringComparison.OrdinalIgnoreCase)
                && !activeHostService.IsActiveHost(requester.ProfileId))
            {
                Logger.Warning($"[LsrCoop.Server] rejected criminal justice snapshot from {requester.ProfileId}: profile mismatch {snapshot.ProfileId}");
                return;
            }

            CoopPlayerProfile profile = worldProfileStoreService.GetProfile(snapshot.ProfileId);
            if (profile == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected criminal justice snapshot from {requester.ProfileId}: missing profile");
                return;
            }

            snapshot.CriminalHistory.WorldId = worldProfileStoreService.WorldId;
            snapshot.CriminalHistory.ProfileId = profile.ProfileId;
            snapshot.CriminalHistory.CharacterId = string.IsNullOrWhiteSpace(snapshot.CriminalHistory.CharacterId) ? profile.ProfileId : snapshot.CriminalHistory.CharacterId;
            snapshot.CriminalHistory.Crimes = snapshot.CriminalHistory.Crimes ?? new List<CoopCriminalHistoryCrimeRecordDto>();
            profile.CriminalHistory = snapshot.CriminalHistory;
            worldProfileStoreService.Save();

            Logger.Info($"[LsrCoop.Server] criminal history saved: profile={profile.ProfileId}, crimes={profile.CriminalHistory.Crimes?.Count ?? 0}, maxWanted={profile.CriminalHistory.WantedLevel}; active wanted/search/chase state ignored");
        }

        private void OnGangReputationSnapshotCommitted(CustomEventReceivedArgs args)
        {
            CoopClientStatus requester = playerRegistrationService.RegisterClient(args?.Client, "gang-reputation-snapshot");
            if (requester == null)
            {
                return;
            }

            string payloadJson = GetArg(args, 0);
            string eventType = GetArg(args, 1);
            string nonce = GetArg(args, 2);
            string sourceProfile = GetArg(args, 3);
            if (!TryDeserializeEventPayload(payloadJson, eventType, nonce, sourceProfile, out CoopGangReputationStateDto snapshot))
            {
                return;
            }

            if (snapshot == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected gang reputation snapshot from {requester.ProfileId}: empty payload");
                return;
            }

            if (!string.Equals(snapshot.WorldId, worldProfileStoreService.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[LsrCoop.Server] rejected gang reputation snapshot from {requester.ProfileId}: wrong world {snapshot.WorldId}");
                return;
            }

            if (!string.Equals(snapshot.ProfileId, requester.ProfileId, StringComparison.OrdinalIgnoreCase)
                && !activeHostService.IsActiveHost(requester.ProfileId))
            {
                Logger.Warning($"[LsrCoop.Server] rejected gang reputation snapshot from {requester.ProfileId}: profile mismatch {snapshot.ProfileId}");
                return;
            }

            CoopPlayerProfile profile = worldProfileStoreService.GetProfile(snapshot.ProfileId);
            if (profile == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected gang reputation snapshot from {requester.ProfileId}: missing profile");
                return;
            }

            snapshot.WorldId = worldProfileStoreService.WorldId;
            snapshot.ProfileId = profile.ProfileId;
            snapshot.CharacterId = string.IsNullOrWhiteSpace(snapshot.CharacterId) ? profile.ProfileId : snapshot.CharacterId;
            profile.GangReputation = snapshot;
            worldProfileStoreService.Save();

            Logger.Info($"[LsrCoop.Server] gang reputation saved: profile={profile.ProfileId}, gangs={profile.GangReputation.Reputations?.Count ?? 0}, currentGang={profile.GangReputation.CurrentGangId ?? "none"}");
        }

        private void OnClientReady(object eventPayload)
        {
            CoopClientStatus status = playerRegistrationService.RegisterClient(ExtractClient(eventPayload), "ready");
            playerRegistrationService.SendRegistrationState(status, "client-ready");
            activeHostHandoffService.EvaluateAndSync("client-ready");
        }

        private void OnClientDisconnected(object eventPayload)
        {
            Client client = ExtractClient(eventPayload);
            string profileId = playerRegistrationService.GetClientProfileId(client);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                playerRegistrationService.RefreshConnectedClients(API?.GetAllClients());
                activeHostHandoffService.EvaluateAndSync("disconnect-refresh");
                return;
            }

            bool disconnectedActiveHost = activeHostService.IsActiveHost(profileId);
            playerRegistrationService.Remove(profileId);
            Logger.Info($"[LsrCoop.Server] client disconnected: {profileId}");

            if (disconnectedActiveHost)
            {
                activeHostHandoffService.HandleActiveHostLeft(profileId, "active-host-left");
                return;
            }

            activeHostHandoffService.EvaluateAndSync("client-disconnected");
        }

        private CoopGameplayActionResultDto CreateGameplayActionResult(CoopGameplayActionRequestDto request, bool accepted, bool requiresResync, string reason)
        {
            if (request == null)
            {
                return null;
            }

            return new CoopGameplayActionResultDto
            {
                RequestId = request.RequestId,
                ActionType = request.ActionType,
                WorldId = request.WorldId,
                SourceProfileId = request.SourceProfileId,
                SourceCharacterId = request.SourceCharacterId,
                TargetProfileId = request.TargetProfileId,
                TargetCharacterId = request.TargetCharacterId,
                Accepted = accepted,
                RequiresPersistentCommit = accepted,
                RequiresResync = requiresResync,
                Reason = reason ?? string.Empty,
                ResolvedUtc = DateTimeOffset.UtcNow,
            };
        }

        private void SendGameplayActionResult(CoopClientStatus status, CoopGameplayActionResultDto result)
        {
            if (status?.Client == null || result == null)
            {
                return;
            }

            eventRouter.Send(status.Client, EventRouter.GameplayActionResultEventHash, new object[] { JsonSerializer.Serialize(result) });
        }

        private string GetDataFolder()
        {
            if (!string.IsNullOrWhiteSpace(CurrentResource?.DataFolder))
            {
                return CurrentResource.DataFolder;
            }

            return Path.Combine(AppContext.BaseDirectory, "Resources", "Server", "data", "LsrCoop.Server");
        }

        private bool TrySubscribeConnectionEvent(object events, string eventName, Action<object> handler)
        {
            EventInfo eventInfo = events.GetType().GetEvent(eventName);
            if (eventInfo == null)
            {
                return false;
            }

            try
            {
                Delegate eventHandler = CreateEventDelegate(eventInfo.EventHandlerType, handler);
                eventInfo.AddEventHandler(events, eventHandler);
                connectionSubscriptions.Add(new ConnectionEventSubscription(events, eventInfo, eventHandler));
                Logger.Info($"[LsrCoop.Server] connection hook registered: {eventName}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LsrCoop.Server] failed to register connection hook {eventName}: {ex.Message}");
                return false;
            }
        }

        private Delegate CreateEventDelegate(Type delegateType, Action<object> handler)
        {
            MethodInfo invoke = delegateType.GetMethod("Invoke");
            ParameterExpression[] parameters = invoke.GetParameters()
                .Select(x => Expression.Parameter(x.ParameterType, x.Name))
                .ToArray();

            Expression payload = parameters.Length == 0
                ? Expression.Constant(null, typeof(object))
                : Expression.Convert(parameters[parameters.Length == 1 ? 0 : 1], typeof(object));

            MethodInfo handlerInvoke = typeof(Action<object>).GetMethod("Invoke");
            MethodCallExpression body = Expression.Call(Expression.Constant(handler), handlerInvoke, payload);
            return Expression.Lambda(delegateType, body, parameters).Compile();
        }

        private Client ExtractClient(object eventPayload)
        {
            if (eventPayload is Client client)
            {
                return client;
            }

            object clientValue = eventPayload?.GetType().GetProperty("Client")?.GetValue(eventPayload);
            return clientValue as Client;
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
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LsrCoop.Server] failed to read event payload: {ex.Message}");
                return null;
            }
        }

        private bool TryDeserializeEventPayload<T>(string json, string eventType, string nonce, string profileId, out T payload) where T : class
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            try
            {
                payload = JsonSerializer.Deserialize<T>(json);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LsrCoop.Server] failed to read event payload: eventType={eventType ?? "unknown"}, nonce={nonce ?? "unknown"}, profile={profileId ?? "unknown"}, error={ex.Message}");
                return false;
            }
        }
    }
}
