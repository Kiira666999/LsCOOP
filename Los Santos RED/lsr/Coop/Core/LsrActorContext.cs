using Rage;

namespace LosSantosRED.lsr.Coop.Core
{
    public class LsrActorContext
    {
        public LsrActorContext(
            CoopCharacterId characterId,
            CoopProfileId profileId,
            string displayName,
            Ped actorPed,
            Vehicle actorVehicle,
            Vector3 position,
            bool isLocal,
            bool isActiveHost,
            bool isAdmin,
            bool isTrustedHost,
            Mod.Player existingPlayer)
        {
            CharacterId = characterId;
            ProfileId = profileId;
            DisplayName = displayName;
            ActorPed = actorPed;
            ActorVehicle = actorVehicle;
            Position = position;
            IsLocal = isLocal;
            IsActiveHost = isActiveHost;
            IsAdmin = isAdmin;
            IsTrustedHost = isTrustedHost;
            ExistingPlayer = existingPlayer;
        }

        public CoopCharacterId CharacterId { get; private set; }
        public CoopProfileId ProfileId { get; private set; }
        public string DisplayName { get; private set; }
        public Ped ActorPed { get; private set; }
        public Vehicle ActorVehicle { get; private set; }
        public Vector3 Position { get; private set; }
        public bool IsLocal { get; private set; }
        public bool IsRemote => !IsLocal;
        public bool IsActiveHost { get; private set; }
        public bool IsAdmin { get; private set; }
        public bool IsTrustedHost { get; private set; }
        public Mod.Player ExistingPlayer { get; private set; }
    }
}
