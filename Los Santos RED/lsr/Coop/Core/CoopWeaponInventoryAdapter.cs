using Rage;
using System;

namespace LosSantosRED.lsr.Coop.Core
{
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
    }
}
