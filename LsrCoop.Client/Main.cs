using RageCoop.Core.Scripting;
using RageCoop.Client.Scripting;
using GTA;
using GTA.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private const string CharacterCreatedBridgeFileName = "LsrCoopCharacterCreated.txt";
        private const string CharacterSnapshotBridgeFileName = "LsrCoopCharacterSnapshot.txt";
        private const string GameplayOutboundBridgeSearchPattern = "LsrCoopGameplayOut.*.txt";
        private const string GameplayInboundBridgeFilePrefix = "LsrCoopGameplayIn.";

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
        private readonly string bridgeSessionId = Guid.NewGuid().ToString("N");
        private readonly CoopAppearanceCaptureService appearanceCaptureService = new CoopAppearanceCaptureService();
        private readonly CoopAppearanceApplyService appearanceApplyService = new CoopAppearanceApplyService();
        private readonly Dictionary<string, PvpVictimDamageState> pvpVictimDamageStates = new Dictionary<string, PvpVictimDamageState>(StringComparer.OrdinalIgnoreCase);
        private CoopWorldSnapshotDto latestWorldSnapshot;
        private string localWorldId;
        private string localProfileId;
        private string localRole;
        private string activeHostProfileId;
        private bool localCharacterReadyForSimulation;
        private bool characterCreationRequired;
        private bool characterCreationRequestSent;
        private DateTimeOffset characterCreateRequiredUtc;
        private bool clientTickSubscribed;
        private string lastLoggedStartupModeKey;
        private DateTimeOffset lastCharacterCreatedBridgePollUtc;
        private DateTimeOffset lastGameplayBridgePollUtc;
        private readonly HashSet<string> processedCharacterCreationNonces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> processedGameplayBridgeNonces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Main()
        {
            serializer.MaxJsonLength = int.MaxValue;
        }

        public override void OnStart()
        {
            Logger.Info("[LsrCoop.Client] loaded");
            CleanupStaleGameplayBridgeFiles();
            UpdateBridgeWaiting(localWorldId);
            RegisterCustomEventStubs();
            SubscribeClientTick();
            SendLoadPing();
            SendCompatibilityReport();
        }

        public override void OnStop()
        {
            UnsubscribeClientTick();
            ExitCharacterCreationSafeState();
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

        private void SubscribeClientTick()
        {
            if (clientTickSubscribed)
            {
                return;
            }

            API.Events.OnTick += OnClientTick;
            clientTickSubscribed = true;
            Logger.Info("[LsrCoop.Client] character creation bridge poller started");
        }

        private void UnsubscribeClientTick()
        {
            if (!clientTickSubscribed)
            {
                return;
            }

            API.Events.OnTick -= OnClientTick;
            clientTickSubscribed = false;
            Logger.Info("[LsrCoop.Client] character creation bridge poller stopped");
        }

        private void OnClientTick()
        {
            PollCharacterCreationBridge();
            PollGameplayBridge();
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
            localRole = role;
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
                if (string.Equals(profile.ProfileId, localProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    WriteCharacterSnapshotBridge(snapshot.WorldId, profile.ProfileId, profile.Character, profile.InventoryMoney, profile.Weapons, profile.OwnedVehicles, profile.PropertyOwnership, profile.CriminalHistory, profile.GangReputation);
                }
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
            CoopInventoryMoneySnapshot inventoryMoney = Deserialize<CoopInventoryMoneySnapshot>(GetArg(args, 3));
            CoopWeaponSnapshot weapons = Deserialize<CoopWeaponSnapshot>(GetArg(args, 4));
            CoopCriminalHistoryStateDto criminalHistory = Deserialize<CoopCriminalHistoryStateDto>(GetArg(args, 5));
            CoopGangReputationStateDto gangReputation = Deserialize<CoopGangReputationStateDto>(GetArg(args, 6));
            CoopOwnedVehicleSnapshot ownedVehicles = Deserialize<CoopOwnedVehicleSnapshot>(GetArg(args, 7));
            CoopPropertyOwnershipSnapshot propertyOwnership = Deserialize<CoopPropertyOwnershipSnapshot>(GetArg(args, 8));
            Logger.Info($"[LsrCoop.Client] character snapshot property payload received: world={worldId}, profile={profileId}, properties={propertyOwnership?.Properties?.Count ?? 0}, firstProperty={DescribeFirstProperty(propertyOwnership)}");
            ApplyAppearanceIfLocal(worldId, profileId, snapshot?.Appearance);
            if (string.Equals(profileId, localProfileId, StringComparison.OrdinalIgnoreCase))
            {
                localCharacterReadyForSimulation = snapshot != null;
                if (localCharacterReadyForSimulation)
                {
                    WriteCharacterSnapshotBridge(worldId, profileId, snapshot, inventoryMoney, weapons, ownedVehicles, propertyOwnership, criminalHistory, gangReputation);
                    ExitCharacterCreationSafeState();
                }
                UpdateCurrentBridgeState();
                SendCharacterSnapshotAck(worldId, profileId);
            }
            Logger.Info($"[LsrCoop.Client] character snapshot received: world={worldId}, profile={profileId}, ownedVehicles={ownedVehicles?.Vehicles?.Count ?? 0}, properties={propertyOwnership?.Properties?.Count ?? 0}, firstProperty={DescribeFirstProperty(propertyOwnership)}, gangReputation={gangReputation?.Reputations?.Count ?? 0}, vagos={DescribeGangReputationRecord(FindGangReputationRecord(gangReputation, "AMBIENT_GANG_MEXICAN"))}");
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

        private bool SendCharacterCreatedRequest(string modelName, string displayName)
        {
            if (characterCreationRequestSent)
            {
                return false;
            }

            if (!API.IsOnServer || string.IsNullOrWhiteSpace(localWorldId) || string.IsNullOrWhiteSpace(localProfileId))
            {
                Logger.Info("[LsrCoop.Client] character created request skipped; client is not registered");
                return false;
            }

            Ped ped = Game.Player?.Character;
            if (ped == null || !ped.Exists())
            {
                Logger.Info("[LsrCoop.Client] character created request skipped; local ped unavailable");
                return false;
            }

            modelName = string.IsNullOrWhiteSpace(modelName) ? GetPedModelName(ped) : modelName;
            displayName = string.IsNullOrWhiteSpace(displayName) ? localProfileId : displayName;
            CoopAppearanceState appearance = appearanceCaptureService.Capture(ped, modelName);
            if (appearance == null)
            {
                Logger.Info("[LsrCoop.Client] character created request skipped; appearance unavailable");
                return false;
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
            return true;
        }

        private void PollCharacterCreationBridge()
        {
            if (!API.IsOnServer || string.IsNullOrWhiteSpace(localWorldId) || string.IsNullOrWhiteSpace(localProfileId))
            {
                return;
            }

            if (DateTimeOffset.UtcNow - lastCharacterCreatedBridgePollUtc < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            lastCharacterCreatedBridgePollUtc = DateTimeOffset.UtcNow;
            foreach (string path in GetCharacterCreationBridgePaths())
            {
                TryProcessCharacterCreationBridgeFile(path);
            }
        }

        private void TryProcessCharacterCreationBridgeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            Logger.Info($"[LsrCoop.Client] character bridge file found: {path}");
            Dictionary<string, string> values;
            try
            {
                values = ReadBridgeKeyValues(path);
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] character bridge read skipped: {ex.Message}");
                return;
            }

            string worldId = GetBridgeValue(values, "WorldId");
            string profileId = GetBridgeValue(values, "ProfileId");
            string nonce = GetBridgeValue(values, "Nonce");
            if (!string.Equals(worldId, localWorldId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(profileId, localProfileId, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"[LsrCoop.Client] character bridge ignored; expected world={localWorldId}, profile={localProfileId}, file world={worldId}, profile={profileId}");
                return;
            }

            if (string.IsNullOrWhiteSpace(nonce))
            {
                Logger.Info($"[LsrCoop.Client] character bridge ignored; missing nonce: {path}");
                return;
            }

            if (processedCharacterCreationNonces.Contains(nonce))
            {
                DeleteCharacterCreationBridgeFiles(nonce);
                return;
            }

            Logger.Info($"[LsrCoop.Client] character bridge accepted: world={worldId}, profile={profileId}, nonce={nonce}");
            if (SendCharacterCreatedRequest(GetBridgeValue(values, "ModelName"), GetBridgeValue(values, "PlayerName")))
            {
                processedCharacterCreationNonces.Add(nonce);
                DeleteCharacterCreationBridgeFiles(nonce);
                Logger.Info($"[LsrCoop.Client] character bridge consumed: nonce={nonce}");
            }
        }

        private void PollGameplayBridge()
        {
            if (!API.IsOnServer)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - lastGameplayBridgePollUtc < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            lastGameplayBridgePollUtc = DateTimeOffset.UtcNow;
            foreach (string path in GetGameplayOutboundBridgePaths())
            {
                TryProcessGameplayBridgeFile(path);
            }
        }

        private void TryProcessGameplayBridgeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            Dictionary<string, string> values;
            try
            {
                values = ReadBridgeKeyValues(path);
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] gameplay bridge read skipped: {ex.Message}");
                return;
            }

            string nonce = GetBridgeValue(values, "Nonce");
            if (string.IsNullOrWhiteSpace(nonce))
            {
                Logger.Info($"[LsrCoop.Client] gameplay bridge ignored; missing nonce: {path}");
                return;
            }

            if (processedGameplayBridgeNonces.Contains(nonce))
            {
                TryDeleteGameplayBridgeFile(path, nonce);
                return;
            }

            if (!IsCurrentProcessBridgeFile(values))
            {
                TryDeleteStaleGameplayBridgeFile(path, values, "process/session mismatch");
                return;
            }

            if (!IsCurrentWorldProfileBridgeFile(values))
            {
                TryDeleteStaleGameplayBridgeFile(path, values, "world/profile mismatch");
                return;
            }

            string eventType = GetBridgeValue(values, "EventType");
            string payloadJson = GetBridgeValue(values, "PayloadJson");
            bool processed = TrySendGameplayBridgeEvent(eventType, payloadJson, nonce, GetBridgeValue(values, "ProfileId"));
            if (processed)
            {
                processedGameplayBridgeNonces.Add(nonce);
                TryDeleteGameplayBridgeFile(path, nonce);
                Logger.Info($"[LsrCoop.Client] gameplay bridge consumed: type={eventType}, nonce={nonce}");
            }
        }

        private bool TrySendGameplayBridgeEvent(string eventType, string payloadJson, string nonce, string profileId)
        {
            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            if (string.Equals(eventType, "GameplayActionCommitted", StringComparison.OrdinalIgnoreCase))
            {
                API.SendCustomEvent(GameplayActionCommittedEventHash, new object[] { payloadJson, eventType ?? string.Empty, nonce ?? string.Empty, profileId ?? string.Empty });
                Logger.Info($"[LsrCoop.Client] gameplay commit sent: profile={profileId}");
                return true;
            }

            if (string.Equals(eventType, "PvpCrimeReported", StringComparison.OrdinalIgnoreCase))
            {
                API.SendCustomEvent(PvpCrimeReportedEventHash, new object[] { payloadJson, eventType ?? string.Empty, nonce ?? string.Empty, profileId ?? string.Empty });
                Logger.Info($"[LsrCoop.Client] PvP crime reported: profile={profileId}");
                return true;
            }

            if (string.Equals(eventType, "CriminalJusticeSnapshotCommitted", StringComparison.OrdinalIgnoreCase))
            {
                API.SendCustomEvent(CriminalJusticeSnapshotCommittedEventHash, new object[] { payloadJson, eventType ?? string.Empty, nonce ?? string.Empty, profileId ?? string.Empty });
                Logger.Info($"[LsrCoop.Client] criminal history event forwarded: profile={profileId}");
                return true;
            }

            if (string.Equals(eventType, "GangReputationSnapshotCommitted", StringComparison.OrdinalIgnoreCase))
            {
                API.SendCustomEvent(GangReputationSnapshotCommittedEventHash, new object[] { payloadJson, eventType ?? string.Empty, nonce ?? string.Empty, profileId ?? string.Empty });
                Logger.Info($"[LsrCoop.Client] gang reputation snapshot sent: profile={profileId}");
                return true;
            }

            Logger.Info($"[LsrCoop.Client] gameplay bridge ignored; unknown type={eventType}");
            return true;
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

        private void WriteCharacterSnapshotBridge(string worldId, string profileId, CoopCharacterSnapshot snapshot, CoopInventoryMoneySnapshot inventoryMoney = null, CoopWeaponSnapshot weapons = null, CoopOwnedVehicleSnapshot ownedVehicles = null, CoopPropertyOwnershipSnapshot propertyOwnership = null, CoopCriminalHistoryStateDto criminalHistory = null, CoopGangReputationStateDto gangReputation = null)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(profileId))
            {
                return;
            }

            string modelName = !string.IsNullOrWhiteSpace(snapshot.ModelName)
                ? snapshot.ModelName
                : snapshot.Appearance?.ModelName ?? string.Empty;
            string serializedProperties = SerializeProperties(propertyOwnership?.Properties);
            string nonce = Guid.NewGuid().ToString("N");
            string[] lines =
            {
                "BridgeVersion=1",
                "TransportMode=RAGECOOP",
                $"ProcessId={Process.GetCurrentProcess().Id}",
                $"WorldId={EscapeBridgeValue(worldId)}",
                $"ProfileId={EscapeBridgeValue(profileId)}",
                $"CharacterId={EscapeBridgeValue(snapshot.CharacterId ?? profileId)}",
                $"DisplayName={EscapeBridgeValue(snapshot.DisplayName ?? profileId)}",
                $"ModelName={EscapeBridgeValue(modelName)}",
                $"IsMale={GetLocalPedIsMale()}",
                $"Components={SerializeComponents(snapshot.Appearance?.Components)}",
                $"Props={SerializeProps(snapshot.Appearance?.Props)}",
                $"InventoryMoneySnapshotId={EscapeBridgeValue(inventoryMoney?.SnapshotId ?? string.Empty)}",
                $"OnHandCash={EscapeBridgeValue(inventoryMoney?.OnHandCash.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"TotalAccountMoney={EscapeBridgeValue(inventoryMoney?.TotalAccountMoney.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"InventoryMoneySnapshotUtc={EscapeBridgeValue(FormatDateTime(inventoryMoney?.SnapshotUtc))}",
                $"InventoryItems={EscapeBridgeValue(SerializeInventoryItems(inventoryMoney?.InventoryItems))}",
                $"BankAccounts={EscapeBridgeValue(SerializeBankAccounts(inventoryMoney?.BankAccounts))}",
                $"WeaponSnapshotId={EscapeBridgeValue(weapons?.SnapshotId ?? string.Empty)}",
                $"WeaponSnapshotUtc={EscapeBridgeValue(FormatDateTime(weapons?.SnapshotUtc))}",
                $"Weapons={EscapeBridgeValue(SerializeWeapons(weapons?.Weapons))}",
                $"OwnedVehicleSnapshotId={EscapeBridgeValue(ownedVehicles?.SnapshotId ?? string.Empty)}",
                $"OwnedVehicleSnapshotUtc={EscapeBridgeValue(FormatDateTime(ownedVehicles?.SnapshotUtc))}",
                $"OwnedVehicles={EscapeBridgeValue(SerializeOwnedVehicles(ownedVehicles?.Vehicles))}",
                $"PropertyOwnershipSnapshotId={EscapeBridgeValue(propertyOwnership?.SnapshotId ?? string.Empty)}",
                $"PropertyOwnershipSnapshotUtc={EscapeBridgeValue(FormatDateTime(propertyOwnership?.SnapshotUtc))}",
                $"Properties={EscapeBridgeValue(serializedProperties)}",
                $"CriminalHistoryHasHistory={EscapeBridgeValue((criminalHistory?.HasHistory == true).ToString().ToLowerInvariant())}",
                $"CriminalHistoryLastSeenX={EscapeBridgeValue(criminalHistory?.LastSeenX.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"CriminalHistoryLastSeenY={EscapeBridgeValue(criminalHistory?.LastSeenY.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"CriminalHistoryLastSeenZ={EscapeBridgeValue(criminalHistory?.LastSeenZ.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"CriminalHistoryWantedLevel={EscapeBridgeValue(criminalHistory?.WantedLevel.ToString(CultureInfo.InvariantCulture) ?? string.Empty)}",
                $"CriminalHistoryDateTimeLastWantedEnded={EscapeBridgeValue(FormatDateTime(criminalHistory?.DateTimeLastWantedEnded))}",
                $"CriminalHistoryUpdatedUtc={EscapeBridgeValue(FormatDateTime(criminalHistory?.UpdatedUtc))}",
                $"CriminalHistoryCrimes={EscapeBridgeValue(SerializeCriminalHistoryCrimes(criminalHistory?.Crimes))}",
                $"GangReputationStateId={EscapeBridgeValue(gangReputation?.StateId ?? string.Empty)}",
                $"GangReputationCurrentGangId={EscapeBridgeValue(gangReputation?.CurrentGangId ?? string.Empty)}",
                $"GangReputationUpdatedUtc={EscapeBridgeValue(FormatDateTime(gangReputation?.UpdatedUtc))}",
                $"GangReputationRecords={EscapeBridgeValue(SerializeGangReputations(gangReputation?.Reputations))}",
                $"TimestampUtc={EscapeBridgeValue(DateTime.UtcNow.ToString("O"))}",
                $"Nonce={EscapeBridgeValue(nonce)}",
            };

            foreach (string folder in GetBridgeStateFolders())
            {
                WriteAtomicBridgeFile(folder, CharacterSnapshotBridgeFileName, lines, nonce);
            }

            Logger.Info($"[LsrCoop.Client] character snapshot bridge written: world={worldId}, profile={profileId}, model={modelName}, inventoryItems={inventoryMoney?.InventoryItems?.Count ?? 0}, weapons={weapons?.Weapons?.Count ?? 0}, ownedVehicles={ownedVehicles?.Vehicles?.Count ?? 0}, properties={propertyOwnership?.Properties?.Count ?? 0}, criminalHistory={criminalHistory?.Crimes?.Count ?? 0}, gangReputation={gangReputation?.Reputations?.Count ?? 0}");
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

            WriteGameplayInboundBridgeFile("GameplayActionResult", new Dictionary<string, string>
            {
                ["WorldId"] = result.WorldId ?? localWorldId ?? string.Empty,
                ["ProfileId"] = result.SourceProfileId ?? localProfileId ?? string.Empty,
                ["RequestId"] = result.RequestId ?? string.Empty,
                ["Accepted"] = result.Accepted.ToString().ToLowerInvariant(),
                ["RequiresResync"] = result.RequiresResync.ToString().ToLowerInvariant(),
                ["Reason"] = result.Reason ?? string.Empty,
            });

            Logger.Info($"[LsrCoop.Client] gameplay action result: request={result.RequestId}, accepted={result.Accepted}, resync={result.RequiresResync}");
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
                if (string.Equals(localProfileId, activeHostProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    WriteApplyRemotePvpCrimeBridge(report);
                }
                else
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
            report.OffenderPedHandle = offenderPed?.Handle ?? report.OffenderPedHandle;
            report.VictimPedHandle = victimPed?.Handle ?? report.VictimPedHandle;
            WriteApplyRemotePvpCrimeBridge(report);

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
            LogStartupBridgeMode("Session", string.Empty);
        }

        private void UpdateBridgeActiveHost(string worldId, string activeHostProfileId)
        {
            localWorldId = string.IsNullOrWhiteSpace(worldId) ? localWorldId : worldId;
            WriteBridgeState(true, true, localWorldId, localProfileId, activeHostProfileId, localCharacterReadyForSimulation);
            InvokeLsrBridge("SetActiveHost", localWorldId ?? string.Empty, activeHostProfileId ?? string.Empty, localProfileId ?? string.Empty, localCharacterReadyForSimulation);
            LogStartupBridgeMode("ActiveHostAssigned", activeHostProfileId);
        }

        private void UpdateBridgeWaiting(string worldId)
        {
            localWorldId = string.IsNullOrWhiteSpace(worldId) ? localWorldId : worldId;
            WriteBridgeState(true, false, localWorldId, localProfileId, string.Empty, localCharacterReadyForSimulation);
            InvokeLsrBridge("ClearActiveHost", localWorldId ?? string.Empty);
            LogStartupBridgeMode("WaitingForActiveHost", string.Empty);
        }

        private void SetBridgeDisabled()
        {
            activeHostProfileId = string.Empty;
            localRole = string.Empty;
            localCharacterReadyForSimulation = false;
            WriteBridgeState(false, false, string.Empty, string.Empty, string.Empty, false);
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
            if (!string.Equals(methodName, "SetDisabled", StringComparison.Ordinal))
            {
                InvokeLsrStaticBridge("LosSantosRED.lsr.Coop.Core.CoopStartupBridge", "SetBridgeSessionId", bridgeSessionId);
                InvokeLsrStaticBridge("LosSantosRED.lsr.Coop.Core.CoopStartupBridge", "SetLocalRole", localRole ?? string.Empty);
            }
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
                DateTimeLastWantedEnded = GetDateTimeOffsetOrDefault(state, "DateTimeLastWantedEnded"),
                UpdatedUtc = GetDateTimeOffset(state, "UpdatedUtc"),
                ClearReason = GetString(state, "ClearReason"),
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
                    GangName = GetString(record, "GangName"),
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
                    SavedGameLocationXml = GetString(property, "SavedGameLocationXml"),
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
                    VehicleSaveStatusXml = GetString(vehicle, "VehicleSaveStatusXml"),
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

        private DateTimeOffset GetDateTimeOffsetOrDefault(object source, string propertyName)
        {
            object value = GetPropertyValue(source, propertyName);
            return value is DateTimeOffset dateTimeOffset ? dateTimeOffset : DateTimeOffset.MinValue;
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

        private IEnumerable<string> GetCharacterCreationBridgePaths()
        {
            foreach (string folder in GetBridgeStateFolders())
            {
                yield return Path.Combine(folder, CharacterCreatedBridgeFileName);
            }
        }

        private IEnumerable<string> GetGameplayOutboundBridgePaths()
        {
            foreach (string folder in GetBridgeStateFolders())
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string path in Directory.GetFiles(folder, GameplayOutboundBridgeSearchPattern))
                {
                    yield return path;
                }
            }
        }

        private void CleanupStaleGameplayBridgeFiles()
        {
            foreach (string folder in GetBridgeStateFolders())
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string path in Directory.GetFiles(folder, "LsrCoopGameplayOut.*.tmp"))
                {
                    TryDeleteOldBridgeTempFile(path);
                }
            }
        }

        private Dictionary<string, string> ReadBridgeKeyValues(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int separatorIndex = line?.IndexOf('=') ?? -1;
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex);
                string value = line.Substring(separatorIndex + 1);
                values[key] = Uri.UnescapeDataString(value ?? string.Empty);
            }

            return values;
        }

        private string GetBridgeValue(Dictionary<string, string> values, string key)
        {
            return values != null && values.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        }

        private void WriteAtomicBridgeFile(string folder, string fileName, string[] lines, string nonce)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                string targetPath = Path.Combine(folder, fileName);
                string tempPath = targetPath + "." + nonce + ".tmp";
                File.WriteAllLines(tempPath, lines);
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Replace(tempPath, targetPath, null);
                        return;
                    }
                    catch
                    {
                        File.Delete(targetPath);
                    }
                }

                File.Move(tempPath, targetPath);
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] bridge file write skipped: {ex.Message}");
            }
        }

        private void WriteApplyRemotePvpCrimeBridge(CoopPvpCrimeReportDto report)
        {
            if (report == null)
            {
                return;
            }

            WriteGameplayInboundBridgeFile("ApplyRemotePvpCrime", new Dictionary<string, string>
            {
                ["WorldId"] = report.WorldId ?? localWorldId ?? string.Empty,
                ["ProfileId"] = report.SourceProfileId ?? localProfileId ?? string.Empty,
                ["SourceProfileId"] = report.SourceProfileId ?? string.Empty,
                ["TargetProfileId"] = report.TargetProfileId ?? string.Empty,
                ["OffenderPedHandle"] = report.OffenderPedHandle.ToString(),
                ["VictimPedHandle"] = report.VictimPedHandle.ToString(),
                ["WasKilled"] = report.WasKilled.ToString().ToLowerInvariant(),
                ["WasShot"] = report.WasShot.ToString().ToLowerInvariant(),
                ["WasMeleeAttacked"] = report.WasMeleeAttacked.ToString().ToLowerInvariant(),
                ["WasHitByVehicle"] = report.WasHitByVehicle.ToString().ToLowerInvariant(),
            });
        }

        private void WriteGameplayInboundBridgeFile(string eventType, Dictionary<string, string> values)
        {
            string nonce = Guid.NewGuid().ToString("N");
            List<string> lines = new List<string>
            {
                "BridgeVersion=1",
                "TransportMode=RAGECOOP",
                "Direction=RAGECOOP_TO_LSR",
                $"ProcessId={Process.GetCurrentProcess().Id}",
                $"EventType={EscapeBridgeValue(eventType)}",
                $"TimestampUtc={EscapeBridgeValue(DateTime.UtcNow.ToString("O"))}",
                $"Nonce={EscapeBridgeValue(nonce)}",
            };

            foreach (KeyValuePair<string, string> pair in values ?? new Dictionary<string, string>())
            {
                lines.Add($"{pair.Key}={EscapeBridgeValue(pair.Value)}");
            }

            foreach (string folder in GetBridgeStateFolders())
            {
                WriteAtomicBridgeFile(folder, GameplayInboundBridgeFilePrefix + nonce + ".txt", lines.ToArray(), nonce);
            }
        }

        private string SerializeComponents(IEnumerable<CoopPedComponentState> components)
        {
            if (components == null)
            {
                return string.Empty;
            }

            return string.Join(";", components.Select(component => string.Join(",", new[]
            {
                component.ComponentId.ToString(),
                component.DrawableId.ToString(),
                component.TextureId.ToString(),
                component.PaletteId.ToString()
            })));
        }

        private string SerializeProps(IEnumerable<CoopPedPropState> props)
        {
            if (props == null)
            {
                return string.Empty;
            }

            return string.Join(";", props.Select(prop => string.Join(",", new[]
            {
                prop.PropId.ToString(),
                prop.DrawableId.ToString(),
                prop.TextureId.ToString(),
                prop.IsCleared.ToString().ToLowerInvariant()
            })));
        }

        private string SerializeInventoryItems(IEnumerable<CoopInventoryItemState> items)
        {
            if (items == null)
            {
                return string.Empty;
            }

            return string.Join(";", items.Select(item => string.Join(",", new[]
            {
                EscapeListPart(item.ItemName),
                item.RemainingPercent.ToString(CultureInfo.InvariantCulture)
            })));
        }

        private string SerializeBankAccounts(IEnumerable<CoopBankAccountState> accounts)
        {
            if (accounts == null)
            {
                return string.Empty;
            }

            return string.Join(";", accounts.Select(account => string.Join(",", new[]
            {
                EscapeListPart(account.BankContactName),
                EscapeListPart(account.AccountName),
                account.Money.ToString(CultureInfo.InvariantCulture),
                account.IsPrimary.ToString().ToLowerInvariant()
            })));
        }

        private string SerializeWeapons(IEnumerable<CoopWeaponRecord> weapons)
        {
            if (weapons == null)
            {
                return string.Empty;
            }

            return string.Join(";", weapons.Select(weapon => string.Join(",", new[]
            {
                EscapeListPart(weapon.WeaponHash),
                EscapeListPart(weapon.WeaponName),
                EscapeListPart(weapon.Category),
                weapon.Ammo.ToString(CultureInfo.InvariantCulture),
                weapon.IsLegal.ToString().ToLowerInvariant(),
                weapon.IsEquipped.ToString().ToLowerInvariant()
            })));
        }

        private string SerializeOwnedVehicles(IEnumerable<CoopOwnedVehicleRecord> vehicles)
        {
            if (vehicles == null)
            {
                return string.Empty;
            }

            return string.Join(";", vehicles.Select(vehicle => string.Join(",", new[]
            {
                EscapeListPart(vehicle.VehicleId),
                EscapeListPart(vehicle.ModelHash),
                EscapeListPart(vehicle.ModelName),
                EscapeListPart(vehicle.VehicleSaveStatusXml),
                EscapeListPart(vehicle.PlateNumber),
                vehicle.PlateType.ToString(CultureInfo.InvariantCulture),
                vehicle.PlateIsWanted.ToString().ToLowerInvariant(),
                vehicle.PositionX.ToString(CultureInfo.InvariantCulture),
                vehicle.PositionY.ToString(CultureInfo.InvariantCulture),
                vehicle.PositionZ.ToString(CultureInfo.InvariantCulture),
                vehicle.Heading.ToString(CultureInfo.InvariantCulture),
                vehicle.IsImpounded.ToString().ToLowerInvariant(),
                vehicle.DateTimeImpounded.ToString("O", CultureInfo.InvariantCulture),
                vehicle.TimesImpounded.ToString(CultureInfo.InvariantCulture),
                EscapeListPart(vehicle.ImpoundedLocation),
                vehicle.StoredCash.ToString(CultureInfo.InvariantCulture)
            })));
        }

        private string SerializeProperties(IEnumerable<CoopPropertyOwnershipRecord> properties)
        {
            if (properties == null)
            {
                return string.Empty;
            }

            return string.Join(";", properties.Select(property => string.Join(",", new[]
            {
                EscapeListPart(property.PropertyId),
                EscapeListPart(property.Name),
                EscapeListPart(property.PropertyType),
                property.IsOwned.ToString().ToLowerInvariant(),
                property.IsRented.ToString().ToLowerInvariant(),
                property.IsRentedOut.ToString().ToLowerInvariant(),
                property.EntranceX.ToString(CultureInfo.InvariantCulture),
                property.EntranceY.ToString(CultureInfo.InvariantCulture),
                property.EntranceZ.ToString(CultureInfo.InvariantCulture),
                property.CurrentSalesPrice.ToString(CultureInfo.InvariantCulture),
                property.PayoutDate.ToString("O", CultureInfo.InvariantCulture),
                property.DateOfLastPayout.ToString("O", CultureInfo.InvariantCulture),
                property.RentalPaymentDate.ToString("O", CultureInfo.InvariantCulture),
                property.DateOfLastRentalPayment.ToString("O", CultureInfo.InvariantCulture),
                EscapeListPart(property.SavedGameLocationXml)
            })));
        }

        private string DescribeFirstProperty(CoopPropertyOwnershipSnapshot snapshot)
        {
            CoopPropertyOwnershipRecord property = snapshot?.Properties?.FirstOrDefault();
            return property == null
                ? "none"
                : $"{property.PropertyId}|name={property.Name}|type={property.PropertyType}|owned={property.IsOwned}|rented={property.IsRented}|rentedOut={property.IsRentedOut}";
        }

        private string DescribeFirstPropertyLine(string serializedProperties)
        {
            if (string.IsNullOrWhiteSpace(serializedProperties))
            {
                return "empty";
            }

            string first = serializedProperties.Split(';').FirstOrDefault() ?? string.Empty;
            return first.Length > 240 ? first.Substring(0, 240) + "..." : first;
        }

        private string SerializeCriminalHistoryCrimes(IEnumerable<CoopCriminalHistoryCrimeRecordDto> crimes)
        {
            if (crimes == null)
            {
                return string.Empty;
            }

            return string.Join(";", crimes.Select(crime => string.Join(",", new[]
            {
                EscapeListPart(crime.CrimeId),
                EscapeListPart(crime.CrimeName),
                crime.Instances.ToString(CultureInfo.InvariantCulture),
                crime.ResultingWantedLevel.ToString(CultureInfo.InvariantCulture),
                crime.Priority.ToString(CultureInfo.InvariantCulture),
                crime.ResultsInLethalForce.ToString().ToLowerInvariant()
            })));
        }

        private string SerializeGangReputations(IEnumerable<CoopGangReputationRecordDto> records)
        {
            if (records == null)
            {
                return string.Empty;
            }

            return string.Join(";", records.Where(record => !string.IsNullOrWhiteSpace(record.GangId)).Select(record => string.Join(",", new[]
            {
                EscapeListPart(record.GangId),
                EscapeListPart(record.GangName),
                record.Reputation.ToString(CultureInfo.InvariantCulture),
                record.MembersHurt.ToString(CultureInfo.InvariantCulture),
                record.MembersKilled.ToString(CultureInfo.InvariantCulture),
                record.MembersCarJacked.ToString(CultureInfo.InvariantCulture),
                record.MembersHurtInTerritory.ToString(CultureInfo.InvariantCulture),
                record.MembersKilledInTerritory.ToString(CultureInfo.InvariantCulture),
                record.MembersCarJackedInTerritory.ToString(CultureInfo.InvariantCulture),
                record.PlayerDebt.ToString(CultureInfo.InvariantCulture),
                record.IsMember.ToString().ToLowerInvariant(),
                record.IsEnemy.ToString().ToLowerInvariant(),
                record.TasksCompleted.ToString(CultureInfo.InvariantCulture)
            })));
        }

        private CoopGangReputationRecordDto FindGangReputationRecord(CoopGangReputationStateDto state, string gangId)
        {
            return state?.Reputations?
                .Where(x => string.Equals(x?.GangId, gangId, StringComparison.OrdinalIgnoreCase))
                .LastOrDefault();
        }

        private string DescribeGangReputationRecord(CoopGangReputationRecordDto record)
        {
            return record == null
                ? "missing"
                : $"{record.GangId}:rep={record.Reputation},hurt={record.MembersHurt},killed={record.MembersKilled}";
        }

        private string FormatDateTime(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : string.Empty;
        }

        private string EscapeListPart(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private string EscapeBridgeValue(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private string GetLocalPedIsMale()
        {
            try
            {
                Ped ped = Game.Player?.Character;
                if (ped != null && ped.Exists())
                {
                    return Function.Call<bool>(Hash.IS_PED_MALE, ped.Handle).ToString().ToLowerInvariant();
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private void DeleteCharacterCreationBridgeFiles(string nonce)
        {
            foreach (string path in GetCharacterCreationBridgePaths())
            {
                TryDeleteCharacterCreationBridgeFile(path, nonce);
            }
        }

        private void TryDeleteCharacterCreationBridgeFile(string path, string nonce)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                Dictionary<string, string> values = ReadBridgeKeyValues(path);
                if (string.Equals(GetBridgeValue(values, "Nonce"), nonce, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    Logger.Info($"[LsrCoop.Client] character bridge file deleted: {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] character bridge cleanup skipped: {ex.Message}");
            }
        }

        private void TryDeleteGameplayBridgeFile(string path, string nonce)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                Dictionary<string, string> values = ReadBridgeKeyValues(path);
                if (string.Equals(GetBridgeValue(values, "Nonce"), nonce, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] gameplay bridge cleanup skipped: {ex.Message}");
            }
        }

        private bool IsCurrentProcessBridgeFile(Dictionary<string, string> values)
        {
            if (values == null)
            {
                return false;
            }

            if (!string.Equals(GetBridgeValue(values, "BridgeVersion"), "1", StringComparison.Ordinal)
                || !string.Equals(GetBridgeValue(values, "TransportMode"), "RAGECOOP", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!int.TryParse(GetBridgeValue(values, "ProcessId"), out int processId)
                || processId != Process.GetCurrentProcess().Id)
            {
                return false;
            }

            string sessionId = GetBridgeValue(values, "SessionId");
            return !string.IsNullOrWhiteSpace(sessionId)
                && string.Equals(sessionId, bridgeSessionId, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCurrentWorldProfileBridgeFile(Dictionary<string, string> values)
        {
            if (values == null)
            {
                return false;
            }

            string worldId = GetBridgeValue(values, "WorldId");
            string profileId = GetBridgeValue(values, "ProfileId");
            bool worldMatches = string.IsNullOrWhiteSpace(localWorldId)
                || string.IsNullOrWhiteSpace(worldId)
                || string.Equals(worldId, localWorldId, StringComparison.OrdinalIgnoreCase);
            bool profileMatches = string.IsNullOrWhiteSpace(localProfileId)
                || string.IsNullOrWhiteSpace(profileId)
                || string.Equals(profileId, localProfileId, StringComparison.OrdinalIgnoreCase);

            return worldMatches && profileMatches;
        }

        private void TryDeleteStaleGameplayBridgeFile(string path, Dictionary<string, string> values, string reason)
        {
            try
            {
                string processId = GetBridgeValue(values, "ProcessId");
                string sessionId = GetBridgeValue(values, "SessionId");
                string worldId = GetBridgeValue(values, "WorldId");
                string profileId = GetBridgeValue(values, "ProfileId");
                File.Delete(path);
                Logger.Info($"[LsrCoop.Client] stale gameplay bridge deleted: reason={reason}, process={processId}, session={sessionId}, world={worldId}, profile={profileId}, path={path}");
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] stale gameplay bridge cleanup skipped: {ex.Message}");
            }
        }

        private void TryDeleteOldBridgeTempFile(string path)
        {
            try
            {
                FileInfo file = new FileInfo(path);
                if (file.Exists && DateTimeOffset.UtcNow - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) > TimeSpan.FromMinutes(5))
                {
                    file.Delete();
                    Logger.Info($"[LsrCoop.Client] stale gameplay bridge temp deleted: {path}");
                }
            }
            catch (Exception ex)
            {
                Logger.Info($"[LsrCoop.Client] stale gameplay bridge temp cleanup skipped: {ex.Message}");
            }
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
                $"SessionId={bridgeSessionId}",
                $"IsCoopEnabled={isCoopEnabled.ToString().ToLowerInvariant()}",
                $"HasActiveHostAssigned={hasActiveHostAssigned.ToString().ToLowerInvariant()}",
                $"CharacterReadyForSimulation={isCharacterReadyForSimulation.ToString().ToLowerInvariant()}",
                $"CharacterCreationRequired={(!isCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(localProfileId)).ToString().ToLowerInvariant()}",
                $"WorldId={worldId ?? string.Empty}",
                $"LocalProfileId={localProfileId ?? string.Empty}",
                $"LocalRole={localRole ?? string.Empty}",
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

        private void LogStartupBridgeMode(string reason, string assignedActiveHostProfileId)
        {
            bool isLocalActiveHost = !string.IsNullOrWhiteSpace(localProfileId)
                && !string.IsNullOrWhiteSpace(assignedActiveHostProfileId)
                && string.Equals(localProfileId, assignedActiveHostProfileId, StringComparison.OrdinalIgnoreCase);
            string selectedMode = !localCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(localProfileId)
                ? "BootstrapOnly"
                : isLocalActiveHost && localCharacterReadyForSimulation
                    ? "FullSimulation"
                    : localCharacterReadyForSimulation
                        ? "ClientMode"
                        : "Blocked";
            string logKey = $"{selectedMode}|{localProfileId}|{assignedActiveHostProfileId}|{localCharacterReadyForSimulation}";
            if (string.Equals(lastLoggedStartupModeKey, logKey, StringComparison.Ordinal))
            {
                return;
            }

            lastLoggedStartupModeKey = logKey;
            Logger.Info($"[LsrCoop.Client] startup mode selected: {selectedMode}, localProfile={localProfileId}, activeHost={assignedActiveHostProfileId}, reason={reason}");
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
