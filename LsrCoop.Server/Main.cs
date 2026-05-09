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
        private const string RoleConfigFileName = "LsrCoop.RoleConfig.json";
        private const string WorldProfileStoreFileName = "LsrCoop.WorldProfiles.json";
        private const string RequiredCoopBuildVersion = "0.1.0";
        private const string RequiredLsrVersion = "1.0.0.513";
        private const string RequiredConfigVersion = "1";

        private static readonly int PingEventHash = CustomEvents.Hash("lsrcoop.ping");
        private static readonly int PongEventHash = CustomEvents.Hash("lsrcoop.pong");
        private static readonly int CompatibilityReportEventHash = CustomEvents.Hash("lsrcoop.compatibility.report");
        private static readonly int CompatibilityStatusEventHash = CustomEvents.Hash("lsrcoop.compatibility.status");
        private static readonly int PlayerRegisteredEventHash = CustomEvents.Hash("lsrcoop.player.registered");
        private static readonly int CharacterCreateRequiredEventHash = CustomEvents.Hash("lsrcoop.character.createRequired");
        private static readonly int CharacterSnapshotEventHash = CustomEvents.Hash("lsrcoop.character.snapshot");
        private static readonly int AppearanceChangeRequestedEventHash = CustomEvents.Hash("lsrcoop.appearance.changeRequested");
        private static readonly int AppearanceChangedEventHash = CustomEvents.Hash("lsrcoop.appearance.changed");
        private static readonly int ActiveHostAssignedEventHash = CustomEvents.Hash("lsrcoop.activeHost.assigned");
        private static readonly int ActiveHostReleasedEventHash = CustomEvents.Hash("lsrcoop.activeHost.released");
        private static readonly int ActiveHostUnavailableEventHash = CustomEvents.Hash("lsrcoop.activeHost.unavailable");

        private readonly Dictionary<string, CoopClientStatus> clientStatuses = new Dictionary<string, CoopClientStatus>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ConnectionEventSubscription> connectionSubscriptions = new List<ConnectionEventSubscription>();
        private LsrCoopRoleConfig roleConfig = new LsrCoopRoleConfig();
        private CoopWorldProfileStore worldProfiles = new CoopWorldProfileStore();
        private string roleConfigPath;
        private string worldProfileStorePath;
        private string activeHostId;
        private bool activeHostUnavailableAnnounced;

        public override void OnStart()
        {
            Logger.Info("[LsrCoop.Server] loaded");
            roleConfig = LoadRoleConfig();
            worldProfiles = LoadWorldProfileStore();
            RegisterConnectionHooks();
            RegisterCustomEventStubs();
            RefreshConnectedClients();
            EvaluateActiveHost("resource-start");
        }

        public override void OnStop()
        {
            ReleaseActiveHost("resource-stop");
            UnregisterConnectionHooks();
            Logger.Info("[LsrCoop.Server] stopped");
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
            API.RegisterCustomEventHandler(PingEventHash, OnPingReceived);
            API.RegisterCustomEventHandler(CompatibilityReportEventHash, OnCompatibilityReportReceived);
            API.RegisterCustomEventHandler(AppearanceChangeRequestedEventHash, OnAppearanceChangeRequested);
            Logger.Info("[LsrCoop.Server] custom event stubs registered");
        }

        private void OnPingReceived(CustomEventReceivedArgs args)
        {
            CoopClientStatus status = RegisterClient(args?.Client, "ping");
            SendRegistrationState(status, "ping");
            string source = GetClientName(args?.Client);
            string message = args?.Args != null && args.Args.Length > 0 ? args.Args[0]?.ToString() : string.Empty;
            Logger.Info($"[LsrCoop.Server] ping from {source}: {message}");
            args?.Client?.SendCustomEvent(PongEventHash, new object[] { "server-pong" });
        }

        private void OnCompatibilityReportReceived(CustomEventReceivedArgs args)
        {
            CoopClientStatus status = RegisterClient(args?.Client, "compatibility-report");
            if (status == null)
            {
                return;
            }

            status.CoopBuildVersion = GetArg(args, 0);
            status.LsrVersion = GetArg(args, 1);
            status.ConfigVersion = GetArg(args, 2);
            status.RequiredResourceLoaded = bool.TryParse(GetArg(args, 3), out bool loaded) && loaded;
            status.CompatibilityState = EvaluateCompatibility(status);

            Logger.Info($"[LsrCoop.Server] compatibility {status.ProfileId}: {status.CompatibilityState}, coop={status.CoopBuildVersion}, lsr={status.LsrVersion}, config={status.ConfigVersion}, resourceLoaded={status.RequiredResourceLoaded}");
            status.Client?.SendCustomEvent(CompatibilityStatusEventHash, new object[] { status.CompatibilityState.ToString(), RequiredCoopBuildVersion, RequiredConfigVersion, RequiredLsrVersion });
            SendRegistrationState(status, "compatibility-report");
            EvaluateActiveHost("compatibility-report");
        }

        private void OnAppearanceChangeRequested(CustomEventReceivedArgs args)
        {
            CoopClientStatus requester = RegisterClient(args?.Client, "appearance-change-request");
            if (requester == null)
            {
                return;
            }

            CoopAppearanceChangeRequest request = Deserialize<CoopAppearanceChangeRequest>(GetArg(args, 0));
            if (request?.Appearance == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: empty appearance");
                return;
            }

            request.ProfileId = string.IsNullOrWhiteSpace(request.ProfileId) ? requester.ProfileId : request.ProfileId;
            request.TargetProfileId = string.IsNullOrWhiteSpace(request.TargetProfileId) ? requester.ProfileId : request.TargetProfileId;
            request.WorldId = string.IsNullOrWhiteSpace(request.WorldId) ? worldProfiles.WorldId : request.WorldId;

            if (!string.Equals(request.WorldId, worldProfiles.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: wrong world {request.WorldId}");
                return;
            }

            bool requesterIsAdmin = IsAdmin(requester.ProfileId);
            if (!string.Equals(requester.ProfileId, request.TargetProfileId, StringComparison.OrdinalIgnoreCase) && !requesterIsAdmin)
            {
                Logger.Warning($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: target {request.TargetProfileId} requires Admin");
                return;
            }

            CoopPlayerProfile targetProfile = GetProfile(request.TargetProfileId);
            if (targetProfile == null)
            {
                Logger.Warning($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: missing profile {request.TargetProfileId}");
                return;
            }

            if (IsModelChangeAfterCreation(targetProfile, request.Appearance) && !requesterIsAdmin)
            {
                Logger.Warning($"[LsrCoop.Server] rejected appearance request from {requester.ProfileId}: model change after creation requires Admin");
                return;
            }

            SaveAcceptedAppearance(targetProfile, request.Appearance);
            CoopAppearanceChanged changed = new CoopAppearanceChanged
            {
                WorldId = worldProfiles.WorldId,
                ProfileId = targetProfile.ProfileId,
                SourceProfileId = requester.ProfileId,
                Appearance = request.Appearance,
                AcceptedUtc = DateTimeOffset.UtcNow
            };

            BroadcastEvent(AppearanceChangedEventHash, new object[] { JsonSerializer.Serialize(changed) });
            BroadcastCharacterSnapshot(targetProfile);
            Logger.Info($"[LsrCoop.Server] appearance accepted: profile={targetProfile.ProfileId}, source={requester.ProfileId}");
        }

        private LsrCoopRoleConfig LoadRoleConfig()
        {
            roleConfigPath = Path.Combine(GetDataFolder(), RoleConfigFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(roleConfigPath));

            if (!File.Exists(roleConfigPath))
            {
                LsrCoopRoleConfig defaultConfig = new LsrCoopRoleConfig();
                SaveRoleConfig(defaultConfig);
                Logger.Info($"[LsrCoop.Server] created role config: {roleConfigPath}");
                return defaultConfig;
            }

            try
            {
                string json = File.ReadAllText(roleConfigPath);
                LsrCoopRoleConfig config = JsonSerializer.Deserialize<LsrCoopRoleConfig>(json) ?? new LsrCoopRoleConfig();
                config.AdminIds = config.AdminIds ?? new List<string>();
                config.TrustedHostIds = config.TrustedHostIds ?? new List<string>();
                Logger.Info($"[LsrCoop.Server] role config loaded: world={config.WorldId}, admins={config.AdminIds.Count}, trustedHosts={config.TrustedHostIds.Count}");
                return config;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LsrCoop.Server] failed to load role config, using defaults: {ex.Message}");
                return new LsrCoopRoleConfig();
            }
        }

        private void SaveRoleConfig(LsrCoopRoleConfig config)
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(roleConfigPath, json);
        }

        private CoopWorldProfileStore LoadWorldProfileStore()
        {
            worldProfileStorePath = Path.Combine(GetDataFolder(), WorldProfileStoreFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(worldProfileStorePath));

            if (!File.Exists(worldProfileStorePath))
            {
                CoopWorldProfileStore defaultStore = new CoopWorldProfileStore { WorldId = roleConfig.WorldId };
                SaveWorldProfileStore(defaultStore);
                Logger.Info($"[LsrCoop.Server] created world profile store: {worldProfileStorePath}");
                return defaultStore;
            }

            try
            {
                string json = File.ReadAllText(worldProfileStorePath);
                CoopWorldProfileStore store = JsonSerializer.Deserialize<CoopWorldProfileStore>(json) ?? new CoopWorldProfileStore();
                store.Profiles = store.Profiles ?? new List<CoopPlayerProfile>();
                store.WorldId = string.IsNullOrWhiteSpace(store.WorldId) ? roleConfig.WorldId : store.WorldId;

                if (!string.Equals(store.WorldId, roleConfig.WorldId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warning($"[LsrCoop.Server] world profile store id {store.WorldId} does not match role config world id {roleConfig.WorldId}");
                }

                Logger.Info($"[LsrCoop.Server] world profiles loaded: world={store.WorldId}, profiles={store.Profiles.Count}");
                return store;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[LsrCoop.Server] failed to load world profiles, using empty store: {ex.Message}");
                return new CoopWorldProfileStore { WorldId = roleConfig.WorldId };
            }
        }

        private void SaveWorldProfileStore(CoopWorldProfileStore store)
        {
            string json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(worldProfileStorePath, json);
        }

        private string GetDataFolder()
        {
            if (!string.IsNullOrWhiteSpace(CurrentResource?.DataFolder))
            {
                return CurrentResource.DataFolder;
            }

            return Path.Combine(AppContext.BaseDirectory, "Resources", "Server", "data", "LsrCoop.Server");
        }

        private void RefreshConnectedClients()
        {
            Dictionary<int, Client> allClients = API?.GetAllClients();
            if (allClients == null)
            {
                return;
            }

            foreach (Client client in allClients.Values)
            {
                CoopClientStatus status = RegisterClient(client, "refresh");
                SendRegistrationState(status, "refresh");
            }
        }

        private void OnClientReady(object eventPayload)
        {
            CoopClientStatus status = RegisterClient(ExtractClient(eventPayload), "ready");
            SendRegistrationState(status, "client-ready");
            EvaluateActiveHost("client-ready");
        }

        private void OnClientDisconnected(object eventPayload)
        {
            Client client = ExtractClient(eventPayload);
            string profileId = GetClientProfileId(client);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                RefreshConnectedClients();
                EvaluateActiveHost("disconnect-refresh");
                return;
            }

            clientStatuses.Remove(profileId);
            Logger.Info($"[LsrCoop.Server] client disconnected: {profileId}");

            if (string.Equals(activeHostId, profileId, StringComparison.OrdinalIgnoreCase))
            {
                ReleaseActiveHost("active-host-left");
            }

            EvaluateActiveHost("client-disconnected");
        }

        private CoopClientStatus RegisterClient(Client client, string reason)
        {
            string profileId = GetClientProfileId(client);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            bool isNew = !clientStatuses.TryGetValue(profileId, out CoopClientStatus status);
            if (isNew)
            {
                status = new CoopClientStatus(profileId, client);
                clientStatuses[profileId] = status;
            }
            else
            {
                status.Client = client;
            }

            status.ClientId = GetClientId(client);
            status.Profile = LoadOrCreateProfile(status);

            if (isNew)
            {
                Logger.Info($"[LsrCoop.Server] client registered ({reason}): {profileId}, role={GetRoleName(profileId)}, trustedHost={IsTrustedHost(profileId)}, compatibility={status.CompatibilityState}");
            }

            return status;
        }

        private CoopPlayerProfile LoadOrCreateProfile(CoopClientStatus status)
        {
            CoopPlayerProfile profile = worldProfiles.Profiles.FirstOrDefault(x => string.Equals(x.ProfileId, status.ProfileId, StringComparison.OrdinalIgnoreCase));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string role = GetRoleName(status.ProfileId);
            bool created = false;

            if (profile == null)
            {
                profile = new CoopPlayerProfile
                {
                    ProfileId = status.ProfileId,
                    DisplayName = GetClientName(status.Client),
                    CreatedUtc = now
                };
                worldProfiles.Profiles.Add(profile);
                created = true;
            }

            profile.ClientId = status.ClientId;
            profile.DisplayName = GetClientName(status.Client);
            profile.Role = role;
            profile.LastSeenUtc = now;
            SaveWorldProfileStore(worldProfiles);

            if (created)
            {
                Logger.Info($"[LsrCoop.Server] created co-op profile: world={worldProfiles.WorldId}, profile={profile.ProfileId}, role={profile.Role}");
            }

            return profile;
        }

        private void SendRegistrationState(CoopClientStatus status, string reason)
        {
            if (status?.Client == null || status.Profile == null)
            {
                return;
            }

            status.Client.SendCustomEvent(PlayerRegisteredEventHash, new object[]
            {
                worldProfiles.WorldId,
                status.Profile.ProfileId,
                status.ClientId,
                status.Profile.DisplayName,
                status.Profile.Role,
                status.CompatibilityState.ToString()
            });

            if (status.Profile.Character == null)
            {
                status.Client.SendCustomEvent(CharacterCreateRequiredEventHash, new object[] { worldProfiles.WorldId, status.Profile.ProfileId, reason });
                Logger.Info($"[LsrCoop.Server] character create required: {status.Profile.ProfileId} ({reason})");
                return;
            }

            status.Client.SendCustomEvent(CharacterSnapshotEventHash, new object[]
            {
                worldProfiles.WorldId,
                status.Profile.ProfileId,
                JsonSerializer.Serialize(status.Profile.Character)
            });
            Logger.Info($"[LsrCoop.Server] character snapshot sent: {status.Profile.ProfileId} ({reason})");
        }

        private CoopPlayerProfile GetProfile(string profileId)
        {
            return worldProfiles.Profiles.FirstOrDefault(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveAcceptedAppearance(CoopPlayerProfile profile, CoopAppearanceState appearance)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (profile.Character == null)
            {
                profile.Character = new CoopCharacterSnapshot
                {
                    CharacterId = profile.ProfileId,
                    ProfileId = profile.ProfileId,
                    DisplayName = profile.DisplayName,
                    ModelName = appearance.ModelName,
                    UpdatedUtc = now
                };
            }

            profile.Character.Appearance = appearance;
            profile.Character.UpdatedUtc = now;
            profile.Character.DisplayName = profile.DisplayName;
            if (!string.IsNullOrWhiteSpace(appearance.ModelName))
            {
                profile.Character.ModelName = appearance.ModelName;
            }

            SaveWorldProfileStore(worldProfiles);
        }

        private bool IsModelChangeAfterCreation(CoopPlayerProfile profile, CoopAppearanceState appearance)
        {
            if (profile.Character == null || appearance == null || string.IsNullOrWhiteSpace(appearance.ModelName))
            {
                return false;
            }

            string existingModel = !string.IsNullOrWhiteSpace(profile.Character.ModelName)
                ? profile.Character.ModelName
                : profile.Character.Appearance?.ModelName;

            return !string.IsNullOrWhiteSpace(existingModel) && !string.Equals(existingModel, appearance.ModelName, StringComparison.OrdinalIgnoreCase);
        }

        private void BroadcastCharacterSnapshot(CoopPlayerProfile profile)
        {
            if (profile?.Character == null)
            {
                return;
            }

            BroadcastEvent(CharacterSnapshotEventHash, new object[]
            {
                worldProfiles.WorldId,
                profile.ProfileId,
                JsonSerializer.Serialize(profile.Character)
            });
        }

        private void EvaluateActiveHost(string reason)
        {
            if (!string.IsNullOrWhiteSpace(activeHostId) && clientStatuses.ContainsKey(activeHostId) && IsTrustedHost(activeHostId) && IsCompatible(activeHostId))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(activeHostId))
            {
                ReleaseActiveHost("active-host-invalid");
            }

            Client nextHost = clientStatuses
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Where(x => IsTrustedHost(x.Key) && x.Value.CompatibilityState == CoopClientCompatibilityState.Compatible)
                .Select(x => x.Value.Client)
                .FirstOrDefault();

            if (nextHost == null)
            {
                AnnounceActiveHostUnavailable(reason);
                return;
            }

            AssignActiveHost(nextHost, reason);
        }

        private void AssignActiveHost(Client client, string reason)
        {
            activeHostId = GetClientProfileId(client);
            activeHostUnavailableAnnounced = false;
            Logger.Info($"[LsrCoop.Server] active host assigned: {activeHostId} ({reason})");
            BroadcastEvent(ActiveHostAssignedEventHash, new object[] { roleConfig.WorldId, activeHostId, reason });
        }

        private void ReleaseActiveHost(string reason)
        {
            if (string.IsNullOrWhiteSpace(activeHostId))
            {
                return;
            }

            string releasedHostId = activeHostId;
            activeHostId = null;
            Logger.Info($"[LsrCoop.Server] active host released: {releasedHostId} ({reason})");
            BroadcastEvent(ActiveHostReleasedEventHash, new object[] { roleConfig.WorldId, releasedHostId, reason });
        }

        private void AnnounceActiveHostUnavailable(string reason)
        {
            if (activeHostUnavailableAnnounced)
            {
                return;
            }

            activeHostUnavailableAnnounced = true;
            Logger.Info($"[LsrCoop.Server] active host unavailable; no compatible connected TrustedHost ({reason})");
            BroadcastEvent(ActiveHostUnavailableEventHash, new object[] { roleConfig.WorldId, reason });
        }

        private bool IsTrustedHost(string profileId)
        {
            return roleConfig.TrustedHostIds.Any(x => string.Equals(x, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsAdmin(string profileId)
        {
            return roleConfig.AdminIds.Any(x => string.Equals(x, profileId, StringComparison.OrdinalIgnoreCase));
        }

        private string GetRoleName(string profileId)
        {
            bool isAdmin = IsAdmin(profileId);
            bool isTrustedHost = IsTrustedHost(profileId);

            if (isAdmin && isTrustedHost)
            {
                return "Admin,TrustedHost";
            }

            if (isAdmin)
            {
                return LsrCoopRole.Admin.ToString();
            }

            if (isTrustedHost)
            {
                return LsrCoopRole.TrustedHost.ToString();
            }

            return LsrCoopRole.Player.ToString();
        }

        private void BroadcastEvent(int eventHash, object[] args)
        {
            foreach (Client client in clientStatuses.Values.Select(x => x.Client).ToArray())
            {
                try
                {
                    client.SendCustomEvent(eventHash, args);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[LsrCoop.Server] failed to send event {eventHash} to {GetClientName(client)}: {ex.Message}");
                }
            }
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

        private CoopClientCompatibilityState EvaluateCompatibility(CoopClientStatus status)
        {
            if (!status.RequiredResourceLoaded)
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            if (!string.Equals(status.CoopBuildVersion, RequiredCoopBuildVersion, StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            if (string.IsNullOrWhiteSpace(status.LsrVersion) || string.Equals(status.LsrVersion, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Unknown;
            }

            if (!string.Equals(status.LsrVersion, RequiredLsrVersion, StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            if (!string.Equals(status.ConfigVersion, RequiredConfigVersion, StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            return CoopClientCompatibilityState.Compatible;
        }

        private bool IsCompatible(string profileId)
        {
            return clientStatuses.TryGetValue(profileId, out CoopClientStatus status) && status.CompatibilityState == CoopClientCompatibilityState.Compatible;
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

        private string GetClientProfileId(Client client)
        {
            return string.IsNullOrWhiteSpace(client?.Username) ? null : client.Username;
        }

        private string GetClientId(Client client)
        {
            if (client == null)
            {
                return null;
            }

            string[] propertyNames = { "Id", "ID", "NetHandle", "Handle" };
            foreach (string propertyName in propertyNames)
            {
                object value = client.GetType().GetProperty(propertyName)?.GetValue(client);
                if (value != null)
                {
                    return value.ToString();
                }
            }

            return GetClientProfileId(client);
        }

        private string GetClientName(Client client)
        {
            return string.IsNullOrWhiteSpace(client?.Username) ? "unknown" : client.Username;
        }
    }
}
