namespace LosSantosRED.lsr.Coop.Core
{
    public interface ICoopPedProvider
    {
        bool HasPed { get; }
        int PedHandle { get; }
    }
}
