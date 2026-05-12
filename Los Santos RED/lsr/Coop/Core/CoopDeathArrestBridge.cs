using LosSantosRED.lsr.Interface;
using Rage;
using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopDeathArrestBridge
    {
        private static readonly CoopActionAuthorityService AuthorityService = new CoopActionAuthorityService();
        private static readonly CoopInventoryMoneyAdapter InventoryMoneyAdapter = new CoopInventoryMoneyAdapter();
        private static readonly CoopWeaponInventoryAdapter WeaponInventoryAdapter = new CoopWeaponInventoryAdapter();
        public static void CompleteDeathState(IRespawnable player, ILocationRespawnable respawnLocation, string outcomeType, int hospitalFee, int hospitalBillPastDue, int hospitalDuration, DateTime releaseDate, int timesDied)
        {
            CompleteOutcome(CoopGameplayActionType.ApplyDeathState, player, respawnLocation, outcomeType, hospitalFee, hospitalBillPastDue, hospitalDuration, 0, 0, 0, 0, false, false, releaseDate, timesDied);
        }

        public static void CompleteArrestState(IRespawnable player, ILocationRespawnable respawnLocation, string outcomeType, int bailFee, int bailFeePastDue, int bailDuration, int todayPayment, bool hadIllegalWeapons, bool hadIllegalItems, DateTime releaseDate)
        {
            CompleteOutcome(CoopGameplayActionType.ApplyArrestState, player, respawnLocation, outcomeType, 0, 0, 0, bailFee, bailFeePastDue, bailDuration, todayPayment, hadIllegalWeapons, hadIllegalItems, releaseDate, 0);
        }

        private static void CompleteOutcome(CoopGameplayActionType actionType, IRespawnable player, ILocationRespawnable respawnLocation, string outcomeType, int hospitalFee, int hospitalBillPastDue, int hospitalDuration, int bailFee, int bailFeePastDue, int bailDuration, int todayPayment, bool hadIllegalWeapons, bool hadIllegalItems, DateTime releaseDate, int timesDied)
        {
            if (!CoopStartupBridge.IsCoopEnabled || !CoopStartupBridge.IsLocalActiveHost)
            {
                return;
            }

            string profileId = string.IsNullOrWhiteSpace(CoopStartupBridge.LocalProfileId) ? "single-player-profile" : CoopStartupBridge.LocalProfileId;
            CoopWorldId worldId = new CoopWorldId(CoopStartupBridge.WorldId);
            CoopProfileId sourceProfileId = new CoopProfileId(profileId);
            CoopCharacterId sourceCharacterId = new CoopCharacterId(profileId);
            CoopGameplayActionRequest request = new CoopGameplayActionRequest
            {
                ActionType = actionType,
                WorldId = worldId,
                SourceProfileId = sourceProfileId,
                SourceCharacterId = sourceCharacterId,
                TargetProfileId = sourceProfileId,
                TargetCharacterId = sourceCharacterId,
                AllowsOptimisticClientFeedback = AuthorityService.CanUseOptimisticClientFeedback(actionType),
            };

            request.Parameters["OutcomeType"] = outcomeType ?? string.Empty;
            request.Parameters["RespawnLocationName"] = respawnLocation?.Name ?? string.Empty;
            request.Parameters["ActorName"] = player?.PlayerName ?? string.Empty;

            Mod.Player modPlayer = player as Mod.Player;
            CoopGameplayActionResult result = AuthorityService.CreateAcceptedResult(request, "Accepted by active host");
            CoopGameplayFileBridge.PublishGameplayCommit(new CoopStorePurchaseCommit
            {
                Request = request,
                Result = result,
                InventoryMoneySnapshot = InventoryMoneyAdapter.CaptureFromPlayer(modPlayer, sourceProfileId, sourceCharacterId, worldId),
                WeaponSnapshot = WeaponInventoryAdapter.CaptureFromPlayer(modPlayer, sourceProfileId, sourceCharacterId, worldId),
                DeathArrestState = CaptureState(actionType, player, respawnLocation, outcomeType, worldId, sourceProfileId, sourceCharacterId, hospitalFee, hospitalBillPastDue, hospitalDuration, bailFee, bailFeePastDue, bailDuration, todayPayment, hadIllegalWeapons, hadIllegalItems, releaseDate, timesDied),
            });
        }

        private static CoopDeathArrestState CaptureState(CoopGameplayActionType actionType, IRespawnable player, ILocationRespawnable respawnLocation, string outcomeType, CoopWorldId worldId, CoopProfileId profileId, CoopCharacterId characterId, int hospitalFee, int hospitalBillPastDue, int hospitalDuration, int bailFee, int bailFeePastDue, int bailDuration, int todayPayment, bool hadIllegalWeapons, bool hadIllegalItems, DateTime releaseDate, int timesDied)
        {
            Vector3 position = player?.Character?.Exists() == true ? player.Character.Position : Vector3.Zero;
            float heading = player?.Character?.Exists() == true ? player.Character.Heading : 0.0f;
            return new CoopDeathArrestState
            {
                WorldId = worldId,
                ProfileId = profileId,
                CharacterId = characterId,
                ActionType = actionType,
                OutcomeType = outcomeType ?? string.Empty,
                RespawnLocationName = respawnLocation?.Name ?? string.Empty,
                PositionX = position.X,
                PositionY = position.Y,
                PositionZ = position.Z,
                Heading = heading,
                HospitalFee = hospitalFee,
                HospitalBillPastDue = hospitalBillPastDue,
                HospitalDuration = hospitalDuration,
                BailFee = bailFee,
                BailFeePastDue = bailFeePastDue,
                BailDuration = bailDuration,
                TodayPayment = todayPayment,
                TimesDied = timesDied,
                HadIllegalWeapons = hadIllegalWeapons,
                HadIllegalItems = hadIllegalItems,
                ReleaseDate = releaseDate,
            };
        }
    }
}
