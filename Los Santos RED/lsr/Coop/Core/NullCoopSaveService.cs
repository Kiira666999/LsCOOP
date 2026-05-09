namespace LosSantosRED.lsr.Coop.Core
{
    public class NullCoopSaveService : CoopSaveService
    {
        public bool TryLoadWorld(CoopWorldId worldId, out CoopServerWorldSave worldSave)
        {
            worldSave = null;
            return false;
        }

        public void SaveWorld(CoopServerWorldSave worldSave)
        {
        }

        public void Clear()
        {
        }
    }
}
