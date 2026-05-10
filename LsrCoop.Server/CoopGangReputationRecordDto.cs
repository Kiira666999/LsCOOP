namespace LsrCoop.Server
{
    public class CoopGangReputationRecordDto
    {
        public string GangId { get; set; }
        public int Reputation { get; set; }
        public int MembersHurt { get; set; }
        public int MembersKilled { get; set; }
        public int MembersCarJacked { get; set; }
        public int MembersHurtInTerritory { get; set; }
        public int MembersKilledInTerritory { get; set; }
        public int MembersCarJackedInTerritory { get; set; }
        public int PlayerDebt { get; set; }
        public bool IsMember { get; set; }
        public bool IsEnemy { get; set; }
        public int TasksCompleted { get; set; }
    }
}
