using System;
using System.Diagnostics;
using System.IO;
using LsrCoop.Shared;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopStartupBridge
    {
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
        public static string LocalRole { get; private set; } = string.Empty;
        public static string BridgeSessionId { get; private set; } = string.Empty;
        public static bool IsLocalAdmin => HasRole(LocalRole, "Admin");

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
            LocalRole = string.Empty;
            BridgeSessionId = string.Empty;
            CoopRuntimeServices.ResetToDisabled();
        }

        public static void SetBridgeSessionId(string sessionId)
        {
            BridgeSessionId = sessionId ?? string.Empty;
        }

        public static void SetLocalRole(string localRole)
        {
            LocalRole = localRole ?? string.Empty;
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

            if (IsCharacterCreationRequired || (!IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId)))
            {
                blockedReason = "LSR co-op character creation is required";
                StartupMode = CoopStartupMode.BootstrapOnly;
                return StartupMode;
            }

            if (IsLocalActiveHost && IsCharacterReadyForSimulation)
            {
                StartupMode = CoopStartupMode.FullSimulation;
                return StartupMode;
            }

            if (IsCharacterReadyForSimulation)
            {
                StartupMode = CoopStartupMode.ClientMode;
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
                    DeleteRejectedStateFile(statePath, "old process");
                    SetDisabled();
                    return;
                }

                bool validHeader = string.Equals(GetValue(lines, "BridgeVersion"), RequiredBridgeVersion, StringComparison.Ordinal)
                    && string.Equals(GetValue(lines, "TransportMode"), RequiredTransportMode, StringComparison.OrdinalIgnoreCase);
                if (!validHeader)
                {
                    DeleteRejectedStateFile(statePath, "invalid header");
                    SetDisabled();
                    return;
                }

                bool configured = IsTrue(GetValue(lines, "CoopModeEnabled"));
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
                LocalRole = GetValue(lines, "LocalRole") ?? string.Empty;
                BridgeSessionId = GetValue(lines, "SessionId") ?? string.Empty;
                IsLocalActiveHost = HasActiveHostAssigned && !string.IsNullOrWhiteSpace(LocalProfileId) && string.Equals(LocalProfileId, ActiveHostProfileId, StringComparison.OrdinalIgnoreCase);
                if (!IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId))
                {
                    IsCharacterCreationRequired = true;
                }
                RefreshStartupMode();
            }
            catch
            {
                DeleteRejectedStateFile(statePath, "malformed");
            }
        }

        private static void RefreshStartupMode()
        {
            if (!IsCoopEnabled)
            {
                StartupMode = CoopStartupMode.Disabled;
            }
            else if (IsCharacterCreationRequired || (!IsCharacterReadyForSimulation && !string.IsNullOrWhiteSpace(LocalProfileId)))
            {
                StartupMode = CoopStartupMode.BootstrapOnly;
            }
            else if (IsLocalActiveHost && IsCharacterReadyForSimulation)
            {
                StartupMode = CoopStartupMode.FullSimulation;
            }
            else if (IsCharacterReadyForSimulation)
            {
                StartupMode = CoopStartupMode.ClientMode;
            }
            else
            {
                StartupMode = CoopStartupMode.Blocked;
            }
        }

        private static string FindStateFilePath()
        {
            return CoopBridgePaths.StartupStatePath;
        }

        private static void DeleteRejectedStateFile(string path, string reason)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                    EntryPoint.WriteToConsole($"Co-op startup bridge file deleted Reason:{reason} File:{Path.GetFileName(path)}", 0);
                }
            }
            catch
            {
            }
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

        private static bool HasRole(string roles, string role)
        {
            if (string.IsNullOrWhiteSpace(roles) || string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            string[] splitRoles = roles.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string splitRole in splitRoles)
            {
                if (string.Equals(splitRole.Trim(), role, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
