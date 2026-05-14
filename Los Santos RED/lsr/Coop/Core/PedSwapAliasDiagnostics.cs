using LosSantosRED.lsr.Interface;
using System.Globalization;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class PedSwapAliasDiagnostics
    {
        public static PedSwapAliasDiagnosticSnapshot LastPedSwapSnapshot { get; private set; }

        public static bool IsVerboseEnabled(ISettingsProvideable settings)
        {
            return CoopStartupBridge.IsCoopEnabled
                && settings != null
                && settings.SettingsManager != null
                && settings.SettingsManager.DebugSettings != null
                && settings.SettingsManager.DebugSettings.LogCoopAliasDiagnostics;
        }

        public static void RecordPedSwapState(string stage, ISettingsProvideable settings, string playerModelName, bool characterModelIsPrimaryCharacter, bool isAliasOffsetActive, string aliasModelName, bool hasAddOffsetRun, bool hasSetPlayerOffsetRun, string currentModelPlayerIs)
        {
            if (!CoopStartupBridge.IsCoopEnabled)
            {
                return;
            }

            PedSwapAliasDiagnosticSnapshot snapshot = new PedSwapAliasDiagnosticSnapshot
            {
                Stage = stage,
                CurrentPlayerModel = playerModelName,
                CharacterModelIsPrimaryCharacter = characterModelIsPrimaryCharacter,
                IsAliasOffsetActive = isAliasOffsetActive,
                AliasModelName = aliasModelName,
                HasAddOffsetRun = hasAddOffsetRun,
                HasSetPlayerOffsetRun = hasSetPlayerOffsetRun,
                CurrentModelPlayerIs = currentModelPlayerIs
            };

            LastPedSwapSnapshot = snapshot;

            if (IsVerboseEnabled(settings))
            {
                EntryPoint.WriteToConsole("Co-op alias diag pedswap "
                    + "Stage:" + stage
                    + " Model:" + playerModelName
                    + " Primary:" + characterModelIsPrimaryCharacter.ToString(CultureInfo.InvariantCulture)
                    + " OffsetActive:" + isAliasOffsetActive.ToString(CultureInfo.InvariantCulture)
                    + " AliasModel:" + aliasModelName
                    + " AddOffsetRun:" + hasAddOffsetRun.ToString(CultureInfo.InvariantCulture)
                    + " SetOffsetRun:" + hasSetPlayerOffsetRun.ToString(CultureInfo.InvariantCulture)
                    + " CurrentModelPlayerIs:" + currentModelPlayerIs, 0);
            }
        }
    }

    public sealed class PedSwapAliasDiagnosticSnapshot
    {
        public string Stage { get; set; }
        public string CurrentPlayerModel { get; set; }
        public bool CharacterModelIsPrimaryCharacter { get; set; }
        public bool IsAliasOffsetActive { get; set; }
        public string AliasModelName { get; set; }
        public bool HasAddOffsetRun { get; set; }
        public bool HasSetPlayerOffsetRun { get; set; }
        public string CurrentModelPlayerIs { get; set; }
    }
}
