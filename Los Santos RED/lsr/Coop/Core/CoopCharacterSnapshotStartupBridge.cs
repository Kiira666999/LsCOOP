using LosSantosRED.lsr.Helper;
using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LosSantosRED.lsr.Coop.Core
{
    public sealed class CoopCharacterStartupSnapshot
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string ModelName { get; set; }
        public CoopAppearanceState Appearance { get; set; }
    }

    public static class CoopCharacterSnapshotStartupBridge
    {
        private const string CharacterSnapshotBridgeFileName = "LsrCoopCharacterSnapshot.txt";
        private const string RequiredBridgeVersion = "1";
        private const string RequiredTransportMode = "RAGECOOP";
        private const int SafePedHealth = 200;

        public static CoopCharacterStartupSnapshot TryReadReadySnapshot()
        {
            if (!CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsCharacterReadyForSimulation)
            {
                return null;
            }

            string blockedReason;
            CoopStartupMode startupMode = CoopStartupBridge.GetStartupMode(out blockedReason);
            if (startupMode != CoopStartupMode.FullSimulation && startupMode != CoopStartupMode.ClientMode)
            {
                return null;
            }

            foreach (string path in GetSnapshotBridgePaths())
            {
                CoopCharacterStartupSnapshot snapshot = TryReadSnapshot(path);
                if (snapshot != null)
                {
                    EntryPoint.WriteToConsole($"Co-op character startup snapshot loaded World:{snapshot.WorldId} Profile:{snapshot.ProfileId} Model:{snapshot.ModelName}", 0);
                    return snapshot;
                }
            }

            EntryPoint.WriteToConsole("Co-op character startup snapshot not found; using current local ped model", 0);
            return null;
        }

        public static bool ApplyModelBeforeCreatePlayer(CoopCharacterStartupSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ModelName))
            {
                return false;
            }

            Ped ped = GetLocalPed();
            if (ped == null)
            {
                EntryPoint.WriteToConsole($"Co-op character startup model apply skipped; local ped missing Model:{snapshot.ModelName}", 0);
                return false;
            }

            string currentModel = ped.Model.Name;
            if (string.Equals(currentModel, snapshot.ModelName, StringComparison.OrdinalIgnoreCase))
            {
                EntryPoint.WriteToConsole($"Co-op character startup model already applied Model:{currentModel}", 0);
                return true;
            }

            EntryPoint.WriteToConsole($"Co-op character startup applying model Before:{currentModel} Saved:{snapshot.ModelName}", 0);
            try
            {
                SafeStatePed(ped, true);
                NativeHelper.ChangeModel(snapshot.ModelName);
                GameFiber.Yield();
                Ped changedPed = GetLocalPed();
                SafeStatePed(changedPed, false);
                EntryPoint.WriteToConsole($"Co-op character startup applying model After:{(changedPed == null ? "missing" : changedPed.Model.Name)} Saved:{snapshot.ModelName}", 0);
                return changedPed != null && string.Equals(changedPed.Model.Name, snapshot.ModelName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                SafeStatePed(GetLocalPed(), false);
                EntryPoint.WriteToConsole($"Co-op character startup model apply failed Model:{snapshot.ModelName} Error:{ex.Message}", 0);
                return false;
            }
        }

        public static bool ApplyAppearanceAfterPlayerSetup(CoopCharacterStartupSnapshot snapshot, Mod.Player player)
        {
            if (snapshot?.Appearance == null)
            {
                return false;
            }

            Ped ped = GetLocalPed();
            if (ped == null)
            {
                EntryPoint.WriteToConsole("Co-op character startup appearance apply skipped; local ped missing", 0);
                return false;
            }

            bool applied = new CoopAppearanceApplyService().TryApply(ped, snapshot.Appearance);
            if (applied && player != null)
            {
                player.ModelName = ped.Model.Name;
                player.CurrentModelVariation = NativeHelper.GetPedVariation(ped);
            }

            EntryPoint.WriteToConsole($"Co-op character startup appearance apply Result:{applied} Model:{ped.Model.Name}", 0);
            return applied;
        }

        private static CoopCharacterStartupSnapshot TryReadSnapshot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                Dictionary<string, string> values = ReadKeyValues(path);
                if (!string.Equals(GetValue(values, "BridgeVersion"), RequiredBridgeVersion, StringComparison.Ordinal)
                    || !string.Equals(GetValue(values, "TransportMode"), RequiredTransportMode, StringComparison.OrdinalIgnoreCase)
                    || !IsCurrentProcess(GetValue(values, "ProcessId")))
                {
                    return null;
                }

                string worldId = GetValue(values, "WorldId");
                string profileId = GetValue(values, "ProfileId");
                if (!string.Equals(worldId, CoopStartupBridge.WorldId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(profileId, CoopStartupBridge.LocalProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                string modelName = GetValue(values, "ModelName");
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    return null;
                }

                return new CoopCharacterStartupSnapshot
                {
                    WorldId = worldId,
                    ProfileId = profileId,
                    ModelName = modelName,
                    Appearance = new CoopAppearanceState
                    {
                        ModelName = modelName,
                        Components = ParseComponents(GetValue(values, "Components")),
                        Props = ParseProps(GetValue(values, "Props")),
                    },
                };
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op character startup snapshot read skipped Path:{path} Error:{ex.Message}", 0);
                return null;
            }
        }

        private static Dictionary<string, string> ReadKeyValues(string path)
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

        private static List<CoopPedComponentState> ParseComponents(string raw)
        {
            List<CoopPedComponentState> components = new List<CoopPedComponentState>();
            foreach (string entry in SplitEntries(raw))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                components.Add(new CoopPedComponentState
                {
                    ComponentId = ParseInt(parts[0]),
                    DrawableId = ParseInt(parts[1]),
                    TextureId = ParseInt(parts[2]),
                    PaletteId = ParseInt(parts[3]),
                });
            }

            return components;
        }

        private static List<CoopPedPropState> ParseProps(string raw)
        {
            List<CoopPedPropState> props = new List<CoopPedPropState>();
            foreach (string entry in SplitEntries(raw))
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 4)
                {
                    continue;
                }

                props.Add(new CoopPedPropState
                {
                    PropId = ParseInt(parts[0]),
                    DrawableId = ParseInt(parts[1]),
                    TextureId = ParseInt(parts[2]),
                    IsCleared = string.Equals(parts[3], "true", StringComparison.OrdinalIgnoreCase),
                });
            }

            return props;
        }

        private static IEnumerable<string> SplitEntries(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                yield break;
            }

            foreach (string entry in raw.Split(';'))
            {
                if (!string.IsNullOrWhiteSpace(entry))
                {
                    yield return entry;
                }
            }
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : 0;
        }

        private static string GetValue(Dictionary<string, string> values, string key)
        {
            return values != null && values.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        }

        private static bool IsCurrentProcess(string value)
        {
            int processId;
            return int.TryParse(value, out processId) && processId == Process.GetCurrentProcess().Id;
        }

        private static IEnumerable<string> GetSnapshotBridgePaths()
        {
            foreach (string folder in GetBridgeFolders())
            {
                yield return Path.Combine(folder, CharacterSnapshotBridgeFileName);
            }
        }

        private static string[] GetBridgeFolders()
        {
            return new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED"),
                Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "LosSantosRED"),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
            };
        }

        private static Ped GetLocalPed()
        {
            try
            {
                Ped ped = Game.LocalPlayer?.Character;
                if (ped != null && ped.Exists())
                {
                    return ped;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void SafeStatePed(Ped ped, bool freeze)
        {
            if (ped == null || !ped.Exists())
            {
                return;
            }

            NativeFunction.Natives.FREEZE_ENTITY_POSITION(ped, freeze);
            ped.IsInvincible = freeze;
            if (ped.MaxHealth < SafePedHealth)
            {
                ped.MaxHealth = SafePedHealth;
            }
            if (ped.Health < SafePedHealth)
            {
                ped.Health = SafePedHealth;
            }
            ped.IsCollisionEnabled = true;
        }
    }
}
