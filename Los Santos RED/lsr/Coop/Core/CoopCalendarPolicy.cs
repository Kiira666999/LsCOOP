using LosSantosRED.lsr.Interface;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LosSantosRED.lsr.Coop.Core
{
    public enum CoopProfileTimeSkipReason
    {
        Hospital,
        Jail,
    }

    public sealed class CoopCalendarSkipDecision
    {
        public bool CanApplyGlobalTimeSkip { get; set; }
        public bool IsCoopEnabled { get; set; }
        public bool IsCoopMultiClient { get; set; }
        public bool PolicyAllowsGlobalTimeSkip { get; set; }
        public int ConnectedCharacterReadyProfileCount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class CoopProfileTimeSkipResult
    {
        public bool Applied { get; set; }
        public bool Duplicate { get; set; }
        public string SkipId { get; set; } = string.Empty;
        public int ProcessedDueSystems { get; set; }
        public int DeferredDueSystems { get; set; }
        public string DeferredDueSystemNames { get; set; } = string.Empty;
    }

    public static class CoopCalendarPolicy
    {
        public static CoopCalendarSkipDecision EvaluateGlobalTimeSkip(CoopProfileTimeSkipReason reason, DateTime from, DateTime to)
        {
            CoopStartupBridge.RefreshRuntimeState();

            bool isCoopEnabled = CoopStartupBridge.IsCoopEnabled;
            int readyCount = CoopStartupBridge.ConnectedCharacterReadyProfileCount;
            bool policyAllowsGlobalSkip = CoopStartupBridge.AllowGlobalTimeSkip;
            bool isMultiClient = isCoopEnabled && (!policyAllowsGlobalSkip || readyCount > 1);
            bool canApplyGlobalSkip = !isCoopEnabled || !isMultiClient;

            CoopCalendarSkipDecision decision = new CoopCalendarSkipDecision
            {
                CanApplyGlobalTimeSkip = canApplyGlobalSkip,
                IsCoopEnabled = isCoopEnabled,
                IsCoopMultiClient = isMultiClient,
                PolicyAllowsGlobalTimeSkip = policyAllowsGlobalSkip,
                ConnectedCharacterReadyProfileCount = readyCount,
                Reason = BuildReason(isCoopEnabled, isMultiClient, policyAllowsGlobalSkip, readyCount),
            };

            EntryPoint.WriteToConsole($"Co-op calendar skip decision Reason:{reason} From:{from:O} To:{to:O} Coop:{decision.IsCoopEnabled} ReadyProfiles:{decision.ConnectedCharacterReadyProfileCount} PolicyAllowsGlobalSkip:{decision.PolicyAllowsGlobalTimeSkip} IsMultiClient:{decision.IsCoopMultiClient} GlobalSkipAllowed:{decision.CanApplyGlobalTimeSkip} Decision:{decision.Reason}", 5);
            return decision;
        }

        private static string BuildReason(bool isCoopEnabled, bool isMultiClient, bool policyAllowsGlobalSkip, int readyCount)
        {
            if (!isCoopEnabled)
            {
                return "single-player";
            }

            if (isMultiClient)
            {
                return policyAllowsGlobalSkip
                    ? "multiple character-ready profiles connected"
                    : "server policy disallows global time skips";
            }

            return $"co-op global skip allowed with {readyCount} character-ready profile(s)";
        }
    }

    public static class CoopProfileTimeSkipService
    {
        private static readonly HashSet<string> AppliedSkipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] DeferredDueSystems =
        {
            "GangLoans",
            "GangKickUpDues",
            "RentPropertyPayments",
            "BusinessPayouts",
            "OwnedVehicleImpoundDates",
            "LicenseValidity",
            "PlayerTaskCooldowns",
            "ScheduledTexts",
        };

        public static CoopProfileTimeSkipResult Apply(IRespawnable player, DateTime from, DateTime to, CoopProfileTimeSkipReason reason, CoopCalendarSkipDecision decision)
        {
            CoopProfileTimeSkipResult result = new CoopProfileTimeSkipResult
            {
                DeferredDueSystems = DeferredDueSystems.Length,
                DeferredDueSystemNames = string.Join(",", DeferredDueSystems),
            };

            string profileId = string.IsNullOrWhiteSpace(CoopStartupBridge.LocalProfileId)
                ? player?.PlayerName ?? string.Empty
                : CoopStartupBridge.LocalProfileId;
            string worldId = CoopStartupBridge.WorldId ?? string.Empty;
            result.SkipId = BuildSkipId(worldId, profileId, reason, from, to);

            if (to <= from)
            {
                EntryPoint.WriteToConsole($"Co-op profile time skip ignored Reason:{reason} Profile:{profileId} World:{worldId} From:{from:O} To:{to:O} DurationDays:{(to - from).TotalDays.ToString("0.###", CultureInfo.InvariantCulture)} Result:InvalidInterval", 5);
                return result;
            }

            if (!AppliedSkipIds.Add(result.SkipId))
            {
                result.Duplicate = true;
                EntryPoint.WriteToConsole($"Co-op profile time skip duplicate ignored Reason:{reason} Profile:{profileId} World:{worldId} SkipId:{result.SkipId}", 5);
                return result;
            }

            result.Applied = true;

            string outcomeSystem = reason == CoopProfileTimeSkipReason.Hospital
                ? "HospitalDebt"
                : "BailDebt";
            EntryPoint.WriteToConsole($"Co-op profile time skip applied Reason:{reason} Profile:{profileId} World:{worldId} From:{from:O} To:{to:O} DurationDays:{(to - from).TotalDays.ToString("0.###", CultureInfo.InvariantCulture)} GlobalTimeBefore:{from:O} GlobalSkipAllowed:{decision?.CanApplyGlobalTimeSkip == true} ProcessedDueSystems:{result.ProcessedDueSystems} DeferredDueSystems:{result.DeferredDueSystems} Deferred:{result.DeferredDueSystemNames} OutcomeSystemHandledByRespawn:{outcomeSystem}", 5);
            CoopPersistenceDiagnostics.WriteVerbose($"Co-op profile time skip v1 Profile:{profileId} Reason:{reason} From:{from:O} To:{to:O} ProcessedDueSystems:{result.ProcessedDueSystems} DeferredDueSystems:{result.DeferredDueSystemNames}; hospital/bail money/debt remains handled by existing respawn outcome code");
            return result;
        }

        private static string BuildSkipId(string worldId, string profileId, CoopProfileTimeSkipReason reason, DateTime from, DateTime to)
        {
            return string.Join("|", new[]
            {
                worldId ?? string.Empty,
                profileId ?? string.Empty,
                reason.ToString(),
                from.ToString("O", CultureInfo.InvariantCulture),
                to.ToString("O", CultureInfo.InvariantCulture),
            });
        }
    }
}
