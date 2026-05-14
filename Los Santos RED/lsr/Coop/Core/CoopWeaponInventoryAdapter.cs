using Rage;
using Rage.Native;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopWeaponHydrationResult
    {
        public bool Applied { get; set; }
        public int HydratedCount { get; set; }
        public int ExistingCount { get; set; }
        public int SkippedDuplicateCount { get; set; }
        public int SkippedInvalidCount { get; set; }
    }

    public class CoopWeaponInventoryAdapter
    {
        public CoopWeaponSnapshot CaptureFromPlayer(Mod.Player player, CoopProfileId profileId, CoopCharacterId characterId, CoopWorldId worldId)
        {
            CoopWeaponSnapshot snapshot = new CoopWeaponSnapshot
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
            };

            if (player?.Character?.Exists() != true)
            {
                return snapshot;
            }

            WeaponHash equippedHash = player.Character.Inventory.EquippedWeapon?.Hash ?? 0;
            foreach (WeaponDescriptor weapon in player.Character.Inventory.Weapons)
            {
                WeaponInformation weaponInfo = player.WeaponEquipment?.CurrentWeapon?.Hash == (uint)weapon.Hash
                    ? player.WeaponEquipment.CurrentWeapon
                    : null;

                snapshot.Weapons.Add(new CoopWeaponRecord
                {
                    WeaponHash = ((uint)weapon.Hash).ToString(),
                    WeaponName = weaponInfo?.ModelName ?? weapon.Hash.ToString(),
                    Category = weaponInfo?.Category.ToString() ?? string.Empty,
                    Ammo = weapon.Ammo,
                    IsLegal = weaponInfo?.IsLegal == true || weaponInfo?.IsLegalWithoutCCW == true,
                    IsEquipped = weapon.Hash == equippedHash,
                });
            }

            return snapshot;
        }

        public CoopWeaponHydrationResult TryApplySnapshotToPlayer(Mod.Player player, CoopWeaponSnapshot snapshot)
        {
            CoopWeaponHydrationResult result = new CoopWeaponHydrationResult();
            if (player?.Character?.Exists() != true || snapshot == null)
            {
                return result;
            }

            Dictionary<uint, CoopWeaponRecord> recordsByHash = new Dictionary<uint, CoopWeaponRecord>();
            foreach (CoopWeaponRecord record in snapshot.Weapons ?? Enumerable.Empty<CoopWeaponRecord>())
            {
                if (!TryParseWeaponHash(record?.WeaponHash, out uint weaponHash) || weaponHash == 0)
                {
                    result.SkippedInvalidCount++;
                    continue;
                }

                if (recordsByHash.ContainsKey(weaponHash))
                {
                    result.SkippedDuplicateCount++;
                    continue;
                }

                recordsByHash[weaponHash] = record;
            }

            uint equippedHash = 0;
            foreach (KeyValuePair<uint, CoopWeaponRecord> entry in recordsByHash)
            {
                uint weaponHash = entry.Key;
                CoopWeaponRecord record = entry.Value;
                int ammo = Math.Max(0, record.Ammo);

                bool alreadyHadWeapon = NativeFunction.Natives.HAS_PED_GOT_WEAPON<bool>(player.Character, weaponHash, false);
                if (!alreadyHadWeapon)
                {
                    NativeFunction.Natives.GIVE_WEAPON_TO_PED(player.Character, weaponHash, ammo, false, false);
                    result.HydratedCount++;
                }
                else
                {
                    result.ExistingCount++;
                }

                if (NativeFunction.Natives.HAS_PED_GOT_WEAPON<bool>(player.Character, weaponHash, false))
                {
                    NativeFunction.CallByName<bool>("SET_PED_AMMO", player.Character, weaponHash, ammo);
                }

                if (record.IsEquipped)
                {
                    equippedHash = weaponHash;
                }
            }

            if (equippedHash != 0 && NativeFunction.Natives.HAS_PED_GOT_WEAPON<bool>(player.Character, equippedHash, false))
            {
                NativeFunction.CallByName<bool>("SET_CURRENT_PED_WEAPON", player.Character, equippedHash, true);
            }
            else
            {
                player.WeaponEquipment?.SetUnarmed();
            }

            player.WeaponEquipment?.Update();
            result.Applied = result.HydratedCount > 0 || result.ExistingCount > 0 || result.SkippedDuplicateCount > 0;
            return result;
        }

        private bool TryParseWeaponHash(string value, out uint weaponHash)
        {
            if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out weaponHash))
            {
                return true;
            }

            if (Enum.TryParse(value, true, out WeaponHash parsedHash))
            {
                weaponHash = (uint)parsedHash;
                return true;
            }

            weaponHash = 0;
            return false;
        }
    }
}
