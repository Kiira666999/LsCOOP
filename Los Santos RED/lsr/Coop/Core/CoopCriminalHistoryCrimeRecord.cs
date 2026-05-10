namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCriminalHistoryCrimeRecord
    {
        public string CrimeId { get; set; }
        public string CrimeName { get; set; }
        public int Instances { get; set; }
        public int ResultingWantedLevel { get; set; }
        public int Priority { get; set; }
        public bool ResultsInLethalForce { get; set; }
    }
}
