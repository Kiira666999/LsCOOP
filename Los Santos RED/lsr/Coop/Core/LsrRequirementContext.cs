using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class LsrRequirementContext : IActorRequirementContext
    {
        private readonly Ped fallbackPed;
        private readonly Vector3 fallbackPosition;
        private readonly Vehicle fallbackVehicle;

        public LsrRequirementContext(
            LsrActorContext actorContext,
            object legacyPlayer,
            Ped fallbackPed,
            Vehicle fallbackVehicle,
            Vector3 fallbackPosition,
            bool isCoopEnabled)
        {
            ActorContext = actorContext;
            LegacyPlayer = legacyPlayer;
            this.fallbackPed = fallbackPed;
            this.fallbackVehicle = fallbackVehicle;
            this.fallbackPosition = fallbackPosition;
            IsCoopEnabled = isCoopEnabled;
        }

        public LsrActorContext ActorContext { get; private set; }
        public object LegacyPlayer { get; private set; }
        public Ped ActorPed => ActorContext == null ? fallbackPed : ActorContext.ActorPed;
        public Vehicle ActorVehicle => ActorContext == null ? fallbackVehicle : ActorContext.ActorVehicle;
        public Vector3 Position => ActorContext == null ? fallbackPosition : ActorContext.Position;
        public bool IsLocal => ActorContext == null || ActorContext.IsLocal;
        public bool IsRemote => ActorContext != null && ActorContext.IsRemote;
        public bool IsCoopEnabled { get; private set; }
        public bool IsActiveHost => ActorContext != null && ActorContext.IsActiveHost;
        public bool IsAdmin => ActorContext != null && ActorContext.IsAdmin;
        public bool IsTrustedHost => ActorContext != null && ActorContext.IsTrustedHost;
    }
}
