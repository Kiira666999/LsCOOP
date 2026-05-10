using System;
using System.Diagnostics;
using System.IO;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopStartupBridge
    {
        private const string StateFileName = "LsrCoopStartupState.txt";
        private const string RequiredBridgeVersion = "2";
        private const string RequiredTransportMode = "RAGECOOP";

        public static bool IsCoopEnabled { get; private set; }
        public static bool IsCoopModeConfigured { get; private set; }
        public static bool HasActiveHostAssigned { get; private set; }
        public static bool IsLocalActiveHost { get; private set; }
        public static bool IsCharacterReadyForSimulation { get; private set; }
        public static bool IsCharacterCreationRequired { get; private set; }
        public static CoopStartupMode StartupMode { get; private set; } = CoopStartupMode.Disabled;
        public static string WorldId { get; private set; } = string.Empty;
        public static string LocalProfileId { get; private set; } = string.Empty;
        public static string ActiveHostProfileId { get; private set; } = string.Empty;

        public static void SetDisabled()
        {
            IsCoopEnabled = false;
            IsCoopModeConfigured = false;
            HasActiveHostAssigned = false;
            IsLocalActiveHost = false;
            IsCharacterReadyForSimulation = false;
            IsCharacterCreationRequired = false;
            StartupMode = CoopStartupMode.Disabled;
            WorldId = string.Empty;
            LocalProfileId = string.Empty;
            ActiveHostProfileId = string.Empty;
            CoopRuntimeServices.ResetToDisabled();
        }

        public static void SetSession(string worldId, string localProfileId)
        {
            SetSession(worldId, localProfileId, false);
        }

        public static void SetSession(string worldId, string localProfileId, bool isCharacterReadyForSimulation)
        {
            IsCoopModeConfigured = true;
            IsCoopEnabled = true;
            WorldId = worldId ?? string.Empty;
            LocalProfileId = localProfileId ?? string.Empty;
            IsCharacterReadyForSimulation = isCharacterReadyForSimulation;
            IsCharacterCreationRequired = !IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId);
            IsLocalActiveHost = HasActiveHostAssigned && !string.IsNullOrWhiteSpace(LocalProfileId) && string.Equals(LocalProfileId, ActiveHostProfileId, System.StringComparison.OrdinalIgnoreCase);
            RefreshStartupMode();
        }

        public static void SetActiveHost(string worldId, string activeHostProfileId, string localProfileId)
        {
            SetActiveHost(worldId, activeHostProfileId, localProfileId, false);
        }

        public static void SetActiveHost(string worldId, string activeHostProfileId, string localProfileId, bool isCharacterReadyForSimulation)
        {
            IsCoopModeConfigured = true;
            IsCoopEnabled = true;
            HasActiveHostAssigned = true;
            IsCharacterReadyForSimulation = isCharacterReadyForSimulation;
            WorldId = worldId ?? string.Empty;
            ActiveHostProfileId = activeHostProfileId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(localProfileId))
            {
                LocalProfileId = localProfileId;
            }
            IsLocalActiveHost = !string.IsNullOrWhiteSpace(LocalProfileId) && string.Equals(LocalProfileId, ActiveHostProfileId, System.StringComparison.OrdinalIgnoreCase);
            IsCharacterCreationRequired = !IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId);
            RefreshStartupMode();
        }

        public static void ClearActiveHost(string worldId)
        {
            IsCoopModeConfigured = true;
            IsCoopEnabled = true;
            HasActiveHostAssigned = false;
            IsLocalActiveHost = false;
            WorldId = worldId ?? WorldId;
            ActiveHostProfileId = string.Empty;
            RefreshStartupMode();
        }

        public static bool CanStartFullSimulation(out string blockedReason)
        {
            CoopStartupMode mode = GetStartupMode(out blockedReason);
            return mode == CoopStartupMode.Disabled || mode == CoopStartupMode.FullSimulation;
        }

        public static CoopStartupMode GetStartupMode(out string blockedReason)
        {
            RefreshFromStateFile();
            blockedReason = string.Empty;

            if (!IsCoopEnabled)
            {
                StartupMode = CoopStartupMode.Disabled;
                return StartupMode;
            }

            if (IsLocalActiveHost && IsCharacterReadyForSimulation)
            {
                StartupMode = CoopStartupMode.FullSimulation;
                return StartupMode;
            }

            if (IsCharacterCreationRequired || (!IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId)))
            {
                blockedReason = "LSR co-op character creation is required";
                StartupMode = CoopStartupMode.BootstrapOnly;
                return StartupMode;
            }

            blockedReason = "LSR co-op is waiting for active host";
            StartupMode = CoopStartupMode.Blocked;
            return StartupMode;
        }

        private static void RefreshFromStateFile()
        {
            string statePath = FindStateFilePath();
            if (string.IsNullOrWhiteSpace(statePath) || !File.Exists(statePath))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(statePath);
                string processId = GetValue(lines, "ProcessId");
                int parsedProcessId;
                if (!int.TryParse(processId, out parsedProcessId) || parsedProcessId != Process.GetCurrentProcess().Id)
                {
                    SetDisabled();
                    return;
                }

                bool configured = IsTrue(GetValue(lines, "CoopModeEnabled"))
                    && string.Equals(GetValue(lines, "BridgeVersion"), RequiredBridgeVersion, StringComparison.Ordinal)
                    && string.Equals(GetValue(lines, "TransportMode"), RequiredTransportMode, StringComparison.OrdinalIgnoreCase);
                bool enabled = configured && IsTrue(GetValue(lines, "IsCoopEnabled"));
                if (!enabled)
                {
                    SetDisabled();
                    return;
                }

                IsCoopModeConfigured = true;
                IsCoopEnabled = true;
                HasActiveHostAssigned = IsTrue(GetValue(lines, "HasActiveHostAssigned"));
                IsCharacterReadyForSimulation = IsTrue(GetValue(lines, "CharacterReadyForSimulation"));
                IsCharacterCreationRequired = IsTrue(GetValue(lines, "CharacterCreationRequired"));
                WorldId = GetValue(lines, "WorldId") ?? string.Empty;
                LocalProfileId = GetValue(lines, "LocalProfileId") ?? string.Empty;
                ActiveHostProfileId = GetValue(lines, "ActiveHostProfileId") ?? string.Empty;
                IsLocalActiveHost = HasActiveHostAssigned && !string.IsNullOrWhiteSpace(LocalProfileId) && string.Equals(LocalProfileId, ActiveHostProfileId, StringComparison.OrdinalIgnoreCase);
                if (!IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId))
                {
                    IsCharacterCreationRequired = true;
                }
                RefreshStartupMode();
            }
            catch
            {
            }
        }

        private static void RefreshStartupMode()
        {
            if (!IsCoopEnabled)
            {
                StartupMode = CoopStartupMode.Disabled;
            }
            else if (IsLocalActiveHost && IsCharacterReadyForSimulation)
            {
                StartupMode = CoopStartupMode.FullSimulation;
            }
            else if (IsCharacterCreationRequired || (!IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId)))
            {
                StartupMode = CoopStartupMode.BootstrapOnly;
            }
            else
            {
                StartupMode = CoopStartupMode.Blocked;
            }
        }

        private static string FindStateFilePath()
        {
            foreach (string folder in GetStateFolders())
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                string path = Path.Combine(folder, StateFileName);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return Path.Combine(GetPrimaryStateFolder(), StateFileName);
        }

        private static string[] GetStateFolders()
        {
            return new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED"),
                Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "LosSantosRED"),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };
        }

        private static string GetPrimaryStateFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED");
        }

        private static string GetValue(string[] lines, string key)
        {
            string prefix = key + "=";
            foreach (string line in lines)
            {
                if (line != null && line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line.Substring(prefix.Length);
                }
            }

            return string.Empty;
        }

        private static bool IsTrue(string value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
