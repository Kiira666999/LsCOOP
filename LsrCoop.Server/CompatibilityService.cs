using System;

namespace LsrCoop.Server
{
    internal class CompatibilityService
    {
        public const string RequiredCoopBuildVersion = "0.1.0";
        public const string RequiredLsrVersion = "1.0.0.513";
        public const string RequiredConfigVersion = "1";

        public void ApplyReport(CoopClientStatus status, string coopBuildVersion, string lsrVersion, string configVersion, bool requiredResourceLoaded)
        {
            status.CoopBuildVersion = coopBuildVersion;
            status.LsrVersion = lsrVersion;
            status.ConfigVersion = configVersion;
            status.RequiredResourceLoaded = requiredResourceLoaded;
            status.CompatibilityState = Evaluate(status);
        }

        public CoopClientCompatibilityState Evaluate(CoopClientStatus status)
        {
            if (!status.RequiredResourceLoaded)
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            if (!string.Equals(status.CoopBuildVersion, RequiredCoopBuildVersion, StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            if (string.IsNullOrWhiteSpace(status.LsrVersion) || string.Equals(status.LsrVersion, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Unknown;
            }

            if (!string.Equals(status.LsrVersion, RequiredLsrVersion, StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            if (!string.Equals(status.ConfigVersion, RequiredConfigVersion, StringComparison.OrdinalIgnoreCase))
            {
                return CoopClientCompatibilityState.Incompatible;
            }

            return CoopClientCompatibilityState.Compatible;
        }

        public bool IsCompatible(CoopClientStatus status)
        {
            return status != null && status.CompatibilityState == CoopClientCompatibilityState.Compatible;
        }
    }
}
