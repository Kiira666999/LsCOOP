using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LsrCoop.Server
{
    internal class RoleConfigService
    {
        private const string RoleConfigFileName = "LsrCoop.RoleConfig.json";

        private readonly string dataFolder;
        private readonly Action<string> info;
        private readonly Action<string> warning;
        private string roleConfigPath;

        public RoleConfigService(string dataFolder, Action<string> info, Action<string> warning)
        {
            this.dataFolder = dataFolder;
            this.info = info;
            this.warning = warning;
            Config = new LsrCoopRoleConfig();
        }

        public LsrCoopRoleConfig Config { get; private set; }
        public string WorldId => Config.WorldId;

        public void Load()
        {
            roleConfigPath = Path.Combine(dataFolder, RoleConfigFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(roleConfigPath));

            if (!File.Exists(roleConfigPath))
            {
                Config = new LsrCoopRoleConfig();
                Save();
                info?.Invoke($"[LsrCoop.Server] created role config: {roleConfigPath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(roleConfigPath);
                Config = JsonSerializer.Deserialize<LsrCoopRoleConfig>(json) ?? new LsrCoopRoleConfig();
                Config.AdminIds = Config.AdminIds ?? new List<string>();
                Config.TrustedHostIds = Config.TrustedHostIds ?? new List<string>();
                info?.Invoke($"[LsrCoop.Server] role config loaded: world={Config.WorldId}, admins={Config.AdminIds.Count}, trustedHosts={Config.TrustedHostIds.Count}");
            }
            catch (Exception ex)
            {
                warning?.Invoke($"[LsrCoop.Server] failed to load role config, trying backup: {ex.Message}");
                if (!TryLoadBackup())
                {
                    warning?.Invoke("[LsrCoop.Server] role config backup unavailable, using defaults");
                    Config = new LsrCoopRoleConfig();
                }
            }
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
            SafeJsonFileStore.WriteAllText(roleConfigPath, json);
        }

        public CoopWorldRolesSnapshot CreateSnapshot()
        {
            return new CoopWorldRolesSnapshot
            {
                ConfigVersion = Config.ConfigVersion,
                WorldId = Config.WorldId,
                AdminIds = Config.AdminIds?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                TrustedHostIds = Config.TrustedHostIds?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                UpdatedUtc = DateTimeOffset.UtcNow,
            };
        }

        public void ApplyWorldRolesIfLocalConfigEmpty(CoopWorldRolesSnapshot roles)
        {
            if (roles == null || ((roles.AdminIds == null || roles.AdminIds.Count == 0) && (roles.TrustedHostIds == null || roles.TrustedHostIds.Count == 0)))
            {
                return;
            }

            if ((Config.AdminIds?.Count ?? 0) != 0 || (Config.TrustedHostIds?.Count ?? 0) != 0)
            {
                return;
            }

            Config.WorldId = string.IsNullOrWhiteSpace(roles.WorldId) ? Config.WorldId : roles.WorldId;
            Config.AdminIds = roles.AdminIds?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            Config.TrustedHostIds = roles.TrustedHostIds?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            Save();
            info?.Invoke($"[LsrCoop.Server] role config restored from world save: admins={Config.AdminIds.Count}, trustedHosts={Config.TrustedHostIds.Count}");
        }

        private bool TryLoadBackup()
        {
            string backupPath = SafeJsonFileStore.BackupPath(roleConfigPath);
            if (!File.Exists(backupPath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(backupPath);
                Config = JsonSerializer.Deserialize<LsrCoopRoleConfig>(json) ?? new LsrCoopRoleConfig();
                Config.AdminIds = Config.AdminIds ?? new List<string>();
                Config.TrustedHostIds = Config.TrustedHostIds ?? new List<string>();
                info?.Invoke($"[LsrCoop.Server] role config backup loaded: world={Config.WorldId}, admins={Config.AdminIds.Count}, trustedHosts={Config.TrustedHostIds.Count}");
                return true;
            }
            catch (Exception backupEx)
            {
                warning?.Invoke($"[LsrCoop.Server] failed to load role config backup: {backupEx.Message}");
                return false;
            }
        }

        public bool IsTrustedHost(string profileId)
        {
            return Config.TrustedHostIds.Any(x => string.Equals(x, profileId, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsAdmin(string profileId)
        {
            return Config.AdminIds.Any(x => string.Equals(x, profileId, StringComparison.OrdinalIgnoreCase));
        }

        public string GetRoleName(string profileId)
        {
            bool isAdmin = IsAdmin(profileId);
            bool isTrustedHost = IsTrustedHost(profileId);

            if (isAdmin && isTrustedHost)
            {
                return "Admin,TrustedHost";
            }

            if (isAdmin)
            {
                return LsrCoopRole.Admin.ToString();
            }

            return isTrustedHost ? LsrCoopRole.TrustedHost.ToString() : LsrCoopRole.Player.ToString();
        }
    }
}
