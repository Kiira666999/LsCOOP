using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class LocalCoopPedProvider : ICoopPedProvider
    {
        private readonly Mod.Player player;

        public LocalCoopPedProvider(Mod.Player player)
        {
            this.player = player;
        }

        public Ped Ped => player == null ? null : player.Character;

        public bool HasPed => Ped != null && Ped.Exists();

        public int PedHandle => HasPed ? (int)Ped.Handle.Value : 0;
    }
}
