using System;
using System.Diagnostics;
using System.IO;
using LsrCoop.Shared;
using Rage;
using Rage.Native;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopCharacterCreationBridge
    {
        private const int StablePedTimeoutMilliseconds = 3000;
        private const int StablePedTickMilliseconds = 100;
        private const int RequiredStableMilliseconds = 1200;
        private const int SafePedHealth = 200;

        public static bool ShouldGuardBootstrapOnlyCharacterCreation(bool isNewPlayerCreation)
        {
            if (!isNewPlayerCreation || !CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsCharacterCreationRequired)
            {
                return false;
            }

            string blockedReason;
            return CoopStartupBridge.GetStartupMode(out blockedReason) == CoopStartupMode.BootstrapOnly;
        }

        public static void BeginBootstrapOnlyModelChangeGuard(string modelName)
        {
            EntryPoint.WriteToConsole($"Co-op BootstrapOnly character creation model guard start Model:{modelName}", 0);
            SafeStateLocalPed(true, "start", modelName);
        }

        public static bool WaitForStableBootstrapOnlyPed(string modelName)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            int stableMilliseconds = 0;

            while (timeout.ElapsedMilliseconds <= StablePedTimeoutMilliseconds)
            {
                Ped ped = GetLocalPed();
                if (ped != null)
                {
                    SafeStatePed(ped, true);
                    if (IsStablePed(ped))
                    {
                        stableMilliseconds += StablePedTickMilliseconds;
                        if (stableMilliseconds >= RequiredStableMilliseconds)
                        {
                            LogPedState("stable", modelName, ped);
                            return true;
                        }
                    }
                    else
                    {
                        stableMilliseconds = 0;
                    }
                }
                else
                {
                    stableMilliseconds = 0;
                }

                GameFiber.Wait(StablePedTickMilliseconds);
            }

            Ped finalPed = GetLocalPed();
            LogPedState("failed", modelName, finalPed);
            EntryPoint.WriteToConsole($"Co-op BootstrapOnly character creation model guard failed Model:{modelName}; character-created bridge event suppressed", 0);
            return false;
        }

        public static void EndBootstrapOnlyModelChangeGuard(string modelName, bool releaseSafeState)
        {
            Ped ped = GetLocalPed();
            if (ped != null)
            {
                SafeStatePed(ped, !releaseSafeState);
            }

            LogPedState("end", modelName, ped);
        }

        public static void NotifyCharacterCreated(Mod.Player player)
        {
            if (!CoopStartupBridge.IsCoopEnabled || player == null)
            {
                return;
            }

            string blockedReason;
            CoopStartupMode startupMode = CoopStartupBridge.GetStartupMode(out blockedReason);
            if (startupMode != CoopStartupMode.BootstrapOnly && !CoopStartupBridge.IsCharacterCreationRequired)
            {
                return;
            }

            string profileId = CoopStartupBridge.LocalProfileId ?? string.Empty;
            string nonce = Guid.NewGuid().ToString("N");
            string[] lines =
            {
                "BridgeVersion=1",
                "TransportMode=RAGECOOP",
                "Direction=LSR_TO_RAGECOOP",
                $"ProcessId={Process.GetCurrentProcess().Id}",
                $"SessionId={Escape(CoopStartupBridge.BridgeSessionId)}",
                $"WorldId={Escape(CoopStartupBridge.WorldId ?? string.Empty)}",
                $"ProfileId={Escape(profileId)}",
                $"CharacterId={Escape(string.IsNullOrWhiteSpace(profileId) ? player.PlayerName : profileId)}",
                $"PlayerName={Escape(player.PlayerName ?? profileId)}",
                $"ModelName={Escape(player.ModelName ?? string.Empty)}",
                $"TimestampUtc={Escape(DateTime.UtcNow.ToString("O"))}",
                $"Nonce={Escape(nonce)}",
            };

            WriteAtomic(CoopBridgePaths.CharacterFolder, lines, nonce);
        }

        private static void WriteAtomic(string folder, string[] lines, string nonce)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                string targetPath = Path.Combine(folder, CoopBridgePaths.CharacterCreatedFileName);
                string tempPath = targetPath + "." + nonce + ".tmp";
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    foreach (string line in lines ?? new string[0])
                    {
                        writer.WriteLine(line);
                    }

                    writer.Flush();
                    stream.Flush();
                }

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
            catch
            {
            }
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static bool SafeStateLocalPed(bool freeze, string phase, string modelName)
        {
            Ped ped = GetLocalPed();
            if (ped == null)
            {
                EntryPoint.WriteToConsole($"Co-op BootstrapOnly character creation model guard {phase} Model:{modelName} LocalPed:missing", 0);
                return false;
            }

            SafeStatePed(ped, freeze);
            LogPedState(phase, modelName, ped);
            return IsStablePed(ped);
        }

        private static void SafeStatePed(Ped ped, bool freeze)
        {
            if (ped == null || !ped.Exists())
            {
                return;
            }

            try
            {
                NativeFunction.Natives.FREEZE_ENTITY_POSITION(ped, freeze);
                ped.IsInvincible = freeze;
                if (ped.MaxHealth < SafePedHealth)
                {
                    ped.MaxHealth = SafePedHealth;
                }
                if (ped.IsDead || ped.Health < SafePedHealth)
                {
                    NativeFunction.Natives.RESURRECT_PED(ped);
                    NativeFunction.Natives.REVIVE_INJURED_PED(ped);
                    NativeFunction.Natives.CLEAR_PED_TASKS_IMMEDIATELY(ped);
                }
                if (ped.Health < SafePedHealth)
                {
                    ped.Health = SafePedHealth;
                }
            }
            catch (Exception ex)
            {
                EntryPoint.WriteToConsole($"Co-op BootstrapOnly character creation model guard safe-state failed {ex.Message}", 0);
            }
        }

        private static bool IsStablePed(Ped ped)
        {
            return ped != null && ped.Exists() && ped.Handle != 0 && !ped.IsDead && ped.Health >= SafePedHealth;
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

        private static void LogPedState(string phase, string modelName, Ped ped)
        {
            if (ped == null || !ped.Exists())
            {
                EntryPoint.WriteToConsole($"Co-op BootstrapOnly character creation model guard {phase} Model:{modelName} LocalPed:missing", 0);
                return;
            }

            EntryPoint.WriteToConsole($"Co-op BootstrapOnly character creation model guard {phase} Model:{modelName} Handle:{ped.Handle} Health:{ped.Health} IsDead:{ped.IsDead}", 0);
        }
    }
}
