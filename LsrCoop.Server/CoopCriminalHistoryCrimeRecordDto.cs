namespace LsrCoop.Server
{
    public class CoopCriminalHistoryCrimeRecordDto
    {
        public string CrimeId { get; set; }
        public string CrimeName { get; set; }
        public int Instances { get; set; }
        public int ResultingWantedLevel { get; set; }
        public int Priority { get; set; }
        public bool ResultsInLethalForce { get; set; }
    }
}
