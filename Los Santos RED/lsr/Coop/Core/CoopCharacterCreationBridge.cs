using System;
using System.IO;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopCharacterCreationBridge
    {
        private const string CharacterCreatedFileName = "LsrCoopCharacterCreated.txt";

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
                $"WorldId={Escape(CoopStartupBridge.WorldId ?? string.Empty)}",
                $"ProfileId={Escape(profileId)}",
                $"CharacterId={Escape(string.IsNullOrWhiteSpace(profileId) ? player.PlayerName : profileId)}",
                $"PlayerName={Escape(player.PlayerName ?? profileId)}",
                $"ModelName={Escape(player.ModelName ?? string.Empty)}",
                $"TimestampUtc={Escape(DateTime.UtcNow.ToString("O"))}",
                $"Nonce={Escape(nonce)}",
            };

            foreach (string folder in GetBridgeFolders())
            {
                WriteAtomic(folder, lines, nonce);
            }
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
                string targetPath = Path.Combine(folder, CharacterCreatedFileName);
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
            catch
            {
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

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }
    }
}
