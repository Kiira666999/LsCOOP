using System.Collections.Generic;
using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopPendingActionTracker
    {
        private readonly Dictionary<string, CoopGameplayActionRequest> pendingRequests = new Dictionary<string, CoopGameplayActionRequest>();
        private readonly HashSet<string> optimisticRequestIds = new HashSet<string>();

        public IEnumerable<CoopGameplayActionRequest> PendingRequests => pendingRequests.Values.ToList();
        public int Count => pendingRequests.Count;

        public bool Track(CoopGameplayActionRequest request, bool hasOptimisticLocalState)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                return false;
            }

            pendingRequests[request.RequestId] = request;

            if (hasOptimisticLocalState || request.AllowsOptimisticClientFeedback)
            {
                optimisticRequestIds.Add(request.RequestId);
            }

            return true;
        }

        public bool TryGet(string requestId, out CoopGameplayActionRequest request)
        {
            request = null;
            return !string.IsNullOrWhiteSpace(requestId) && pendingRequests.TryGetValue(requestId, out request);
        }

        public bool IsPending(string requestId)
        {
            return !string.IsNullOrWhiteSpace(requestId) && pendingRequests.ContainsKey(requestId);
        }

        public bool HasOptimisticLocalState(string requestId)
        {
            return !string.IsNullOrWhiteSpace(requestId) && optimisticRequestIds.Contains(requestId);
        }

        public bool Resolve(CoopGameplayActionResult result, out CoopGameplayActionRequest request)
        {
            request = null;

            if (result == null || string.IsNullOrWhiteSpace(result.RequestId))
            {
                return false;
            }

            if (!pendingRequests.TryGetValue(result.RequestId, out request))
            {
                return false;
            }

            pendingRequests.Remove(result.RequestId);
            optimisticRequestIds.Remove(result.RequestId);
            return true;
        }

        public bool ShouldCommitAcceptedState(CoopGameplayActionResult result)
        {
            return result != null && result.Accepted && result.RequiresPersistentCommit;
        }

        public bool ShouldRevertOptimisticState(CoopGameplayActionResult result)
        {
            return result != null && !result.Accepted && HasOptimisticLocalState(result.RequestId);
        }

        public bool ShouldResyncAfterResult(CoopGameplayActionResult result)
        {
            return result != null && (!result.Accepted || result.RequiresResync);
        }

        public void Clear()
        {
            pendingRequests.Clear();
            optimisticRequestIds.Clear();
        }
    }
}
