using LosSantosRED.lsr.Interface;
using LSR.Vehicles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopStorePurchaseBridge
    {
        private static readonly CoopActionAuthorityService AuthorityService = new CoopActionAuthorityService();
        private static readonly CoopPendingActionTracker PendingActions = new CoopPendingActionTracker();
        private static readonly CoopInventoryMoneyAdapter InventoryMoneyAdapter = new CoopInventoryMoneyAdapter();
        private static readonly CoopWeaponInventoryAdapter WeaponInventoryAdapter = new CoopWeaponInventoryAdapter();
        private static readonly CoopPropertyOwnershipAdapter PropertyOwnershipAdapter = new CoopPropertyOwnershipAdapter();
        private static readonly CoopOwnedVehicleAdapter OwnedVehicleAdapter = new CoopOwnedVehicleAdapter();
        private static readonly Dictionary<string, PendingPurchaseState> PendingPurchaseStates = new Dictionary<string, PendingPurchaseState>();

        public static bool TryBeginPurchaseItem(Transaction transaction, ILocationInteractable player, MenuItem menuItem, ModItem modItem, int quantity, int totalPrice, bool isStealing, out CoopGameplayActionRequest request, out string blockedReason)
        {
            return TryBeginPurchase(CoopGameplayActionType.PurchaseItem, transaction, player, menuItem, modItem, quantity, totalPrice, isStealing, out request, out blockedReason);
        }

        public static bool TryBeginPurchaseVehicle(Transaction transaction, ILocationInteractable player, MenuItem menuItem, ModItem modItem, int quantity, int totalPrice, bool isStealing, out CoopGameplayActionRequest request, out string blockedReason)
        {
            return TryBeginPurchase(CoopGameplayActionType.PurchaseVehicle, transaction, player, menuItem, modItem, quantity, totalPrice, isStealing, out request, out blockedReason);
        }

        public static void CompletePurchase(CoopGameplayActionRequest request, ILocationInteractable player)
        {
            if (!CoopStartupBridge.IsCoopEnabled || request == null)
            {
                return;
            }

            CoopGameplayActionResult result = AuthorityService.CreateAcceptedResult(request, "Accepted by active host");

            Mod.Player modPlayer = player as Mod.Player;
            CoopInventoryMoneySnapshot snapshot = InventoryMoneyAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId);
            CoopOwnedVehicleSnapshot ownedVehicleSnapshot = request.ActionType == CoopGameplayActionType.PurchaseVehicle
                ? OwnedVehicleAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId)
                : null;
            CoopGameplayFileBridge.PublishGameplayCommit(new CoopStorePurchaseCommit
            {
                Request = request,
                Result = result,
                InventoryMoneySnapshot = snapshot,
                WeaponSnapshot = WeaponInventoryAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId),
                OwnedVehicleSnapshot = ownedVehicleSnapshot,
            });
        }

        public static bool TryBeginPurchaseProperty(GameLocation property, ILocationInteractable player, int amount, string propertyAction, out CoopGameplayActionRequest request, out string blockedReason)
        {
            request = null;
            blockedReason = string.Empty;

            if (!CoopStartupBridge.IsCoopEnabled)
            {
                return true;
            }

            string profileId = CoopStartupBridge.LocalProfileId;
            request = new CoopGameplayActionRequest
            {
                ActionType = CoopGameplayActionType.PurchaseProperty,
                WorldId = new CoopWorldId(CoopStartupBridge.WorldId),
                SourceProfileId = new CoopProfileId(profileId),
                SourceCharacterId = new CoopCharacterId(profileId),
                TargetProfileId = new CoopProfileId(profileId),
                TargetCharacterId = new CoopCharacterId(profileId),
                AllowsOptimisticClientFeedback = AuthorityService.CanUseOptimisticClientFeedback(CoopGameplayActionType.PurchaseProperty),
            };

            request.Parameters["PropertyName"] = property?.Name ?? string.Empty;
            request.Parameters["PropertyType"] = property?.GetType().Name ?? string.Empty;
            request.Parameters["PropertyAction"] = propertyAction ?? string.Empty;
            request.Parameters["Amount"] = amount.ToString();
            request.Parameters["ActorName"] = (player as Mod.Player)?.PlayerName ?? string.Empty;

            PendingActions.Track(request, request.AllowsOptimisticClientFeedback);
            TrackPendingState(request, player as ILocationInteractable, null);

            if (!CoopStartupBridge.IsLocalActiveHost)
            {
                blockedReason = "LSR co-op is waiting for active host";
                return false;
            }

            return true;
        }

        public static void CompletePropertyOwnershipChange(CoopGameplayActionRequest request, ILocationInteractable player)
        {
            if (!CoopStartupBridge.IsCoopEnabled || request == null)
            {
                return;
            }

            Mod.Player modPlayer = player as Mod.Player;
            CoopGameplayActionResult result = AuthorityService.CreateAcceptedResult(request, "Accepted by active host");
            CoopInventoryMoneySnapshot moneySnapshot = InventoryMoneyAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId);
            CoopPropertyOwnershipSnapshot propertySnapshot = PropertyOwnershipAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId);

            CoopGameplayFileBridge.PublishGameplayCommit(new CoopStorePurchaseCommit
            {
                Request = request,
                Result = result,
                InventoryMoneySnapshot = moneySnapshot,
                WeaponSnapshot = WeaponInventoryAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId),
                PropertyOwnershipSnapshot = propertySnapshot,
            });
        }

        public static bool TryBeginSaveOwnedVehicle(IVehicleOwnable player, VehicleExt vehicle, string vehicleAction, out CoopGameplayActionRequest request, out string blockedReason)
        {
            request = null;
            blockedReason = string.Empty;

            if (!CoopStartupBridge.IsCoopEnabled)
            {
                return true;
            }

            string profileId = CoopStartupBridge.LocalProfileId;
            request = new CoopGameplayActionRequest
            {
                ActionType = CoopGameplayActionType.SaveOwnedVehicle,
                WorldId = new CoopWorldId(CoopStartupBridge.WorldId),
                SourceProfileId = new CoopProfileId(profileId),
                SourceCharacterId = new CoopCharacterId(profileId),
                TargetProfileId = new CoopProfileId(profileId),
                TargetCharacterId = new CoopCharacterId(profileId),
                AllowsOptimisticClientFeedback = AuthorityService.CanUseOptimisticClientFeedback(CoopGameplayActionType.SaveOwnedVehicle),
            };

            request.Parameters["VehicleAction"] = vehicleAction ?? string.Empty;
            request.Parameters["VehicleHandle"] = vehicle?.Handle.ToString() ?? string.Empty;
            request.Parameters["VehicleModelHash"] = vehicle?.Vehicle.Exists() == true ? vehicle.Vehicle.Model.Hash.ToString() : string.Empty;
            request.Parameters["ActorName"] = player?.PlayerName ?? string.Empty;

            PendingActions.Track(request, request.AllowsOptimisticClientFeedback);

            if (!CoopStartupBridge.IsLocalActiveHost)
            {
                blockedReason = "LSR co-op is waiting for active host";
                return false;
            }

            return true;
        }

        public static void CompleteOwnedVehicleChange(CoopGameplayActionRequest request, IVehicleOwnable player)
        {
            if (!CoopStartupBridge.IsCoopEnabled || request == null)
            {
                return;
            }

            Mod.Player modPlayer = player as Mod.Player;
            CoopGameplayActionResult result = AuthorityService.CreateAcceptedResult(request, "Accepted by active host");
            CoopOwnedVehicleSnapshot ownedVehicleSnapshot = OwnedVehicleAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId);

            CoopGameplayFileBridge.PublishGameplayCommit(new CoopStorePurchaseCommit
            {
                Request = request,
                Result = result,
                WeaponSnapshot = WeaponInventoryAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId),
                OwnedVehicleSnapshot = ownedVehicleSnapshot,
            });
        }

        public static void HandlePurchaseResult(string requestId, bool accepted, bool requiresResync, string reason)
        {
            if (string.IsNullOrWhiteSpace(requestId) || !PendingActions.TryGet(requestId, out CoopGameplayActionRequest request))
            {
                return;
            }

            CoopGameplayActionResult result = accepted
                ? AuthorityService.CreateAcceptedResult(request, reason)
                : AuthorityService.CreateRejectedResult(request, reason);
            result.RequiresResync = requiresResync;
            PendingActions.Resolve(result, out _);

            if ((!result.Accepted || result.RequiresResync) && PendingPurchaseStates.TryGetValue(requestId, out PendingPurchaseState pendingState))
            {
                ApplySnapshotToPlayer(pendingState.Player, pendingState.BeforeSnapshot, pendingState.ItemsByName);
            }

            PendingPurchaseStates.Remove(requestId);
        }

        private static bool TryBeginPurchase(CoopGameplayActionType actionType, Transaction transaction, ILocationInteractable player, MenuItem menuItem, ModItem modItem, int quantity, int totalPrice, bool isStealing, out CoopGameplayActionRequest request, out string blockedReason)
        {
            request = null;
            blockedReason = string.Empty;

            if (!CoopStartupBridge.IsCoopEnabled)
            {
                return true;
            }

            request = CreatePurchaseRequest(actionType, transaction, player, menuItem, modItem, quantity, totalPrice, isStealing);
            PendingActions.Track(request, AuthorityService.CanUseOptimisticClientFeedback(actionType));
            TrackPendingState(request, player, modItem);

            if (!CoopStartupBridge.IsLocalActiveHost)
            {
                blockedReason = "LSR co-op is waiting for active host";
                return false;
            }

            return true;
        }

        private static CoopGameplayActionRequest CreatePurchaseRequest(CoopGameplayActionType actionType, Transaction transaction, ILocationInteractable player, MenuItem menuItem, ModItem modItem, int quantity, int totalPrice, bool isStealing)
        {
            string profileId = CoopStartupBridge.LocalProfileId;
            CoopGameplayActionRequest request = new CoopGameplayActionRequest
            {
                ActionType = actionType,
                WorldId = new CoopWorldId(CoopStartupBridge.WorldId),
                SourceProfileId = new CoopProfileId(profileId),
                SourceCharacterId = new CoopCharacterId(profileId),
                TargetProfileId = new CoopProfileId(profileId),
                TargetCharacterId = new CoopCharacterId(profileId),
                AllowsOptimisticClientFeedback = AuthorityService.CanUseOptimisticClientFeedback(actionType),
            };

            request.Parameters["StoreName"] = transaction?.Store?.Name ?? transaction?.StoreName ?? string.Empty;
            request.Parameters["ShopMenuId"] = transaction?.ShopMenu?.ID ?? string.Empty;
            request.Parameters["ItemName"] = modItem?.Name ?? menuItem?.ModItemName ?? string.Empty;
            request.Parameters["MenuItemName"] = menuItem?.ModItemName ?? string.Empty;
            request.Parameters["Quantity"] = quantity.ToString();
            request.Parameters["UnitPrice"] = menuItem?.PurchasePrice.ToString() ?? "0";
            request.Parameters["TotalPrice"] = totalPrice.ToString();
            request.Parameters["UseAccounts"] = (transaction?.UseAccounts ?? true).ToString();
            request.Parameters["IsStealing"] = isStealing.ToString();
            request.Parameters["ActorName"] = player?.PlayerName ?? string.Empty;

            return request;
        }

        private static void TrackPendingState(CoopGameplayActionRequest request, ILocationInteractable player, ModItem modItem)
        {
            Mod.Player modPlayer = player as Mod.Player;
            if (request == null || modPlayer == null)
            {
                return;
            }

            Dictionary<string, ModItem> itemsByName = new Dictionary<string, ModItem>();
            if (modItem != null && !string.IsNullOrWhiteSpace(modItem.Name))
            {
                itemsByName[modItem.Name] = modItem;
            }

            foreach (InventoryItem item in modPlayer.Inventory?.ItemsList?.Where(x => x?.ModItem != null) ?? Enumerable.Empty<InventoryItem>())
            {
                itemsByName[item.ModItem.Name] = item.ModItem;
            }

            PendingPurchaseStates[request.RequestId] = new PendingPurchaseState
            {
                Player = modPlayer,
                BeforeSnapshot = InventoryMoneyAdapter.CaptureFromPlayer(modPlayer, request.SourceProfileId, request.SourceCharacterId, request.WorldId),
                ItemsByName = itemsByName,
            };
        }

        private static void ApplySnapshotToPlayer(Mod.Player player, CoopInventoryMoneySnapshot snapshot, Dictionary<string, ModItem> itemsByName)
        {
            if (player == null || snapshot == null)
            {
                return;
            }

            if (player.BankAccounts != null)
            {
                player.BankAccounts.SetCash(snapshot.OnHandCash);
                player.BankAccounts.BankAccountList = snapshot.BankAccounts.Select(x => new BankAccount(x.BankContactName, x.AccountName, x.Money)
                {
                    IsPrimary = x.IsPrimary
                }).ToList();
            }

            if (player.Inventory != null)
            {
                player.Inventory.Clear();
                foreach (CoopInventoryItemState item in snapshot.InventoryItems)
                {
                    if (itemsByName != null && itemsByName.TryGetValue(item.ItemName, out ModItem modItem))
                    {
                        player.Inventory.Add(modItem, item.RemainingPercent);
                    }
                }
            }
        }

        private class PendingPurchaseState
        {
            public Mod.Player Player { get; set; }
            public CoopInventoryMoneySnapshot BeforeSnapshot { get; set; }
            public Dictionary<string, ModItem> ItemsByName { get; set; }
        }
    }
}
