namespace LosSantosRED.lsr.Coop.Core
{
    public interface IActorContextProvider
    {
        bool HasActorContext { get; }
        LsrActorContext GetCurrentActorContext();
    }
}
