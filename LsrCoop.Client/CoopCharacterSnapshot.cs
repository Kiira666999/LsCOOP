namespace LsrCoop.Client
{
    public class CoopCharacterSnapshot
    {
        public string CharacterId { get; set; }
        public string ProfileId { get; set; }
        public string DisplayName { get; set; }
        public string ModelName { get; set; }
        public CoopAppearanceState Appearance { get; set; }
    }
}
