namespace LsrCoop.Client
{
    public class CoopCharacterCreatedRequest
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public CoopCharacterSnapshot Character { get; set; }
    }
}
