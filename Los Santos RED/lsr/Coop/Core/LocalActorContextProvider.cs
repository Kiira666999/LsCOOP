namespace LosSantosRED.lsr.Coop.Core
{
    public class LocalActorContextProvider : IActorContextProvider
    {
        private readonly Mod.Player player;
        private readonly LsrActorContextFactory factory;

        public LocalActorContextProvider(Mod.Player player) : this(player, new LsrActorContextFactory())
        {
        }

        public LocalActorContextProvider(Mod.Player player, LsrActorContextFactory factory)
        {
            this.player = player;
            this.factory = factory;
        }

        public bool HasActorContext => player != null;

        public LsrActorContext GetCurrentActorContext()
        {
            return factory.CreateLocalSinglePlayer(player);
        }
    }
}
