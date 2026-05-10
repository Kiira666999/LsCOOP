using System.Linq;

namespace LosSantosRED.lsr.Coop.Core
{
    public class CoopPermissionService
    {
        public CoopPermissionService()
        {
            SessionState = new CoopSessionState { Mode = LsrCoopMode.Disabled };
            LocalCharacterExists = true;
        }

        public CoopSessionState SessionState { get; private set; }
        public LsrActorContext LocalActorContext { get; private set; }
        public bool LocalCharacterExists { get; private set; }

        public void SetSessionState(CoopSessionState sessionState)
        {
            SessionState = sessionState ?? new CoopSessionState { Mode = LsrCoopMode.Disabled };
        }

        public void SetLocalActorContext(LsrActorContext actorContext)
        {
            LocalActorContext = actorContext;
        }

        public void SetLocalCharacterExists(bool exists)
        {
            LocalCharacterExists = exists;
        }

        public bool CanUsePedSwap()
        {
            return IsCoopDisabled || IsLocalAdmin || IsCharacterCreationBootstrap || !LocalCharacterExists;
        }

        public bool CanChangeModelAfterCreation()
        {
            return IsCoopDisabled || IsLocalAdmin || IsCharacterCreationBootstrap;
        }

        public bool CanEditIdentityAfterCreation()
        {
            return IsCoopDisabled || IsLocalAdmin || IsCharacterCreationBootstrap;
        }
        
        public bool CanEditMoney()
        {
            return IsCoopDisabled || IsLocalAdmin;
        }
        public bool CanUseUndieOption()
        {
            return IsCoopDisabled || IsLocalAdmin;
        }
        public bool CanEditCriminalRecord()
        {
            return IsCoopDisabled || IsLocalAdmin;
        }

        public bool CanUseDebugMenu()
        {
            return IsCoopDisabled || IsLocalAdmin;
        }

        private bool IsCoopDisabled => !CoopStartupBridge.IsCoopEnabled || SessionState == null || !SessionState.IsEnabled;

        private bool IsCharacterCreationBootstrap =>
            CoopStartupBridge.StartupMode == CoopStartupMode.BootstrapOnly
            || CoopStartupBridge.IsCharacterCreationRequired
            || (SessionState?.Mode == LsrCoopMode.BootstrapOnly && !LocalCharacterExists);

        private bool IsLocalAdmin
        {
            get
            {
                if (CoopStartupBridge.IsLocalAdmin)
                {
                    return true;
                }

                if (LocalActorContext?.IsAdmin == true)
                {
                    return true;
                }

                CoopCharacterSnapshot localCharacter = SessionState?.Characters?.FirstOrDefault(x => x.CharacterId.Equals(SessionState.LocalCharacterId));
                return localCharacter != null && (localCharacter.Role == LsrCoopAuthorityRole.Admin || localCharacter.Permissions.HasFlag(CoopPermission.AdminActions));
            }
        }
    }
}
