using System;
using System.Collections.Generic;

namespace LsrCoop.Server
{
    public class CoopGangReputationStateDto
    {
        public CoopGangReputationStateDto()
        {
            Reputations = new List<CoopGangReputationRecordDto>();
        }

        public string StateId { get; set; }
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public string CurrentGangId { get; set; }
        public List<CoopGangReputationRecordDto> Reputations { get; private set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
