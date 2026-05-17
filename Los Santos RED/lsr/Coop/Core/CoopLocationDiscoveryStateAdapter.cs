using LosSantosRED.lsr.Interface;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopLocationDiscoveryStateAdapter
    {
        public static CoopLocationDiscoveryStateAdapter Current { get; } = new CoopLocationDiscoveryStateAdapter();

        public void NotifyLocationDiscovered(global::GameLocation location)
        {
            if (!CoopStartupBridge.IsCoopEnabled || location == null || string.IsNullOrWhiteSpace(location.LocationID))
            {
                return;
            }

            CoopProfileId profileId = GetCurrentProfileId();
            CoopLocationDiscoveryState state = new CoopLocationDiscoveryState
            {
                WorldId = GetCurrentWorldId(),
                ProfileId = profileId,
                CharacterId = GetCurrentCharacterId(profileId),
                DiscoveredLocationIds = new List<string> { location.LocationID.Trim() },
                UpdatedUtc = DateTimeOffset.UtcNow,
            };

            CoopGameplayFileBridge.PublishLocationDiscoveryState(state);
        }

        public bool TryApplyPersistentStateToLocations(CoopLocationDiscoveryState state, IPlacesOfInterest placesOfInterest, ISettingsProvideable settings)
        {
            if (state?.DiscoveredLocationIds == null || placesOfInterest == null || !ShouldApplyLocationDiscovery(settings))
            {
                return false;
            }

            HashSet<string> discoveredIds = new HashSet<string>(
                state.DiscoveredLocationIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (!discoveredIds.Any())
            {
                return false;
            }

            bool applied = false;
            foreach (global::GameLocation location in placesOfInterest.AllLocations().Where(x => x != null && x.IsDiscoverable && !x.IsBlipEnabled && !string.IsNullOrWhiteSpace(x.LocationID)))
            {
                if (!discoveredIds.Contains(location.LocationID.Trim()))
                {
                    continue;
                }

                if (!location.IsDiscovered)
                {
                    location.IsDiscovered = true;
                    applied = true;
                }
            }

            CoopPersistenceDiagnostics.WriteVerbose($"Co-op location discovery hydrated Profile:{state.ProfileId} IDs:{discoveredIds.Count} Applied:{applied}");
            return applied;
        }

        private static bool ShouldApplyLocationDiscovery(ISettingsProvideable settings)
        {
            return settings?.SettingsManager?.WorldSettings?.EnableLocationDiscovery == true
                && settings.SettingsManager.WorldSettings.PersistDiscoveredLocations;
        }

        private static CoopWorldId GetCurrentWorldId()
        {
            return new CoopWorldId(string.IsNullOrWhiteSpace(CoopStartupBridge.WorldId) ? "single-player-world" : CoopStartupBridge.WorldId);
        }

        private static CoopProfileId GetCurrentProfileId()
        {
            return new CoopProfileId(string.IsNullOrWhiteSpace(CoopStartupBridge.LocalProfileId) ? "single-player-profile" : CoopStartupBridge.LocalProfileId);
        }

        private static CoopCharacterId GetCurrentCharacterId(CoopProfileId profileId)
        {
            string profile = profileId.ToString();
            return new CoopCharacterId(string.IsNullOrWhiteSpace(profile) || profile == "single-player-profile" ? "single-player-character" : profile);
        }
    }
}
