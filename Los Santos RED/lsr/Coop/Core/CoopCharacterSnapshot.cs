namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopCharacterSnapshot
    {
        public CoopCharacterId CharacterId { get; set; }
        public CoopProfileId ProfileId { get; set; }
        public string DisplayName { get; set; }
        public string ModelName { get; set; }
        public bool IsLocal { get; set; }
        public bool IsConnected { get; set; }
        public LsrCoopAuthorityRole Role { get; set; }
        public CoopPermission Permissions { get; set; }
        public CoopAppearanceState Appearance { get; set; }
    }
}
