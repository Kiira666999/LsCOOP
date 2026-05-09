using RageCoop.Core.Scripting;
using RageCoop.Client.Scripting;
using GTA;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private static readonly int CharacterSnapshotEventHash = CustomEvents.Hash("lsrcoop.character.snapshot");
        private static readonly int AppearanceChangeRequestedEventHash = CustomEvents.Hash("lsrcoop.appearance.changeRequested");
        private static readonly int AppearanceChangedEventHash = CustomEvents.Hash("lsrcoop.appearance.changed");

        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly CoopAppearanceCaptureService appearanceCaptureService = new CoopAppearanceCaptureService();
        private readonly CoopAppearanceApplyService appearanceApplyService = new CoopAppearanceApplyService();
        private string localWorldId;
        private string localProfileId;

        public override void OnStart()
        {
            Logger.Info("[LsrCoop.Client] loaded");
            RegisterCustomEventStubs();
            SendLoadPing();
            SendCompatibilityReport();
        }

        public override void OnStop()
        {
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
            Logger.Info("[LsrCoop.Client] custom event stubs registered");
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
            localWorldId = worldId;
            localProfileId = profileId;
            Logger.Info($"[LsrCoop.Client] player registered: world={worldId}, profile={profileId}, role={role}, compatibility={compatibility}");
        }

        private void OnCharacterCreateRequired(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            string profileId = GetArg(args, 1);
            Logger.Info($"[LsrCoop.Client] character create required: world={worldId}, profile={profileId}");
        }

        private void OnCharacterSnapshot(CustomEventReceivedArgs args)
        {
            string worldId = GetArg(args, 0);
            string profileId = GetArg(args, 1);
            CoopCharacterSnapshot snapshot = Deserialize<CoopCharacterSnapshot>(GetArg(args, 2));
            ApplyAppearanceIfLocal(worldId, profileId, snapshot?.Appearance);
            Logger.Info($"[LsrCoop.Client] character snapshot received: world={worldId}, profile={profileId}");
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
    }
}
