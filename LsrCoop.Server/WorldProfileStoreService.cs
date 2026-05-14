using RageCoop.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LsrCoop.Server
{
    internal class WorldProfileStoreService
    {
        private const string WorldProfileStoreFileName = "LsrCoop.WorldProfiles.json";

        private readonly string dataFolder;
        private readonly RoleConfigService roleConfigService;
        private readonly Action<string> info;
        private readonly Action<string> warning;
        private string worldProfileStorePath;

        public WorldProfileStoreService(string dataFolder, RoleConfigService roleConfigService, Action<string> info, Action<string> warning)
        {
            this.dataFolder = dataFolder;
            this.roleConfigService = roleConfigService;
            this.info = info;
            this.warning = warning;
            Store = new CoopWorldProfileStore();
        }

        public CoopWorldProfileStore Store { get; private set; }
        public string WorldId => Store.WorldId;
        public List<CoopPlayerProfile> Profiles => Store.Profiles;

        public void Load()
        {
            worldProfileStorePath = Path.Combine(dataFolder, WorldProfileStoreFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(worldProfileStorePath));

            if (!File.Exists(worldProfileStorePath))
            {
                Store = new CoopWorldProfileStore { WorldId = roleConfigService.WorldId, Roles = roleConfigService.CreateSnapshot() };
                Save();
                info?.Invoke($"[LsrCoop.Server] created world profile store: {worldProfileStorePath}");
                return;
            }

            if (!TryLoadStore(worldProfileStorePath, out CoopWorldProfileStore loadedStore))
            {
                string backupPath = SafeJsonFileStore.BackupPath(worldProfileStorePath);
                warning?.Invoke("[LsrCoop.Server] failed to load world profiles, trying backup");
                TryLoadStore(backupPath, out loadedStore);
            }

            Store = loadedStore ?? new CoopWorldProfileStore { WorldId = roleConfigService.WorldId };
            roleConfigService.ApplyWorldRolesIfLocalConfigEmpty(Store.Roles);
            NormalizeStore();

            if (!string.Equals(Store.WorldId, roleConfigService.WorldId, StringComparison.OrdinalIgnoreCase))
            {
                warning?.Invoke($"[LsrCoop.Server] world profile store id {Store.WorldId} does not match role config world id {roleConfigService.WorldId}");
            }

            info?.Invoke($"[LsrCoop.Server] world profiles loaded: world={Store.WorldId}, profiles={Store.Profiles.Count}");
        }

        public void Save()
        {
            NormalizeStore();
            string json = JsonSerializer.Serialize(Store, new JsonSerializerOptions { WriteIndented = true });
            SafeJsonFileStore.WriteAllText(worldProfileStorePath, json);
        }

        public CoopPlayerProfile GetProfile(string profileId)
        {
            return Store.Profiles.FirstOrDefault(x => string.Equals(x.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        }

        public CoopPlayerProfile LoadOrCreateProfile(CoopClientStatus status, Client client, string clientId, string role)
        {
            CoopPlayerProfile profile = GetProfile(status.ProfileId);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool created = false;

            if (profile == null)
            {
                profile = new CoopPlayerProfile
                {
                    ProfileId = status.ProfileId,
                    DisplayName = GetClientName(client),
                    CreatedUtc = now
                };
                Store.Profiles.Add(profile);
                created = true;
            }

            profile.ClientId = clientId;
            profile.DisplayName = GetClientName(client);
            profile.Role = role;
            profile.LastSeenUtc = now;
            Save();

            if (created)
            {
                info?.Invoke($"[LsrCoop.Server] created co-op profile: world={Store.WorldId}, profile={profile.ProfileId}, role={profile.Role}");
            }

            return profile;
        }

        private string GetClientName(Client client)
        {
            return string.IsNullOrWhiteSpace(client?.Username) ? "unknown" : client.Username;
        }

        private bool TryLoadStore(string path, out CoopWorldProfileStore store)
        {
            store = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                store = JsonSerializer.Deserialize<CoopWorldProfileStore>(json);
                return store != null;
            }
            catch (Exception ex)
            {
                warning?.Invoke($"[LsrCoop.Server] failed reading world profile store {path}: {ex.Message}");
                return false;
            }
        }

        private void NormalizeStore()
        {
            Store = Store ?? new CoopWorldProfileStore();
            Store.StoreVersion = string.IsNullOrWhiteSpace(Store.StoreVersion) ? "1" : Store.StoreVersion;
            Store.WorldId = string.IsNullOrWhiteSpace(Store.WorldId) ? roleConfigService.WorldId : Store.WorldId;
            Store.Profiles = Store.Profiles ?? new List<CoopPlayerProfile>();
            Store.WorldFlags = Store.WorldFlags ?? new List<string>();
            Store.LongTermLsrRecords = Store.LongTermLsrRecords ?? new List<string>();
            Store.CreatedUtc = Store.CreatedUtc == default ? DateTimeOffset.UtcNow : Store.CreatedUtc;
            Store.UpdatedUtc = DateTimeOffset.UtcNow;
            Store.Roles = roleConfigService.CreateSnapshot();

            foreach (CoopPlayerProfile profile in Store.Profiles.Where(x => x != null))
            {
                NormalizeProfile(profile);
            }
        }

        private void NormalizeProfile(CoopPlayerProfile profile)
        {
            profile.ProfileId = profile.ProfileId ?? string.Empty;
            profile.Role = roleConfigService.GetRoleName(profile.ProfileId);
            profile.DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.ProfileId : profile.DisplayName;
            profile.CreatedUtc = profile.CreatedUtc == default ? DateTimeOffset.UtcNow : profile.CreatedUtc;
            profile.LastSeenUtc = profile.LastSeenUtc == default ? profile.CreatedUtc : profile.LastSeenUtc;
            profile.LongTermLsrRecords = profile.LongTermLsrRecords ?? new List<string>();
            NormalizeCharacter(profile);
            NormalizeInventoryMoney(profile);
            NormalizeWeapons(profile);
            NormalizeOwnedVehicles(profile);
            NormalizeProperties(profile);
            NormalizeGangReputation(profile);
            NormalizeCriminalHistory(profile);
            NormalizeDeathArrest(profile);
        }

        private void NormalizeCharacter(CoopPlayerProfile profile)
        {
            if (profile.Character == null)
            {
                return;
            }

            profile.Character.ProfileId = string.IsNullOrWhiteSpace(profile.Character.ProfileId) ? profile.ProfileId : profile.Character.ProfileId;
            profile.Character.CharacterId = string.IsNullOrWhiteSpace(profile.Character.CharacterId) ? profile.ProfileId : profile.Character.CharacterId;
            profile.Character.DisplayName = string.IsNullOrWhiteSpace(profile.Character.DisplayName) ? profile.DisplayName : profile.Character.DisplayName;
        }

        private void NormalizeInventoryMoney(CoopPlayerProfile profile)
        {
            if (profile.InventoryMoney == null)
            {
                return;
            }

            profile.InventoryMoney.WorldId = Store.WorldId;
            profile.InventoryMoney.ProfileId = profile.ProfileId;
            profile.InventoryMoney.CharacterId = string.IsNullOrWhiteSpace(profile.InventoryMoney.CharacterId) ? profile.ProfileId : profile.InventoryMoney.CharacterId;
            profile.InventoryMoney.InventoryItems = profile.InventoryMoney.InventoryItems ?? new List<CoopInventoryItemState>();
            profile.InventoryMoney.BankAccounts = profile.InventoryMoney.BankAccounts ?? new List<CoopBankAccountState>();
        }

        private void NormalizeWeapons(CoopPlayerProfile profile)
        {
            if (profile.Weapons == null)
            {
                return;
            }

            profile.Weapons.WorldId = Store.WorldId;
            profile.Weapons.ProfileId = profile.ProfileId;
            profile.Weapons.CharacterId = string.IsNullOrWhiteSpace(profile.Weapons.CharacterId) ? profile.ProfileId : profile.Weapons.CharacterId;
            profile.Weapons.Weapons = profile.Weapons.Weapons ?? new List<CoopWeaponRecord>();
        }

        private void NormalizeOwnedVehicles(CoopPlayerProfile profile)
        {
            if (profile.OwnedVehicles == null)
            {
                return;
            }

            profile.OwnedVehicles.WorldId = Store.WorldId;
            profile.OwnedVehicles.ProfileId = profile.ProfileId;
            profile.OwnedVehicles.CharacterId = string.IsNullOrWhiteSpace(profile.OwnedVehicles.CharacterId) ? profile.ProfileId : profile.OwnedVehicles.CharacterId;
        }

        private void NormalizeProperties(CoopPlayerProfile profile)
        {
            if (profile.PropertyOwnership == null)
            {
                return;
            }

            profile.PropertyOwnership.WorldId = Store.WorldId;
            profile.PropertyOwnership.ProfileId = profile.ProfileId;
            profile.PropertyOwnership.CharacterId = string.IsNullOrWhiteSpace(profile.PropertyOwnership.CharacterId) ? profile.ProfileId : profile.PropertyOwnership.CharacterId;
        }

        private void NormalizeGangReputation(CoopPlayerProfile profile)
        {
            if (profile.GangReputation == null)
            {
                return;
            }

            profile.GangReputation.WorldId = Store.WorldId;
            profile.GangReputation.ProfileId = profile.ProfileId;
            profile.GangReputation.CharacterId = string.IsNullOrWhiteSpace(profile.GangReputation.CharacterId) ? profile.ProfileId : profile.GangReputation.CharacterId;
            profile.GangReputation.Reputations = profile.GangReputation.Reputations ?? new List<CoopGangReputationRecordDto>();
        }

        private void NormalizeCriminalHistory(CoopPlayerProfile profile)
        {
            if (profile.CriminalHistory == null)
            {
                return;
            }

            profile.CriminalHistory.WorldId = Store.WorldId;
            profile.CriminalHistory.ProfileId = profile.ProfileId;
            profile.CriminalHistory.CharacterId = string.IsNullOrWhiteSpace(profile.CriminalHistory.CharacterId) ? profile.ProfileId : profile.CriminalHistory.CharacterId;
            profile.CriminalHistory.Crimes = profile.CriminalHistory.Crimes ?? new List<CoopCriminalHistoryCrimeRecordDto>();
        }

        private void NormalizeDeathArrest(CoopPlayerProfile profile)
        {
            if (profile.DeathArrestState == null)
            {
                return;
            }

            profile.DeathArrestState.WorldId = Store.WorldId;
            profile.DeathArrestState.ProfileId = profile.ProfileId;
            profile.DeathArrestState.CharacterId = string.IsNullOrWhiteSpace(profile.DeathArrestState.CharacterId) ? profile.ProfileId : profile.DeathArrestState.CharacterId;
        }
    }
}
