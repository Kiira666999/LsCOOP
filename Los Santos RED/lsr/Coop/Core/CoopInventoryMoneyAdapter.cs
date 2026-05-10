using LosSantosRED.lsr.Interface;
using LosSantosRED.lsr.Player;
using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopInventoryMoneyAdapter
    {
        public CoopInventoryMoneySnapshot GetSnapshot(LsrActorContext actorContext, Mod.Player fallbackPlayer, CoopServerWorldSave serverWorldSave = null)
        {
            CoopProfileId profileId = actorContext != null ? actorContext.ProfileId : new CoopProfileId();

            if (CoopStartupBridge.IsCoopEnabled && serverWorldSave != null && TryGetSnapshot(serverWorldSave, profileId, out CoopInventoryMoneySnapshot serverSnapshot))
            {
                return serverSnapshot;
            }

            Mod.Player player = actorContext?.ExistingPlayer ?? fallbackPlayer;
            return CaptureFromPlayer(player, profileId, actorContext != null ? actorContext.CharacterId : new CoopCharacterId(), serverWorldSave?.WorldId ?? new CoopWorldId());
        }

        public CoopInventoryMoneySnapshot CaptureFromPlayer(Mod.Player player, CoopProfileId profileId, CoopCharacterId characterId, CoopWorldId worldId)
        {
            CoopInventoryMoneySnapshot snapshot = new CoopInventoryMoneySnapshot
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
            };

            if (player == null)
            {
                return snapshot;
            }

            if (player.BankAccounts != null)
            {
                snapshot.OnHandCash = player.BankAccounts.GetMoney(false);
                snapshot.TotalAccountMoney = player.BankAccounts.TotalAccountMoney;

                foreach (BankAccount account in player.BankAccounts.BankAccountList ?? new List<BankAccount>())
                {
                    snapshot.BankAccounts.Add(new CoopBankAccountState
                    {
                        BankContactName = account.BankContactName,
                        AccountName = account.AccountName,
                        Money = account.Money,
                        IsPrimary = account.IsPrimary,
                    });
                }
            }

            if (player.Inventory?.ItemsList != null)
            {
                foreach (InventoryItem item in player.Inventory.ItemsList.Where(x => x?.ModItem != null))
                {
                    snapshot.InventoryItems.Add(new CoopInventoryItemState
                    {
                        ItemName = item.ModItem.Name,
                        RemainingPercent = item.RemainingPercent,
                    });
                }
            }

            return snapshot;
        }

        public bool TryGetSnapshot(CoopServerWorldSave worldSave, CoopProfileId profileId, out CoopInventoryMoneySnapshot snapshot)
        {
            snapshot = null;
            CoopPersistentPlayerState state = GetPersistentState(worldSave, profileId);
            if (state == null)
            {
                return false;
            }

            snapshot = CreateSnapshotFromPersistentState(state, worldSave?.WorldId ?? state.WorldId);
            return true;
        }

        public bool TrySaveSnapshot(CoopServerWorldSave worldSave, CoopInventoryMoneySnapshot snapshot)
        {
            CoopPersistentPlayerState state = GetPersistentState(worldSave, snapshot?.ProfileId ?? new CoopProfileId());
            if (state == null || snapshot == null)
            {
                return false;
            }

            ApplySnapshotToPersistentState(state, snapshot);
            return true;
        }

        public bool TryApplySnapshotToPlayer(Mod.Player player, CoopInventoryMoneySnapshot snapshot, IModItems modItems)
        {
            if (player == null || snapshot == null)
            {
                return false;
            }

            if (player.BankAccounts != null)
            {
                player.BankAccounts.SetCash(snapshot.OnHandCash);
                player.BankAccounts.BankAccountList = snapshot.BankAccounts.Select(x => new BankAccount(x.BankContactName, x.AccountName, x.Money)
                {
                    IsPrimary = x.IsPrimary
                }).ToList();
            }

            if (player.Inventory != null && modItems != null)
            {
                player.Inventory.Clear();
                foreach (CoopInventoryItemState item in snapshot.InventoryItems)
                {
                    ModItem modItem = modItems.Get(item.ItemName);
                    if (modItem != null)
                    {
                        player.Inventory.Add(modItem, item.RemainingPercent);
                    }
                }
            }

            return true;
        }

        public int GetMoney(LsrActorContext actorContext, Mod.Player fallbackPlayer, CoopServerWorldSave serverWorldSave, bool includeAccounts)
        {
            CoopInventoryMoneySnapshot snapshot = GetSnapshot(actorContext, fallbackPlayer, serverWorldSave);
            return includeAccounts ? snapshot.TotalMoney : snapshot.OnHandCash;
        }

        public bool SetMoney(CoopServerWorldSave worldSave, CoopProfileId profileId, int onHandCash)
        {
            CoopPersistentPlayerState state = GetPersistentState(worldSave, profileId);
            if (state == null)
            {
                return false;
            }

            state.Money = onHandCash;
            state.OnHandCash = onHandCash;
            return true;
        }

        private CoopPersistentPlayerState GetPersistentState(CoopServerWorldSave worldSave, CoopProfileId profileId)
        {
            if (worldSave?.WorldState?.Profiles == null || profileId.IsEmpty)
            {
                return null;
            }

            CoopServerPlayerProfile profile = worldSave.WorldState.Profiles.FirstOrDefault(x => x.ProfileId.Equals(profileId));
            return profile?.PersistentState;
        }

        private CoopInventoryMoneySnapshot CreateSnapshotFromPersistentState(CoopPersistentPlayerState state, CoopWorldId worldId)
        {
            CoopInventoryMoneySnapshot snapshot = new CoopInventoryMoneySnapshot
            {
                WorldId = worldId,
                ProfileId = state.ProfileId,
                CharacterId = state.CharacterId,
                OnHandCash = state.OnHandCash,
            };

            if (state.OnHandCash == 0 && state.Money != 0)
            {
                snapshot.OnHandCash = state.Money;
            }

            foreach (CoopBankAccountState account in state.BankAccounts)
            {
                snapshot.BankAccounts.Add(new CoopBankAccountState
                {
                    BankContactName = account.BankContactName,
                    AccountName = account.AccountName,
                    Money = account.Money,
                    IsPrimary = account.IsPrimary,
                });
            }

            snapshot.TotalAccountMoney = snapshot.BankAccounts.Sum(x => x.Money);

            foreach (CoopInventoryItemState item in state.InventoryItems)
            {
                snapshot.InventoryItems.Add(new CoopInventoryItemState
                {
                    ItemName = item.ItemName,
                    RemainingPercent = item.RemainingPercent,
                });
            }

            return snapshot;
        }

        private void ApplySnapshotToPersistentState(CoopPersistentPlayerState state, CoopInventoryMoneySnapshot snapshot)
        {
            state.WorldId = snapshot.WorldId;
            state.ProfileId = snapshot.ProfileId;
            state.CharacterId = snapshot.CharacterId;
            state.Money = snapshot.OnHandCash;
            state.OnHandCash = snapshot.OnHandCash;

            state.BankAccounts.Clear();
            foreach (CoopBankAccountState account in snapshot.BankAccounts)
            {
                state.BankAccounts.Add(new CoopBankAccountState
                {
                    BankContactName = account.BankContactName,
                    AccountName = account.AccountName,
                    Money = account.Money,
                    IsPrimary = account.IsPrimary,
                });
            }

            state.InventoryItems.Clear();
            foreach (CoopInventoryItemState item in snapshot.InventoryItems)
            {
                state.InventoryItems.Add(new CoopInventoryItemState
                {
                    ItemName = item.ItemName,
                    RemainingPercent = item.RemainingPercent,
                });
            }
        }
    }
}
