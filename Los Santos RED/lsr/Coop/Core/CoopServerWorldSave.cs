using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopServerWorldSave
    {
        public CoopServerWorldSave()
        {
            SaveVersion = "1";
            CreatedUtc = DateTime.UtcNow;
            UpdatedUtc = CreatedUtc;
            RoleConfig = new CoopRoleConfig();
            WorldState = new CoopPersistentWorldState();
        }

        public CoopWorldId WorldId { get; set; }
        public string SaveVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public CoopRoleConfig RoleConfig { get; set; }
        public CoopPersistentWorldState WorldState { get; set; }
    }
}
