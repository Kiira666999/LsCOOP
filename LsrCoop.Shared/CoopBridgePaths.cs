using System;
using System.Collections.Generic;
using System.IO;

namespace LsrCoop.Shared
{
    public static class CoopBridgePaths
    {
        public const string BridgeRootPath = @"O:\SteamLibrary\steamapps\common\Grand Theft Auto V\scripts\RageCoop\Data\LsrCoopBridge";
        public const string StartupStateFileName = "LsrCoopStartupState.txt";
        public const string CharacterCreatedFileName = "LsrCoopCharacterCreated.txt";
        public const string CharacterSnapshotFileName = "LsrCoopCharacterSnapshot.txt";
        public const string GameplayOutboundFilePrefix = "LsrCoopGameplayOut.";
        public const string GameplayInboundFilePrefix = "LsrCoopGameplayIn.";
        public const string BridgeFileExtension = ".txt";
        public const string BridgeTempSearchPattern = "LsrCoop*.tmp";
        public const string GameplayOutboundSearchPattern = GameplayOutboundFilePrefix + "*" + BridgeFileExtension;
        public const string GameplayInboundSearchPattern = GameplayInboundFilePrefix + "*" + BridgeFileExtension;

        private const string GtaRootPath = @"O:\SteamLibrary\steamapps\common\Grand Theft Auto V";

        public static string StartupFolder => Path.Combine(BridgeRootPath, "Startup");
        public static string CharacterFolder => Path.Combine(BridgeRootPath, "Character");
        public static string GameplayOutboundFolder => Path.Combine(BridgeRootPath, "GameplayOut");
        public static string GameplayInboundFolder => Path.Combine(BridgeRootPath, "GameplayIn");

        public static string StartupStatePath => Path.Combine(StartupFolder, StartupStateFileName);
        public static string CharacterCreatedPath => Path.Combine(CharacterFolder, CharacterCreatedFileName);
        public static string CharacterSnapshotPath => Path.Combine(CharacterFolder, CharacterSnapshotFileName);

        public static string GameplayOutboundPath(string nonce)
        {
            return Path.Combine(GameplayOutboundFolder, GameplayOutboundFilePrefix + (nonce ?? string.Empty) + BridgeFileExtension);
        }

        public static string GameplayInboundPath(string nonce)
        {
            return Path.Combine(GameplayInboundFolder, GameplayInboundFilePrefix + (nonce ?? string.Empty) + BridgeFileExtension);
        }

        public static IEnumerable<string> GetCurrentBridgeFolders()
        {
            yield return StartupFolder;
            yield return CharacterFolder;
            yield return GameplayOutboundFolder;
            yield return GameplayInboundFolder;
        }

        public static IEnumerable<string> GetLegacyBridgeFolders()
        {
            foreach (string folder in GetDistinctFolders(new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "LosSantosRED"),
                Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "LosSantosRED"),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.Combine(GtaRootPath, "Plugins", "LosSantosRED"),
                Path.Combine(GtaRootPath, "scripts"),
                GtaRootPath,
            }))
            {
                if (!IsCurrentBridgeFolder(folder))
                {
                    yield return folder;
                }
            }
        }

        private static IEnumerable<string> GetDistinctFolders(IEnumerable<string> folders)
        {
            HashSet<string> distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(folder))
                {
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(folder);
                }
                catch
                {
                    fullPath = folder;
                }

                if (distinct.Add(fullPath))
                {
                    yield return fullPath;
                }
            }
        }

        private static bool IsCurrentBridgeFolder(string folder)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(folder);
            }
            catch
            {
                fullPath = folder;
            }

            foreach (string currentFolder in GetCurrentBridgeFolders())
            {
                string currentFullPath;
                try
                {
                    currentFullPath = Path.GetFullPath(currentFolder);
                }
                catch
                {
                    currentFullPath = currentFolder;
                }

                if (string.Equals(fullPath, currentFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
