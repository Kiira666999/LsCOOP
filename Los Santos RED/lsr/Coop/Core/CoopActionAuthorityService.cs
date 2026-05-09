namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopActionAuthorityService
    {
        public bool CanUseOptimisticClientFeedback(CoopGameplayActionType actionType)
        {
            switch (actionType)
            {
                case CoopGameplayActionType.PurchaseItem:
                case CoopGameplayActionType.PurchaseVehicle:
                case CoopGameplayActionType.PurchaseProperty:
                case CoopGameplayActionType.SaveOwnedVehicle:
                case CoopGameplayActionType.SaveCharacter:
                case CoopGameplayActionType.SaveAppearance:
                    return true;
                default:
                    return false;
            }
        }

        public bool RequiresActiveHostValidation(CoopGameplayActionType actionType)
        {
            switch (actionType)
            {
                case CoopGameplayActionType.PurchaseItem:
                case CoopGameplayActionType.PurchaseVehicle:
                case CoopGameplayActionType.PurchaseProperty:
                case CoopGameplayActionType.SaveOwnedVehicle:
                case CoopGameplayActionType.CommitCrime:
                case CoopGameplayActionType.ApplyDeathState:
                case CoopGameplayActionType.ApplyArrestState:
                    return true;
                default:
                    return false;
            }
        }

        public bool RequiresServerCommit(CoopGameplayActionType actionType)
        {
            switch (actionType)
            {
                case CoopGameplayActionType.PurchaseItem:
                case CoopGameplayActionType.PurchaseVehicle:
                case CoopGameplayActionType.PurchaseProperty:
                case CoopGameplayActionType.SaveOwnedVehicle:
                case CoopGameplayActionType.SaveCharacter:
                case CoopGameplayActionType.SaveAppearance:
                case CoopGameplayActionType.CommitCrime:
                case CoopGameplayActionType.ApplyDeathState:
                case CoopGameplayActionType.ApplyArrestState:
                    return true;
                default:
                    return false;
            }
        }

        public bool IsValidRequest(CoopGameplayActionRequest request)
        {
            return request != null
                && !string.IsNullOrWhiteSpace(request.RequestId)
                && request.ActionType != CoopGameplayActionType.Unknown
                && !request.WorldId.IsEmpty
                && !request.SourceProfileId.IsEmpty
                && !request.SourceCharacterId.IsEmpty;
        }

        public bool DoesResultMatchRequest(CoopGameplayActionRequest request, CoopGameplayActionResult result)
        {
            return IsValidRequest(request)
                && result != null
                && string.Equals(request.RequestId, result.RequestId, System.StringComparison.Ordinal)
                && request.ActionType == result.ActionType
                && request.WorldId.Equals(result.WorldId)
                && request.SourceProfileId.Equals(result.SourceProfileId)
                && request.SourceCharacterId.Equals(result.SourceCharacterId);
        }

        public CoopGameplayActionResult CreateAcceptedResult(CoopGameplayActionRequest request, string reason = "")
        {
            return CreateResult(request, true, reason, RequiresServerCommit(request.ActionType), false);
        }

        public CoopGameplayActionResult CreateRejectedResult(CoopGameplayActionRequest request, string reason)
        {
            return CreateResult(request, false, reason, false, true);
        }

        private CoopGameplayActionResult CreateResult(CoopGameplayActionRequest request, bool accepted, string reason, bool requiresPersistentCommit, bool requiresResync)
        {
            if (request == null)
            {
                return null;
            }

            return new CoopGameplayActionResult
            {
                RequestId = request.RequestId,
                ActionType = request.ActionType,
                WorldId = request.WorldId,
                SourceProfileId = request.SourceProfileId,
                SourceCharacterId = request.SourceCharacterId,
                TargetProfileId = request.TargetProfileId,
                TargetCharacterId = request.TargetCharacterId,
                Accepted = accepted,
                RequiresPersistentCommit = requiresPersistentCommit,
                RequiresResync = requiresResync,
                Reason = reason ?? string.Empty,
            };
        }
    }
}
