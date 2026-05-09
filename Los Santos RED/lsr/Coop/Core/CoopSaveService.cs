namespace LosSantosRED.lsr.Coop.Core
{
    public interface CoopSaveService
    {
        bool TryLoadWorld(CoopWorldId worldId, out CoopServerWorldSave worldSave);
        void SaveWorld(CoopServerWorldSave worldSave);
        void Clear();
    }
}
