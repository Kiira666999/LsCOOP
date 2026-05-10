using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopCharacterCreationBridge
    {
        private static Action<object> characterCreatedSink;

        public static void RegisterCharacterCreatedSink(Action<object> sink)
        {
            characterCreatedSink = sink;
        }

        public static void UnregisterCharacterCreatedSink()
        {
            characterCreatedSink = null;
        }

        public static void NotifyCharacterCreated(Mod.Player player)
        {
            if (!CoopStartupBridge.IsCoopEnabled || player == null)
            {
                return;
            }

            string profileId = CoopStartupBridge.LocalProfileId ?? string.Empty;
            characterCreatedSink?.Invoke(new CoopCharacterCreationCompleted
            {
                WorldId = CoopStartupBridge.WorldId ?? string.Empty,
                ProfileId = profileId,
                CharacterId = string.IsNullOrWhiteSpace(profileId) ? player.PlayerName : profileId,
                PlayerName = player.PlayerName ?? profileId,
                ModelName = player.ModelName ?? string.Empty,
            });
        }
    }
}
