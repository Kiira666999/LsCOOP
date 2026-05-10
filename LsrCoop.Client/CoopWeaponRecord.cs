namespace LsrCoop.Client
{
    public class CoopWeaponRecord
    {
        public string WeaponHash { get; set; }
        public string WeaponName { get; set; }
        public string Category { get; set; }
        public int Ammo { get; set; }
        public bool IsLegal { get; set; }
        public bool IsEquipped { get; set; }
    }
}
